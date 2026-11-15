using System;
using System.IO;
using System.Collections.Generic;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    bool gameLureSelected;
    bool GameLureSpot {get{return gameWater!=null&&gameWater.WaterId=="coastal-bay"&&gameWater.SpotId=="rock-bank";}}
    void SendGameLureShop(ProbeWindow p)
    {
        GameBinding b;if(!GameAtlasMode||!p.Ready||!gameViews.TryGetValue(p,out b)||b.Document==null)return;
        var s=game.Snapshot.State;
        Send(p,new {kind="game-lure-shop-state",documentId=p.Ticket.DocumentId,revision=checked(++b.LureShopRevision),state=new {
            available=s!=null&&OwnedLureKit.Enabled(s),owned=s!=null&&s.OwnedLureKit!=null,selected=gameLureSelected,allowed=GameLureSpot,priceMinor=OwnedLureKit.PriceMinor}});
    }
    void ReceiveLureShop(ProbeWindow p,GameBinding b,Dictionary<string,object> m,string t)
    {
        string action;long seq;
        if(m.Count!=6||!StringField(m,"action",out action)||(action!="buy"&&action!="select"&&action!="leave")||!GameEquipmentExpectedSequence(m,out seq)){
            GameReply(p,t,"invalid-intent");return;
        }
        AdvanceActivity();var snapshot=game.Snapshot;var state=snapshot.State;
        if(!GameAtlasMode||!game.CanControl(b.View,b.Document,b.Lease)||state==null||!OwnedLureKit.Enabled(state)){GameReply(p,t,"lure-unavailable");return;}
        if(!snapshot.MenuOpen||state.Session!=null||state.BankSessions.Count!=0||snapshot.PendingResult||GameBankMode||GameCampActive||b.Shop!=null||b.Spares!=null||b.Equipment!=null){
            GameReply(p,t,"lure-shop-busy");return;
        }
        if(seq!=state.Sequence){GameReply(p,t,"lure-shop-stale");Broadcast();return;}
        if(action=="buy"){
            if(state.OwnedLureKit!=null){GameReply(p,t,"lure-kit-owned");return;}
            var kit=new OwnedLureKit(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
            var cmd=new BuyLureKit(state.ProfileId,state.BranchId,Guid.NewGuid(),Guid.NewGuid(),kit,seq,state.Rig.Revision,state.RulesVersion);
            try{
                var r=game.ExecuteUser(b.View,b.Document,b.Lease,cmd);GameReply(p,t,r.OperationSucceeded?"lure-kit-purchased":r.Code,r);
            }catch(FloatSaveUncertainException){ExitCode=4;GameReply(p,t,"save-outcome-unknown");}
            catch(IOException){GameReply(p,t,"lure-not-written");}
        }else{
            if(action=="select"&&!GameLureSpot){GameReply(p,t,"lure-spot-unavailable");return;}
            if(action=="select"&&state.OwnedLureKit==null){GameReply(p,t,"lure-kit-not-owned");return;}
            if(gameLureSelected!=(action=="select")){
                ClearGameWalk();CancelGameCast();gameCastInput=null;gameWaterPlan=null;gameWaterTarget=null;gameWaterLanding=null;gameWaterLandingSession=Guid.Empty;
                gameBottom=null;gameBottomAttempt=Guid.Empty;gameBottomSelected=false;gameBite=null;gameFight=null;gameFlight=null;gameFishingPool=null;gameFlightTrail.Clear();
                ResetGameLure();gameLureSelected=action=="select";InitializeGameCast();InitializeGameFlight();
            }
            GameReply(p,t,"lure-method-selected");
        }
        Broadcast();
    }
}
