using System;
using System.Threading;
using FishingDesktop.GameCore;

static class FloatBiteSimulationTests
{
    static int passed,failed,nextId;
    static Guid G(){return new Guid(++nextId,0,0,new byte[]{0,0,0,0,0,0,0,9});}
    static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Reject<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    static void Test(string name,Action body){try{body();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
    static FloatBiteCandidate Carp(int weight=3,long minimum=100000,long maximum=3000000){return new FloatBiteCandidate("carp",weight,minimum,maximum);}
    static FloatBiteCandidate Crucian(int weight=7){return new FloatBiteCandidate("crucian",weight,100000,800000);}
    static FloatBiteEnvironment Env(double rate=1000,FloatBiteCandidate[] candidates=null,bool available=true)
    {return new FloatBiteEnvironment(rate,candidates??new[]{Carp(),Crucian()},available);}
    static FloatBiteRules Rules(){return new FloatBiteRules(1,2,3,4);}
    static FloatBiteSimulation Bite(Guid? session=null,uint seed=77,FloatBiteRules rules=null,FloatBiteEnvironment environment=null)
    {return new FloatBiteSimulation(session??G(),seed,rules??Rules(),environment??Env());}
    static FloatBiteSnapshot Go(FloatBiteSimulation bite,FloatBitePhase phase)
    {
        for(int i=0;i<10000&&bite.Snapshot.Phase!=phase;i++){
            var result=bite.Advance(bite.Snapshot.SessionId,50);Need(result.Accepted,"Cannot reach "+phase+": "+result.Code);
        }
        Need(bite.Snapshot.Phase==phase,"Phase did not arrive.");return bite.Snapshot;
    }
    static void Same(FloatBiteSnapshot a,FloatBiteSnapshot b)
    {
        Need(a.SessionId==b.SessionId&&a.EncounterId==b.EncounterId&&a.Phase==b.Phase&&a.Reason==b.Reason,"Identity or phase differs.");
        Need(a.StepIndex==b.StepIndex&&a.SearchEpoch==b.SearchEpoch&&a.OpenStep==b.OpenStep&&a.CloseStep==b.CloseStep&&a.RemainderMilliseconds==b.RemainderMilliseconds,"Clock/epoch differs.");
        Need(a.Exposure==b.Exposure&&a.Threshold==b.Threshold&&a.BaitAttached==b.BaitAttached&&a.BobberOffsetMm==b.BobberOffsetMm&&a.BobberDriftMm==b.BobberDriftMm,"Presentation or search differs.");
        Need((a.Target==null)==(b.Target==null),"Target existence differs.");
        if(a.Target!=null)Need(a.Target.SpeciesId==b.Target.SpeciesId&&a.Target.MassMg==b.Target.MassMg,"Locked fish differs.");
    }
    static int Main()
    {
        Test("confirmed identity starts settling with one attached bait and no target",()=>{
            var s=Bite().Snapshot;Need(s.Phase==FloatBitePhase.Settling&&s.SessionId!=Guid.Empty&&s.EncounterId==Guid.Empty&&s.Target==null&&s.StepIndex==0&&s.SearchEpoch==0&&s.BaitAttached,"Unexpected initial state.");
        });
        Test("only complete 50 ms active ticks advance with a retained remainder",()=>{
            var f=Bite();var id=f.Snapshot.SessionId;f.Advance(id,49);Need(f.Snapshot.StepIndex==0&&f.Snapshot.RemainderMilliseconds==49,"Substep advanced.");
            f.Advance(id,0);Need(f.Snapshot.StepIndex==0,"Zero time advanced.");f.Advance(id,1);Need(f.Snapshot.StepIndex==1&&f.Snapshot.RemainderMilliseconds==0&&f.Snapshot.Phase==FloatBitePhase.Searching,"Full tick was lost.");
        });
        Test("equal seed and active time produce identical state with 1 and 250 ms partitions",()=>{
            Guid id=G();var a=Bite(id,environment:Env(.15));var b=Bite(id,environment:Env(.15));
            for(int i=0;i<40;i++)a.Advance(id,250);for(int i=0;i<10000;i++)b.Advance(id,1);Same(a.Snapshot,b.Snapshot);
        });
        Test("render-like snapshots and zero elapsed do not advance or reroll",()=>{
            var f=Bite();Go(f,FloatBitePhase.Nibbling);var before=f.Snapshot;
            for(int i=0;i<1000;i++){var r=f.Advance(before.SessionId,0);Need(r.Accepted&&!r.ShouldStartFight&&!r.TerminalEvent,"Pause produced an outcome.");Same(before,f.Snapshot);}
        });
        Test("zero rate waits without a fallback fish or automatic timeout",()=>{
            var f=Bite(environment:Env(0));Go(f,FloatBitePhase.Searching);double threshold=f.Snapshot.Threshold;
            for(int i=0;i<400;i++)f.Advance(f.Snapshot.SessionId,250);
            var s=f.Snapshot;Need(s.Phase==FloatBitePhase.Searching&&s.Reason=="zero-rate"&&s.Target==null&&s.Exposure==0&&s.Threshold==threshold&&s.SearchEpoch==1&&s.BaitAttached,"Zero hazard invented an encounter.");
        });
        Test("empty and zero-weight pools legally wait without accumulating search exposure",()=>{
            foreach(var pool in new[]{new FloatBiteCandidate[0],new[]{Carp(0)}}){var f=Bite(environment:Env(1000,pool));for(int i=0;i<100;i++)f.Advance(f.Snapshot.SessionId,250);
                Need(f.Snapshot.Phase==FloatBitePhase.Searching&&f.Snapshot.Reason=="no-target"&&f.Snapshot.Target==null&&f.Snapshot.Exposure==0&&f.Snapshot.SearchEpoch==1,"Empty pool created fish or rerolled.");}
        });
        Test("rate and target-pool changes preserve an existing threshold and exposure",()=>{
            var f=Bite(environment:Env(.01));Go(f,FloatBitePhase.Searching);f.Advance(f.Snapshot.SessionId,100);var before=f.Snapshot;
            f.SetEnvironment(before.SessionId,Env(0,new FloatBiteCandidate[0]));f.Advance(before.SessionId,250);
            Need(f.Snapshot.Threshold==before.Threshold&&f.Snapshot.Exposure==before.Exposure&&f.Snapshot.SearchEpoch==before.SearchEpoch,"Environment reset search.");
            f.SetEnvironment(before.SessionId,Env(1000));var found=Go(f,FloatBitePhase.Approaching);Need(found.SearchEpoch==before.SearchEpoch&&found.Threshold==before.Threshold,"Restoring eligibility rerolled threshold.");
        });
        Test("species catalog order does not change deterministic selection or mass",()=>{
            Guid id=G();var a=Bite(id,environment:Env(1000,new[]{Carp(),Crucian()}));var b=Bite(id,environment:Env(1000,new[]{Crucian(),Carp()}));
            Go(a,FloatBitePhase.Approaching);Go(b,FloatBitePhase.Approaching);Same(a.Snapshot,b.Snapshot);
        });
        Test("weighted selection never chooses an explicit zero-weight species",()=>{
            for(uint seed=0;seed<100;seed++){var f=Bite(seed:seed,environment:Env(1000,new[]{Carp(0),Crucian(1)}));var s=Go(f,FloatBitePhase.Approaching);Need(s.Target.SpeciesId=="crucian","Zero weight selected.");}
        });
        Test("integer body mass is within configured inclusive bounds including a single point",()=>{
            for(uint seed=0;seed<100;seed++){var f=Bite(seed:seed,environment:Env(1000,new[]{Carp(1,19,21)}));long mass=Go(f,FloatBitePhase.Approaching).Target.MassMg;Need(mass>=19&&mass<=21,"Mass out of bounds.");}
            var exact=Bite(environment:Env(1000,new[]{Carp(1,1000000000000L,1000000000000L)}));Need(Go(exact,FloatBitePhase.Approaching).Target.MassMg==1000000000000L,"Single mass changed.");
        });
        Test("waiting duration uses a separate random stream from species and body mass",()=>{
            var a=Bite(environment:Env(.4));var b=Bite(environment:Env(1000));var x=Go(a,FloatBitePhase.Approaching);var y=Go(b,FloatBitePhase.Approaching);
            Need(x.StepIndex>y.StepIndex&&x.Target.SpeciesId==y.Target.SpeciesId&&x.Target.MassMg==y.Target.MassMg,"Long wait altered target draw.");
        });
        Test("target and encounter are locked through approach nibble and strike",()=>{
            var f=Bite();var first=Go(f,FloatBitePhase.Approaching);f.SetEnvironment(first.SessionId,Env(0,new[]{Carp(1,1,1),Crucian(1)}));
            var window=Go(f,FloatBitePhase.Strikeable);Need(Object.ReferenceEquals(first.Target,window.Target)&&first.EncounterId==window.EncounterId,"Fish rerolled after encounter.");
            var result=f.Strike(window.SessionId,window.EncounterId);Need(result.ShouldStartFight&&Object.ReferenceEquals(first.Target,result.Snapshot.Target)&&first.EncounterId==result.Snapshot.EncounterId,"Strike rerolled target.");
        });
        Test("encounter identity is stable by session and distinct across sessions",()=>{
            Guid id=G();var a=Go(Bite(id),FloatBitePhase.Approaching);var b=Go(Bite(id),FloatBitePhase.Approaching);var c=Go(Bite(),FloatBitePhase.Approaching);
            Need(a.EncounterId!=Guid.Empty&&a.EncounterId==b.EncounterId&&a.EncounterId!=c.EncounterId,"Encounter identity is unstable or reused.");
        });
        Test("right action at the exact open step starts one fight without granting a fish",()=>{
            var f=Bite();var s=Go(f,FloatBitePhase.Strikeable);Need(s.StepIndex==s.OpenStep&&s.CloseStep-s.OpenStep==4,"Window bounds incorrect.");
            var hit=f.Strike(s.SessionId,s.EncounterId);Need(hit.Accepted&&hit.ShouldStartFight&&!hit.TerminalEvent&&hit.Snapshot.Phase==FloatBitePhase.Hooked&&hit.Snapshot.BaitAttached,"Valid strike was not unique handoff.");
        });
        Test("right action one tick before close remains valid",()=>{
            var f=Bite();var s=Go(f,FloatBitePhase.Strikeable);f.Advance(s.SessionId,150);Need(f.Snapshot.StepIndex==s.CloseStep-1,"Fixture did not reach boundary.");
            Need(f.Strike(s.SessionId,s.EncounterId).ShouldStartFight,"Last valid step was excluded.");
        });
        Test("the exact close step expires before input and never starts a fight",()=>{
            var f=Bite();var s=Go(f,FloatBitePhase.Strikeable);var expire=f.Advance(s.SessionId,200);Need(expire.TerminalEvent&&expire.Snapshot.StepIndex==s.CloseStep&&expire.Snapshot.Reason=="bite-missed"&&!expire.Snapshot.BaitAttached,"Exclusive close not honored.");
            Need(!f.Strike(s.SessionId,s.EncounterId).ShouldStartFight,"Late strike was cached or accepted.");
        });
        Test("a duplicate strike cannot emit a second fight or advance the locked fish",()=>{
            var f=Bite();var s=Go(f,FloatBitePhase.Strikeable);f.Strike(s.SessionId,s.EncounterId);var before=f.Snapshot;
            for(int i=0;i<20;i++){var r=f.Strike(s.SessionId,s.EncounterId);Need(!r.Accepted&&!r.ShouldStartFight&&!r.TerminalEvent,"Repeated fight.");f.Advance(s.SessionId,250);Same(before,f.Snapshot);}
        });
        Test("empty strike requires retrieval and cannot cache input for a later fish",()=>{
            var f=Bite();var id=f.Snapshot.SessionId;var r=f.Strike(id,Guid.Empty);Need(r.Accepted&&r.TerminalEvent&&!r.ShouldStartFight&&r.Code=="empty-strike"&&!r.Snapshot.BaitAttached&&r.Snapshot.Target==null,"Empty strike outcome incorrect.");
            f.Advance(id,250);Need(f.Snapshot.Phase==FloatBitePhase.RetrieveRequired&&f.Snapshot.Target==null,"Empty strike became a future hook.");
        });
        Test("early strike during nibble depletes only the attached runtime bait",()=>{
            var f=Bite();var s=Go(f,FloatBitePhase.Nibbling);var r=f.Strike(s.SessionId,s.EncounterId);
            Need(r.Accepted&&r.TerminalEvent&&!r.ShouldStartFight&&r.Code=="strike-too-early"&&!r.Snapshot.BaitAttached&&Object.ReferenceEquals(s.Target,r.Snapshot.Target),"Early strike did not terminate unchanged target.");
        });
        Test("stale sessions and stale encounter actions cannot mutate the current bite",()=>{
            var f=Bite();var s=Go(f,FloatBitePhase.Strikeable);Need(!f.Advance(G(),250).Accepted&&!f.SetEnvironment(G(),Env(0)).Accepted&&!f.Strike(G(),s.EncounterId).Accepted&&!f.End(G()).Accepted,"Old session accepted.");
            Need(!f.Strike(s.SessionId,G()).Accepted&&!f.Strike(s.SessionId,Guid.Empty).Accepted,"Old encounter accepted.");Same(s,f.Snapshot);
        });
        Test("lost water eligibility interrupts the same encounter without selecting a replacement",()=>{
            var f=Bite();var s=Go(f,FloatBitePhase.Approaching);var r=f.SetEnvironment(s.SessionId,Env(1000,available:false));
            Need(r.TerminalEvent&&r.Code=="environment-interrupted"&&!r.Snapshot.BaitAttached&&Object.ReferenceEquals(s.Target,r.Snapshot.Target)&&s.EncounterId==r.Snapshot.EncounterId,"Environment swapped fish.");
            Need(!f.SetEnvironment(s.SessionId,Env()).Accepted,"Restoration restarted a depleted encounter.");
        });
        Test("removing the locked species ends encounter instead of silently changing its identity",()=>{
            var f=Bite(environment:Env(1000,new[]{Carp(1)}));var s=Go(f,FloatBitePhase.Approaching);var r=f.SetEnvironment(s.SessionId,Env(1000,new[]{Crucian(1)}));
            Need(r.TerminalEvent&&r.Snapshot.Target.SpeciesId=="carp"&&r.Snapshot.EncounterId==s.EncounterId&&r.Snapshot.Phase==FloatBitePhase.RetrieveRequired,"Missing target replaced.");
        });
        Test("unavailable initial water reports failure on first active tick and never draws a fish",()=>{
            var f=Bite(environment:Env(1000,available:false));var r=f.Advance(f.Snapshot.SessionId,50);
            Need(r.TerminalEvent&&r.Code=="environment-unavailable"&&r.Snapshot.Target==null&&r.Snapshot.SearchEpoch==0&&!r.Snapshot.BaitAttached,"Unavailable geometry spawned fish.");
        });
        Test("ending a live or hooked encounter emits one terminal outcome without deleting target evidence",()=>{
            foreach(bool hook in new[]{false,true}){var f=Bite();var s=Go(f,FloatBitePhase.Strikeable);if(hook)f.Strike(s.SessionId,s.EncounterId);
                var end=f.End(s.SessionId);Need(end.Accepted&&end.TerminalEvent&&!end.ShouldStartFight&&end.Snapshot.Phase==FloatBitePhase.Ended&&end.Snapshot.Target==s.Target,"End lost target evidence.");
                Need(!f.End(s.SessionId).TerminalEvent&&!f.Advance(s.SessionId,250).Accepted,"End replayed or resumed.");}
        });
        Test("retrieve-required outcome is not reemitted by recall or repeated callbacks",()=>{
            var f=Bite();var s=Go(f,FloatBitePhase.Strikeable);Need(f.Advance(s.SessionId,250).TerminalEvent,"Missing expiry.");
            Need(!f.Advance(s.SessionId,250).TerminalEvent&&!f.Strike(s.SessionId,s.EncounterId).TerminalEvent&&!f.End(s.SessionId).TerminalEvent&&!f.End(s.SessionId).TerminalEvent,"Duplicate terminal event.");
        });
        Test("bobber offsets remain bounded and mild movement never implies a valid strike window",()=>{
            var f=Bite(rules:new FloatBiteRules(200,200,200,200));bool mild=false,nibble=false,dive=false;
            for(int i=0;i<1000;i++){var s=f.Snapshot;Need(Math.Abs(s.BobberOffsetMm)<=160&&Math.Abs(s.BobberDriftMm)<=30,"Unbounded bobber.");
                if(s.Phase==FloatBitePhase.Approaching){mild|=s.BobberOffsetMm!=0;Need(s.OpenStep==0,"Wind opened a strike.");}
                if(s.Phase==FloatBitePhase.Nibbling){nibble|=Math.Abs(s.BobberOffsetMm)>3;Need(s.OpenStep==0,"Nibble opened a strike.");}
                if(s.Phase==FloatBitePhase.Strikeable)dive|=s.BobberOffsetMm<=-45;
                f.Advance(s.SessionId,50);}
            Need(mild&&nibble&&dive,"Declared bobber phases were absent.");
        });
        Test("environment input and exported collection cannot mutate the captured candidate catalog",()=>{
            var original=Carp(1);var input=new[]{original};var environment=Env(1000,input);input[0]=Crucian(1);
            Reject<NotSupportedException>(()=>((System.Collections.Generic.IList<FloatBiteCandidate>)environment.Candidates)[0]=Crucian(1));
            Need(Go(Bite(environment:environment),FloatBitePhase.Approaching).Target.SpeciesId=="carp","External array changed target.");
        });
        Test("missing and malformed configuration is rejected separately from legal no-fish conditions",()=>{
            Reject<ArgumentException>(()=>Bite(Guid.Empty));Reject<ArgumentNullException>(()=>new FloatBiteSimulation(G(),1,null,Env()));Reject<ArgumentNullException>(()=>new FloatBiteSimulation(G(),1,Rules(),null));
            foreach(double hazard in new[]{-1,1001,Double.NaN,Double.PositiveInfinity})Reject<ArgumentOutOfRangeException>(()=>Env(hazard));
            Reject<ArgumentNullException>(()=>new FloatBiteEnvironment(1,null));Reject<ArgumentException>(()=>Env(1,new[]{Carp(),Carp()}));Reject<ArgumentException>(()=>Env(1,new FloatBiteCandidate[]{null}));
            Reject<ArgumentException>(()=>new FloatBiteCandidate("../bad",1,1,1));Reject<ArgumentOutOfRangeException>(()=>Carp(-1));Reject<ArgumentOutOfRangeException>(()=>Carp(1000001));
            Reject<ArgumentOutOfRangeException>(()=>Carp(1,0,1));Reject<ArgumentOutOfRangeException>(()=>Carp(1,10,9));Reject<ArgumentOutOfRangeException>(()=>Carp(1,1,1000000000001L));
            Reject<ArgumentOutOfRangeException>(()=>new FloatBiteRules(0,1,1,1));Reject<ArgumentOutOfRangeException>(()=>new FloatBiteRules(1,1,12001,1));
            var f=Bite();Reject<ArgumentOutOfRangeException>(()=>f.Advance(f.Snapshot.SessionId,-1));Reject<ArgumentOutOfRangeException>(()=>f.Advance(f.Snapshot.SessionId,251));Reject<ArgumentNullException>(()=>f.SetEnvironment(f.Snapshot.SessionId,null));
        });
        Test("all live model access is restricted to its creator thread",()=>{
            var f=Bite();Guid id=f.Snapshot.SessionId;int rejected=0;Exception failure=null;
            var worker=new Thread(()=>{try{foreach(Action action in new Action[]{()=>{var ignored=f.Snapshot;},()=>f.Advance(id,50),()=>f.SetEnvironment(id,Env()),()=>f.Strike(id,Guid.Empty),()=>f.End(id)}){
                try{action();}catch(InvalidOperationException){rejected++;}}}catch(Exception error){failure=error;}});worker.Start();worker.Join();
            Need(failure==null&&rejected==5&&f.Snapshot.StepIndex==0,"Cross-thread model access succeeded.");
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
}
