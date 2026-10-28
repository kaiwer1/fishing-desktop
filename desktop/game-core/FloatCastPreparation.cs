using System;
using System.IO;
using System.Threading;

namespace FishingDesktop.GameCore
{
    // All types in this slice are host-only. This is a fixed-target preparation
    // seam, not terrain, projectile physics, a timer or a page command API.
    internal sealed class FloatCastTarget
    {
        internal readonly int Xmm,Ymm,Zmm;
        internal readonly long RulesVersion,RequiredLineMm;
        internal FloatCastTarget(int xMm,int yMm,int zMm,long rulesVersion,long requiredLineMm)
        {
            if(rulesVersion<=0||requiredLineMm<=0)throw new ArgumentOutOfRangeException("rulesVersion");
            Xmm=xMm;Ymm=yMm;Zmm=zMm;RulesVersion=rulesVersion;RequiredLineMm=requiredLineMm;
        }
    }
    internal sealed class FloatCastPreparationRules
    {
        internal readonly int MaximumChargeMilliseconds,MinimumPowerPermille;
        internal FloatCastPreparationRules(int maximumChargeMilliseconds,int minimumPowerPermille)
        {
            if(maximumChargeMilliseconds<=0||minimumPowerPermille<0||minimumPowerPermille>1000)throw new ArgumentOutOfRangeException("maximumChargeMilliseconds");
            MaximumChargeMilliseconds=maximumChargeMilliseconds;MinimumPowerPermille=minimumPowerPermille;
        }
    }
    internal enum FloatCastPhase { Idle,Charging,Prepared,Committed,Cancelled,Rejected,Unknown }
    internal sealed class FloatCastGesture { internal FloatCastGesture(){} }
    internal sealed class FloatCastSnapshot
    {
        internal readonly FloatCastPhase Phase;
        internal readonly int ElapsedMilliseconds,PowerPermille;
        internal readonly FloatCastTarget Target;
        internal readonly Guid SessionId;
        internal FloatCastSnapshot(FloatCastPhase phase,int elapsed,int power,FloatCastTarget target,Guid session)
        {Phase=phase;ElapsedMilliseconds=elapsed;PowerPermille=power;Target=target;SessionId=session;}
    }
    internal sealed class FloatCastResult
    {
        internal readonly bool Accepted,ShouldStartFlight;
        internal readonly string Code;
        internal readonly FloatCastGesture Gesture;
        internal readonly FloatCastSnapshot Snapshot;
        internal FloatCastResult(bool accepted,string code,FloatCastGesture gesture,FloatCastSnapshot snapshot,bool flight)
        {Accepted=accepted;Code=code;Gesture=gesture;Snapshot=snapshot;ShouldStartFlight=flight;}
    }
    internal sealed class FloatCastPreparation
    {
        readonly FloatGameOwner owner;
        readonly FloatCastPreparationRules rules;
        readonly Func<FloatCastTarget,bool> validateTarget;
        readonly int thread=Thread.CurrentThread.ManagedThreadId;
        FloatCastGesture gesture;
        FloatViewTicket view;
        FloatDocumentTicket document;
        FloatControlLease lease;
        FloatGameState frozen;
        FloatCastTarget target;
        FloatCommand command;
        FloatCastPhase phase=FloatCastPhase.Idle;
        int elapsed,power;
        bool busy;

        internal FloatCastPreparation(FloatGameOwner owner,FloatCastPreparationRules rules,Func<FloatCastTarget,bool> validateTarget)
        {
            if(owner==null||rules==null||validateTarget==null)throw new ArgumentNullException("preparation dependency");
            // Also assert construction on the owner's host thread.
            var initial=owner.Snapshot;
            this.owner=owner;this.rules=rules;this.validateTarget=validateTarget;
        }
        void ThreadCheck(){if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("Use the preparation owner's host thread.");}
        FloatCastSnapshot Describe(){return new FloatCastSnapshot(phase,elapsed,power,target,command==null?Guid.Empty:command.SessionId);}
        internal FloatCastSnapshot Snapshot{get{ThreadCheck();return Describe();}}
        FloatCastResult Result(bool accepted,string code,bool flight=false){return new FloatCastResult(accepted,code,gesture,Describe(),flight);}
        FloatCastResult Run(Func<FloatCastResult> operation)
        {
            ThreadCheck();if(busy)throw new InvalidOperationException("Reentrant preparation changes are not allowed.");
            busy=true;try{return operation();}finally{busy=false;}
        }
        bool Current(FloatCastGesture value){return value!=null&&Object.ReferenceEquals(value,gesture);}
        bool Uncommitted{get{return phase==FloatCastPhase.Charging||phase==FloatCastPhase.Prepared;}}
        bool Ready(FloatViewTicket checkView,FloatDocumentTicket checkDocument,FloatControlLease checkLease,FloatCastTarget checkTarget,FloatGameState expected,out string code)
        {
            code=null;FloatOwnerSnapshot snapshot;
            try{
                snapshot=owner.Snapshot;
                if(!owner.CanControl(checkView,checkDocument,checkLease)){code="control-required";return false;}
            }catch(ObjectDisposedException){code="owner-unavailable";return false;}
            if(!snapshot.CanSimulate||snapshot.State==null){code="domain-paused";return false;}
            var state=snapshot.State;
            if(state.Session!=null){code="session-busy";return false;}
            if(state.BaitCount<1){code="bait-empty";return false;}
            if(state.Stamina<state.Rules.CastStaminaCost){code="stamina-insufficient";return false;}
            if(state.Rig.DefinitionKey!=state.Rules.RigDefinitionKey){code="rig-invalid";return false;}
            // This revision is the fixed slice's rule version. Future geometry
            // freshness belongs to the host validator, not a claimed map model.
            if(checkTarget==null||checkTarget.RulesVersion!=state.RulesVersion){code="target-invalid";return false;}
            if(state.SchemaVersion==3){
                if(checkTarget.RequiredLineMm>FloatEquipmentGuard.MaximumLineMm){code="target-invalid";return false;}
                var check=state.Equipment.EvaluateCastReadiness(FloatEquipmentGate.Idle,checkTarget.RequiredLineMm,state.BaitCount,state.Stamina,state.Rules.CastStaminaCost);
                if(!check.Valid){code=check.Issues[0].Code;return false;}
            }else if(!state.Rig.LineLengthMm.HasValue||state.Rig.LineLengthMm.Value<=0){code="rig-invalid";return false;}
            else if(checkTarget.RequiredLineMm>state.Rig.LineLengthMm.Value){code="line-insufficient";return false;}
            if(expected!=null&&(state.Sequence!=expected.Sequence||state.RulesVersion!=expected.RulesVersion||
                !Object.ReferenceEquals(state.LegacyRig,expected.LegacyRig)||!Object.ReferenceEquals(state.Equipment,expected.Equipment)||state.Rules.Signature!=expected.Rules.Signature||
                state.BaitStackId!=expected.BaitStackId||state.BaitDefinitionKey!=expected.BaitDefinitionKey||
                state.BaitCount!=expected.BaitCount||state.Stamina!=expected.Stamina)){
                code="prepared-state-changed";return false;
            }
            return true;
        }
        bool TargetValid(FloatCastTarget value)
        {
            try{return validateTarget(value);}catch(Exception){return false;}
        }
        bool StillReady(out string code){return Ready(view,document,lease,target,frozen,out code);}
        FloatCastResult CancelInvalid(string code){phase=FloatCastPhase.Cancelled;return Result(false,code);}
        internal FloatCastResult Begin(FloatViewTicket newView,FloatDocumentTicket newDocument,FloatControlLease newLease,FloatCastTarget newTarget)
        {
            return Run(()=>{
                if(phase==FloatCastPhase.Unknown)return Result(false,"save-outcome-unknown");
                if(Uncommitted)return Result(false,"gesture-active");
                string code;if(!Ready(newView,newDocument,newLease,newTarget,null,out code))return Result(false,code);
                var before=owner.Snapshot.State;
                if(!TargetValid(newTarget))return Result(false,"target-invalid");
                // A host validator is read-only by contract; still recheck all
                // owner facts after it so a callback cannot authorize old data.
                if(!Ready(newView,newDocument,newLease,newTarget,before,out code))return Result(false,code);
                FloatCommand preparedCommand=before.SchemaVersion==3?(FloatCommand)new CastFloatWithEquipment(before.ProfileId,before.BranchId,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),newTarget.RequiredLineMm,before.Sequence,before.Rig.Revision,before.RulesVersion,before.Equipment.InventoryRevision,before.Equipment.RulesVersion):new CastFloat(before.ProfileId,before.BranchId,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),before.Sequence,before.Rig.Revision,before.RulesVersion);
                view=newView;document=newDocument;lease=newLease;target=newTarget;frozen=before;command=preparedCommand;
                gesture=new FloatCastGesture();elapsed=0;power=rules.MinimumPowerPermille;phase=FloatCastPhase.Charging;
                return Result(true,"charging");
            });
        }
        internal FloatCastResult Advance(FloatCastGesture value,int activeDeltaMilliseconds)
        {
            return Run(()=>{
                if(activeDeltaMilliseconds<0||activeDeltaMilliseconds>250)throw new ArgumentOutOfRangeException("activeDeltaMilliseconds");
                if(!Current(value))return Result(false,"gesture-not-current");
                if(!Uncommitted)return Result(false,"gesture-not-active");
                string code;if(!StillReady(out code))return CancelInvalid(code);
                if(phase!=FloatCastPhase.Charging)return Result(false,"gesture-not-charging");
                elapsed=(int)Math.Min((long)rules.MaximumChargeMilliseconds,(long)elapsed+activeDeltaMilliseconds);
                power=rules.MinimumPowerPermille+(int)((1000L-rules.MinimumPowerPermille)*elapsed/rules.MaximumChargeMilliseconds);
                return Result(true,"charging");
            });
        }
        internal FloatCastResult Release(FloatCastGesture value)
        {
            return Run(()=>{
                if(!Current(value))return Result(false,"gesture-not-current");
                if(!Uncommitted)return Result(false,"gesture-not-active");
                string code;if(!StillReady(out code))return CancelInvalid(code);
                if(phase!=FloatCastPhase.Charging)return Result(false,"gesture-not-charging");
                phase=FloatCastPhase.Prepared;return Result(true,"prepared");
            });
        }
        internal FloatCastResult Cancel(FloatCastGesture value)
        {
            return Run(()=>{
                if(!Current(value))return Result(false,"gesture-not-current");
                if(!Uncommitted)return Result(false,"gesture-not-active");
                phase=FloatCastPhase.Cancelled;return Result(true,"cancelled");
            });
        }
        internal FloatCastResult CommitPrepared(FloatCastGesture value)
        {
            return Run(()=>{
                if(!Current(value))return Result(false,"gesture-not-current");
                if(!Uncommitted)return Result(false,phase==FloatCastPhase.Unknown?"save-outcome-unknown":"gesture-not-active");
                string code;if(!StillReady(out code))return CancelInvalid(code);
                if(phase!=FloatCastPhase.Prepared)return Result(false,"gesture-not-prepared");
                if(!TargetValid(target)){phase=FloatCastPhase.Rejected;return Result(false,"target-invalid");}
                if(!StillReady(out code))return CancelInvalid(code);
                FloatCommitResult transaction;
                try{transaction=owner.ExecuteUser(view,document,lease,command);}
                catch(FloatSaveUncertainException){phase=FloatCastPhase.Unknown;return Result(false,"save-outcome-unknown");}
                catch(IOException){phase=FloatCastPhase.Rejected;return Result(false,"save-write-rejected");}
                catch{phase=FloatCastPhase.Rejected;throw;}
                if(transaction.Committed&&transaction.Written&&!transaction.IsReplay){
                    phase=FloatCastPhase.Committed;return Result(true,transaction.Code,true);
                }
                phase=FloatCastPhase.Rejected;return Result(false,transaction.Code);
            });
        }
    }
}
