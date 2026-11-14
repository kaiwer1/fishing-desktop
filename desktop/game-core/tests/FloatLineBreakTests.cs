using System;
using System.Globalization;
using FishingDesktop.GameCore;

static class FloatLineBreakTests
{
    static int passed,failed,nextId;
    const long ThresholdMilliN=5000;
    const int ExposureSteps=5;
    static Guid G(){return new Guid(++nextId,0,0,new byte[]{0,0,0,0,0,0,0,41});}
    static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Near(double a,double b){Need(Math.Abs(a-b)<1e-9,"Expected "+b+"; got "+a);}
    static void Reject<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    static void Test(string name,Action body){try{body();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
    static FloatLineBreakRules Rules(long threshold=ThresholdMilliN,int exposure=ExposureSteps,Guid? line=null,long revision=1,string key="mainline-terminal")
    {return new FloatLineBreakRules(line??G(),revision,1,key,threshold,exposure);}
    static FloatFightRigRules Rig(int drag=10,double maximum=200,double minimumDrag=1,double maximumDrag=20,double stall=40,double payout=3)
    {return new FloatFightRigRules(80,1,40,maximum,minimumDrag,maximumDrag,.2,1.6,stall,payout,3,drag,10);}
    static FloatFightSetup Setup(FloatLineBreakRules rules=null,bool enabled=true,string species="carp",long mass=1000000,uint seed=91,
        FloatFightRigRules rig=null,FloatFightFishRules fish=null,FloatUnhookRules unhook=null,FloatFightPoint start=null,double? outLine=null,long total=50000,
        Guid? session=null,Guid? encounter=null)
    {
        var p=start??new FloatFightPoint(0,-.2,15);double distance=Math.Sqrt(p.X*p.X+1.7*1.7+p.Z*p.Z);
        return new FloatFightSetup(session??G(),encounter??G(),new FloatBiteTarget(species,mass),1,new FloatFightPoint(0,1.5,0),p,total,outLine??distance,
            new FloatFightWater(0,-2,-40,40,3,80),rig??Rig(),fish??new FloatFightFishRules(species=="carp"?3:2,2,20,.05,.005,10),
            new FloatLandingRules(true,5,1,1,.5),seed,unhook,enabled?(rules??Rules()):null);
    }
    static FloatFightSimulation Fight(FloatFightSetup setup=null,bool reeling=true)
    {var f=new FloatFightSimulation(setup??Setup());var s=f.Snapshot;if(reeling)f.SetReeling(s.SessionId,s.EncounterId,true);return f;}
    static FloatFightResult Tick(FloatFightSimulation f,int ms=50){return f.Advance(f.Snapshot.SessionId,ms);}
    static bool Active(FloatFightSnapshot s){return s.Phase==FloatFightPhase.Fighting||s.Phase==FloatFightPhase.LandingEligible;}
    static FloatFightResult Mature(FloatFightSimulation f)
    {
        for(int i=0;i<3600;i++){var r=Tick(f);if(r.ShouldCommitFailure){Need(r.FailureTicket.Reason=="line-overloaded"&&r.FailureTicket.LineBreakEvidence!=null,"Expected physical line failure.");return r;}
            Need(r.Accepted&&Active(r.Snapshot),"Fixture failed before line overload: "+r.Code);}
        throw new InvalidOperationException("Actual load never matured.");
    }
    static void Same(FloatFightSnapshot a,FloatFightSnapshot b)
    {
        Need(a.Phase==b.Phase&&a.Reason==b.Reason&&a.StepIndex==b.StepIndex&&a.RemainderMilliseconds==b.RemainderMilliseconds&&a.Reeling==b.Reeling,
            "Phase clock or control changed.");
        Need(a.LineBreakExposureSteps==b.LineBreakExposureSteps&&a.UnhookExposureSteps==b.UnhookExposureSteps&&a.FailureAttempt==b.FailureAttempt,"Failure state changed.");
        foreach(var pair in new[]{new[]{a.Position.X,b.Position.X},new[]{a.Position.Z,b.Position.Z},new[]{a.Velocity.X,b.Velocity.X},new[]{a.Velocity.Z,b.Velocity.Z},
            new[]{a.OutLineM,b.OutLineM},new[]{a.SpoolRemainingM,b.SpoolRemainingM},new[]{a.TensionN,b.TensionN},new[]{a.Energy,b.Energy}})Near(pair[0],pair[1]);
    }
    static void Observe(string species,int drag,FloatFightSnapshot state,double peak,bool broken)
    {
        Console.WriteLine("OBSERVED {\"species\":\""+species+"\",\"massMg\":"+state.Target.MassMg+",\"seed\":91,\"initialWaterZMetres\":15,\"dragLevel\":"+drag+",\"dragN\":"+state.DragN.ToString("R",CultureInfo.InvariantCulture)+
            ",\"reelMps\":1.6,\"thresholdMilliN\":"+ThresholdMilliN+",\"exposureSteps\":"+ExposureSteps+",\"steps\":"+state.StepIndex+
            ",\"peakTensionN\":"+peak.ToString("R",CultureInfo.InvariantCulture)+",\"finalTensionN\":"+state.TensionN.ToString("R",CultureInfo.InvariantCulture)+
            ",\"outLineMmFloor\":"+Math.Floor(state.OutLineM*1000).ToString(CultureInfo.InvariantCulture)+",\"broke\":"+(broken?"true":"false")+"}");
    }
    static void CompareLoads(string species,long mass)
    {
        var setup=Setup(species:species,mass:mass,unhook:new FloatUnhookRules(.25,60,2));var low=Fight(setup);var high=Fight(setup);
        low.SetDragLevel(setup.SessionId,setup.EncounterId,0);double lowPeak=0,highPeak=0;FloatFightResult broken=null;
        for(int i=0;i<400;i++){
            var l=Tick(low);Need(l.Accepted&&Active(l.Snapshot)&&!l.ShouldCommitFailure,"Low drag failed: "+l.Code);lowPeak=Math.Max(lowPeak,l.Snapshot.TensionN);
            Near(l.Snapshot.OutLineM+l.Snapshot.SpoolRemainingM,50);
            if(broken==null){var h=Tick(high);highPeak=Math.Max(highPeak,h.Snapshot.TensionN);if(h.ShouldCommitFailure)broken=h;else Need(h.Accepted&&Active(h.Snapshot),"High load became a model fault: "+h.Code);}
        }
        Observe(species,0,low.Snapshot,lowPeak,false);Observe(species,10,high.Snapshot,highPeak,broken!=null);
        Need(broken!=null&&broken.FailureTicket.LineBreakEvidence!=null&&broken.FailureTicket.Reason=="line-overloaded","High load did not break within 20 active seconds.");
        Need(lowPeak<=1.001&&highPeak>ThresholdMilliN/1000.0&&highPeak<setup.Rig.MaximumSupportedTensionN,"Drag comparison left its supported envelope.");
        Need(high.Snapshot.LineBreakExposureSteps==ExposureSteps&&high.Snapshot.StepIndex<low.Snapshot.StepIndex,"Wrong exposure chronology.");
    }
    static int Main()
    {
        Test("line rules validate identity policy key force and exposure bounds",()=>{
            Reject<ArgumentException>(()=>Rules(line:Guid.Empty));Reject<ArgumentOutOfRangeException>(()=>Rules(revision:0));
            Reject<ArgumentOutOfRangeException>(()=>new FloatLineBreakRules(G(),1,2,"mainline-terminal",5000,6));
            foreach(string key in new[]{null,"","Upper","with space",new string('a',97)})Reject<ArgumentException>(()=>Rules(key:key));
            foreach(long force in new long[]{-1,0,10000001})Reject<ArgumentOutOfRangeException>(()=>Rules(threshold:force));
            foreach(int steps in new[]{0,12001})Reject<ArgumentOutOfRangeException>(()=>Rules(exposure:steps));
            Guid id=G();Reject<ArgumentException>(()=>Setup(rules:Rules(line:id),session:id));Reject<ArgumentException>(()=>Setup(rules:Rules(line:id),encounter:id));
            Reject<ArgumentException>(()=>Setup(rules:Rules(threshold:200000)));Need(Rules(threshold:1,exposure:12000).ExposureSteps==12000,"Valid bounds lost.");
        });
        Test("omitted line rules preserve legacy loaded fighting without an implicit failure",()=>{
            var f=Fight(Setup(enabled:false));double peak=0;
            for(int i=0;i<400;i++){var r=Tick(f);Need(r.Accepted&&Active(r.Snapshot)&&!r.ShouldCommitFailure&&r.FailureTicket==null,"Legacy behavior gained breakage.");peak=Math.Max(peak,r.Snapshot.TensionN);}
            Need(peak>5&&f.Snapshot.LineBreakExposureSteps==0,"Legacy load fixture did not cross proposed break force.");
        });
        Test("same seeded carp survives low drag and breaks under real supported high winding load",()=>CompareLoads("carp",1000000));
        Test("same seeded crucian survives low drag and breaks without increasing its two newton drive",()=>CompareLoads("crucian",500000));
        Test("actual host seeded bite target also reaches overload through the unchanged fish rules",()=>{
            Guid session=G();var bite=new FloatBiteSimulation(session,1,new FloatBiteRules(20,20,30,80),new FloatBiteEnvironment(.18,
                new[]{new FloatBiteCandidate("carp",3,300000,1800000),new FloatBiteCandidate("crucian",7,100000,650000)}));
            for(int i=0;i<2400&&bite.Snapshot.Phase!=FloatBitePhase.Strikeable;i++)bite.Advance(session,50);
            var hooked=bite.Strike(session,bite.Snapshot.EncounterId);Need(hooked.ShouldStartFight&&hooked.Snapshot.Target!=null,"Natural seeded fish did not hook.");
            var target=hooked.Snapshot.Target;Console.WriteLine("NATURAL_TARGET "+target.SpeciesId+" "+target.MassMg+" mg");CompareLoads(target.SpeciesId,target.MassMg);
        });
        Test("reducing drag resets consecutive overload exposure through the actual solver",()=>{
            var f=Fight(Setup(rules:Rules(exposure:100)));for(int i=0;i<400&&f.Snapshot.LineBreakExposureSteps<3;i++)Tick(f);
            Need(f.Snapshot.LineBreakExposureSteps>=3&&f.Snapshot.LineBreakExposureSteps<100,"No pre-failure exposure.");var s=f.Snapshot;
            f.SetDragLevel(s.SessionId,s.EncounterId,0);var r=Tick(f);Need(r.Snapshot.TensionN<5&&r.Snapshot.LineBreakExposureSteps==0&&!r.ShouldCommitFailure,"Unload did not clear consecutive exposure.");
        });
        Test("fixed step partitions produce the same overloaded step evidence and failure identity",()=>{
            var setup=Setup();var a=Fight(setup);var b=Fight(setup);FloatFightResult ar=null,br=null;int ac=0,bc=0;
            for(int i=0;i<80;i++){var r=Tick(a,250);if(r.ShouldCommitFailure){ar=r;ac++;}}
            for(int i=0;i<20000;i++){var r=Tick(b,1);if(r.ShouldCommitFailure){br=r;bc++;}}
            Need(ar!=null&&br!=null&&ac==1&&bc==1,"One initial failure was not emitted.");Same(a.Snapshot,b.Snapshot);
            Need(ar.FailureTicket.FailureId==br.FailureTicket.FailureId&&ar.FailureTicket.LineBreakEvidence.OutLineMm==br.FailureTicket.LineBreakEvidence.OutLineMm,"Partition altered frozen loss.");
        });
        Test("paused and partial active time preserve overload exposure without catchup",()=>{
            var f=Fight(Setup(rules:Rules(exposure:100)));for(int i=0;i<400&&f.Snapshot.LineBreakExposureSteps<2;i++)Tick(f);
            Need(f.Snapshot.LineBreakExposureSteps>=2,"No exposure to pause.");Tick(f,49);var before=f.Snapshot;
            Need(before.RemainderMilliseconds==49,"Partial step advanced.");for(int i=0;i<100;i++){Tick(f,0);Same(before,f.Snapshot);}
            Tick(f,1);Need(f.Snapshot.StepIndex==before.StepIndex+1&&f.Snapshot.LineBreakExposureSteps==before.LineBreakExposureSteps+1,"Resume lost or fabricated time.");
        });
        Test("unsupported physical speed faults before producing loss evidence",()=>{
            var f=Fight(Setup(rules:Rules(threshold:1,exposure:1),fish:new FloatFightFishRules(20,2,20,.05,.005,.01,FloatFightBehavior.Outward)),false);
            var r=Tick(f);Need(r.Snapshot.Phase==FloatFightPhase.Faulted&&r.Code=="speed-outside-supported-range"&&!r.ShouldCommitFailure&&r.FailureTicket==null&&r.Snapshot.LineBreakExposureSteps==0,"Speed fault disguised as broken line.");
        });
        Test("unsupported load remains a diagnostic fault rather than an equipment loss",()=>{
            var f=Fight(Setup(rules:Rules(threshold:100,exposure:1),rig:Rig(maximum:.2,minimumDrag:.1,maximumDrag:.1,stall:.1,payout:0),
                fish:new FloatFightFishRules(20,2,20,.05,.005,10,FloatFightBehavior.Outward)),false);
            var r=Tick(f);Need(r.Snapshot.Phase==FloatFightPhase.Faulted&&r.Code=="load-outside-supported-range"&&!r.ShouldCommitFailure&&r.FailureTicket==null&&r.Snapshot.LineBreakExposureSteps==0,"Unsupported load became lost equipment.");
        });
        Test("validated break freezes canonical integer loss evidence without mutating physical line ownership",()=>{
            var setup=Setup();var f=Fight(setup);var r=Mature(f);var e=r.FailureTicket.LineBreakEvidence;
            Need(e.LineId==setup.LineBreak.LineId&&e.LineRevision==1&&e.PolicyVersion==1&&e.LossRuleKey=="mainline-terminal"&&e.BreakMilliN==ThresholdMilliN&&e.ExposureSteps==ExposureSteps,"Rules were not frozen.");
            Need(e.OutLineMm==(long)Math.Floor(r.Snapshot.OutLineM*1000)&&e.OutLineMm>0&&e.OutLineMm<=e.TotalLineMm&&e.CutDistanceFromReelMm==0&&e.TotalLineMm==50000,"Integer loss envelope wrong.");
            long lost=e.OutLineMm-e.CutDistanceFromReelMm,remaining=e.TotalLineMm-lost;Need(lost+remaining==e.TotalLineMm&&remaining>0,"Millimetre conservation failed.");
            Near(r.Snapshot.OutLineM+r.Snapshot.SpoolRemainingM,50);Need(e.StepIndex==r.Snapshot.StepIndex&&e.TensionN==r.Snapshot.TensionN&&e.TensionN>5&&Object.ReferenceEquals(r.Snapshot.Target,setup.Target),"Evidence detached from validated state.");
            Need(!r.TerminalEvent&&!r.ShouldCommitLanding&&!r.Snapshot.Reeling&&r.Snapshot.RemainderMilliseconds==0,"Unconfirmed loss was treated as settled.");
        });
        Test("all paid out line has exact full-length loss with no invented retained millimetres",()=>{
            var setup=Setup(rules:Rules(threshold:1000,exposure:2),start:new FloatFightPoint(0,-.2,Math.Sqrt(144-1.7*1.7)),outLine:12,total:12000,
                fish:new FloatFightFishRules(3,2,20,.05,.005,10,FloatFightBehavior.Outward));var f=Fight(setup,false);var r=Mature(f);var e=r.FailureTicket.LineBreakEvidence;
            Need(e.OutLineMm==12000&&e.TotalLineMm==12000&&e.CutDistanceFromReelMm==0&&e.TotalLineMm-e.OutLineMm==0,"Full spool boundary rounded past or below its owned budget.");
            Near(r.Snapshot.SpoolRemainingM,0);Near(r.Snapshot.OutLineM,12);
        });
        Test("known unwritten break retries the same ticket evidence and attempt only on explicit request",()=>{
            var f=Fight();var r=Mature(f);var ticket=r.FailureTicket;var evidence=ticket.LineBreakEvidence;
            Need(f.IsCurrentFailureSubmission(ticket,1)&&!f.IsCurrentFailureSubmission(ticket,2),"Initial submission authority mismatch.");
            var known=f.ConfirmFailure(ticket,1,FloatFailureOutcome.KnownNotWritten);Need(known.Accepted&&!known.TerminalEvent&&!known.ShouldCommitFailure,"Known write failure auto-settled.");
            Need(!f.IsCurrentFailureSubmission(ticket,1),"Unwritten outcome left a submitted ticket authorized.");
            for(int i=0;i<10;i++)Need(!Tick(f,250).ShouldCommitFailure,"Failure auto-retried.");
            var retry=f.RetryFailure(ticket);Need(retry.ShouldCommitFailure&&retry.FailureAttempt==2&&retry.Code=="line-overloaded"&&Object.ReferenceEquals(ticket,retry.FailureTicket)&&Object.ReferenceEquals(evidence,retry.FailureTicket.LineBreakEvidence),"Retry changed frozen overload.");
            Need(f.IsCurrentFailureSubmission(ticket,2)&&!f.IsCurrentFailureSubmission(ticket,1),"Explicit retry failed to replace submission attempt authority.");
            Need(!f.ConfirmFailure(ticket,1,FloatFailureOutcome.Confirmed).Accepted&&!f.RetryFailure(ticket).Accepted,"Stale or duplicate attempt accepted.");
            Need(f.ConfirmFailure(ticket,2,FloatFailureOutcome.Confirmed).TerminalEvent,"Current retry did not settle.");
            Need(!f.IsCurrentFailureSubmission(ticket,2),"Confirmed ticket remained authorized for another write.");
        });
        Test("unknown break save freezes evidence retry and abandonment without confirmation",()=>{
            var f=Fight();var r=Mature(f);var unknown=f.ConfirmFailure(r.FailureTicket,1,FloatFailureOutcome.Unknown);var before=f.Snapshot;
            Need(unknown.Accepted&&!unknown.TerminalEvent&&before.Reason=="failure-save-unknown","Unknown outcome changed semantics.");
            Need(!f.IsCurrentFailureSubmission(r.FailureTicket,1),"Unknown ticket remained authorized for another write.");
            Need(!f.RetryFailure(r.FailureTicket).Accepted&&!f.End(before.SessionId).Accepted&&!Tick(f,250).Accepted&&!f.ConfirmFailure(r.FailureTicket,1,FloatFailureOutcome.Confirmed).Accepted,"Unknown loss unlocked.");Same(before,f.Snapshot);
        });
        Test("equal value and previous runtime tickets cannot authorize a physical loss",()=>{
            var setup=Setup();var a=Fight(setup);var b=Fight(setup);var ar=Mature(a);var br=Mature(b);var t=ar.FailureTicket;
            Need(t.FailureId==br.FailureTicket.FailureId,"Same setup changed failure identity.");
            var forged=new FloatFailureTicket(t.FailureId,t.SessionId,t.EncounterId,t.Reason,t.LineBreakEvidence);
            Need(!a.ConfirmFailure(forged,1,FloatFailureOutcome.Confirmed).Accepted&&!a.RetryFailure(forged).Accepted&&!b.ConfirmFailure(t,1,FloatFailureOutcome.Confirmed).Accepted,"Ticket reference authority bypassed.");
            Need(!a.IsCurrentFailureSubmission(forged,1)&&!b.IsCurrentFailureSubmission(t,1),"Forged or foreign ticket authorized a write before confirmation.");
            Need(a.ConfirmFailure(t,1,FloatFailureOutcome.Confirmed).Accepted&&b.ConfirmFailure(br.FailureTicket,1,FloatFailureOutcome.Confirmed).Accepted,"Actual ticket lost authority.");
        });
        Test("confirmed break emits one terminal notification and never grants or lands the fish",()=>{
            var f=Fight();var r=Mature(f);var c=f.ConfirmFailure(r.FailureTicket,1,FloatFailureOutcome.Confirmed);
            Need(c.Accepted&&c.TerminalEvent&&c.Snapshot.Phase==FloatFightPhase.Escaped&&c.Code=="line-overloaded"&&!c.ShouldCommitLanding&&!c.ShouldCommitFailure,"Break confirmation did not terminate once.");
            Need(!f.ConfirmFailure(r.FailureTicket,1,FloatFailureOutcome.Confirmed).Accepted&&!f.RetryFailure(r.FailureTicket).Accepted&&!f.TryLand(c.Snapshot.SessionId,c.Snapshot.EncounterId,G()).Accepted&&!f.End(c.Snapshot.SessionId).TerminalEvent,"Confirmed break duplicated or landed fish.");
        });
        Test("stale identities and invalid time cannot advance exposure or trigger loss",()=>{
            var f=Fight();Tick(f,49);var s=f.Snapshot;
            Need(!f.Advance(G(),250).Accepted&&!f.SetDragLevel(s.SessionId,G(),0).Accepted&&!f.ClearInput(G()).Accepted&&!f.End(G()).Accepted,"Stale action accepted.");
            Reject<ArgumentOutOfRangeException>(()=>Tick(f,-1));Reject<ArgumentOutOfRangeException>(()=>Tick(f,251));Same(s,f.Snapshot);
        });
        Test("legacy slack unhook retains its reason and has no line loss evidence",()=>{
            var f=Fight(Setup(rules:Rules(threshold:100000),outLine:18,unhook:new FloatUnhookRules(.25,4,2),fish:new FloatFightFishRules(0,2,20,.05,.005,10,FloatFightBehavior.Rest)),false);
            var r=Tick(f,250);Need(r.ShouldCommitFailure&&r.FailureTicket.Reason=="slack-unhooked"&&r.FailureTicket.LineBreakEvidence==null&&r.Snapshot.LineBreakExposureSteps==0,"Slack failure gained equipment damage.");
            var retry=f.ConfirmFailure(r.FailureTicket,1,FloatFailureOutcome.KnownNotWritten);Need(retry.Accepted&&f.RetryFailure(r.FailureTicket).Code=="slack-unhooked","Legacy retry reason changed.");
            Need(f.ConfirmFailure(r.FailureTicket,2,FloatFailureOutcome.Confirmed).Code=="slack-unhooked","Legacy confirmation changed.");
        });
        Test("failure identity binds line revision and loss rules without changing the physical random stream",()=>{
            Guid session=G(),encounter=G(),line=G();var a=Fight(Setup(rules:Rules(line:line),session:session,encounter:encounter));
            var b=Fight(Setup(rules:Rules(line:line,revision:2),session:session,encounter:encounter));
            var c=Fight(Setup(rules:Rules(line:line,key:"mainline-terminal-v1"),session:session,encounter:encounter));
            var ar=Mature(a);var br=Mature(b);var cr=Mature(c);Same(a.Snapshot,b.Snapshot);Same(a.Snapshot,c.Snapshot);
            Need(ar.FailureTicket.FailureId!=br.FailureTicket.FailureId&&ar.FailureTicket.FailureId!=cr.FailureTicket.FailureId,"Distinct frozen line rules reused failure identity.");
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
}
