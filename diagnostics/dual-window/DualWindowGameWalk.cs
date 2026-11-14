using System;
using System.Collections.Generic;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    FloatBankWalk gameWalk;
    ProbeWindow gameWalkPane;
    FloatControlLease gameWalkLease;
    double gameWalkMilliseconds;

    bool GameWalkMoving {get{return gameWalk!=null && gameWalk.Direction!=0;}}

    void InitializeGameWalk()
    {
        int revision=gameWalk==null || gameWalk.PoseRevision==0?0:checked(gameWalk.PoseRevision+1);
        gameWalk=gameFishingWater==null?null:new FloatBankWalk(gameFishingWater.Geometry,revision);
        gameWalkPane=null;gameWalkLease=null;gameWalkMilliseconds=0;
    }
    bool ValidGameWalkAim(Dictionary<string,object> aim,out string code)
    {
        code="invalid-intent";
        object revision;
        bool supplied=aim.TryGetValue("poseRevision",out revision);
        if(aim.Count!=(supplied?4:3) || (supplied&&(gameWalk==null||!(revision is int)||(int)revision<0)))return false;
        int current=gameWalk==null?0:gameWalk.PoseRevision;
        if((supplied?(int)revision:0)!=current){code="walk-pose-stale";return false;}
        return true;
    }
    bool GameWalkFree()
    {
        var state=game.Snapshot;
        if(!state.CanSimulate || state.State==null || state.State.Session!=null || GameHeldBankSession!=null || state.PendingResult ||
            GameFailureUnconfirmed || !owner.ActivityState().CanRun)return false;
        var phase=gameCast.Snapshot.Phase;
        return phase!=FloatCastPhase.Charging && phase!=FloatCastPhase.Prepared && phase!=FloatCastPhase.Unknown;
    }
    void ClearGameWalk(ProbeWindow pane=null)
    {
        if(!GameWalkMoving || (pane!=null && pane!=gameWalkPane))return;
        gameWalk.SetMotion(0,false);gameWalkPane=null;gameWalkLease=null;gameWalkMilliseconds=0;
        BroadcastGameWalk();if(GameBankMode)BroadcastBankStand();
    }
    void AdvanceGameWalk(double activeSeconds)
    {
        if(!GameWalkMoving)return;
        GameBinding current;
        if(gameWalkPane==null || !Live(gameWalkPane) || !gameWalkPane.Ready || gameWalkPane.AwaitingNavigationStart ||
            !gameViews.TryGetValue(gameWalkPane,out current) || !Object.ReferenceEquals(current.Lease,gameWalkLease) ||
            !GameAuthorized(gameWalkPane,gameWalkPane.Ticket.DocumentId,current.WireLease,out current) ||
            !game.CanControl(current.View,current.Document,current.Lease) || !GameWalkFree()){
            ClearGameWalk();return;
        }
        var time=activeSeconds*1000+gameWalkMilliseconds;
        int elapsed=(int)Math.Floor(time);gameWalkMilliseconds=time-elapsed;
        if(!gameWalk.Advance(Math.Min(250,elapsed)))return;
        gameWater=gameWalk.Geometry;
        gameFlightOrigin=new FloatFlightPoint(gameWater.AnchorXmm,gameWater.AnchorYmm,gameWater.AnchorZmm);
        // The previous cast is already settled. Do not pair a stale projected
        // target or landing with the newly moved anchor.
        gameWaterPlan=null;gameWaterTarget=null;gameWaterLanding=null;gameWaterLandingSession=Guid.Empty;
        ConfigureGameWaterBudget();BroadcastGameWater();BroadcastGameWalk();if(GameBankMode)BroadcastBankStand();
    }
    void ReceiveGameWalk(ProbeWindow pane,GameBinding binding,Dictionary<string,object> message,string transport)
    {
        object version,direction,fast;long input;
        if(message.Count!=8 || !message.TryGetValue("geometryVersion",out version) || !(version is int) || (int)version<1 ||
            !message.TryGetValue("direction",out direction) || !(direction is int) || (int)direction< -1 || (int)direction>1 ||
            !message.TryGetValue("fast",out fast) || !(fast is bool) || ((int)direction==0&&(bool)fast) || !GameInputSequence(message,out input)){
            GameReply(pane,transport,"invalid-intent");return;
        }
        AdvanceActivity();
        GameBinding current;
        if(!GameAuthorized(pane,pane.Ticket.DocumentId,binding.WireLease,out current) || current!=binding ||
            !game.CanControl(binding.View,binding.Document,binding.Lease)){
            GameReply(pane,transport,"control-required");return;
        }
        if(input<=binding.WalkHighWater){GameReply(pane,transport,"stale-input");return;}
        binding.WalkHighWater=input;
        if(gameWalk==null){GameReply(pane,transport,"walk-unavailable");return;}
        if((int)version!=gameWalk.BaseGeometry.Version){GameReply(pane,transport,"geometry-version-mismatch");return;}
        int next=(int)direction;
        if(next!=0 && !GameWalkFree()){GameReply(pane,transport,"walk-busy");return;}
        gameWalk.SetMotion(next,(bool)fast);
        gameWalkPane=next==0?null:pane;gameWalkLease=next==0?null:binding.Lease;
        if(next==0)gameWalkMilliseconds=0;
        GameReply(pane,transport,next==0?"walk-stopped":"walk-updated");
        BroadcastGameWalk();if(GameBankMode)BroadcastBankStand();
    }
    void SendGameWalk(ProbeWindow pane)
    {
        GameBinding binding;
        if(game==null || !pane.Ready || !gameViews.TryGetValue(pane,out binding) || binding.Document==null)return;
        object state=new {enabled=false};
        if(gameWalk!=null)state=new {enabled=true,waterId=gameWalk.BaseGeometry.WaterId,geometryVersion=gameWalk.BaseGeometry.Version,
            xMm=gameWalk.Xmm,minXmm=gameWalk.MinimumXmm,maxXmm=gameWalk.MaximumXmm,direction=gameWalk.Direction,fast=gameWalk.Fast,
            poseRevision=gameWalk.PoseRevision};
        Send(pane,new {kind="game-bank-state",documentId=pane.Ticket.DocumentId,revision=checked(++binding.WalkRevision),state});
    }
    void BroadcastGameWalk(){foreach(var pane in windows.Values)SendGameWalk(pane);}
}
