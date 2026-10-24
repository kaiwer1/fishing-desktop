using System;
using System.Collections.Generic;
using System.Threading;

sealed class OwnerSnapshot
{
    public string OwnerId { get; set; }
    public long Sequence { get; set; }
    public long Value { get; set; }
    public bool Faulted { get; set; }
}
sealed class OwnerReply
{
    public bool Ok { get; set; }
    public string Error { get; set; }
    public CounterReceipt Receipt { get; set; }
    public OwnerSnapshot Snapshot { get; set; }
}

// One owner thread serializes every diagnostic mutation. Views only hold snapshots.
sealed class ViewAuthority
{
    sealed class ViewSession
    {
        public string DocumentId;
        public bool CanControl;
    }
    readonly DurableCounterStore store;
    readonly int threadId = Thread.CurrentThread.ManagedThreadId;
    readonly Dictionary<string,ViewSession> views = new Dictionary<string,ViewSession>();
    readonly string ownerId = Guid.NewGuid().ToString("D");
    string activeView;
    string activeLease;
    bool faulted;

    public ViewAuthority(DurableCounterStore store) { this.store=store; }
    void CheckThread()
    {
        if(Thread.CurrentThread.ManagedThreadId!=threadId)throw new InvalidOperationException("Authority requires its owner thread.");
    }
    public OwnerSnapshot Snapshot()
    {
        CheckThread();
        return new OwnerSnapshot { OwnerId=ownerId, Sequence=store.Sequence, Value=store.Value, Faulted=faulted };
    }
    public void Attach(string viewId,string documentId,bool canControl)
    {
        CheckThread();
        Guid parsed;
        if(String.IsNullOrWhiteSpace(viewId) || !Guid.TryParseExact(documentId,"D",out parsed))throw new ArgumentException("Invalid view identity.");
        Detach(viewId);
        views[viewId]=new ViewSession { DocumentId=documentId, CanControl=canControl };
    }
    public void Detach(string viewId)
    {
        CheckThread();
        Revoke(viewId);
        views.Remove(viewId);
    }
    public string Activate(string viewId)
    {
        CheckThread();
        ViewSession view;
        if(faulted || !views.TryGetValue(viewId,out view) || !view.CanControl)return null;
        // Every explicit activation invalidates previous leases, including this view's old lease.
        activeView=viewId; activeLease=Guid.NewGuid().ToString("D");
        return activeLease;
    }
    public void Revoke(string viewId)
    {
        CheckThread();
        if(viewId==activeView) { activeView=null; activeLease=null; }
    }
    public bool Revoke(string viewId,string expectedLease)
    {
        CheckThread();
        // Page-originated cancellations target the lease that page actually saw.
        // A delayed blur from an older activation cannot cancel a later activation.
        if(String.IsNullOrEmpty(expectedLease) || viewId!=activeView || expectedLease!=activeLease)return false;
        activeView=null; activeLease=null;
        return true;
    }
    public OwnerReply Execute(string viewId,string documentId,string lease,string requestId,int delta,long expectedSequence)
    {
        CheckThread();
        ViewSession view;
        if(faulted)return Reply(false,"owner-faulted",null);
        if(!views.TryGetValue(viewId,out view) || view.DocumentId!=documentId)return Reply(false,"stale-document",null);
        if(!view.CanControl)return Reply(false,"read-only-view",null);
        if(activeView!=viewId || activeLease==null || activeLease!=lease)return Reply(false,"inactive-lease",null);
        Guid parsed;
        if(!Guid.TryParseExact(requestId,"D",out parsed) || parsed.ToString("D")!=requestId || delta<1 || delta>1000 || expectedSequence<0)return Reply(false,"invalid-command",null);
        try {
            CounterReceipt previous;
            if(store.TryGetReceipt(requestId,out previous)) {
                if(previous.Delta!=delta)return Reply(false,"request-conflict",null);
                return Reply(true,null,previous);
            }
            if(expectedSequence!=store.Sequence)return Reply(false,"stale-state",null);
            return Reply(true,null,store.Commit(requestId,delta));
        }
        catch(JournalCapacityExceededException) {
            // Capacity was checked before writing. Keep the current lease and
            // receipt lookups available so already committed requests can retry.
            return Reply(false,"journal-full",null);
        }
        catch(Exception) {
            // A failed write may have reached disk. Stop accepting commands until explicit restart/replay.
            faulted=true; activeView=null; activeLease=null;
            return Reply(false,"persistence-uncertain",null);
        }
    }
    OwnerReply Reply(bool ok,string error,CounterReceipt receipt)
    {
        return new OwnerReply { Ok=ok, Error=error, Receipt=receipt, Snapshot=Snapshot() };
    }
}
