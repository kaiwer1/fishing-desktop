using System;
using System.Collections.Generic;
using FishingDesktop.Diagnostics.HostPolicy;

// The application controller owns this object; no Form owns the journal lifetime.
sealed class DualWindowOwner : IDisposable
{
    internal sealed class Ticket
    {
        internal readonly string Role;
        internal readonly string ViewId = Guid.NewGuid().ToString("D");
        internal readonly bool CanControl;
        internal string DocumentId;
        internal int Epoch;
        internal int Revision;
        internal Ticket(string role, bool canControl) { Role=role; CanControl=canControl; }
    }
    readonly DurableCounterStore store;
    readonly ViewAuthority authority;
    readonly RunGate runGate=new RunGate();
    readonly Dictionary<string,Ticket> views = new Dictionary<string,Ticket>();
    bool disposed;
    public DualWindowOwner(string journal) { store=new DurableCounterStore(journal); authority=new ViewAuthority(store);runGate.Set(PauseReason.Startup,false); }
    void Check() { if(disposed)throw new ObjectDisposedException("DualWindowOwner"); authority.Snapshot(); }
    public OwnerSnapshot Snapshot() { Check(); return authority.Snapshot(); }
    public Ticket Open(bool canControl)
    {
        Check();
        string role=canControl?"interaction":"observer";
        if(views.ContainsKey(role))throw new InvalidOperationException("Close the existing role before creating another view.");
        var ticket=new Ticket(role,canControl); views.Add(role,ticket); return ticket;
    }
    public bool IsCurrent(Ticket ticket)
    {
        Check(); Ticket current;
        return ticket!=null && views.TryGetValue(ticket.Role,out current) && Object.ReferenceEquals(ticket,current);
    }
    public void Navigating(Ticket ticket)
    {
        if(!IsCurrent(ticket))return;
        authority.Detach(ticket.ViewId); ticket.DocumentId=null; ticket.Epoch=0; ticket.Revision=0;
    }
    public bool Attach(Ticket ticket,string document)
    {
        if(!IsCurrent(ticket))return false;
        authority.Attach(ticket.ViewId,document,ticket.CanControl); ticket.DocumentId=document; return true;
    }
    public void Close(Ticket ticket)
    {
        if(!IsCurrent(ticket))return;
        authority.Detach(ticket.ViewId); ticket.DocumentId=null; views.Remove(ticket.Role);
    }
    public string Grant(Ticket ticket)
    {
        return IsCurrent(ticket) && ticket.DocumentId!=null && runGate.Snapshot().CanRun ? authority.Activate(ticket.ViewId) : null;
    }
    public RunState ActivityState(){Check();return runGate.Snapshot();}
    public RunTransition SetPause(PauseReason reason,bool blocked){Check();return Apply(runGate.Set(reason,blocked));}
    public RunTransition ReplacePause(PauseReason mask,PauseReason blocked){Check();return Apply(runGate.Replace(mask,blocked));}
    RunTransition Apply(RunTransition transition)
    {
        if(transition.RevokeInput)foreach(var ticket in views.Values)authority.Revoke(ticket.ViewId);
        return transition;
    }
    public void Revoke(Ticket ticket) { if(IsCurrent(ticket))authority.Revoke(ticket.ViewId); }
    public bool Cancel(Ticket ticket,string document,string lease,int epoch)
    {
        if(!IsCurrent(ticket) || ticket.DocumentId!=document || epoch<ticket.Epoch || epoch<0)return false;
        ticket.Epoch=epoch;
        return authority.Revoke(ticket.ViewId,lease);
    }
    public int NextRevision(Ticket ticket)
    {
        if(!IsCurrent(ticket))throw new InvalidOperationException("Stale view.");
        return ticket.Revision=checked(ticket.Revision+1);
    }
    public OwnerReply Execute(Ticket ticket,string document,string lease,string request,int delta,long expected)
    {
        Check();
        if(!runGate.Snapshot().CanRun)return new OwnerReply{Ok=false,Error="host-paused",Snapshot=authority.Snapshot()};
        // Detached tickets retain their unique ViewId, so even a late callback cannot
        // address a replacement window's authority.
        return authority.Execute(ticket.ViewId,document,lease,request,delta,expected);
    }
    public void Dispose()
    {
        if(disposed)return;
        Check();
        foreach(var ticket in views.Values)authority.Detach(ticket.ViewId);
        views.Clear(); disposed=true; store.Dispose();
    }
}
