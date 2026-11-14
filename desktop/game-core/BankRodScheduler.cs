using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

namespace FishingDesktop.GameCore
{
    internal enum BankTipKind { Touch, Bite }

    internal sealed class BankTipEvent
    {
        // Slot zero denotes the hand. The slot at the instant of the signal
        // is retained even if the rod moves to a different post later.
        internal readonly int Slot;
        internal readonly long Step,SignalSequence;
        internal readonly Guid RigId,SessionId;
        internal readonly BankTipKind Kind;
        internal BankTipEvent(int slot,long step,BottomRigSnapshot source,BankTipKind kind)
        {Slot=slot;Step=step;SignalSequence=source.SignalSequence;RigId=source.RigId;SessionId=source.SessionId;Kind=kind;}
    }

    internal sealed class BankRodClockResult
    {
        internal readonly string Code;
        internal readonly ReadOnlyCollection<BankTipEvent> Signals;
        // Only new failure submissions, for the host's ordinary save path.
        internal readonly ReadOnlyCollection<FloatFightResult> Failures;
        internal BankRodClockResult(string code,List<BankTipEvent> signals,List<FloatFightResult> failures)
        {Code=code;Signals=signals.AsReadOnly();Failures=failures.AsReadOnly();}
    }

    internal sealed class BankRodSummary
    {
        internal readonly int Slot,TipOffsetMm;
        internal readonly Guid RigId,SessionId;
        internal readonly long SignalSequence,BottomStep;
        internal readonly BankRodPhase Phase;
        internal readonly bool NeedsRetrieve;
        internal BankRodSummary(int slot,BottomRigSnapshot snapshot,BankRodPhase phase)
        {
            Slot=slot;RigId=snapshot.RigId;SessionId=snapshot.SessionId;SignalSequence=snapshot.SignalSequence;
            TipOffsetMm=snapshot.TipOffsetMm;BottomStep=snapshot.StepIndex;Phase=phase;
            NeedsRetrieve=snapshot.Phase==BottomPhase.RetrieveRequired;
        }
    }

    // One scheduler is the exclusive clock owner for these runtimes. The
    // stand remains the sole owner of physical hand/slot positions. The host
    // supplies an already confirmed water contact and never separately ticks
    // an admitted bottom runtime or the fight returned by StrikeHeld.
    internal sealed class BankRodScheduler
    {
        sealed class Rod
        {
            internal readonly BottomFightHandoff Bottom;
            internal int NextActiveMilliseconds;
            internal FloatFightSimulation Fight;
            internal Rod(BottomFightHandoff bottom,int firstMilliseconds){Bottom=bottom;NextActiveMilliseconds=firstMilliseconds;}
            internal BankRodPhase Phase{
                get{
                    if(Fight!=null){
                        var state=Fight.Snapshot.Phase;
                        return state==FloatFightPhase.Fighting||state==FloatFightPhase.LandingEligible?
                            BankRodPhase.Fighting:BankRodPhase.PendingResult;
                    }
                    switch(Bottom.Snapshot.Phase){
                        case BottomPhase.Sinking:case BottomPhase.Stabilizing:return BankRodPhase.Settling;
                        case BottomPhase.Nibbling:case BottomPhase.Strikeable:return BankRodPhase.Biting;
                        default:return BankRodPhase.Waiting;
                    }
                }
            }
        }

        readonly int ownerThread=Thread.CurrentThread.ManagedThreadId;
        readonly BankRodStand stand;
        readonly Dictionary<Guid,Rod> rods=new Dictionary<Guid,Rod>();
        readonly HashSet<Guid> usedSessions=new HashSet<Guid>();
        readonly HashSet<Guid> knownRigs=new HashSet<Guid>();
        long frame,step;
        int remainder;
        bool paused;
        long commandEpoch,commandGeneration,lastCommand;
        internal long StepIndex{get{Check();return step;}}
        internal int RemainderMilliseconds{get{Check();return remainder;}}

        internal BankRodScheduler(BankRodStand stand)
        {if(stand==null)throw new ArgumentNullException("stand");this.stand=stand;}
        void Check()
        {if(Thread.CurrentThread.ManagedThreadId!=ownerThread)throw new InvalidOperationException("Use the stand owner's host thread.");}

        // Pass this projection to the stand for active rods. Its owner supplies
        // durable rig facts for idle/storage rods that have no runtime yet.
        internal BankRodState ReadRig(Guid rigId)
        {
            Check();Rod rod;if(!rods.TryGetValue(rigId,out rod))return null;
            var s=rod.Bottom.Snapshot;return new BankRodState(s.RigId,s.RigRevision,s.SessionId,rod.Phase,true);
        }
        internal string AttachHeld(BottomFightHandoff owner)
        {
            Check();if(owner==null)return "bottom-owner-required";
            var s=owner.Snapshot;
            if(s.RigId==s.SessionId||s.RigId==stand.StandId||s.SessionId==stand.StandId||
                usedSessions.Contains(s.RigId)||knownRigs.Contains(s.SessionId))return "rod-identity-conflict";
            if(usedSessions.Contains(s.SessionId)||rods.ContainsKey(s.RigId))return "rod-already-attached";
            if(rods.Count==3)return "rod-capacity";
            if(stand.Held!=s.RigId||stand.Pending!=null)return "not-held";
            if(s.Phase!=BottomPhase.Sinking||s.StepIndex!=0||s.RemainderMilliseconds!=0)return "bottom-clock-already-started";
            // A late join receives only its part of the first shared interval.
            // Its solver retains these active milliseconds until a full step.
            rods.Add(s.RigId,new Rod(owner,BottomRigSimulation.StepMilliseconds-remainder));
            usedSessions.Add(s.SessionId);knownRigs.Add(s.RigId);return null;
        }

        Guid Position(int index){return index==3?stand.Held:stand.At(index+1);}
        int Slot(int index){return index==3?0:index+1;}
        internal ReadOnlyCollection<BankRodSummary> Summary()
        {
            Check();var result=new List<BankRodSummary>(3);
            for(int i=0;i<4;i++){
                Rod rod;if(rods.TryGetValue(Position(i),out rod))result.Add(new BankRodSummary(Slot(i),rod.Bottom.Snapshot,rod.Phase));
            }
            return result.AsReadOnly();
        }
        internal BankRodClockResult Advance(long frameSequence,int activeMilliseconds,bool isPaused=false)
        {
            Check();var signals=new List<BankTipEvent>();var failures=new List<FloatFightResult>();
            if(activeMilliseconds<0||activeMilliseconds>250)throw new ArgumentOutOfRangeException("activeMilliseconds");
            if(frameSequence<=frame)return new BankRodClockResult("stale-clock-frame",signals,failures);
            if(frameSequence-1!=frame)return new BankRodClockResult("clock-frame-gap",signals,failures);
            int present=0;
            for(int i=0;i<4;i++)if(rods.ContainsKey(Position(i)))present++;
            if(present!=rods.Count)return new BankRodClockResult("rod-location-missing",signals,failures);
            frame=frameSequence;paused=isPaused;
            foreach(var entry in rods){
                var f=entry.Value.Fight;
                if(f!=null&&f.Snapshot.Reeling&&(isPaused||!stand.CanOperateHeld(entry.Key,f.Snapshot.SessionId,commandEpoch,commandGeneration)))
                    f.ClearInput(f.Snapshot.SessionId);
            }
            if(isPaused)return new BankRodClockResult("clock-paused",signals,failures);
            remainder+=activeMilliseconds;
            while(remainder>=BottomRigSimulation.StepMilliseconds){
                remainder-=BottomRigSimulation.StepMilliseconds;step=checked(step+1);
                for(int i=0;i<4;i++){
                    Rod rod;
                    if(!rods.TryGetValue(Position(i),out rod))continue;
                    int elapsed=rod.NextActiveMilliseconds;
                    rod.NextActiveMilliseconds=BottomRigSimulation.StepMilliseconds;
                    var before=rod.Bottom.Snapshot;
                    if(rod.Fight!=null){
                        var next=rod.Fight.Advance(before.SessionId,elapsed);
                        if(next.Accepted&&next.ShouldCommitFailure)failures.Add(next);
                    }else if(before.Phase!=BottomPhase.RetrieveRequired&&before.Phase!=BottomPhase.Ended){
                        var after=rod.Bottom.Advance(before.SessionId,elapsed).Snapshot;
                        if(after.SignalSequence!=before.SignalSequence)signals.Add(new BankTipEvent(Slot(i),step,after,
                            after.Phase==BottomPhase.Nibbling?BankTipKind.Touch:BankTipKind.Bite));
                    }
                }
            }
            return new BankRodClockResult("clock-advanced",signals,failures);
        }

        internal string SetEnvironment(Guid rig,Guid session,FloatBiteEnvironment environment)
        {
            Check();Rod rod;if(!rods.TryGetValue(rig,out rod)||rod.Bottom.Snapshot.SessionId!=session)return "rod-not-current";
            var result=rod.Bottom.SetEnvironment(session,environment);return result.Accepted?null:result.Code;
        }
        string Input(Guid rig,Guid session,long epoch,long generation,long sequence,out Rod rod)
        {
            rod=null;if(paused)return "clock-paused";
            if(!stand.CanOperateHeld(rig,session,epoch,generation))return "not-current-hand";
            if(!rods.TryGetValue(rig,out rod)||rod.Bottom.Snapshot.SessionId!=session)return "rod-not-current";
            if(epoch!=commandEpoch||generation!=commandGeneration){commandEpoch=epoch;commandGeneration=generation;lastCommand=0;}
            if(sequence<=lastCommand)return "stale-rod-input";
            lastCommand=sequence;return null;
        }
        internal string StrikeHeld(Guid rig,Guid session,long rigRevision,Guid encounter,long epoch,long generation,
            long inputSequence,out FloatFightSimulation fight)
        {
            Check();fight=null;Rod rod;
            string error=Input(rig,session,epoch,generation,inputSequence,out rod);if(error!=null)return error;
            var result=rod.Bottom.StrikeAndStart(session,rig,rigRevision,encounter,true,out fight);
            if(!result.ShouldStartFight)return result.Code;
            rod.Fight=fight;
            // The portion of this shared interval before the actual strike
            // belongs to waiting, never to the newly attached fish solver.
            rod.NextActiveMilliseconds=BottomRigSimulation.StepMilliseconds-remainder;
            return null;
        }
        internal string ReelHeld(Guid rig,Guid session,Guid encounter,long epoch,long generation,long inputSequence,bool held)
        {
            Check();Rod rod;string error=Input(rig,session,epoch,generation,inputSequence,out rod);if(error!=null)return error;
            if(rod.Fight==null)return "no-fight";
            var result=rod.Fight.SetReeling(session,encounter,held);return result.Accepted?null:result.Code;
        }
        // The host calls this only after durable sale/landing/release/recall
        // resolution. Pending or uncertain fight submissions still own the rod.
        internal string DetachConfirmed(Guid rig,Guid session)
        {
            Check();Rod rod;if(!rods.TryGetValue(rig,out rod)||rod.Bottom.Snapshot.SessionId!=session)return "rod-not-current";
            if(rod.Fight==null)rod.Bottom.End(session);
            else if(rod.Fight.Snapshot.Phase!=FloatFightPhase.Landed&&rod.Fight.Snapshot.Phase!=FloatFightPhase.Escaped){
                var ending=rod.Fight.End(session);
                if(!ending.Accepted&&ending.Snapshot.Phase!=FloatFightPhase.Ended)return ending.Code;
            }
            rods.Remove(rig);return null;
        }
    }
}
