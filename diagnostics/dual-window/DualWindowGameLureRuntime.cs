using System;
using System.Collections.Generic;
using System.IO;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    LureRetrieveSimulation gameLure;
    FloatCastLaunchPlan gameLurePlan;
    ProbeWindow gameLurePane;
    FloatControlLease gameLureLease;
    Guid gameLureAttempt;
    long gameLureEpoch;
    double gameLureMilliseconds;
    RecallFloat gameLureEnding;
    bool gameLureEndingRetry;

    FloatBiteCandidate GameLureBass(FloatCastLaunchPlan plan)
    {
        if(!GameLureSpot||plan==null||gameFishingWater==null)return null;
        var pool=BuildGameFishingPool(plan.PredictedSample);
        if(pool!=null)foreach(var candidate in pool.Environment.Candidates)
            if(candidate.SpeciesId=="spotted-seabass")return candidate;
        return null;
    }
    int GameLureActivity(FloatFightPoint position,double waterDepthM,long atMilliseconds)
    {
        if(!GameLureSpot||gameLurePlan==null||!Object.ReferenceEquals(gameWater,gameLurePlan.Geometry))return 0;
        var sampled=gameWater.Sample(FightUnits(position.X),FightUnits(position.Z),gameWater.Version);
        if(!sampled.Accepted)return 0;
        var work=sampled.Sample;int y=FightUnits(position.Y);
        // The surveyed integer bed floors interpolated depth; the spoon uses
        // the same continuous slope. Allow their millimetre rounding margin.
        if(y>work.SurfaceYmm||y<work.BottomYmm-2||Math.Abs(waterDepthM*1000-work.DepthMm)>2)return 0;
        var current=gameClimate.Sample(gameFishingWater,atMilliseconds);
        return current.ActivityPermille("spotted-seabass",work.DepthMm>=gameFishingWater.DeepThresholdMm);
    }
    string CheckGameLurePlan(FloatCastLaunchPlan plan)
    {
        if(!GameLureSpot)return "lure-spot-unavailable";
        LureRetrieveRig rig;string why=LureOwnedEquipment.BindForCast(game.Snapshot.State,out rig);
        if(why!=null)return why;
        return LureRetrieveSimulation.CheckCast(rig,plan,GameLureBass(plan));
    }
    void ClearGameLureInput(ProbeWindow pane=null)
    {
        if(gameLure==null||gameLureLease==null||(pane!=null&&gameLurePane!=pane))return;
        gameLure.SetControl(checked(++gameLureEpoch),false);
        gameLureLease=null;gameLurePane=null;gameLureMilliseconds=0;
    }
    void ResetGameLure()
    {
        ClearGameLureInput();gameLure=null;gameLurePlan=null;gameLureAttempt=Guid.Empty;
        gameLureEnding=null;gameLureEndingRetry=false;gameLureMilliseconds=0;
    }
    GameBinding GetGameLureControl()
    {
        if(!game.Snapshot.CanSimulate){ClearGameLureInput();return null;}
        foreach(var pair in gameViews){
            var pane=pair.Key;var b=pair.Value;GameBinding current;
            if(!Live(pane)||!pane.Ready||pane.AwaitingNavigationStart||b.Lease==null||
                !GameAuthorized(pane,pane.Ticket.DocumentId,b.WireLease,out current)||current!=b||
                !game.CanControl(b.View,b.Document,b.Lease))continue;
            if(gameLureLease!=b.Lease){
                ClearGameLureInput();gameLure.SetControl(checked(++gameLureEpoch),true);gameLureLease=b.Lease;gameLurePane=pane;
            }
            return b;
        }
        ClearGameLureInput();return null;
    }
    void AdvanceGameLure(double seconds)
    {
        var s=game.Snapshot.State;var active=s==null?null:s.Session;
        if(active==null||!active.IsLure){if(gameLure!=null||gameLurePlan!=null)ResetGameLure();return;}
        if(gameLureAttempt!=active.Id){
            if(!GameReadyForFish())return;
            gameLureAttempt=active.Id;
            LureRetrieveRig rig;string why=LureOwnedEquipment.Bind(s,active.Id,out rig);
            LureActivitySampler activity=GameEcologyEnabled?(LureActivitySampler)GameLureActivity:null;
            if(why==null)why=LureRetrieveSimulation.AtWaterContact(active.Id,rig,gameLurePlan,gameFlight.Snapshot,GameLureBass(gameLurePlan),
                GameSelfTest?1u:BitConverter.ToUInt32(active.Id.ToByteArray(),0),s.OwnedNet!=null&&s.EquippedNetId==s.OwnedNet.Id,out gameLure,activity);
            if(why!=null)throw new InvalidOperationException("A confirmed lure cast lost its flight or equipment binding: "+why);
            GetGameLureControl();BroadcastGameLure();return;
        }
        if(gameLure==null||gameLure.Snapshot.Phase==LureRetrievePhase.Hooked)return;
        var binding=GetGameLureControl();
        if(binding==null){gameLure.Advance(active.Id,0,true);return;}
        double ms=seconds*1000+gameLureMilliseconds;
        int whole=(int)Math.Floor(ms);gameLureMilliseconds=ms-whole;
        // Advance(0) also restores the current host pause state before a fresh
        // input. No wall-clock delta is inferred while a view has no lease.
        var before=gameLure.Snapshot;
        do{
            int n=Math.Min(250,whole);whole-=n;
            gameLure.Advance(active.Id,n,false,GameEcologyEnabled?(long?)(gameEcologyTimeMs-whole):null);
        }while(whole>0&&gameLure.Snapshot.Phase!=LureRetrievePhase.RetrievePending);
        var after=gameLure.Snapshot;
        if(after.Phase==LureRetrievePhase.RetrievePending&&gameLureEnding==null){
            gameLureEnding=new RecallFloat(s.ProfileId,s.BranchId,Guid.NewGuid(),after.RetrieveEventId,active.Id,s.Sequence,s.Rig.Revision,s.RulesVersion);
            WriteLureRetrieve(binding,null,null);return;
        }
        if(after.Step!=before.Step)BroadcastGameLure();
    }
    void WriteLureRetrieve(GameBinding b,ProbeWindow pane,string transport)
    {
        var run=gameLure;var command=gameLureEnding;FloatCommitResult result=null;string code;
        gameLureEndingRetry=false;
        try{
            result=game.ExecuteUser(b.View,b.Document,b.Lease,command);
            if(result.Committed&&result.Receipt!=null&&result.Receipt.Outcome==FloatOutcome.Recalled){
                code=run.ConfirmRetrieve(command.SessionId,command.EventId);
                if(code!=null&&code!="retrieve-replayed")throw new InvalidOperationException("The saved spoon belongs to a different retrieval.");
                code="lure-retrieved";
            }else{code=result.Code;gameLureEndingRetry=true;}
        }catch(FloatSaveUncertainException){ExitCode=4;code="save-outcome-unknown";}
        catch(IOException){code="lure-retrieval-not-written";gameLureEndingRetry=true;}
        if(pane!=null)GameReply(pane,transport,code,result);
        Broadcast();
    }
    bool ReceiveLureRecall(ProbeWindow p,GameBinding b,Dictionary<string,object> intent,string t)
    {
        object action;
        if(intent==null||!intent.TryGetValue("action",out action)||(action as string)!="recall")return false;
        AdvanceActivity();
        var s=game.Snapshot.State;FloatCommand parsed;
        if(s==null||!FloatPageCommands.TryParse(intent,s,out parsed)){GameReply(p,t,"invalid-intent");return true;}
        if(s.Session==null||!s.Session.IsLure||s.Session.Id!=parsed.SessionId)return false;
        if(gameLure==null||gameLure.Snapshot.Phase==LureRetrievePhase.Hooked)return false;
        if(gameLureEnding!=null&&gameLureEndingRetry&&parsed.ExpectedSequence==s.Sequence&&b.Lease==gameLureLease){
            WriteLureRetrieve(b,p,t);return true;
        }
        GameReply(p,t,"lure-retrieve-required");return true;
    }

    static string LurePhaseName(LureRetrievePhase phase)
    {
        switch(phase){
            case LureRetrievePhase.InWater:return "in-water";
            case LureRetrievePhase.BottomRecovery:return "bottom-recovery";
            case LureRetrievePhase.RetrievePending:return "retrieve-pending";
            default:return phase.ToString().ToLowerInvariant();
        }
    }
    void SendGameLure(ProbeWindow p)
    {
        GameBinding b;if(!GameAtlasMode||!p.Ready||!gameViews.TryGetValue(p,out b)||b.Document==null)return;
        object state;var s=game.Snapshot.State;
        var l=gameLure==null?null:gameLure.Snapshot;
        if(!gameLureSelected||l==null||s==null||s.Session==null||!s.Session.IsLure||s.Session.Id!=l.SessionId)
            state=new {phase="idle",sessionId=(string)null};
        else state=new {sessionId=l.SessionId.ToString("D"),phase=LurePhaseName(l.Phase),encounterId=l.EncounterId==Guid.Empty?null:l.EncounterId.ToString("D"),
            layer=l.Layer.ToString().ToLowerInvariant(),position=new {xMm=FightUnits(l.Position.X),yMm=FightUnits(l.Position.Y),zMm=FightUnits(l.Position.Z)},
            depthMm=FightUnits(l.DepthM),bedClearanceMm=FightUnits(l.BedClearanceM),outLineMm=FightUnits(l.OutLineM),
            remainingLineMm=l.Rig.TotalLineMm-FightUnits(l.OutLineM),speedLevel=l.SpeedLevel,reeling=l.Reeling,bailOpen=l.BailOpen};
        bool canRetryRetrieve=gameLureSelected&&l!=null&&l.Phase==LureRetrievePhase.RetrievePending&&gameLureEndingRetry&&
            !game.Snapshot.Faulted&&s!=null&&s.Session!=null&&s.Session.Id==l.SessionId;
        Send(p,new {kind="game-lure-state",documentId=p.Ticket.DocumentId,revision=checked(++b.LureRevision),canRetryRetrieve,state});
    }
    void BroadcastGameLure(){foreach(var p in windows.Values)SendGameLure(p);}
    void ReceiveGameLure(ProbeWindow p,GameBinding b,Dictionary<string,object> m,string t)
    {
        string action;long seq;Guid session,encounter=Guid.Empty;object value;int level=0;
        if(!StringField(m,"action",out action)||!GameInputSequence(m,out seq)||!m.TryGetValue("sessionId",out value)||!GameGuid(value,false,out session)||
            m.Count!=(action=="speed"||action=="strike"?8:7)){GameReply(p,t,"invalid-intent");return;}
        if(action=="speed"){
            if(!m.TryGetValue("value",out value)||!(value is int)||(level=(int)value)<0||level>4){GameReply(p,t,"invalid-intent");return;}
        }else if(action=="strike"){
            if(!m.TryGetValue("encounterId",out value)||!GameGuid(value,false,out encounter)){GameReply(p,t,"invalid-intent");return;}
        }else if(action!="reel-start"&&action!="reel-stop"){GameReply(p,t,"invalid-intent");return;}
        if(seq<=b.LureHighWater){GameReply(p,t,"stale-input");return;}b.LureHighWater=seq;
        AdvanceActivity();
        var state=game.Snapshot.State;
        if(!gameLureSelected||GameBankMode||state==null||state.Session==null||!state.Session.IsLure||state.Session.Id!=session||
            gameLure==null||gameLure.Snapshot.SessionId!=session||GetGameLureControl()!=b||!game.Snapshot.CanSimulate){GameReply(p,t,"lure-not-current");return;}
        var rig=gameLure.Snapshot.Rig;string code;
        if(action=="strike"){
            FloatFightSimulation fight;code=gameLure.StrikeAndStart(rig.RigId,session,gameLureEpoch,seq,encounter,out fight);
            if(code==null){
                gameFight=fight;gameFightMillisecondRemainder=0;gameReelInput=null;gameLineBreakRules=null;
                gameLandingCommand=null;gameLandingTicket=null;gameLandingRetry=false;
                gameFailureCommand=null;gameFailureTicket=null;gameFailureRetry=false;
                gameHookedEmissions=checked(gameHookedEmissions+1);code="lure-hooked";BroadcastGameFight();
            }
        }else if(action=="speed")code=gameLure.SetSpeed(rig.RigId,session,gameLureEpoch,seq,level)??"lure-speed";
        else code=gameLure.SetReeling(rig.RigId,session,gameLureEpoch,seq,action=="reel-start")??(action=="reel-start"?"lure-reeling":"lure-stopped");
        GameReply(p,t,code);BroadcastGameLure();
    }
}
