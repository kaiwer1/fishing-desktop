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
    Dictionary<string,object> WaterProbeMessage(ProbeWindow pane,string lease,string action,long sequence,int u=5000,int v=5000,int version=1,string document=null)
    {
        var message=GameCastMessage(pane,lease,action,sequence,document);
        if(action=="begin")message["aim"]=new Dictionary<string,object>{{"u",u},{"v",v},{"geometryVersion",version}};
        else message.Remove("aim");
        return message;
    }
    async Task<Dictionary<string,object>> WaterProbeWire(ProbeWindow pane,Dictionary<string,object> message)
    {
        await pane.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.sendRaw("+json.Serialize(message)+")");
        return await Wait(pane,s=>Field(s,"lastReply")!=null&&(string)Field(s,"lastReply")["transportId"]==(string)message["transportId"]);
    }
    async Task WaterProbeMouse(ProbeWindow pane,int u,int v,bool down)
    {
        // DevTools input targets this one hidden WebView. It exercises browser
        // hit testing and pointer capture without moving the system mouse.
        string raw=await pane.View.CoreWebView2.ExecuteScriptAsync("(()=>{const s=document.getElementById('water-aim-svg');if(!s)throw Error('Water SVG missing');s.scrollIntoView({block:'center'});const m=s.getScreenCTM();if(!m)throw Error('Water SVG has no screen transform');const p=new DOMPoint("+
            (u*480.0/10000).ToString("R",CultureInfo.InvariantCulture)+","+(v*280.0/10000).ToString("R",CultureInfo.InvariantCulture)+
            ").matrixTransform(m);return {x:p.x,y:p.y};})()");
        var point=json.Deserialize<Dictionary<string,object>>(raw);
        Require(point!=null&&point.ContainsKey("x")&&point.ContainsKey("y"),"SVG did not provide viewport coordinates.");
        await pane.View.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",json.Serialize(new {
            type=down?"mousePressed":"mouseReleased",x=Convert.ToDouble(point["x"],CultureInfo.InvariantCulture),
            y=Convert.ToDouble(point["y"],CultureInfo.InvariantCulture),button="left",buttons=down?1:0,clickCount=1}));
    }
    async Task WaterProbeBegin(ProbeWindow pane,int u,int v)
    {
        long saved=game.Snapshot.State.Sequence;int flights=gameFlightEmissions;
        await WaterProbeMouse(pane,u,v,true);
        var page=await Wait(pane,s=>Field(s,"castState")!=null&&(string)Field(s,"castState")["phase"]=="charging"&&
            Field(s,"inputState")!=null&&Field(Field(s,"inputState"),"aim")!=null&&gameCast.Snapshot.Phase==FloatCastPhase.Charging);
        var aim=Field(Field(page,"inputState"),"aim");
        Require(Number(aim,"u")==u&&Number(aim,"v")==v&&Number(aim,"geometryVersion")==gameWater.Version&&
            gameWaterPlan.U==u&&gameWaterPlan.V==v&&gameCast.Snapshot.Target.GeometryVersion==gameWater.Version,
            "Actual SVG input did not freeze the selected normalized aim.");
        Require(game.Snapshot.State.Sequence==saved&&gameFlightEmissions==flights,"Pointer begin spent resources or emitted a cast.");
    }
    void WaterProbeCharge()
    {
        Require(GameWaterSelfTest&&gameCastInput!=null&&GameCastCurrent(gameCastInput)&&gameCast.Snapshot.Phase==FloatCastPhase.Charging,
            "Controlled charge is restricted to the private current water gesture.");
        for(int i=0;i<8&&gameCast.Snapshot.PowerPermille<1000;i++)gameCast.Advance(gameCastInput.Gesture,250);
        Require(gameCast.Snapshot.PowerPermille==1000,"Controlled active charge did not reach full power.");
        BroadcastGameCast();
    }
    async Task WaterProbeRelease(ProbeWindow pane,int u,int v,long before,int flights)
    {
        await WaterProbeMouse(pane,u,v,false);
        await Wait(pane,s=>Field(s,"snapshot")!=null&&(string)Field(s,"snapshot")["Sequence"]==(before+1).ToString(CultureInfo.InvariantCulture));
        Require(gameFlightEmissions==flights+1&&game.Snapshot.State.Session!=null,"Browser release did not confirm one cast.");
    }
    async Task WaterProbeFullCast(ProbeWindow pane,int u,int v)
    {
        long before=game.Snapshot.State.Sequence;int flights=gameFlightEmissions;
        await WaterProbeBegin(pane,u,v);WaterProbeCharge();await WaterProbeRelease(pane,u,v,before,flights);
        Require(gameFlight.Snapshot.PowerPermille==1000,"Full charge was not used for the actual flight.");
    }
    async Task<FloatFlightSnapshot> WaterProbeLanding(ProbeWindow pane,bool full)
    {
        await GameWaitForWater(pane);var flight=gameFlight.Snapshot;
        var sample=gameWater.Sample(FlightMm(flight.Xmm),FlightMm(flight.Zmm),gameWater.Version);
        Require(sample.Accepted&&gameWaterLanding!=null&&gameWaterLanding.Accepted&&gameWaterLandingSession==flight.SessionId&&
            gameWaterLanding.Sample.Xmm==sample.Sample.Xmm&&gameWaterLanding.Sample.Zmm==sample.Sample.Zmm&&
            gameWaterLanding.Sample.DepthMm==sample.Sample.DepthMm&&sample.Sample.HookYmm==-200&&sample.Sample.BottomClearanceMm>=100,
            "Actual contact did not use the shared water depth and clearance.");
        if(full)Require(Math.Abs(flight.Xmm-gameWaterPlan.Target.Xmm)<.001&&Math.Abs(flight.Zmm-gameWaterPlan.Target.Zmm)<.001,
            "Full charge failed to reach the frozen aim.");
        await Wait(pane,s=>Field(s,"waterState")!=null&&Field(Field(s,"waterState"),"landing")!=null&&
            (bool)Field(Field(s,"waterState"),"landing")["accepted"]&&
            Number(Field(Field(s,"waterState"),"landing"),"xMm")==sample.Sample.Xmm&&
            Number(Field(Field(s,"waterState"),"landing"),"zMm")==sample.Sample.Zmm&&
            Number(Field(Field(s,"waterState"),"landing"),"depthMm")==sample.Sample.DepthMm);
        Require(game.Snapshot.State.Creel.Count==0,"Water contact awarded a fish.");return flight;
    }
    async Task WaterProbeRecall(ProbeWindow pane,string lease)
    {
        var intent=GameIntent("recall");intent["rigRevision"]=game.Snapshot.State.Rig.Revision.ToString(CultureInfo.InvariantCulture);
        GameCode(await GameWire(pane,lease,intent),"recall-candidate",true);
        Require(game.Snapshot.State.Session==null,"Recall retained the completed cast session.");
    }
    async void RunGameWaterProbe()
    {
        try{
            Require(GameWaterSelfTest,"Water probe requires the explicit private mode.");
            var read=windows[false];var pane=windows[true];await Ready(read);await Ready(pane);
            var page=await Wait(pane,s=>Field(s,"waterState")!=null&&(bool)Field(s,"waterState")["enabled"]);
            var initial=game.Snapshot.State;
            Require(initial.Sequence==1&&initial.EquipmentOffers!=null&&initial.BaitCount==12&&initial.Stamina==100&&gameWater!=null,
                "Water profile lacks its explicit replacement catalogue or initial resources.");
            string projection=json.Serialize(Field(page,"waterState"));
            Require(!projection.Contains("ProfileId")&&!projection.Contains("Receipts")&&!projection.Contains("Fingerprint")&&
                Number(Field(page,"waterState"),"geometryVersion")==gameWater.Version,"Water projection leaked private ownership or changed geometry.");
            await Wait(read,s=>Field(s,"waterState")!=null&&(bool)Field(s,"waterState")["enabled"]);
            checks.Add("both real pages receive one bounded immutable water and bed projection without private save history");

            string lease=await Activate(pane);string digest=GameShopJournalDigest();long high=gameViews[pane].CastHighWater;
            var missing=WaterProbeMessage(pane,lease,"begin",high+1);missing.Remove("aim");
            GameCode(await WaterProbeWire(pane,missing),"invalid-intent",false);
            var extra=WaterProbeMessage(pane,lease,"begin",high+1);((Dictionary<string,object>)extra["aim"])["depthMm"]=1000;
            GameCode(await WaterProbeWire(pane,extra),"invalid-intent",false);
            var numeric=WaterProbeMessage(pane,lease,"begin",high+1);((Dictionary<string,object>)numeric["aim"])["u"]=5000.5;
            GameCode(await WaterProbeWire(pane,numeric),"invalid-intent",false);
            GameCode(await WaterProbeWire(pane,WaterProbeMessage(pane,lease,"begin",high+1,-1)),"aim-out-of-range",false);
            GameCode(await WaterProbeWire(pane,WaterProbeMessage(pane,lease,"begin",high+1,5000,5000,2)),"geometry-version-mismatch",false);
            GameCode(await WaterProbeWire(read,WaterProbeMessage(read,lease,"begin",1)),"control-required",false);
            Require(GameShopJournalDigest()==digest&&gameViews[pane].CastHighWater==high&&gameFlightEmissions==0,"Invalid or observer aim consumed input or resources.");
            checks.Add("missing extra fractional out-of-range and stale-version aims plus observer input are rejected before consumption");

            await WaterProbeBegin(pane,5000,5000);WaterProbeCharge();long sequence=gameCastInput.Sequence;
            var geometry=gameWater;var release=WaterProbeMessage(pane,lease,"release",sequence);
            Receive(pane,PageUrl,json.Serialize(release));
            Require(gameCast.Snapshot.Phase==FloatCastPhase.Prepared,"Private ordered release was not prepared.");
            gameWater=FloatStillWaterGeometry.CreateDevelopment();
            try{
                await WaterProbeMouse(pane,5000,5000,false);await Task.Delay(130);
                Require(gameCast.Snapshot.Phase==FloatCastPhase.Cancelled&&game.Snapshot.State.Sequence==initial.Sequence&&gameFlightEmissions==0,
                    "Prepared cast ignored replacement of its frozen geometry reference.");
            }finally{gameWater=geometry;BroadcastGameWater();}
            Require(GameShopJournalDigest()==digest,"Geometry cancellation wrote a cast.");
            checks.Add("a controlled geometry replacement after preparation cancels queued submission without a debit or flight");

            await WaterProbeBegin(pane,3500,5000);sequence=gameCastInput.Sequence;string oldLease=lease;
            Revoke(pane);await Wait(pane,s=>s["lease"]==null);await WaterProbeMouse(pane,3500,5000,false);
            GameCode(await WaterProbeWire(pane,WaterProbeMessage(pane,oldLease,"release",sequence)),"control-required",false);
            Require(gameCast.Snapshot.Phase==FloatCastPhase.Cancelled&&game.Snapshot.State.Sequence==initial.Sequence&&gameFlightEmissions==0&&GameShopJournalDigest()==digest,
                "Revoked gesture launched or consumed resources.");
            lease=await Activate(pane);
            checks.Add("revoked browser pointer charge cannot be released by the old grant and reauthorization spends nothing");

            await WaterProbeBegin(pane,6500,5000);sequence=gameCastInput.Sequence;oldLease=lease;string oldDocument=pane.Ticket.DocumentId;
            Reload(pane);await Wait(pane,s=>(string)s["documentId"]!=oldDocument&&Field(s,"waterState")!=null);
            await WaterProbeMouse(pane,6500,5000,false);
            GameCode(await WaterProbeWire(pane,WaterProbeMessage(pane,oldLease,"release",sequence,5000,5000,1,oldDocument)),"control-required",false);
            Require(gameCast.Snapshot.Phase==FloatCastPhase.Cancelled&&game.Snapshot.State.Sequence==initial.Sequence&&gameFlightEmissions==0&&GameShopJournalDigest()==digest,
                "Reload resurrected an uncommitted aim.");
            lease=await Activate(pane);
            checks.Add("real document reload cancels a held aim and rejects the previous document without any cast payment");

            long before=game.Snapshot.State.Sequence;int flights=gameFlightEmissions;
            await WaterProbeBegin(pane,1250,4500);WaterProbeCharge();var frozenPlan=gameWaterPlan;sequence=gameCastInput.Sequence;
            var changedRelease=WaterProbeMessage(pane,lease,"release",sequence);changedRelease["aim"]=new {u=8750,v=1000,geometryVersion=1};
            GameCode(await WaterProbeWire(pane,changedRelease),"invalid-intent",false);
            Require(gameCast.Snapshot.Phase==FloatCastPhase.Charging&&Object.ReferenceEquals(frozenPlan,gameWaterPlan)&&game.Snapshot.State.Sequence==before,
                "Release payload secretly replaced the frozen target.");
            // Release over another place deliberately: only the original down
            // determines the aim; browser capture still pairs this physical up.
            await WaterProbeRelease(pane,8750,1000,before,flights);
            Require(Object.ReferenceEquals(frozenPlan,gameWaterPlan)&&gameFlight.Snapshot.PowerPermille==1000,"Browser release moved the aim or lost full power.");
            checks.Add("actual SVG browser input freezes a left aim while release over another location and extra release data cannot retarget it");

            var runtime=gameFlight;await GameMenu(pane,lease,true);var frozen=runtime.Snapshot;await Task.Delay(180);
            Require(runtime.Snapshot.ElapsedMilliseconds==frozen.ElapsedMilliseconds&&runtime.Snapshot.Xmm==frozen.Xmm&&!GameReadyForFish(),"Menu advanced the new water flight.");
            await GameMenu(pane,lease,false);Require(Object.ReferenceEquals(runtime,gameFlight),"Menu replaced the selected trajectory.");
            var left=await WaterProbeLanding(pane,true);await WaterProbeRecall(pane,lease);
            checks.Add("menu freezes the same real host flight before its left landing samples the shared bed without rewards");

            await WaterProbeFullCast(pane,8750,4500);var right=await WaterProbeLanding(pane,true);
            Require(left.Xmm<0&&right.Xmm>0&&Math.Abs(left.Xmm+right.Xmm)<.001&&Math.Abs(left.Zmm-right.Zmm)<.001,"Symmetric actual aim did not produce distinct mirrored landings.");
            await WaterProbeRecall(pane,lease);
            checks.Add("a separate right aim produces a mirrored real landing and recall closes each paid session");

            await WaterProbeFullCast(pane,5000,9000);var near=await WaterProbeLanding(pane,true);await WaterProbeRecall(pane,lease);
            await WaterProbeFullCast(pane,5000,1000);var far=await WaterProbeLanding(pane,true);
            Require(far.Zmm>near.Zmm+20000&&Math.Abs(far.Xmm)<.001&&Math.Abs(near.Xmm)<.001,"Near and far full charges did not follow their selected distances.");
            await WaterProbeRecall(pane,lease);
            checks.Add("near and far full charges reach different selected distances using one water definition");

            before=game.Snapshot.State.Sequence;flights=gameFlightEmissions;
            await WaterProbeMouse(pane,5000,1000,true);await WaterProbeRelease(pane,5000,1000,before,flights);
            var low=await WaterProbeLanding(pane,false);
            Require(low.PowerPermille<1000&&low.Zmm<far.Zmm&&Math.Abs(low.Xmm)<.001&&gameWaterPlan.U==5000&&gameWaterPlan.V==1000,
                "Quick browser release forced the distant target or changed its heading.");
            checks.Add("quick browser press and release lands closer at lower actual power along the same selected heading");

            await GameHookCurrent(pane,lease);var fight=gameFight;var start=fight.InitialFishPosition;var landing=gameWaterLanding.Sample;
            Require(Math.Abs(start.X-low.Xmm/1000.0)<1e-9&&Math.Abs(start.Z-low.Zmm/1000.0)<1e-9&&start.Y==landing.HookYmm/1000.0&&start.Y==-.2,
                "Naturally hooked fish did not start at the actual landing and shared hook depth.");
            var checkedDepth=gameWater.Sample(FlightMm(start.X*1000),FlightMm(start.Z*1000),gameWater.Version);
            Require(checkedDepth.Accepted&&checkedDepth.Sample.DepthMm==landing.DepthMm&&checkedDepth.Sample.BottomClearanceMm>=100,"Fight inherited an unrelated depth sample.");
            await GameMenu(pane,lease,true);long fightStep=fight.Snapshot.StepIndex;await Task.Delay(160);
            Require(fight.Snapshot.StepIndex==fightStep&&!fight.Snapshot.Reeling,"Menu continued the new geometry fight.");
            await GameMenu(pane,lease,false);
            checks.Add("one natural hook starts at actual landing coordinates and shared submerged depth while menu still pauses the fight");

            await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('water-aim-svg').scrollIntoView({block:'center'})");
            using(var preview=File.Create(Path.Combine(root,"game-water-panel.png")))await pane.View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,preview);
            await WaterProbeRecall(pane,lease);var final=game.Snapshot.State;
            Require(final.Sequence==11&&final.Session==null&&final.WalletMinor==12000&&final.BaitCount==7&&final.Stamina==90&&final.Creel.Count==0&&
                final.Equipment.Instances.Count==6&&final.Equipment.RemainingLineMm==50000&&final.Equipment.LostLineMm==0&&gameFlightEmissions==5,
                "Five aimed casts and recalls violated resource conservation.");
            await Wait(read,s=>Field(s,"snapshot")!=null&&(string)Field(s,"snapshot")["Sequence"]=="11");
            PrepareGameShutdown();Require(ExitCode==0&&gameShutdownCode=="no-active-session","Idle water test shutdown appended a recovery.");
            checks.Add("five casts and recalls preserve all equipment money and line while charging only five bait and ten stamina");

            File.WriteAllText(Path.Combine(root,"webview-summary.json"),json.Serialize(new{passed=true,checks=checks.ToArray(),gameLoaded=true,
                waterAimConnected=true,sharedWaterGeometry=true,controlledCharge=true,controlledGeometryReplacement=true,
                devToolsMouseInput=true,nativePhysicalMouse=false,nativeInputTested=false,pointerCaptureStubbed=false,scriptedPointerEvents=false,
                realHostFlight=true,naturalHookedTarget=true,controlledFishFacts=false,realDiskFailureTested=false,desktopLayer=false,personalSaveRead=false,
                schemaVersion=final.SchemaVersion,sequence=final.Sequence,value=final.WalletMinor,baitCount=final.BaitCount,stamina=final.Stamina,
                equipmentInstances=final.Equipment.Instances.Count,initialLineMm=final.Equipment.InitialLineMm,remainingLineMm=final.Equipment.RemainingLineMm,
                lostLineMm=final.Equipment.LostLineMm,gameFlightEmissions,geometryVersion=gameWater.Version,leftXmm=FlightMm(left.Xmm),rightXmm=FlightMm(right.Xmm),
                nearZmm=FlightMm(near.Zmm),farZmm=FlightMm(far.Zmm),lowPowerZmm=FlightMm(low.Zmm),lowPowerPermille=low.PowerPermille,
                landingDepthMm=landing.DepthMm,hookYmm=landing.HookYmm}),Encoding.UTF8);
            Shutdown();
        }catch(Exception error){Fail(error);}
    }
}
