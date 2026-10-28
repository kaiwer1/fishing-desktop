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
        gameFlightOrigin=new FloatFlightPoint(0,1500,0);
        gameFlightWater=new FloatFlightWater(0,-40000,40000,3000,80000);
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
        var launch=new FloatFlightLaunch(gameFlightOrigin,new FloatFlightPoint(target.Xmm,target.Ymm,target.Zmm),
            gameFlightWater,gameFlightRules,cast.PowerPermille,state.Rig.LineLengthMm);
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
            state.State.Session.Id==flight.SessionId && flight.Phase==FloatFlightPhase.Waiting;
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
            xMm=flight==null?0:FlightMm(flight.Xmm),yMm=flight==null?1500:FlightMm(flight.Ymm),zMm=flight==null?0:FlightMm(flight.Zmm),
            lineUsedMm=flight==null?0:FlightMm(flight.LineUsedMm),origin=new {xMm=0,yMm=1500,zMm=0},waterYmm=0,trail=gameFlightTrail.ToArray()}});
    }
    void BroadcastGameFlight(){foreach(var pane in windows.Values)SendGameFlight(pane);}
}
