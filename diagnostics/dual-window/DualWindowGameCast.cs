using System;
using System.Collections.Generic;
using System.Globalization;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    FloatCastPreparation gameCast;
    GameCastInput gameCastInput;
    double gameCastMillisecondRemainder;
    int gameFlightEmissions;
    sealed class GameCastInput
    {
        internal ProbeWindow Pane;
        internal GameBinding Binding;
        internal FloatDocumentTicket Document;
        internal FloatControlLease Lease;
        internal string WireLease,DocumentId;
        internal int Navigation;
        internal long Sequence;
        internal FloatCastGesture Gesture;
    }
    void InitializeGameCast()
    {
        // This is a fixed host target for input integration, not a water raycast.
        gameCast=new FloatCastPreparation(game,new FloatCastPreparationRules(1600,150),
            target=>target.Xmm==0 && target.Ymm==0 && target.Zmm==15000 && target.RequiredLineMm==15000);
    }
    static bool GameInputSequence(Dictionary<string,object> message,out long sequence)
    {
        string value; sequence=0;
        return StringField(message,"inputSequence",out value) && value.Length>0 && value.Length<=19 && value[0]>='1' && value[0]<='9' &&
            Int64.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out sequence) && sequence>0 &&
            value==sequence.ToString(CultureInfo.InvariantCulture);
    }
    bool GameCastCurrent(GameCastInput input)
    {
        GameBinding binding;
        return input!=null && Object.ReferenceEquals(input,gameCastInput) && Live(input.Pane) && input.Pane.Ready &&
            !input.Pane.AwaitingNavigationStart && input.Pane.NavigationGeneration==input.Navigation &&
            GameAuthorized(input.Pane,input.DocumentId,input.WireLease,out binding) && Object.ReferenceEquals(binding,input.Binding) &&
            Object.ReferenceEquals(binding.Document,input.Document) && Object.ReferenceEquals(binding.Lease,input.Lease) &&
            game.CanControl(binding.View,input.Document,input.Lease);
    }
    void CancelGameCast(ProbeWindow pane=null)
    {
        if(gameCast==null || gameCastInput==null || (pane!=null && !Object.ReferenceEquals(gameCastInput.Pane,pane)))return;
        var phase=gameCast.Snapshot.Phase;
        if(phase==FloatCastPhase.Charging || phase==FloatCastPhase.Prepared){gameCast.Cancel(gameCastInput.Gesture);BroadcastGameCast();}
        gameCastMillisecondRemainder=0;
    }
    void AdvanceGameCast(double activeSeconds)
    {
        if(gameCast==null || gameCastInput==null)return;
        if(!GameCastCurrent(gameCastInput) || !game.Snapshot.CanSimulate){CancelGameCast();return;}
        if(gameCast.Snapshot.Phase!=FloatCastPhase.Charging){gameCastMillisecondRemainder=0;return;}
        // ActiveClock is the sole elapsed-time source. Preserve fractional ms
        // between samples; never recover capped stalls or paused wall time.
        double milliseconds=activeSeconds*1000+gameCastMillisecondRemainder;
        int whole=(int)Math.Floor(milliseconds);
        gameCastMillisecondRemainder=milliseconds-whole;
        if(whole==0)return;
        while(whole>0){int step=Math.Min(250,whole);gameCast.Advance(gameCastInput.Gesture,step);whole-=step;}
        BroadcastGameCast();
    }
    void SendGameCast(ProbeWindow pane)
    {
        GameBinding binding;
        if(gameCast==null || !pane.Ready || !gameViews.TryGetValue(pane,out binding) || binding.Document==null)return;
        var state=gameCast.Snapshot;
        Send(pane,new {kind="game-cast-state",documentId=pane.Ticket.DocumentId,revision=checked(++binding.CastRevision),
            inputSequence=gameCastInput==null?null:gameCastInput.Sequence.ToString(CultureInfo.InvariantCulture),
            phase=state.Phase.ToString().ToLowerInvariant(),powerPermille=state.PowerPermille,elapsedMilliseconds=state.ElapsedMilliseconds});
    }
    void BroadcastGameCast(){foreach(var pane in windows.Values)SendGameCast(pane);}
    void ReceiveGameCast(ProbeWindow pane,GameBinding binding,Dictionary<string,object> message,string transport)
    {
        string action;long sequence;
        if(message.Count!=6 || !StringField(message,"action",out action) || !GameInputSequence(message,out sequence) ||
            (action!="begin" && action!="release" && action!="cancel")){
            GameReply(pane,transport,"invalid-intent");return;
        }
        AdvanceActivity();
        FloatCastResult result;
        if(action=="begin"){
            // Consume even refused new begin attempts. A late delivery must
            // never become a fresh cast after the current session ends.
            if(sequence<=binding.CastHighWater){GameReply(pane,transport,"stale-input");return;}
            binding.CastHighWater=sequence;
            var state=game.Snapshot.State;
            if(state==null){GameReply(pane,transport,"state-unavailable");return;}
            result=gameCast.Begin(binding.View,binding.Document,binding.Lease,new FloatCastTarget(0,0,15000,state.RulesVersion,15000));
            if(result.Accepted){
                gameCastInput=new GameCastInput {Pane=pane,Binding=binding,Document=binding.Document,Lease=binding.Lease,
                    WireLease=binding.WireLease,DocumentId=pane.Ticket.DocumentId,Navigation=pane.NavigationGeneration,Sequence=sequence,Gesture=result.Gesture};
                gameCastMillisecondRemainder=0;
            }
        }else{
            var input=gameCastInput;
            if(!GameCastCurrent(input) || input.Sequence!=sequence || !Object.ReferenceEquals(input.Binding,binding)){
                GameReply(pane,transport,"gesture-not-current");return;
            }
            result=action=="cancel"?gameCast.Cancel(input.Gesture):gameCast.Release(input.Gesture);
            if(action=="release" && result.Accepted){
                // Temporary explicit host release event. The queued event must
                // recheck its captured authority; it is not an animation timer.
                dispatcher.BeginInvoke(new Action(()=>CommitGameCast(input)));
            }
        }
        GameReply(pane,transport,result.Code);BroadcastGameCast();
    }
    void CommitGameCast(GameCastInput input)
    {
        if(stopping || gameCast==null || !Object.ReferenceEquals(input,gameCastInput))return;
        try{
            AdvanceActivity();
            if(!GameCastCurrent(input) || !game.Snapshot.CanSimulate){CancelGameCast(input.Pane);return;}
            if(gameCast.Snapshot.Phase!=FloatCastPhase.Prepared)return;
            var result=gameCast.CommitPrepared(input.Gesture);
            if(result.ShouldStartFlight){
                gameFlightEmissions=checked(gameFlightEmissions+1);
                foreach(var pane in windows.Values)Send(pane,new {kind="game-flight",documentId=pane.Ticket.DocumentId,revision=gameFlightEmissions,
                    inputSequence=input.Sequence.ToString(CultureInfo.InvariantCulture),sessionId=result.Snapshot.SessionId.ToString("D"),powerPermille=result.Snapshot.PowerPermille});
            }
            // Preparation converts an uncertain file outcome to Unknown. Keep
            // its visible fault state, but never report a later exit as normal.
            if(game.Snapshot.Faulted)ExitCode=4;
            Broadcast();
        }catch(Exception error){Fail(error);}
    }
}
