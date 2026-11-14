using System;
using System.Collections.Generic;
using System.Globalization;
using FishingDesktop.GameCore;

static class FloatWaterAimTests
{
    static int passed,failed,nextId;
    static Guid G(){return new Guid(++nextId,0,0,new byte[]{0,0,0,0,0,0,0,43});}
    static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Near(double actual,double expected,double tolerance=1e-6){Need(Math.Abs(actual-expected)<=tolerance,"Expected "+expected+"; got "+actual);}
    static void Reject<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    static void Test(string name,Action body){try{body();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
    static FloatStillWaterGeometry Water(){return FloatStillWaterGeometry.CreateDevelopment();}
    static FloatStillWaterGeometry Custom(IEnumerable<FloatWaterBedKnot> bed=null,long version=1,int anchorY=1500,int aimMinimumZ=12000,int aimMaximumZ=40000)
    {return new FloatStillWaterGeometry("test-water","test-bank",version,0,anchorY,0,0,-40000,40000,3000,80000,-12000,12000,aimMinimumZ,aimMaximumZ,
        bed??new[]{new FloatWaterBedKnot(3000,600),new FloatWaterBedKnot(12000,1200),new FloatWaterBedKnot(30000,2400),new FloatWaterBedKnot(80000,4000)});}
    static void Refused(FloatWaterSampleResult result)
    {Need(!result.Accepted&&result.Sample==null&&!String.IsNullOrEmpty(result.Code),"Invalid geometry request returned a usable sample.");}
    static void Refused(FloatCastLaunchPlanResult result)
    {Need(!result.Accepted&&result.Plan==null&&!String.IsNullOrEmpty(result.Code),"Invalid cast request returned a usable launch.");}
    static FloatWaterSample Sample(FloatStillWaterGeometry water,int x,int z)
    {var r=water.Sample(x,z,water.Version);Need(r.Accepted&&r.Sample!=null,"Sample refused: "+r.Code);return r.Sample;}
    static FloatCastLaunchPlan Plan(FloatStillWaterGeometry water,int u=5000,int v=5000,int power=1000)
    {var r=FloatCastLaunchPlanner.Plan(water,u,v,water.Version,power);Need(r.Accepted&&r.Plan!=null,"Plan refused: "+r.Code);return r.Plan;}
    static FloatFlightLaunch Launch(FloatCastLaunchPlan plan,int power=1000,double? line=null)
    {
        var g=plan.Geometry;return new FloatFlightLaunch(new FloatFlightPoint(g.AnchorXmm,g.AnchorYmm,g.AnchorZmm),
            new FloatFlightPoint(plan.Target.Xmm,plan.Target.SurfaceYmm,plan.Target.Zmm),
            new FloatFlightWater(g.SurfaceYmm,g.MinimumXmm,g.MaximumXmm,g.MinimumZmm,g.MaximumZmm),
            new FloatFlightRules(plan.MinimumSpeedMmPerSecond,plan.FullSpeedMmPerSecond,plan.LaunchAngleDegrees,plan.GravityMmPerSecondSquared,plan.MaximumFlightMilliseconds),power,line??plan.RequiredLineMm);
    }
    static FloatFlightSnapshot Finish(FloatFlightLaunch launch,int delta=250)
    {
        var f=new FloatFlightSimulation(G(),launch);int events=0;
        for(int i=0;i<6000&&f.Snapshot.Phase==FloatFlightPhase.Flying;i++){var r=f.Advance(f.Snapshot.SessionId,delta);Need(r.Accepted,"Flight refused an active step.");if(r.TerminalEvent)events++;}
        Need(events==1&&f.Snapshot.Phase!=FloatFlightPhase.Flying,"Flight did not finish once.");return f.Snapshot;
    }
    static double Distance(double x,double y,double z){return Math.Sqrt(x*x+y*y+z*z);}
    static void SamePlan(FloatCastLaunchPlan a,FloatCastLaunchPlan b)
    {
        Need(a.GeometryVersion==b.GeometryVersion&&a.RequiredLineMm==b.RequiredLineMm&&a.Target.Xmm==b.Target.Xmm&&a.Target.Zmm==b.Target.Zmm,"Plan identity or integer values changed.");
        foreach(var pair in new[]{new[]{a.PredictedXmm,b.PredictedXmm},new[]{a.PredictedYmm,b.PredictedYmm},new[]{a.PredictedZmm,b.PredictedZmm},
            new[]{a.FullSpeedMmPerSecond,b.FullSpeedMmPerSecond},new[]{a.LaunchSpeedMmPerSecond,b.LaunchSpeedMmPerSecond},new[]{a.FlightSeconds,b.FlightSeconds}})Near(pair[0],pair[1],0);
    }
    static int Main()
    {
        Test("development water declares one millimetre based bank and a bounded shared water rectangle",()=>{
            var g=Water();Need(g.WaterId=="development-still-water"&&g.SpotId=="bank-01"&&g.Version==1,"Unexpected geometry identity.");
            Need(g.AnchorXmm==0&&g.AnchorYmm==1500&&g.AnchorZmm==0&&g.SurfaceYmm==0,"Anchor or unit convention changed.");
            Need(g.MinimumXmm==-40000&&g.MaximumXmm==40000&&g.MinimumZmm==3000&&g.MaximumZmm==80000,"Water extent changed.");
            Need(g.SafeWorkMinimumXmm>=g.MinimumXmm&&g.SafeWorkMaximumXmm<=g.MaximumXmm&&g.SafeWorkMinimumZmm>=g.MinimumZmm&&g.SafeWorkMaximumZmm<=g.MaximumZmm,"Safe rectangle left the same water.");
        });
        Test("normalised aim maps left right and top far bottom near without changing the bank",()=>{
            var g=Water();var left=g.ResolveAim(0,5000,1);var right=g.ResolveAim(10000,5000,1);var far=g.ResolveAim(5000,0,1);var near=g.ResolveAim(5000,10000,1);
            Need(left.Accepted&&right.Accepted&&far.Accepted&&near.Accepted,"Aim edge refused.");
            Need(left.Sample.Xmm==-12000&&right.Sample.Xmm==12000&&left.Sample.Zmm==26000&&right.Sample.Zmm==26000,"Horizontal aim mapping changed.");
            Need(far.Sample.Xmm==0&&far.Sample.Zmm==40000&&near.Sample.Zmm==12000,"Near/far mapping inverted.");
            Need(g.AnchorYmm==1500&&g.AnchorZmm==0,"Aim moved the bank.");
        });
        Test("invalid normalised coordinates and stale geometry versions return no sample",()=>{
            var g=Water();foreach(int n in new[]{Int32.MinValue,-1,10001,Int32.MaxValue}){Refused(g.ResolveAim(n,5000,1));Refused(g.ResolveAim(5000,n,1));}
            foreach(long version in new long[]{0,-1,2,Int64.MaxValue}){Refused(g.ResolveAim(5000,5000,version));Refused(g.Sample(0,15000,version));}
        });
        Test("water boundary is inclusive and every point beyond the declared bank or sides is refused",()=>{
            var g=Water();foreach(int x in new[]{-40000,40000})foreach(int z in new[]{3000,80000})Need(g.Sample(x,z,1).Accepted,"Water edge excluded.");
            foreach(int x in new[]{Int32.MinValue,-40001,40001,Int32.MaxValue})Refused(g.Sample(x,15000,1));
            foreach(int z in new[]{Int32.MinValue,2999,80001,Int32.MaxValue})Refused(g.Sample(0,z,1));
        });
        Test("piecewise bed interpolation uses conservative integer depth from one source",()=>{
            var g=Water();foreach(var row in new[]{new[]{3000,600},new[]{7500,900},new[]{12000,1200},new[]{21000,1800},new[]{30000,2400},new[]{55000,3200},new[]{80000,4000}}){
                var s=Sample(g,0,row[0]);Need(s.DepthMm==row[1]&&s.BottomYmm==-row[1]&&s.SurfaceYmm==0&&s.HookYmm==-200&&s.BottomClearanceMm==row[1]-200,"Bed, hook or clearance disagreed at "+row[0]);}
            Need(Sample(g,0,3001).DepthMm==600,"Fractional depth rounded up.");
            Need(Sample(g,-40000,21000).DepthMm==Sample(g,40000,21000).DepthMm,"This Z-only bed invented an X-dependent depth.");
        });
        Test("geometry copies the supplied bed instead of retaining mutable external rows",()=>{
            var bed=new List<FloatWaterBedKnot>{new FloatWaterBedKnot(3000,600),new FloatWaterBedKnot(80000,4000)};var g=Custom(bed);var before=Sample(g,0,15000).DepthMm;
            bed[0]=new FloatWaterBedKnot(3000,3000);bed.Clear();Need(Sample(g,0,15000).DepthMm==before,"Source list mutation changed frozen geometry.");
        });
        Test("safe work rectangle starts at the first integer with hook depth plus bottom clearance",()=>{
            var g=Custom(new[]{new FloatWaterBedKnot(3000,100),new FloatWaterBedKnot(12000,600),new FloatWaterBedKnot(80000,600)});
            Need(g.SafeWorkMinimumZmm==6600&&g.SafeWorkMaximumZmm==80000,"Safe shore boundary did not use the same interpolated depth.");
            Refused(g.Sample(0,6599,1));var edge=Sample(g,0,6600);Need(edge.DepthMm==300&&edge.HookYmm==-200&&edge.BottomClearanceMm==100,"Exact clearance boundary was not usable.");
            foreach(int x in new[]{g.SafeWorkMinimumXmm,0,g.SafeWorkMaximumXmm})foreach(int z in new[]{g.SafeWorkMinimumZmm,12000,g.SafeWorkMaximumZmm})
                Need(Sample(g,x,z).BottomClearanceMm>=100,"Safe rectangle contains an unsafe hook position.");
            var descending=Custom(new[]{new FloatWaterBedKnot(3000,600),new FloatWaterBedKnot(20000,600),new FloatWaterBedKnot(80000,100)});
            Need(descending.SafeWorkMinimumZmm==3000&&descending.SafeWorkMaximumZmm==56000,"Descending depth crossing rounded outside the safe interval.");
            Need(Sample(descending,0,56000).DepthMm==300,"Exact descending clearance edge was refused.");Refused(descending.Sample(0,56001,1));
        });
        Test("empty or disconnected safe work areas and an unsafe aim rectangle cannot be silently flattened",()=>{
            Reject<ArgumentException>(()=>Custom(new[]{new FloatWaterBedKnot(3000,100),new FloatWaterBedKnot(80000,100)}));
            Reject<ArgumentException>(()=>Custom(new[]{new FloatWaterBedKnot(3000,600),new FloatWaterBedKnot(10000,100),new FloatWaterBedKnot(20000,600),new FloatWaterBedKnot(80000,600)}));
            Reject<ArgumentException>(()=>Custom(new[]{new FloatWaterBedKnot(3000,100),new FloatWaterBedKnot(12000,600),new FloatWaterBedKnot(80000,600)},aimMinimumZ:4000));
        });
        Test("invalid geometry versions anchors and bed coverage are rejected before planning",()=>{
            Reject<ArgumentException>(()=>Custom(version:0));Reject<ArgumentException>(()=>Custom(anchorY:0));
            Reject<ArgumentException>(()=>Custom(new FloatWaterBedKnot[0]));
            Reject<ArgumentException>(()=>Custom(new[]{new FloatWaterBedKnot(3001,600),new FloatWaterBedKnot(80000,4000)}));
            Reject<ArgumentException>(()=>Custom(new[]{new FloatWaterBedKnot(3000,600),new FloatWaterBedKnot(79999,4000)}));
            Reject<ArgumentException>(()=>Custom(new[]{new FloatWaterBedKnot(3000,600),new FloatWaterBedKnot(3000,1200),new FloatWaterBedKnot(80000,4000)}));
        });
        Test("full charge lands at independently selected left right near and far targets",()=>{
            var g=Water();foreach(var uv in new[]{new[]{0,5000},new[]{10000,5000},new[]{5000,10000},new[]{5000,0},new[]{0,0},new[]{10000,10000}}){
                var p=Plan(g,uv[0],uv[1]);var actual=Finish(Launch(p));Need(actual.Phase==FloatFlightPhase.Waiting&&actual.Reason=="water-contact","Planned full cast did not reach water.");
                Near(actual.Xmm,p.Target.Xmm,.001);Near(actual.Zmm,p.Target.Zmm,.001);Near(actual.Ymm,0,0);
                Near(actual.Xmm,p.PredictedXmm,.001);Near(actual.Zmm,p.PredictedZmm,.001);Near(actual.DurationMilliseconds,Math.Ceiling(p.FlightSeconds*1000),1);
            }
        });
        Test("lower charge lands closer along the same heading and preserves the full cast budget",()=>{
            var g=Water();double previous=0;long required=0;
            foreach(int power in new[]{0,250,500,750,1000}){var p=Plan(g,7500,4000,power);var actual=Finish(Launch(p,power));
                Need(actual.Phase==FloatFlightPhase.Waiting,"Valid lower charge left the water.");double distance=Distance(actual.Xmm,0,actual.Zmm);
                Need(distance>previous,"Charge did not increase actual range.");previous=distance;Near(actual.Xmm/actual.Zmm,p.Target.Xmm/(double)p.Target.Zmm);
                if(required==0)required=p.RequiredLineMm;Need(required==p.RequiredLineMm,"Lower power weakened preparation line reserve.");
                Near(actual.Xmm,p.PredictedXmm,.001);Near(actual.Zmm,p.PredictedZmm,.001);
            }
        });
        Test("planner rejects invalid power geometry versions and incompatible minimum speed",()=>{
            var g=Water();foreach(int n in new[]{-1,1001,Int32.MaxValue})Refused(FloatCastLaunchPlanner.Plan(g,5000,5000,1,n));
            Refused(FloatCastLaunchPlanner.Plan(g,5000,5000,2,1000));Refused(FloatCastLaunchPlanner.Plan(g,-1,5000,1,1000));
            Refused(FloatCastLaunchPlanner.Plan(g,5000,5000,1,1000,0));Refused(FloatCastLaunchPlanner.Plan(g,5000,10000,1,1000,1000000));
        });
        Test("actual low-power contact is checked against the bed rather than only the distant aim",()=>{
            var g=Custom(new[]{new FloatWaterBedKnot(3000,100),new FloatWaterBedKnot(12000,600),new FloatWaterBedKnot(80000,600)});
            Need(g.ResolveAim(5000,5000,1).Accepted,"Distant target was not safe.");Refused(FloatCastLaunchPlanner.Plan(g,5000,5000,1,0));
            var full=Plan(g);Need(Finish(Launch(full)).Phase==FloatFlightPhase.Waiting,"Safe full cast was refused with the shallow shore.");
        });
        Test("required integer line covers the whole full-power trajectory and submerged hook",()=>{
            var g=Water();foreach(var uv in new[]{new[]{0,0},new[]{5000,0},new[]{5000,10000},new[]{10000,5000}}){
                var p=Plan(g,uv[0],uv[1]);var f=new FloatFlightSimulation(G(),Launch(p));double peak=0;
                while(f.Snapshot.Phase==FloatFlightPhase.Flying){var r=f.Advance(f.Snapshot.SessionId,1);peak=Math.Max(peak,r.Snapshot.LineUsedMm);Need(r.Snapshot.LineUsedMm<p.RequiredLineMm,"Trajectory exhausted its advertised budget.");}
                Need(f.Snapshot.Phase==FloatFlightPhase.Waiting,"Full trajectory failed despite reserved line.");
                double hook=Distance(p.Target.Xmm-g.AnchorXmm,p.Target.HookYmm-g.AnchorYmm,p.Target.Zmm-g.AnchorZmm);
                Need(p.RequiredLineMm>=Math.Ceiling(Math.Max(peak,hook))+2,"Budget omitted submerged hook or safety millimetres.");
                Need(hook>Distance(p.Target.Xmm-g.AnchorXmm,p.Target.SurfaceYmm-g.AnchorYmm,p.Target.Zmm-g.AnchorZmm),"Hook reserve was only a surface span.");
                Near(p.MaximumFlightLineMm,Distance(p.Target.Xmm-g.AnchorXmm,p.Target.SurfaceYmm-g.AnchorYmm,p.Target.Zmm-g.AnchorZmm));
                Near(p.MaximumHookLineMm,hook);Need(p.RequiredLineMm==(long)Math.Ceiling(p.MaximumLineEnvelopeMm)+2,"Canonical integer reserve changed.");
            }
        });
        Test("genuinely insufficient installed line is rejected by the actual continuous flight",()=>{
            var p=Plan(Water(),5000,0);var shortFlight=Finish(Launch(p,line:5000));Need(shortFlight.Phase==FloatFlightPhase.Invalid&&shortFlight.Reason=="line-limit","Short installed line reached the planned target.");
            Near(shortFlight.LineUsedMm,5000,.001);Need(Finish(Launch(p)).Phase==FloatFlightPhase.Waiting,"Advertised reserve did not clear the same trajectory.");
        });
        Test("planner arithmetic is deterministic and millimetres remain consistent with flight seconds",()=>{
            var g=Water();var a=Plan(g,1250,7250,650);var b=Plan(g,1250,7250,650);SamePlan(a,b);
            Need(Object.ReferenceEquals(a.Geometry,g)&&a.GeometryVersion==g.Version&&a.Target.Xmm==-9000&&a.Target.Zmm==19700,"Frozen geometry or integer target changed.");
            Near(a.LaunchSpeedMmPerSecond,a.MinimumSpeedMmPerSecond+(a.FullSpeedMmPerSecond-a.MinimumSpeedMmPerSecond)*.65);
            Near(a.PredictedYmm,g.SurfaceYmm);Need(a.FlightSeconds>0&&a.FlightSeconds<5&&a.RequiredLineMm<50000,"Units expanded beyond the development envelope.");
            var x=Finish(Launch(a,650),1);var y=Finish(Launch(a,650),250);Near(x.Xmm,y.Xmm,0);Near(x.Ymm,y.Ymm,0);Near(x.Zmm,y.Zmm,0);
        });
        Test("all development aim corners and powers produce finite geometry-derived contact",()=>{
            var g=Water();int checkedPlans=0;long maximum=0;
            foreach(int u in new[]{0,2500,5000,7500,10000})foreach(int v in new[]{0,2500,5000,7500,10000})foreach(int power in new[]{0,500,1000}){
                var p=Plan(g,u,v,power);var actual=Finish(Launch(p,power));Need(actual.Phase==FloatFlightPhase.Waiting,"Development grid trajectory was invalid.");
                foreach(double value in new[]{p.PredictedXmm,p.PredictedYmm,p.PredictedZmm,p.FullSpeedMmPerSecond,p.FlightSeconds})Need(!Double.IsNaN(value)&&!Double.IsInfinity(value),"Nonfinite planned value.");
                int x=(int)Math.Round(actual.Xmm,MidpointRounding.AwayFromZero),z=(int)Math.Round(actual.Zmm,MidpointRounding.AwayFromZero);var sampled=Sample(g,x,z);
                Need(sampled.BottomClearanceMm>=100&&p.PredictedSample.Xmm==x&&p.PredictedSample.Zmm==z&&p.PredictedSample.DepthMm==sampled.DepthMm&&
                    p.PredictedSample.GeometryVersion==g.Version,"Contact ignored the shared depth source.");
                checkedPlans++;maximum=Math.Max(maximum,p.RequiredLineMm);
            }
            Console.WriteLine("OBSERVED {\"plannedTrajectories\":"+checkedPlans+",\"maximumRequiredLineMm\":"+maximum.ToString(CultureInfo.InvariantCulture)+",\"fullChargeTargetsMatched\":true}");
        });
        Test("nonfinite values cannot enter the floating flight boundary while geometry requests are integers",()=>{
            foreach(double invalid in new[]{Double.NaN,Double.PositiveInfinity,Double.NegativeInfinity}){
                Reject<ArgumentOutOfRangeException>(()=>new FloatFlightPoint(invalid,1500,0));
                Reject<ArgumentOutOfRangeException>(()=>new FloatFlightPoint(0,invalid,0));
                Reject<ArgumentOutOfRangeException>(()=>new FloatFlightPoint(0,1500,invalid));
            }
            // Geometry and planner expose integer coordinates/power; no NaN
            // injection or reflection is used to manufacture an invalid state.
            Refused(Water().Sample(Int32.MinValue,Int32.MaxValue,1));
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
}
