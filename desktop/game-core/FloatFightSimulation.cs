using System;
using System.Threading;

namespace FishingDesktop.GameCore
{
    internal static class FloatFightGuard
    {
        internal static void Range(double value,double minimum,double maximum,string name)
        {if(Double.IsNaN(value)||Double.IsInfinity(value)||value<minimum||value>maximum)throw new ArgumentOutOfRangeException(name);}
        internal static void Level(int value){if(value<0||value>10)throw new ArgumentOutOfRangeException("level");}
    }
    internal sealed class FloatFightPoint
    {
        internal readonly double X,Y,Z;
        internal FloatFightPoint(double xM,double yM,double zM)
        {foreach(double value in new[]{xM,yM,zM})FloatFightGuard.Range(value,-10000,10000,"coordinate");X=xM;Y=yM;Z=zM;}
    }
    internal sealed class FloatFightWater
    {
        internal readonly double SurfaceY,BottomY,MinimumX,MaximumX,MinimumZ,MaximumZ;
        internal FloatFightWater(double surfaceY,double bottomY,double minimumX,double maximumX,double minimumZ,double maximumZ)
        {
            foreach(double value in new[]{surfaceY,bottomY,minimumX,maximumX,minimumZ,maximumZ})FloatFightGuard.Range(value,-10000,10000,"water coordinate");
            if(bottomY>=surfaceY||minimumX>=maximumX||minimumZ>=maximumZ)throw new ArgumentException("Water bounds must have positive extent.");
            SurfaceY=surfaceY;BottomY=bottomY;MinimumX=minimumX;MaximumX=maximumX;MinimumZ=minimumZ;MaximumZ=maximumZ;
        }
        internal bool Contains(FloatFightPoint p){return p.X>=MinimumX&&p.X<=MaximumX&&p.Z>=MinimumZ&&p.Z<=MaximumZ&&p.Y>=BottomY&&p.Y<=SurfaceY;}
    }
    internal sealed class FloatFightRigRules
    {
        internal readonly double LineSpringNPerM,LineDampingNsPerM,RodSpringNPerM,MaximumSupportedTensionN;
        internal readonly double MinimumDragN,MaximumDragN,MinimumReelMps,MaximumReelMps,ReelStallN,MaximumPayoutMps,MinimumOutLineM;
        internal readonly int InitialDragLevel,InitialReelLevel;
        internal FloatFightRigRules(double lineSpringNPerM,double lineDampingNsPerM,double rodSpringNPerM,double maximumSupportedTensionN,
            double minimumDragN,double maximumDragN,double minimumReelMps,double maximumReelMps,double reelStallN,double maximumPayoutMps,
            double minimumOutLineM,int initialDragLevel,int initialReelLevel)
        {
            FloatFightGuard.Range(lineSpringNPerM,.01,10000,"lineSpringNPerM");FloatFightGuard.Range(lineDampingNsPerM,0,1000,"lineDampingNsPerM");
            FloatFightGuard.Range(rodSpringNPerM,.01,10000,"rodSpringNPerM");FloatFightGuard.Range(maximumSupportedTensionN,.01,10000,"maximumSupportedTensionN");
            FloatFightGuard.Range(minimumDragN,.001,maximumSupportedTensionN,"minimumDragN");FloatFightGuard.Range(maximumDragN,minimumDragN,maximumSupportedTensionN,"maximumDragN");
            FloatFightGuard.Range(minimumReelMps,0,10,"minimumReelMps");FloatFightGuard.Range(maximumReelMps,minimumReelMps,10,"maximumReelMps");
            FloatFightGuard.Range(reelStallN,.001,maximumSupportedTensionN,"reelStallN");FloatFightGuard.Range(maximumPayoutMps,0,20,"maximumPayoutMps");
            FloatFightGuard.Range(minimumOutLineM,.001,1000,"minimumOutLineM");FloatFightGuard.Level(initialDragLevel);FloatFightGuard.Level(initialReelLevel);
            LineSpringNPerM=lineSpringNPerM;LineDampingNsPerM=lineDampingNsPerM;RodSpringNPerM=rodSpringNPerM;MaximumSupportedTensionN=maximumSupportedTensionN;
            MinimumDragN=minimumDragN;MaximumDragN=maximumDragN;MinimumReelMps=minimumReelMps;MaximumReelMps=maximumReelMps;
            ReelStallN=reelStallN;MaximumPayoutMps=maximumPayoutMps;MinimumOutLineM=minimumOutLineM;InitialDragLevel=initialDragLevel;InitialReelLevel=initialReelLevel;
        }
    }
    internal enum FloatFightBehavior { Seeded,Outward,Rest }
    internal sealed class FloatFightFishRules
    {
        internal readonly double DriveForceN,WaterDragNsPerM,FatiguePerJoule,RecoveryPerSecond,MaximumSupportedSpeedMps;
        internal readonly int DecisionSteps;
        internal readonly FloatFightBehavior Behavior;
        internal FloatFightFishRules(double driveForceN,double waterDragNsPerM,int decisionSteps,double fatiguePerJoule,double recoveryPerSecond,
            double maximumSupportedSpeedMps,FloatFightBehavior behavior=FloatFightBehavior.Seeded)
        {
            FloatFightGuard.Range(driveForceN,0,1000,"driveForceN");FloatFightGuard.Range(waterDragNsPerM,.01,1000,"waterDragNsPerM");
            if(decisionSteps<1||decisionSteps>12000)throw new ArgumentOutOfRangeException("decisionSteps");
            FloatFightGuard.Range(fatiguePerJoule,0,10,"fatiguePerJoule");FloatFightGuard.Range(recoveryPerSecond,0,1,"recoveryPerSecond");
            FloatFightGuard.Range(maximumSupportedSpeedMps,.01,50,"maximumSupportedSpeedMps");
            if(behavior!=FloatFightBehavior.Seeded&&behavior!=FloatFightBehavior.Outward&&behavior!=FloatFightBehavior.Rest)throw new ArgumentOutOfRangeException("behavior");
            DriveForceN=driveForceN;WaterDragNsPerM=waterDragNsPerM;DecisionSteps=decisionSteps;FatiguePerJoule=fatiguePerJoule;
            RecoveryPerSecond=recoveryPerSecond;MaximumSupportedSpeedMps=maximumSupportedSpeedMps;Behavior=behavior;
        }
    }
    internal sealed class FloatLandingRules
    {
        // The development adapter supplies this fixture capability. This bool
        // is not an owned inventory item or permission to invent a free net.
        internal readonly bool NetAvailable;
        internal readonly double MaximumDistanceM,MaximumDepthM,MaximumSpeedMps,MaximumEnergy;
        internal FloatLandingRules(bool netAvailable,double maximumDistanceM,double maximumDepthM,double maximumSpeedMps,double maximumEnergy)
        {
            FloatFightGuard.Range(maximumDistanceM,.001,100,"maximumDistanceM");FloatFightGuard.Range(maximumDepthM,0,100,"maximumDepthM");
            FloatFightGuard.Range(maximumSpeedMps,0,50,"maximumSpeedMps");FloatFightGuard.Range(maximumEnergy,0,1,"maximumEnergy");
            NetAvailable=netAvailable;MaximumDistanceM=maximumDistanceM;MaximumDepthM=maximumDepthM;MaximumSpeedMps=maximumSpeedMps;MaximumEnergy=maximumEnergy;
        }
    }
    internal sealed class FloatFightSetup
    {
        internal readonly Guid SessionId,EncounterId;
        internal readonly FloatBiteTarget Target;
        internal readonly long RigRevision,TotalLineMm;
        internal readonly double InitialOutLineM;
        internal readonly FloatFightPoint Anchor,FishStart;
        internal readonly FloatFightWater Water;
        internal readonly FloatFightRigRules Rig;
        internal readonly FloatFightFishRules Fish;
        internal readonly FloatLandingRules Landing;
        internal readonly uint Seed;
        internal FloatFightSetup(Guid sessionId,Guid encounterId,FloatBiteTarget target,long rigRevision,FloatFightPoint anchor,FloatFightPoint fishStart,
            long totalLineMm,double initialOutLineM,FloatFightWater water,FloatFightRigRules rig,FloatFightFishRules fish,FloatLandingRules landing,uint seed)
        {
            if(sessionId==Guid.Empty||encounterId==Guid.Empty||sessionId==encounterId)throw new ArgumentException("Distinct session and encounter identities are required.");
            if(target==null||anchor==null||fishStart==null||water==null||rig==null||fish==null||landing==null)throw new ArgumentNullException("fight dependency");
            if(String.IsNullOrEmpty(target.SpeciesId)||target.MassMg<10000||target.MassMg>500000000)throw new ArgumentException("Target is outside the development mass envelope.");
            if(rigRevision<1||totalLineMm<1||totalLineMm>100000000)throw new ArgumentOutOfRangeException("rigRevision or totalLineMm");
            if(anchor.Y<=water.SurfaceY||anchor.Z>=water.MinimumZ||anchor.X<water.MinimumX||anchor.X>water.MaximumX||!water.Contains(fishStart))
                throw new ArgumentException("This slice requires a bank anchor behind the rectangular water and a valid fixed fish depth.");
            FloatFightGuard.Range(initialOutLineM,rig.MinimumOutLineM,totalLineMm/1000.0,"initialOutLineM");
            double dx=fishStart.X-anchor.X,dy=fishStart.Y-anchor.Y,dz=fishStart.Z-anchor.Z;
            if(initialOutLineM+1e-9<Math.Sqrt(dx*dx+dy*dy+dz*dz))throw new ArgumentException("Initial line cannot be shorter than the fish path.");
            SessionId=sessionId;EncounterId=encounterId;Target=target;RigRevision=rigRevision;Anchor=anchor;FishStart=fishStart;
            TotalLineMm=totalLineMm;InitialOutLineM=initialOutLineM;Water=water;Rig=rig;Fish=fish;Landing=landing;Seed=seed;
        }
    }
    internal enum FloatFightPhase { Fighting,LandingEligible,LandingPending,Landed,CapacityBlocked,Faulted,Ended }
    internal enum FloatLandingOutcome { Landed,CapacityBlocked,KnownNotWritten,Unknown }
    internal sealed class FloatLandingTicket
    {
        internal readonly Guid LandingId,SessionId,EncounterId;
        internal FloatLandingTicket(Guid landing,Guid session,Guid encounter){LandingId=landing;SessionId=session;EncounterId=encounter;}
    }
    internal sealed class FloatFightSnapshot
    {
        internal readonly Guid SessionId,EncounterId;
        internal readonly long RigRevision,StepIndex;
        internal readonly int RemainderMilliseconds,DragLevel,ReelLevel,LandingAttempt;
        internal readonly FloatFightPhase Phase;
        internal readonly string Reason,LandingReason;
        internal readonly FloatFightPoint Position,Velocity;
        internal readonly double OutLineM,SpoolRemainingM,SlackM,TensionN,DragN,RodDeflectionM,Energy,ReeledThisStepM,PaidOutThisStepM;
        internal readonly bool Reeling;
        internal readonly FloatBiteTarget Target;
        internal FloatFightSnapshot(FloatFightSetup setup,long step,int remainder,FloatFightPhase phase,string reason,string landingReason,
            FloatFightPoint position,FloatFightPoint velocity,double line,double slack,double tension,double drag,double energy,bool reeling,int dragLevel,int reelLevel,
            double reeled,double paidOut,int landingAttempt)
        {SessionId=setup.SessionId;EncounterId=setup.EncounterId;RigRevision=setup.RigRevision;Target=setup.Target;StepIndex=step;RemainderMilliseconds=remainder;
            Phase=phase;Reason=reason;LandingReason=landingReason;Position=position;Velocity=velocity;OutLineM=line;SpoolRemainingM=setup.TotalLineMm/1000.0-line;
            SlackM=slack;TensionN=tension;DragN=drag;RodDeflectionM=tension/setup.Rig.RodSpringNPerM;Energy=energy;Reeling=reeling;DragLevel=dragLevel;ReelLevel=reelLevel;
            ReeledThisStepM=reeled;PaidOutThisStepM=paidOut;LandingAttempt=landingAttempt;}
    }
    internal sealed class FloatFightResult
    {
        internal readonly bool Accepted,ShouldCommitLanding,TerminalEvent;
        internal readonly string Code;
        internal readonly FloatLandingTicket LandingTicket;
        internal readonly int LandingAttempt;
        internal readonly FloatFightSnapshot Snapshot;
        internal FloatFightResult(bool accepted,bool submit,bool terminal,string code,FloatLandingTicket ticket,int attempt,FloatFightSnapshot snapshot)
        {Accepted=accepted;ShouldCommitLanding=submit;TerminalEvent=terminal;Code=code;LandingTicket=ticket;LandingAttempt=attempt;Snapshot=snapshot;}
    }
    internal sealed class FloatFightSimulation
    {
        internal const int ModelVersion=1,StepMilliseconds=50;
        const double Dt=.05;
        readonly int thread=Thread.CurrentThread.ManagedThreadId;
        readonly FloatFightSetup setup;
        readonly FloatBiteRandom random;
        readonly double mass,spring,total;
        double x,z,vx,vz,line,tension,energy=1,reeled,paidOut;
        long step;
        int remainder,dragLevel,reelLevel,behavior;
        bool reeling,terminalEmitted,landingSubmitted,landingUnknown;
        FloatFightPhase phase=FloatFightPhase.Fighting;
        string reason="fighting";
        string solveFailure="solver-unresolved";
        FloatLandingTicket landingTicket;
        int landingAttempt;
        internal FloatFightSimulation(FloatFightSetup setup)
        {
            if(setup==null)throw new ArgumentNullException("setup");this.setup=setup;random=new FloatBiteRandom(setup.Seed^0x657832cbu);
            mass=setup.Target.MassMg/1000000.0;spring=1/(1/setup.Rig.LineSpringNPerM+1/setup.Rig.RodSpringNPerM);total=setup.TotalLineMm/1000.0;
            x=setup.FishStart.X;z=setup.FishStart.Z;line=setup.InitialOutLineM;dragLevel=setup.Rig.InitialDragLevel;reelLevel=setup.Rig.InitialReelLevel;
            UpdateEligibility();
        }
        void Check(){if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("Use the fight owner's host thread.");}
        bool Active{get{return phase==FloatFightPhase.Fighting||phase==FloatFightPhase.LandingEligible;}}
        bool Current(Guid sessionId,Guid encounterId){return sessionId==setup.SessionId&&encounterId==setup.EncounterId;}
        double Drag{get{return setup.Rig.MinimumDragN+(setup.Rig.MaximumDragN-setup.Rig.MinimumDragN)*dragLevel/10;}}
        double Speed{get{return setup.Rig.MinimumReelMps+(setup.Rig.MaximumReelMps-setup.Rig.MinimumReelMps)*reelLevel/10;}}
        double Distance(double px,double pz)
        {double dx=px-setup.Anchor.X,dy=setup.FishStart.Y-setup.Anchor.Y,dz=pz-setup.Anchor.Z;return Math.Sqrt(dx*dx+dy*dy+dz*dz);}
        string LandingReason()
        {
            if(!setup.Landing.NetAvailable)return "net-unavailable";
            if(Distance(x,z)>setup.Landing.MaximumDistanceM)return "fish-too-far";
            if(setup.Water.SurfaceY-setup.FishStart.Y>setup.Landing.MaximumDepthM)return "fish-too-deep";
            if(Math.Sqrt(vx*vx+vz*vz)>setup.Landing.MaximumSpeedMps)return "fish-moving";
            if(energy>setup.Landing.MaximumEnergy)return "fish-active";
            return "eligible";
        }
        void UpdateEligibility(){if(!Active)return;bool ready=LandingReason()=="eligible";phase=ready?FloatFightPhase.LandingEligible:FloatFightPhase.Fighting;reason=ready?"landing-eligible":"fighting";}
        void Fault(string why){phase=FloatFightPhase.Faulted;reason=why;reeling=false;remainder=0;}
        static void Boundary(ref double position,ref double velocity,double minimum,double maximum)
        {if(position<minimum){position=minimum;if(velocity<0)velocity=0;}else if(position>maximum){position=maximum;if(velocity>0)velocity=0;}}
        sealed class Solution
        {internal double X,Z,Vx,Vz,Tension;}
        Solution AtForce(double force,double predictedVx,double predictedVz,double nx,double nz)
        {
            double nextVx=predictedVx-nx*force*Dt/mass,nextVz=predictedVz-nz*force*Dt/mass;
            double nextX=x+nextVx*Dt,nextZ=z+nextVz*Dt;
            // A line impulse approaches its anchor; it cannot propel a fish
            // through the anchor's projected axis. This bounds the implicit
            // trial path before a very large trial force reverses distance and
            // destroys scalar bracket monotonicity. Independent predicted fish
            // motion may still cross this axis normally on a later step.
            if((nx>0&&nextX<setup.Anchor.X)||(nx<0&&nextX>setup.Anchor.X)){nextX=setup.Anchor.X;nextVx=0;}
            if((nz>0&&nextZ<setup.Anchor.Z)||(nz<0&&nextZ>setup.Anchor.Z)){nextZ=setup.Anchor.Z;nextVz=0;}
            Boundary(ref nextX,ref nextVx,setup.Water.MinimumX,setup.Water.MaximumX);Boundary(ref nextZ,ref nextVz,setup.Water.MinimumZ,setup.Water.MaximumZ);
            return new Solution {X=nextX,Z=nextZ,Vx=nextVx,Vz=nextVz,Tension=force};
        }
        double SpringForce(Solution state,double outLine)
        {
            double distance=Distance(state.X,state.Z),extension=distance-outLine;if(extension<=0)return 0;
            double separation=((state.X-setup.Anchor.X)*state.Vx+(state.Z-setup.Anchor.Z)*state.Vz)/distance-(outLine-line)/Dt;
            // Fade damping in over 5 cm of extension so a slack line produces
            // neither force nor a discontinuous contact jump in the solver.
            return Math.Max(0,spring*extension+setup.Rig.LineDampingNsPerM*separation*Math.Min(1,extension/.05));
        }
        Solution Solve(double outLine,double predictedVx,double predictedVz)
        {
            solveFailure="solver-unresolved";
            double px=x+predictedVx*Dt,pz=z+predictedVz*Dt;
            double distance=Distance(px,pz),nx=(px-setup.Anchor.X)/distance,nz=(pz-setup.Anchor.Z)/distance;
            var zero=AtForce(0,predictedVx,predictedVz,nx,nz);if(SpringForce(zero,outLine)==0)return zero;
            double low=0,high=setup.Rig.MaximumSupportedTensionN*8;
            var upper=AtForce(high,predictedVx,predictedVz,nx,nz);if(high<SpringForce(upper,outLine)){solveFailure="load-outside-supported-range";return null;}
            Solution candidate=null;
            for(int i=0;i<40;i++){
                double middle=(low+high)/2;candidate=AtForce(middle,predictedVx,predictedVz,nx,nz);
                double residual=middle-SpringForce(candidate,outLine);
                if(Math.Abs(residual)<=Math.Max(1e-7,setup.Rig.MaximumSupportedTensionN*1e-7))return candidate;
                if(residual>0)high=middle;else low=middle;
            }
            return null;
        }
        void Tick()
        {
            step=checked(step+1);reeled=0;paidOut=0;
            if(step==1||(step-1)%setup.Fish.DecisionSteps==0){
                if(setup.Fish.Behavior==FloatFightBehavior.Outward)behavior=0;
                else if(setup.Fish.Behavior==FloatFightBehavior.Rest)behavior=3;
                else{ulong value=random.Bounded(100);behavior=value<55?0:value<70?1:value<85?2:3;}
            }
            double horizontal=Math.Sqrt((x-setup.Anchor.X)*(x-setup.Anchor.X)+(z-setup.Anchor.Z)*(z-setup.Anchor.Z));
            double outwardX=(x-setup.Anchor.X)/horizontal,outwardZ=(z-setup.Anchor.Z)/horizontal;
            double drive=setup.Fish.DriveForceN*(.12+.88*energy),fx=0,fz=0;
            if(behavior==0){fx=outwardX*drive;fz=outwardZ*drive;}
            else if(behavior==1){fx=-outwardZ*drive;fz=outwardX*drive;}
            else if(behavior==2){fx=outwardZ*drive;fz=-outwardX*drive;}
            double damping=1+setup.Fish.WaterDragNsPerM*Dt/mass;
            double predictedVx=(vx+fx*Dt/mass)/damping,predictedVz=(vz+fz*Dt/mass)/damping;
            double requested=reeling?Math.Min(Math.Max(0,line-setup.Rig.MinimumOutLineM),Speed*Dt):0;
            double candidateLine=line-requested;var solved=Solve(candidateLine,predictedVx,predictedVz);
            // A trial winding request can exceed the bracket even though the
            // unwound state is supported. Refuse that winding before treating
            // the fish/rig state as an unsupported load.
            if(solved==null&&requested>0&&solveFailure=="load-outside-supported-range"){
                requested=0;candidateLine=line;solved=Solve(candidateLine,predictedVx,predictedVz);
            }
            if(solved==null&&solveFailure!="load-outside-supported-range"){Fault(solveFailure);return;}
            // Current-step reel torque, not the previous frame's load, limits
            // winding. A stalled reel cannot force its requested shortening.
            if(requested>0&&solved!=null&&solved.Tension>setup.Rig.ReelStallN){
                var unwound=Solve(line,predictedVx,predictedVz);if(unwound==null){Fault(solveFailure);return;}
                if(unwound.Tension>=setup.Rig.ReelStallN){requested=0;candidateLine=line;solved=unwound;}
                else{
                    double low=0,high=requested;
                    for(int i=0;i<20;i++){double amount=(low+high)/2;var trial=Solve(line-amount,predictedVx,predictedVz);if(trial==null){Fault(solveFailure);return;}
                        if(trial.Tension>setup.Rig.ReelStallN)high=amount;else low=amount;}
                    requested=low;candidateLine=line-requested;solved=Solve(candidateLine,predictedVx,predictedVz);
                }
            }
            double payout=0;
            if(solved==null&&solveFailure!="load-outside-supported-range"){Fault(solveFailure);return;}
            if((solved==null||solved.Tension>Drag)&&candidateLine<total&&setup.Rig.MaximumPayoutMps>0){
                double limit=Math.Min(total-candidateLine,setup.Rig.MaximumPayoutMps*Dt);var full=Solve(candidateLine+limit,predictedVx,predictedVz);
                if(full==null){Fault(solveFailure);return;}
                if(full.Tension>=Drag)payout=limit;
                else{double low=0,high=limit;for(int i=0;i<20;i++){double amount=(low+high)/2;var trial=Solve(candidateLine+amount,predictedVx,predictedVz);
                        if(trial==null){if(solveFailure=="load-outside-supported-range"){low=amount;continue;}Fault(solveFailure);return;}
                        if(trial.Tension>Drag)low=amount;else high=amount;}payout=high;}
                candidateLine+=payout;solved=Solve(candidateLine,predictedVx,predictedVz);
            }
            if(solved==null){Fault(solveFailure);return;}
            if(solved.Tension>setup.Rig.MaximumSupportedTensionN){Fault("load-outside-supported-range");return;}
            double speed=Math.Sqrt(solved.Vx*solved.Vx+solved.Vz*solved.Vz);
            if(Double.IsNaN(speed)||Double.IsInfinity(speed)||speed>setup.Fish.MaximumSupportedSpeedMps){Fault("speed-outside-supported-range");return;}
            if(candidateLine<setup.Rig.MinimumOutLineM-1e-9||candidateLine>total+1e-9){Fault("line-conservation-failed");return;}
            double actualTravel=Math.Sqrt((solved.X-x)*(solved.X-x)+(solved.Z-z)*(solved.Z-z));
            // Equivalent effort includes positive propulsive work, resisting
            // line work and a 0.03 m/s contraction proxy only under load. It
            // is a declared gameplay approximation, not measured physiology.
            double effort=Math.Max(0,fx*solved.Vx+fz*solved.Vz)*Dt+solved.Tension*actualTravel;
            if(solved.Tension>0&&behavior!=3)effort+=drive*.03*Dt;
            double recovery=solved.Tension<Drag*.25&&behavior==3?setup.Fish.RecoveryPerSecond*Dt:0;
            energy=Math.Max(0,Math.Min(1,energy-effort*setup.Fish.FatiguePerJoule+recovery));
            x=solved.X;z=solved.Z;vx=solved.Vx;vz=solved.Vz;tension=solved.Tension;line=Math.Max(setup.Rig.MinimumOutLineM,Math.Min(total,candidateLine));
            reeled=requested;paidOut=payout;UpdateEligibility();
        }
        FloatFightSnapshot Describe()
        {return new FloatFightSnapshot(setup,step,remainder,phase,reason,LandingReason(),new FloatFightPoint(x,setup.FishStart.Y,z),new FloatFightPoint(vx,0,vz),
            line,Math.Max(0,line-Distance(x,z)),tension,Drag,energy,reeling,dragLevel,reelLevel,reeled,paidOut,landingAttempt);}
        internal FloatFightSnapshot Snapshot{get{Check();return Describe();}}
        FloatFightResult Result(bool accepted,bool submit,bool terminal,string code)
        {return new FloatFightResult(accepted,submit,terminal,code,landingTicket,landingAttempt,Describe());}
        internal FloatFightResult Advance(Guid sessionId,int activeMilliseconds)
        {
            Check();if(activeMilliseconds<0||activeMilliseconds>250)throw new ArgumentOutOfRangeException("activeMilliseconds");
            if(sessionId!=setup.SessionId)return Result(false,false,false,"session-not-current");if(!Active)return Result(false,false,false,"fight-not-active");
            remainder+=activeMilliseconds;while(remainder>=StepMilliseconds&&Active){remainder-=StepMilliseconds;Tick();}
            return Result(true,false,false,reason);
        }
        internal FloatFightResult SetReeling(Guid sessionId,Guid encounterId,bool held)
        {Check();if(!Current(sessionId,encounterId))return Result(false,false,false,"fight-not-current");if(!Active)return Result(false,false,false,"fight-not-active");reeling=held;return Result(true,false,false,held?"reel-held":"reel-released");}
        internal FloatFightResult SetDragLevel(Guid sessionId,Guid encounterId,int level)
        {Check();FloatFightGuard.Level(level);if(!Current(sessionId,encounterId))return Result(false,false,false,"fight-not-current");if(!Active)return Result(false,false,false,"fight-not-active");dragLevel=level;return Result(true,false,false,"drag-updated");}
        internal FloatFightResult SetReelLevel(Guid sessionId,Guid encounterId,int level)
        {Check();FloatFightGuard.Level(level);if(!Current(sessionId,encounterId))return Result(false,false,false,"fight-not-current");if(!Active)return Result(false,false,false,"fight-not-active");reelLevel=level;return Result(true,false,false,"speed-updated");}
        internal FloatFightResult ClearInput(Guid sessionId)
        {Check();if(sessionId!=setup.SessionId)return Result(false,false,false,"session-not-current");reeling=false;return Result(true,false,false,"input-cleared");}
        internal FloatFightResult TryLand(Guid sessionId,Guid encounterId,Guid landingId)
        {
            Check();if(!Current(sessionId,encounterId))return Result(false,false,false,"fight-not-current");
            if(landingId==Guid.Empty||landingId==setup.SessionId||landingId==setup.EncounterId)return Result(false,false,false,"landing-identity-invalid");
            if(landingTicket!=null){if(landingTicket.LandingId==landingId)return Result(true,false,false,"landing-already-requested");return Result(false,false,false,"landing-already-pending");}
            if(!Active)return Result(false,false,false,"fight-not-active");
            string eligibility=LandingReason();if(eligibility!="eligible")return Result(false,false,false,eligibility);
            landingTicket=new FloatLandingTicket(landingId,sessionId,encounterId);landingAttempt=1;landingSubmitted=true;reeling=false;remainder=0;
            phase=FloatFightPhase.LandingPending;reason="landing-pending";return Result(true,true,false,reason);
        }
        internal FloatFightResult RetryLanding(FloatLandingTicket ticket)
        {
            Check();if(!Object.ReferenceEquals(ticket,landingTicket)||ticket==null)return Result(false,false,false,"landing-ticket-invalid");
            if(phase!=FloatFightPhase.LandingPending||landingSubmitted||landingUnknown)return Result(false,false,false,"landing-retry-unavailable");
            landingAttempt=checked(landingAttempt+1);landingSubmitted=true;reason="landing-pending";return Result(true,true,false,reason);
        }
        internal FloatFightResult ConfirmLanding(FloatLandingTicket ticket,int attempt,FloatLandingOutcome outcome)
        {
            Check();if(outcome!=FloatLandingOutcome.Landed&&outcome!=FloatLandingOutcome.CapacityBlocked&&outcome!=FloatLandingOutcome.KnownNotWritten&&outcome!=FloatLandingOutcome.Unknown)throw new ArgumentOutOfRangeException("outcome");
            if(!Object.ReferenceEquals(ticket,landingTicket)||ticket==null)return Result(false,false,false,"landing-ticket-invalid");
            if(phase!=FloatFightPhase.LandingPending||!landingSubmitted||attempt!=landingAttempt)return Result(false,false,false,"landing-attempt-not-current");
            landingSubmitted=false;
            if(outcome==FloatLandingOutcome.Unknown){landingUnknown=true;Fault("landing-save-unknown");return Result(true,false,false,reason);}
            if(outcome==FloatLandingOutcome.KnownNotWritten){reason="landing-not-written";return Result(true,false,false,reason);}
            phase=outcome==FloatLandingOutcome.Landed?FloatFightPhase.Landed:FloatFightPhase.CapacityBlocked;reason=outcome==FloatLandingOutcome.Landed?"landed":"creel-capacity-exceeded";
            bool emit=outcome==FloatLandingOutcome.Landed&&!terminalEmitted;terminalEmitted|=emit;return Result(true,false,emit,reason);
        }
        internal FloatFightResult End(Guid sessionId)
        {
            Check();if(sessionId!=setup.SessionId)return Result(false,false,false,"session-not-current");
            if(landingUnknown||(phase==FloatFightPhase.LandingPending&&landingSubmitted))return Result(false,false,false,"landing-result-required");
            if(phase==FloatFightPhase.Ended)return Result(false,false,false,"fight-not-active");
            phase=FloatFightPhase.Ended;reason="ended";reeling=false;remainder=0;bool emit=!terminalEmitted;terminalEmitted=true;return Result(true,false,emit,reason);
        }
    }
}
