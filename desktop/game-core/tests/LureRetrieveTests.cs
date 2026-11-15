using System;
using System.Collections.Generic;
using FishingDesktop.GameCore;

static class LureRetrieveTests
{
    static readonly Guid Session=Id(1),RigId=Id(2);
    static int failed;
    static Guid Id(int n){return new Guid(n,0,0,new byte[8]);}
    static void Need(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Near(double a,double b,double tolerance=1e-8){Need(Math.Abs(a-b)<=tolerance,"observed "+a+", expected "+b);}
    static void Test(string name,Action action)
    {try{action();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+" "+e);}}
    static LureRetrieveRig Rig(int line=50000){return new LureRetrieveRig(RigId,7,Id(3),Id(4),Id(5),Id(6),3,line);}
    static FloatBiteCandidate Bass(int weight=1){return new FloatBiteCandidate("spotted-seabass",weight,480000,2600000);}
    static FloatCastLaunchPlan Plan(string water="coastal-bay",int u=6000,int v=3000,int power=1000)
    {
        var g=FloatFishingAtlas.Find(water).Geometry;
        var planned=FloatCastLaunchPlanner.Plan(g,u,v,g.Version,power);
        Need(planned.Accepted,planned.Code);return planned.Plan;
    }
    static FloatFlightSimulation Launch(FloatCastLaunchPlan p)
    {
        var g=p.Geometry;
        return new FloatFlightSimulation(Session,new FloatFlightLaunch(new FloatFlightPoint(p.AnchorXmm,p.AnchorYmm,p.AnchorZmm),
            new FloatFlightPoint(p.Target.Xmm,p.Target.SurfaceYmm,p.Target.Zmm),new FloatFlightWater(g.SurfaceYmm,g.MinimumXmm,g.MaximumXmm,g.MinimumZmm,g.MaximumZmm),
            new FloatFlightRules(p.MinimumSpeedMmPerSecond,p.FullSpeedMmPerSecond,p.LaunchAngleDegrees,p.GravityMmPerSecondSquared,p.MaximumFlightMilliseconds),p.PowerPermille,50000));
    }
    static FloatFlightSnapshot Land(FloatCastLaunchPlan p)
    {
        var flight=Launch(p);while(flight.Snapshot.Phase==FloatFlightPhase.Flying)flight.Advance(Session,50);
        Need(flight.Snapshot.Phase==FloatFlightPhase.Waiting,"real flight missed");return flight.Snapshot;
    }
    sealed class Run
    {
        internal readonly FloatCastLaunchPlan Plan;
        internal readonly FloatFlightSnapshot Contact;
        internal readonly LureRetrieveSimulation Sim;
        internal long Sequence,WorldMs;
        internal Run(uint seed=1,int weight=1,int v=3000,LureActivitySampler activity=null){
            Plan=LureRetrieveTests.Plan(v:v);Contact=Land(Plan);
            LureRetrieveSimulation sim;
            string err=LureRetrieveSimulation.AtWaterContact(Session,Rig(),Plan,Contact,Bass(weight),seed,true,out sim,activity);
            Need(err==null,err);Sim=sim;Sim.SetControl(1,true);
        }
        internal LureRetrieveSnapshot S{get{return Sim.Snapshot;}}
        internal void Step(int count){for(int i=0;i<count;i++){WorldMs+=50;Sim.Advance(Session,50,false,WorldMs);}}
        internal void Reel(bool down){Need(Sim.SetReeling(RigId,Session,1,++Sequence,down)==null,"reel input");}
        internal void Speed(int level){Need(Sim.SetSpeed(RigId,Session,1,++Sequence,level)==null,"speed input");}
        internal void Bail(bool open){Need(Sim.SetBail(RigId,Session,1,++Sequence,open)==null,"bail input");}
        internal void WaitFor(LureRetrievePhase phase,int limit=800)
        {for(int i=0;i<limit&&S.Phase!=phase;i++)Step(1);Need(S.Phase==phase,"expected "+phase+", got "+S.Phase);}
    }
    static void Invariant(Run r)
    {
        var s=r.S;var g=r.Plan.Geometry;var p=s.Position;
        double x=p.X-g.AnchorXmm/1000d,y=p.Y-g.AnchorYmm/1000d,z=p.Z-g.AnchorZmm/1000d;
        Near(s.OutLineM+s.RemainingLineM,50);Need(s.OutLineM+1e-8>=Math.Sqrt(x*x+y*y+z*z)&&s.SlackM>=0&&s.RemainingLineM>=0,"line constraint");
        var sample=g.Sample((int)Math.Round(p.X*1000),(int)Math.Round(p.Z*1000),g.Version);
        Need(sample.Accepted&&p.Y>=sample.Sample.BottomYmm/1000d-.002&&p.Y<=sample.Sample.SurfaceYmm/1000d+1e-8,"water constraint");
        Near(s.DepthM+s.BedClearanceM,sample.Sample.DepthMm/1000d,.002);
    }
    static int Main()
    {
        Test("confirmed coastal flight, installed spool and reusable spoon identities survive water contact",()=>{
            var r=new Run();var s=r.S;
            Need(s.Phase==LureRetrievePhase.InWater&&!s.Reeling&&!s.BailOpen&&s.EncounterId==Guid.Empty&&s.Rig.LureId==Id(6),"landing");
            Near(s.Position.X*1000,r.Contact.Xmm);Near(s.Position.Z*1000,r.Contact.Zmm);Near(s.OutLineM*1000,r.Contact.LineUsedMm);Invariant(r);
        });
        Test("wrong session, unlanded flight, unavailable station, wrong frozen plan and short line refuse admission",()=>{
            var p=Plan();var c=Land(p);LureRetrieveSimulation run;
            Need(LureRetrieveSimulation.AtWaterContact(Id(7),Rig(),p,c,Bass(),1,true,out run)=="confirmed-contact-required","old session");
            Need(LureRetrieveSimulation.AtWaterContact(Session,Rig(),p,Launch(p).Snapshot,Bass(),1,true,out run)=="confirmed-contact-required","flying");
            var other=Plan("river-bend");Need(LureRetrieveSimulation.AtWaterContact(Session,Rig(),other,Land(other),Bass(),1,true,out run)=="lure-spot-unavailable","wrong station");
            Need(LureRetrieveSimulation.AtWaterContact(Session,Rig(),Plan(u:3000),c,Bass(),1,true,out run)=="lure-contact-invalid","wrong plan");
            Need(LureRetrieveSimulation.AtWaterContact(Session,Rig(3000),p,c,Bass(),1,true,out run)=="lure-contact-line-mismatch","short installed spool");
            Need(LureRetrieveSimulation.AtWaterContact(Session,Rig(),p,c,new FloatBiteCandidate("carp",1,480000,900000),1,true,out run)=="lure-species-unconfigured"&&run==null,"wrong fish");
            try{new LureRetrieveRig(RigId,1,Id(3),Id(4),Id(5),Id(3),1,50000);throw new Exception("aliased equipment");}catch(ArgumentException){}
        });
        Test("stationary closed spoon sinks on its line arc but gains no fishing exposure",()=>{
            var r=new Run();double original=r.S.OutLineM,z=r.S.Position.Z;
            for(int i=0;i<250;i++){r.Step(1);Invariant(r);Need(r.S.EncounterId==Guid.Empty&&r.S.HazardPerSecond==0&&r.S.Exposure==0,"idle bite");}
            Need(r.S.Phase==LureRetrievePhase.BottomRecovery&&r.S.Position.Z<z,"no bed or line swing");Near(r.S.OutLineM,original);
        });
        Test("open bail pays finite measured line; closing alone does not start retrieval",()=>{
            var r=new Run();var a=r.S;r.Bail(true);r.Step(30);var b=r.S;
            Need(b.DepthM>a.DepthM&&b.OutLineM>a.OutLineM&&b.OutLineM-a.OutLineM<=1.5*.75,"unbounded or absent payout");Near(a.Position.Z,b.Position.Z);
            r.Bail(false);Need(!r.S.Reeling&&!r.S.BailOpen,"closing bail reeled");double line=r.S.OutLineM;r.Step(8);Near(r.S.OutLineM,line);
            r.Bail(true);r.Reel(true);Need(r.S.Reeling&&!r.S.BailOpen,"fresh left input");r.Step(5);Invariant(r);
        });
        Test("speed gears measure actual line pickup and alter both trajectory and swimming depth",()=>{
            var slow=new Run(weight:0);var fast=new Run(weight:0);slow.Step(32);fast.Step(32);
            slow.Speed(0);fast.Speed(3);slow.Reel(true);fast.Reel(true);
            for(int i=0;i<32;i++){slow.Step(1);fast.Step(1);Invariant(slow);Invariant(fast);}
            Need(fast.S.OutLineM<slow.S.OutLineM-1&&fast.S.Position.Z<slow.S.Position.Z&&fast.S.DepthM<slow.S.DepthM,"gears had no actual effect");
        });
        Test("depth bands, current clearance and actual speed set each step's attack rate",()=>{
            var r=new Run(seed:18);r.Reel(true);r.Step(6);Need(r.S.Layer==LureWaterLayer.Surface&&r.S.HazardPerSecond==0,"surface");
            r.Step(10);double upper=r.S.HazardPerSecond;Need(r.S.Layer==LureWaterLayer.Upper&&upper>0,"upper");
            r.Step(35);Need(r.S.Layer==LureWaterLayer.Middle&&r.S.HazardPerSecond>upper,"middle");
            r.Speed(4);r.Step(10);Need(r.S.HazardPerSecond==0,"outside working speed");
        });
        Test("pause opportunity requires preceding movement, expires and cannot be reset by short clicks",()=>{
            var r=new Run(seed:18);r.Reel(true);r.Step(25);r.Reel(false);r.Step(1);
            Need(r.S.HazardPerSecond>0&&!r.S.Reeling,"qualified falling spoon");r.Step(12);Need(r.S.HazardPerSecond==0,"end of pause exposure");
            for(int i=0;i<8;i++){r.Reel(true);r.Step(1);r.Reel(false);r.Step(1);Need(r.S.HazardPerSecond==0,"pulsing renews pause");}
        });
        Test("pure empty species weights allow retrieval without any target or reward",()=>{
            var r=new Run(weight:0);r.Reel(true);
            while(r.S.Phase!=LureRetrievePhase.RetrievePending){r.Step(1);Need(r.S.Step<1600&&r.S.HazardPerSecond==0&&r.S.Target==null,"empty pool");}
            Need(Math.Abs(r.S.Position.Z-r.Plan.Geometry.MinimumZmm/1000d)<1e-7,"world pickup edge");
        });
        Test("early strike and outrunning a following fish abandon only that encounter",()=>{
            var r=new Run();r.Reel(true);r.WaitFor(LureRetrievePhase.Following);var locked=r.S;
            FloatFightSimulation f;
            Need(r.Sim.StrikeAndStart(RigId,Session,1,++r.Sequence,locked.EncounterId,out f)=="empty-strike"&&f==null&&r.S.SearchEpoch==locked.SearchEpoch+1&&r.S.Target==null,"early strike");
            r.WaitFor(LureRetrievePhase.Following);Need(r.S.EncounterId!=locked.EncounterId,"encounter id reused");
            r.Speed(4);r.Step(1);Need(r.S.Phase==LureRetrievePhase.InWater&&r.S.EncounterId==Guid.Empty,"fish chased out of range");
        });
        Test("real attack window carries the same fish, position, weight and line once into the fight",()=>{
            var r=new Run();r.Reel(true);r.WaitFor(LureRetrievePhase.Strikeable);var locked=r.S;
            Need(locked.OpenStep==locked.Step&&locked.CloseStep>locked.Step&&locked.Reeling,"open edge");
            FloatFightSimulation f;Need(r.Sim.StrikeAndStart(RigId,Session,1,++r.Sequence,Id(99),out f)=="encounter-not-current"&&f==null,"foreign target");
            Need(r.Sim.StrikeAndStart(RigId,Session,1,++r.Sequence,locked.EncounterId,out f)==null,"strike");var s=f.Snapshot;
            Need(s.SessionId==Session&&s.EncounterId==locked.EncounterId&&Object.ReferenceEquals(locked.Target,s.Target)&&s.RigRevision==7&&!s.Reeling&&!r.S.Reeling,"fight identity");
            Near(s.Position.X,locked.Position.X);Near(s.Position.Y,locked.Position.Y);Near(s.Position.Z,locked.Position.Z);
            Near(s.OutLineM,locked.OutLineM);Near(s.SpoolRemainingM,locked.RemainingLineM);
            FloatFightSimulation duplicate;Need(r.Sim.StrikeAndStart(RigId,Session,1,++r.Sequence,locked.EncounterId,out duplicate)=="lure-not-active"&&duplicate==null,"duplicate");
            Need(f.Advance(Session,50).Accepted&&f.Snapshot.Target==locked.Target,"physical successor");
        });
        Test("expiry at the exclusive close step does not erase the spoon or permit a late hook",()=>{
            var r=new Run();r.Reel(true);r.WaitFor(LureRetrievePhase.Strikeable);var before=r.S;
            r.Step((int)(before.CloseStep-r.S.Step-1));Need(r.S.Phase==LureRetrievePhase.Strikeable,"closed early");
            r.Step(1);FloatFightSimulation f;
            Need(r.S.Step==before.CloseStep&&r.S.Phase==LureRetrievePhase.InWater&&r.S.Rig.LureId==before.Rig.LureId&&
                r.Sim.StrikeAndStart(RigId,Session,1,++r.Sequence,before.EncounterId,out f)=="encounter-not-current"&&f==null,"late strike");
        });
        Test("the hooked coastal bass can be manually reeled over the same reef slope into the equipped net",()=>{
            var r=new Run();r.Reel(true);r.WaitFor(LureRetrievePhase.Strikeable);var bite=r.S;
            FloatFightSimulation f;Need(r.Sim.StrikeAndStart(RigId,Session,1,++r.Sequence,bite.EncounterId,out f)==null,"no hooked fish");
            f.SetDragLevel(Session,bite.EncounterId,6);f.SetReelLevel(Session,bite.EncounterId,10);f.SetReeling(Session,bite.EncounterId,true);
            for(int i=0;i<2200&&f.Snapshot.Phase==FloatFightPhase.Fighting;i++){
                f.Advance(Session,50);var point=f.Snapshot.Position;
                var bottom=r.Plan.Geometry.Sample((int)Math.Round(point.X*1000),(int)Math.Round(point.Z*1000),r.Plan.GeometryVersion);
                Need(bottom.Accepted&&point.Y>=bottom.Sample.BottomYmm/1000d-.002,"fish crossed surveyed reef");
            }
            Need(f.Snapshot.Phase==FloatFightPhase.LandingEligible,"fish never reached net: "+f.Snapshot.Phase+" / "+f.Snapshot.LandingReason);
            var result=f.TryLand(Session,bite.EncounterId,Id(750));Need(result.ShouldCommitLanding&&result.Snapshot.Target==bite.Target,"different fish in net");
        });
        Test("a frozen open attack retains its remaining window after resume, opening the bail prevents a hook",()=>{
            var r=new Run();r.Reel(true);r.WaitFor(LureRetrievePhase.Strikeable);var open=r.S;
            r.Sim.Advance(Session,250,true);r.Sim.Advance(Session,50,false);
            Need(r.S.Step==open.Step+1&&r.S.EncounterId==open.EncounterId&&r.S.Phase==LureRetrievePhase.Strikeable&&!r.S.Reeling,"window lost on resume");
            r.Bail(true);FloatFightSimulation f;
            Need(r.Sim.StrikeAndStart(RigId,Session,1,++r.Sequence,open.EncounterId,out f)=="lure-line-not-engaged"&&f==null,"hooked on open bail");
            r.Step(1);Need(r.S.Phase==LureRetrievePhase.InWater&&r.S.Target==null,"old attack retained");
        });
        Test("a diagonal cast from a displaced bank position cannot retrieve through an unreachable shore",()=>{
            var g=Plan().Geometry;
            var shifted=new FloatStillWaterGeometry(g.WaterId,g.SpotId,g.Version,8000,g.AnchorYmm,g.AnchorZmm,g.SurfaceYmm,
                g.MinimumXmm,g.MaximumXmm,g.MinimumZmm,g.MaximumZmm,g.AimMinimumXmm,g.AimMaximumXmm,g.AimMinimumZmm,g.AimMaximumZmm,g.BedKnots,g.ShelfDepthMm);
            var p=FloatCastLaunchPlanner.Plan(shifted,0,10000,shifted.Version,1000).Plan;LureRetrieveSimulation run;
            Need(p!=null&&LureRetrieveSimulation.CheckCast(Rig(),p,Bass())=="lure-return-path-unreachable"&&
                LureRetrieveSimulation.AtWaterContact(Session,Rig(),p,Land(p),Bass(),1,true,out run)=="lure-return-path-unreachable","impossible pickup accepted");
        });
        Test("pausing authority clears the held button without catch-up, stale inputs cannot resume winding",()=>{
            var r=new Run();r.Reel(true);r.Step(7);var before=r.S;r.Sim.Advance(Session,250,true);
            for(int i=0;i<120;i++)r.Sim.Advance(Session,250,true);
            Need(r.S.Step==before.Step&&!r.S.Reeling&&r.S.Position.Z==before.Position.Z&&r.S.Exposure==before.Exposure,"paused clock");
            Need(r.Sim.SetReeling(RigId,Session,1,++r.Sequence,true)=="control-required","input during pause");
            r.Sim.SetControl(2,false);r.Sim.Advance(Session,250);Need(r.S.Step==before.Step,"observer advanced");
            r.Sim.SetControl(3,true);r.Sim.Advance(Session,50);Need(r.S.Step==before.Step+1&&!r.S.Reeling,"stuck winding");
            Need(r.Sim.SetReeling(RigId,Session,1,100,true)=="control-required"&&r.Sim.SetReeling(RigId,Id(100),3,1,true)=="lure-not-current","stale ownership");
            Need(r.Sim.SetReeling(RigId,Session,3,1,true)==null&&r.Sim.SetReeling(RigId,Session,3,1,false)=="stale-input","duplicate sequence");
        });
        Test("bed recovery follows the measured shoal, awards no fish and confirms the same terminal ticket once",()=>{
            var r=new Run(v:7500);r.Step(200);Need(r.S.Phase==LureRetrievePhase.BottomRecovery,"bottom not reached");
            r.Speed(3);r.Reel(true);
            while(r.S.Phase!=LureRetrievePhase.RetrievePending){r.Step(1);Invariant(r);Need(r.S.Step<1200&&r.S.Target==null&&r.S.HazardPerSecond==0,"bottom search or no exit");}
            var t=r.S;Need(!t.Reeling&&t.RetrieveEventId!=Guid.Empty&&t.DepthM<.002,"pickup zone");
            Need(r.Sim.ConfirmRetrieve(Session,Id(110))=="retrieve-not-current"&&r.Sim.ConfirmRetrieve(Session,t.RetrieveEventId)==null&&
                r.Sim.ConfirmRetrieve(Session,t.RetrieveEventId)=="retrieve-replayed"&&r.S.Rig==t.Rig,"terminal identity");
        });
        Test("equal active milliseconds across frame partitions preserve trajectory, search epoch and fish identity",()=>{
            var a=new Run();var b=new Run();a.Reel(true);b.Reel(true);
            for(int i=0;i<45;i++){
                a.Sim.Advance(Session,17);a.Sim.Advance(Session,34);a.Sim.Advance(Session,49);b.Sim.Advance(Session,100);
                Need(a.S.Step==b.S.Step&&a.S.EncounterId==b.S.EncounterId&&a.S.Phase==b.S.Phase&&a.S.SearchEpoch==b.S.SearchEpoch,"frame dependence");
                Near(a.S.Position.X,b.S.Position.X);Near(a.S.Position.Z,b.S.Position.Z);Near(a.S.DepthM,b.S.DepthM);Near(a.S.OutLineM,b.S.OutLineM);Invariant(a);
                if(a.S.Target!=null)Need(a.S.Target.MassMg==b.S.Target.MassMg,"mass changed");
            }
        });
        Test("moving spoon samples both local coastal depth bands at its actual submerged coordinates and active time",()=>{
            var water=FloatFishingAtlas.Find("coastal-bay");var climate=new ChinaWaterConditions(91,30000);
            FloatFightPoint last=null;int calls=0,lastRate=0;long lastTime=-1;bool shallow=false,deep=false;
            var r=new Run(activity:(p,depth,t)=>{
                var sample=water.Geometry.Sample((int)Math.Round(p.X*1000),(int)Math.Round(p.Z*1000),water.Geometry.Version);
                Need(sample.Accepted&&p.Y*1000<=sample.Sample.SurfaceYmm&&p.Y*1000>=sample.Sample.BottomYmm-2,"invalid working point");
                Near(sample.Sample.DepthMm,depth*1000,2);
                bool isDeep=sample.Sample.DepthMm>=water.DeepThresholdMm;deep|=isDeep;shallow|=!isDeep;
                calls++;last=p;lastTime=t;lastRate=climate.Sample(water,t).ActivityPermille("spotted-seabass",isDeep);
                return lastRate;
            });
            r.Reel(true);int moving=0;
            while(r.S.Phase!=LureRetrievePhase.RetrievePending){
                int before=calls;r.Step(1);Need(r.S.Step<1500,"failed to reach shore");
                if(before==calls)continue;
                Near(last.X,r.S.Position.X);Near(last.Y,r.S.Position.Y);Near(last.Z,r.S.Position.Z);
                Need(lastTime==r.WorldMs&&lastRate==r.S.ActivityPermille,"stale world sample");
                if(r.S.Step>6&&r.S.Layer==LureWaterLayer.Middle&&r.S.BedClearanceM>.12&&r.S.HazardPerSecond>0){
                    Near(r.S.HazardPerSecond,.60*r.S.ActivityPermille/1000.0);moving++;
                }
            }
            Need(deep&&shallow&&moving>10&&calls>100,"only the initial landing or one layer was sampled");
        });
        Test("zero activity or no eligible bass produces no encounter, increased activity cannot animate a stopped lure",()=>{
            var zero=new Run(activity:(p,d,t)=>0);
            var empty=new Run(weight:0,activity:(p,d,t)=>2000);
            var still=new Run(activity:(p,d,t)=>2000);
            zero.Reel(true);empty.Reel(true);
            for(int i=0;i<150;i++){
                zero.Step(1);empty.Step(1);still.Step(1);
                Need(zero.S.Exposure==0&&empty.S.Exposure==0&&still.S.Exposure==0&&
                    zero.S.Target==null&&empty.S.Target==null&&still.S.Target==null,"ecology generated a fish without an eligible presentation");
            }
            var normal=new Run(seed:18);var high=new Run(seed:18,activity:(p,d,t)=>1700);
            normal.Reel(true);high.Reel(true);normal.Step(18);high.Step(18);
            Need(normal.S.ActivityPermille==1000&&normal.S.HazardPerSecond>0,"original rate lost");
            Near(high.S.HazardPerSecond,normal.S.HazardPerSecond*1.7);
        });
        Test("weather changes during a committed follow never substitute a new encounter or fish",()=>{
            int rate=1000;var r=new Run(activity:(p,d,t)=>rate);r.Reel(true);r.WaitFor(LureRetrievePhase.Following);
            var following=r.S;rate=0;r.Step(1);r.WaitFor(LureRetrievePhase.Strikeable,40);var attacking=r.S;
            Need(attacking.HazardPerSecond==0&&attacking.ActivityPermille==0&&attacking.EncounterId==following.EncounterId&&
                Object.ReferenceEquals(attacking.Target,following.Target),"weather redrew the committed follower");
            FloatFightSimulation fight;
            Need(r.Sim.StrikeAndStart(RigId,Session,1,++r.Sequence,attacking.EncounterId,out fight)==null&&
                Object.ReferenceEquals(fight.Snapshot.Target,following.Target),"handoff lost the locked fish");
        });
        Test("each fixed step uses its exact world time across display partitions and paused input",()=>{
            var aTimes=new List<long>();var bTimes=new List<long>();
            var a=new Run(activity:(p,d,t)=>{aTimes.Add(t);return (int)(t%1000)+550;});
            var b=new Run(activity:(p,d,t)=>{bTimes.Add(t);return (int)(t%1000)+550;});
            a.Reel(true);b.Reel(true);long start=9021;
            for(int i=0;i<40;i++){
                long t=start+100*i;
                a.Sim.Advance(Session,17,false,t+17);a.Sim.Advance(Session,34,false,t+51);a.Sim.Advance(Session,49,false,t+100);
                b.Sim.Advance(Session,100,false,t+100);
                Need(a.S.EncounterId==b.S.EncounterId&&a.S.Phase==b.S.Phase&&a.S.ActivityPermille==b.S.ActivityPermille,"frame-dependent fish response");
                Near(a.S.Exposure,b.S.Exposure);
            }
            Need(aTimes.Count==80&&aTimes.Count==bTimes.Count,"missed fixed step");
            for(int i=0;i<aTimes.Count;i++)Need(aTimes[i]==start+50*(i+1)&&aTimes[i]==bTimes[i],"end-of-frame weather applied to earlier physics step");
            a.Sim.Advance(Session,250,true,start+4250);Need(aTimes.Count==80,"paused time sampled conditions");
            a.Sim.Advance(Session,50,false,start+4300);Need(aTimes.Count==81&&aTimes[80]==start+4300&&!a.S.Reeling,"resume backfilled missed samples");
        });
        return failed==0?0:1;
    }
}
