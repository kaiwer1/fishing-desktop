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
    Dictionary<string,object> SparesProbeMessage(ProbeWindow pane,string lease,string action,string value=null,string document=null)
    {
        var m=new Dictionary<string,object>{{"kind","game-spares"},{"documentId",document??pane.Ticket.DocumentId},{"lease",lease},
            {"transportId",Guid.NewGuid().ToString("D")},{"action",action}};
        if(value!=null)m.Add(action=="quote"?"sku":action=="retry"?"operationId":"offerId",value);
        return m;
    }
    async Task<Dictionary<string,object>> SparesProbeReady(ProbeWindow pane)
    {
        return await Wait(pane,s=>Field(s,"sparesInputState")!=null&&Field(s,"sparesState")!=null&&
            (bool)Field(s,"sparesInputState")["ready"]&&Field(s,"sparesInputState")["inflight"]==null&&
            !(bool)Field(s,"sparesInputState")["awaitingState"]&&
            (string)Field(s,"sparesState")["stateSequence"]==game.Snapshot.State.Sequence.ToString(CultureInfo.InvariantCulture));
    }
    async Task<Dictionary<string,object>> SparesProbeClick(ProbeWindow pane,string element,string code,bool committed)
    {
        var page=await SparesProbeReady(pane);var old=Field(page,"lastReply");string previous=old==null?null:(string)old["transportId"];
        string id=json.Serialize(element);
        Require(await pane.View.CoreWebView2.ExecuteScriptAsync("Boolean(document.getElementById("+id+")&&!document.getElementById("+id+").disabled)")=="true","Disabled spares control: "+element);
        await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById("+id+").click()");
        page=await Wait(pane,s=>Field(s,"lastReply")!=null&&(string)Field(s,"lastReply")["transportId"]!=previous);
        GameCode(page,code,committed);return await SparesProbeReady(pane);
    }
    static string SparesProbeOffer(Dictionary<string,object> page)
    {var quote=Field(Field(page,"sparesState"),"quote");Require(quote!=null,"Missing spares quote.");return (string)quote["offerId"];}
    Guid SparesProbeInventory(FloatEquipmentKind kind)
    {
        foreach(var item in game.Snapshot.State.Equipment.Instances)if(item.Kind==kind&&item.Location.Place==FloatEquipmentPlace.Inventory)return item.Id;
        throw new InvalidOperationException("Purchased spare missing from inventory.");
    }
    // This probe injects one explicit loss fact. It does not claim a physical
    // break trigger, real mouse operation, desktop attachment or disk failure.
    async void RunGameSparesProbe()
    {
        try{
            Require(GameSparesSelfTest,"Private spares mode required.");
            var read=windows[false];var control=windows[true];await Ready(read);await Ready(control);
            var initial=game.Snapshot.State;
            Require(initial.Sequence==1&&initial.EquipmentOffers!=null&&initial.WalletMinor==12000&&initial.Equipment.Instances.Count==6,"Explicit initial offer setup wrong.");
            Guid oldLine=initial.Rig.LineId.Value,reel=initial.Rig.ReelId.Value,oldHook=initial.Rig.HookId.Value,oldBobber=initial.Rig.BobberId.Value,oldSinker=initial.Rig.SinkerId.Value;
            string lease=await Activate(control);
            checks.Add("new private profile has one explicit catalogue transaction and six original items");

            await GameCastAndWait(control,lease);await GameWaitForWater(control);
            var s=game.Snapshot.State;var line=s.Equipment.Find(oldLine);
            var loss=new HostFloatTerminalLoss(s.ProfileId,s.BranchId,Guid.NewGuid(),Guid.NewGuid(),s.Session.Id,Guid.NewGuid(),oldLine,line.Revision,
                "mainline-terminal",25000,5000,s.Sequence,s.Rig.Revision,s.RulesVersion,s.Equipment.InventoryRevision,s.Equipment.RulesVersion,1);
            Require(game.ExecuteHost(loss).Committed,"Controlled terminal loss was not confirmed.");
            ReconcileGameFlight();ReconcileGameBite();ReconcileGameFight();Broadcast();
            s=game.Snapshot.State;
            Require(s.Sequence==3&&s.Session==null&&s.Rig.HookId==null&&s.Rig.BobberId==null&&s.Rig.SinkerId==null&&s.Rig.LineLengthMm==30000,"Loss did not preserve the shorter original line and missing terminal parts.");
            checks.Add("one controlled loss atomically ends the paid cast with 30 metres and three missing parts");
            string unchanged=GameShopJournalDigest();
            GameCode(await GameCastWire(control,lease,"begin",NextGameInput(control)),"missing-component",false);
            Require(GameShopJournalDigest()==unchanged&&game.Snapshot.State.BaitCount==11&&game.Snapshot.State.Stamina==98,"Missing gear consumed a second cast.");
            checks.Add("missing components prevent another cast without spending bait or stamina");

            await GameMenu(control,lease,true);var page=await SparesProbeReady(control);
            var observed=await Wait(read,p=>Field(p,"sparesState")!=null&&(string)Field(p,"sparesState")["stateSequence"]=="3");
            Require(!(bool)Field(observed,"sparesState")["available"]&&Field(Field(observed,"sparesState"),"quote")==null,"Observer received purchase authority.");
            GameCode(await GameShopWire(read,SparesProbeMessage(read,lease,"quote","hook-single")),"control-required",false);
            foreach(string key in new[]{"priceMinor","newItemId","quantity"}){
                var bad=SparesProbeMessage(control,lease,"quote","hook-single");bad.Add(key,"1");GameCode(await GameShopWire(control,bad),"invalid-intent",false);
            }
            Require(GameShopJournalDigest()==unchanged,"Invalid spares message wrote state.");
            checks.Add("observer and injected prices quantities or asset identities cannot write a purchase");

            page=await SparesProbeClick(control,"spares-quote-hook-single","spares-quoted",false);string offer=SparesProbeOffer(page);
            Require(GameShopJournalDigest()==unchanged,"Quotation charged the wallet.");
            ArmGameSparesPrewriteFailure();page=await SparesProbeClick(control,"spares-buy","spares-not-written",false);
            Require(GameShopJournalDigest()==unchanged&&Field(Field(page,"sparesState"),"retry")!=null,"Known failure changed the journal or lost retry.");
            GameCode(await GameShopWire(control,SparesProbeMessage(control,lease,"buy",offer)),"spares-retry-required",false);
            Require(GameShopJournalDigest()==unchanged,"Old buy bypassed explicit retry.");
            page=await SparesProbeClick(control,"spares-retry","equipment-purchase-candidate",true);
            Require(game.Snapshot.State.Sequence==4&&game.Snapshot.State.WalletMinor==11965&&game.Snapshot.State.Rig.HookId==null,"Retry did not buy exactly one unequipped hook.");
            string afterHook=GameShopJournalDigest();GameCode(await GameShopWire(control,SparesProbeMessage(control,lease,"buy",offer)),"spares-intent-not-current",false);
            Require(GameShopJournalDigest()==afterHook,"Consumed purchase charged twice.");
            checks.Add("known prewrite failure leaves the journal unchanged and explicit retry buys one hook");

            await GameShopClick(control,"quote-bait","shop-quoted",false);await GameShopClick(control,"shop-cancel","shop-cancelled",false);
            await GameEquipmentProbeReady(control);await SparesProbeReady(control);
            Require(GameShopJournalDigest()==afterHook,"Cross-menu quotation wrote a transaction.");
            checks.Add("spares confirmation immediately leaves supplies and equipment menus usable");

            foreach(string sku in new[]{"float-3g","sinker-2_5g","nylon-018-50m"}){
                await SparesProbeClick(control,"spares-quote-"+sku,"spares-quoted",false);
                await SparesProbeClick(control,"spares-buy","equipment-purchase-candidate",true);
            }
            s=game.Snapshot.State;Guid hook=SparesProbeInventory(FloatEquipmentKind.Hook),bobber=SparesProbeInventory(FloatEquipmentKind.Bobber),sinker=SparesProbeInventory(FloatEquipmentKind.Sinker),newLine=SparesProbeInventory(FloatEquipmentKind.Line);
            Require(s.Sequence==7&&s.WalletMinor==11420&&s.Equipment.Instances.Count==10&&s.Equipment.Find(oldLine).RemainingLineMm==30000&&s.Equipment.Find(newLine).RemainingLineMm==50000&&s.Rig.HookId==null,"Four spare purchases refilled or auto-equipped old gear.");
            checks.Add("four actual menu purchases cost 580 minor and keep the old 30 metre line separate from the new 50 metre line");
            await control.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('spares').scrollIntoView()");
            using(var preview=File.Create(Path.Combine(root,"game-spares-panel.png")))await control.View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,preview);

            page=await SparesProbeClick(control,"spares-quote-hook-single","spares-quoted",false);offer=SparesProbeOffer(page);
            string document=control.Ticket.DocumentId,oldLease=lease;unchanged=GameShopJournalDigest();Reload(control);
            await Wait(control,p=>(string)p["documentId"]!=document&&Field(p,"sparesState")!=null);
            GameCode(await GameShopWire(control,SparesProbeMessage(control,oldLease,"buy",offer,document)),"control-required",false);
            lease=await Activate(control);await SparesProbeReady(control);
            GameCode(await GameShopWire(control,SparesProbeMessage(control,lease,"buy",offer)),"spares-intent-not-current",false);
            Require(GameShopJournalDigest()==unchanged&&gameViews[control].Spares==null,"Reload revived an obsolete quote.");
            checks.Add("actual page reload cancels quotation and both old document and new grant reject its handle");

            foreach(var row in new[]{new KeyValuePair<string,Guid>("hook",hook),new KeyValuePair<string,Guid>("bobber",bobber),new KeyValuePair<string,Guid>("sinker",sinker)}){
                await GameEquipmentProbeSelect(control,"equipment-slot-"+row.Key,row.Value.ToString("D"));
                await GameEquipmentProbeClick(control,"equipment-apply-"+row.Key,"assembly-candidate",true);
            }
            Require(game.Snapshot.State.Sequence==10&&game.Snapshot.State.Rig.HookId==hook&&game.Snapshot.State.Rig.LineId==oldLine,"Terminal spare assembly changed the original line.");
            checks.Add("actual equipment selectors install only the three purchased terminal components");
            await GameEquipmentProbeSelect(control,"equipment-line",oldLine.ToString("D"));await GameEquipmentProbeSelect(control,"equipment-reel","");
            await GameEquipmentProbeClick(control,"equipment-move-line","line-move-candidate",true);
            await GameEquipmentProbeSelect(control,"equipment-line",newLine.ToString("D"));await GameEquipmentProbeSelect(control,"equipment-reel",reel.ToString("D"));
            await GameEquipmentProbeClick(control,"equipment-move-line","line-move-candidate",true);
            s=game.Snapshot.State;
            Require(s.Sequence==12&&s.Rig.LineId==newLine&&s.Equipment.Find(oldLine).Location.Place==FloatEquipmentPlace.Inventory&&s.Equipment.Find(oldLine).RemainingLineMm==30000,"Line exchange discarded or refilled the old segment.");
            checks.Add("actual line controls retain the 30 metre segment in inventory while installing the new 50 metre segment");

            await GameMenu(control,lease,false);await GameCastAndWait(control,lease);
            var recall=GameIntent("recall");recall["rigRevision"]=game.Snapshot.State.Rig.Revision.ToString(CultureInfo.InvariantCulture);
            GameCode(await GameWire(control,lease,recall),"recall-candidate",true);
            s=game.Snapshot.State;int losses=0;foreach(var receipt in s.Receipts)if(receipt.Outcome==FloatOutcome.TerminalLost)losses++;
            Require(s.Sequence==14&&s.WalletMinor==11420&&s.BaitCount==10&&s.Stamina==96&&s.FoodCount==0&&s.OwnedNet==null&&s.Session==null&&gameFlightEmissions==2&&losses==1,"Recast final resources incorrect.");
            Require(s.Equipment.InitialLineMm==100000&&s.Equipment.RemainingLineMm==80000&&s.Equipment.LostLineMm==20000&&s.Equipment.Instances.Count==10,"Final line budget does not conserve purchases and losses.");
            foreach(Guid id in new[]{oldHook,oldBobber,oldSinker})Require(s.Equipment.Find(id).Location.Place==FloatEquipmentPlace.Lost,"Lost terminal identity was resurrected.");
            checks.Add("repaired rig casts and recalls with exactly two total bait and stamina charges and conserved line loss");
            await GameMenu(control,lease,true);await GameEquipmentProbeReady(control);
            string beforeArchive=GameShopJournalDigest();long beforeRig=s.Rig.Revision;
            var injection=GameEquipmentProbeMessage(control,lease,"archive-lost");
            injection.Add("inputSequence","999");injection.Add("expectedSequence","14");injection.Add("itemIds",new[]{oldHook.ToString("D")});
            GameCode(await GameEquipmentProbeWire(control,injection),"invalid-intent",false);
            Require(GameShopJournalDigest()==beforeArchive,"Page-supplied archive identities wrote state.");
            checks.Add("archive intent rejects injected item identities without consuming the menu input edge");

            ArmGameEquipmentPrewriteFailure();
            await GameEquipmentProbeClick(control,"equipment-archive-lost","equipment-not-written",false);
            var frozenArchive=gameViews[control].Equipment.Command;
            Require(GameShopJournalDigest()==beforeArchive&&game.Snapshot.State.Equipment.Instances.Count==10,"Failed archive removed equipment.");
            await GameEquipmentProbeClick(control,"equipment-retry","equipment-archive-candidate",true);
            s=game.Snapshot.State;var archiveReceipt=s.Receipts[s.Receipts.Count-1];
            Require(s.Sequence==15&&s.Equipment.Instances.Count==7&&s.ArchivedEquipmentIds.Count==3&&s.ArchivedEquipmentLineMm==0&&s.Rig.Revision==beforeRig&&
                archiveReceipt.RequestId==frozenArchive.RequestId&&archiveReceipt.EventId==frozenArchive.EventId&&archiveReceipt.Fingerprint==frozenArchive.Fingerprint,
                "Archive retry changed the frozen command or current rig.");
            foreach(Guid id in new[]{oldHook,oldBobber,oldSinker})Require(s.Equipment.Find(id)==null&&s.ArchivedEquipmentIds.Contains(id),"Archived identity was forgotten.");
            Require(s.WalletMinor==11420&&s.Equipment.InitialLineMm==100000&&s.Equipment.RemainingLineMm==80000&&s.Equipment.LostLineMm==20000,"Archive refunded money or lost line history.");
            checks.Add("actual archive button and explicit retry preserve exact command identities loss history money and line totals");

            await SparesProbeClick(control,"spares-quote-hook-single","spares-quoted",false);
            await SparesProbeClick(control,"spares-buy","equipment-purchase-candidate",true);
            document=control.Ticket.DocumentId;Reload(control);
            await Wait(control,p=>(string)p["documentId"]!=document&&Field(p,"equipmentState")!=null);
            lease=await Activate(control);page=await GameEquipmentProbeReady(control);s=game.Snapshot.State;
            Require(s.Sequence==16&&s.WalletMinor==11385&&s.Equipment.Instances.Count==8&&s.ArchivedEquipmentIds.Count==3&&
                Convert.ToInt32(Field(page,"equipmentState")["archivedItemsCount"])==3&&(string)Field(page,"equipmentState")["archivedLineMm"]=="0",
                "Reload lost archived history or another purchase failed.");
            await control.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('equipment-archive-lost').scrollIntoView()");
            using(var preview=File.Create(Path.Combine(root,"game-archive-panel.png")))await control.View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,preview);
            checks.Add("another purchase and real page reload retain archived identity counts with no starter grants");
            PrepareGameShutdown();Require(game.Snapshot.ShutdownConfirmed&&gameShutdownCode=="no-active-session"&&game.Snapshot.State.Sequence==16,"Idle shutdown changed the completed cycle.");
            Require(owner.Snapshot().Sequence==0,"Spares test wrote the diagnostic counter.");
            checks.Add("idle shutdown confirms without another transaction or diagnostic counter write");
            File.WriteAllText(Path.Combine(root,"webview-summary.json"),json.Serialize(new{passed=true,checks=checks.ToArray(),gameLoaded=true,schemaVersion=3,
                value=s.WalletMinor,sequence=s.Sequence,sparesMenuConnected=true,controlledTerminalLoss=true,equipmentDamageImplemented=false,
                equipmentInstances=s.Equipment.Instances.Count,archivedEquipmentInstances=s.ArchivedEquipmentIds.Count,equipmentArchiveConnected=true,
                initialLineMm=s.Equipment.InitialLineMm+s.ArchivedEquipmentLineMm,remainingLineMm=s.Equipment.RemainingLineMm,lostLineMm=s.Equipment.LostLineMm+s.ArchivedEquipmentLineMm,
                baitCount=s.BaitCount,stamina=s.Stamina,foodCount=s.FoodCount,ownedNetEquipped=false,gameFlightEmissions=gameFlightEmissions,terminalLossConfirmations=losses,
                controlledFishFacts=false,castPreparationConnected=true,flatWaterFlightConnected=true,commerceConnected=true,equipmentMenuConnected=true,
                nativeInputTested=false,desktopLayer=false,personalSaveRead=false,realDiskFailureTested=false}),Encoding.UTF8);
            Shutdown();
        }catch(Exception error){Fail(error);}
    }
}
