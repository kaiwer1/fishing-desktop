using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FishingDesktop.GameCore;
using Microsoft.Web.WebView2.Core;

sealed partial class DualWindowContext
{
    Dictionary<string,object> CampProbeMessage(ProbeWindow pane,string lease,string action,string document=null)
    {
        return new Dictionary<string,object>{{"kind","game-camp"},{"documentId",document??pane.Ticket.DocumentId},{"lease",lease},
            {"transportId",Guid.NewGuid().ToString("D")},{"action",action},
            {"inputSequence",checked(gameViews[pane].CampHighWater+1).ToString(CultureInfo.InvariantCulture)}};
    }
    async Task<Dictionary<string,object>> CampProbeReady(ProbeWindow pane)
    {
        return await Wait(pane,p=>Field(p,"campState")!=null&&Field(p,"campInputState")!=null&&
            (bool)Field(p,"campInputState")["ready"]&&Field(p,"campInputState")["inflight"]==null&&
            !(bool)Field(p,"campInputState")["awaitingState"]&&!(bool)Field(p,"campInputState")["faulted"]&&
            (string)Field(p,"campState")["stateSequence"]==game.Snapshot.State.Sequence.ToString(CultureInfo.InvariantCulture));
    }
    async Task<Dictionary<string,object>> CampProbeClick(ProbeWindow pane,string element,string code,bool committed)
    {
        var before=await CampProbeReady(pane);var old=Field(before,"lastReply");string previous=old==null?null:(string)old["transportId"];
        string id=json.Serialize(element);
        Require(await pane.View.CoreWebView2.ExecuteScriptAsync("Boolean(document.getElementById("+id+")&&!document.getElementById("+id+").disabled)")=="true","Disabled camp control: "+element);
        // Exactly one dispatch. A missing reply fails instead of retrying a
        // potentially successful reward claim.
        await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById("+id+").click()");
        var page=await Wait(pane,p=>Field(p,"lastReply")!=null&&(string)Field(p,"lastReply")["transportId"]!=previous);
        GameCode(page,code,committed);return await CampProbeReady(pane);
    }
    void CampProbeControlledReady()
    {
        Require(GameCampSelfTest&&GameSelfTest&&GameCampCurrent(),"Controlled camp time is private to the current self-test binding.");
        int steps=0;
        while(gameCamp.Snapshot.Phase=="working"&&steps<241){gameCamp.Advance(250);steps++;}
        Require(gameCamp.Snapshot.Phase=="ready"&&gameCamp.Snapshot.ElapsedMilliseconds==60000,"Controlled active time did not finish the work round.");
        Broadcast();
    }
    // This probe exercises real WebView intents and confirmations, with a short
    // real host-clock sample. Full work rounds use bounded, controlled active
    // time increments; it does not claim a real one-minute or native-input run.
    async void RunGameCampProbe()
    {
        try{
            Require(GameCampSelfTest,"Private camp self-test required.");
            var read=windows[false];var control=windows[true];await Ready(read);await Ready(control);
            var initial=game.Snapshot.State;
            Require(initial.Sequence==1&&initial.SchemaVersion==3&&initial.EquipmentOffers!=null&&initial.WalletMinor==0&&initial.BaitCount==0&&initial.Stamina==0&&
                initial.FoodCount==0&&initial.OwnedNet==null&&initial.Creel.Count==0&&initial.Session==null&&initial.Equipment.Instances.Count==6&&initial.Equipment.RemainingLineMm==50000,
                "Camp fixture did not begin with empty consumables and six owned items.");
            string lease=await Activate(control);await GameMenu(control,lease,true);await CampProbeReady(control);
            checks.Add("new private profile starts with zero money bait and stamina and keeps its six original equipment items");

            string unchanged=GameShopJournalDigest();
            var observed=await Wait(read,p=>Field(p,"campState")!=null&&(string)Field(p,"campState")["stateSequence"]=="1");
            Require(!(bool)Field(observed,"campState")["available"],"Observer received camp authority.");
            GameCode(await GameShopWire(read,CampProbeMessage(read,lease,"start")),"control-required",false);
            foreach(string key in new[]{"amount","time","rewardMinor","activeMilliseconds","policyVersion"}){
                var bad=CampProbeMessage(control,lease,"start");bad.Add(key,1);GameCode(await GameShopWire(control,bad),"invalid-intent",false);
            }
            Require(GameShopJournalDigest()==unchanged&&gameCamp.Snapshot.Phase=="idle","Observer or injected work values changed the journal.");
            checks.Add("observer and injected reward duration or policy fields cannot start or settle camp work");

            await CampProbeClick(control,"camp-start","camp-work-started",false);
            double fishingTime=activityClock.ActiveSeconds;await Task.Delay(200);AdvanceActivity();
            Require(gameCamp.Snapshot.Phase=="working"&&gameCamp.Snapshot.ElapsedMilliseconds>0&&gameCamp.Snapshot.ElapsedMilliseconds<60000&&
                activityClock.ActiveSeconds==fishingTime&&GameShopJournalDigest()==unchanged,"Short real camp clock did not advance independently of paused fishing.");
            checks.Add("a short real host-clock interval advances camp work while the open menu keeps fishing simulation paused");

            await GameMenu(control,lease,false);
            Require(gameCamp.Snapshot.Phase=="cancelled"&&gameCamp.Snapshot.ElapsedMilliseconds==0&&GameShopJournalDigest()==unchanged,"Closing the menu paid or retained unfinished work.");
            await GameMenu(control,lease,true);await CampProbeReady(control);
            checks.Add("closing the menu cancels unfinished time without any reward transaction");

            await CampProbeClick(control,"camp-start","camp-work-started",false);
            var page=await CampProbeReady(control);var previousRevoke=Field(page,"lastRevoke");
            string revokeTransport=previousRevoke==null?null:(string)previousRevoke["transportId"],oldLease=lease;
            await control.View.CoreWebView2.ExecuteScriptAsync("window.dispatchEvent(new Event('blur'))");
            await Wait(control,p=>p["lease"]==null&&Field(p,"lastRevoke")!=null&&(string)Field(p,"lastRevoke")["transportId"]!=revokeTransport&&gameViews[control].Lease==null);
            Require(gameCamp.Snapshot.Phase=="cancelled"&&gameCamp.Snapshot.ElapsedMilliseconds==0&&gameCampBinding==null&&GameShopJournalDigest()==unchanged,"Lost authority retained a work round.");
            GameCode(await GameShopWire(control,CampProbeMessage(control,oldLease,"claim")),"control-required",false);
            lease=await Activate(control);await CampProbeReady(control);
            checks.Add("page blur revokes the work binding and an old grant cannot claim its cancelled time");

            await CampProbeClick(control,"camp-start","camp-work-started",false);CampProbeControlledReady();
            await Wait(control,p=>Field(p,"campState")!=null&&(string)Field(p,"campState")["phase"]=="ready");
            Require(GameShopJournalDigest()==unchanged&&game.Snapshot.State.WalletMinor==0,"Reaching ready automatically paid a reward.");
            GameCode(await GameShopWire(control,ShopQuoteMessage(control,lease,"bait",1)),"camp-work-busy",false);
            Require(gameViews[control].Shop==null&&GameShopJournalDigest()==unchanged,"Another menu changed resources during pending work.");
            await control.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('camp').scrollIntoView()");
            using(var preview=File.Create(Path.Combine(root,"camp-panel.png")))await control.View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,preview);
            checks.Add("controlled active time reaches ready without payment and pending work blocks conflicting shop operations");

            ArmGameCampPrewriteFailure();await CampProbeClick(control,"camp-claim","camp-work-not-written",false);
            Require(gameCamp.Snapshot.Phase=="retry"&&GameShopJournalDigest()==unchanged&&game.Snapshot.State.WalletMinor==0,"Known prewrite failure paid or lost its explicit retry.");
            await CampProbeClick(control,"camp-claim","camp-work-candidate",true);
            Require(game.Snapshot.State.Sequence==2&&game.Snapshot.State.WalletMinor==800&&gameCamp.Snapshot.Phase=="confirmed","Explicit claim retry did not pay exactly 800 once.");
            string paid=GameShopJournalDigest();
            Require(await control.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('camp-claim').disabled")=="true","Confirmed reward can still be claimed.");
            await control.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('camp-claim').click()");
            Require(GameShopJournalDigest()==paid,"Disabled repeated claim changed the journal.");
            checks.Add("a known failed claim leaves zero balance and explicit retry confirms one reward with duplicate claim disabled");

            await GameShopClick(control,"quote-bait","shop-quoted",false);await GameShopClick(control,"shop-buy","purchase-candidate",true);
            await GameShopClick(control,"quote-food","shop-quoted",false);await GameShopClick(control,"shop-buy","purchase-candidate",true);
            await GameShopClick(control,"food-eat","food-candidate",true);
            var state=game.Snapshot.State;
            Require(state.Sequence==5&&state.WalletMinor==760&&state.BaitCount==5&&state.Stamina==20&&state.FoodCount==0,"Actual supply purchases did not restore bait and stamina.");
            checks.Add("actual shop buttons spend earned money on bait and food and eating restores twenty stamina");

            for(int round=0;round<3;round++){
                await CampProbeClick(control,"camp-start","camp-work-started",false);CampProbeControlledReady();
                await Wait(control,p=>Field(p,"campState")!=null&&(string)Field(p,"campState")["phase"]=="ready");
                await CampProbeClick(control,"camp-claim","camp-work-candidate",true);
            }
            state=game.Snapshot.State;
            int rewards=0;foreach(var receipt in state.Receipts)if(receipt.Outcome==FloatOutcome.CampWorkRewarded)rewards++;
            Require(state.Sequence==8&&state.WalletMinor==3160&&rewards==4&&state.BaitCount==5&&state.Stamina==20&&state.Equipment.Instances.Count==6,"Four work rounds changed unexpected resources.");
            checks.Add("three more controlled active rounds confirm four total rewards without creating supplies or equipment");

            await GameShopClick(control,"quote-net","shop-quoted",false);await GameShopClick(control,"shop-buy","purchase-candidate",true);
            await GameShopClick(control,"net-equip","equip-candidate",true);
            state=game.Snapshot.State;
            Require(state.Sequence==10&&state.WalletMinor==760&&state.OwnedNet!=null&&state.EquippedNetId==state.OwnedNet.Id,"Actual net purchase and equip failed.");
            checks.Add("actual shop purchase and equipment action buy and equip a landing net from the recovered balance");

            await GameMenu(control,lease,false);await GameCastAndWait(control,lease);
            GameCode(await GameWire(control,lease,GameIntent("recall")),"recall-candidate",true);
            state=game.Snapshot.State;
            Require(state.Sequence==12&&state.WalletMinor==760&&state.BaitCount==4&&state.Stamina==18&&state.Session==null&&
                state.Equipment.Instances.Count==6&&state.Equipment.InitialLineMm==50000&&state.Equipment.RemainingLineMm==50000&&state.Equipment.LostLineMm==0&&
                state.Rig.Revision==1&&gameFlightEmissions==1,"Recovered profile did not complete one paid cast and recall.");
            checks.Add("recovered resources support one actual cast and recall with one bait and two stamina spent");

            await Wait(read,p=>Field(p,"snapshot")!=null&&(string)Field(p,"snapshot")["Sequence"]=="12");
            PrepareGameShutdown();
            Require(game.Snapshot.ShutdownConfirmed&&gameShutdownCode=="no-active-session"&&game.Snapshot.State.Sequence==12&&owner.Snapshot().Sequence==0,"Idle camp shutdown wrote an extra transaction.");
            checks.Add("observer receives the completed profile and idle shutdown preserves the twelve confirmed transactions");
            File.WriteAllText(Path.Combine(root,"webview-summary.json"),json.Serialize(new{passed=true,checks=checks.ToArray(),gameLoaded=true,schemaVersion=3,
                sequence=state.Sequence,value=state.WalletMinor,campWorkConnected=true,campRewardConfirmations=rewards,controlledCampTime=true,controlledActiveTime=true,realFullMinute=false,
                realWebViewMessages=true,independentWindows=true,shortHostClockSample=true,
                baitCount=state.BaitCount,stamina=state.Stamina,foodCount=state.FoodCount,ownedNetEquipped=true,
                equipmentInstances=state.Equipment.Instances.Count,initialLineMm=state.Equipment.InitialLineMm,remainingLineMm=state.Equipment.RemainingLineMm,lostLineMm=state.Equipment.LostLineMm,
                gameFlightEmissions=gameFlightEmissions,castPreparationConnected=true,flatWaterFlightConnected=true,commerceConnected=true,
                controlledFishFacts=false,nativeInputTested=false,desktopLayer=false,personalSaveRead=false,realDiskFailureTested=false}),Encoding.UTF8);
            Shutdown();
        }catch(Exception error){FailCampProbe(error);}
    }
    async void FailCampProbe(Exception error)
    {
            // Keep a compact private state sample when a real-page wait fails.
            // This is diagnostic evidence only, never a retry of an action.
            try{
                ProbeWindow pane;if(windows.TryGetValue(true,out pane)&&Live(pane)&&pane.Ready)
                    File.WriteAllText(Path.Combine(root,"camp-failure-state.json"),await pane.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.state()"),Encoding.UTF8);
                File.WriteAllText(Path.Combine(root,"camp-failure-checks.json"),json.Serialize(checks.ToArray()),Encoding.UTF8);
            }catch(Exception){}
            Fail(error);
    }
}
