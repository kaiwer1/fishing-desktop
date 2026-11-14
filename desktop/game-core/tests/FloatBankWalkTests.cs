using System;
using FishingDesktop.GameCore;

static class FloatBankWalkTests
{
    static int passed;
    static void Need(bool condition,string check){if(!condition)throw new Exception(check);passed++;Console.WriteLine("PASS "+check);}
    static void Reject(Action action,string check){
        try{action();}catch(ArgumentException){Need(true,check);return;}
        throw new Exception(check);
    }
    public static int Main()
    {
        var g=FloatStillWaterGeometry.CreateDevelopment();var w=new FloatBankWalk(g);
        Need(w.Xmm==0 && w.PoseRevision==0 && w.MinimumXmm==-8000 && w.MaximumXmm==8000 && w.NearestAimU==5000,"station starts at the original anchor");
        Reject(()=>w.SetMotion(-2,false),"invalid direction rejected");
        Reject(()=>w.SetMotion(0,true),"impossible stopped sprint rejected");
        Reject(()=>w.Advance(251),"one step cannot catch up a stall");
        Need(!w.Advance(200) && w.Geometry==g && w.PoseRevision==0,"idle time does not move or replace the geometry");
        w.SetMotion(1,false);w.Advance(250);var right=w.Geometry;
        Need(w.Xmm==400 && right!=g && w.PoseRevision==1 && g.AnchorXmm==0,"quarter-second walk moves 400 mm and preserves the frozen base");
        var split=new FloatBankWalk(g);split.SetMotion(1,false);for(int i=0;i<250;i++)split.Advance(1);
        Need(split.Xmm==w.Xmm,"millisecond ticks preserve fractional travel");
        w.SetMotion(-1,true);w.Advance(250);
        Need(w.Xmm==-450 && right.AnchorXmm==400 && w.PoseRevision==2,"sprint reverses 850 mm while a past plan stays immutable");
        w.SetMotion(0,false);Need(!w.Advance(250)&&w.Xmm==-450,"release stops movement through later active ticks");
        var mark=g.ResolveAim(2000,8000,1).Sample;var same=w.Geometry.ResolveAim(2000,8000,1).Sample;
        Need(mark.Xmm==same.Xmm && mark.Zmm==same.Zmm && mark.DepthMm==same.DepthMm && g.Version==w.Geometry.Version &&
            g.SpotId==w.Geometry.SpotId && g.BedKnots[0]==w.Geometry.BedKnots[0],"world aim, bed and station identity stay fixed");
        foreach(int d in new[]{-1,1}){
            w.SetMotion(d,true);for(int i=0;i<32;i++)w.Advance(250);
            int rev=w.PoseRevision;Need(w.Xmm==8000*d && !w.Advance(250) && w.PoseRevision==rev,"movement clamps at bank end "+d);
        }
        var plan=FloatCastLaunchPlanner.Plan(w.Geometry,5000,5000,1,1000);
        Need(plan.Accepted && plan.Plan.AnchorXmm==8000 && Math.Abs(plan.Plan.PredictedXmm)<.001 &&
            Math.Abs(plan.Plan.PredictedZmm-plan.Plan.Target.Zmm)<.001,"moving launches from the endpoint to the same actual water target");
        var nearest=w.Geometry.ResolveAim(w.NearestAimU,10000,1).Sample;
        Need(Math.Abs(nearest.Xmm-w.Xmm)<=2 && nearest.Zmm==g.AimMinimumZmm,"near-edge reserve follows the player's nearest normalized target");
        var next=new FloatBankWalk(g,w.PoseRevision+1);
        Need(next.Direction==0 && next.Xmm==0 && next.PoseRevision>w.PoseRevision,"station change can invalidate earlier pose tickets");
        Console.WriteLine("RESULT passed="+passed);return 0;
    }
}
