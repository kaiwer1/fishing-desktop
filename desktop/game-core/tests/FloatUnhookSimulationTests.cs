using System;
using System.Threading;
using FishingDesktop.GameCore;

static class FloatUnhookSimulationTests
{
    static int passed,failed,nextId;
    static Guid G(){return new Guid(++nextId,0,0,new byte[]{0,0,0,0,0,0,0,27});}
    static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static void Near(double a,double b){Need(Math.Abs(a-b)<1e-9,"Expected "+b+"; got "+a);}
    static void Reject<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    static void Test(string name,Action body){try{body();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
    static FloatFightSetup Setup(FloatUnhookRules rules=null,bool enabled=true,double slack=1,double force=0,double maximumSpeed=10,
        FloatFightBehavior behavior=FloatFightBehavior.Rest,uint seed=71)
    {
        var start=new FloatFightPoint(0,-.2,12);double distance=Math.Sqrt(144+1.7*1.7);
        return new FloatFightSetup(G(),G(),new FloatBiteTarget("carp",1000000),1,new FloatFightPoint(0,1.5,0),start,50000,distance+slack,
            new FloatFightWater(0,-2,-20,20,3,60),new FloatFightRigRules(80,1,40,200,1,20,10,10,40,3,3,5,5),
            new FloatFightFishRules(force,2,20,.003,.01,maximumSpeed,behavior),new FloatLandingRules(true,50,1,50,1),seed,
            enabled?(rules??new FloatUnhookRules(.2,4,1)):null);
    }
    static FloatFightSimulation Fight(FloatFightSetup setup=null){return new FloatFightSimulation(setup??Setup());}
    static FloatFightResult Tick(FloatFightSimulation f,int milliseconds=50){return f.Advance(f.Snapshot.SessionId,milliseconds);}
    static FloatFightResult Mature(FloatFightSimulation f=null)
    {
        if(f==null)f=Fight();FloatFightResult result=null;
        for(int i=0;i<15000;i++){result=Tick(f,250);if(result.ShouldCommitFailure)return result;Need(result.Accepted&&result.Snapshot.Phase!=FloatFightPhase.Faulted,"Fixture did not mature: "+result.Code);}
        throw new InvalidOperationException("Fixture did not reach a failure.");
    }
    static void Same(FloatFightSnapshot a,FloatFightSnapshot b)
    {
        Need(a.SessionId==b.SessionId&&a.EncounterId==b.EncounterId&&Object.ReferenceEquals(a.Target,b.Target),"Locked target changed.");
        Need(a.Phase==b.Phase&&a.Reason==b.Reason&&a.StepIndex==b.StepIndex&&a.RemainderMilliseconds==b.RemainderMilliseconds&&a.Reeling==b.Reeling,"Phase or clock changed.");
        Need(a.UnhookExposureSteps==b.UnhookExposureSteps&&a.FailureAttempt==b.FailureAttempt,"Failure chronology changed.");
        foreach(var pair in new[]{new[]{a.Position.X,b.Position.X},new[]{a.Position.Z,b.Position.Z},new[]{a.Velocity.X,b.Velocity.X},new[]{a.Velocity.Z,b.Velocity.Z},
            new[]{a.OutLineM,b.OutLineM},new[]{a.SpoolRemainingM,b.SpoolRemainingM},new[]{a.SlackM,b.SlackM},new[]{a.TensionN,b.TensionN},new[]{a.Energy,b.Energy}})
            Need(pair[0]==pair[1],"Physical state changed.");
    }
    static int Main()
    {
        Test("rules reject nonfinite or nonpositive slack and unbounded exposure or recovery",()=>{
            foreach(double value in new[]{Double.NaN,Double.PositiveInfinity,Double.NegativeInfinity,0,-1,.0001,1000.1})Reject<ArgumentOutOfRangeException>(()=>new FloatUnhookRules(value,1,1));
            foreach(int value in new[]{Int32.MinValue,0,12001,Int32.MaxValue}){
                Reject<ArgumentOutOfRangeException>(()=>new FloatUnhookRules(.1,value,1));Reject<ArgumentOutOfRangeException>(()=>new FloatUnhookRules(.1,1,value));}
            var valid=new FloatUnhookRules(.001,12000,12000);Need(valid.ExposureSteps==12000&&valid.RecoveryStepsPerTick==12000,"Bounded configuration changed.");
        });
        Test("version two has no hidden default and disabled legacy setup never accumulates exposure",()=>{
            var setup=Setup(enabled:false);var f=Fight(setup);Need(FloatFightSimulation.ModelVersion==2&&setup.Unhook==null,"Implicit rules installed.");
            for(int i=0;i<200;i++){var r=Tick(f,250);Need(r.Accepted&&!r.ShouldCommitFailure&&r.FailureTicket==null,"Disabled rules failed.");}
            Need(f.Snapshot.UnhookExposureSteps==0&&f.Snapshot.FailureAttempt==0,"Disabled rules accumulated exposure.");
        });
        Test("initial slack and partial steps do not mature before the first full active step",()=>{
            var f=Fight(Setup(rules:new FloatUnhookRules(.2,1,1)));Need(f.Snapshot.SlackM>.2&&f.Snapshot.UnhookExposureSteps==0,"Construction advanced the model.");
            var r=Tick(f,49);Need(!r.ShouldCommitFailure&&r.Snapshot.UnhookExposureSteps==0&&r.Snapshot.StepIndex==0,"Partial step counted.");
            r=Tick(f,1);Need(r.ShouldCommitFailure&&r.Snapshot.StepIndex==1&&r.Snapshot.UnhookExposureSteps==1&&r.Snapshot.RemainderMilliseconds==0,"Exact step did not mature.");
        });
        Test("slack threshold is inclusive and strictly smaller slack recovers",()=>{
            var basis=Fight(Setup()).Snapshot.SlackM;
            var exact=Fight(Setup(rules:new FloatUnhookRules(basis,1,1)));var below=Fight(Setup(rules:new FloatUnhookRules(basis+1e-6,1,1)));
            Need(Tick(exact).ShouldCommitFailure,"Exact threshold excluded.");Need(!Tick(below).ShouldCommitFailure&&below.Snapshot.UnhookExposureSteps==0,"Below threshold counted.");
        });
        Test("failure step identity and physics are independent of rendering partitions",()=>{
            var setup=Setup(rules:new FloatUnhookRules(.2,19,2),force:1,behavior:FloatFightBehavior.Seeded);var a=Fight(setup);var b=Fight(setup);
            FloatFightResult ar=null,br=null;int asends=0,bsends=0;
            for(int i=0;i<4;i++){ar=Tick(a,250);if(ar.ShouldCommitFailure)asends++;}
            for(int i=0;i<1000;i++){var result=Tick(b,1);if(result.ShouldCommitFailure){bsends++;br=result;}}
            Same(a.Snapshot,b.Snapshot);Need(asends==1&&bsends==1&&ar.FailureTicket.FailureId==br.FailureTicket.FailureId,"Partition changed failure identity or submissions.");
            Need(a.Snapshot.StepIndex==19&&a.Snapshot.RemainderMilliseconds==0,"Mature step consumed later active time.");
        });
        Test("pause preserves exposure and accumulator without reset or hidden catch-up",()=>{
            var f=Fight();Tick(f,149);var before=f.Snapshot;Need(before.UnhookExposureSteps==2&&before.RemainderMilliseconds==49,"Fixture chronology wrong.");
            for(int i=0;i<100;i++){Need(!Tick(f,0).ShouldCommitFailure,"Paused failure.");Same(before,f.Snapshot);}
            Tick(f,1);Need(f.Snapshot.UnhookExposureSteps==3&&f.Snapshot.StepIndex==3,"Resume reset exposure or accumulator.");
            Need(Tick(f).ShouldCommitFailure,"Resume missed final exposure step.");
        });
        Test("taking up actual slack decays exposure gradually instead of resetting it",()=>{
            var f=Fight(Setup(rules:new FloatUnhookRules(.1,20,2),slack:.25));Tick(f,250);Need(f.Snapshot.UnhookExposureSteps==5,"Fixture did not accumulate.");
            var s=f.Snapshot;f.SetReeling(s.SessionId,s.EncounterId,true);Tick(f);Need(f.Snapshot.SlackM<.1&&f.Snapshot.UnhookExposureSteps==3,"Tightening did not subtract configured recovery.");
            Tick(f);Need(f.Snapshot.UnhookExposureSteps==1,"Recovery reset or accumulated.");Tick(f);Need(f.Snapshot.UnhookExposureSteps==0,"Recovery fell below zero.");
        });
        Test("recovery larger than accumulated exposure clamps at zero",()=>{
            var f=Fight(Setup(rules:new FloatUnhookRules(.1,20,12000),slack:.25));Tick(f,150);var s=f.Snapshot;
            f.SetReeling(s.SessionId,s.EncounterId,true);Tick(f);Need(f.Snapshot.UnhookExposureSteps==0,"Recovery underflowed.");
        });
        Test("released reel under outward fish tension does not create artificial slack risk",()=>{
            var f=Fight(Setup(slack:0,force:3,behavior:FloatFightBehavior.Outward));
            for(int i=0;i<100;i++){var r=Tick(f);Need(r.Accepted&&!r.ShouldCommitFailure&&r.Snapshot.UnhookExposureSteps==0,"Release became a timeout failure.");}
            Need(!f.Snapshot.Reeling&&f.Snapshot.TensionN>0,"Fixture was not a connected pulling fish.");
        });
        Test("authority loss clears held input without an immediate failure or exposure reset",()=>{
            var f=Fight();Tick(f,100);var s=f.Snapshot;f.SetReeling(s.SessionId,s.EncounterId,true);
            var clear=f.ClearInput(s.SessionId);Need(clear.Accepted&&!clear.ShouldCommitFailure&&!clear.Snapshot.Reeling&&clear.Snapshot.UnhookExposureSteps==2,"Clear input advanced or reset risk.");
            var paused=f.Snapshot;for(int i=0;i<100;i++){Tick(f,0);Same(paused,f.Snapshot);}Tick(f);Need(f.Snapshot.UnhookExposureSteps==3&&!f.Snapshot.Reeling,"Resume inherited held input or instantly matured.");
        });
        Test("unsupported physical speed wins over a same-step mature slack failure",()=>{
            var f=Fight(Setup(rules:new FloatUnhookRules(.2,1,1),slack:10,force:20,maximumSpeed:.01,behavior:FloatFightBehavior.Outward));
            var r=Tick(f);Need(r.Snapshot.Phase==FloatFightPhase.Faulted&&r.Code=="speed-outside-supported-range"&&!r.ShouldCommitFailure&&r.FailureTicket==null&&r.Snapshot.UnhookExposureSteps==0,"Fault was disguised as unhook.");
        });
        Test("mature slack takes precedence over landing eligibility on the same step",()=>{
            var f=Fight(Setup(rules:new FloatUnhookRules(.2,1,1)));Need(f.Snapshot.Phase==FloatFightPhase.LandingEligible,"Fixture not in net envelope.");
            var r=Tick(f);Need(r.ShouldCommitFailure&&!r.ShouldCommitLanding&&r.Snapshot.Phase==FloatFightPhase.FailurePending,"Landing won a mature failure.");
            Need(!f.TryLand(r.Snapshot.SessionId,r.Snapshot.EncounterId,G()).Accepted,"Detached fish could be landed.");
        });
        Test("failure pending freezes physics and refuses new reel drag speed or landing actions",()=>{
            var f=Fight();var r=Mature(f);var before=f.Snapshot;
            Need(!Tick(f,250).Accepted&&!f.SetReeling(before.SessionId,before.EncounterId,true).Accepted&&!f.SetDragLevel(before.SessionId,before.EncounterId,1).Accepted&&
                !f.SetReelLevel(before.SessionId,before.EncounterId,1).Accepted&&!f.TryLand(before.SessionId,before.EncounterId,G()).Accepted,"Pending accepted new gameplay.");
            Same(before,f.Snapshot);Need(!r.Snapshot.Reeling&&r.Snapshot.RemainderMilliseconds==0,"Pending retained input or time.");
        });
        Test("one initial failure submission preserves the same fish target and conserved line",()=>{
            var setup=Setup();var f=Fight(setup);var r=Mature(f);Need(r.ShouldCommitFailure&&!r.TerminalEvent&&!r.ShouldCommitLanding&&r.FailureAttempt==1,"Initial failure did not request exactly one commit.");
            Need(r.FailureTicket.SessionId==setup.SessionId&&r.FailureTicket.EncounterId==setup.EncounterId&&r.FailureTicket.Reason=="slack-unhooked"&&
                r.FailureTicket.FailureId!=Guid.Empty&&r.FailureTicket.FailureId!=setup.SessionId&&r.FailureTicket.FailureId!=setup.EncounterId,"Failure identity changed.");
            Need(Object.ReferenceEquals(r.Snapshot.Target,setup.Target),"Failure rerolled the target.");Near(r.Snapshot.OutLineM+r.Snapshot.SpoolRemainingM,50);
            for(int i=0;i<10;i++)Need(!Tick(f).ShouldCommitFailure&&!f.ClearInput(setup.SessionId).ShouldCommitFailure,"Repeated submission without explicit retry.");
        });
        Test("forged equal-value ticket and null ticket cannot confirm or retry failure",()=>{
            var f=Fight();var r=Mature(f);var t=r.FailureTicket;var forged=new FloatFailureTicket(t.FailureId,t.SessionId,t.EncounterId,t.Reason);
            Need(!f.ConfirmFailure(forged,1,FloatFailureOutcome.Confirmed).Accepted&&!f.RetryFailure(forged).Accepted&&
                !f.ConfirmFailure(null,1,FloatFailureOutcome.Confirmed).Accepted&&!f.RetryFailure(null).Accepted,"Ticket reference authority bypassed.");
            Need(f.ConfirmFailure(t,1,FloatFailureOutcome.Confirmed).Accepted,"Real ticket lost authority.");
        });
        Test("ticket from another runtime is stale even with the same deterministic failure identity",()=>{
            var setup=Setup();var a=Fight(setup);var b=Fight(setup);var ar=Mature(a);var br=Mature(b);Need(ar.FailureTicket.FailureId==br.FailureTicket.FailureId,"Fixture identities differ.");
            Need(!b.ConfirmFailure(ar.FailureTicket,1,FloatFailureOutcome.Confirmed).Accepted&&!b.RetryFailure(ar.FailureTicket).Accepted,"Old runtime callback accepted.");
            Need(b.ConfirmFailure(br.FailureTicket,1,FloatFailureOutcome.Confirmed).Accepted,"Current runtime callback rejected.");
        });
        Test("confirmed failure emits one terminal event and late repeats cannot grant or mutate anything",()=>{
            var f=Fight();var r=Mature(f);var confirmed=f.ConfirmFailure(r.FailureTicket,1,FloatFailureOutcome.Confirmed);
            Need(confirmed.Accepted&&confirmed.TerminalEvent&&confirmed.Snapshot.Phase==FloatFightPhase.Escaped&&!confirmed.ShouldCommitFailure&&!confirmed.ShouldCommitLanding,"Failure confirmation did not terminate.");
            Need(!f.ConfirmFailure(r.FailureTicket,1,FloatFailureOutcome.Confirmed).Accepted&&!f.RetryFailure(r.FailureTicket).Accepted,"Confirmed failure repeated.");
            Need(!f.End(confirmed.Snapshot.SessionId).TerminalEvent&&!f.End(confirmed.Snapshot.SessionId).TerminalEvent,"End duplicated terminal event.");
        });
        Test("known not written permits only an explicit retry with the same opaque ticket",()=>{
            var f=Fight();var r=Mature(f);var known=f.ConfirmFailure(r.FailureTicket,1,FloatFailureOutcome.KnownNotWritten);
            Need(known.Accepted&&!known.TerminalEvent&&!known.ShouldCommitFailure&&known.Snapshot.Phase==FloatFightPhase.FailurePending,"Known failure terminated or retried.");
            Need(!Tick(f,250).ShouldCommitFailure&&!f.ConfirmFailure(r.FailureTicket,1,FloatFailureOutcome.Confirmed).Accepted,"Known not written silently retried.");
            var retry=f.RetryFailure(r.FailureTicket);Need(retry.Accepted&&retry.ShouldCommitFailure&&retry.FailureAttempt==2&&Object.ReferenceEquals(retry.FailureTicket,r.FailureTicket),"Explicit retry changed operation identity.");
            Need(!f.RetryFailure(r.FailureTicket).Accepted,"Concurrent retry accepted.");Need(f.ConfirmFailure(r.FailureTicket,2,FloatFailureOutcome.Confirmed).TerminalEvent,"Retry did not confirm.");
        });
        Test("late earlier attempts cannot confirm fail or unlock a newer submitted attempt",()=>{
            var f=Fight();var r=Mature(f);f.ConfirmFailure(r.FailureTicket,1,FloatFailureOutcome.KnownNotWritten);f.RetryFailure(r.FailureTicket);
            foreach(var outcome in new[]{FloatFailureOutcome.Confirmed,FloatFailureOutcome.KnownNotWritten,FloatFailureOutcome.Unknown})Need(!f.ConfirmFailure(r.FailureTicket,1,outcome).Accepted,"Earlier callback changed retry state.");
            Need(!f.RetryFailure(r.FailureTicket).Accepted&&f.Snapshot.FailureAttempt==2,"Earlier callback unlocked retry.");Need(f.ConfirmFailure(r.FailureTicket,2,FloatFailureOutcome.Confirmed).TerminalEvent,"Current callback lost authority.");
        });
        Test("unknown write result permanently locks retry end landing and late confirmations",()=>{
            var f=Fight();var r=Mature(f);var unknown=f.ConfirmFailure(r.FailureTicket,1,FloatFailureOutcome.Unknown);var before=f.Snapshot;
            Need(unknown.Accepted&&!unknown.TerminalEvent&&before.Phase==FloatFightPhase.FailurePending&&before.Reason=="failure-save-unknown","Unknown became known failure.");
            Need(!f.RetryFailure(r.FailureTicket).Accepted&&!f.End(before.SessionId).Accepted&&!f.TryLand(before.SessionId,before.EncounterId,G()).Accepted&&!Tick(f).Accepted,"Unknown was unlocked.");
            foreach(var outcome in new[]{FloatFailureOutcome.Confirmed,FloatFailureOutcome.KnownNotWritten,FloatFailureOutcome.Unknown})Need(!f.ConfirmFailure(r.FailureTicket,1,outcome).Accepted,"Unknown accepted a late decision.");
            Same(before,f.Snapshot);
        });
        Test("pending failure cannot be abandoned before or after a known prewrite failure",()=>{
            var f=Fight();var r=Mature(f);Need(!f.End(r.Snapshot.SessionId).Accepted,"Pending commit bypassed.");
            f.ConfirmFailure(r.FailureTicket,1,FloatFailureOutcome.KnownNotWritten);Need(!f.End(r.Snapshot.SessionId).Accepted,"Known not written bypassed required terminal transaction.");
        });
        Test("landing requested before maturity disconnects exposure from later active time",()=>{
            var f=Fight();Tick(f,150);var s=f.Snapshot;var land=f.TryLand(s.SessionId,s.EncounterId,G());Need(land.ShouldCommitLanding&&s.UnhookExposureSteps==3,"Fixture did not request eligible landing.");
            for(int i=0;i<20;i++){var r=Tick(f,250);Need(!r.ShouldCommitFailure&&r.Snapshot.UnhookExposureSteps==3,"Landing pending accrued slack.");}
            var confirmed=f.ConfirmLanding(land.LandingTicket,land.LandingAttempt,FloatLandingOutcome.Landed);Need(confirmed.TerminalEvent&&!confirmed.ShouldCommitFailure&&confirmed.Snapshot.Phase==FloatFightPhase.Landed,"Landing was replaced by late failure.");
        });
        Test("explicit end before maturity is terminal and cannot create a later failure",()=>{
            var f=Fight();Tick(f,150);var ended=f.End(f.Snapshot.SessionId);Need(ended.TerminalEvent&&!ended.ShouldCommitFailure,"End did not terminate once.");
            Need(!Tick(f,250).Accepted&&f.Snapshot.FailureAttempt==0&&f.Snapshot.UnhookExposureSteps==3,"Ended hook accrued exposure.");
        });
        Test("stale session input cannot advance exposure clear current hold or end the fight",()=>{
            var f=Fight();var s=f.Snapshot;f.SetReeling(s.SessionId,s.EncounterId,true);var before=f.Snapshot;
            Need(!f.Advance(G(),250).Accepted&&!f.ClearInput(G()).Accepted&&!f.SetReeling(s.SessionId,G(),false).Accepted&&!f.End(G()).Accepted,"Stale identity accepted.");Same(before,f.Snapshot);
        });
        Test("invalid outcomes and active-time bounds are rejected without changing pending state",()=>{
            var f=Fight();var r=Mature(f);var before=f.Snapshot;
            Reject<ArgumentOutOfRangeException>(()=>f.ConfirmFailure(r.FailureTicket,1,(FloatFailureOutcome)99));
            Reject<ArgumentOutOfRangeException>(()=>f.Advance(before.SessionId,-1));Reject<ArgumentOutOfRangeException>(()=>f.Advance(before.SessionId,251));
            Need(!f.ConfirmFailure(r.FailureTicket,0,FloatFailureOutcome.Confirmed).Accepted&&!f.ConfirmFailure(r.FailureTicket,2,FloatFailureOutcome.Confirmed).Accepted,"Unsubmitted attempt accepted.");Same(before,f.Snapshot);
        });
        Test("all new state and failure operations remain guarded by the creator thread",()=>{
            var f=Fight();var r=Mature(f);Exception failure=null;int guarded=0;
            var worker=new Thread(()=>{try{
                Reject<InvalidOperationException>(()=>{var snapshot=f.Snapshot;});guarded++;
                Reject<InvalidOperationException>(()=>f.RetryFailure(r.FailureTicket));guarded++;
                Reject<InvalidOperationException>(()=>f.ConfirmFailure(r.FailureTicket,1,FloatFailureOutcome.Confirmed));guarded++;
                Reject<InvalidOperationException>(()=>f.Advance(r.Snapshot.SessionId,0));guarded++;
            }catch(Exception e){failure=e;}});worker.Start();worker.Join();Need(failure==null&&guarded==4,"Cross-thread operation was accepted.");
            Need(f.ConfirmFailure(r.FailureTicket,1,FloatFailureOutcome.Confirmed).TerminalEvent,"Foreign thread changed owner state.");
        });
        Test("explicit production slack rules keep 100 natural single-water fights manually landable",()=>{
            int earliest=Int32.MaxValue,latest=0,maximumExposure=0;
            for(uint seed=0;seed<100;seed++){
                bool carp=seed%2==0;long mass=carp?300000+(long)((seed/2)%16)*100000:100000+(long)((seed/2)%12)*50000;
                var start=new FloatFightPoint(((int)(seed%3)-1)*5,-.2,6+seed%37);double distance=Math.Sqrt(start.X*start.X+1.7*1.7+start.Z*start.Z);
                var setup=new FloatFightSetup(G(),G(),new FloatBiteTarget(carp?"carp":"crucian",mass),1,new FloatFightPoint(0,1.5,0),start,50000,distance,
                    new FloatFightWater(0,-2,-40,40,3,80),new FloatFightRigRules(80,1,40,200,1,20,.2,1.6,40,3,3,5,10),
                    new FloatFightFishRules(carp?3:2,2,20,.05,.005,10),new FloatLandingRules(true,5,1,1,.5),seed,new FloatUnhookRules(.25,60,2));
                var f=Fight(setup);f.SetReeling(setup.SessionId,setup.EncounterId,true);int steps=0;
                while(f.Snapshot.Phase!=FloatFightPhase.LandingEligible&&steps<3600){var r=Tick(f);Need(r.Accepted&&!r.ShouldCommitFailure&&r.Snapshot.Phase!=FloatFightPhase.Faulted,"Production rules broke natural fight at seed "+seed+": "+r.Code);
                    Near(r.Snapshot.OutLineM+r.Snapshot.SpoolRemainingM,50);maximumExposure=Math.Max(maximumExposure,r.Snapshot.UnhookExposureSteps);steps++;}
                Need(f.Snapshot.Phase==FloatFightPhase.LandingEligible,"Natural fight never reached net at seed "+seed);earliest=Math.Min(earliest,steps);latest=Math.Max(latest,steps);
                Need(f.TryLand(setup.SessionId,setup.EncounterId,G()).ShouldCommitLanding,"Natural eligible fight could not request explicit landing.");
            }
            Console.WriteLine("SWEEP production rules; 100 cases; earliest "+earliest+" steps; latest "+latest+" steps; maximum slack exposure "+maximumExposure);
        });
        Test("production seed 91 can wait without auto reel or forced slack failure then wind manually",()=>{
            foreach(bool carp in new[]{true,false}){
                var start=new FloatFightPoint(0,-.2,24);double distance=Math.Sqrt(24*24+1.7*1.7);
                var setup=new FloatFightSetup(G(),G(),new FloatBiteTarget(carp?"carp":"crucian",carp?1000000:400000),1,new FloatFightPoint(0,1.5,0),start,50000,distance,
                    new FloatFightWater(0,-2,-40,40,3,80),new FloatFightRigRules(80,1,40,200,1,20,.2,1.6,40,3,3,5,5),
                    new FloatFightFishRules(carp?3:2,2,20,.05,.005,10),new FloatLandingRules(true,5,1,1,.5),91,new FloatUnhookRules(.25,60,2));
                var f=Fight(setup);for(int i=0;i<600;i++){var r=Tick(f);Need(r.Accepted&&!r.ShouldCommitFailure&&r.Snapshot.Phase!=FloatFightPhase.Faulted&&!r.Snapshot.Reeling,"Seed 91 waiting unexpectedly failed: "+r.Code);}
                f.SetReeling(setup.SessionId,setup.EncounterId,true);int steps=0;while(f.Snapshot.Phase!=FloatFightPhase.LandingEligible&&steps<3600){var r=Tick(f);Need(r.Accepted&&!r.ShouldCommitFailure&&r.Snapshot.Phase!=FloatFightPhase.Faulted,"Seed 91 winding failed: "+r.Code);steps++;}
                Need(f.Snapshot.Phase==FloatFightPhase.LandingEligible,"Seed 91 waiting prevented later manual landing.");
            }
        });
        Test("controlled extra two meters of existing line mature production exposure at exactly three active seconds",()=>{
            var setup=Setup(rules:new FloatUnhookRules(.25,60,2),slack:2);var f=Fight(setup);
            for(int i=0;i<11;i++)Need(!Tick(f,250).ShouldCommitFailure,"Controlled exposure matured before three seconds.");
            var before=Tick(f,200);Need(before.Snapshot.StepIndex==59&&before.Snapshot.UnhookExposureSteps==59&&!before.ShouldCommitFailure,"Controlled 2950 ms boundary changed.");
            Near(before.Snapshot.SlackM,2);Near(before.Snapshot.OutLineM+before.Snapshot.SpoolRemainingM,50);
            var result=Tick(f,50);Need(result.ShouldCommitFailure&&result.Snapshot.StepIndex==60&&result.Snapshot.UnhookExposureSteps==60&&!result.Snapshot.Reeling,"Controlled 3000 ms boundary changed.");
            Need(Object.ReferenceEquals(result.Snapshot.Target,setup.Target),"Controlled setup rerolled the target.");
        });
        Console.WriteLine("RESULT "+passed+" passed; "+failed+" failed");return failed==0?0:1;
    }
}
