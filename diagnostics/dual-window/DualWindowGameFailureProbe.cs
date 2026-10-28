using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FishingDesktop.GameCore;
using Microsoft.Web.WebView2.Core;

sealed partial class DualWindowContext
{
    async Task<string> GameFailureProbe(ProbeWindow read,ProbeWindow pane,string lease)
    {
        Require(GameSelfTest,"Controlled slack setup belongs only to the private test.");
        var beforeCast=game.Snapshot.State;
        gameFailureFixture=true;
        try{await GameCastAndWait(pane,lease);await GameWaitForWater(pane);await GameHookCurrent(pane,lease);}
        finally{gameFailureFixture=false;}
        var runtime=gameFight;var paid=game.Snapshot.State;var target=runtime.Snapshot;
        Require(target.SlackM>1.9 && Math.Abs(target.OutLineM+target.SpoolRemainingM-50)<1e-8,"Controlled slack fixture did not conserve actual owned line.");
        await GameMenu(pane,lease,true);var frozen=runtime.Snapshot;await Task.Delay(250);
        Require(runtime.Snapshot.StepIndex==frozen.StepIndex&&runtime.Snapshot.UnhookExposureSteps==frozen.UnhookExposureSteps,"Menu accumulated unhook exposure.");
        checks.Add("controlled extra paid-out line and resting hooked target conserve line; menu freezes slack exposure rather than using wall time");

        gameFailurePrewriteArmed=true;
        await GameMenu(pane,lease,false);
        await GameWaitForFight(pane,FloatFightPhase.FailurePending,7000);
        await Wait(pane,s=>Field(s,"fightState")!=null && (bool)Field(s,"fightState")["canRetryFailure"]);
        Require(gameFailureRetry&&!gameFailurePrewriteArmed&&game.Snapshot.State.Sequence==paid.Sequence&&gameUnhookedConfirmations==0&&
            runtime.Snapshot.Reason=="failure-not-written"&&game.Snapshot.State.Session.Id==paid.Session.Id,"Failed unhook write was falsely confirmed.");
        var failure=gameFailureCommand;string digest=GameShopJournalDigest();long stoppedStep=runtime.Snapshot.StepIndex;
        await Task.Delay(250);
        Require(runtime.Snapshot.StepIndex==stoppedStep&&GameShopJournalDigest()==digest&&gameFailureCommand==failure,"Pending unhook advanced or automatically retried.");
        checks.Add("real active ticks emit one unhook attempt; injected prewrite refusal keeps the original session frozen with no automatic retry");

        GameCode(await GameWire(pane,lease,GameIntent("recall")),"failure-result-required",false);
        GameCode(await GameFightWire(pane,lease,"land"),"fight-not-active",false);
        GameCode(await GameFightWire(pane,lease,"reel-start"),"fight-not-active",false);
        GameCode(await GameStrikeWire(read,GameFightMessage(read,lease,"retry-escape",1)),"control-required",false);
        Require(GameShopJournalDigest()==digest&&game.Snapshot.State.Session.Id==paid.Session.Id,"Recall or another action bypassed unconfirmed unhook.");
        checks.Add("unconfirmed unhook refuses recall landing winding and observer retry while leaving the paid session unchanged");

        string oldDocument=pane.Ticket.DocumentId,oldLease=lease;
        Reload(pane);await Wait(pane,s=>(string)s["documentId"]!=oldDocument&&Field(s,"fightState")!=null);
        Require(Object.ReferenceEquals(runtime,gameFight)&&gameFailureCommand==failure&&gameFailureRetry,"Reload replaced the pending failure or original command.");
        GameCode(await GameStrikeWire(pane,GameFightMessage(pane,oldLease,"retry-escape",1,null,oldDocument)),"control-required",false);
        lease=await Activate(pane);await Wait(pane,s=>Field(s,"fightState")!=null&&(bool)Field(s,"fightState")["canRetryFailure"]);
        Require(await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('fight-status').textContent.includes('确认') && !document.getElementById('retry-escape').disabled")=="true","Pending failure was shown as a completed escape or lacked explicit retry.");
        await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('retry-escape').click()");
        await GameWaitForFight(pane,FloatFightPhase.Escaped);
        var saved=game.Snapshot.State;var receipt=saved.Receipts[saved.Receipts.Count-1];
        Require(saved.Sequence==paid.Sequence+1&&saved.Session==null&&saved.WalletMinor==paid.WalletMinor&&saved.BaitCount==paid.BaitCount&&
            saved.Stamina==paid.Stamina&&saved.Creel.Count==paid.Creel.Count&&saved.Equipment==paid.Equipment&&saved.LegacyRig==paid.LegacyRig&&saved.Rig.Revision==paid.Rig.Revision&&saved.Rig.LineLengthMm==paid.Rig.LineLengthMm&&saved.OwnedNet==paid.OwnedNet&&saved.FoodCount==paid.FoodCount&&
            receipt.Outcome==FloatOutcome.Unhooked&&receipt.SessionId==paid.Session.Id&&receipt.EncounterId==target.EncounterId&&receipt.EventId==failure.EventId&&gameUnhookedConfirmations==1,
            "Confirmed unhook changed resources, target, identities or count.");
        checks.Add("real reload retains failure ticket and explicit retry button confirms one original escape with no refund fish or equipment loss");

        digest=GameShopJournalDigest();GameCode(await GameFightWire(pane,lease,"retry-escape"),"failure-retry-unavailable",false);
        var replay=game.ExecuteHost(failure);Require(replay.Committed&&replay.IsReplay&&!replay.Written&&GameShopJournalDigest()==digest,"Failure replay wrote a second terminal.");
        Require(beforeCast.BaitCount-saved.BaitCount==1&&beforeCast.Stamina-saved.Stamina==2&&saved.Sequence==beforeCast.Sequence+2,"Failed cast consumed anything beyond the original cast cost.");
        await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('fight').scrollIntoView()");
        using(var preview=File.Create(Path.Combine(root,"game-escape-panel.png")))await pane.View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,preview);
        checks.Add("confirmed escape receipt replays without another write and final display preserves only the original cast costs");
        return lease;
    }
}
