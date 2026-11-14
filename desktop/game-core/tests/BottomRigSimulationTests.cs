using System;
using FishingDesktop.GameCore;

internal static class BottomRigSimulationTests
{
    static readonly Guid Session=Id(100),RigId=Id(110);
    static readonly string[] Definitions={"bottom-bank-rod","bottom-bank-reel","bottom-022-line","bottom-016-leader",
        "bottom-single-hook","bottom-18g-sinker","worm"};
    static int failures;
    static Guid Id(int n){return new Guid(n,0,0,new byte[8]);}
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Test(string name,Action test)
    {
        try{test();Console.WriteLine("PASS "+name);}
        catch(Exception e){failures++;Console.WriteLine("FAIL "+name+" "+e.Message);}
    }
    static BottomSimpleAssembly Rig(int line=60000,string mode="bottom_simple",int missing=-1,
        int replace=-1,BottomRigPart replacement=null)
    {
        var parts=new BottomRigPart[7];
        for(int i=0;i<parts.Length;i++)parts[i]=i==missing?null:
            i==replace?replacement:new BottomRigPart(Id(i+1),(BottomPartKind)i,Definitions[i]);
        return new BottomSimpleAssembly(RigId,9,mode,line,parts[0],parts[1],parts[2],parts[3],parts[4],parts[5],parts[6]);
    }
    static FloatBiteEnvironment Fish(double hazard=1000,int weight=1,bool available=true)
    {
        return new FloatBiteEnvironment(hazard,new[]{new FloatBiteCandidate("carp",weight,10000,19999)},available);
    }
    static FloatStillWaterGeometry Bed(params FloatWaterBedKnot[] knots)
    {
        return new FloatStillWaterGeometry("survey","bank",3,5000,1500,0,0,-20000,20000,3000,80000,
            -5000,5000,18000,28000,knots);
    }
    static BottomSurveyPatch Patch(FloatStillWaterGeometry water=null,BottomSubstrate soil=BottomSubstrate.FirmGravel,bool clear=true)
    {
        water=water??FloatStillWaterGeometry.CreateDevelopment();
        return new BottomSurveyPatch(water,water.MinimumXmm,water.MaximumXmm,water.MinimumZmm,water.MaximumZmm,soil,clear);
    }
    static BottomRigSimulation Start(int z=12000,BottomSurveyPatch patch=null,FloatBiteEnvironment env=null,int line=60000)
    {
        patch=patch??Patch();BottomRigSimulation sim;
        string code=BottomRigSimulation.AtWaterContact(Session,Rig(line),patch,patch.Geometry.Version,0,z,7,env??Fish(),out sim);
        Require(code==null&&sim!=null,"contact refused "+code);return sim;
    }
    static BottomRigSnapshot Until(BottomRigSimulation sim,BottomPhase phase)
    {
        for(int i=0;i<800;i++){
            if(sim.Snapshot.Phase==phase)return sim.Snapshot;
            var result=sim.Advance(Session,50);Require(result.Accepted,"ended unexpectedly "+result.Code);
        }
        throw new Exception("never reached "+phase);
    }
    static BottomActionResult Strike(BottomRigSimulation sim)
    {return sim.Strike(Session,RigId,9,sim.Snapshot.EncounterId,true);}

    internal static int Main()
    {
        Test("seven distinct live identities and catalogued cast mass",delegate{
            var rig=Rig();Require(rig.ValidateAssembly()==null&&rig.EvaluateCastReadiness(1,2,true)==null,"ready");
            for(int i=0;i<7;i++)Require(rig.ItemId((BottomPartKind)i)==Id(i+1),"identity");
            Require(BottomSimpleAssembly.CastMassMg==18900&&BottomSimpleAssembly.RodMinimumCastMg<=18900&&
                BottomSimpleAssembly.RodMaximumCastMg>=18900&&BottomSimpleAssembly.LeaderLengthMm==400,"dimensions");
        });
        Test("missing slots are editable but incomplete for release or water contact",delegate{
            for(int i=0;i<7;i++){var rig=Rig(missing:i);
                Require(rig.ValidateAssembly()==null&&rig.EvaluateCastReadiness(1,2,true)=="rig-incomplete","slot "+i);
                BottomRigSimulation sim;Require(BottomRigSimulation.AtWaterContact(Session,rig,Patch(),1,0,12000,1,Fish(),out sim)=="rig-incomplete"&&sim==null,"launch");
            }
        });
        Test("feeder unsupported and overlapping component or rig identities refused",delegate{
            Require(Rig(mode:"bottom_cage").ValidateAssembly()=="bottom-unsupported","cage");
            Require(Rig(replace:5,replacement:new BottomRigPart(Id(88),BottomPartKind.Feeder,"bottom-18g-sinker")).ValidateAssembly()=="rig-incompatible","feeder");
            Require(Rig(replace:5,replacement:new BottomRigPart(Id(1),BottomPartKind.Sinker,Definitions[5])).ValidateAssembly()=="part-identity-conflict","duplicate");
            Require(Rig(replace:0,replacement:new BottomRigPart(RigId,BottomPartKind.Rod,Definitions[0])).ValidateAssembly()=="part-identity-conflict","rig");
        });
        Test("held bait stamina and finite spool are independent entry checks",delegate{
            Require(Rig().EvaluateCastReadiness(4,2,false)=="not-held"&&Rig().EvaluateCastReadiness(0,2,true)=="hookbait-empty"&&
                Rig().EvaluateCastReadiness(1,1,true)=="stamina-insufficient"&&Rig(0).EvaluateCastReadiness(1,2,true)=="line-insufficient","readiness");
            bool threw=false;try{Rig(100001);}catch(ArgumentException){threw=true;}Require(threw,"spool size");
        });
        Test("absent authority unavailable environment and wrong geometry never open a session",delegate{
            BottomRigSimulation s;
            Require(BottomRigSimulation.AtWaterContact(Guid.Empty,Rig(),Patch(),1,0,12000,1,Fish(),out s)=="session-not-current"&&s==null,"session");
            Require(BottomRigSimulation.AtWaterContact(Session,Rig(),null,1,0,12000,1,Fish(),out s)=="configuration-missing"&&s==null,"patch");
            Require(BottomRigSimulation.AtWaterContact(Session,Rig(),Patch(),1,0,12000,1,Fish(1,1,false),out s)=="environment-unavailable"&&s==null,"environment");
            Require(BottomRigSimulation.AtWaterContact(Session,Rig(),Patch(),2,0,12000,1,Fish(),out s)=="geometry-version-mismatch"&&s==null,"version");
        });
        Test("both ends stay in surveyed firm obstacle-free patch",delegate{
            BottomContact c;
            foreach(var kind in new[]{BottomSubstrate.Unknown,BottomSubstrate.Weeds,BottomSubstrate.SoftMud})
                Require(Patch(soil:kind).Resolve(1,0,12000,50000,out c)=="bottom-unsupported"&&c==null,"unsupported soil");
            Require(Patch(clear:false).Resolve(1,0,12000,50000,out c)=="bottom-unsupported","obstacles");
            Require(Patch().Resolve(1,40001,12000,50000,out c)=="outside-bottom-patch"&&
                Patch().Resolve(1,0,3239,50000,out c)=="outside-bottom-patch"&&Patch().Resolve(1,0,3240,50000,out c)==null,"patch edges");
        });
        Test("local steep segment rejected even where endpoint-average slope is zero",delegate{
            var patch=Patch(Bed(new FloatWaterBedKnot(3000,1000),new FloatWaterBedKnot(11760,1000),
                new FloatWaterBedKnot(11880,1100),new FloatWaterBedKnot(12000,1000),new FloatWaterBedKnot(80000,1000)));
            BottomContact c;Require(patch.Resolve(3,0,12000,50000,out c)=="bed-too-steep"&&c==null,"steep crest");
            Require(patch.Resolve(3,0,12240,50000,out c)==null,"outside crest");
        });
        Test("bed contact uses bottom soundings even below float clearance",delegate{
            var water=Bed(new FloatWaterBedKnot(3000,180),new FloatWaterBedKnot(17000,600),new FloatWaterBedKnot(80000,600));
            Require(!water.Sample(0,4000,3).Accepted,"the float rig cannot work here");
            var s=Start(4000,Patch(water));var state=Until(s,BottomPhase.Stabilizing);
            Require(state.SinkerYmm==-210&&state.BaitYmm==-202,"distinct sounded heights");
        });
        Test("strict integer mainline budget includes anchor offset and full-depth slack",delegate{
            var water=Bed(new FloatWaterBedKnot(3000,1000),new FloatWaterBedKnot(80000,1000));
            var patch=Patch(water);BottomContact c;
            int expected=(int)Math.Ceiling(Math.Sqrt(5000d*5000+2500d*2500+12000d*12000))+180;
            Require(patch.Resolve(3,0,12000,expected,out c)==null&&c.RequiredLineMm==expected,"exact budget");
            Require(patch.Resolve(3,0,12000,expected-1,out c)=="line-insufficient"&&c==null,"short by one");
        });
        Test("deeper sounding settles later with conserved line and bounded leader each step",delegate{
            var shallow=Start();long near=Until(shallow,BottomPhase.Stabilizing).StepIndex;
            var s=Start(30000);int last=0;var p=s.Snapshot;
            while(p.Phase==BottomPhase.Sinking){
                Require(p.OutLineMm>=last&&p.OnSpoolMm+p.OutLineMm==60000&&p.OnSpoolMm>=0,"line balance");
                long dy=p.SinkerYmm-p.BaitYmm,dz=p.Contact.Zmm-p.Contact.BaitZmm;
                Require(dy*dy+dz*dz<=400L*400&&p.BaitYmm>=p.Contact.BaitYmm&&p.EncounterId==Guid.Empty,"leader/bed/encounter");
                last=p.OutLineMm;p=s.Advance(Session,50).Snapshot;
            }
            Require(p.Phase==BottomPhase.Stabilizing&&p.SinkerOnBed&&p.BaitOnBed&&p.OutLineMm==p.Contact.RequiredLineMm&&
                p.StepIndex>near&&p.SinkerYmm==-2400&&p.BaitYmm==-2384,"landed");
        });
        Test("contact requires five quiet steps before any search",delegate{
            var s=Start();var settled=Until(s,BottomPhase.Stabilizing);
            for(int i=0;i<4;i++){
                var p=s.Advance(Session,50).Snapshot;Require(p.Phase==BottomPhase.Stabilizing&&p.EncounterId==Guid.Empty,"too early");
            }
            var search=s.Advance(Session,50).Snapshot;
            Require(search.Phase==BottomPhase.Searching&&search.StepIndex==settled.StepIndex+5&&search.EncounterId==Guid.Empty,"start");
        });
        Test("zero rate and empty weighted table do not manufacture fish or signals",delegate{
            foreach(var env in new[]{Fish(0),Fish(1000,0)}){
                var s=Start(env:env);for(int i=0;i<140;i++)s.Advance(Session,250);
                var p=s.Snapshot;Require(p.Phase==BottomPhase.Searching&&p.EncounterId==Guid.Empty&&p.TipOffsetMm<=1&&p.SignalSequence==0,"empty");
            }
        });
        Test("tip pulses are distinct and identified, without automatic hooking",delegate{
            var s=Start();var a=Until(s,BottomPhase.Approaching);var n=Until(s,BottomPhase.Nibbling);
            int peak=0;for(int i=0;i<9;i++)peak=Math.Max(peak,s.Advance(Session,50).Snapshot.TipOffsetMm);
            var w=Until(s,BottomPhase.Strikeable);
            Require(a.TipOffsetMm<=1&&a.SignalSequence==0&&n.SignalSequence==1&&peak==16&&w.SignalSequence==2&&
                w.TipOffsetMm==28&&w.SessionId==Session&&w.RigId==RigId&&w.RigRevision==9&&w.EncounterId==n.EncounterId,"tip");
            Require(s.Advance(Session,250).Target==null,"automatic fight");
        });
        Test("render cadence and pause do not alter the encounter or opening",delegate{
            var reference=Start();var w=Until(reference,BottomPhase.Strikeable);var other=Start();
            int elapsed=checked((int)(w.StepIndex*50)),sent=0,index=0;var chunks=new[]{16,17,33,17,0,50,16,34};
            while(sent<elapsed){int n=Math.Min(elapsed-sent,chunks[index++%chunks.Length]);other.Advance(Session,n);sent+=n;}
            var p=other.Snapshot;
            Require(p.StepIndex==w.StepIndex&&p.Phase==w.Phase&&p.OpenStep==w.OpenStep&&p.CloseStep==w.CloseStep&&
                p.EncounterId==w.EncounterId&&p.TipOffsetMm==w.TipOffsetMm&&p.RemainderMilliseconds==0,"cadence");
            Require(Strike(reference).Target.MassMg==Strike(other).Target.MassMg,"target draw");
        });
        Test("wrong session rig revision encounter and stand inputs leave the live window intact",delegate{
            var s=Start();var w=Until(s,BottomPhase.Strikeable);
            Require(s.Advance(Id(70),250).Code=="session-not-current"&&
                s.Strike(Id(70),RigId,9,w.EncounterId,true).Code=="session-not-current"&&
                s.Strike(Session,Id(70),9,w.EncounterId,true).Code=="rig-not-current"&&
                s.Strike(Session,RigId,8,w.EncounterId,true).Code=="rig-not-current"&&
                s.Strike(Session,RigId,9,w.EncounterId,false).Code=="not-held"&&
                s.Strike(Session,RigId,9,Id(70),true).Code=="encounter-not-current","authority");
            Require(s.Snapshot.StepIndex==w.StepIndex&&Strike(s).ShouldStartFight,"valid input");
        });
        Test("one manual hook at opening or last valid step returns the locked same encounter once",delegate{
            foreach(bool last in new[]{false,true}){
                var s=Start();var w=Until(s,BottomPhase.Strikeable);Require(w.OpenStep==w.StepIndex&&w.CloseStep-w.OpenStep==20,"window");
                while(last&&s.Snapshot.StepIndex<w.CloseStep-1)s.Advance(Session,50);
                // A new mass range for the same species must not replace its chosen body.
                s.SetEnvironment(Session,new FloatBiteEnvironment(1,new[]{new FloatBiteCandidate("carp",1,1000000,1000000)}));
                var result=Strike(s);Require(result.ShouldStartFight&&result.Target.SpeciesId=="carp"&&result.Target.MassMg>=10000&&
                    result.Target.MassMg<=19999&&result.Snapshot.EncounterId==w.EncounterId&&result.Snapshot.Phase==BottomPhase.Hooked,"target");
                Require(!Strike(s).ShouldStartFight&&s.Advance(Session,250).Target==null,"only one");
            }
        });
        Test("closed step expires, empty or early strikes cannot carry forward",delegate{
            var s=Start();var w=Until(s,BottomPhase.Strikeable);
            while(s.Snapshot.StepIndex<w.CloseStep)s.Advance(Session,50);
            Require(s.Snapshot.Reason=="bite-missed"&&!s.Snapshot.BaitAttached&&!Strike(s).Accepted,"close");
            foreach(var phase in new[]{BottomPhase.Sinking,BottomPhase.Searching,BottomPhase.Nibbling}){
                var t=Start();Until(t,phase);var result=Strike(t);
                Require(result.Accepted&&!result.ShouldStartFight&&!result.Snapshot.BaitAttached&&
                    result.Snapshot.Phase==BottomPhase.RetrieveRequired&&!t.Advance(Session,250).Accepted,"early");
            }
        });
        Test("uncertified environment or removed target interrupts without producing a fish",delegate{
            var s=Start();s.SetEnvironment(Session,Fish(0,1,false));Require(s.Snapshot.Reason=="environment-interrupted"&&!Strike(s).Accepted,"pre-contact");
            s=Start();Until(s,BottomPhase.Approaching);
            var result=s.SetEnvironment(Session,new FloatBiteEnvironment(1000,new FloatBiteCandidate[0]));
            Require(result.Code=="environment-interrupted"&&!result.ShouldStartFight&&!Strike(s).Accepted,"lost habitat");
        });
        Test("deep water times out in active time with no free target",delegate{
            var depth=Bed(new FloatWaterBedKnot(3000,50000),new FloatWaterBedKnot(80000,50000));
            var s=Start(12000,Patch(depth),line:100000);s.Advance(Session,49);
            Require(s.Snapshot.StepIndex==0&&s.Advance(Session,0).Snapshot.RemainderMilliseconds==49,"pause");
            s.Advance(Session,1);
            while(s.Snapshot.StepIndex<BottomRigSimulation.MaximumSettlingSteps)s.Advance(Session,250);
            Require(s.Snapshot.Reason=="settling-timeout"&&s.Snapshot.StepIndex==600&&s.Snapshot.RemainderMilliseconds==0&&
                s.Snapshot.BaitAttached&&!s.Snapshot.SinkerOnBed&&s.Snapshot.EncounterId==Guid.Empty&&!Strike(s).ShouldStartFight,"timeout");
        });
        Test("end is session-bound and prevents late activity and repeated terminal actions",delegate{
            var s=Start();Until(s,BottomPhase.Nibbling);
            Require(!s.End(Id(70)).Accepted&&s.End(Session).Accepted&&!s.End(Session).Accepted,"end once");
            Require(s.Snapshot.Phase==BottomPhase.Ended&&!s.Snapshot.BaitAttached&&s.Snapshot.TipOffsetMm==0&&
                !Strike(s).Accepted&&!s.Advance(Session,50).Accepted,"late action");
        });
        return failures==0?0:1;
    }
}
