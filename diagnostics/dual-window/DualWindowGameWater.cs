using System;
using System.Collections.Generic;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    FloatStillWaterGeometry gameWater;
    FloatCastLaunchPlan gameWaterPlan;
    FloatCastTarget gameWaterTarget;
    FloatWaterSampleResult gameWaterLanding;
    Guid gameWaterLandingSession;
    long gameWaterMinimumLineMm;
    void InitializeGameWater()
    {
        if(!GameWaterMode)return;
        gameWater=FloatStillWaterGeometry.CreateDevelopment();
        // This symmetrical development sector has its nearest target in the
        // centre of its near edge. Menu readiness is for at least one target.
        var near=FloatCastLaunchPlanner.Plan(gameWater,5000,10000,gameWater.Version,1000);
        if(!near.Accepted)throw new InvalidOperationException("The bank has no supported near cast.");
        gameWaterMinimumLineMm=near.Plan.RequiredLineMm;
    }
    bool GameWaterTargetCurrent(FloatCastTarget target)
    {
        return gameWater!=null&&gameWaterPlan!=null&&Object.ReferenceEquals(target,gameWaterTarget)&&
            Object.ReferenceEquals(gameWaterPlan.Geometry,gameWater)&&target.GeometryVersion==gameWater.Version&&
            target.Xmm==gameWaterPlan.Target.Xmm&&target.Ymm==gameWater.SurfaceYmm&&target.Zmm==gameWaterPlan.Target.Zmm&&
            target.RequiredLineMm==gameWaterPlan.RequiredLineMm;
    }
    bool ParseGameWaterAim(Dictionary<string,object> message,out FloatCastLaunchPlan plan,out string code)
    {
        plan=null;code="invalid-intent";object value,u,v,version;
        if(!message.TryGetValue("aim",out value))return false;
        var aim=value as Dictionary<string,object>;
        if(aim==null||aim.Count!=3||!aim.TryGetValue("u",out u)||!aim.TryGetValue("v",out v)||!aim.TryGetValue("geometryVersion",out version)||
            !(u is int)||!(v is int)||!(version is int)||(int)version<=0)return false;
        var result=FloatCastLaunchPlanner.Plan(gameWater,(int)u,(int)v,(int)version,1000);
        code=result.Code;plan=result.Plan;return result.Accepted;
    }
    FloatCastLaunchPlan GameWaterReleasePlan(FloatCastSnapshot cast)
    {
        if(!GameWaterTargetCurrent(cast.Target))return null;
        var result=FloatCastLaunchPlanner.Plan(gameWater,gameWaterPlan.U,gameWaterPlan.V,gameWater.Version,cast.PowerPermille);
        return result.Accepted&&result.Plan.RequiredLineMm==cast.Target.RequiredLineMm?result.Plan:null;
    }
    void UpdateGameWaterLanding()
    {
        if(gameWater==null||gameFlight==null)return;
        var flight=gameFlight.Snapshot;
        if(flight.Phase!=FloatFlightPhase.Waiting&&flight.Phase!=FloatFlightPhase.Invalid)return;
        if(gameWaterLandingSession==flight.SessionId&&gameWaterLanding!=null)return;
        gameWaterLandingSession=flight.SessionId;
        gameWaterLanding=flight.Phase==FloatFlightPhase.Waiting?
            gameWater.Sample(FlightMm(flight.Xmm),FlightMm(flight.Zmm),gameWater.Version):new FloatWaterSampleResult(flight.Reason);
    }
    bool GameWaterCanFish()
    {
        if(gameWater==null)return true;
        UpdateGameWaterLanding();
        return gameWaterPlan!=null&&Object.ReferenceEquals(gameWaterPlan.Geometry,gameWater)&&gameFlight!=null&&gameWaterLandingSession==gameFlight.Snapshot.SessionId&&gameWaterLanding!=null&&gameWaterLanding.Accepted&&
            gameWaterLanding.Sample.GeometryVersion==gameWater.Version;
    }
    void SendGameWater(ProbeWindow pane)
    {
        GameBinding binding;if(game==null||!pane.Ready||!gameViews.TryGetValue(pane,out binding)||binding.Document==null)return;
        object state;
        if(gameWater==null)state=new {enabled=false};
        else{
            var beds=new List<object>();foreach(var row in gameWater.BedKnots)beds.Add(new {zMm=row.Zmm,depthMm=row.DepthMm});
            var cast=gameCast==null?null:gameCast.Snapshot;
            var release=cast==null?null:GameWaterReleasePlan(cast);
            object target=gameWaterPlan==null?null:new {u=gameWaterPlan.U,v=gameWaterPlan.V,xMm=gameWaterPlan.Target.Xmm,zMm=gameWaterPlan.Target.Zmm,
                depthMm=gameWaterPlan.Target.DepthMm,requiredLineMm=gameWaterPlan.RequiredLineMm};
            object prediction=release==null?null:new {xMm=FlightMm(release.PredictedXmm),zMm=FlightMm(release.PredictedZmm),depthMm=release.PredictedSample.DepthMm,powerPermille=release.PowerPermille};
            object landing=gameWaterLanding==null?null:new {sessionId=gameWaterLandingSession.ToString("D"),xMm=FlightMm(gameFlight.Snapshot.Xmm),zMm=FlightMm(gameFlight.Snapshot.Zmm),
                depthMm=gameWaterLanding.Accepted?gameWaterLanding.Sample.DepthMm:0,accepted=gameWaterLanding.Accepted,reason=gameWaterLanding.Code};
            state=new {enabled=true,geometryVersion=gameWater.Version,waterId=gameWater.WaterId,spotId=gameWater.SpotId,
                bounds=new {minXmm=gameWater.AimMinimumXmm,maxXmm=gameWater.AimMaximumXmm,minZmm=gameWater.AimMinimumZmm,maxZmm=gameWater.AimMaximumZmm},
                waterBounds=new {minXmm=gameWater.MinimumXmm,maxXmm=gameWater.MaximumXmm,minZmm=gameWater.MinimumZmm,maxZmm=gameWater.MaximumZmm},
                anchor=new {xMm=gameWater.AnchorXmm,yMm=gameWater.AnchorYmm,zMm=gameWater.AnchorZmm},
                surfaceYmm=gameWater.SurfaceYmm,floatDepthMm=FloatStillWaterGeometry.FloatDepthMm,clearanceMm=FloatStillWaterGeometry.MinimumBottomClearanceMm,
                bed=beds.ToArray(),target,prediction,landing};
        }
        Send(pane,new {kind="game-water-state",documentId=pane.Ticket.DocumentId,revision=checked(++binding.WaterRevision),state});
    }
    void BroadcastGameWater(){foreach(var pane in windows.Values)SendGameWater(pane);}
}
