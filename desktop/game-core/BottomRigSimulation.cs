using System;
using System.Threading;

namespace FishingDesktop.GameCore
{
    internal enum BottomPhase
    {
        Sinking, Stabilizing, Searching, Approaching, Nibbling, Strikeable, Hooked, RetrieveRequired, Ended
    }

    internal sealed class BottomRigSnapshot
    {
        internal readonly Guid SessionId,RigId,EncounterId;
        internal readonly long RigRevision,StepIndex,SignalSequence,OpenStep,CloseStep;
        internal readonly BottomPhase Phase;
        internal readonly string Reason;
        internal readonly bool SinkerOnBed,BaitOnBed,BaitAttached;
        internal readonly int SinkerYmm,BaitYmm,OutLineMm,OnSpoolMm,TipOffsetMm,RemainderMilliseconds;
        internal readonly BottomContact Contact;

        internal BottomRigSnapshot(Guid session,BottomSimpleAssembly rig,BottomContact contact,
            Guid encounter,BottomPhase phase,string reason,long step,long signal,long open,long close,
            int remainder,int sinkerY,int baitY,bool baitAttached,int line,int tip)
        {
            SessionId=session;RigId=rig.RigId;RigRevision=rig.RigRevision;Contact=contact;
            EncounterId=encounter;Phase=phase;Reason=reason;StepIndex=step;SignalSequence=signal;
            OpenStep=open;CloseStep=close;RemainderMilliseconds=remainder;SinkerYmm=sinkerY;BaitYmm=baitY;
            SinkerOnBed=sinkerY==contact.SinkerYmm;BaitOnBed=baitY==contact.BaitYmm;BaitAttached=baitAttached;
            OutLineMm=line;OnSpoolMm=rig.LineRemainingMm-line;TipOffsetMm=tip;
        }
    }

    internal sealed class BottomActionResult
    {
        internal readonly bool Accepted,ShouldStartFight;
        internal readonly string Code;
        // Only a successful fresh strike returns the already selected target.
        internal readonly FloatBiteTarget Target;
        internal readonly BottomRigSnapshot Snapshot;
        internal BottomActionResult(bool accepted,string code,BottomRigSnapshot snapshot,FloatBiteTarget target)
        {Accepted=accepted;Code=code;Snapshot=snapshot;Target=target;ShouldStartFight=target!=null;}
    }

    // Runs on the owner's active clock after an accepted flight has reached
    // water. This object has no asset mutation, save access or grant operation.
    internal sealed class BottomRigSimulation
    {
        internal const int ModelVersion=1,StepMilliseconds=50,StableSteps=5,MaximumSettlingSteps=600;
        const int SinkerDropMm=50,BaitFreeDropMm=35;
        // 240 mm trailing separation and 320 mm vertical make a 400 mm leader.
        const int MaximumVerticalSeparationMm=320;
        static readonly FloatBiteRules encounterTiming=new FloatBiteRules(1,8,10,20);
        readonly int ownerThread=Thread.CurrentThread.ManagedThreadId;
        readonly Guid session;
        readonly BottomSimpleAssembly rig;
        readonly FloatStillWaterGeometry water;
        readonly BottomContact contact;
        readonly uint seed;
        FloatBiteEnvironment environment;
        FloatBiteSimulation bite;
        FloatBiteSnapshot biteState;
        BottomPhase phase=BottomPhase.Sinking;
        string reason="sinking";
        long step,signal,phaseStart,encounterStepOrigin;
        int remainder,sinkerY,baitY,outLine;
        bool attached=true;

        BottomRigSimulation(Guid session,BottomSimpleAssembly rig,BottomSurveyPatch patch,
            BottomContact contact,uint seed,FloatBiteEnvironment environment)
        {
            this.session=session;this.rig=rig;this.contact=contact;this.seed=seed;this.environment=environment;
            water=patch.Geometry;sinkerY=baitY=water.SurfaceYmm;
            outLine=BottomContact.LineTo(water,contact.Xmm,sinkerY,contact.Zmm);
        }

        internal static string AtWaterContact(Guid confirmedSession,BottomSimpleAssembly rig,BottomSurveyPatch patch,
            long expectedGeometryVersion,int xMm,int zMm,uint seed,FloatBiteEnvironment environment,out BottomRigSimulation simulation)
        {
            simulation=null;
            if(confirmedSession==Guid.Empty)return "session-not-current";
            if(rig==null||patch==null||environment==null)return "configuration-missing";
            if(!environment.Available)return "environment-unavailable";
            string error=rig.ValidateComplete();if(error!=null)return error;
            BottomContact bottom;
            error=patch.Resolve(expectedGeometryVersion,xMm,zMm,rig.LineRemainingMm,out bottom);
            if(error!=null)return error;
            simulation=new BottomRigSimulation(confirmedSession,rig,patch,bottom,seed,environment);
            return null;
        }

        bool Active{get{return phase!=BottomPhase.Hooked&&phase!=BottomPhase.RetrieveRequired&&phase!=BottomPhase.Ended;}}
        void CheckThread()
        {
            if(Thread.CurrentThread.ManagedThreadId!=ownerThread)
                throw new InvalidOperationException("Use the bottom rig owner's host thread.");
        }
        void Enter(BottomPhase next,string code)
        {
            if(phase!=next){
                phaseStart=step;
                if(next==BottomPhase.Nibbling||next==BottomPhase.Strikeable)signal=checked(signal+1);
            }
            phase=next;reason=code;
            if(!Active)remainder=0;
        }
        int TipOffset()
        {
            int age=(int)((step-phaseStart)%8);
            switch(phase){
                case BottomPhase.Sinking:case BottomPhase.Stabilizing:return 2;
                case BottomPhase.Searching:case BottomPhase.Approaching:return (int)(step/8%2);
                case BottomPhase.Nibbling:return 4+(age<=4?age:8-age)*3;
                case BottomPhase.Strikeable:return 28+(int)Math.Min(step-phaseStart,6)*3;
                case BottomPhase.Hooked:return 36;
                default:return 0;
            }
        }
        internal BottomRigSnapshot Snapshot
        {
            get{
                CheckThread();
                long open=biteState!=null&&biteState.OpenStep!=0?encounterStepOrigin+biteState.OpenStep:0;
                long close=biteState!=null&&biteState.CloseStep!=0?encounterStepOrigin+biteState.CloseStep:0;
                return new BottomRigSnapshot(session,rig,contact,biteState==null?Guid.Empty:biteState.EncounterId,
                    phase,reason,step,signal,open,close,remainder,sinkerY,baitY,attached,outLine,TipOffset());
            }
        }
        BottomActionResult Result(bool accepted,string code,FloatBiteTarget target=null)
        {return new BottomActionResult(accepted,code,Snapshot,target);}

        void ApplyEncounter(FloatBiteSnapshot state)
        {
            biteState=state;attached=state.BaitAttached;
            switch(state.Phase){
                case FloatBitePhase.Searching:Enter(BottomPhase.Searching,state.Reason);break;
                case FloatBitePhase.Approaching:Enter(BottomPhase.Approaching,state.Reason);break;
                case FloatBitePhase.Nibbling:Enter(BottomPhase.Nibbling,state.Reason);break;
                case FloatBitePhase.Strikeable:Enter(BottomPhase.Strikeable,state.Reason);break;
                case FloatBitePhase.Hooked:Enter(BottomPhase.Hooked,state.Reason);break;
                case FloatBitePhase.RetrieveRequired:Enter(BottomPhase.RetrieveRequired,state.Reason);break;
                default:throw new InvalidOperationException("Unexpected bottom encounter transition.");
            }
        }
        void Tick()
        {
            step=checked(step+1);
            if(bite!=null){ApplyEncounter(bite.Advance(session,StepMilliseconds).Snapshot);return;}
            if(phase==BottomPhase.Sinking){
                sinkerY=Math.Max(contact.SinkerYmm,sinkerY-SinkerDropMm);
                int freeFallY=baitY-BaitFreeDropMm;
                baitY=Math.Max(contact.BaitYmm,Math.Min(freeFallY,sinkerY+MaximumVerticalSeparationMm));
                outLine=BottomContact.LineTo(water,contact.Xmm,sinkerY,contact.Zmm);
                if(sinkerY==contact.SinkerYmm&&baitY==contact.BaitYmm)Enter(BottomPhase.Stabilizing,"stabilizing");
            }else if(phase==BottomPhase.Stabilizing&&step-phaseStart>=StableSteps){
                // The shared encounter clock starts once, after the physical
                // settling interval. Its bobber presentation is never used.
                encounterStepOrigin=step-1;
                bite=new FloatBiteSimulation(session,seed^0x624f5454u,encounterTiming,environment);
                ApplyEncounter(bite.Advance(session,StepMilliseconds).Snapshot);
            }
            if(bite==null&&step>=MaximumSettlingSteps)Enter(BottomPhase.RetrieveRequired,"settling-timeout");
        }
        internal BottomActionResult Advance(Guid sessionId,int activeMilliseconds)
        {
            CheckThread();
            if(sessionId!=session)return Result(false,"session-not-current");
            if(activeMilliseconds<0||activeMilliseconds>250)throw new ArgumentOutOfRangeException("activeMilliseconds");
            if(!Active)return Result(false,"bottom-not-active");
            remainder+=activeMilliseconds;
            while(Active&&remainder>=StepMilliseconds){remainder-=StepMilliseconds;Tick();}
            return Result(true,reason);
        }
        internal BottomActionResult Strike(Guid sessionId,Guid rigId,long rigRevision,Guid encounterId,bool held)
        {
            CheckThread();
            if(sessionId!=session)return Result(false,"session-not-current");
            if(rigId!=rig.RigId||rigRevision!=rig.RigRevision)return Result(false,"rig-not-current");
            if(!held)return Result(false,"not-held");
            if(!Active)return Result(false,"bottom-not-active");
            if(bite==null){
                if(encounterId!=Guid.Empty)return Result(false,"encounter-not-current");
                attached=false;Enter(BottomPhase.RetrieveRequired,"empty-strike");
                return Result(true,reason);
            }
            var attempt=bite.Strike(session,encounterId);
            if(!attempt.Accepted)return Result(false,attempt.Code);
            ApplyEncounter(attempt.Snapshot);
            return Result(true,reason,attempt.ShouldStartFight?attempt.Snapshot.Target:null);
        }
        internal BottomActionResult SetEnvironment(Guid sessionId,FloatBiteEnvironment next)
        {
            CheckThread();
            if(sessionId!=session)return Result(false,"session-not-current");
            if(next==null)throw new ArgumentNullException("next");
            if(!Active)return Result(false,"bottom-not-active");
            environment=next;
            if(bite!=null){
                var update=bite.SetEnvironment(session,next);
                ApplyEncounter(update.Snapshot);
                return Result(update.Accepted,update.Code);
            }
            if(!next.Available){Enter(BottomPhase.RetrieveRequired,"environment-interrupted");return Result(true,reason);}
            return Result(true,"environment-updated");
        }
        internal BottomActionResult End(Guid sessionId)
        {
            CheckThread();
            if(sessionId!=session)return Result(false,"session-not-current");
            if(phase==BottomPhase.Ended)return Result(false,"bottom-not-active");
            if(bite!=null)bite.End(session);
            attached=false;Enter(BottomPhase.Ended,"ended");return Result(true,reason);
        }
    }
}
