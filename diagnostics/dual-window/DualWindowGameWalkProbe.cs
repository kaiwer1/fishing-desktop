using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    bool GameWalkSelfTest {get{return gameMode=="walk-self-test";}}

    async Task WalkProbeKey(ProbeWindow pane,string key,string code,int vk,bool down,bool shift=false)
    {
        await pane.View.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",json.Serialize(new {
            type=down?"rawKeyDown":"keyUp",key,code,windowsVirtualKeyCode=vk,modifiers=shift?8:0}));
    }
    bool WalkProbeStopped(Dictionary<string,object> page)
    {
        var bank=Field(page,"bankState");var renderer=SceneProbeRenderer(page);
        return bank!=null && renderer!=null && !(bool)page["bankPending"] && !GameWalkMoving && Number(bank,"direction")==0 &&
            Number(bank,"xMm")==gameWalk.Xmm && Number(bank,"poseRevision")==gameWalk.PoseRevision &&
            Number(renderer,"cameraXmm")==gameWalk.Xmm && Number(renderer,"rodAnchorXmm")==gameWalk.Xmm;
    }
    Dictionary<string,object> WalkProbeMessage(ProbeWindow pane,string lease,long sequence,int direction)
    {
        return new Dictionary<string,object>{
            {"kind","game-bank-walk"},{"documentId",pane.Ticket.DocumentId},{"lease",lease},{"transportId",Guid.NewGuid().ToString("D")},
            {"inputSequence",sequence.ToString(CultureInfo.InvariantCulture)},{"direction",direction},{"fast",false},{"geometryVersion",gameWater.Version}};
    }
    async void RunGameWalkProbe()
    {
        try{
            Require(GameWalkSelfTest && windows.Count==1 && windows.ContainsKey(true),"Walk test requires one private player window.");
            var pane=windows[true];await Ready(pane);pane.ClientSize=new Size(1280,800);
            string lease=await Activate(pane),digest=GameShopJournalDigest();var binding=gameViews[pane];
            var lake=FloatFishingAtlas.Find("development-still-water");var coast=FloatFishingAtlas.Find("coastal-bay");
            var first=await Wait(pane,p=>PlayerProbeDrawn(p,lake) && WalkProbeStopped(p),30000);
            long initialBuilds=Number(SceneProbeRenderer(first),"sceneBuilds");
            Require(gameWalk.Xmm==0 && gameWalk.PoseRevision==0 && gameWater==lake.Geometry,"Private lake did not begin at its published anchor.");
            checks.Add("the single player begins at its published lake anchor with one live WebGL scene");

            await WalkProbeKey(pane,"d","KeyD",68,true);
            await Wait(pane,p=>gameWalk.Xmm>=500 && gameWalk.Direction==1 && !gameWalk.Fast);
            await WalkProbeKey(pane,"Shift","ShiftLeft",16,true,true);
            await Wait(pane,p=>gameWalk.Fast && gameWalk.Direction==1);
            await Wait(pane,p=>gameWalk.Xmm==gameWalk.MaximumXmm,15000);
            var busy=WaterProbeMessage(pane,lease,"begin",binding.CastHighWater+1);
            ((Dictionary<string,object>)busy["aim"])["poseRevision"]=gameWalk.PoseRevision;
            GameCode(await WaterProbeWire(pane,busy),"walk-busy",false);
            await WalkProbeKey(pane,"D","KeyD",68,false,true);await WalkProbeKey(pane,"Shift","ShiftLeft",16,false);
            var end=await Wait(pane,WalkProbeStopped);
            Require(gameWalk.Xmm>=900 && Number(SceneProbeRenderer(end),"sceneBuilds")==initialBuilds,"Walking rebuilt the scene or failed to change its camera.");
            checks.Add("DevTools D and Shift drive normal and fast host-clock movement, the moving cast is refused and release settles camera and rod");

            int stopped=gameWalk.Xmm,revision=gameWalk.PoseRevision;
            await Task.Delay(175);
            busy["transportId"]=Guid.NewGuid().ToString("D");
            GameCode(await WaterProbeWire(pane,busy),"stale-input",false);
            var stale=WaterProbeMessage(pane,lease,"begin",binding.CastHighWater+1);
            ((Dictionary<string,object>)stale["aim"])["poseRevision"]=revision-1;
            GameCode(await WaterProbeWire(pane,stale),"walk-pose-stale",false);
            GameCode(await WaterProbeWire(pane,WalkProbeMessage(pane,lease,binding.WalkHighWater,1)),"stale-input",false);
            Require(gameWalk.Xmm==stopped && gameWalk.PoseRevision==revision && GameShopJournalDigest()==digest,"Release drifted or replay spent resources.");
            checks.Add("the stopped pose does not drift and stale pose and walk sequence cannot spend bait or resume a previous direction");

            await WalkProbeKey(pane,"a","KeyA",65,true);
            await Wait(pane,p=>gameWalk.Direction==-1 && gameWalk.Xmm<=stopped-150);
            await SceneProbeButton(pane,"scene-menu");
            await Wait(pane,p=>game.Snapshot.MenuOpen && !GameWalkMoving);
            int menuX=gameWalk.Xmm;await WalkProbeKey(pane,"a","KeyA",65,false);
            await Task.Delay(150);Require(gameWalk.Xmm==menuX,"Open menu did not stop the walk.");
            await GameMenu(pane,lease,false);
            await Wait(pane,p=>PlayerProbeDrawn(p,lake) && WalkProbeStopped(p));
            checks.Add("a real menu click while A is held stops the player at the current bank coordinate until a new key press");

            await WalkProbeKey(pane,"d","KeyD",68,true);await Wait(pane,p=>gameWalk.Direction==1);
            // Simulate only the page's focus-loss notification. Do not focus
            // another application or claim this is a physical Alt-Tab test.
            await pane.View.CoreWebView2.ExecuteScriptAsync("window.dispatchEvent(new Event('blur'))");
            await Wait(pane,p=>p["lease"]==null && !GameWalkMoving);
            int blurred=gameWalk.Xmm;await WalkProbeKey(pane,"d","KeyD",68,false);
            GameCode(await WaterProbeWire(pane,WalkProbeMessage(pane,lease,1000,1)),"control-required",false);
            await Task.Delay(150);Require(gameWalk.Xmm==blurred,"Old grant retained movement after focus loss.");
            lease=await Activate(pane);
            checks.Add("a controlled page blur stops held motion, rejects the stale lease, and requires a new explicit grant");

            await PlayerProbeTravel(pane,lease,coast);
            var coastPage=await Wait(pane,p=>PlayerProbeDrawn(p,coast) && WalkProbeStopped(p));
            Require(gameWalk.Xmm==0 && gameWalk.PoseRevision>revision && GameShopJournalDigest()==digest,"Travel did not reset the bank without a journal write.");
            long coastBuilds=Number(SceneProbeRenderer(coastPage),"sceneBuilds");
            await WalkProbeKey(pane,"a","KeyA",65,true);
            await Wait(pane,p=>gameWalk.Xmm<=-650);
            await WalkProbeKey(pane,"a","KeyA",65,false);
            var coastStop=await Wait(pane,WalkProbeStopped);
            int anchor=gameWalk.Xmm,castPose=gameWalk.PoseRevision;
            Require(coastBuilds==Number(SceneProbeRenderer(coastStop),"sceneBuilds") && gameWater!=coast.Geometry &&
                gameWater.AimMinimumXmm==coast.Geometry.AimMinimumXmm && gameWater.MinimumZmm==coast.Geometry.MinimumZmm,"Walking moved the water bounds or rebuilt the coast.");
            checks.Add("a new coast starts at center, then its camera and rod move west while the scene and water coordinates stay fixed");

            var point=await SceneProbePoint(pane);await SceneProbeMouse(pane,point,true);
            await Wait(pane,p=>gameCast.Snapshot.Phase==FloatCastPhase.Charging && gameCast.Snapshot.PowerPermille==1000);
            Require(gameWaterPlan!=null && gameWaterPlan.AnchorXmm==anchor && gameWaterPlan.Geometry==gameWalk.Geometry,"Cast did not freeze the moved anchor.");
            GameCode(await WaterProbeWire(pane,WalkProbeMessage(pane,lease,binding.WalkHighWater+1,1)),"walk-busy",false);
            await SceneProbeMouse(pane,point,false);
            var flight=await WaterProbeLanding(pane,true);
            var landing=gameWaterLanding.Sample;
            Require(gameFlightEmissions==1 && gameFlightOrigin.Xmm==anchor && gameWalk.Xmm==anchor && gameWalk.PoseRevision==castPose &&
                flight.Phase==FloatFlightPhase.Waiting && coast.Geometry.Sample(landing.Xmm,landing.Zmm,coast.Geometry.Version).Sample.DepthMm==landing.DepthMm,
                "Actual coastal contact or frozen bank pose disagrees with the public water.");
            checks.Add("real full charge and release launch from the moved coastal anchor, reject movement while charging and land on the original seabed");

            GameCode(await WaterProbeWire(pane,WalkProbeMessage(pane,lease,binding.WalkHighWater+1,-1)),"walk-busy",false);
            await SceneProbeButton(pane,"scene-recall");
            await Wait(pane,p=>game.Snapshot.State.Session==null && game.Snapshot.State.Sequence==3);
            Require(gameWalk.PoseRevision==castPose,"Active or recalled line displaced the bank pose.");
            checks.Add("an active rod blocks walking, and the visible recall control settles precisely one paid cast");

            var final=game.Snapshot.State;
            Require(final.Sequence==3 && final.WalletMinor==12000 && final.BaitCount==11 && final.Stamina==98 && final.Creel.Count==0 &&
                final.Equipment.RemainingLineMm==50000 && gameFlightEmissions==1,"Unexpected walking or casting cost.");
            PrepareGameShutdown();Require(gameShutdownCode=="no-active-session" && ExitCode==0,"Walk profile did not close normally.");
            checks.Add("all walks and travel are free while the single cast consumes one bait and two stamina and closes normally");
            File.WriteAllText(Path.Combine(root,"webview-summary.json"),json.Serialize(new {
                passed=true,checks=checks.ToArray(),gameLoaded=true,walkConnected=true,devToolsKeyboardInput=true,devToolsMouseInput=true,
                controlledBlur=true,nativeAltTabTested=false,realHostClock=true,stalePoseRejected=true,heldWalkBlocksCast=true,
                singleInteractionWindow=true,personalSaveRead=false,desktopLayer=false,coastalCast=true,anchorXmm=anchor,poseRevision=castPose,
                landingXmm=landing.Xmm,landingZmm=landing.Zmm,landingDepthMm=landing.DepthMm,sequence=final.Sequence,value=final.WalletMinor,
                baitCount=final.BaitCount,stamina=final.Stamina,remainingLineMm=final.Equipment.RemainingLineMm,gameFlightEmissions,shutdown=gameShutdownCode}),Encoding.UTF8);
            Shutdown();
        }catch(Exception error){Fail(error);}
    }
}
