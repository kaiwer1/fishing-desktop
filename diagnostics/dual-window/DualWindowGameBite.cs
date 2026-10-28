using System;
using System.Collections.Generic;
using System.Globalization;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    FloatBiteSimulation gameBite;
    double gameBiteMillisecondRemainder;
    int gameHookedEmissions;

    bool ReconcileGameBite()
    {
        if(gameBite==null || gameBite.Snapshot.Phase==FloatBitePhase.Ended)return false;
        var state=game.Snapshot;var bite=gameBite.Snapshot;
        if(state.State!=null && state.State.Session!=null && state.State.Session.Id==bite.SessionId && !state.PendingResult &&
            gameFlight!=null && gameFlight.Snapshot.SessionId==bite.SessionId && gameFlight.Snapshot.Phase==FloatFlightPhase.Waiting)return false;
        gameBite.End(bite.SessionId);gameBiteMillisecondRemainder=0;return true;
    }
    void AdvanceGameBite(double activeSeconds)
    {
        if(game==null)return;
        bool reconciled=ReconcileGameBite();
        if(!GameReadyForFish()){if(reconciled)BroadcastGameBite();return;}
        var session=game.Snapshot.State.Session.Id;
        if(gameBite==null || gameBite.Snapshot.SessionId!=session){
            // The only supported slice is the explicit starter rig and a flat,
            // still-water worm pool. No time/weather/pressure ecology is implied.
            var state=game.Snapshot.State;
            if(state.Rig.DefinitionKey!="float-starter" || state.Rules.BaitDefinitionKey!="worm")
                throw new InvalidOperationException("No bite configuration for this rig/bait.");
            var pool=new[]{new FloatBiteCandidate("carp",3,300000,1800000),new FloatBiteCandidate("crucian",7,100000,650000)};
            foreach(var row in pool){
                var rule=state.Rules.Find(row.SpeciesId);
                if(rule==null || row.MinimumMassMg<rule.MinimumMassMg || row.MaximumMassMg>rule.MaximumMassMg)
                    throw new InvalidOperationException("Bite catalog does not match the saved rules.");
            }
            // Private scripted checks use one explicit seed, but identical
            // durations/rate/target ranges and the actual activity clock.
            uint seed=GameSelfTest?1u:BitConverter.ToUInt32(session.ToByteArray(),0);
            gameBite=new FloatBiteSimulation(session,seed,new FloatBiteRules(20,20,30,80),new FloatBiteEnvironment(.18,pool));
            gameBiteMillisecondRemainder=0;
            // Landing consumed this activity sample. Start at step zero and do
            // not give the same elapsed interval to two successive phases.
            BroadcastGameBite();return;
        }
        var phase=gameBite.Snapshot.Phase;
        if(phase==FloatBitePhase.Hooked || phase==FloatBitePhase.RetrieveRequired || phase==FloatBitePhase.Ended){gameBiteMillisecondRemainder=0;return;}
        double milliseconds=activeSeconds*1000+gameBiteMillisecondRemainder;
        int whole=(int)Math.Floor(milliseconds);gameBiteMillisecondRemainder=milliseconds-whole;
        if(whole==0)return;
        while(whole>0){int step=Math.Min(250,whole);gameBite.Advance(session,step);whole-=step;}
        BroadcastGameBite();
    }
    void SendGameBite(ProbeWindow pane)
    {
        GameBinding binding;
        if(game==null || !pane.Ready || !gameViews.TryGetValue(pane,out binding) || binding.Document==null)return;
        var bite=gameBite==null?null:gameBite.Snapshot;
        // Deliberate allowlist: do not send the target, mass, threshold, PRNG,
        // successful window endpoints or full owner snapshot to the page.
        Send(pane,new {kind="game-bite-state",documentId=pane.Ticket.DocumentId,revision=checked(++binding.BiteRevision),state=new {
            sessionId=bite==null?null:bite.SessionId.ToString("D"),encounterId=bite==null || bite.EncounterId==Guid.Empty?null:bite.EncounterId.ToString("D"),
            phase=bite==null?"idle":bite.Phase==FloatBitePhase.RetrieveRequired?"retrieve-required":bite.Phase.ToString().ToLowerInvariant(),
            reason=bite==null?null:bite.Reason,stepIndex=(bite==null?0:bite.StepIndex).ToString(CultureInfo.InvariantCulture),
            searchEpoch=(bite==null?0:bite.SearchEpoch).ToString(CultureInfo.InvariantCulture),baitAttached=bite!=null && bite.BaitAttached,
            bobberOffsetMm=bite==null?0:bite.BobberOffsetMm,bobberLateralMm=bite==null?0:bite.BobberDriftMm}});
    }
    void BroadcastGameBite(){foreach(var pane in windows.Values)SendGameBite(pane);}
    static bool GameGuid(object raw,bool nullable,out Guid value)
    {
        value=Guid.Empty;if(raw==null)return nullable;
        var text=raw as string;
        return text!=null && Guid.TryParseExact(text,"D",out value) && value!=Guid.Empty && value.ToString("D")==text;
    }
    void ReceiveGameStrike(ProbeWindow pane,GameBinding binding,Dictionary<string,object> message,string transport)
    {
        long sequence;Guid session,encounter;object sessionRaw,encounterRaw;
        if(message.Count!=7 || !GameInputSequence(message,out sequence) || !message.TryGetValue("sessionId",out sessionRaw) ||
            !message.TryGetValue("encounterId",out encounterRaw) || !GameGuid(sessionRaw,false,out session) || !GameGuid(encounterRaw,true,out encounter)){
            GameReply(pane,transport,"invalid-intent");return;
        }
        if(sequence<=binding.StrikeHighWater){GameReply(pane,transport,"stale-input");return;}
        binding.StrikeHighWater=sequence;
        AdvanceActivity();
        if(!game.CanControl(binding.View,binding.Document,binding.Lease) || !game.Snapshot.CanSimulate){GameReply(pane,transport,"domain-paused");return;}
        if(!GameReadyForFish() || gameBite==null){GameReply(pane,transport,"bite-not-ready");return;}
        var result=gameBite.Strike(session,encounter);
        // The next stage consumes this same locked encounter. A strike does
        // not grant a fish, make an inventory transaction or auto-reel.
        if(result.ShouldStartFight)gameHookedEmissions=checked(gameHookedEmissions+1);
        GameReply(pane,transport,result.Code);BroadcastGameBite();
    }
}
