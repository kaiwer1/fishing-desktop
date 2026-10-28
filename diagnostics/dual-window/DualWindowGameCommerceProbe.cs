using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

sealed partial class DualWindowContext
{
    Dictionary<string,object> GameShopMessage(ProbeWindow pane,string lease,string action,string document=null)
    {
        return new Dictionary<string,object>{{"kind","game-shop"},{"documentId",document??pane.Ticket.DocumentId},{"lease",lease},
            {"transportId",Guid.NewGuid().ToString("D")},{"action",action}};
    }
    async Task<Dictionary<string,object>> GameShopWire(ProbeWindow pane,Dictionary<string,object> message)
    {
        await pane.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.sendRaw("+json.Serialize(message)+")");
        return await Wait(pane,s=>Field(s,"lastReply")!=null&&(string)Field(s,"lastReply")["transportId"]==(string)message["transportId"]);
    }
    async Task<Dictionary<string,object>> GameShopReady(ProbeWindow pane)
    {
        return await Wait(pane,s=>Field(s,"shopState")!=null&&Field(s,"shopInputState")!=null&&
            (bool)Field(s,"shopInputState")["ready"]&&Field(s,"shopInputState")["inflight"]==null&&!(bool)Field(s,"shopInputState")["awaitingState"]);
    }
    async Task<Dictionary<string,object>> GameShopClick(ProbeWindow pane,string element,string code,bool committed)
    {
        var before=await GameShopReady(pane);var oldReply=Field(before,"lastReply");string oldTransport=oldReply==null?null:(string)oldReply["transportId"];
        string id=json.Serialize(element);
        Require(await pane.View.CoreWebView2.ExecuteScriptAsync("Boolean(document.getElementById("+id+") && !document.getElementById("+id+").disabled)")=="true","Shop control is disabled: "+element);
        // Dispatch once. A timeout is a failure, never a reason to repeat a
        // possibly successful payment or consumption click.
        await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById("+id+").click()");
        var result=await Wait(pane,s=>Field(s,"lastReply")!=null&&(string)Field(s,"lastReply")["transportId"]!=oldTransport);
        GameCode(result,code,committed);return await GameShopReady(pane);
    }
    string GameShopJournalDigest()
    {
        using(var source=new FileStream(Path.Combine(root,"game.fltj"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
        using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(source));
    }
    static string ShopQuoteId(Dictionary<string,object> page)
    {
        var quote=Field(Field(page,"shopState"),"quote");Require(quote!=null,"Expected a current projected shop quote.");return (string)quote["id"];
    }
    Dictionary<string,object> ShopQuoteMessage(ProbeWindow pane,string lease,string product,object packs)
    {var message=GameShopMessage(pane,lease,"quote");message.Add("product",product);message.Add("packs",packs);return message;}
    Dictionary<string,object> ShopBuyMessage(ProbeWindow pane,string lease,string id,string document=null)
    {var message=GameShopMessage(pane,lease,"buy",document);message.Add("quoteId",id);return message;}

    // Called once with no current session, after the bite-failure probe and
    // before natural landing checks. It creates no casts or controlled fish.
    // Expected delta: seven commits, -2480 minor, +10 bait, one remaining food,
    // stamina capped to100, and one owned equipped net. No implicit retry loop.
    async Task<string> GameCommerceProbe(ProbeWindow read,ProbeWindow control,string lease)
    {
        Require(GameSelfTest,"Commerce probe requires a new private self-test fixture.");
        var start=game.Snapshot.State;long sequence=start.Sequence,wallet=start.WalletMinor;int baits=start.BaitCount,flights=gameFlightEmissions;
        Require(start.SchemaVersion==3&&start.Session==null&&start.OwnedNet==null&&start.FoodCount==0&&start.EquippedNetId==Guid.Empty,"Commerce probe did not start with unowned supplies.");
        Require(start.Stamina>=80&&start.Stamina<100,"Fixture stamina must allow one food portion to reach the cap.");
        await GameMenu(control,lease,true);var page=await GameShopReady(control);
        Require(!(bool)Field(page,"shopState")["netOwned"]&&Number(Field(page,"shopState"),"foodCount")==0,"Page manufactured starter supplies.");
        var readPage=await Wait(read,s=>Field(s,"shopState")!=null);
        Require(Field(Field(readPage,"shopState"),"quote")==null&&Field(Field(readPage,"shopState"),"retry")==null,"Observer received an opaque shopping handle.");
        checks.Add("schema3 menu starts with no owned net or food and observer receives only supply display state");

        string untouched=GameShopJournalDigest();
        foreach(var extra in new[]{"totalPriceMinor","newNetId","foodStackId"}){
            var bad=ShopQuoteMessage(control,lease,"bait",1);bad.Add(extra,"1");GameCode(await GameShopWire(control,bad),"invalid-intent",false);
        }
        foreach(object invalid in new object[]{0,-1,101,"2",1.5,true})GameCode(await GameShopWire(control,ShopQuoteMessage(control,lease,"bait",invalid)),"invalid-intent",false);
        GameCode(await GameShopWire(control,ShopQuoteMessage(control,lease,"net",2)),"invalid-intent",false);
        GameCode(await GameShopWire(control,ShopQuoteMessage(control,lease,"unknown",1)),"invalid-intent",false);
        Require(GameShopJournalDigest()==untouched&&game.Snapshot.State.Sequence==sequence,"Malformed shopping intent wrote to the journal.");
        checks.Add("real shop wire rejects page prices asset IDs invalid products and noninteger or excessive packs without writes");

        page=await GameShopClick(control,"quote-net","shop-quoted",false);string netQuote=ShopQuoteId(page);
        var q=Field(Field(page,"shopState"),"quote");Require((string)q["product"]=="net"&&(string)q["totalPriceMinor"]=="2400"&&Number(q,"units")==1,"Host net quote facts wrong.");
        Require(GameShopJournalDigest()==untouched&&game.Snapshot.State.OwnedNet==null,"Quote charged or created a net.");
        GameCode(await GameShopWire(read,ShopBuyMessage(read,lease,netQuote)),"control-required",false);
        GameCode(await GameShopWire(read,ShopQuoteMessage(read,lease,"net",1)),"control-required",false);
        Require(GameShopJournalDigest()==untouched&&gameViews[control].Shop!=null&&gameViews[control].Shop.Id==netQuote,"Observer changed or queried another binding quote.");
        checks.Add("real quote button freezes a 24-coin net offer without debit while observer cannot use its handle");

        page=await GameShopClick(control,"shop-buy","purchase-candidate",true);
        Require(game.Snapshot.State.OwnedNet!=null&&game.Snapshot.State.EquippedNetId==Guid.Empty&&game.Snapshot.State.WalletMinor==wallet-2400&&game.Snapshot.State.Sequence==sequence+1,"Net buy did not persist exactly one un-equipped net.");
        string afterNet=GameShopJournalDigest();GameCode(await GameShopWire(control,ShopBuyMessage(control,lease,netQuote)),"shop-intent-not-current",false);
        Require(GameShopJournalDigest()==afterNet&&Field(Field(await GameShopReady(control),"shopState"),"quote")==null,"Consumed buy handle wrote again.");
        checks.Add("actual buy button purchases one owned net without equipping and duplicate confirmation cannot charge again");

        await control.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('bait-packs').value='2'");
        page=await GameShopClick(control,"quote-bait","shop-quoted",false);string baitQuote=ShopQuoteId(page);string beforeFault=GameShopJournalDigest();
        ArmGameShopPrewriteFailure();page=await GameShopClick(control,"shop-buy","shop-not-written",false);
        var retry=Field(Field(page,"shopState"),"retry");Require(retry!=null&&(string)retry["operationId"]==baitQuote&&game.Snapshot.State.BaitCount==baits,"Known failure lost original retry identity or added bait.");
        GameCode(await GameShopWire(control,ShopBuyMessage(control,lease,baitQuote)),"shop-intent-not-current",false);
        Require(GameShopJournalDigest()==beforeFault&&game.Snapshot.State.WalletMinor==wallet-2400,"Old buy silently retried after known failure.");
        page=await GameShopClick(control,"shop-retry-confirm","purchase-candidate",true);
        Require(game.Snapshot.State.BaitCount==baits+10&&game.Snapshot.State.WalletMinor==wallet-2440&&game.Snapshot.State.Sequence==sequence+2,"Explicit same-operation retry did not buy exactly ten bait.");
        string afterBait=GameShopJournalDigest();var oldRetry=GameShopMessage(control,lease,"retry");oldRetry.Add("operationId",baitQuote);
        GameCode(await GameShopWire(control,oldRetry),"shop-intent-not-current",false);Require(GameShopJournalDigest()==afterBait,"Completed retry was consumed twice.");
        checks.Add("known prewrite shop fault preserves bytes rejects delayed old buy and only explicit same-ID retry purchases bait once");

        await control.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('food-packs').value='2'");
        page=await GameShopClick(control,"quote-food","shop-quoted",false);long priorStamina=game.Snapshot.State.Stamina;
        await GameShopClick(control,"shop-buy","purchase-candidate",true);
        Require(game.Snapshot.State.FoodCount==2&&game.Snapshot.State.Stamina==priorStamina&&game.Snapshot.State.WalletMinor==wallet-2480,"Food purchase consumed food or restored stamina.");
        checks.Add("food purchase adds two stored portions and does not restore stamina automatically");
        await GameShopClick(control,"food-eat","food-candidate",true);
        Require(game.Snapshot.State.FoodCount==1&&game.Snapshot.State.Stamina==100,"Explicit food button failed to restore capped stamina.");
        string beforeFull=GameShopJournalDigest();await GameShopClick(control,"food-eat","stamina-full",false);
        Require(gameShopPrewriteFaultArmed==false&&game.Snapshot.State.FoodCount==1&&GameShopJournalDigest()==beforeFull,"Full-stamina food was consumed or saved.");
        checks.Add("actual food button consumes one portion only and a second full-stamina action leaves food and journal unchanged");

        await GameShopClick(control,"net-equip","equip-candidate",true);Guid net=game.Snapshot.State.OwnedNet.Id;
        Require(game.Snapshot.State.EquippedNetId==net,"Equip button did not select owned net.");
        await GameShopClick(control,"net-unequip","equip-candidate",true);Require(game.Snapshot.State.EquippedNetId==Guid.Empty&&game.Snapshot.State.OwnedNet.Id==net,"Unequip deleted the owned asset.");
        await GameShopClick(control,"net-equip","equip-candidate",true);
        Require(game.Snapshot.State.EquippedNetId==net&&game.Snapshot.State.WalletMinor==wallet-2480,"Equipment change charged or replaced net.");
        checks.Add("actual equip unequip and re-equip buttons preserve one owned net without charging or inventing an asset");

        page=await GameShopClick(control,"quote-bait","shop-quoted",false);string blurQuote=ShopQuoteId(page),oldLease=lease;string stable=GameShopJournalDigest();
        var previousRevoke=Field(page,"lastRevoke");string previousRevokeTransport=previousRevoke==null?null:(string)previousRevoke["transportId"];
        await control.View.CoreWebView2.ExecuteScriptAsync("window.dispatchEvent(new Event('blur'))");
        await Wait(control,s=>s["lease"]==null&&Field(s,"lastRevoke")!=null&&(string)Field(s,"lastRevoke")["transportId"]!=previousRevokeTransport&&gameViews[control].Lease==null);
        Require(gameViews[control].Shop==null&&GameShopJournalDigest()==stable,"Page blur retained a quote or changed resources.");
        GameCode(await GameShopWire(control,ShopBuyMessage(control,oldLease,blurQuote)),"control-required",false);
        GameCode(await GameShopWire(control,ShopQuoteMessage(control,oldLease,"bait",1)),"control-required",false);
        lease=await Activate(control);await GameShopReady(control);
        GameCode(await GameShopWire(control,ShopBuyMessage(control,lease,blurQuote)),"shop-intent-not-current",false);
        Require(GameShopJournalDigest()==stable,"Fresh grant revived old blur-cancelled quote.");
        checks.Add("real page blur cancellation clears pending quote and neither old grant nor fresh grant can confirm it");

        page=await GameShopClick(control,"quote-bait","shop-quoted",false);string reloadQuote=ShopQuoteId(page),oldDocument=control.Ticket.DocumentId;oldLease=lease;
        Reload(control);await Wait(control,s=>(string)s["documentId"]!=oldDocument&&Field(s,"shopState")!=null);
        Require(gameViews[control].Shop==null&&GameShopJournalDigest()==stable,"Navigation retained a pending quote or mutated save.");
        GameCode(await GameShopWire(control,ShopBuyMessage(control,oldLease,reloadQuote,oldDocument)),"control-required",false);
        lease=await Activate(control);await GameShopReady(control);
        GameCode(await GameShopWire(control,ShopBuyMessage(control,lease,reloadQuote)),"shop-intent-not-current",false);
        Require(GameShopJournalDigest()==stable,"Reloaded page revived a cancelled quote.");
        checks.Add("actual WebView reload discards pending quote while stale document and later grant cannot consume its old ID");

        page=await GameShopReady(control);readPage=await Wait(read,s=>Field(s,"shopState")!=null&&(string)Field(s,"shopState")["walletMinor"]==(wallet-2480).ToString(CultureInfo.InvariantCulture));
        var final=game.Snapshot.State;
        Require(final.Sequence==sequence+7&&final.WalletMinor==wallet-2480&&final.BaitCount==baits+10&&final.FoodCount==1&&final.Stamina==100&&final.OwnedNet.Id==net&&final.EquippedNetId==net&&gameFlightEmissions==flights,"Commerce probe resource/transaction delta differs from plan.");
        string projected=json.Serialize(Field(readPage,"shopState"));Require(!projected.Contains("ProfileId")&&!projected.Contains("BranchId")&&!projected.Contains("FoodStackId")&&!projected.Contains(net.ToString("D"))&&Field(Field(readPage,"shopState"),"quote")==null,"Private asset or pending quote leaked to observer.");
        await control.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('shop').scrollIntoView()");
        using(var preview=File.Create(Path.Combine(root,"game-shop-panel.png")))await control.View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,preview);
        await GameMenu(control,lease,false);
        checks.Add("both real pages reflect seven conserved supply transactions with no extra casts and shop screenshot captured");
        return lease;
    }
}
