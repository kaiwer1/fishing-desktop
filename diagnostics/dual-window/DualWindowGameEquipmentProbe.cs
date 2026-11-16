using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using FishingDesktop.GameCore;
using Microsoft.Web.WebView2.Core;

sealed partial class DualWindowContext
{
    async Task BagProbeKey(ProbeWindow pane,string key,string code,int vk,int modifiers=0)
    {
        foreach(bool down in new[]{true,false})
            await pane.View.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",json.Serialize(new{
                type=down?"rawKeyDown":"keyUp",key=key,code=code,windowsVirtualKeyCode=vk,modifiers=modifiers}));
    }

    async Task BagProbeClick(ProbeWindow pane,string selector)
    {
        var point=await SceneProbeScript(pane,@"(()=>{
            const n=document.querySelector("+json.Serialize(selector)+@");if(!n)return null;
            const b=n.closest('button')||n;b.scrollIntoView({block:'nearest',inline:'nearest',behavior:'instant'});
            const r=b.getBoundingClientRect(),x=Math.round(r.left+r.width/2),y=Math.round(r.top+r.height/2);
            return !b.disabled&&b.contains(document.elementFromPoint(x,y))?{x,y}:null;
        })()");
        await SceneProbeMouse(pane,point,true);await SceneProbeMouse(pane,point,false);
    }

    async Task<object> GamePlayerBag(ProbeWindow pane,FloatFishingWater water)
    {
        string digest=GameShopJournalDigest();var state=game.Snapshot.State;
        var originalSize=pane.ClientSize;
        int toolbarHeight=originalSize.Height-pane.View.ClientSize.Height;
        pane.ClientSize=new System.Drawing.Size(1280,720+toolbarHeight);
        await pane.View.CoreWebView2.ExecuteScriptAsync("document.activeElement?.blur()");
        await BagProbeKey(pane,"i","KeyI",73);
        await Wait(pane,p=>Field(p,"snapshot")!=null&&(bool)Field(p,"snapshot")["MenuOpen"]&&Field(p,"equipmentState")!=null);
        string samples=@"(()=>{
            const detail=document.querySelector('#bag-detail'),cards=[...document.querySelectorAll('#bag-items [data-bag-item]')];
            const panel=document.getElementById('camp-panels'),facts=[...detail.querySelectorAll('dd')].map(n=>n.textContent);
            return {gear:document.body.dataset.campPage==='gear',count:cards.length,art:cards.filter(c=>c.querySelector('svg[data-tackle]')).length,
              selected:cards.filter(c=>c.getAttribute('aria-pressed')==='true').length,title:detail.querySelector('h3')?.textContent||'',
              countText:document.getElementById('bag-count').textContent,empty:!document.getElementById('bag-empty').hidden,
              detailHidden:detail.hidden,facts:facts.join('/'),width:innerWidth,height:innerHeight,
              fits:panel.scrollWidth<=panel.clientWidth+1&&document.documentElement.scrollWidth<=innerWidth+1};
        })()";
        var normal=await SceneProbeScript(pane,samples);
        Require((bool)normal["gear"]&&Number(normal,"count")==6&&Number(normal,"art")==6&&Number(normal,"selected")==1&&
            (bool)normal["fits"]&&!((bool)normal["detailHidden"]),"I did not open the six illustrated starting items.");
        await BagProbeClick(pane,"#bag-items svg[data-tackle=line]");
        var selected=await SceneProbeScript(pane,samples);
        Require(((string)selected["title"]).Contains("尼龙线")&&((string)selected["facts"]).Contains("50 米")&&
            ((string)selected["facts"]).Contains("0.18 毫米")&&((string)selected["facts"]).Contains("线杯"),
            "Line card did not show the installed spool, diameter and saved 50 m of line.");
        await WardrobeProbePhoto(pane,"bag-landscape.png");
        await BagProbeClick(pane,"#bag-types [data-kind=rod]");
        var rods=await SceneProbeScript(pane,samples);
        Require(Number(rods,"count")==1&&((string)rods["title"]).Contains("浮漂竿"),"Rod filter did not select only the owned rod.");
        await BagProbeClick(pane,"#bag-types [data-kind=all]");
        await BagProbeClick(pane,"#bag-search");
        await pane.View.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.insertText",json.Serialize(new{text="尼龙"}));
        var line=await SceneProbeScript(pane,samples);
        Require(Number(line,"count")==1&&(string)line["title"]==(string)selected["title"],"Searching for nylon did not return the same installed line.");
        await pane.View.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.insertText",json.Serialize(new{text="无匹配"}));
        var empty=await SceneProbeScript(pane,samples);
        Require(Number(empty,"count")==0&&(bool)empty["empty"]&&(bool)empty["detailHidden"],"An empty search left a stale equipment detail.");
        await BagProbeKey(pane,"a","KeyA",65,2);await BagProbeKey(pane,"Backspace","Backspace",8);
        Require(Number(await SceneProbeScript(pane,samples),"count")==6,"Clearing the query did not restore all owned plates.");
        pane.ClientSize=new System.Drawing.Size(812,375+toolbarHeight);
        await Wait(pane,p=>Number(SceneProbeRenderer(p),"width")>0);
        await BagProbeClick(pane,"#bag-items svg[data-tackle=reel]");
        var compact=await SceneProbeScript(pane,samples);
        Require(Number(compact,"height")<Number(normal,"height")&&Number(compact,"width")>Number(compact,"height")&&
            (bool)compact["fits"]&&Number(compact,"art")==6&&((string)compact["title"]).Contains("纺车轮"),
            "The short landscape view overflowed or the selected reel was inaccessible.");
        await WardrobeProbePhoto(pane,"bag-short-landscape.png");
        pane.ClientSize=originalSize;await SceneProbeButton(pane,"menu");await Wait(pane,p=>PlayerProbeDrawn(p,water));
        Require(state==game.Snapshot.State&&digest==GameShopJournalDigest(),"Browsing the backpack wrote an equipment or economy transaction.");
        return new {ownedCount=6,filterVerified=true,searchVerified=true,lineOnSpool=true,journalUnchanged=true,
            landscape=new {width=Number(normal,"width"),height=Number(normal,"height")},shortLandscape=new {width=Number(compact,"width"),height=Number(compact,"height")}};
    }

    Dictionary<string,object> GameEquipmentProbeMessage(ProbeWindow pane,string lease,string action,string document=null)
    {
        return new Dictionary<string,object>{{"kind","game-equipment"},{"documentId",document??pane.Ticket.DocumentId},{"lease",lease},
            {"transportId",Guid.NewGuid().ToString("D")},{"action",action}};
    }
    Dictionary<string,object> GameEquipmentProbeReplace(ProbeWindow pane,string lease,string slot,Guid? item,long input,long expected,string document=null)
    {
        var message=GameEquipmentProbeMessage(pane,lease,"replace-slot",document);
        message.Add("slot",slot);message.Add("itemId",item.HasValue?item.Value.ToString("D"):null);
        message.Add("inputSequence",input.ToString(CultureInfo.InvariantCulture));message.Add("expectedSequence",expected.ToString(CultureInfo.InvariantCulture));return message;
    }
    async Task<Dictionary<string,object>> GameEquipmentProbeWire(ProbeWindow pane,Dictionary<string,object> message)
    {
        await pane.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.sendRaw("+json.Serialize(message)+")");
        return await Wait(pane,s=>Field(s,"lastReply")!=null&&(string)Field(s,"lastReply")["transportId"]==(string)message["transportId"]);
    }
    async Task<Dictionary<string,object>> GameEquipmentProbeReady(ProbeWindow pane)
    {
        return await Wait(pane,s=>Field(s,"equipmentState")!=null&&Field(s,"equipmentInputState")!=null&&
            (bool)Field(s,"equipmentInputState")["ready"]&&Field(s,"equipmentInputState")["inflight"]==null&&
            !(bool)Field(s,"equipmentInputState")["awaitingState"]&&!(bool)Field(s,"equipmentInputState")["faulted"]&&
            (string)Field(s,"equipmentState")["stateSequence"]==game.Snapshot.State.Sequence.ToString(CultureInfo.InvariantCulture));
    }
    async Task GameEquipmentProbeSelect(ProbeWindow pane,string element,string value)
    {
        await GameEquipmentProbeReady(pane);
        string selected=await pane.View.CoreWebView2.ExecuteScriptAsync("(()=>{const e=document.getElementById("+json.Serialize(element)+");"+
            "if(!e||e.disabled)return false;e.value="+json.Serialize(value)+";e.dispatchEvent(new Event('change',{bubbles:true}));return e.value==="+json.Serialize(value)+";})()");
        Require(selected=="true","Equipment selection was unavailable: "+element);
    }
    async Task<Dictionary<string,object>> GameEquipmentProbeClick(ProbeWindow pane,string element,string code,bool committed)
    {
        var before=await GameEquipmentProbeReady(pane);var oldReply=Field(before,"lastReply");string previous=oldReply==null?null:(string)oldReply["transportId"];
        string id=json.Serialize(element);
        Require(await pane.View.CoreWebView2.ExecuteScriptAsync("Boolean(document.getElementById("+id+")&&!document.getElementById("+id+").disabled)")=="true","Equipment control is disabled: "+element);
        // Exactly one click. A missing acknowledgement fails this probe; it
        // must never retry a possibly successful inventory transaction.
        await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById("+id+").click()");
        var page=await Wait(pane,s=>Field(s,"lastReply")!=null&&(string)Field(s,"lastReply")["transportId"]!=previous);
        GameCode(page,code,committed);return await GameEquipmentProbeReady(pane);
    }
    static IList GameEquipmentProbeItems(Dictionary<string,object> page)
    {
        var state=Field(page,"equipmentState");var items=state==null?null:state["items"] as IList;
        Require(items!=null,"Expected bounded equipment projection.");return items;
    }
    static Dictionary<string,object> GameEquipmentProbeItem(Dictionary<string,object> page,Guid id)
    {
        foreach(object row in GameEquipmentProbeItems(page)){
            var item=row as Dictionary<string,object>;
            if(item!=null&&(string)item["id"]==id.ToString("D"))return item;
        }
        throw new InvalidOperationException("Original equipment identity missing from page.");
    }

    // Run once after the confirmed escape, with no active session. This uses
    // the real page selects/buttons for all six saved edits. Expected delta:
    // +6 transactions; same six instances and 50,000 mm; no cast/resource cost.
    // The controlled pre-write error is retried only by the explicit button.
    async Task<string> GameEquipmentProbe(ProbeWindow read,ProbeWindow pane,string lease)
    {
        Require(GameSelfTest,"Equipment probe requires the private self-test fixture.");
        var start=game.Snapshot.State;var original=start.Equipment;
        Require(start.SchemaVersion==3&&start.LegacyRig==null&&start.Session==null&&original!=null&&original.Instances.Count==6&&
            start.Rig.HookId.HasValue&&start.Rig.ReelId.HasValue&&start.Rig.LineId.HasValue&&start.Rig.LineLengthMm==50000&&
            start.OwnedNet!=null&&start.EquippedNetId==start.OwnedNet.Id,"Equipment probe requires the complete original six-item rig and equipped owned net.");
        long sequence=start.Sequence;int flights=gameFlightEmissions;Guid hook=start.Rig.HookId.Value,reel=start.Rig.ReelId.Value,line=start.Rig.LineId.Value;
        await GameMenu(pane,lease,true);var page=await GameEquipmentProbeReady(pane);
        var observer=await Wait(read,s=>Field(s,"equipmentState")!=null&&(string)Field(s,"equipmentState")["stateSequence"]==sequence.ToString(CultureInfo.InvariantCulture));
        Require(GameEquipmentProbeItems(page).Count==6&&GameEquipmentProbeItems(observer).Count==6&&
            Number(GameEquipmentProbeItem(page,line),"remainingLineMm")==50000&&!(bool)Field(observer,"equipmentState")["available"],"Page did not project six actual instances and 50 m.");
        Require(await pane.View.CoreWebView2.ExecuteScriptAsync("!document.getElementById('equipment').hidden && document.querySelectorAll('#equipment-slots article').length===5 && document.getElementById('equipment-line-status').textContent.includes('50 米')")=="true","Actual equipment panel is missing its slots or line length.");
        checks.Add("real equipment menu projects six owned instances and 50 metres while the observer remains unable to edit");

        string unchanged=GameShopJournalDigest();
        GameCode(await GameEquipmentProbeWire(read,GameEquipmentProbeReplace(read,lease,"hook",null,1,sequence)),"control-required",false);
        var malformed=GameEquipmentProbeReplace(pane,lease,"hook",null,1,sequence);malformed.Add("price",0);
        GameCode(await GameEquipmentProbeWire(pane,malformed),"invalid-intent",false);
        malformed=GameEquipmentProbeReplace(pane,lease,"hook",null,1,sequence);malformed.Add("newAssetId",Guid.NewGuid().ToString("D"));
        GameCode(await GameEquipmentProbeWire(pane,malformed),"invalid-intent",false);
        malformed=GameEquipmentProbeReplace(pane,lease,"line",null,1,sequence);
        GameCode(await GameEquipmentProbeWire(pane,malformed),"invalid-intent",false);
        Require(GameShopJournalDigest()==unchanged&&game.Snapshot.State.Sequence==sequence&&gameViews[pane].EquipmentHighWater==0,"Unauthorized or malformed intent changed state or consumed a valid input edge.");
        checks.Add("observer price injection unknown asset fields and invalid slot names are rejected before equipment writes or input consumption");

        await GameEquipmentProbeSelect(pane,"equipment-slot-hook","");
        page=await GameEquipmentProbeClick(pane,"equipment-apply-hook","assembly-candidate",true);
        Require(game.Snapshot.State.Sequence==sequence+1&&game.Snapshot.State.Rig.HookId==null&&
            game.Snapshot.State.Equipment.Find(hook).Location.Place==FloatEquipmentPlace.Inventory&&
            (string)GameEquipmentProbeItem(page,hook)["location"]=="inventory","Actual hook detach did not preserve the original item in inventory.");
        await GameMenu(pane,lease,false);unchanged=GameShopJournalDigest();
        GameCode(await GameCastWire(pane,lease,"begin",NextGameInput(pane)),"missing-component",false);
        Require(GameShopJournalDigest()==unchanged&&game.Snapshot.State.BaitCount==start.BaitCount&&game.Snapshot.State.Stamina==start.Stamina&&
            game.Snapshot.State.Session==null&&gameFlightEmissions==flights,"Missing hook began a cast or spent resources.");
        checks.Add("actual hook select and apply save one detach; closing menu and attempting cast with the missing hook spends nothing");

        await GameMenu(pane,lease,true);await GameEquipmentProbeReady(pane);
        string oldDocument=pane.Ticket.DocumentId,oldLease=lease;
        Reload(pane);await Wait(pane,s=>(string)s["documentId"]!=oldDocument&&Field(s,"equipmentState")!=null&&Field(s,"snapshot")!=null);
        Require(game.Snapshot.State.Rig.HookId==null&&game.Snapshot.State.Sequence==sequence+1&&GameShopJournalDigest()==unchanged,"Reload reconstructed a complete rig or changed the journal.");
        GameCode(await GameEquipmentProbeWire(pane,GameEquipmentProbeReplace(pane,oldLease,"hook",hook,2,sequence+1,oldDocument)),"control-required",false);
        lease=await Activate(pane);await GameMenu(pane,lease,true);page=await GameEquipmentProbeReady(pane);
        Require((string)GameEquipmentProbeItem(page,hook)["location"]=="inventory"&&
            await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('equipment-current-hook').textContent==='尚未装配' && document.getElementById('equipment-readiness').textContent.includes('缺少部件')")=="true","Reloaded menu hid the missing hook state.");
        checks.Add("actual WebView reload retains the missing hook and inventory identity while the old document and lease cannot restore it");

        await GameEquipmentProbeSelect(pane,"equipment-slot-hook",hook.ToString("D"));
        ArmGameEquipmentPrewriteFailure();page=await GameEquipmentProbeClick(pane,"equipment-apply-hook","equipment-not-written",false);
        var pending=gameViews[pane].Equipment;var retry=Field(Field(page,"equipmentState"),"retry");
        Require(pending!=null&&pending.Retry&&retry!=null&&(string)retry["operationId"]==pending.Id&&!gameEquipmentPrewriteFaultArmed&&
            game.Snapshot.State.Rig.HookId==null&&GameShopJournalDigest()==unchanged,"Known pre-write failure lost retry identity or saved an edit.");
        string operation=pending.Id;var exactCommand=pending.Command;
        var replacement=GameEquipmentProbeReplace(pane,lease,"reel",null,checked(gameViews[pane].EquipmentHighWater+1),sequence+1);
        GameCode(await GameEquipmentProbeWire(pane,replacement),"equipment-retry-required",false);
        Require(Object.ReferenceEquals(gameViews[pane].Equipment,pending)&&Object.ReferenceEquals(gameViews[pane].Equipment.Command,exactCommand)&&GameShopJournalDigest()==unchanged,"New edit replaced a known-not-written retry.");
        page=await GameEquipmentProbeClick(pane,"equipment-retry","assembly-candidate",true);
        var restored=game.Snapshot.State;var receipt=restored.Receipts[restored.Receipts.Count-1];
        Require(restored.Sequence==sequence+2&&restored.Rig.HookId==hook&&receipt.RequestId==exactCommand.RequestId&&receipt.EventId==exactCommand.EventId&&gameViews[pane].Equipment==null,"Explicit retry did not confirm the original command exactly once.");
        unchanged=GameShopJournalDigest();var completed=GameEquipmentProbeMessage(pane,lease,"retry");completed.Add("operationId",operation);
        GameCode(await GameEquipmentProbeWire(pane,completed),"equipment-intent-not-current",false);
        Require(GameShopJournalDigest()==unchanged,"Completed retry handle performed a second write.");
        // The rejected fresh raw edge consumed the host high-water mark. A
        // new grant clears that mark; it cannot revive the completed retry.
        lease=await Activate(pane);await GameEquipmentProbeReady(pane);
        checks.Add("known prewrite equipment failure preserves one command rejects replacement edits and explicit retry saves once while the old handle is inert");

        await GameEquipmentProbeSelect(pane,"equipment-slot-reel","");
        page=await GameEquipmentProbeClick(pane,"equipment-apply-reel","assembly-candidate",true);
        var detached=game.Snapshot.State;
        Require(detached.Rig.ReelId==null&&detached.Rig.LineId==null&&detached.Rig.LineLengthMm==null&&
            detached.Equipment.Find(reel).Location.Place==FloatEquipmentPlace.Inventory&&detached.Equipment.Spool(reel).Id==line&&
            (string)GameEquipmentProbeItem(page,line)["reelId"]==reel.ToString("D")&&GameEquipmentProbeItems(page).Count==6,"Detaching reel duplicated its line or left a shadow equipped line.");
        await GameEquipmentProbeSelect(pane,"equipment-slot-reel",reel.ToString("D"));
        page=await GameEquipmentProbeClick(pane,"equipment-apply-reel","assembly-candidate",true);
        Require(game.Snapshot.State.Sequence==sequence+4&&game.Snapshot.State.Rig.ReelId==reel&&game.Snapshot.State.Rig.LineId==line&&
            game.Snapshot.State.Equipment.RemainingLineMm==50000,"Reattaching the same reel changed its line or length.");
        checks.Add("actual reel detach and reattach keep the same 50-metre line on its original reel with no duplicate equipped line");

        await GameEquipmentProbeSelect(pane,"equipment-line",line.ToString("D"));
        await GameEquipmentProbeSelect(pane,"equipment-reel","");
        page=await GameEquipmentProbeClick(pane,"equipment-move-line","line-move-candidate",true);
        Require(game.Snapshot.State.Rig.LineId==null&&game.Snapshot.State.Equipment.Spool(reel)==null&&
            game.Snapshot.State.Equipment.Find(line).Location.Place==FloatEquipmentPlace.Inventory&&
            Number(GameEquipmentProbeItem(page,line),"remainingLineMm")==50000,"Removing whole line lost length or left a loaded spool.");
        await GameEquipmentProbeSelect(pane,"equipment-line",line.ToString("D"));
        await GameEquipmentProbeSelect(pane,"equipment-reel",reel.ToString("D"));
        page=await GameEquipmentProbeClick(pane,"equipment-move-line","line-move-candidate",true);
        Require(game.Snapshot.State.Sequence==sequence+6&&game.Snapshot.State.Rig.LineId==line&&
            game.Snapshot.State.Equipment.Find(line).InitialLineMm==50000&&game.Snapshot.State.Equipment.RemainingLineMm==50000&&
            game.Snapshot.State.Equipment.LostLineMm==0,"Whole-line return changed identity or conserved millimetres.");
        checks.Add("actual line and reel selects move one whole 50-metre segment to inventory and back without loss splitting or duplication");

        unchanged=GameShopJournalDigest();var stale=GameEquipmentProbeReplace(pane,lease,"hook",null,checked(gameViews[pane].EquipmentHighWater+1),sequence);
        GameCode(await GameEquipmentProbeWire(pane,stale),"version-conflict",false);
        Require(GameShopJournalDigest()==unchanged&&game.Snapshot.State.Sequence==sequence+6,"Outdated expected sequence changed equipment.");
        lease=await Activate(pane);page=await GameEquipmentProbeReady(pane);
        checks.Add("an authorized edit with an obsolete expected sequence is rejected without a write and fresh control remains available");

        var final=game.Snapshot.State;var ids=new HashSet<Guid>();foreach(var item in original.Instances)ids.Add(item.Id);
        Require(final.Sequence==sequence+6&&final.WalletMinor==start.WalletMinor&&final.BaitCount==start.BaitCount&&final.Stamina==start.Stamina&&
            final.FoodCount==start.FoodCount&&final.OwnedNet==start.OwnedNet&&final.EquippedNetId==start.EquippedNetId&&final.Creel.Count==start.Creel.Count&&
            final.Session==null&&gameFlightEmissions==flights&&final.Equipment.Instances.Count==6&&final.Equipment.RigRevision==original.RigRevision+6&&
            final.Equipment.InventoryRevision==original.InventoryRevision+6&&final.Equipment.InitialLineMm==50000&&final.Equipment.RemainingLineMm==50000&&
            final.Equipment.LostLineMm==0&&final.Rig.HookId==hook&&final.Rig.ReelId==reel&&final.Rig.LineId==line,"Final equipment transaction/resource totals differ from the six-edit plan.");
        foreach(var item in final.Equipment.Instances){var old=original.Find(item.Id);Require(ids.Remove(item.Id)&&old!=null&&item.DefinitionKey==old.DefinitionKey&&
            item.Kind==old.Kind&&item.InitialLineMm==old.InitialLineMm&&item.RemainingLineMm==old.RemainingLineMm&&item.Location.Place==old.Location.Place&&
            item.Location.OwnerId==old.Location.OwnerId&&item.Location.Slot==old.Location.Slot,"Equipment identity or final ownership changed.");}
        Require(ids.Count==0,"An original equipment instance disappeared.");
        for(int i=0;i<start.Creel.Count;i++)Require(final.Creel[i].Id==start.Creel[i].Id&&final.Creel[i].MassMg==start.Creel[i].MassMg&&final.Creel[i].SaleValueMinor==start.Creel[i].SaleValueMinor,"Equipment edits changed existing fish.");
        observer=await Wait(read,s=>Field(s,"equipmentState")!=null&&(string)Field(s,"equipmentState")["stateSequence"]==final.Sequence.ToString(CultureInfo.InvariantCulture));
        Require(GameEquipmentProbeItems(observer).Count==6&&Number(GameEquipmentProbeItem(observer,line),"remainingLineMm")==50000,"Observer projection did not converge to the final owned line.");
        await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('equipment').scrollIntoView()");
        using(var preview=File.Create(Path.Combine(root,"game-equipment-panel.png")))await pane.View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,preview);
        await GameMenu(pane,lease,false);
        Require(!game.Snapshot.MenuOpen&&game.Snapshot.State.Session==null,"Equipment probe did not leave a closed idle menu.");
        checks.Add("both pages converge after exactly six equipment commits preserving money bait stamina food fish original IDs and line length; panel screenshot captured");
        return lease;
    }
}
