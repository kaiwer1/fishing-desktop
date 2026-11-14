using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FishingDesktop.GameCore;
using Microsoft.Web.WebView2.Core;

sealed partial class DualWindowContext
{
    async void RunGameLineBreakProbe()
    {
        try{
            Require(GameLineBreakSelfTest,"Private line-break test required.");
            var bare=CreateGameFixture();bool rejected=false;
            try{ValidateGameLineBreakCatalog(bare);}catch(InvalidOperationException){rejected=true;}
            Require(rejected,"Missing recovery offers were accepted.");
            var subset=new FloatEquipmentOffers(1,bare.Equipment.Catalog,new[]{new FloatEquipmentOffer("hook-single","single-hook",FloatEquipmentKind.Hook,35)},bare.Rules.MaximumWalletMinor);
            var configure=new ConfigureFloatEquipmentOffers(bare.ProfileId,bare.BranchId,Guid.NewGuid(),Guid.NewGuid(),FloatEquipmentOffersCodec.Encode(subset),
                bare.Sequence,bare.Rig.Revision,bare.RulesVersion,bare.Equipment.InventoryRevision,bare.Equipment.RulesVersion);
            var incomplete=FloatGameReducer.Reduce(bare,configure,bare.Rules).Candidate;Require(incomplete!=null,"Private subset fixture failed.");
            rejected=false;try{ValidateGameLineBreakCatalog(incomplete);}catch(InvalidOperationException){rejected=true;}
            Require(rejected,"Incomplete replacement offers were accepted.");
            var read=windows[false];var pane=windows[true];await Ready(read);await Ready(pane);
            var initial=game.Snapshot.State;
            Require(initial.Sequence==1&&initial.EquipmentOffers!=null,"Explicit recovery catalogue missing.");
            Guid lineId=initial.Rig.LineId.Value,reelId=initial.Rig.ReelId.Value;
            string lease=await Activate(pane);
            await GameCastAndWait(pane,lease);await GameWaitForWater(pane);await GameHookCurrent(pane,lease);
            var runtime=gameFight;var paid=game.Snapshot.State;
            var page=await Wait(pane,p=>Field(p,"fightState")!=null&&(bool)Field(p,"fightState")["breakEnabled"]);
            Require(gameLineBreakRules.LineId==lineId&&gameLineBreakRules.LineRevision==paid.Equipment.Find(lineId).Revision,"Policy did not bind installed line.");
            checks.Add("explicit private mode binds the full starter catalogue and actual installed line to a naturally hooked encounter");

            var bad=GameFightMessage(pane,lease,"drag",gameViews[pane].FightHighWater+1,10);bad.Add("breakThresholdMilliN",1);
            GameCode(await GameStrikeWire(pane,bad),"invalid-intent",false);
            GameCode(await GameStrikeWire(read,GameFightMessage(read,lease,"reel-start",1)),"control-required",false);
            GameCode(await GameFightWire(pane,lease,"drag",0),"drag-updated",false);
            GameCode(await GameFightWire(pane,lease,"speed",10),"speed-updated",false);
            GameCode(await GameFightWire(pane,lease,"reel-start"),"reel-held",false);
            await Task.Delay(1000);
            Require(runtime.Snapshot.Phase==FloatFightPhase.Fighting&&runtime.Snapshot.TensionN<5&&runtime.Snapshot.LineBreakExposureSteps==0&&game.Snapshot.State.Sequence==paid.Sequence,"Low drag did not protect the line.");
            checks.Add("actual low-drag winding survives while observer and page-supplied strength are refused without transactions");

            await GameMenu(pane,lease,true);long step=runtime.Snapshot.StepIndex;await Task.Delay(220);
            Require(runtime.Snapshot.StepIndex==step&&!runtime.Snapshot.Reeling&&runtime.Snapshot.LineBreakExposureSteps==0,"Menu accumulated load exposure.");
            await GameMenu(pane,lease,false);
            gameFailurePrewriteArmed=true;
            GameCode(await GameFightWire(pane,lease,"drag",10),"drag-updated",false);
            GameCode(await GameFightWire(pane,lease,"reel-start"),"reel-held",false);
            await GameWaitForFight(pane,FloatFightPhase.FailurePending,12000);
            await Wait(pane,p=>Field(p,"fightState")!=null&&(bool)Field(p,"fightState")["canRetryFailure"]);
            var failure=gameFailureCommand as HostFloatTerminalLoss;var evidence=gameFailureTicket.LineBreakEvidence;
            Require(failure!=null&&evidence!=null&&gameFailureTicket.Reason=="line-overloaded"&&!gameFailurePrewriteArmed&&gameFailureRetry&&
                game.Snapshot.State.Sequence==paid.Sequence&&game.Snapshot.State.Equipment.Find(lineId).RemainingLineMm==50000&&gameLineBreakConfirmations==0,"Unwritten break changed ownership.");
            checks.Add("menu freezes physical load and subsequent high-drag winding produces a real overload ticket before a scripted prewrite refusal");
            string digest=GameShopJournalDigest();step=runtime.Snapshot.StepIndex;await Task.Delay(180);
            GameCode(await GameWire(pane,lease,GameIntent("recall")),"failure-result-required",false);
            Require(GameShopJournalDigest()==digest&&runtime.Snapshot.StepIndex==step&&gameFailureCommand==failure,"Unconfirmed loss retried or disappeared.");
            checks.Add("unconfirmed break freezes the same command and refuses recall without spending or refunding anything");

            string document=pane.Ticket.DocumentId,oldLease=lease;Reload(pane);
            await Wait(pane,p=>(string)p["documentId"]!=document&&Field(p,"fightState")!=null);
            GameCode(await GameStrikeWire(pane,GameFightMessage(pane,oldLease,"retry-escape",1,null,document)),"control-required",false);
            Require(Object.ReferenceEquals(gameFight,runtime)&&gameFailureCommand==failure,"Reload discarded physical evidence.");
            lease=await Activate(pane);
            await Wait(pane,p=>Field(p,"fightState")!=null&&(bool)Field(p,"fightState")["canRetryFailure"]);
            await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('retry-escape').click()");
            await GameWaitForFight(pane,FloatFightPhase.Escaped);
            var state=game.Snapshot.State;var receipt=state.Receipts[state.Receipts.Count-1];
            Require(state.Sequence==3&&state.Session==null&&state.Rig.HookId==null&&state.Rig.BobberId==null&&state.Rig.SinkerId==null&&
                state.Rig.LineId==lineId&&state.Rig.LineLengthMm==50000-evidence.OutLineMm&&gameLineBreakConfirmations==1&&gameUnhookedConfirmations==0&&
                receipt.RequestId==failure.RequestId&&receipt.EventId==failure.EventId&&receipt.Outcome==FloatOutcome.TerminalLost&&receipt.Fingerprint==failure.Fingerprint,"Explicit retry did not confirm the exact physical loss.");
            checks.Add("real reload retains evidence and the actual retry button confirms one atomic line and terminal-component loss");
            digest=GameShopJournalDigest();var replay=game.ExecuteHost(failure);
            Require(replay.Committed&&replay.IsReplay&&!replay.Written&&GameShopJournalDigest()==digest,"Break replay wrote a second loss.");
            Require(await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('fight-status').textContent.includes('断线')")=="true","Confirmed break was presented as harmless unhooking.");
            await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('fight').scrollIntoView()");
            using(var preview=File.Create(Path.Combine(root,"game-line-break-panel.png")))await pane.View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,preview);
            checks.Add("confirmed loss replay is idempotent and the page explicitly displays a saved line break");

            GameCode(await GameCastWire(pane,lease,"begin",NextGameInput(pane)),"missing-component",false);
            Require(game.Snapshot.State.BaitCount==11&&game.Snapshot.State.Stamina==98,"Incomplete rig spent another cast.");
            await GameMenu(pane,lease,true);await SparesProbeReady(pane);
            foreach(string sku in new[]{"hook-single","float-3g","sinker-2_5g","nylon-018-50m"}){
                await SparesProbeClick(pane,"spares-quote-"+sku,"spares-quoted",false);
                await SparesProbeClick(pane,"spares-buy","equipment-purchase-candidate",true);
            }
            checks.Add("missing components prevent casting and four real menu purchases buy replacements without restoring the old line");
            foreach(var slot in new[]{FloatEquipmentSlot.Hook,FloatEquipmentSlot.Bobber,FloatEquipmentSlot.Sinker}){
                string key=slot.ToString().ToLowerInvariant();FloatEquipmentKind kind=(FloatEquipmentKind)Enum.Parse(typeof(FloatEquipmentKind),slot.ToString());
                await GameEquipmentProbeSelect(pane,"equipment-slot-"+key,SparesProbeInventory(kind).ToString("D"));
                await GameEquipmentProbeClick(pane,"equipment-apply-"+key,"assembly-candidate",true);
            }
            Guid newLine=SparesProbeInventory(FloatEquipmentKind.Line);
            await GameEquipmentProbeSelect(pane,"equipment-line",lineId.ToString("D"));await GameEquipmentProbeSelect(pane,"equipment-reel","");
            await GameEquipmentProbeClick(pane,"equipment-move-line","line-move-candidate",true);
            await GameEquipmentProbeSelect(pane,"equipment-line",newLine.ToString("D"));await GameEquipmentProbeSelect(pane,"equipment-reel",reelId.ToString("D"));
            await GameEquipmentProbeClick(pane,"equipment-move-line","line-move-candidate",true);
            state=game.Snapshot.State;
            Require(state.Sequence==12&&state.Rig.LineId==newLine&&state.Rig.LineLengthMm==50000&&state.Equipment.Find(lineId).RemainingLineMm==50000-evidence.OutLineMm,"Repair refilled or discarded the original line.");
            checks.Add("actual equipment selectors reinstall purchased parts and keep the shorter original segment in inventory");

            await GameMenu(pane,lease,false);await GameCastAndWait(pane,lease);
            var recall=GameIntent("recall");recall["rigRevision"]=game.Snapshot.State.Rig.Revision.ToString(CultureInfo.InvariantCulture);
            GameCode(await GameWire(pane,lease,recall),"recall-candidate",true);
            state=game.Snapshot.State;
            Require(state.Sequence==14&&state.Session==null&&state.WalletMinor==11420&&state.BaitCount==10&&state.Stamina==96&&state.Creel.Count==0&&gameFlightEmissions==2,"Repaired rig cannot cast normally.");
            Require(state.Equipment.InitialLineMm==100000&&state.Equipment.RemainingLineMm+evidence.OutLineMm==100000&&state.Equipment.LostLineMm==evidence.OutLineMm,"Integer line conservation failed.");
            checks.Add("repaired rig casts and recalls while purchases actual integer line loss bait stamina and money remain conserved");
            await Wait(read,p=>Field(p,"snapshot")!=null&&(string)Field(p,"snapshot")["Sequence"]=="14");
            PrepareGameShutdown();Require(ExitCode==0&&gameShutdownCode=="no-active-session","Completed recovery did not shut down normally.");
            checks.Add("observer sees the final conserved profile and idle shutdown adds no recovery or additional loss");
            File.WriteAllText(Path.Combine(root,"webview-summary.json"),json.Serialize(new {passed=true,checks=checks.ToArray(),gameLoaded=true,
                lineBreakConnected=true,physicalOverload=true,naturalHookedTarget=true,controlledTerminalLoss=false,nativeInputTested=false,
                realDiskFailureTested=false,schemaVersion=state.SchemaVersion,sequence=state.Sequence,value=state.WalletMinor,
                baitCount=state.BaitCount,stamina=state.Stamina,gameFlightEmissions,gameLineBreakConfirmations,
                initialLineMm=state.Equipment.InitialLineMm,remainingLineMm=state.Equipment.RemainingLineMm,lostLineMm=state.Equipment.LostLineMm,
                breakStep=evidence.StepIndex,breakTensionN=evidence.TensionN,breakThresholdMilliN=GameLineBreakMilliN,
                equipmentInstances=state.Equipment.Instances.Count,knownWriteRetry=true,documentReloadTested=true}),Encoding.UTF8);
            Shutdown();
        }catch(Exception error){Fail(error);}
    }
}
