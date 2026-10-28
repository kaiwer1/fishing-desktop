using System;
using System.Threading;
using FishingDesktop.GameCore;

static class FloatFightSimulationTests
{
    static int passed,failed,nextId;
    static Guid G(){return new Guid(++nextId,0,0,new byte[]{0,0,0,0,0,0,0,11});}
    static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Near(double a,double b,double tolerance=1e-8){Need(Math.Abs(a-b)<=tolerance,"Expected "+b+"; got "+a);}
    static void Reject<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    static void Test(string name,Action body){try{body();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
    static FloatFightRigRules Rig(double maximum=200,double minimumDrag=1,double maximumDrag=20,int drag=5,double stall=40,double payout=3,
        double minimumReel=.2,double maximumReel=1.6,int reel=5,double minimumLine=3,double spring=80,double damping=1,double rod=40)
    {return new FloatFightRigRules(spring,damping,rod,maximum,minimumDrag,maximumDrag,minimumReel,maximumReel,stall,payout,minimumLine,drag,reel);}
    static FloatFightFishRules Fish(double force=3,double drag=2,double fatigue=.003,double recovery=.01,double speed=10,FloatFightBehavior behavior=FloatFightBehavior.Outward,int decisions=20)
    {return new FloatFightFishRules(force,drag,decisions,fatigue,recovery,speed,behavior);}
    static FloatLandingRules Net(bool available=true,double distance=5,double depth=1,double speed=1,double energy=.5)
    {return new FloatLandingRules(available,distance,depth,speed,energy);}
    static FloatFightWater Water(){return new FloatFightWater(0,-2,-20,20,3,60);}
    static FloatFightSetup Setup(Guid? session=null,Guid? encounter=null,FloatBiteTarget target=null,FloatFightPoint start=null,double? outLine=null,long total=50000,
        FloatFightRigRules rig=null,FloatFightFishRules fish=null,FloatLandingRules landing=null,uint seed=91,FloatFightWater water=null)
    {
        var p=start??new FloatFightPoint(0,-.2,12);double distance=Math.Sqrt(p.X*p.X+(p.Y-1.5)*(p.Y-1.5)+p.Z*p.Z);
        return new FloatFightSetup(session??G(),encounter??G(),target??new FloatBiteTarget("carp",1000000),1,new FloatFightPoint(0,1.5,0),p,total,
            outLine??distance,water??Water(),rig??Rig(),fish??Fish(),landing??Net(),seed);
    }
    static FloatFightSimulation Fight(FloatFightSetup setup=null){return new FloatFightSimulation(setup??Setup());}
    static FloatFightSimulation Ready(){return Fight(Setup(start:new FloatFightPoint(0,-.2,3),landing:Net(energy:1),fish:Fish(behavior:FloatFightBehavior.Rest)));}
    static void Run(FloatFightSimulation f,int ticks)
    {for(int i=0;i<ticks;i++){var result=f.Advance(f.Snapshot.SessionId,50);Need(result.Accepted&&result.Snapshot.Phase!=FloatFightPhase.Faulted,"Simulation failed: "+result.Code);}}
    static void Same(FloatFightSnapshot a,FloatFightSnapshot b)
    {
        Need(a.SessionId==b.SessionId&&a.EncounterId==b.EncounterId&&a.Phase==b.Phase&&a.Reason==b.Reason&&a.LandingReason==b.LandingReason,"State/identity mismatch.");
        Need(a.StepIndex==b.StepIndex&&a.RemainderMilliseconds==b.RemainderMilliseconds&&a.Reeling==b.Reeling&&a.DragLevel==b.DragLevel&&a.ReelLevel==b.ReelLevel,"Clock/control mismatch.");
        foreach(var pair in new[]{new[]{a.Position.X,b.Position.X},new[]{a.Position.Y,b.Position.Y},new[]{a.Position.Z,b.Position.Z},new[]{a.Velocity.X,b.Velocity.X},new[]{a.Velocity.Z,b.Velocity.Z},
            new[]{a.OutLineM,b.OutLineM},new[]{a.SpoolRemainingM,b.SpoolRemainingM},new[]{a.SlackM,b.SlackM},new[]{a.TensionN,b.TensionN},new[]{a.Energy,b.Energy}})Near(pair[0],pair[1],0);
        Need(Object.ReferenceEquals(a.Target,b.Target),"Target changed.");
    }
    static int Main()
    {
        Test("hooked fish starts with no automatic winding and keeps the original locked target",()=>{
            var setup=Setup();var f=Fight(setup);var s=f.Snapshot;
            Need(!s.Reeling&&Object.ReferenceEquals(s.Target,setup.Target)&&s.SessionId==setup.SessionId&&s.EncounterId==setup.EncounterId&&s.StepIndex==0&&s.TensionN==0,"Fight changed target or inherited input.");
            Run(f,10);Need(f.Snapshot.ReeledThisStepM==0&&f.Snapshot.OutLineM>=s.OutLineM,"Unheld reel wound line.");
        });
        Test("50 ms accumulator and bounded calls are independent of rendering partitions",()=>{
            var setup=Setup(fish:Fish(behavior:FloatFightBehavior.Seeded));var a=Fight(setup);var b=Fight(setup);var id=setup.SessionId;
            a.SetReeling(id,setup.EncounterId,true);b.SetReeling(id,setup.EncounterId,true);
            for(int i=0;i<20;i++)a.Advance(id,250);for(int i=0;i<5000;i++)b.Advance(id,1);Same(a.Snapshot,b.Snapshot);
            a.Advance(id,49);Need(a.Snapshot.StepIndex==100&&a.Snapshot.RemainderMilliseconds==49,"Substep incorrectly advanced.");a.Advance(id,1);Need(a.Snapshot.StepIndex==101&&a.Snapshot.RemainderMilliseconds==0,"Full step was lost.");
        });
        Test("pause and clearing held input freeze all physical state and require a new press",()=>{
            var f=Fight();var s=f.Snapshot;f.SetReeling(s.SessionId,s.EncounterId,true);Run(f,10);f.ClearInput(s.SessionId);var paused=f.Snapshot;
            for(int i=0;i<100;i++){f.Advance(s.SessionId,0);Same(paused,f.Snapshot);}Run(f,1);Need(!f.Snapshot.Reeling&&f.Snapshot.ReeledThisStepM==0,"Resume inherited a press.");
        });
        Test("line out plus spool reserve stays conserved through winding and payout",()=>{
            var f=Fight(Setup(rig:Rig(drag:0),fish:Fish(behavior:FloatFightBehavior.Seeded)));var s=f.Snapshot;
            for(int i=0;i<1000;i++){if(i%80==0)f.SetReeling(s.SessionId,s.EncounterId,(i/80)%2==0);Run(f,1);var at=f.Snapshot;
                Near(at.OutLineM+at.SpoolRemainingM,50);Need(at.OutLineM>=3&&at.SpoolRemainingM>=0&&at.SlackM>=0,"Line ownership or range failed.");}
        });
        Test("outward drive moves the fish and produces physical tension with no reel input",()=>{
            var f=Fight();var before=f.Snapshot;Run(f,20);var after=f.Snapshot;
            Need(after.Position.Z>before.Position.Z&&after.TensionN>0&&after.RodDeflectionM>0&&!after.Reeling,"Drive did not interact with line and rod.");
        });
        Test("releasing the reel stops winding but does not subtract a fixed amount of tension",()=>{
            var f=Fight();var s=f.Snapshot;f.SetReeling(s.SessionId,s.EncounterId,true);Run(f,10);f.SetReeling(s.SessionId,s.EncounterId,false);Run(f,30);
            Need(f.Snapshot.ReeledThisStepM==0&&f.Snapshot.TensionN>1&&!f.Snapshot.Reeling,"Release incorrectly removes fish force.");
        });
        Test("lower drag begins payout no later under identical prescribed outward drive",()=>{
            var common=Setup();var low=Fight(Setup(common.SessionId,common.EncounterId,common.Target,rig:Rig(drag:0)));var high=Fight(Setup(common.SessionId,common.EncounterId,common.Target,rig:Rig(drag:10)));
            int lowAt=0,highAt=0;for(int i=1;i<=100;i++){Run(low,1);Run(high,1);if(lowAt==0&&low.Snapshot.PaidOutThisStepM>0)lowAt=i;if(highAt==0&&high.Snapshot.PaidOutThisStepM>0)highAt=i;}
            Need(lowAt>0&&(highAt==0||lowAt<=highAt),"Lower drag opened later.");Need(low.Snapshot.OutLineM>high.Snapshot.OutLineM,"Lower drag did not release more line in the controlled comparison.");
        });
        Test("rate-limited drag does not claim to clamp all tension to its threshold",()=>{
            var f=Fight(Setup(rig:Rig(drag:0,payout:.001),fish:Fish(force:15)));Run(f,60);var s=f.Snapshot;
            Need(s.PaidOutThisStepM>0&&s.PaidOutThisStepM<=.001*.05+1e-10&&s.TensionN>s.DragN,"Drag bypassed finite payout rate.");
        });
        Test("empty spool stops payout without inventing extra line or an automatic lost fish",()=>{
            var start=new FloatFightPoint(0,-.2,12);double distance=Math.Sqrt(144+1.7*1.7);long total=(long)Math.Ceiling(distance*1000);
            var f=Fight(Setup(start:start,total:total,rig:Rig(drag:0,payout:20)));Run(f,100);var s=f.Snapshot;
            Near(s.OutLineM,total/1000.0);Near(s.SpoolRemainingM,0);Need(s.TensionN>0&&s.Phase!=FloatFightPhase.Ended&&s.Phase!=FloatFightPhase.Faulted,"Spool end invented a loss.");
        });
        Test("higher selected speed winds faster under the same static load",()=>{
            var common=Setup();var low=Fight(Setup(common.SessionId,common.EncounterId,common.Target,rig:Rig(reel:0),fish:Fish(force:0,behavior:FloatFightBehavior.Rest)));
            var high=Fight(Setup(common.SessionId,common.EncounterId,common.Target,rig:Rig(reel:10),fish:Fish(force:0,behavior:FloatFightBehavior.Rest)));
            low.SetReeling(common.SessionId,common.EncounterId,true);high.SetReeling(common.SessionId,common.EncounterId,true);Run(low,20);Run(high,20);
            Need(high.Snapshot.OutLineM<low.Snapshot.OutLineM&&high.Snapshot.Position.Z<low.Snapshot.Position.Z,"Speed did not influence actual fish/line motion.");
        });
        Test("current-step reel stall rejects commanded winding under excessive load",()=>{
            var f=Fight(Setup(rig:Rig(stall:1,minimumDrag:100,maximumDrag:100,payout:0),fish:Fish(force:30)));Run(f,20);var s=f.Snapshot;Need(s.TensionN>1,"Fixture lacked load.");
            f.SetReeling(s.SessionId,s.EncounterId,true);Run(f,1);Need(f.Snapshot.ReeledThisStepM==0,"Stalled reel forcibly removed line.");
        });
        Test("an unsupported trial winding amount stalls instead of faulting an otherwise stationary fish",()=>{
            var f=Fight(Setup(rig:Rig(maximum:.1,minimumDrag:.02,maximumDrag:.03,stall:.05,payout:0,minimumReel:10,maximumReel:10,spring:10000,rod:10000),
                fish:Fish(force:0,behavior:FloatFightBehavior.Rest)));var s=f.Snapshot;f.SetReeling(s.SessionId,s.EncounterId,true);Run(f,1);
            Need(f.Snapshot.ReeledThisStepM==0&&f.Snapshot.Phase!=FloatFightPhase.Faulted,"Trial request became a false equipment fault.");Near(f.Snapshot.OutLineM,s.OutLineM);
        });
        Test("available drag payout can relieve a trial load above the solver envelope before publication",()=>{
            var f=Fight(Setup(rig:Rig(maximum:.2,minimumDrag:.1,maximumDrag:.1,stall:.1,payout:20),fish:Fish(force:20)));var before=f.Snapshot;Run(f,1);
            Need(f.Snapshot.PaidOutThisStepM>0&&f.Snapshot.OutLineM>before.OutLineM&&f.Snapshot.TensionN<=.2,"Relievable load was faulted before payout.");
        });
        Test("manual winding brings the fish to shore and eligibility still needs an explicit landing",()=>{
            var f=Fight(Setup(rig:Rig(reel:10),fish:Fish(force:0,fatigue:.05,behavior:FloatFightBehavior.Rest)));var s=f.Snapshot;
            f.SetReeling(s.SessionId,s.EncounterId,true);Run(f,300);var near=f.Snapshot;
            Need(near.Position.Z<s.Position.Z&&near.Phase==FloatFightPhase.LandingEligible&&near.Energy<.5,"Physical winding did not make fish eligible: "+near.LandingReason);
            Need(f.Advance(s.SessionId,0).LandingTicket==null,"Eligibility automatically created a landing.");
        });
        Test("fatigue follows drive and load instead of a wall-clock battle countdown",()=>{
            var rest=Fight(Setup(fish:Fish(force:0,behavior:FloatFightBehavior.Rest)));var work=Fight(Setup(fish:Fish(fatigue:.02)));
            Run(rest,100);Run(work,100);Near(rest.Snapshot.Energy,1);Need(work.Snapshot.Energy<rest.Snapshot.Energy&&work.Snapshot.Energy>=0,"No work-dependent fatigue.");
        });
        Test("bounded water motion retains depth and never crosses the configured bank or sides",()=>{
            var water=new FloatFightWater(0,-2,-.15,.15,3,12.2);var f=Fight(Setup(water:water,fish:Fish(behavior:FloatFightBehavior.Seeded)));
            Run(f,500);var s=f.Snapshot;Need(water.Contains(s.Position)&&s.Position.Y==-.2&&s.Velocity.Y==0,"Motion left supported water plane.");
        });
        Test("large trial line force cannot create a false bracket failure by crossing the projected anchor",()=>{
            var f=Fight(Setup(target:new FloatBiteTarget("carp",10000),start:new FloatFightPoint(5,-.2,12),rig:Rig(spring:10000,rod:10000,payout:0)));
            Run(f,100);Need(f.Snapshot.TensionN<200&&f.Snapshot.Phase!=FloatFightPhase.Faulted,"Valid small-fish high-stiffness root was lost.");
        });
        Test("the proposed single-water carp and crucian tuning remains finite and manually landable across 100 seeds",()=>{
            int minimumSteps=Int32.MaxValue,maximumSteps=0;
            for(uint seed=0;seed<100;seed++){
                bool carp=seed%2==0;long mass=carp?300000+(long)((seed/2)%16)*100000:100000+(long)((seed/2)%12)*50000;
                var start=new FloatFightPoint(((int)(seed%3)-1)*5,-.2,6+seed%37);
                var f=Fight(Setup(target:new FloatBiteTarget(carp?"carp":"crucian",mass),start:start,rig:Rig(reel:10),
                    fish:Fish(force:carp?3:2,fatigue:.05,recovery:.005,behavior:FloatFightBehavior.Seeded),seed:seed,
                    water:new FloatFightWater(0,-2,-40,40,3,80)));var initial=f.Snapshot;
                f.SetReeling(initial.SessionId,initial.EncounterId,true);int steps=0;
                while(f.Snapshot.Phase!=FloatFightPhase.LandingEligible&&steps<3600){Run(f,1);var s=f.Snapshot;Near(s.OutLineM+s.SpoolRemainingM,50);
                    Need(s.Energy>=0&&s.Energy<=1&&s.TensionN>=0&&s.TensionN<=200,"Envelope violated at seed "+seed);steps++;}
                Need(f.Snapshot.Phase==FloatFightPhase.LandingEligible,"Manual landing never became eligible at seed "+seed+": "+f.Snapshot.LandingReason);
                Need(f.Advance(initial.SessionId,0).LandingTicket==null,"Sweep silently awarded a fish.");
                minimumSteps=Math.Min(minimumSteps,steps);maximumSteps=Math.Max(maximumSteps,steps);
            }
            Console.WriteLine("SWEEP 100 cases; earliest eligibility "+minimumSteps+" steps; latest "+maximumSteps+" steps; explicit landing still required");
        });
        Test("landing requires separately configured net distance depth speed and energy conditions",()=>{
            var close=new FloatFightPoint(0,-.2,3);
            Need(Fight(Setup(start:close,landing:Net(available:false,energy:1))).Snapshot.LandingReason=="net-unavailable","Missing net ignored.");
            Need(Fight(Setup(landing:Net(energy:1))).Snapshot.LandingReason=="fish-too-far","Distance ignored.");
            Need(Fight(Setup(start:close,landing:Net(depth:.1,energy:1))).Snapshot.LandingReason=="fish-too-deep","Depth ignored.");
            Need(Fight(Setup(start:close)).Snapshot.LandingReason=="fish-active","Energy ignored.");
            var moving=Fight(Setup(start:close,outLine:10,landing:Net(speed:0,energy:1)));Run(moving,1);Need(moving.Snapshot.LandingReason=="fish-moving","Speed ignored.");
        });
        Test("a fish leaving landing range returns to fighting without an automatic catch",()=>{
            var f=Fight(Setup(start:new FloatFightPoint(0,-.2,3),outLine:10,landing:Net(distance:3.5,speed:10,energy:1)));Need(f.Snapshot.Phase==FloatFightPhase.LandingEligible,"Fixture not eligible.");Run(f,20);
            Need(f.Snapshot.Phase==FloatFightPhase.Fighting&&f.Snapshot.LandingReason=="fish-too-far","Lost qualification remained latched.");Need(!f.TryLand(f.Snapshot.SessionId,f.Snapshot.EncounterId,G()).Accepted,"Out-of-range action landed fish.");
        });
        Test("landing rejects empty reused and mismatched identities without affecting the fish",()=>{
            var f=Ready();var s=f.Snapshot;foreach(Guid id in new[]{Guid.Empty,s.SessionId,s.EncounterId})Need(!f.TryLand(s.SessionId,s.EncounterId,id).Accepted,"Invalid landing identity accepted.");
            Need(!f.TryLand(G(),s.EncounterId,G()).Accepted&&!f.TryLand(s.SessionId,G(),G()).Accepted,"Old session or encounter accepted.");Same(s,f.Snapshot);
        });
        Test("eligible explicit landing emits one opaque ticket and freezes before saving",()=>{
            var f=Ready();var s=f.Snapshot;f.SetReeling(s.SessionId,s.EncounterId,true);Guid landing=G();var r=f.TryLand(s.SessionId,s.EncounterId,landing);
            Need(r.Accepted&&r.ShouldCommitLanding&&!r.TerminalEvent&&r.LandingAttempt==1&&r.LandingTicket.LandingId==landing&&r.Snapshot.Phase==FloatFightPhase.LandingPending&&!r.Snapshot.Reeling,"Submission state incorrect.");
            Need(!f.Advance(s.SessionId,250).Accepted&&!f.SetReeling(s.SessionId,s.EncounterId,true).Accepted&&!f.End(s.SessionId).Accepted,"Pending save allowed movement or abandonment.");
        });
        Test("repeat landing returns the same ticket without a second submit and other identities are refused",()=>{
            var f=Ready();var s=f.Snapshot;Guid id=G();var first=f.TryLand(s.SessionId,s.EncounterId,id);var repeat=f.TryLand(s.SessionId,s.EncounterId,id);
            Need(repeat.Accepted&&!repeat.ShouldCommitLanding&&Object.ReferenceEquals(first.LandingTicket,repeat.LandingTicket),"Landing duplicated ticket or submit.");Need(!f.TryLand(s.SessionId,s.EncounterId,G()).Accepted,"Changed landing identity accepted while pending.");
        });
        Test("a forged equal-value ticket cannot confirm the actual landing",()=>{
            var f=Ready();var s=f.Snapshot;var r=f.TryLand(s.SessionId,s.EncounterId,G());var fake=new FloatLandingTicket(r.LandingTicket.LandingId,s.SessionId,s.EncounterId);
            Need(!f.ConfirmLanding(fake,1,FloatLandingOutcome.Landed).Accepted&&!f.RetryLanding(fake).Accepted&&f.Snapshot.Phase==FloatFightPhase.LandingPending,"Structural equality granted save authority.");
        });
        Test("only confirmed landed status emits a final success and never twice",()=>{
            var f=Ready();var s=f.Snapshot;var pending=f.TryLand(s.SessionId,s.EncounterId,G());var done=f.ConfirmLanding(pending.LandingTicket,1,FloatLandingOutcome.Landed);
            Need(done.Accepted&&done.TerminalEvent&&done.Snapshot.Phase==FloatFightPhase.Landed&&Object.ReferenceEquals(s.Target,done.Snapshot.Target),"Confirmation lost target or failed.");
            Need(!f.ConfirmLanding(pending.LandingTicket,1,FloatLandingOutcome.Landed).Accepted&&!f.RetryLanding(pending.LandingTicket).Accepted&&!f.End(s.SessionId).TerminalEvent,"Confirmed result emitted twice.");
        });
        Test("confirmed full-creel result stays frozen for the owner's explicit release path",()=>{
            var f=Ready();var s=f.Snapshot;var pending=f.TryLand(s.SessionId,s.EncounterId,G());var blocked=f.ConfirmLanding(pending.LandingTicket,1,FloatLandingOutcome.CapacityBlocked);
            Need(blocked.Accepted&&!blocked.TerminalEvent&&blocked.Snapshot.Phase==FloatFightPhase.CapacityBlocked&&!f.RetryLanding(pending.LandingTicket).Accepted&&!f.Advance(s.SessionId,50).Accepted,"Capacity result retried or resumed.");
            Need(f.End(s.SessionId).TerminalEvent&&!f.End(s.SessionId).TerminalEvent,"Explicit terminal notification not unique.");
        });
        Test("known unwritten result retries the same identity and ignores a delayed older confirmation",()=>{
            var f=Ready();var s=f.Snapshot;var first=f.TryLand(s.SessionId,s.EncounterId,G());Need(f.ConfirmLanding(first.LandingTicket,1,FloatLandingOutcome.KnownNotWritten).Accepted,"Known failure rejected.");
            var retry=f.RetryLanding(first.LandingTicket);Need(retry.Accepted&&retry.ShouldCommitLanding&&retry.LandingAttempt==2&&Object.ReferenceEquals(first.LandingTicket,retry.LandingTicket),"Retry replaced identity.");
            Need(!f.ConfirmLanding(first.LandingTicket,1,FloatLandingOutcome.KnownNotWritten).Accepted&&!f.RetryLanding(first.LandingTicket).Accepted,"Old failure unlocked another active submission.");
            Need(f.ConfirmLanding(first.LandingTicket,2,FloatLandingOutcome.Landed).TerminalEvent,"Current attempt did not confirm.");
        });
        Test("unknown save result blocks replay retry abandonment and invented rewards",()=>{
            var f=Ready();var s=f.Snapshot;var p=f.TryLand(s.SessionId,s.EncounterId,G());var unknown=f.ConfirmLanding(p.LandingTicket,1,FloatLandingOutcome.Unknown);
            Need(unknown.Accepted&&!unknown.TerminalEvent&&unknown.Snapshot.Phase==FloatFightPhase.Faulted&&unknown.Code=="landing-save-unknown","Unknown was treated as normal failure.");
            Need(!f.RetryLanding(p.LandingTicket).Accepted&&!f.End(s.SessionId).Accepted&&!f.Advance(s.SessionId,50).Accepted&&!f.ConfirmLanding(p.LandingTicket,1,FloatLandingOutcome.Landed).Accepted,"Unknown save escaped its fault boundary.");
        });
        Test("unsupported force becomes a diagnostic fault without pretending equipment broke",()=>{
            var f=Fight(Setup(rig:Rig(maximum:.2,minimumDrag:.1,maximumDrag:.1,stall:.1,payout:0),fish:Fish(force:20)));var original=f.Snapshot;
            for(int i=0;i<100&&f.Snapshot.Phase!=FloatFightPhase.Faulted;i++)f.Advance(original.SessionId,50);
            Need(f.Snapshot.Phase==FloatFightPhase.Faulted&&f.Snapshot.Reason=="load-outside-supported-range"&&!f.Snapshot.Reeling,"Unsupported load was not faulted.");
            Near(f.Snapshot.OutLineM+f.Snapshot.SpoolRemainingM,50);Need(f.Snapshot.Target==original.Target&&!f.Advance(original.SessionId,250).Accepted,"Fault mutated ownership or resumed.");
        });
        Test("unsupported speed faults before publishing a nonfinite or excessive motion",()=>{
            var f=Fight(Setup(outLine:20,fish:Fish(force:20,speed:.01)));var before=f.Snapshot;f.Advance(before.SessionId,50);
            Need(f.Snapshot.Phase==FloatFightPhase.Faulted&&f.Snapshot.Reason=="speed-outside-supported-range","Speed envelope ignored.");Near(f.Snapshot.Position.Z,before.Position.Z);Near(f.Snapshot.OutLineM,before.OutLineM);
        });
        Test("old sessions and encounters cannot mutate controls geometry or end a current fight",()=>{
            var f=Fight();var s=f.Snapshot;Need(!f.Advance(G(),250).Accepted&&!f.SetReeling(s.SessionId,G(),true).Accepted&&!f.SetDragLevel(G(),s.EncounterId,1).Accepted&&
                !f.SetReelLevel(s.SessionId,G(),1).Accepted&&!f.ClearInput(G()).Accepted&&!f.End(G()).Accepted,"Old authority accepted.");Same(s,f.Snapshot);
        });
        Test("ending a known live or known-unwritten attempt emits one nonreward terminal event",()=>{
            var live=Fight();var s=live.Snapshot;Need(live.End(s.SessionId).TerminalEvent&&!live.End(s.SessionId).TerminalEvent&&!live.Advance(s.SessionId,50).Accepted,"End was not final.");
            var pending=Ready();s=pending.Snapshot;var p=pending.TryLand(s.SessionId,s.EncounterId,G());pending.ConfirmLanding(p.LandingTicket,1,FloatLandingOutcome.KnownNotWritten);
            Need(pending.End(s.SessionId).TerminalEvent&&!pending.RetryLanding(p.LandingTicket).Accepted,"Known unwritten attempt could not be explicitly ended.");
        });
        Test("missing invalid and out-of-range parameters reject before simulation",()=>{
            Reject<ArgumentNullException>(()=>new FloatFightSimulation(null));Reject<ArgumentException>(()=>Setup(session:Guid.Empty));Guid same=G();Reject<ArgumentException>(()=>Setup(same,same));
            Reject<ArgumentException>(()=>Setup(target:new FloatBiteTarget("carp",1)));Reject<ArgumentException>(()=>Setup(start:new FloatFightPoint(0,1,12)));
            Reject<ArgumentException>(()=>Setup(outLine:3));Reject<ArgumentOutOfRangeException>(()=>Setup(total:1));Reject<ArgumentOutOfRangeException>(()=>Rig(spring:Double.NaN));
            Reject<ArgumentOutOfRangeException>(()=>Rig(drag:11));Reject<ArgumentOutOfRangeException>(()=>Rig(payout:-1));Reject<ArgumentOutOfRangeException>(()=>Fish(force:Double.PositiveInfinity));
            Reject<ArgumentOutOfRangeException>(()=>Fish(decisions:0));Reject<ArgumentOutOfRangeException>(()=>Net(energy:1.1));Reject<ArgumentException>(()=>new FloatFightWater(0,0,-1,1,3,4));
            var f=Fight();var s=f.Snapshot;Reject<ArgumentOutOfRangeException>(()=>f.Advance(s.SessionId,251));Reject<ArgumentOutOfRangeException>(()=>f.Advance(s.SessionId,-1));
            Reject<ArgumentOutOfRangeException>(()=>f.SetDragLevel(s.SessionId,s.EncounterId,-1));Reject<ArgumentOutOfRangeException>(()=>f.SetReelLevel(s.SessionId,s.EncounterId,11));
        });
        Test("all simulation and landing access remains on its creator thread",()=>{
            var f=Ready();var s=f.Snapshot;int rejected=0;Exception failure=null;
            var worker=new Thread(()=>{try{foreach(Action action in new Action[]{()=>{var ignored=f.Snapshot;},()=>f.Advance(s.SessionId,50),()=>f.SetReeling(s.SessionId,s.EncounterId,true),
                ()=>f.SetDragLevel(s.SessionId,s.EncounterId,1),()=>f.SetReelLevel(s.SessionId,s.EncounterId,1),()=>f.ClearInput(s.SessionId),()=>f.TryLand(s.SessionId,s.EncounterId,G()),
                ()=>f.RetryLanding(null),()=>f.ConfirmLanding(null,1,FloatLandingOutcome.Landed),()=>f.End(s.SessionId)}){try{action();}catch(InvalidOperationException){rejected++;}}}catch(Exception error){failure=error;}});
            worker.Start();worker.Join();Need(failure==null&&rejected==10&&f.Snapshot.StepIndex==0,"Cross-thread model access accepted.");
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
}
