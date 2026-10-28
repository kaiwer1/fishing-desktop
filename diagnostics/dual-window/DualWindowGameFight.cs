using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    FloatFightSimulation gameFight;
    double gameFightMillisecondRemainder;
    GameCastInput gameReelInput;
    HostFloatFish gameLandingCommand;
    FloatLandingTicket gameLandingTicket;
    bool gameLandingRetry;
    int gameNaturalLandings;
    bool GameFightTimeBlocked {get{
        if(gameFight==null)return false;
        var phase=gameFight.Snapshot.Phase;
        return phase==FloatFightPhase.Faulted || phase==FloatFightPhase.LandingPending;
    }}
    void StartGameFight(FloatBiteSnapshot bite)
    {
        if(bite.Phase!=FloatBitePhase.Hooked || bite.Target==null || !GameReadyForFish() || gameFlight.Snapshot.SessionId!=bite.SessionId)
            throw new InvalidOperationException("Fight requires the locked current hooked encounter.");
        var state=game.Snapshot.State;var flight=gameFlight.Snapshot;
        var anchor=new FloatFightPoint(0,1.5,0);var fish=new FloatFightPoint(flight.Xmm/1000,-.2,flight.Zmm/1000);
        double distance=Math.Sqrt(fish.X*fish.X+1.7*1.7+fish.Z*fish.Z);
        // Original development equipment/flat-depth slice. The extra depth
        // uses the real existing spool budget; this is not new inventory line.
        var setup=new FloatFightSetup(bite.SessionId,bite.EncounterId,bite.Target,state.Rig.Revision,anchor,fish,state.Rig.LineLengthMm,distance,
            new FloatFightWater(0,-2,-40,40,3,80),new FloatFightRigRules(80,1,40,200,1,20,.2,1.6,40,3,3,5,5),
            new FloatFightFishRules(bite.Target.SpeciesId=="carp"?3:2,2,20,.05,.005,10),
            new FloatLandingRules(true,5,1,1,.5),GameSelfTest?91u:BitConverter.ToUInt32(bite.EncounterId.ToByteArray(),0));
        gameFight=new FloatFightSimulation(setup);gameFightMillisecondRemainder=0;gameReelInput=null;
        gameLandingCommand=null;gameLandingTicket=null;gameLandingRetry=false;
        BroadcastGameFight();
    }
    void ClearGameFightInput(ProbeWindow pane=null)
    {
        if(gameFight==null || (pane!=null && (gameReelInput==null || !Object.ReferenceEquals(gameReelInput.Pane,pane))))return;
        gameFight.ClearInput(gameFight.Snapshot.SessionId);gameReelInput=null;
    }
    bool GameReelCurrent()
    {
        var input=gameReelInput;GameBinding binding;
        return input!=null && Live(input.Pane) && input.Pane.Ready && !input.Pane.AwaitingNavigationStart && input.Pane.NavigationGeneration==input.Navigation &&
            GameAuthorized(input.Pane,input.DocumentId,input.WireLease,out binding) && Object.ReferenceEquals(binding,input.Binding) &&
            Object.ReferenceEquals(binding.Document,input.Document) && Object.ReferenceEquals(binding.Lease,input.Lease) && game.CanControl(binding.View,input.Document,input.Lease);
    }
    bool ReconcileGameFight()
    {
        if(gameFight==null)return false;
        var fight=gameFight.Snapshot;var state=game.Snapshot;
        if(fight.Phase==FloatFightPhase.Ended || fight.Phase==FloatFightPhase.Landed)return false;
        if(state.Faulted){ClearGameFightInput();return false;}
        if(state.State!=null && state.State.Session!=null && state.State.Session.Id==fight.SessionId){
            if(!state.CanSimulate || !GameReelCurrent())ClearGameFightInput();
            return false;
        }
        ClearGameFightInput();gameLandingRetry=false;
        return gameFight.End(fight.SessionId).Accepted;
    }
    void AdvanceGameFight(double activeSeconds)
    {
        if(gameFight==null)return;
        if(ReconcileGameFight()){BroadcastGameFight();return;}
        var before=gameFight.Snapshot;
        if(!game.Snapshot.CanSimulate || (before.Phase!=FloatFightPhase.Fighting && before.Phase!=FloatFightPhase.LandingEligible))return;
        double milliseconds=activeSeconds*1000+gameFightMillisecondRemainder;
        int whole=(int)Math.Floor(milliseconds);gameFightMillisecondRemainder=milliseconds-whole;
        if(whole==0)return;
        while(whole>0){int step=Math.Min(250,whole);gameFight.Advance(before.SessionId,step);whole-=step;}
        if(gameFight.Snapshot.Phase==FloatFightPhase.Faulted)ClearGameFightInput();
        BroadcastGameFight();
    }
    static int FightUnits(double value){return checked((int)Math.Round(value*1000,MidpointRounding.AwayFromZero));}
    static string FightPhase(FloatFightPhase phase)
    {
        if(phase==FloatFightPhase.LandingEligible)return "landing-eligible";
        if(phase==FloatFightPhase.LandingPending)return "landing-pending";
        if(phase==FloatFightPhase.CapacityBlocked)return "capacity-blocked";
        return phase.ToString().ToLowerInvariant();
    }
    void SendGameFight(ProbeWindow pane)
    {
        GameBinding binding;
        if(game==null || !pane.Ready || !gameViews.TryGetValue(pane,out binding) || binding.Document==null)return;
        var fight=gameFight==null?null:gameFight.Snapshot;
        Send(pane,new {kind="game-fight-state",documentId=pane.Ticket.DocumentId,revision=checked(++binding.FightRevision),state=new {
            sessionId=fight==null?null:fight.SessionId.ToString("D"),encounterId=fight==null?null:fight.EncounterId.ToString("D"),phase=fight==null?"idle":FightPhase(fight.Phase),
            reason=fight==null?null:fight.Reason,landingReason=fight==null?null:fight.LandingReason,stepIndex=(fight==null?0:fight.StepIndex).ToString(CultureInfo.InvariantCulture),
            xMm=fight==null?0:FightUnits(fight.Position.X),yMm=fight==null?0:FightUnits(fight.Position.Y),zMm=fight==null?0:FightUnits(fight.Position.Z),
            outLineMm=fight==null?0:FightUnits(fight.OutLineM),spoolRemainingMm=fight==null?0:FightUnits(fight.SpoolRemainingM),slackMm=fight==null?0:FightUnits(fight.SlackM),
            tensionMilliN=fight==null?0:FightUnits(fight.TensionN),dragMilliN=fight==null?0:FightUnits(fight.DragN),rodDeflectionMm=fight==null?0:FightUnits(fight.RodDeflectionM),
            energyPermille=fight==null?0:FightUnits(fight.Energy),reeling=fight!=null && fight.Reeling,dragLevel=fight==null?5:fight.DragLevel,reelLevel=fight==null?5:fight.ReelLevel,
            canRetryLanding=gameLandingRetry && fight!=null && fight.Phase==FloatFightPhase.LandingPending}});
    }
    void BroadcastGameFight(){foreach(var pane in windows.Values)SendGameFight(pane);}
    void ReceiveGameFight(ProbeWindow pane,GameBinding binding,Dictionary<string,object> message,string transport)
    {
        string action;long sequence;Guid session,encounter;object sessionRaw,encounterRaw,rawValue;int level=0;
        if(!StringField(message,"action",out action) || !GameInputSequence(message,out sequence) || !message.TryGetValue("sessionId",out sessionRaw) ||
            !message.TryGetValue("encounterId",out encounterRaw) || !GameGuid(sessionRaw,false,out session) || !GameGuid(encounterRaw,false,out encounter)){
            GameReply(pane,transport,"invalid-intent");return;
        }
        bool hasLevel=action=="drag" || action=="speed";
        if(message.Count!=(hasLevel?9:8) || (hasLevel?(!message.TryGetValue("value",out rawValue) || !(rawValue is int) || (level=(int)rawValue)<0 || level>10):
            (action!="reel-start" && action!="reel-stop" && action!="land" && action!="retry-land"))){GameReply(pane,transport,"invalid-intent");return;}
        if(sequence<=binding.FightHighWater){GameReply(pane,transport,"stale-input");return;}
        binding.FightHighWater=sequence;AdvanceActivity();
        if(!game.CanControl(binding.View,binding.Document,binding.Lease) || !game.Snapshot.CanSimulate){GameReply(pane,transport,"domain-paused");return;}
        if(gameFight==null || gameFight.Snapshot.SessionId!=session || gameFight.Snapshot.EncounterId!=encounter){GameReply(pane,transport,"fight-not-current");return;}
        FloatFightResult result;
        if(action=="reel-start" || action=="reel-stop"){
            result=gameFight.SetReeling(session,encounter,action=="reel-start");
            if(result.Accepted){
                gameReelInput=action=="reel-stop"?null:new GameCastInput{Pane=pane,Binding=binding,Document=binding.Document,Lease=binding.Lease,
                    WireLease=binding.WireLease,DocumentId=pane.Ticket.DocumentId,Navigation=pane.NavigationGeneration,Sequence=sequence};
            }
        }else if(action=="drag")result=gameFight.SetDragLevel(session,encounter,level);
        else if(action=="speed")result=gameFight.SetReelLevel(session,encounter,level);
        else if(action=="retry-land")result=gameFight.RetryLanding(gameLandingTicket);
        else result=gameFight.TryLand(session,encounter,Guid.NewGuid());
        if(result.ShouldCommitLanding){
            if(gameLandingCommand==null){
                var state=game.Snapshot.State;var target=result.Snapshot.Target;
                gameLandingTicket=result.LandingTicket;
                Guid fishId=Guid.NewGuid();
                gameLandingCommand=new HostFloatFish(state.ProfileId,state.BranchId,Guid.NewGuid(),result.LandingTicket.LandingId,session,fishId,encounter,
                    target.SpeciesId,target.MassMg,state.Sequence,state.Rig.Revision,state.RulesVersion);
            }
            CommitGameLanding(pane,transport,result);return;
        }
        GameReply(pane,transport,result.Code);BroadcastGameFight();
    }
    void CommitGameLanding(ProbeWindow pane,string transport,FloatFightResult pending)
    {
        ClearGameFightInput();gameLandingRetry=false;
        try{
            var result=game.ExecuteHost(gameLandingCommand);
            if(result.Committed && result.Receipt!=null && (result.Receipt.Outcome==FloatOutcome.Landed || result.Receipt.Outcome==FloatOutcome.CapacityRejected)){
                gameFight.ConfirmLanding(pending.LandingTicket,pending.LandingAttempt,result.Receipt.Outcome==FloatOutcome.Landed?FloatLandingOutcome.Landed:FloatLandingOutcome.CapacityBlocked);
                if(result.Receipt.Outcome==FloatOutcome.Landed && result.Written && !result.IsReplay)gameNaturalLandings=checked(gameNaturalLandings+1);
            }else{
                gameFight.ConfirmLanding(pending.LandingTicket,pending.LandingAttempt,FloatLandingOutcome.KnownNotWritten);gameLandingRetry=true;
            }
            GameReply(pane,transport,result.Code,result);Broadcast();
        }catch(FloatSaveUncertainException){
            gameFight.ConfirmLanding(pending.LandingTicket,pending.LandingAttempt,FloatLandingOutcome.Unknown);ExitCode=4;
            GameReply(pane,transport,"save-outcome-unknown");Broadcast();
        }catch(IOException){
            gameFight.ConfirmLanding(pending.LandingTicket,pending.LandingAttempt,FloatLandingOutcome.KnownNotWritten);gameLandingRetry=true;
            GameReply(pane,transport,"landing-not-written");Broadcast();
        }
    }
}
