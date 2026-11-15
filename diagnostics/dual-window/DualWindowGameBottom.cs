using System;
using System.IO;
using System.Collections.Generic;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    bool gameBottomSelected;
    BottomFightHandoff gameBottom;
    Guid gameBottomAttempt;
    double gameBottomRemainder;
    bool GameBottomSpot {get{return gameWater!=null&&gameWater.WaterId=="development-still-water"&&gameWater.SpotId=="bank-01";}}
    BottomSurveyPatch GameBottomPatch()
    {
        if(!GameBottomSpot)return null;
        return new BottomSurveyPatch(gameWater,gameWater.SafeWorkMinimumXmm,gameWater.SafeWorkMaximumXmm,
            gameWater.SafeWorkMinimumZmm,gameWater.SafeWorkMaximumZmm,BottomSubstrate.FirmGravel,true);
    }
    long GameCastReservation(FloatCastLaunchPlan plan)
    {
        if(!gameBottomSelected&&!GameBankMode)return plan.RequiredLineMm;
        var patch=GameBottomPatch();BottomContact c;
        if(patch==null||patch.Resolve(gameWater.Version,plan.Target.Xmm,plan.Target.Zmm,50000,out c)!=null)return -1;
        return Math.Max(plan.RequiredLineMm,c.RequiredLineMm);
    }
    bool GameBottomLandingAllowed(FloatCastLaunchPlan plan,long reserved)
    {
        if(!gameBottomSelected&&!GameBankMode)return true;
        var patch=GameBottomPatch();BottomContact c;
        return patch!=null&&patch.Resolve(gameWater.Version,FlightMm(plan.PredictedXmm),FlightMm(plan.PredictedZmm),50000,out c)==null&&c.RequiredLineMm<=reserved;
    }
    void SendBottomShop(ProbeWindow pane)
    {
        GameBinding b;if(!GameAtlasMode||!pane.Ready||!gameViews.TryGetValue(pane,out b)||b.Document==null)return;
        var s=game.Snapshot.State;
        Send(pane,new {kind="game-bottom-shop-state",documentId=pane.Ticket.DocumentId,revision=checked(++b.BottomShopRevision),
            state=new {available=s!=null&&BottomOwnedKit.Enabled(s),owned=s!=null&&s.OwnedBottomKit!=null,method=gameBottomSelected?"bottom":"float",allowed=GameBottomSpot,priceMinor=BottomOwnedKit.PriceMinor}});
    }
    void ReceiveBottomShop(ProbeWindow p,GameBinding b,Dictionary<string,object> m,string t)
    {
        string a;long seq;if(m.Count!=6||!StringField(m,"action",out a)||(a!="buy"&&a!="select-bottom"&&a!="select-float")||!GameEquipmentExpectedSequence(m,out seq)){GameReply(p,t,"invalid-intent");return;}
        AdvanceActivity();var v=game.Snapshot;var s=v.State;
        if(GameBankMode){GameReply(p,t,"bank-leave-first");return;}
        if(!game.CanControl(b.View,b.Document,b.Lease)||!GameAtlasMode||s==null||!BottomOwnedKit.Enabled(s)){GameReply(p,t,"bottom-unavailable");return;}
        if(!v.MenuOpen||v.PendingResult||s.Session!=null||GameCampActive||b.Shop!=null||b.Equipment!=null||b.Spares!=null){GameReply(p,t,"bottom-shop-busy");return;}
        if(seq!=s.Sequence){GameReply(p,t,"bottom-shop-stale");Broadcast();return;}
        if(a=="buy"){
            if(s.OwnedBottomKit!=null){GameReply(p,t,"bottom-kit-owned");return;}
            var kit=new BottomOwnedKit(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
            try{
                var result=game.ExecuteUser(b.View,b.Document,b.Lease,new BuyBottomKit(s.ProfileId,s.BranchId,Guid.NewGuid(),Guid.NewGuid(),kit,seq,s.Rig.Revision,s.RulesVersion));
                GameReply(p,t,result.OperationSucceeded?"bottom-kit-purchased":result.Code,result);
            }catch(FloatSaveUncertainException){ExitCode=4;GameReply(p,t,"save-outcome-unknown");}
            catch(IOException){GameReply(p,t,"bottom-not-written");}
        }else{
            if(a=="select-bottom"&&!GameBottomSpot){GameReply(p,t,"bottom-spot-unavailable");return;}
            BottomEquipmentBridge binding;
            if(a=="select-bottom"&&BottomOwnedEquipment.Bind(s,out binding)!=null){GameReply(p,t,"bottom-kit-not-owned");return;}
            if(gameBottomSelected!=(a=="select-bottom")||gameLureSelected){
                ClearGameWalk();CancelGameCast();gameCastInput=null;gameWaterPlan=null;gameWaterTarget=null;gameWaterLanding=null;
                gameBottom=null;gameBottomAttempt=Guid.Empty;gameBite=null;gameFight=null;gameFlight=null;
                ResetGameLure();gameLureSelected=false;gameBottomSelected=a=="select-bottom";InitializeGameCast();InitializeGameFlight();
            }
            GameReply(p,t,"bottom-method-selected");
        }
        Broadcast();
    }
    void AdvanceGameBottom(double seconds)
    {
        var s=game.Snapshot.State;var active=s==null?null:s.Session;
        if(active==null||!active.IsBottom){gameBottom=null;gameBottomAttempt=Guid.Empty;gameBottomRemainder=0;return;}
        if(!GameReadyForFish())return;
        if(gameBottomAttempt!=active.Id){
            gameBottomAttempt=active.Id;gameBottom=null;gameBottomRemainder=0;
            BottomEquipmentBridge kit;var patch=GameBottomPatch();
            if(patch!=null&&BottomOwnedEquipment.Bind(s,out kit)==null&&Object.ReferenceEquals(active.BottomKit,s.OwnedBottomKit)){
                var pool=BuildGameFishingPool(gameWaterLanding.Sample);gameFishingPool=pool;
                var config=BottomFightConfiguration(s);
                if(pool!=null)BottomFightHandoff.AtWaterContact(active.Id,kit.Assembly,patch,gameWater.Version,gameWaterLanding.Sample.Xmm,gameWaterLanding.Sample.Zmm,
                    GameSelfTest?1u:BitConverter.ToUInt32(active.Id.ToByteArray(),0),pool.Environment,config,out gameBottom);
            }
            BroadcastBottom();return;
        }
        if(gameBottom==null||seconds<=0||gameBottom.Snapshot.Phase==BottomPhase.Hooked||gameBottom.Snapshot.Phase==BottomPhase.RetrieveRequired)return;
        double ms=seconds*1000+gameBottomRemainder;int whole=(int)Math.Floor(ms);gameBottomRemainder=ms-whole;
        if(whole==0)return;
        while(whole>0){int n=Math.Min(whole,250);gameBottom.Advance(active.Id,n);whole-=n;}
        BroadcastBottom();
    }
    static BottomFightConfig BottomFightConfiguration(FloatGameState state)
    {
        var rules=new Dictionary<string,FloatFightFishRules>();
        foreach(string key in new[]{"carp","crucian"})rules.Add(key,new FloatFightFishRules(key=="carp"?3:2,2,20,.05,.005,10,FloatFightBehavior.Seeded));
        return new BottomFightConfig(new FloatFightRigRules(80,1,40,200,1,20,.2,1.6,40,3,3,5,5),
            new FloatLandingRules(state.OwnedNet!=null&&state.EquippedNetId==state.OwnedNet.Id,5,1,1,.5),rules,new FloatUnhookRules(.25,60,2));
    }
    static string BottomPhaseName(BottomRigSnapshot b)
    {
        switch(b.Phase){case BottomPhase.Sinking:return "sinking";case BottomPhase.Stabilizing:return "settling";
        case BottomPhase.Searching:case BottomPhase.Approaching:return "settled";case BottomPhase.Nibbling:return "touch";
        case BottomPhase.Strikeable:return "bite";case BottomPhase.Hooked:return "hooked";
        case BottomPhase.RetrieveRequired:return b.Reason=="bite-missed"?"missed":"retrieve-required";
        default:return "retrieved";}
    }
    void SendGameBottom(ProbeWindow p)
    {
        GameBinding b;if(!GameAtlasMode||!p.Ready||!gameViews.TryGetValue(p,out b)||b.Document==null)return;
        var s=game.Snapshot.State;var session=GameBankMode?GameHeldBankSession:(s==null?null:s.Session);
        bool current=session!=null&&session.IsBottom&&session.Id==gameBottomAttempt;
        var state=current&&gameBottom!=null?gameBottom.Snapshot:null;
        object end=state==null?null:new {xMm=state.Contact.Xmm,yMm=state.SinkerYmm,zMm=state.Contact.Zmm};
        Send(p,new {kind="game-bottom-state",documentId=p.Ticket.DocumentId,revision=checked(++b.BottomRevision),lineEnd=end,
            state=new {sessionId=current?session.Id.ToString("D"):null,phase=state==null?(current?"retrieve-required":"idle"):BottomPhaseName(state),
                tipPermille=state==null?0:(int)Math.Round(state.TipOffsetMm*1000.0/46)}});
    }
    void BroadcastBottom(){foreach(var pane in windows.Values)SendGameBottom(pane);}
    void ReceiveBottomStrike(ProbeWindow p,GameBinding b,Dictionary<string,object> m,string t)
    {
        if(!BankWireHand(p,t,m))return;
        long seq;object raw;Guid id;if(m.Count!=6+(GameBankMode?1:0)||!GameInputSequence(m,out seq)||!m.TryGetValue("sessionId",out raw)||!GameGuid(raw,false,out id)){GameReply(p,t,"invalid-intent");return;}
        if(seq<=b.BottomStrikeHighWater){GameReply(p,t,"stale-input");return;}b.BottomStrikeHighWater=seq;
        AdvanceActivity();var s=game.Snapshot.State;
        var active=GameBankMode?GameHeldBankSession:(s==null?null:s.Session);
        if(!game.CanControl(b.View,b.Document,b.Lease)||!GameReadyForFish()||active==null||!active.IsBottom||active.Id!=id||gameBottom==null||gameBottom.Snapshot.SessionId!=id){GameReply(p,t,"bottom-unavailable");return;}
        var snap=gameBottom.Snapshot;
        if(snap.Phase!=BottomPhase.Strikeable){GameReply(p,t,"bottom-strike-missed");return;}
        FloatFightSimulation fight;bool hooked;
        if(GameBankMode){
            var why=gameBankScheduler.StrikeHeld(snap.RigId,id,snap.RigRevision,snap.EncounterId,gameBankStand.ControlEpoch,
                gameBankStand.HandGeneration,checked(++bankRodControlSequence),out fight);
            hooked=why==null&&fight!=null;
        }else hooked=gameBottom.StrikeAndStart(id,snap.RigId,snap.RigRevision,snap.EncounterId,true,out fight).ShouldStartFight;
        if(hooked&&fight!=null){
            gameFight=fight;gameLineBreakRules=null;gameFightMillisecondRemainder=0;gameReelInput=null;
            gameLandingCommand=null;gameLandingTicket=null;gameLandingRetry=false;
            gameFailureCommand=null;gameFailureTicket=null;gameFailureRetry=false;
            gameHookedEmissions=checked(gameHookedEmissions+1);BroadcastGameFight();
        }
        GameReply(p,t,hooked?"bottom-hooked":"bottom-strike-missed");BroadcastBottom();
        if(GameBankMode)BroadcastBankStand();
    }
}
