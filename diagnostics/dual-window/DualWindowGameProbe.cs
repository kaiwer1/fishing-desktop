using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FishingDesktop.GameCore;
using FishingDesktop.Diagnostics.HostPolicy;

sealed partial class DualWindowContext
{
    Dictionary<string,object> GameIntent(string action)
    {
        var state=game.Snapshot.State;
        return new Dictionary<string,object> { {"action",action},{"requestId",Guid.NewGuid().ToString("D")},{"eventId",Guid.NewGuid().ToString("D")},
            {"sessionId",action=="cast"?Guid.NewGuid().ToString("D"):action=="sell"?"":state.Session.Id.ToString("D")},
            {"expectedSequence",state.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)},
            {"rigRevision","1"},{"rulesVersion","1"} };
    }
    async Task<Dictionary<string,object>> GameWire(ProbeWindow pane,string lease,Dictionary<string,object> intent,string document=null)
    {
        string transport=Guid.NewGuid().ToString("D");
        await pane.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.sendRaw("+json.Serialize(new {kind="game-intent",documentId=document??pane.Ticket.DocumentId,lease=lease,transportId=transport,intent=intent})+")");
        return await Wait(pane,s=>Field(s,"lastReply")!=null && (string)Field(s,"lastReply")["transportId"]==transport);
    }
    async Task GameMenu(ProbeWindow pane,string lease,bool open)
    {
        string transport=Guid.NewGuid().ToString("D");
        await pane.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.sendRaw("+json.Serialize(new {kind="game-menu",documentId=pane.Ticket.DocumentId,lease=lease,transportId=transport,open=open})+")");
        await Wait(pane,s=>Field(s,"lastReply")!=null && (string)Field(s,"lastReply")["transportId"]==transport && (string)Field(s,"lastReply")["code"]=="menu-updated");
    }
    static void GameCode(Dictionary<string,object> state,string code,bool committed)
    {
        var reply=Field(state,"lastReply");
        Require((string)reply["code"]==code && (bool)reply["committed"]==committed,"Unexpected game reply: "+(string)reply["code"]);
    }
    void GameFixtureLand()
    {
        var s=game.Snapshot.State;
        var result=game.ExecuteHost(new HostFloatFish(s.ProfileId,s.BranchId,Guid.NewGuid(),Guid.NewGuid(),s.Session.Id,Guid.NewGuid(),Guid.NewGuid(),"carp",1000000,s.Sequence,s.Rig.Revision,s.RulesVersion));
        Require(result.Committed,"Controlled host fact was not saved."); Broadcast();
    }
    async void RunGameProbe()
    {
        try {
            var read=windows[false]; var control=windows[true];
            var r=await Ready(read); var c=await Ready(control);
            Require((string)Field(r,"snapshot")["Sequence"]=="0" && c["lease"]==null,"Initial game projection invalid.");
            string projection=json.Serialize(Field(r,"snapshot"));
            Require(!projection.Contains("Receipts") && !projection.Contains("ProfileId") && !projection.Contains("BranchId") && !projection.Contains("Fingerprint"),"Private owner state reached page.");
            checks.Add("two real pages receive bounded game display data without owner history or an input grant");

            var cast=GameIntent("cast"); string fake=Guid.NewGuid().ToString("D");
            GameCode(await GameWire(read,fake,cast),"control-required",false);
            GameCode(await GameWire(control,fake,cast),"control-required",false);
            Require(game.Snapshot.State.Sequence==0,"Unauthorized page changed save.");
            checks.Add("observer and unactivated interaction cannot mutate or query a transaction receipt");

            string lease=await Activate(control);
            GameCode(await GameWire(control,lease,cast),"cast-candidate",true);
            await Wait(read,s=>(string)Field(s,"snapshot")["Sequence"]=="1");
            Require(game.Snapshot.State.BaitCount==11 && game.Snapshot.State.Stamina==98,"Cast cost was not saved exactly once.");
            Require(await read.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('stats').textContent.includes('11 份')")=="true","Visible resource text did not update.");
            checks.Add("typed page cast commits actual game file before the other page receives new resources");

            c=await GameWire(control,lease,cast); GameCode(c,"cast-candidate",true);
            Require((bool)Field(c,"lastReply")["replay"] && game.Snapshot.State.Sequence==1,"Replay charged again.");
            var malformed=new Dictionary<string,object>(cast);malformed.Add("massMg","999999");
            GameCode(await GameWire(control,lease,malformed),"invalid-intent",false);
            checks.Add("same request replays once and a page-provided fish mass cannot enter the journal");

            string oldDocument=control.Ticket.DocumentId,oldLease=lease;
            Reload(control); await Wait(control,s=>(string)s["documentId"]!=oldDocument && Field(s,"snapshot")!=null);
            Require(game.Snapshot.State.Sequence==1 && game.Snapshot.State.Session!=null,"Page reload settled game session.");
            GameCode(await GameWire(control,oldLease,cast,oldDocument),"control-required",false);
            GameCode(await GameWire(control,oldLease,cast),"control-required",false);
            lease=await Activate(control);GameCode(await GameWire(control,lease,cast),"cast-candidate",true);
            checks.Add("real document reload keeps active session while old document and lease remain rejected");

            SetHostPause(PauseReason.Manual,true);
            await Wait(control,s=>s["lease"]==null);
            GameCode(await GameWire(control,lease,cast),"control-required",false);
            Require(game.Snapshot.HostPaused && !game.Snapshot.CanSimulate,"Host pause did not reach game owner.");
            SetHostPause(PauseReason.Manual,false);
            GameCode(await GameWire(control,lease,cast),"control-required",false);
            lease=await Activate(control);
            GameCode(await GameWire(control,lease,GameIntent("recall")),"recall-candidate",true);
            Require(game.Snapshot.State.BaitCount==11,"Recall refunded consumed bait.");
            checks.Add("host pause revokes game control and resume requires fresh activation before recall");

            await Wait(control,s=>(string)Field(s,"snapshot")["Sequence"]=="2");
            await control.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('cast').click()");
            await Wait(control,s=>(string)Field(s,"snapshot")["Sequence"]=="3");
            Require(game.Snapshot.State.BaitCount==10,"Page button did not reach typed game transaction.");
            GameFixtureLand();
            await GameMenu(control,lease,true);
            Require(!game.Snapshot.CanSimulate && game.Snapshot.MenuOpen,"Menu did not pause game eligibility.");
            var sell=GameIntent("sell"); sell.Add("fishIds",new[]{game.Snapshot.State.Creel[0].Id.ToString("D")});
            GameCode(await GameWire(control,lease,sell),"sale-candidate",true);
            Require(game.Snapshot.State.WalletMinor==12120 && game.Snapshot.State.Creel.Count==0,"Menu sale failed.");
            checks.Add("host-only controlled fish fact can be sold by selected IDs while menu simulation is paused");

            GameCode(await GameWire(control,lease,GameIntent("cast")),"domain-paused",false);
            await GameMenu(control,lease,false);
            for(int index=0;index<3;index++) { GameCode(await GameWire(control,lease,GameIntent("cast")),"cast-candidate",true); GameFixtureLand(); }
            Require(game.Snapshot.PendingResult && !game.Snapshot.CanSimulate,"Full-creel result was not paused.");
            await Wait(control,s=>(bool)Field(s,"snapshot")["PendingResult"]);
            Require(await control.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('session').textContent.includes('鲤鱼 · 1.000000 kg')")=="true","Pending fish identity was not shown before release.");
            GameCode(await GameWire(control,lease,GameIntent("release-current")),"released-candidate",true);
            Require(game.Snapshot.State.Session==null && game.Snapshot.State.Creel.Count==2,"Release lost owned fish.");
            checks.Add("menu blocks a new cast and full creel requires explicit release without deleting old fish");

            await control.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.cancel()");
            await Wait(control,s=>s["lease"]==null && Field(s,"lastRevoke")!=null);
            GameCode(await GameWire(control,lease,sell),"control-required",false);
            lease=await Activate(control);
            GameCode(await GameWire(control,lease,sell),"sale-candidate",true);
            Require(game.Snapshot.State.WalletMinor==12120,"Old sale replay paid twice.");
            checks.Add("page cancellation invalidates game lease and fresh authorized retry preserves old sale receipt");

            GameCode(await GameWire(control,lease,GameIntent("cast")),"cast-candidate",true);
            long sequence=game.Snapshot.State.Sequence;
            read.Close();read.Dispose();control.Close();control.Dispose();
            Require(game.Snapshot.State.Session!=null && game.Snapshot.State.Sequence==sequence && game.Snapshot.HostPaused,"Closing views altered paid session or left simulation active.");
            bool locked=false;try { using(var other=FloatGameStore.OpenExisting(Path.Combine(root,"game.fltj"))) {} } catch(IOException) { locked=true; }
            Require(locked,"Game writer lock vanished with views.");
            read=await Open(false);control=await Open(true);await Ready(read);c=await Ready(control);
            Require(c["lease"]==null && game.Snapshot.State.Sequence==sequence,"Rebuilt views reset game or granted control.");
            checks.Add("zero real windows retain the unique game file writer and later view recreation preserves state");

            Require(owner.Snapshot().Sequence==0 && owner.Snapshot().Value==0,"Game path wrote diagnostic counter.");
            checks.Add("all game intents bypass diagnostic counter transactions");
            PrepareGameShutdown();
            Require(game.Snapshot.ShutdownConfirmed && game.Snapshot.State.Session==null && game.Snapshot.State.Sequence==sequence+1,"Explicit shutdown failed to settle active cast.");
            checks.Add("normal host shutdown confirms one no-refund recovery before closing views");
            File.WriteAllText(Path.Combine(root,"webview-summary.json"),json.Serialize(new {passed=true,checks=checks.ToArray(),gameLoaded=true,
                sequence=game.Snapshot.State.Sequence,value=game.Snapshot.State.WalletMinor,realWebViewMessages=true,independentWindows=true,
                nativeInputTested=false,desktopLayer=false,nativeFullscreenTested=false,gameSimulationImplemented=false,controlledFishFacts=true,personalSaveRead=false}),Encoding.UTF8);
            Shutdown();
        } catch(Exception error) { Fail(error); }
    }
}
