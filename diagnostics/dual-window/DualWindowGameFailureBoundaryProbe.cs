using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    FloatSaveFaultPoint? gameFailureBoundaryFault;
    async void RunGameFailureBoundaryProbe()
    {
        try{
            Require(GameSelfTest&&GameFailureCase!=null,"Failure boundary probe is private and explicit.");
            string mode=GameFailureCase,path=Path.Combine(root,"game.fltj");
            var pane=windows[true];await Ready(windows[false]);await Ready(pane);
            string lease=await Activate(pane);
            gameFailureFixture=true;
            try{await GameCastAndWait(pane,lease);await GameWaitForWater(pane);await GameHookCurrent(pane,lease);}
            finally{gameFailureFixture=false;}
            var paid=game.Snapshot.State;var runtime=gameFight;
            byte[] paidBytes;
            using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
            using(var buffer=new MemoryStream()){input.CopyTo(buffer);paidBytes=buffer.ToArray();}
            Require(paid.Sequence==1&&paid.BaitCount==11&&paid.Stamina==98&&paid.Creel.Count==0&&runtime.Snapshot.SlackM>1.9,"Private failure setup or original cast debit changed.");
            checks.Add("real pages cast and hook one target; controlled slack conserves paid line and original bait and stamina cost");

            gameFailureBoundaryFault=mode=="known-close"?FloatSaveFaultPoint.BeforeWrite:mode=="after-flush"?FloatSaveFaultPoint.AfterFlush:FloatSaveFaultPoint.AfterPrefix;
            await GameWaitForFight(pane,FloatFightPhase.FailurePending,7000);
            await Wait(pane,s=>Field(s,"fightState")!=null&&(string)Field(s,"fightState")["phase"]=="failure-pending");
            bool known=mode=="known-close";
            Require(gameFailureBoundaryFault==null&&gameFailureRetry==known&&game.Snapshot.Faulted==!known&&gameUnhookedConfirmations==0&&
                runtime.Snapshot.Reason==(known?"failure-not-written":"failure-save-unknown"),"Failure write outcome was falsely confirmed or retryable.");
            string digest=GameShopJournalDigest();long step=runtime.Snapshot.StepIndex;
            await Task.Delay(250);
            Require(GameShopJournalDigest()==digest&&runtime.Snapshot.StepIndex==step,"Pending failure retried or advanced by itself.");
            checks.Add("injected write boundary separates known no-write from unknown outcome and freezes simulation without automatic retry");

            string oldDocument=pane.Ticket.DocumentId,oldLease=lease;
            if(known){
                Reload(pane);await Wait(pane,s=>(string)s["documentId"]!=oldDocument&&Field(s,"fightState")!=null);
                Require(Object.ReferenceEquals(runtime,gameFight)&&gameFailureRetry,"Reload replaced the unresolved failure.");
            }
            GameCode(await GameStrikeWire(pane,GameFightMessage(pane,oldLease,"retry-escape",1,null,oldDocument)),known?"control-required":"domain-paused",false);
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
                Require(saved.Sequence==(known?1:2)&&saved.BaitCount==paid.BaitCount&&saved.Stamina==paid.Stamina&&saved.WalletMinor==paid.WalletMinor&&saved.Creel.Count==0,"Confirmed bytes changed original resources.");
                Require(known?saved.Session!=null:saved.Session==null&&saved.Receipts[saved.Receipts.Count-1].Outcome==FloatOutcome.Unhooked,"Persisted outcome did not match the injected boundary.");
                using(var other=FloatGameOwner.OpenExisting(copy)){
                    var restored=other.Snapshot.State;
                    Require(restored.Sequence==2&&restored.Session==null&&restored.BaitCount==11&&restored.Stamina==98&&restored.WalletMinor==12000&&restored.Creel.Count==0,"Owner startup refunded or duplicated resources.");
                    Require(restored.Receipts[1].Outcome==(known?FloatOutcome.Recovered:FloatOutcome.Unhooked),"Owner startup changed the confirmed terminal outcome.");
                }
                reopened=known?"interrupted-recovery":"confirmed-unhook";
            }
            checks.Add("owner reopen on a separate private copy recovers only unwritten ordinary casts, preserves confirmed unhook, or rejects partial frames");
            using(var sha=System.Security.Cryptography.SHA256.Create()){
                string retained=Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
                Require(retained==digest,"Reopen check changed the original evidence file.");
                if(mode=="after-prefix")Require(Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(copy)))==digest,"Rejected partial copy was modified.");
            }
            checks.Add("original failed-session file stays byte-identical during reopen verification; no fallback or automatic repair");
            File.WriteAllText(Path.Combine(root,"webview-summary.json"),json.Serialize(new{passed=true,checks=checks,gameLoaded=true,failureBoundary=mode,expectedExitCode=4,reopened=reopened,
                sourcePreserved=true,privateCopyReopen=true,documentReloadTested=known,secondHostProcessTested=false,realDiskFailureTested=false,controlledSlackSetup=true,value=12000,sequence=known?(long?)1:mode=="after-flush"?(long?)2:null}),Encoding.UTF8);
            Shutdown();
        }catch(Exception error){Fail(error);}
    }
}
