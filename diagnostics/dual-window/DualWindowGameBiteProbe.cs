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
    Dictionary<string,object> GameStrikeMessage(ProbeWindow pane,string lease,Guid session,Guid encounter,long sequence,string document=null)
    {
        return new Dictionary<string,object>{{"kind","game-strike"},{"documentId",document??pane.Ticket.DocumentId},{"lease",lease},
            {"transportId",Guid.NewGuid().ToString("D")},{"sessionId",session.ToString("D")},
            {"encounterId",encounter==Guid.Empty?null:encounter.ToString("D")},{"inputSequence",sequence.ToString(CultureInfo.InvariantCulture)}};
    }
    async Task<Dictionary<string,object>> GameStrikeWire(ProbeWindow pane,Dictionary<string,object> message)
    {
        await pane.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.sendRaw("+json.Serialize(message)+")");
        return await Wait(pane,s=>Field(s,"lastReply")!=null && (string)Field(s,"lastReply")["transportId"]==(string)message["transportId"]);
    }
    async Task GameWaitForBite(ProbeWindow pane,FloatBitePhase phase)
    {
        string session=game.Snapshot.State.Session.Id.ToString("D");
        await Wait(pane,s=>gameBite!=null && gameBite.Snapshot.SessionId.ToString("D")==session && gameBite.Snapshot.Phase==phase &&
            Field(s,"biteState")!=null && (string)Field(s,"biteState")["sessionId"]==session &&
            (string)Field(s,"biteState")["phase"]==(phase==FloatBitePhase.RetrieveRequired?"retrieve-required":phase.ToString().ToLowerInvariant()),12000);
    }
    async Task<string> GameBiteLifecycleProbe(ProbeWindow read,ProbeWindow pane,string lease)
    {
        Require(gameBite!=null && gameBite.Snapshot.Phase==FloatBitePhase.Settling,"Valid water did not start the settling runtime.");
        var runtime=gameBite;var saved=game.Snapshot.State;Guid session=saved.Session.Id;
        foreach(var bad in new object[]{"01","+1","9223372036854775808",1}){
            var message=GameStrikeMessage(pane,lease,session,Guid.Empty,1);message["inputSequence"]=bad;
            GameCode(await GameStrikeWire(pane,message),"invalid-intent",false);
        }
        var extra=GameStrikeMessage(pane,lease,session,Guid.Empty,1);extra.Add("massMg","1000000");
        GameCode(await GameStrikeWire(pane,extra),"invalid-intent",false);
        Require(gameViews[pane].StrikeHighWater==0,"Malformed strike consumed a valid input sequence.");
        GameCode(await GameStrikeWire(read,GameStrikeMessage(read,lease,session,Guid.Empty,1)),"control-required",false);
        GameCode(await GameStrikeWire(pane,GameStrikeMessage(pane,lease,Guid.NewGuid(),Guid.Empty,1)),"session-not-current",false);
        GameCode(await GameStrikeWire(pane,GameStrikeMessage(pane,lease,session,Guid.NewGuid(),2)),"encounter-not-current",false);
        Require(gameHookedEmissions==0 && runtime.Snapshot.BaitAttached,"Rejected identity changed bait or hooked a fish.");
        checks.Add("strike protocol rejects malformed sequence, extra fish data, observer and mismatched identities without hooks or costs");

        await GameWaitForBite(pane,FloatBitePhase.Strikeable);
        await GameMenu(pane,lease,true);
        var frozen=runtime.Snapshot;Guid encounter=frozen.EncounterId;
        await Task.Delay(220);
        Require(runtime.Snapshot.StepIndex==frozen.StepIndex && Object.ReferenceEquals(runtime.Snapshot.Target,frozen.Target),"Menu changed bite clock/target.");
        GameCode(await GameStrikeWire(pane,GameStrikeMessage(pane,lease,session,encounter,3)),"domain-paused",false);
        checks.Add("menu freezes an actual strikeable window and its locked target; paused strike is consumed without buffering");

        SetHostPause(PauseReason.Manual,true);await Wait(pane,s=>s["lease"]==null);
        await Task.Delay(180);
        Require(runtime.Snapshot.StepIndex==frozen.StepIndex,"Host pause advanced the fish window.");
        GameCode(await GameStrikeWire(pane,GameStrikeMessage(pane,lease,session,encounter,4)),"control-required",false);
        SetHostPause(PauseReason.Manual,false);lease=await Activate(pane);
        string oldDocument=pane.Ticket.DocumentId,oldLease=lease;
        Reload(pane);await Wait(pane,s=>(string)s["documentId"]!=oldDocument && Field(s,"biteState")!=null);
        Require(Object.ReferenceEquals(runtime,gameBite) && runtime.Snapshot.StepIndex==frozen.StepIndex &&
            runtime.Snapshot.EncounterId==encounter && Object.ReferenceEquals(runtime.Snapshot.Target,frozen.Target),"Page reconstruction rerolled or advanced fish.");
        GameCode(await GameStrikeWire(pane,GameStrikeMessage(pane,oldLease,session,encounter,4,oldDocument)),"control-required",false);
        lease=await Activate(pane);
        var projected=Field(await Ready(pane),"biteState");
        Require(projected.Count==9 && !projected.ContainsKey("target") && !projected.ContainsKey("massMg") && !projected.ContainsKey("openStep"),"Bite view exposed hidden fish data.");
        checks.Add("host pause and real page reload retain the same frozen encounter while stale authority and hidden target data remain rejected");

        await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('bite').scrollIntoView()");
        using(var preview=File.Create(Path.Combine(root,"game-bite-panel.png")))await pane.View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,preview);
        await GameMenu(pane,lease,false);
        await Wait(pane,s=>!(bool)Field(s,"snapshot")["MenuOpen"]);
        await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('strike').click()");
        await GameWaitForBite(pane,FloatBitePhase.Hooked);
        Require(gameHookedEmissions==1 && runtime.Snapshot.EncounterId==encounter && Object.ReferenceEquals(runtime.Snapshot.Target,frozen.Target),"Strike failed to hand off one locked encounter.");
        long used=gameViews[pane].StrikeHighWater;
        GameCode(await GameStrikeWire(pane,GameStrikeMessage(pane,lease,session,encounter,used)),"stale-input",false);
        GameCode(await GameStrikeWire(pane,GameStrikeMessage(pane,lease,session,encounter,used+1)),"bite-not-active",false);
        Require(gameHookedEmissions==1 && game.Snapshot.State.Sequence==saved.Sequence && game.Snapshot.State.BaitCount==saved.BaitCount &&
            game.Snapshot.State.WalletMinor==saved.WalletMinor && game.Snapshot.State.Creel.Count==0,"Strike granted fish or repeated spending.");
        checks.Add("real page strike button hooks the existing encounter exactly once without awarding fish, replaying or consuming another bait");
        return lease;
    }
    async Task GameBiteFailureProbe(ProbeWindow pane,string lease)
    {
        await GameCastAndWait(pane,lease);await GameWaitForWater(pane);
        var saved=game.Snapshot.State;var current=gameBite.Snapshot;
        GameCode(await GameStrikeWire(pane,GameStrikeMessage(pane,lease,current.SessionId,current.EncounterId,gameViews[pane].StrikeHighWater+1)),"empty-strike",false);
        Require(gameBite.Snapshot.Phase==FloatBitePhase.RetrieveRequired && !gameBite.Snapshot.BaitAttached &&
            game.Snapshot.State.Sequence==saved.Sequence && game.Snapshot.State.BaitCount==saved.BaitCount,"Empty strike granted/debited inventory or stayed active.");
        GameCode(await GameWire(pane,lease,GameIntent("recall")),"recall-candidate",true);
        Require(gameBite.Snapshot.Phase==FloatBitePhase.Ended,"Recall kept a failed bite alive.");
        checks.Add("empty strike requires recall and depletes only the already-spent hook bait with no second debit");

        await GameCastAndWait(pane,lease);await GameWaitForWater(pane);
        saved=game.Snapshot.State;
        await GameWaitForBite(pane,FloatBitePhase.RetrieveRequired);
        current=gameBite.Snapshot;await Task.Delay(150);
        Require(current.Reason=="bite-missed" && current.StepIndex==gameBite.Snapshot.StepIndex && !current.BaitAttached,"Missed fish restarted an invisible search.");
        GameCode(await GameStrikeWire(pane,GameStrikeMessage(pane,lease,current.SessionId,current.EncounterId,gameViews[pane].StrikeHighWater+1)),"bite-not-active",false);
        Require(game.Snapshot.State.Sequence==saved.Sequence && game.Snapshot.State.BaitCount==saved.BaitCount && gameHookedEmissions==1,"Late strike created rewards or new spending.");
        GameCode(await GameWire(pane,lease,GameIntent("recall")),"recall-candidate",true);
        checks.Add("real active time expires an unstruck fish window once; late strike cannot catch or silently rebait");
    }
}
