using System;
using System.Collections.Generic;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    FloatFlightSimulation gameFlight;
    FloatFlightPoint gameFlightOrigin;
    FloatFlightWater gameFlightWater;
    FloatFlightRules gameFlightRules;
    double gameFlightMillisecondRemainder;
    int gameFlightTrailAt;
    readonly List<object> gameFlightTrail=new List<object>();

    void InitializeGameFlight()
    {
        // Explicit original flat-water slice. Origin is also the straight-line
        // anchor. No terrain, wind, drag, slack line or RF4 formula is implied.
        gameFlightOrigin=gameWater==null?new FloatFlightPoint(0,1500,0):new FloatFlightPoint(gameWater.AnchorXmm,gameWater.AnchorYmm,gameWater.AnchorZmm);
        gameFlightWater=gameWater==null?new FloatFlightWater(0,-40000,40000,3000,80000):
            new FloatFlightWater(gameWater.SurfaceYmm,gameWater.MinimumXmm,gameWater.MaximumXmm,gameWater.MinimumZmm,gameWater.MaximumZmm);
        gameFlightRules=new FloatFlightRules(6500,20000,40,9810,5000);
    }
    static int FlightMm(double value){return checked((int)Math.Round(value,MidpointRounding.AwayFromZero));}
    object FlightPoint(FloatFlightSnapshot state)
    {return new {xMm=FlightMm(state.Xmm),yMm=FlightMm(state.Ymm),zMm=FlightMm(state.Zmm)};}
    void StartGameFlight(FloatCastSnapshot cast)
    {
        var state=game.Snapshot.State;
        if(state==null || state.Session==null || state.Session.Id!=cast.SessionId || state.Session.CapacityBlocked)
            throw new InvalidOperationException("Flight requires the newly confirmed active cast.");
        var target=cast.Target;
        if(gameWater!=null){
            var plan=GameWaterReleasePlan(cast);
            if(plan==null)throw new InvalidOperationException("The confirmed cast lost its frozen water geometry and launch plan.");
            gameFlightOrigin=new FloatFlightPoint(plan.AnchorXmm,plan.AnchorYmm,plan.AnchorZmm);
            gameFlightWater=new FloatFlightWater(gameWater.SurfaceYmm,gameWater.MinimumXmm,gameWater.MaximumXmm,gameWater.MinimumZmm,gameWater.MaximumZmm);
            gameFlightRules=new FloatFlightRules(plan.MinimumSpeedMmPerSecond,plan.FullSpeedMmPerSecond,plan.LaunchAngleDegrees,plan.GravityMmPerSecondSquared,plan.MaximumFlightMilliseconds);
        }
        var launch=new FloatFlightLaunch(gameFlightOrigin,new FloatFlightPoint(target.Xmm,target.Ymm,target.Zmm),
            gameFlightWater,gameFlightRules,cast.PowerPermille,GameInstalledLineMm(state));
        gameFlight=new FloatFlightSimulation(cast.SessionId,launch);
        gameFlightMillisecondRemainder=0;gameFlightTrailAt=0;
        gameFlightTrail.Clear();gameFlightTrail.Add(FlightPoint(gameFlight.Snapshot));
    }
    bool ReconcileGameFlight()
    {
        if(gameFlight==null)return false;
        var flight=gameFlight.Snapshot;
        if(flight.Phase==FloatFlightPhase.Ended)return false;
        var state=game.Snapshot;
        if(state.State!=null && state.State.Session!=null && state.State.Session.Id==flight.SessionId && !state.PendingResult)return false;
        gameFlight.End(flight.SessionId);gameFlightMillisecondRemainder=0;return true;
    }
    bool GameReadyForFish()
    {
        if(gameFlight==null)return false;
        var state=game.Snapshot;var flight=gameFlight.Snapshot;
        return state.CanSimulate && state.State!=null && state.State.Session!=null && !state.PendingResult &&
            state.State.Session.Id==flight.SessionId && flight.Phase==FloatFlightPhase.Waiting && GameWaterCanFish();
    }
    void AdvanceGameFlight(double activeSeconds)
    {
        if(gameFlight==null)return;
        if(ReconcileGameFlight()){BroadcastGameFlight();return;}
        var before=gameFlight.Snapshot;
        if(!game.Snapshot.CanSimulate || before.Phase!=FloatFlightPhase.Flying)return;
        double milliseconds=activeSeconds*1000+gameFlightMillisecondRemainder;
        int whole=(int)Math.Floor(milliseconds);gameFlightMillisecondRemainder=milliseconds-whole;
        if(whole<=0)return;
        while(whole>0 && gameFlight.Snapshot.Phase==FloatFlightPhase.Flying){
            int step=Math.Min(250,whole);gameFlight.Advance(before.SessionId,step);whole-=step;
        }
        var after=gameFlight.Snapshot;
        UpdateGameWaterLanding();
        if(after.ElapsedMilliseconds-gameFlightTrailAt>=100 || after.Phase!=before.Phase){
            gameFlightTrail.Add(FlightPoint(after));gameFlightTrailAt=after.ElapsedMilliseconds;
            if(gameFlightTrail.Count>32)gameFlightTrail.RemoveAt(0);
        }
        BroadcastGameFlight();
    }
    void SendGameFlight(ProbeWindow pane)
    {
        GameBinding binding;
        if(game==null || !pane.Ready || !gameViews.TryGetValue(pane,out binding) || binding.Document==null)return;
        var flight=gameFlight==null?null:gameFlight.Snapshot;
        Send(pane,new {kind="game-flight-state",documentId=pane.Ticket.DocumentId,revision=checked(++binding.FlightRevision),state=new {
            sessionId=flight==null?null:flight.SessionId.ToString("D"),phase=flight==null?"idle":flight.Phase.ToString().ToLowerInvariant(),
            reason=flight==null?null:flight.Reason,elapsedMilliseconds=flight==null?0:flight.ElapsedMilliseconds,
            xMm=FlightMm(flight==null?gameFlightOrigin.Xmm:flight.Xmm),yMm=FlightMm(flight==null?gameFlightOrigin.Ymm:flight.Ymm),zMm=FlightMm(flight==null?gameFlightOrigin.Zmm:flight.Zmm),
            lineUsedMm=flight==null?0:FlightMm(flight.LineUsedMm),origin=new {xMm=FlightMm(gameFlightOrigin.Xmm),yMm=FlightMm(gameFlightOrigin.Ymm),zMm=FlightMm(gameFlightOrigin.Zmm)},waterYmm=FlightMm(gameFlightWater.Ymm),trail=gameFlightTrail.ToArray()}});
        if(flight!=null&&(flight.Phase==FloatFlightPhase.Waiting||flight.Phase==FloatFlightPhase.Invalid))SendGameWater(pane);
    }
    void BroadcastGameFlight(){foreach(var pane in windows.Values)SendGameFlight(pane);}
}
