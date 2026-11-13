using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FishingDesktop.GameCore;
using FishingDesktop.Diagnostics.HostPolicy;

sealed partial class DualWindowContext
{
    FloatCampWork gameCamp;
    readonly ActiveClock gameCampClock=new ActiveClock(.25);
    double gameCampRemainder;
    int gameCampPublishedElapsed;
    string gameCampPublishedPhase;
    GameCampBinding gameCampBinding;
    bool gameCampPrewriteFaultArmed,gameCampCommitting;
    sealed class GameCampBinding
    {
        internal ProbeWindow Pane;
        internal GameBinding Binding;
        internal string DocumentId,WireLease;
        internal FloatDocumentTicket Document;
        internal FloatControlLease Lease;
        internal int Navigation;
    }
    void InitializeGameCamp(){gameCamp=new FloatCampWork(game);}
    bool GameCampActive
    {
        get{if(gameCamp==null)return false;string phase=gameCamp.Snapshot.Phase;return phase=="working"||phase=="ready"||phase=="retry";}
    }
    bool GameCampCurrent()
    {
        var input=gameCampBinding;GameBinding current;
        return input!=null&&Live(input.Pane)&&input.Pane.Ready&&!input.Pane.AwaitingNavigationStart&&
            input.Navigation==input.Pane.NavigationGeneration&&GameAuthorized(input.Pane,input.DocumentId,input.WireLease,out current)&&
            Object.ReferenceEquals(input.Binding,current)&&Object.ReferenceEquals(current.Document,input.Document)&&Object.ReferenceEquals(current.Lease,input.Lease)&&
            game.CanControl(current.View,current.Document,current.Lease);
    }
    void ClearGameCamp(ProbeWindow pane=null)
    {
        if(gameCamp==null||(pane!=null&&(gameCampBinding==null||!Object.ReferenceEquals(gameCampBinding.Pane,pane))))return;
        gameCamp.Cancel();gameCampBinding=null;gameCampRemainder=0;
    }
    void AdvanceGameCamp(double now)
    {
        if(gameCamp==null)return;
        bool eligible=GameCampActive&&GameCampCurrent()&&owner.ActivityState().CanRun;
        // Separate from the fishing clock: work requires the open menu, while
        // fishing remains paused there. Both use the same monotonic host watch.
        double seconds=gameCampClock.Advance(now,eligible);
        if(GameCampActive){
            if(!eligible)ClearGameCamp();
            else{
                double milliseconds=seconds*1000+gameCampRemainder;int whole=(int)Math.Floor(milliseconds);
                gameCampRemainder=milliseconds-whole;
                if(whole==0)gameCamp.Advance(0);
                while(whole>0){int step=Math.Min(250,whole);gameCamp.Advance(step);whole-=step;}
            }
        }
        var snapshot=gameCamp.Snapshot;
        if(snapshot.Phase!=gameCampPublishedPhase||snapshot.ElapsedMilliseconds-gameCampPublishedElapsed>=250){
            gameCampPublishedPhase=snapshot.Phase;gameCampPublishedElapsed=snapshot.ElapsedMilliseconds;
            foreach(var pane in windows.Values)SendGameCamp(pane);
        }
    }
    void SendGameCamp(ProbeWindow pane)
    {
        GameBinding binding;
        if(gameCamp==null||!pane.Ready||!gameViews.TryGetValue(pane,out binding)||binding.Document==null)return;
        var snapshot=game.ReadView(binding.View,binding.Document);GameBinding current;
        bool authorized=GameAuthorized(pane,pane.Ticket.DocumentId,binding.WireLease,out current)&&Object.ReferenceEquals(current,binding)&&game.CanControl(binding.View,binding.Document,binding.Lease);
        string reason=snapshot.Faulted?"camp-work-save-unknown":snapshot.ShutdownConfirmed?"owner-stopped":!authorized?"control-required":
            !snapshot.MenuOpen?"menu-required":FloatCampWorkRules.Check(snapshot.State);
        var work=gameCamp.Snapshot;
        if(work.Phase=="unknown")reason="camp-work-save-unknown";
        if(reason==null){
            var budget=game.SaveBudget;
            if(budget.RemainingTransactions<1||budget.RemainingBytes<FloatCampWork.RequiredClaimFrameBytes)reason="save-capacity";
        }
        Send(pane,new{kind="game-camp-state",documentId=pane.Ticket.DocumentId,revision=checked(++binding.CampRevision),state=new{
            available=reason==null,reason=reason,lease=authorized?binding.WireLease:null,phase=work.Phase,
            elapsedMilliseconds=work.ElapsedMilliseconds,requiredMilliseconds=FloatCampWorkRules.DurationMilliseconds,
            rewardMinor=FloatCampWorkRules.RewardMinor.ToString(CultureInfo.InvariantCulture),
            stateSequence=snapshot.State==null?null:snapshot.State.Sequence.ToString(CultureInfo.InvariantCulture)}});
    }
    void ReceiveGameCamp(ProbeWindow pane,GameBinding binding,Dictionary<string,object> message,string document,string lease,string transport)
    {
        GameBinding current;string action;long input;
        if(!GameAuthorized(pane,document,lease,out current)||!Object.ReferenceEquals(binding,current)||!game.CanControl(binding.View,binding.Document,binding.Lease)){
            GameCampReply(pane,transport,"control-required");return;
        }
        if(!GameEquipmentKeys(message,"inputSequence")||!StringField(message,"action",out action)||
            (action!="start"&&action!="claim"&&action!="cancel")||!GameInputSequence(message,out input)){
            GameCampReply(pane,transport,"invalid-intent");return;
        }
        AdvanceActivity();
        if(!GameAuthorized(pane,document,lease,out current)||!Object.ReferenceEquals(binding,current)||!game.CanControl(binding.View,binding.Document,binding.Lease)){
            GameCampReply(pane,transport,"control-required");return;
        }
        if(input<=binding.CampHighWater){GameCampReply(pane,transport,"stale-input");return;}binding.CampHighWater=input;
        if(action=="start"){
            if(binding.Shop!=null||binding.Equipment!=null||binding.Spares!=null){GameCampReply(pane,transport,"menu-operation-pending");return;}
            string code=gameCamp.Start(binding.View,binding.Document,binding.Lease);
            if(code=="camp-work-started"){
                gameCampBinding=new GameCampBinding{Pane=pane,Binding=binding,DocumentId=document,WireLease=lease,Document=binding.Document,Lease=binding.Lease,Navigation=pane.NavigationGeneration};
                gameCampClock.Advance(activityWatch.Elapsed.TotalSeconds,false);gameCampRemainder=0;
            }
            GameCampReply(pane,transport,code);
        }else if(action=="cancel"){
            string code=gameCamp.Cancel();gameCampBinding=null;gameCampRemainder=0;GameCampReply(pane,transport,code);
        }else{
            if(!GameCampCurrent()){ClearGameCamp();GameCampReply(pane,transport,"camp-work-not-current");return;}
            FloatCommitResult result;gameCampCommitting=true;
            try{result=gameCamp.Claim(binding.View,binding.Document,binding.Lease);}finally{gameCampCommitting=false;}
            if(result.Code=="camp-work-save-unknown")ExitCode=4;
            GameCampReply(pane,transport,result.Code,result);
        }
    }
    void GameCampReply(ProbeWindow pane,string transport,string code,FloatCommitResult result=null)
    {GameReply(pane,transport,code,result);Broadcast();}
    void ArmGameCampPrewriteFailure()
    {
        if(!GameSelfTest||gameCampPrewriteFaultArmed)throw new InvalidOperationException("Camp failure injection is private and single-use.");
        gameCampPrewriteFaultArmed=true;
    }
    void GameCampSaveFault(FloatSaveFaultPoint point)
    {
        if(GameSelfTest&&gameCampCommitting&&gameCampPrewriteFaultArmed&&point==FloatSaveFaultPoint.BeforeWrite){
            gameCampPrewriteFaultArmed=false;throw new IOException("Private camp reward failure before write.");
        }
    }
}
