using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FishingDesktop.GameCore;
using FishingDesktop.Diagnostics.HostPolicy;
using Microsoft.Web.WebView2.Core;

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
    Dictionary<string,object> GameCastMessage(ProbeWindow pane,string lease,string action,long sequence,string document=null)
    {
        return new Dictionary<string,object> {{"kind","game-cast"},{"documentId",document??pane.Ticket.DocumentId},{"lease",lease},
            {"transportId",Guid.NewGuid().ToString("D")},{"action",action},{"inputSequence",sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)}};
    }
    async Task<Dictionary<string,object>> GameCastWire(ProbeWindow pane,string lease,string action,long sequence,string document=null)
    {
        var message=GameCastMessage(pane,lease,action,sequence,document);
        await pane.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.sendRaw("+json.Serialize(message)+")");
        return await Wait(pane,s=>Field(s,"lastReply")!=null && (string)Field(s,"lastReply")["transportId"]==(string)message["transportId"]);
    }
    long NextGameInput(ProbeWindow pane){return checked(gameViews[pane].CastHighWater+1);}
    async Task GameCastAndWait(ProbeWindow pane,string lease)
    {
        long sequence=NextGameInput(pane),saved=game.Snapshot.State.Sequence;int flights=gameFlightEmissions;
        GameCode(await GameCastWire(pane,lease,"begin",sequence),"charging",false);
        Require(game.Snapshot.State.Sequence==saved,"Begin consumed resources.");
        GameCode(await GameCastWire(pane,lease,"release",sequence),"prepared",false);
        await Wait(pane,s=>(string)Field(s,"snapshot")["Sequence"]==(saved+1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Require(gameFlightEmissions==flights+1,"Confirmed cast did not emit flight exactly once.");
    }
    static void GameCode(Dictionary<string,object> state,string code,bool committed)
    {
        var reply=Field(state,"lastReply");
        Require((string)reply["code"]==code && (bool)reply["committed"]==committed,"Unexpected game reply: "+(string)reply["code"]);
    }
    void GameFixtureLand()
    {
        Require(GameSelfTest,"Controlled fish facts are restricted to the private scripted check.");
        Require(GameReadyForFish(),"A fish fact cannot bypass valid water contact or pause.");
        var s=game.Snapshot.State;
        var result=game.ExecuteHost(new HostFloatFish(s.ProfileId,s.BranchId,Guid.NewGuid(),Guid.NewGuid(),s.Session.Id,Guid.NewGuid(),Guid.NewGuid(),"carp",1000000,s.Sequence,s.Rig.Revision,s.RulesVersion));
        Require(result.Committed,"Controlled host fact was not saved."); Broadcast();
    }
    async Task GameWaitForWater(ProbeWindow pane)
    {
        string session=game.Snapshot.State.Session.Id.ToString("D");
        await Wait(pane,s=>Field(s,"flightState")!=null && (string)Field(s,"flightState")["sessionId"]==session &&
            (string)Field(s,"flightState")["phase"]=="waiting" && GameReadyForFish());
        Require(GameReadyForFish() && gameFlight.Snapshot.Ymm==0,"Waiting projection lacks valid host water contact.");
    }
    async Task<string> GameFlightLifecycleProbe(ProbeWindow pane,string lease)
    {
        var runtime=gameFlight;
        Require(runtime!=null && runtime.Snapshot.Phase==FloatFlightPhase.Flying && !GameReadyForFish(),"Flight did not begin before water eligibility.");
        long sequence=game.Snapshot.State.Sequence;
        await GameMenu(pane,lease,true);
        var frozen=runtime.Snapshot;
        await Task.Delay(180);
        Require(runtime.Snapshot.ElapsedMilliseconds==frozen.ElapsedMilliseconds && runtime.Snapshot.Ymm==frozen.Ymm && !GameReadyForFish(),"Menu advanced flight or allowed fish facts.");
        await GameMenu(pane,lease,false);
        Require(Object.ReferenceEquals(runtime,gameFlight),"Menu rebuilt the trajectory.");
        checks.Add("menu freezes the active trajectory and fish eligibility without replacing its paid session");

        SetHostPause(PauseReason.Manual,true);await Wait(pane,s=>s["lease"]==null);
        frozen=runtime.Snapshot;await Task.Delay(180);
        Require(runtime.Snapshot.ElapsedMilliseconds==frozen.ElapsedMilliseconds && runtime.Snapshot.Zmm==frozen.Zmm,"Host pause advanced the trajectory.");
        SetHostPause(PauseReason.Manual,false);lease=await Activate(pane);
        Require(Object.ReferenceEquals(runtime,gameFlight),"Host resume launched the same session again.");
        checks.Add("host pause freezes flight and resume retains one runtime with a new control grant");

        await GameWaitForWater(pane);
        Require(game.Snapshot.State.Sequence==sequence && game.Snapshot.State.Creel.Count==0 && runtime.Snapshot.LineUsedMm<=game.Snapshot.State.Rig.LineLengthMm,"Water contact created rewards or exceeded line.");
        await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('flight').scrollIntoView()");
        using(var preview=File.Create(Path.Combine(root,"game-flight-panel.png")))
            await pane.View.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png,preview);
        checks.Add("host trajectory reaches bounded flat water before waiting without granting fish or spending again");
        return lease;
    }
    async Task GamePreparationCancellationProbe(ProbeWindow pane,string lease)
    {
        long saved=game.Snapshot.State.Sequence;int flights=gameFlightEmissions;
        long cancelled=NextGameInput(pane);
        GameCode(await GameCastWire(pane,lease,"begin",cancelled),"charging",false);
        GameCode(await GameCastWire(pane,lease,"cancel",cancelled),"cancelled",false);
        GameCode(await GameCastWire(pane,lease,"begin",cancelled),"stale-input",false);
        await GameCastAndWait(pane,lease);
        GameCode(await GameWire(pane,lease,GameIntent("recall")),"recall-candidate",true);
        GameCode(await GameCastWire(pane,lease,"begin",cancelled),"stale-input",false);
        GameCode(await GameCastWire(pane,lease,"release",cancelled),"gesture-not-current",false);
        Require(game.Snapshot.State.Sequence==saved+2 && gameFlightEmissions==flights+1,"Old begin returned after a newer completed session.");
        saved=game.Snapshot.State.Sequence;flights=gameFlightEmissions;
        checks.Add("cancelled input stays consumed after a newer cast and recall; old release cannot target the new gesture");

        long held=NextGameInput(pane);
        GameCode(await GameCastWire(pane,lease,"begin",held),"charging",false);
        await Task.Delay(90);await GameMenu(pane,lease,true);
        int elapsed=gameCast.Snapshot.ElapsedMilliseconds;double frozen=activityClock.ActiveSeconds;
        await Task.Delay(160);
        Require(gameCast.Snapshot.Phase==FloatCastPhase.Cancelled && gameCast.Snapshot.ElapsedMilliseconds==elapsed && activityClock.ActiveSeconds==frozen,"Menu did not cancel and freeze preparation.");
        await GameMenu(pane,lease,false);
        GameCode(await GameCastWire(pane,lease,"release",held),"gesture-not-active",false);
        Require(game.Snapshot.State.Sequence==saved && gameFlightEmissions==flights,"Menu cancellation charged resources.");
        checks.Add("real menu message cancels held charge and freezes host time without release on menu close");

        long queued=NextGameInput(pane);
        GameCode(await GameCastWire(pane,lease,"begin",queued),"charging",false);
        // Controlled ordering on the same owner turn: the usual release entry
        // queues its host event, then menu cancellation wins before dispatch.
        Receive(pane,PageUrl,json.Serialize(GameCastMessage(pane,lease,"release",queued)));
        Require(gameCast.Snapshot.Phase==FloatCastPhase.Prepared,"Controlled release was not prepared.");
        Receive(pane,PageUrl,json.Serialize(new {kind="game-menu",documentId=pane.Ticket.DocumentId,lease=lease,transportId=Guid.NewGuid().ToString("D"),open=true}));
        await Task.Delay(150);
        Require(gameCast.Snapshot.Phase==FloatCastPhase.Cancelled && game.Snapshot.State.Sequence==saved && gameFlightEmissions==flights,"Queued release crossed menu cancellation.");
        await GameMenu(pane,lease,false);
        checks.Add("controlled owner ordering cancels a prepared queued release before the host event can commit");

        queued=NextGameInput(pane);
        GameCode(await GameCastWire(pane,lease,"begin",queued),"charging",false);
        Receive(pane,PageUrl,json.Serialize(GameCastMessage(pane,lease,"release",queued)));
        Revoke(pane);
        await Wait(pane,s=>s["lease"]==null);
        await Task.Delay(100);
        Require(gameCast.Snapshot.Phase==FloatCastPhase.Cancelled && game.Snapshot.State.Sequence==saved && gameFlightEmissions==flights,"Queued release crossed revocation.");
        string oldLease=lease;lease=await Activate(pane);
        GameCode(await GameCastWire(pane,oldLease,"begin",1),"control-required",false);
        GameCode(await GameCastWire(pane,lease,"begin",1),"charging",false);
        Require(gameCastInput.Sequence==1,"Fresh grant did not get its independent input highwater.");
        checks.Add("prepared host event rechecks the lease and a fresh grant may start its own sequence without accepting old authority");

        string oldDocument=pane.Ticket.DocumentId;oldLease=lease;
        Reload(pane);await Wait(pane,s=>(string)s["documentId"]!=oldDocument && Field(s,"snapshot")!=null);
        Require(gameCast.Snapshot.Phase==FloatCastPhase.Cancelled && game.Snapshot.State.Sequence==saved,"Reload failed to cancel an uncommitted gesture.");
        GameCode(await GameCastWire(pane,oldLease,"release",1,oldDocument),"control-required",false);
        lease=await Activate(pane);GameCode(await GameCastWire(pane,lease,"begin",1),"charging",false);
        SetHostPause(PauseReason.Manual,true);await Wait(pane,s=>s["lease"]==null);
        elapsed=gameCast.Snapshot.ElapsedMilliseconds;frozen=activityClock.ActiveSeconds;await Task.Delay(150);
        Require(gameCast.Snapshot.Phase==FloatCastPhase.Cancelled && gameCast.Snapshot.ElapsedMilliseconds==elapsed && activityClock.ActiveSeconds==frozen,"Host pause advanced a held preparation.");
        SetHostPause(PauseReason.Manual,false);
        GameCode(await GameCastWire(pane,lease,"release",1),"control-required",false);
        Require(game.Snapshot.State.Sequence==saved && gameFlightEmissions==flights,"Pause/reload created an unexpected cast.");
        checks.Add("real reload and controlled host pause cancel charge; late document and pre-pause release cannot revive it");
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
            GameCode(await GameCastWire(read,fake,"begin",1),"control-required",false);
            GameCode(await GameCastWire(control,fake,"begin",1),"control-required",false);
            Require(game.Snapshot.State.Sequence==0,"Unauthorized page changed save.");
            checks.Add("observer and unactivated interaction cannot mutate or query a transaction receipt");

            string lease=await Activate(control);
            foreach(var badSequence in new object[]{"01","+1","9223372036854775808",1}) {
                var bad=GameCastMessage(control,lease,"begin",1);bad["inputSequence"]=badSequence;
                await control.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.sendRaw("+json.Serialize(bad)+")");
                GameCode(await Wait(control,s=>Field(s,"lastReply")!=null && (string)Field(s,"lastReply")["transportId"]==(string)bad["transportId"]),"invalid-intent",false);
            }
            var extra=GameCastMessage(control,lease,"begin",1);extra.Add("powerPermille",1000);
            await control.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.sendRaw("+json.Serialize(extra)+")");
            GameCode(await Wait(control,s=>Field(s,"lastReply")!=null && (string)Field(s,"lastReply")["transportId"]==(string)extra["transportId"]),"invalid-intent",false);
            Require(gameViews[control].CastHighWater==0 && game.Snapshot.State.Sequence==0,"Malformed input changed gesture or durable state.");
            checks.Add("cast protocol rejects noncanonical sequence, overflow, numeric sequence and page-provided power before consuming input");
            GameCode(await GameCastWire(control,lease,"begin",1),"charging",false);
            await Task.Delay(180);
            Require(gameCast.Snapshot.ElapsedMilliseconds>=100 && gameCast.Snapshot.PowerPermille>150 && game.Snapshot.State.Sequence==0,"Host preparation clock did not advance without charging resources.");
            int charged=gameCast.Snapshot.ElapsedMilliseconds;
            GameCode(await GameCastWire(control,lease,"begin",1),"stale-input",false);
            Require(gameCast.Snapshot.ElapsedMilliseconds>=charged,"Duplicate begin reset charge time.");
            checks.Add("host active clock charges a held gesture without file writes and duplicate begin cannot reset it");
            GameCode(await GameCastWire(control,lease,"release",1),"prepared",false);
            await Wait(read,s=>(string)Field(s,"snapshot")["Sequence"]=="1");
            await Wait(read,s=>Number(s,"flightCount")==1);await Wait(control,s=>Number(s,"flightCount")==1);
            Require(game.Snapshot.State.BaitCount==11 && game.Snapshot.State.Stamina==98 && gameFlightEmissions==1,"Cast cost/flight was not saved/emitted exactly once.");
            Require(await read.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('stats').textContent.includes('11 份')")=="true","Visible resource text did not update.");
            checks.Add("typed release prepares first and host event commits once before the other page receives new resources");

            lease=await GameFlightLifecycleProbe(control,lease);
            lease=await GameBiteLifecycleProbe(read,control,lease);

            GameCode(await GameCastWire(control,lease,"release",1),"gesture-not-current",false);
            GameCode(await GameCastWire(control,lease,"begin",1),"session-busy",false);
            Require(game.Snapshot.State.Sequence==1 && gameFlightEmissions==1,"Repeated input charged or emitted flight again.");
            GameCode(await GameWire(control,lease,cast),"cast-preparation-required",false);
            var malformed=new Dictionary<string,object>(cast);malformed.Add("massMg","999999");
            GameCode(await GameWire(control,lease,malformed),"invalid-intent",false);
            checks.Add("late release cannot repeat flight, direct cast bypass is closed and page fish mass is rejected");

            string oldDocument=control.Ticket.DocumentId,oldLease=lease;
            var keptFlight=gameFlight;
            Reload(control); await Wait(control,s=>(string)s["documentId"]!=oldDocument && Field(s,"snapshot")!=null);
            Require(game.Snapshot.State.Sequence==1 && game.Snapshot.State.Session!=null,"Page reload settled game session.");
            await GameWaitForWater(control);
            Require(Object.ReferenceEquals(keptFlight,gameFlight),"Page reload restarted flight.");
            c=await Ready(control);Require(Number(c,"flightCount")==0 && Field(c,"lastFlight")==null,"New document replayed an old flight.");
            GameCode(await GameCastWire(control,oldLease,"release",1,oldDocument),"control-required",false);
            GameCode(await GameCastWire(control,oldLease,"release",1),"control-required",false);
            lease=await Activate(control);GameCode(await GameCastWire(control,lease,"release",1),"gesture-not-current",false);
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
            Require(gameFlight.Snapshot.Phase==FloatFlightPhase.Ended,"Recall left the old flight active.");
            checks.Add("host pause revokes game control and resume requires fresh activation before recall");

            await Wait(control,s=>(string)Field(s,"snapshot")["Sequence"]=="2");
            await control.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('cast').click()");
            await Wait(control,s=>Field(s,"castState")!=null && (string)Field(s,"castState")["phase"]=="charging");
            Require(game.Snapshot.State.Sequence==2,"Page begin button skipped preparation.");
            using(var preview=File.Create(Path.Combine(root,"game-cast-panel.png")))
                await control.View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,preview);
            await control.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('cast-release').click()");
            await Wait(control,s=>(string)Field(s,"snapshot")["Sequence"]=="3");
            await Wait(read,s=>Number(s,"flightCount")==2);
            Require(game.Snapshot.State.BaitCount==10,"Page button did not reach typed game transaction.");
            await GameWaitForWater(control);GameFixtureLand();
            await GameMenu(control,lease,true);
            Require(!game.Snapshot.CanSimulate && game.Snapshot.MenuOpen,"Menu did not pause game eligibility.");
            var sell=GameIntent("sell"); sell.Add("fishIds",new[]{game.Snapshot.State.Creel[0].Id.ToString("D")});
            GameCode(await GameWire(control,lease,sell),"sale-candidate",true);
            Require(game.Snapshot.State.WalletMinor==12120 && game.Snapshot.State.Creel.Count==0,"Menu sale failed.");
            checks.Add("host-only controlled fish fact can be sold by selected IDs while menu simulation is paused");

            GameCode(await GameCastWire(control,lease,"begin",NextGameInput(control)),"domain-paused",false);
            await GameMenu(control,lease,false);
            await GamePreparationCancellationProbe(control,lease);
            // The probe above revokes/regrants; explicitly acquire the current lease.
            lease=await Activate(control);
            await GameBiteFailureProbe(control,lease);
            for(int index=0;index<3;index++) { await GameCastAndWait(control,lease); await GameWaitForWater(control); GameFixtureLand(); }
            Require(game.Snapshot.PendingResult && !game.Snapshot.CanSimulate,"Full-creel result was not paused.");
            Require(gameFlight.Snapshot.Phase==FloatFlightPhase.Ended,"Pending fish kept an active flight runtime.");
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

            await GameCastAndWait(control,lease);
            await Wait(read,s=>Number(s,"flightCount")==gameFlightEmissions);
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
                nativeInputTested=false,desktopLayer=false,nativeFullscreenTested=false,gameSimulationImplemented=false,controlledFishFacts=true,personalSaveRead=false,
                castPreparationConnected=true,gameFlightEmissions=gameFlightEmissions,flatWaterFlightConnected=true,realWaterAndFlight=false,
                biteSimulationConnected=true,gameHookedEmissions=gameHookedEmissions,naturalFishLanded=false,controlledBiteSeed=true}),Encoding.UTF8);
            Shutdown();
        } catch(Exception error) { Fail(error); }
    }
}
