using System;
using System.Collections.Generic;
using FishingDesktop.GameCore;

static class BottomFightHandoffTests
{
    static readonly Guid Session=G(100),RigId=G(101);
    static int failures;
    static Guid G(int n){return new Guid(n,0,0,new byte[8]);}
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Near(double a,double b){Check(Math.Abs(a-b)<1e-9,"expected "+b+", observed "+a);}
    static void Test(string name,Action body)
    {try{body();Console.WriteLine("PASS "+name);}catch(Exception e){failures++;Console.WriteLine("FAIL "+name+" "+e.Message);}}
    static BottomRigPart Part(BottomPartKind kind,string id){return new BottomRigPart(G((int)kind+1),kind,id);}
    static BottomSimpleAssembly Rig(int total=50000)
    {return new BottomSimpleAssembly(RigId,7,"bottom_simple",total,Part(BottomPartKind.Rod,"bottom-bank-rod"),
        Part(BottomPartKind.Reel,"bottom-bank-reel"),Part(BottomPartKind.SpoolLine,"bottom-022-line"),
        Part(BottomPartKind.Leader,"bottom-016-leader"),Part(BottomPartKind.Hook,"bottom-single-hook"),
        Part(BottomPartKind.Sinker,"bottom-18g-sinker"),Part(BottomPartKind.HookBait,"worm"));}
    static FloatStillWaterGeometry Water()
    {
        return new FloatStillWaterGeometry("bottom","slope",4,0,1500,0,0,-20000,20000,3000,60000,-4000,4000,9000,30000,
            new[]{new FloatWaterBedKnot(3000,500),new FloatWaterBedKnot(12000,1250),new FloatWaterBedKnot(20000,2130),
            new FloatWaterBedKnot(30000,3000),new FloatWaterBedKnot(60000,4800)});
    }
    static FloatBiteEnvironment Pool(string species="carp",long min=1100000,long max=1400000,int weight=1)
    {return new FloatBiteEnvironment(1000,new[]{new FloatBiteCandidate(species,weight,min,max)});}
    static FloatFightFishRules Fish(FloatFightBehavior behavior)
    {return new FloatFightFishRules(2,2,20,.05,0,10,behavior);}
    static BottomFightConfig Config(FloatFightBehavior behavior=FloatFightBehavior.Rest,bool net=true,double minLine=3)
    {
        var rig=new FloatFightRigRules(80,1,40,200,1,20,.2,1.6,40,3,minLine,10,10);
        return new BottomFightConfig(rig,new FloatLandingRules(net,5,1,2,1),
            new Dictionary<string,FloatFightFishRules>{{"carp",Fish(behavior)}});
    }
    static string Create(out BottomFightHandoff owner,BottomFightConfig config=null,FloatBiteEnvironment pool=null,
        int total=50000,int x=1000,int z=25000,int minX=-8000,int maxX=8000)
    {
        var patch=new BottomSurveyPatch(Water(),minX,maxX,3000,39000,BottomSubstrate.FirmGravel,true);
        return BottomFightHandoff.AtWaterContact(Session,Rig(total),patch,4,x,z,42,pool??Pool(),config??Config(),out owner);
    }
    static BottomFightHandoff Owner(BottomFightConfig config=null,int x=1000,int minX=-8000,int maxX=8000)
    {
        BottomFightHandoff owner;string reason=Create(out owner,config,x:x,minX:minX,maxX:maxX);
        Check(owner!=null&&reason==null,"factory "+reason);return owner;
    }
    static void Window(BottomFightHandoff owner)
    {
        for(int i=0;i<200&&owner.Snapshot.Phase!=BottomPhase.Strikeable;i++)owner.Advance(Session,50);
        Check(owner.Snapshot.Phase==BottomPhase.Strikeable,"wait for actual bite");
    }
    static FloatFightSimulation Start(BottomFightHandoff owner)
    {
        Window(owner);FloatFightSimulation f;
        var result=owner.StrikeAndStart(Session,RigId,7,owner.Snapshot.EncounterId,true,out f);
        Check(result.ShouldStartFight&&f!=null&&Object.ReferenceEquals(result.Target,f.Snapshot.Target),"real fish transferred");return f;
    }
    static double Floor(double z)
    {
        if(z<12)return -.5-(z-3)*(.75/9);
        if(z<20)return -1.25-(z-12)*(.88/8);
        if(z<30)return -2.13-(z-20)*(.87/10);
        return -3-(z-30)*(.06);
    }
    static int Main()
    {
        Test("same confirmed encounter, rig revision, target and actual bait-bed start",delegate{
            var owner=Owner();var fight=Start(owner);var p=fight.Snapshot;var b=owner.Snapshot;
            Check(p.SessionId==Session&&p.EncounterId==b.EncounterId&&p.RigRevision==7&&p.Target.SpeciesId=="carp"&&
                p.Target.MassMg>=1100000&&p.Target.MassMg<=1400000&&!p.Reeling,"snapshot identity");
            Near(p.Position.X,b.Contact.Xmm/1000d);Near(p.Position.Z,b.Contact.BaitZmm/1000d);Near(p.Position.Y,b.Contact.BaitYmm/1000d);
            Check(Math.Abs(p.Position.Y-b.Contact.SinkerYmm/1000d)>.01&&p.Position.Y< -2,"no float or sinker substitution");
            Near(p.OutLineM,b.OutLineMm/1000d);Near(p.SpoolRemainingM,b.OnSpoolMm/1000d);
        });
        Test("stale identity, not held or stale encounter creates no fight and leaves strike open",delegate{
            var owner=Owner();Window(owner);var original=owner.Snapshot;FloatFightSimulation f;
            Check(!owner.StrikeAndStart(G(90),RigId,7,original.EncounterId,true,out f).Accepted&&f==null,"session");
            Check(!owner.StrikeAndStart(Session,RigId,6,original.EncounterId,true,out f).Accepted&&f==null,"revision");
            Check(!owner.StrikeAndStart(Session,RigId,7,original.EncounterId,false,out f).Accepted&&f==null,"held");
            Check(!owner.StrikeAndStart(Session,RigId,7,G(90),true,out f).Accepted&&f==null,"encounter");
            Check(owner.Snapshot.OpenStep==original.OpenStep&&Start(owner)!=null,"retry");
        });
        Test("one right-click creates one fight and bottom owner cannot terminate its successor",delegate{
            var owner=Owner();var first=Start(owner);var locked=first.Snapshot;FloatFightSimulation duplicate;
            var result=owner.StrikeAndStart(Session,RigId,7,locked.EncounterId,true,out duplicate);
            Check(!result.Accepted&&!result.ShouldStartFight&&duplicate==null,"duplicate");
            Check(owner.End(Session).Code=="fight-owns-session"&&!owner.Advance(Session,250).Accepted,"ownership");
            var next=first.Advance(Session,50);Check(next.Accepted&&Object.ReferenceEquals(locked.Target,next.Snapshot.Target),"same fight");
        });
        Test("early, missed and ended sessions never allocate a fight",delegate{
            FloatFightSimulation f;var owner=Owner();
            Check(owner.StrikeAndStart(Session,RigId,7,Guid.Empty,true,out f).Code=="empty-strike"&&f==null,"early");
            owner=Owner();Window(owner);while(owner.Snapshot.Phase==BottomPhase.Strikeable)owner.Advance(Session,50);
            Check(!owner.StrikeAndStart(Session,RigId,7,owner.Snapshot.EncounterId,true,out f).Accepted&&f==null,"closed");
            owner=Owner();Window(owner);owner.End(Session);
            Check(!owner.StrikeAndStart(Session,RigId,7,owner.Snapshot.EncounterId,true,out f).Accepted&&f==null,"ended");
        });
        Test("unknown species or unsupported mass rejected before encounter or environment change",delegate{
            BottomFightHandoff invalid;
            Check(Create(out invalid,pool:Pool("unknown"))=="fight-species-unconfigured"&&invalid==null,"species");
            Check(Create(out invalid,pool:Pool(min:9999))=="fight-mass-unsupported"&&invalid==null,"mass");
            Check(Create(out invalid,pool:Pool(max:500000001))=="fight-mass-unsupported","upper mass");
            Check(Create(out invalid,pool:Pool("unused",1,1,0))==null,"no unsupported active candidates");
            var owner=Owner();Window(owner);
            Check(!owner.SetEnvironment(Session,Pool("unknown")).Accepted&&Start(owner)!=null,"invalid update");
        });
        Test("short spool or invalid minimum working line refused before casting reaches a bite",delegate{
            BottomFightHandoff owner;
            Check(Create(out owner,total:24000)=="line-insufficient"&&owner==null,"short spool");
            Check(Create(out owner,Config(minLine:30))=="fight-line-insufficient"&&owner==null,"minimum working line");
        });
        Test("continuous pull up surveyed slope never penetrates local bed and net uses current depth",delegate{
            var owner=Owner();var f=Start(owner);var initial=f.Snapshot;double previousY=initial.Position.Y;
            bool rising=false,crossedTwoKnots=false;
            f.SetReeling(Session,initial.EncounterId,true);
            for(int i=0;i<800;i++){
                var s=f.Advance(Session,50).Snapshot;
                Check(s.Phase==FloatFightPhase.Fighting||s.Phase==FloatFightPhase.LandingEligible,"physics "+s.Reason);
                Check(s.Position.Y>=Floor(s.Position.Z)-1e-10&&s.Position.Y<=0&&s.Position.Z>=3&&s.Position.Z<=39,"bed/shore");
                Check(Math.Abs((s.Position.Y-previousY)/.05-s.Velocity.Y)<1e-8,"vertical speed");
                Near(s.OutLineM+s.SpoolRemainingM,50);Check(Object.ReferenceEquals(initial.Target,s.Target),"target reroll");
                previousY=s.Position.Y;rising|=s.Velocity.Y>.001;crossedTwoKnots|=s.Position.Z<12;
                if(s.Phase==FloatFightPhase.LandingEligible)break;
            }
            var landed=f.Snapshot;Check(rising&&crossedTwoKnots&&landed.Position.Y>-.9&&landed.Phase==FloatFightPhase.LandingEligible,"landable slope");
            var attempt=f.TryLand(Session,initial.EncounterId,G(104));Check(attempt.ShouldCommitLanding&&attempt.Snapshot.Target==initial.Target,"landing");
            Check(f.ConfirmLanding(attempt.LandingTicket,attempt.LandingAttempt,FloatLandingOutcome.Landed).Snapshot.Phase==FloatFightPhase.Landed,"confirm");
        });
        Test("work rectangle need not include bank lateral coordinate, fish stays inside its survey",delegate{
            var owner=Owner(x:5000,minX:4000,maxX:7000);var f=Start(owner);f.SetReeling(Session,f.Snapshot.EncounterId,true);
            for(int i=0;i<400;i++){var p=f.Advance(Session,50).Snapshot;
                Check(p.Position.X>=4&&p.Position.X<=7&&p.Position.Z>=3&&p.Phase!=FloatFightPhase.Faulted,"bounds");}
            Check(f.Snapshot.Position.Z<9,"must approach bank inside patch");
        });
        Test("outward fish over deeper water retains original depth instead of teleporting to the bed",delegate{
            var owner=Owner(Config(FloatFightBehavior.Outward));var f=Start(owner);var p0=f.Snapshot;
            f.SetDragLevel(Session,p0.EncounterId,0);
            for(int i=0;i<180;i++){
                var p=f.Advance(Session,50).Snapshot;Check(p.Phase!=FloatFightPhase.Faulted,"outward load");
                Check(p.Position.Y>=Floor(p.Position.Z)-1e-9&&p.Position.Z<=39,"deeper water");
            }
            var last=f.Snapshot;Check(last.Position.Z>p0.Position.Z+1,"actual outward motion");Near(last.Position.Y,p0.Position.Y);
        });
        Test("survey shape is immutable and missing coverage or dry stations are refused",delegate{
            double[] z={3,12,20},y={-.5,-1.25,-2.13};var b=new FloatFightBed(z,y);y[1]=200;z[1]=200;
            Near(b.Elevation(12),-1.25);Near(b.Elevation(8),-.5-5*.75/9);
            bool bad=false;try{new FloatFightWater(0,-2.13,-2,2,2,12,b);}catch(ArgumentException){bad=true;}Check(bad,"missing cover");
            bad=false;try{new FloatFightWater(0,-2,-2,2,3,20,new FloatFightBed(new[]{3d,20},new[]{-.6,0d}));}
            catch(ArgumentException){bad=true;}Check(bad,"dry");
        });
        Test("same active time and one target produce identical sampled trajectories at two render rates",delegate{
            var a=Start(Owner());var b=Start(Owner());a.SetReeling(Session,a.Snapshot.EncounterId,true);b.SetReeling(Session,b.Snapshot.EncounterId,true);
            for(int i=0;i<300;i++){a.Advance(Session,50);b.Advance(Session,16);b.Advance(Session,17);b.Advance(Session,17);}
            var p=a.Snapshot;var q=b.Snapshot;Check(p.StepIndex==q.StepIndex&&p.Phase==q.Phase&&p.Target.MassMg==q.Target.MassMg,"cadence");
            Near(p.Position.X,q.Position.X);Near(p.Position.Y,q.Position.Y);Near(p.Position.Z,q.Position.Z);Near(p.OutLineM,q.OutLineM);
        });
        return failures==0?0:1;
    }
}
