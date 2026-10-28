using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using FishingDesktop.GameCore;
using FishingDesktop.Diagnostics.HostPolicy;
using Microsoft.Web.WebView2.Core;

sealed partial class DualWindowContext
{
    Dictionary<string,object> GameFightMessage(ProbeWindow pane,string lease,string action,long sequence,int? value=null,string document=null)
    {
        var fight=gameFight.Snapshot;
        var message=new Dictionary<string,object>{{"kind","game-fight"},{"documentId",document??pane.Ticket.DocumentId},{"lease",lease},
            {"transportId",Guid.NewGuid().ToString("D")},{"sessionId",fight.SessionId.ToString("D")},{"encounterId",fight.EncounterId.ToString("D")},
            {"inputSequence",sequence.ToString(CultureInfo.InvariantCulture)},{"action",action}};
        if(value.HasValue)message.Add("value",value.Value);return message;
    }
    async Task<Dictionary<string,object>> GameFightWire(ProbeWindow pane,string lease,string action,int? value=null)
    {return await GameStrikeWire(pane,GameFightMessage(pane,lease,action,gameViews[pane].FightHighWater+1,value));}
    async Task GameWaitForFight(ProbeWindow pane,FloatFightPhase phase,int timeout=6000)
    {
        string session=gameFight.Snapshot.SessionId.ToString("D");
        await Wait(pane,s=>gameFight.Snapshot.SessionId.ToString("D")==session && gameFight.Snapshot.Phase==phase && Field(s,"fightState")!=null &&
            (string)Field(s,"fightState")["sessionId"]==session && (string)Field(s,"fightState")["phase"]==FightPhase(phase),timeout);
    }
    async Task GameHookCurrent(ProbeWindow pane,string lease)
    {
        await GameWaitForBite(pane,FloatBitePhase.Strikeable);var bite=gameBite.Snapshot;
        GameCode(await GameStrikeWire(pane,GameStrikeMessage(pane,lease,bite.SessionId,bite.EncounterId,gameViews[pane].StrikeHighWater+1)),"hooked",false);
        await GameWaitForFight(pane,FloatFightPhase.Fighting);
        Require(Object.ReferenceEquals(bite.Target,gameFight.Snapshot.Target) && !gameFight.Snapshot.Reeling,"Fight rerolled target or started automatic winding.");
    }
    async Task<string> GameFightLifecycleProbe(ProbeWindow read,ProbeWindow pane,string lease)
    {
        var runtime=gameFight;var state=game.Snapshot.State;var initial=runtime.Snapshot;
        var bad=GameFightMessage(pane,lease,"drag",1,11);
        GameCode(await GameStrikeWire(pane,bad),"invalid-intent",false);
        bad=GameFightMessage(pane,lease,"reel-start",1);bad.Add("massMg","1");
        GameCode(await GameStrikeWire(pane,bad),"invalid-intent",false);
        bad=GameFightMessage(pane,lease,"land",1);bad["inputSequence"]="01";
        GameCode(await GameStrikeWire(pane,bad),"invalid-intent",false);
        Require(gameViews[pane].FightHighWater==0,"Malformed fight intent changed high water.");
        GameCode(await GameStrikeWire(read,GameFightMessage(read,lease,"reel-start",1)),"control-required",false);
        bad=GameFightMessage(pane,lease,"land",1);bad["encounterId"]=Guid.NewGuid().ToString("D");
        GameCode(await GameStrikeWire(pane,bad),"fight-not-current",false);
        GameCode(await GameFightWire(pane,lease,"land"),"fish-too-far",false);
        Require(game.Snapshot.State.Sequence==state.Sequence && gameLandingCommand==null,"Distant fish was saved.");
        checks.Add("fight messages reject extra facts, bad levels, stale identities and distant landing before any inventory write");

        await Task.Delay(200);
        Require(!runtime.Snapshot.Reeling && runtime.Snapshot.ReeledThisStepM==0 && runtime.Snapshot.Target==initial.Target,"Hooked fish inherited a held reel or changed target.");
        lease=await Activate(pane);
        await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('reel-hold').dispatchEvent(new KeyboardEvent('keydown',{code:'Enter',bubbles:true}))");
        await Wait(pane,s=>runtime.Snapshot.Reeling && Field(s,"fightState")!=null && (bool)Field(s,"fightState")["reeling"]);
        await Task.Delay(180);var winding=runtime.Snapshot;
        Require(winding.ReeledThisStepM>0 && winding.OutLineM<initial.OutLineM,"Real page hold control did not wind line.");
        await GameMenu(pane,lease,true);var frozen=runtime.Snapshot;await Task.Delay(180);
        Require(!runtime.Snapshot.Reeling && runtime.Snapshot.StepIndex==frozen.StepIndex && runtime.Snapshot.Position.Z==frozen.Position.Z,"Menu kept held input or advanced fish.");
        await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('reel-hold').dispatchEvent(new KeyboardEvent('keyup',{code:'Enter',bubbles:true}))");
        GameCode(await GameFightWire(pane,lease,"reel-start"),"domain-paused",false);
        checks.Add("real page hold winds actual line; menu clears held input and freezes the same physical fish without queued resume");

        SetHostPause(PauseReason.Manual,true);await Wait(pane,s=>s["lease"]==null);
        string oldDocument=pane.Ticket.DocumentId,oldLease=lease;
        SetHostPause(PauseReason.Manual,false);
        Reload(pane);await Wait(pane,s=>(string)s["documentId"]!=oldDocument && Field(s,"fightState")!=null);
        Require(Object.ReferenceEquals(gameFight,runtime) && runtime.Snapshot.StepIndex==frozen.StepIndex && runtime.Snapshot.Target==initial.Target,"Reload replaced frozen fight.");
        GameCode(await GameStrikeWire(pane,GameFightMessage(pane,oldLease,"reel-start",1,null,oldDocument)),"control-required",false);
        lease=await Activate(pane);await GameMenu(pane,lease,false);await Task.Delay(120);
        Require(!runtime.Snapshot.Reeling && runtime.Snapshot.ReeledThisStepM==0,"Resume restored held reel.");
        var view=Field(await Ready(pane),"fightState");
        Require(!view.ContainsKey("target") && !view.ContainsKey("speciesId") && !view.ContainsKey("massMg"),"Fight page revealed locked fish details.");
        checks.Add("pause and real document replacement retain fish and line; old input cannot resume winding or reveal target details");

        GameCode(await GameFightWire(pane,lease,"drag",7),"drag-updated",false);
        GameCode(await GameFightWire(pane,lease,"speed",10),"speed-updated",false);
        GameCode(await GameFightWire(pane,lease,"reel-start"),"reel-held",false);
        long held=gameViews[pane].FightHighWater;
        GameCode(await GameStrikeWire(pane,GameFightMessage(pane,lease,"reel-stop",held-1)),"stale-input",false);
        Require(runtime.Snapshot.Reeling && runtime.Snapshot.DragLevel==7 && runtime.Snapshot.ReelLevel==10,"Old release stopped new held input.");
        await GameWaitForFight(pane,FloatFightPhase.LandingEligible,45000);
        GameCode(await GameFightWire(pane,lease,"reel-stop"),"reel-released",false);
        Require(Math.Abs(runtime.Snapshot.OutLineM+runtime.Snapshot.SpoolRemainingM-50)<1e-8 && game.Snapshot.State.Sequence==state.Sequence,
            "Winding changed line inventory or granted fish before explicit landing.");
        await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('fight').scrollIntoView()");
        using(var preview=File.Create(Path.Combine(root,"game-fight-panel.png")))await pane.View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,preview);
        checks.Add("current drag and speed affect a finite physical fight; stale release is ignored and net eligibility alone grants no fish");
        return lease;
    }
    async Task<string> GameNaturalLanding(ProbeWindow read,ProbeWindow pane,string lease,bool lifecycle,bool capacity)
    {
        await GameCastAndWait(pane,lease);await GameWaitForWater(pane);await GameHookCurrent(pane,lease);
        if(lifecycle)lease=await GameFightLifecycleProbe(read,pane,lease);
        else{
            GameCode(await GameFightWire(pane,lease,"speed",10),"speed-updated",false);
            GameCode(await GameFightWire(pane,lease,"reel-start"),"reel-held",false);
            await GameWaitForFight(pane,FloatFightPhase.LandingEligible,45000);
            GameCode(await GameFightWire(pane,lease,"reel-stop"),"reel-released",false);
        }
        var fish=gameFight.Snapshot;var before=game.Snapshot.State;int landings=gameNaturalLandings;
        lease=await Activate(pane);
        await Wait(pane,s=>Field(s,"fightState")!=null && (string)Field(s,"fightState")["phase"]=="landing-eligible");
        await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('land').click()");
        await GameWaitForFight(pane,capacity?FloatFightPhase.CapacityBlocked:FloatFightPhase.Landed);
        if(!capacity)Require(await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('notice').textContent === '已确认收鱼入护。'")=="true","Successful landing was displayed as a failure.");
        var after=game.Snapshot.State;
        Require(after.Sequence==before.Sequence+1 && after.BaitCount==before.BaitCount && after.WalletMinor==before.WalletMinor,"Landing charged new bait or awarded sale money.");
        var saved=capacity?after.Session.PendingFish:after.Creel[after.Creel.Count-1];
        Require(saved.SessionId==fish.SessionId && saved.EncounterId==fish.EncounterId && saved.SpeciesKey==fish.Target.SpeciesId && saved.MassMg==fish.Target.MassMg,
            "Saved fish differs from the originally hooked target.");
        Require(gameNaturalLandings==landings+(capacity?0:1),"Natural landing emission repeated or capacity counted as owned fish.");
        long sequence=after.Sequence;
        var retry=await GameFightWire(pane,lease,"land");
        GameCode(retry,capacity?"domain-paused":"landing-already-pending",false);
        Require(game.Snapshot.State.Sequence==sequence,"Repeated landing made a second durable result.");
        if(lifecycle)checks.Add("real page landing button saves exactly the originally hooked fish once; repeated landing gives no duplicate asset");
        if(capacity)checks.Add("natural hooked fish reaches full-creel pending with the same identity and mass, without replacing owned fish");
        return lease;
    }
}
