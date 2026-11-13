using System;
using System.IO;
using System.Threading;

namespace FishingDesktop.GameCore
{
    internal sealed class FloatCampWorkSnapshot
    {
        internal readonly string Phase,Reason;
        internal readonly int ElapsedMilliseconds,RequiredMilliseconds;
        internal readonly long RewardMinor;
        internal FloatCampWorkSnapshot(string phase,int elapsed,string reason)
        {Phase=phase;ElapsedMilliseconds=elapsed;RequiredMilliseconds=FloatCampWorkRules.DurationMilliseconds;RewardMinor=FloatCampWorkRules.RewardMinor;Reason=reason;}
    }

    // Host-thread active-time accumulator. It never reads a wall clock or
    // restores unfinished time from disk. Losing the bound input authority,
    // leaving the menu or changing the frozen state cancels the whole round.
    internal sealed class FloatCampWork
    {
        // Current type20 frame: 112-byte command envelope, int duration,
        // long policy version, and the journal's 76-byte frame wrapper.
        internal const int RequiredClaimFrameBytes=200;
        readonly FloatGameOwner owner;
        readonly int thread=Thread.CurrentThread.ManagedThreadId;
        FloatViewTicket view;
        FloatDocumentTicket document;
        FloatControlLease lease;
        FloatGameState frozen;
        HostFloatCampReward command;
        string phase="idle",reason;
        int elapsed;
        bool busy;

        internal FloatCampWork(FloatGameOwner owner)
        {
            if(owner==null)throw new ArgumentNullException("owner");
            // Construction, like every later call, belongs to the owner thread.
            var initial=owner.Snapshot;this.owner=owner;
        }
        void ThreadCheck(){if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("Use the camp work owner's host thread.");}
        T Run<T>(Func<T> operation){ThreadCheck();if(busy)throw new InvalidOperationException("Reentrant camp work is not allowed.");busy=true;try{return operation();}finally{busy=false;}}
        bool Active{get{return phase=="working"||phase=="ready"||phase=="retry";}}
        internal FloatCampWorkSnapshot Snapshot{get{ThreadCheck();return new FloatCampWorkSnapshot(phase,elapsed,reason);}}
        static FloatCommitResult Refused(string code){return new FloatCommitResult(code,null,null,false,false,false);}
        bool SameBinding(FloatViewTicket candidateView,FloatDocumentTicket candidateDocument,FloatControlLease candidateLease)
        {return Object.ReferenceEquals(view,candidateView)&&Object.ReferenceEquals(document,candidateDocument)&&Object.ReferenceEquals(lease,candidateLease);}
        string Eligibility(FloatViewTicket candidateView,FloatDocumentTicket candidateDocument,FloatControlLease candidateLease,FloatGameState expected)
        {
            FloatOwnerSnapshot snapshot;
            try{
                snapshot=owner.Snapshot;
                if(snapshot.Faulted)return "camp-work-save-unknown";
                if(!owner.CanControl(candidateView,candidateDocument,candidateLease))return "control-required";
            }catch(ObjectDisposedException){return "owner-unavailable";}
            if(snapshot.HostPaused||snapshot.ShutdownConfirmed)return "control-required";
            if(!snapshot.MenuOpen)return "menu-required";
            if(expected!=null&&(snapshot.State==null||snapshot.State.Sequence!=expected.Sequence||snapshot.State.ProfileId!=expected.ProfileId||
                snapshot.State.BranchId!=expected.BranchId||snapshot.State.Rig.Revision!=expected.Rig.Revision||snapshot.State.RulesVersion!=expected.RulesVersion))return "camp-work-state-changed";
            string policy=FloatCampWorkRules.Check(snapshot.State);if(policy!=null)return policy;
            var budget=owner.SaveBudget;
            if(budget.RemainingTransactions<1||budget.RemainingBytes<RequiredClaimFrameBytes)return "save-capacity";
            return null;
        }
        string Invalidate(string code)
        {
            reason=code;
            if(code=="camp-work-save-unknown"){phase="unknown";return code;}
            phase="cancelled";elapsed=0;view=null;document=null;lease=null;frozen=null;command=null;return code;
        }
        internal string Start(FloatViewTicket candidateView,FloatDocumentTicket candidateDocument,FloatControlLease candidateLease)
        {
            return Run(()=>{
                if(phase=="unknown")return "camp-work-save-unknown";
                string code=Eligibility(candidateView,candidateDocument,candidateLease,null);if(code!=null)return code;
                if(Active)return "camp-work-busy";
                view=candidateView;document=candidateDocument;lease=candidateLease;frozen=owner.Snapshot.State;
                command=null;elapsed=0;reason=null;phase="working";return "camp-work-started";
            });
        }
        internal string Advance(int activeDeltaMilliseconds)
        {
            return Run(()=>{
                if(activeDeltaMilliseconds<0||activeDeltaMilliseconds>250)throw new ArgumentOutOfRangeException("activeDeltaMilliseconds");
                if(phase=="unknown")return "camp-work-save-unknown";
                if(!Active)return "camp-work-not-active";
                string code=Eligibility(view,document,lease,frozen);if(code!=null)return Invalidate(code);
                if(phase=="working"){
                    elapsed=(int)Math.Min((long)FloatCampWorkRules.DurationMilliseconds,(long)elapsed+activeDeltaMilliseconds);
                    if(elapsed==FloatCampWorkRules.DurationMilliseconds){phase="ready";reason=null;}
                }
                return phase=="working"?"camp-work-progress":phase=="ready"?"camp-work-ready":"camp-work-retry-required";
            });
        }
        internal FloatCommitResult Claim(FloatViewTicket candidateView,FloatDocumentTicket candidateDocument,FloatControlLease candidateLease)
        {
            return Run(()=>{
                if(phase=="unknown")return Refused("camp-work-save-unknown");
                // An unrelated caller cannot cancel someone else's valid round.
                if(!SameBinding(candidateView,candidateDocument,candidateLease))return Refused("control-required");
                if(!Active)return Refused("camp-work-not-ready");
                string code=Eligibility(view,document,lease,frozen);if(code!=null){Invalidate(code);return Refused(code);}
                if(phase!="ready"&&phase!="retry")return Refused("camp-work-not-ready");
                if(command==null)command=new HostFloatCampReward(frozen.ProfileId,frozen.BranchId,Guid.NewGuid(),Guid.NewGuid(),
                    elapsed,FloatCampWorkRules.PolicyVersion,frozen.Sequence,frozen.Rig.Revision,frozen.RulesVersion);
                try{
                    var result=owner.ExecuteHost(command);
                    if(result.Committed){phase="confirmed";reason=null;}
                    else Invalidate(result.Code);
                    return result;
                }catch(FloatSaveUncertainException){Invalidate("camp-work-save-unknown");return Refused("camp-work-save-unknown");}
                catch(IOException){phase="retry";reason="camp-work-not-written";return Refused(reason);}
            });
        }
        internal string Cancel()
        {
            return Run(()=>{
                if(phase=="unknown")return "camp-work-save-unknown";
                if(!Active)return "camp-work-not-active";
                return Invalidate("camp-work-cancelled");
            });
        }
    }
}
