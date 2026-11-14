using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    FloatSaveFaultPoint? gameFailureBoundaryFault;
    void CheckGameFailureBoundaryEquipment(FloatGameState paid,FloatGameState saved,FloatLineBreakEvidence evidence,Guid failureId,bool confirmed)
    {
        Require(paid.Equipment!=null&&saved.Equipment!=null&&paid.Equipment.Instances.Count==6&&saved.Equipment.Instances.Count==6&&
            paid.Equipment.InitialLineMm==50000&&saved.Equipment.InitialLineMm==50000,"Reopen removed or invented original equipment.");
        if(!confirmed){
            Require(Convert.ToBase64String(FloatEquipmentCodec.Encode(paid.Equipment))==Convert.ToBase64String(FloatEquipmentCodec.Encode(saved.Equipment))&&
                saved.Equipment.RemainingLineMm==50000&&saved.Equipment.LostLineMm==0&&saved.Rig.LineId==paid.Rig.LineId&&saved.Rig.LineLengthMm==50000&&
                saved.Rig.HookId==paid.Rig.HookId&&saved.Rig.BobberId==paid.Rig.BobberId&&saved.Rig.SinkerId==paid.Rig.SinkerId,
                "Unwritten break or interrupted recovery changed the original six items or full line.");
            return;
        }
        Require(evidence!=null&&evidence.LineId==paid.Rig.LineId&&evidence.LineRevision==paid.Equipment.Find(evidence.LineId).Revision&&
            evidence.TotalLineMm==50000&&evidence.CutDistanceFromReelMm==0&&evidence.OutLineMm>0&&evidence.OutLineMm<50000,
            "Confirmed loss lacks its original physical integer evidence.");
        long lost=evidence.OutLineMm-evidence.CutDistanceFromReelMm,remaining=50000-lost;
        var line=saved.Equipment.Find(evidence.LineId);var receipt=saved.Receipts[saved.Receipts.Count-1];var summary=receipt.EquipmentLoss;
        Require(line!=null&&line.RemainingLineMm==remaining&&line.Revision==evidence.LineRevision+1&&line.Location.Place==FloatEquipmentPlace.ReelSpool&&
            line.Location.OwnerId==paid.Rig.ReelId&&saved.Rig.LineId==evidence.LineId&&saved.Rig.LineLengthMm==remaining&&
            saved.Equipment.RemainingLineMm==remaining&&saved.Equipment.LostLineMm==lost&&saved.Equipment.RemainingLineMm+saved.Equipment.LostLineMm==50000&&
            saved.Rig.HookId==null&&saved.Rig.BobberId==null&&saved.Rig.SinkerId==null,"Confirmed break failed integer line conservation or left lost parts equipped.");
        Require(receipt.Outcome==FloatOutcome.TerminalLost&&receipt.EventId==failureId&&summary!=null&&summary.LineId==evidence.LineId&&
            summary.RemainingLineBeforeMm==50000&&summary.RemainingLineAfterMm==remaining&&summary.LostLineMm==lost&&summary.LostItemIds.Count==3,
            "Confirmed break receipt changed the frozen physical loss.");
        foreach(Guid id in new[]{paid.Rig.HookId.Value,paid.Rig.BobberId.Value,paid.Rig.SinkerId.Value}){
            var item=saved.Equipment.Find(id);Require(item!=null&&item.Location.Place==FloatEquipmentPlace.Lost&&item.Location.OwnerId==failureId&&
                summary.LostItemIds.Contains(id),"Original terminal component lost its saved loss owner.");
        }
    }
    async void RunGameFailureBoundaryProbe()
    {
        try{
            Require(GameSelfTest&&GameFailureCase!=null,"Failure boundary probe is private and explicit.");
            string mode=GameFailureCase,path=Path.Combine(root,"game.fltj");bool lineBreak=GameLineBreakMode;
            var pane=windows[true];await Ready(windows[false]);await Ready(pane);
            string lease=await Activate(pane);
            gameFailureFixture=!lineBreak;
            try{await GameCastAndWait(pane,lease);await GameWaitForWater(pane);await GameHookCurrent(pane,lease);}
            finally{gameFailureFixture=false;}
            var paid=game.Snapshot.State;var runtime=gameFight;
            byte[] paidBytes;
            using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
            using(var buffer=new MemoryStream()){input.CopyTo(buffer);paidBytes=buffer.ToArray();}
            Require(paid.Sequence==(lineBreak?2:1)&&paid.BaitCount==11&&paid.Stamina==98&&paid.Creel.Count==0&&
                (lineBreak?gameLineBreakRules!=null&&paid.EquipmentOffers!=null&&runtime.Snapshot.SlackM<.25:runtime.Snapshot.SlackM>1.9),"Private failure setup or original cast debit changed.");
            if(lineBreak)CheckGameFailureBoundaryEquipment(paid,paid,null,Guid.Empty,false);
            checks.Add(lineBreak?"real pages cast and naturally hook one target with the original six items and full fifty metre line":"real pages cast and hook one target; controlled slack conserves paid line and original bait and stamina cost");

            gameFailureBoundaryFault=mode=="known-close"?FloatSaveFaultPoint.BeforeWrite:mode=="after-flush"?FloatSaveFaultPoint.AfterFlush:FloatSaveFaultPoint.AfterPrefix;
            if(lineBreak){
                GameCode(await GameFightWire(pane,lease,"drag",10),"drag-updated",false);
                GameCode(await GameFightWire(pane,lease,"speed",10),"speed-updated",false);
                GameCode(await GameFightWire(pane,lease,"reel-start"),"reel-held",false);
            }
            await GameWaitForFight(pane,FloatFightPhase.FailurePending,7000);
            await Wait(pane,s=>Field(s,"fightState")!=null&&(string)Field(s,"fightState")["phase"]=="failure-pending");
            bool known=mode=="known-close";
            Require(gameFailureBoundaryFault==null&&gameFailureRetry==known&&game.Snapshot.Faulted==!known&&gameUnhookedConfirmations==0&&gameLineBreakConfirmations==0&&
                runtime.Snapshot.Reason==(known?"failure-not-written":"failure-save-unknown"),"Failure write outcome was falsely confirmed or retryable.");
            var evidence=gameFailureTicket.LineBreakEvidence;Guid failureId=gameFailureTicket.FailureId;
            if(lineBreak){
                Require(gameFailureCommand is HostFloatTerminalLoss&&evidence!=null&&gameFailureTicket.Reason=="line-overloaded"&&
                    evidence.StepIndex==runtime.Snapshot.StepIndex&&evidence.TensionN==runtime.Snapshot.TensionN&&evidence.TensionN>GameLineBreakMilliN/1000.0&&
                    evidence.OutLineMm==(long)Math.Floor(runtime.Snapshot.OutLineM*1000)&&runtime.Snapshot.LineBreakExposureSteps==GameLineBreakSteps,
                    "Write injection did not follow actual validated line overload.");
                if(known)CheckGameFailureBoundaryEquipment(paid,game.Snapshot.State,evidence,failureId,false);
                else Require(game.Snapshot.State==null,"Unknown write exposed an authoritative playable state.");
            }
            string digest=GameShopJournalDigest();long step=runtime.Snapshot.StepIndex;
            await Task.Delay(250);
            Require(GameShopJournalDigest()==digest&&runtime.Snapshot.StepIndex==step,"Pending failure retried or advanced by itself.");
            checks.Add("injected write boundary separates known no-write from unknown outcome and freezes simulation without automatic retry");

            string oldDocument=pane.Ticket.DocumentId,oldLease=lease;
            if(known){
                Reload(pane);await Wait(pane,s=>(string)s["documentId"]!=oldDocument&&Field(s,"fightState")!=null);
                Require(Object.ReferenceEquals(runtime,gameFight)&&gameFailureRetry,"Reload replaced the unresolved failure.");
            }
            GameCode(await GameStrikeWire(pane,GameFightMessage(pane,oldLease,"retry-escape",known?1:gameViews[pane].FightHighWater+1,null,oldDocument)),known?"control-required":"domain-paused",false);
            if(!known)Require(await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('retry-escape').disabled && document.getElementById('reel-hold').disabled")=="true","Unknown save exposed a retry or reel action.");
            Require(GameShopJournalDigest()==digest,"Reload or stale authority changed unresolved bytes.");
            checks.Add(known?"real reload preserves pending failure and denies the old document":"faulted current document denies its revoked authority and leaves retry and reel controls disabled");

            PrepareGameShutdown();PrepareGameShutdown();
            Require(ExitCode==4&&gameShutdownCode=="unconfirmed-failure-no-settlement"&&!game.Snapshot.ShutdownConfirmed&&GameShopJournalDigest()==digest,"Unconfirmed exit appended a recovery or reported success.");
            checks.Add("repeated shutdown preparation refuses settlement and retains original bytes with an explicit unconfirmed exit");

            // Close the writer, then verify startup on a NEW private copy. This
            // is owner/file reopen coverage, not a second full host process.
            StopActivityPolicy();
            foreach(var view in new System.Collections.Generic.List<ProbeWindow>(windows.Values)){Retire(view);view.Close();view.Dispose();}
            DisposeGame();
            // The 12-byte prefix may still be in FileStream's write buffer
            // until Dispose. Freeze evidence only AFTER closing the writer.
            if(mode=="after-prefix"){
                byte[] closed=File.ReadAllBytes(path);Require(closed.Length==paidBytes.Length+12,"Partial frame has an unexpected final length.");
                for(int i=0;i<paidBytes.Length;i++)Require(closed[i]==paidBytes[i],"Fault changed the previously confirmed prefix.");
                using(var sha=System.Security.Cryptography.SHA256.Create())digest=Convert.ToBase64String(sha.ComputeHash(closed));
            }
            string copy=Path.Combine(root,"reopen-check.fltj");File.Copy(path,copy,false);
            string reopened;
            if(mode=="after-prefix"){
                bool inspectRejected=false,openRejected=false;
                try{FloatJournalArchive.Inspect(copy);}catch(InvalidDataException){inspectRejected=true;}
                try{using(var other=FloatGameOwner.OpenExisting(copy)) {}}catch(InvalidDataException){openRejected=true;}
                Require(inspectRejected&&openRejected,"Incomplete frame was repaired or opened as playable state.");
                reopened="corrupt-rejected";
            }else{
                var saved=FloatJournalArchive.Inspect(copy).State;
                var terminal=lineBreak?FloatOutcome.TerminalLost:FloatOutcome.Unhooked;
                Require(saved.Sequence==paid.Sequence+(known?0:1)&&saved.BaitCount==paid.BaitCount&&saved.Stamina==paid.Stamina&&saved.WalletMinor==paid.WalletMinor&&saved.Creel.Count==0,"Confirmed bytes changed original resources.");
                Require(known?saved.Session!=null:saved.Session==null&&saved.Receipts[saved.Receipts.Count-1].Outcome==terminal,"Persisted outcome did not match the injected boundary.");
                if(lineBreak)CheckGameFailureBoundaryEquipment(paid,saved,evidence,failureId,!known);
                using(var other=FloatGameOwner.OpenExisting(copy)){
                    var restored=other.Snapshot.State;
                    Require(restored.Sequence==paid.Sequence+1&&restored.Session==null&&restored.BaitCount==11&&restored.Stamina==98&&restored.WalletMinor==12000&&restored.Creel.Count==0,"Owner startup refunded or duplicated resources.");
                    Require(restored.Receipts[restored.Receipts.Count-1].Outcome==(known?FloatOutcome.Recovered:terminal),"Owner startup changed the confirmed terminal outcome.");
                    if(lineBreak)CheckGameFailureBoundaryEquipment(paid,restored,evidence,failureId,!known);
                }
                reopened=known?"interrupted-recovery":lineBreak?"confirmed-line-break":"confirmed-unhook";
            }
            checks.Add(lineBreak?"owner reopen on a private copy preserves full equipment after unwritten recovery, retains confirmed integer line and terminal loss, or rejects partial frames":"owner reopen on a separate private copy recovers only unwritten ordinary casts, preserves confirmed unhook, or rejects partial frames");
            using(var sha=System.Security.Cryptography.SHA256.Create()){
                string retained=Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
                Require(retained==digest,"Reopen check changed the original evidence file.");
                if(mode=="after-prefix")Require(Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(copy)))==digest,"Rejected partial copy was modified.");
            }
            checks.Add("original failed-session file stays byte-identical during reopen verification; no fallback or automatic repair");
            File.WriteAllText(Path.Combine(root,"webview-summary.json"),json.Serialize(new{passed=true,checks=checks,gameLoaded=true,failureBoundary=mode,expectedExitCode=4,reopened=reopened,
                sourcePreserved=true,privateCopyReopen=true,documentReloadTested=known,secondHostProcessTested=false,realDiskFailureTested=false,
                lineBreakConnected=lineBreak,physicalOverload=lineBreak,naturalHookedTarget=lineBreak,controlledSlackSetup=!lineBreak,value=paid.WalletMinor,
                sequence=known?(long?)paid.Sequence:mode=="after-flush"?(long?)(paid.Sequence+1):null}),Encoding.UTF8);
            Shutdown();
        }catch(Exception error){Fail(error);}
    }
}
