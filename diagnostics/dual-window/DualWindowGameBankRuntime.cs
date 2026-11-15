using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    sealed class BankLiveRod
    {
        internal Guid Session;
        internal FloatFlightSimulation Flight;
        internal FloatFlightPoint Origin;
        internal FloatFlightWater Water;
        internal FloatFlightRules FlightRules;
        internal FloatCastLaunchPlan Plan;
        internal FloatCastTarget Target;
        internal FloatWaterSampleResult Landing;
        internal FloatFishingPool Pool;
        internal BottomFightHandoff Bottom;
        internal FloatFightSimulation Fight;
        internal BankTipEvent LastParkedBite;
        internal List<object> Trail;
        internal bool ContactAttempted;
    }
    readonly Dictionary<Guid,BankLiveRod> bankLive=new Dictionary<Guid,BankLiveRod>();
    Guid bankHandRig;
    long bankFrame,bankRodControlSequence;
    double bankClockFraction;
    BankRodScheduler bankClockOwner;
    bool BankCanOperate(Guid session)
    {
        return GameBankMode&&gameBankStand.CanOperateHeld(gameBankStand.Held,session,gameBankStand.ControlEpoch,gameBankStand.HandGeneration);
    }

    bool BankWireHand(ProbeWindow pane,string transport,Dictionary<string,object> message)
    {
        object value;bool supplied=message.TryGetValue("bankHand",out value);
        if(!GameBankMode){
            if(!supplied)return true;
            GameReply(pane,transport,"invalid-intent");return false;
        }
        if(gameBankStand.Held==Guid.Empty||gameBankStand.Pending!=null||!(value is string)||
            (string)value!=gameBankStand.HandGeneration.ToString(CultureInfo.InvariantCulture)){
            GameReply(pane,transport,"stand-hand-stale");return false;
        }
        return true;
    }
    BankRodState BankLiveFacts(Guid rig)
    {
        BankLiveRod live;
        if(GameBankMode&&rig==gameBankStand.Held&&gameCast!=null){
            var p=gameCast.Snapshot.Phase;
            if(p==FloatCastPhase.Charging||p==FloatCastPhase.Prepared)
                return new BankRodState(rig,game.Snapshot.State.BottomKitFor(rig).Equipment.RigRevision,Guid.Empty,BankRodPhase.Charging,true);
        }
        if(!bankLive.TryGetValue(rig,out live))return null;
        var durable=game.Snapshot.State.BankSessionFor(rig);if(durable==null||durable.Id!=live.Session)return null;
        if(durable.CapacityBlocked)return new BankRodState(rig,durable.RigRevision,durable.Id,BankRodPhase.PendingResult,true);
        var registered=gameBankScheduler==null?null:gameBankScheduler.ReadRig(rig);
        if(registered!=null)return registered;
        // An unconfirmed bottom or invalid contact stays in the hand until
        // an explicit recall. It must not become a supposedly waiting stand rod.
        return new BankRodState(rig,durable.RigRevision,live.Session,live.Flight.Snapshot.Phase==FloatFlightPhase.Flying?
            BankRodPhase.Flying:BankRodPhase.Settling,true);
    }
    BottomRigSnapshot BankBottomFor(Guid rig)
    {
        BankLiveRod live;
        return bankLive.TryGetValue(rig,out live)&&live.Bottom!=null?live.Bottom.Snapshot:null;
    }
    BankTipEvent BankLastParkedBite(Guid rig)
    {
        BankLiveRod rod;return bankLive.TryGetValue(rig,out rod)?rod.LastParkedBite:null;
    }

    void RegisterBankFlight(FloatSession active)
    {
        Guid rig=active.BankRigId;
        if(rig==Guid.Empty||rig!=gameBankStand.Held||GameHeldBankSession!=active)
            throw new InvalidOperationException("The newly paid bank cast no longer owns the hand.");
        ForgetSettledBankRods();
        bankHandRig=rig;gameBottom=null;gameBottomAttempt=Guid.Empty;gameFight=null;gameFishingPool=null;
        gameLandingCommand=null;gameLandingTicket=null;gameLandingRetry=false;gameFailureCommand=null;gameFailureTicket=null;gameFailureRetry=false;
        bankLive.Add(rig,new BankLiveRod{Session=active.Id,Flight=gameFlight,Origin=gameFlightOrigin,Water=gameFlightWater,
            FlightRules=gameFlightRules,Plan=gameWaterPlan,Target=gameWaterTarget});
    }
    void ActivateBankHand(bool force=false)
    {
        Guid next=GameBankMode?gameBankStand.Held:Guid.Empty;
        if(!force&&bankHandRig==next&&Object.ReferenceEquals(bankClockOwner,gameBankScheduler))return;
        CancelGameCast();ClearGameFightInput();
        BankLiveRod old;
        if(bankHandRig!=Guid.Empty&&bankLive.TryGetValue(bankHandRig,out old)){
            old.Landing=gameWaterLanding;old.Fight=gameFight;old.Trail=new List<object>(gameFlightTrail);
        }
        if(!Object.ReferenceEquals(bankClockOwner,gameBankScheduler)){
            if(bankClockOwner!=null){
                ForgetSettledBankRods(bankClockOwner);
                if(bankLive.Count!=0)throw new InvalidOperationException("A live bank simulation cannot lose its stand.");
            }
            bankClockOwner=gameBankScheduler;bankFrame=0;bankClockFraction=0;bankRodControlSequence=0;
        }
        bankHandRig=next;
        gameCastInput=null;gameReelInput=null;gameCastMillisecondRemainder=0;
        gameWaterPlan=null;gameWaterTarget=null;gameWaterLanding=null;gameWaterLandingSession=Guid.Empty;
        gameFlight=null;gameBottom=null;gameBite=null;gameFight=null;gameBottomAttempt=Guid.Empty;gameFishingPool=null;
        gameLineBreakRules=null;gameLandingCommand=null;gameLandingTicket=null;gameLandingRetry=false;
        gameFailureCommand=null;gameFailureTicket=null;gameFailureRetry=false;
        gameFlightTrail.Clear();InitializeGameCast();InitializeGameFlight();
        BankLiveRod hand;
        if(bankHandRig!=Guid.Empty&&bankLive.TryGetValue(bankHandRig,out hand)){
            gameWaterPlan=hand.Plan;gameWaterTarget=hand.Target;gameWaterLanding=hand.Landing;
            gameFlight=hand.Flight;gameFlightOrigin=hand.Origin;gameFlightWater=hand.Water;gameFlightRules=hand.FlightRules;
            if(hand.Trail!=null)gameFlightTrail.AddRange(hand.Trail);
            if(hand.Landing!=null)gameWaterLandingSession=hand.Session;
            gameBottom=hand.Bottom;gameFight=hand.Fight;gameFishingPool=hand.Pool;
            if(hand.ContactAttempted)gameBottomAttempt=hand.Session;
        }
    }
    void ForgetSettledBankRods(BankRodScheduler previous=null)
    {
        var state=game.Snapshot.State;if(state==null||bankLive.Count==0)return;
        foreach(var rig in new List<Guid>(bankLive.Keys)){
            var live=bankLive[rig];var session=state.BankSessionFor(rig);
            if(session!=null&&session.Id==live.Session)continue;
            if(live.Bottom!=null){
                string error=(previous??gameBankScheduler).DetachConfirmed(rig,live.Session);
                if(error!=null)throw new InvalidOperationException("A settled rod retained a physical submission: "+error);
            }
            live.Flight.End(live.Session);bankLive.Remove(rig);
        }
    }
    void AdvanceBankRods(double activeSeconds)
    {
        if(gameBankScheduler==null||game==null)return;
        var ownerView=game.Snapshot;var state=ownerView.State;
        if(state==null)return;
        ForgetSettledBankRods();
        if(gameFight!=null&&(!ownerView.CanSimulate||!GameReelCurrent()))ClearGameFightInput();
        double milliseconds=activeSeconds*1000+bankClockFraction;
        int remaining=(int)Math.Floor(milliseconds);bankClockFraction=milliseconds-remaining;
        bool paused=!ownerView.CanSimulate||GameFightTimeBlocked;
        if(remaining>0||paused){
            do{
                int amount=Math.Min(remaining,250);
                var tick=gameBankScheduler.Advance(checked(++bankFrame),amount,paused);
                if(tick.Code!=(paused?"clock-paused":"clock-advanced"))throw new InvalidOperationException("Bank clock rejected a host frame: "+tick.Code);
                foreach(var signal in tick.Signals){
                    BankLiveRod rod;
                    if(signal.Slot>0&&signal.Kind==BankTipKind.Bite&&bankLive.TryGetValue(signal.RigId,out rod)&&rod.Session==signal.SessionId)
                        rod.LastParkedBite=signal;
                }
                foreach(var failure in tick.Failures)CommitGameFailure(failure);
                remaining-=amount;
            }while(remaining>0&&!paused);
        }
        // Flights run only in the hand: the stand rejects flying and sinking
        // rods. Admit the new owner AFTER this frame, never charge its flight
        // time to a second solver. Existing parked sessions have already ticked.
        BankLiveRod held;
        if(!paused&&bankLive.TryGetValue(gameBankStand.Held,out held)&&!held.ContactAttempted&&
            held.Flight.Snapshot.Phase!=FloatFlightPhase.Flying){
            held.ContactAttempted=true;gameBottomAttempt=held.Session;
            held.Landing=gameWaterLanding;
            BottomEquipmentBridge equipment;
            if(GameReadyForFish()&&BottomOwnedEquipment.Bind(state,gameBankStand.Held,out equipment)==null){
                held.Pool=BuildGameFishingPool(held.Landing.Sample);
                var patch=GameBottomPatch();
                if(patch!=null&&held.Pool!=null){
                    BottomFightHandoff bottom;
                    uint seed=GameSelfTest?1u:BitConverter.ToUInt32(held.Session.ToByteArray(),0);
                    string error=BottomFightHandoff.AtWaterContact(held.Session,equipment.Assembly,patch,gameWater.Version,
                        held.Landing.Sample.Xmm,held.Landing.Sample.Zmm,seed,held.Pool.Environment,BottomFightConfiguration(state),out bottom);
                    if(error==null){
                        error=gameBankScheduler.AttachHeld(bottom);
                        if(error!=null)throw new InvalidOperationException("A paid bank cast could not register its physical rod: "+error);
                        held.Bottom=gameBottom=bottom;gameFishingPool=held.Pool;
                    }
                }
            }
        }
        if(activeSeconds>0&&bankLive.Count>0){
            BroadcastBottom();BroadcastBankStand();
            if(gameFight!=null)BroadcastGameFight();
        }
    }
    bool ReceiveBankRodEnd(ProbeWindow pane,GameBinding binding,Dictionary<string,object> intent,string transport)
    {
        object action;
        if(intent==null||!intent.TryGetValue("action",out action)||(!(action is string)||((string)action!="recall"&&(string)action!="release-current")))return false;
        AdvanceActivity();var state=game.Snapshot.State;FloatCommand parsed;
        if(state==null||!FloatPageCommands.TryParse(intent,state,out parsed)){GameReply(pane,transport,"invalid-intent");return true;}
        var current=GameHeldBankSession;
        try{
            FloatCommitResult result;
            var code=SettleSelectedBankRod(binding,current,parsed,out result);
            GameReply(pane,transport,code,result);
        }catch(FloatSaveUncertainException){ExitCode=4;GameReply(pane,transport,"save-outcome-unknown");}
        catch(IOException){GameReply(pane,transport,"save-write-rejected");}
        Broadcast();return true;
    }
    string SettleSelectedBankRod(GameBinding binding,FloatSession current,FloatCommand parsed,out FloatCommitResult result)
    {
        result=null;
        if(current==null||current!=GameHeldBankSession||current.Id!=parsed.SessionId||!BankCanOperate(current.Id))return "stand-hand-stale";
        if(GameFailureUnconfirmed||gameLandingRetry||gameFailureRetry)return "failure-result-required";
        var state=game.Snapshot.State;
        var command=new SettleBankBottomRod(state.ProfileId,state.BranchId,parsed.RequestId,parsed.EventId,current.BankRigId,current.Id,
            parsed is RecallFloat?BankRodEnding.Recall:BankRodEnding.Release,Guid.Empty,Guid.Empty,null,0,
            parsed.ExpectedSequence,parsed.ExpectedRigRevision,parsed.ExpectedRulesVersion);
        result=game.ExecuteUser(binding.View,binding.Document,binding.Lease,command);
        if(result.Written&&!result.IsReplay){
            ForgetSettledBankRods();ActivateBankHand(true);
            if(game.Snapshot.State.HasPendingBankResult&&bankLive.Count==0)RestoreBankResultHand();
        }
        return result.Code;
    }
}
