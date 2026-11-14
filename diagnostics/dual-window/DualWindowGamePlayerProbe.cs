using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FishingDesktop.GameCore;
using Microsoft.Web.WebView2.Core;

sealed partial class DualWindowContext
{
    static bool PlayerProbeDrawn(Dictionary<string,object> page,FloatFishingWater station)
    {
        if(!AtlasProbeProjected(page,station.Id))return false;
        var snapshot=Field(page,"snapshot");var scene=Field(page,"sceneState");var renderer=SceneProbeRenderer(page);
        return snapshot!=null && !(bool)snapshot["MenuOpen"] && scene!=null && (string)scene["mode"]=="scene" &&
            renderer!=null && (bool)renderer["ready"] && (bool)renderer["running"] && renderer["error"]==null &&
            Number(renderer,"geometryVersion")==station.Geometry.Version && Number(renderer,"frames")>1 &&
            Number(renderer,"triangles")>0 && Number(renderer,"calls")>0;
    }

    async Task PlayerProbeTravel(ProbeWindow pane,string lease,FloatFishingWater station)
    {
        string journal=GameShopJournalDigest();
        await SceneProbeButton(pane,"scene-menu");
        await Wait(pane,s=>Field(s,"snapshot")!=null && (bool)Field(s,"snapshot")["MenuOpen"]);
        await SceneProbeButton(pane,"spot-select-"+station.Id);
        await Wait(pane,s=>AtlasProbeProjected(s,station.Id));
        Require(gameFishingWater==station && gameWater==station.Geometry && gameWaterLanding==null &&
            journal==GameShopJournalDigest(),"Travel changed game progress or reused the previous water.");
        await GameMenu(pane,lease,false);
        var page=await Wait(pane,s=>PlayerProbeDrawn(s,station),20000);
        long frames=Number(SceneProbeRenderer(page),"frames");
        await Wait(pane,s=>PlayerProbeDrawn(s,station) && Number(SceneProbeRenderer(s),"frames")>frames,15000);
    }

    async Task<Color[]> PlayerProbePicture(ProbeWindow pane,FloatFishingWater station,string name)
    {
        var displayed=await SceneProbeScript(pane,@"(()=>{
            const canvas=document.getElementById('scene-canvas'), r=canvas.getBoundingClientRect();
            const positions=[];
            for(let y=2;y<=10;y++)for(let x=1;x<15;x++){
                const px=r.left+r.width*x/15, py=r.top+r.height*y/13;
                if(document.elementFromPoint(px,py)===canvas)positions.push([px/innerWidth,py/innerHeight]);
            }
            return {label:document.getElementById('scene-spot-name').textContent,
                menu:document.body.dataset.sceneMenu, width:innerWidth,height:innerHeight,positions};
        })()");
        Require((string)displayed["label"]==station.Name && (string)displayed["menu"]=="closed" &&
            Number(displayed,"width")>Number(displayed,"height"),"Screenshot would show the wrong station, open menu or portrait layout.");
        var positions=json.Deserialize<double[][]>(json.Serialize(displayed["positions"]));
        Require(positions.Length>=65,"Too little game canvas is visible for a player preview.");
        string path=Path.Combine(root,name);
        using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            await pane.View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
        var palette=new HashSet<int>();
        using(var image=new Bitmap(path)){
            foreach(var p in positions){
                var c=image.GetPixel((int)(image.Width*p[0]),(int)(image.Height*p[1]));
                palette.Add(((c.R>>3)<<10)|((c.G>>3)<<5)|(c.B>>3));
            }
            Require(palette.Count>=4,"Game preview canvas contains too few distinct colors.");
            // Compare upper scene pixels, away from the title, buttons and
            // water effects, so a changed HUD cannot masquerade as scenery.
            var terrain=new List<Color>();
            for(int y=25;y<=60;y+=5)for(int x=8;x<=92;x+=4)
                terrain.Add(image.GetPixel(image.Width*x/100,image.Height*y/100));
            return terrain.ToArray();
        }
    }

    static bool PlayerProbeDifferent(Color[] a,Color[] b)
    {
        if(a.Length!=b.Length)return false;
        int changed=0;
        for(int i=0;i<a.Length;i++){
            int variation=Math.Abs(a[i].R-b[i].R)+Math.Abs(a[i].G-b[i].G)+Math.Abs(a[i].B-b[i].B);
            if(variation>=32)changed++;
        }
        return changed>=a.Length/12;
    }

    async void RunGamePlayerProbe()
    {
        try{
            Require(GamePlayerSelfTest && windows.Count==1 && windows.ContainsKey(true) && !windows.ContainsKey(false),
                "Single-window player diagnostic unexpectedly opened another pane.");
            var pane=windows[true];await Ready(pane);
            var initial=game.Snapshot.State;var lake=FloatFishingAtlas.Find("development-still-water");
            var river=FloatFishingAtlas.Find("river-bend");var coast=FloatFishingAtlas.Find("coastal-bay");
            Require(!pane.DesktopReadOnly && initial.Sequence==1 && initial.Rules.Species.Count==7 && initial.WalletMinor==12000 &&
                initial.BaitCount==12 && initial.Stamina==100 && initial.Equipment.Instances.Count==6 && initial.Session==null,
                "Single player did not start from its new private seven-species profile.");
            // The hidden diagnostic window uses a fixed landscape preview size.
            pane.ClientSize=new Size(1440,900);
            string lease=await Activate(pane);
            await Wait(pane,s=>PlayerProbeDrawn(s,lake),30000);
            checks.Add("the standalone player starts at the lake in a single hidden interaction pane with all seven sale rules");

            await PlayerProbeTravel(pane,lease,river);
            var riverPixels=await PlayerProbePicture(pane,river,"player-river.png");
            checks.Add("a real lake menu click travels to the river and produces a menu-free landscape preview with its river title");

            await PlayerProbeTravel(pane,lease,lake);
            var lakePixels=await PlayerProbePicture(pane,lake,"player-lake.png");
            checks.Add("the river menu returns to the original lake and its current on-screen station agrees with the owner");

            await PlayerProbeTravel(pane,lease,coast);
            var coastPixels=await PlayerProbePicture(pane,coast,"player-coast.png");
            Require(PlayerProbeDifferent(lakePixels,riverPixels) && PlayerProbeDifferent(lakePixels,coastPixels) &&
                PlayerProbeDifferent(riverPixels,coastPixels),"Three presentations do not show distinguishable scenery outside the HUD.");
            checks.Add("all three clean player screenshots contain distinguishable scenery independently of their HUD names");

            var aimPoint=await SceneProbePoint(pane);
            await SceneProbeMouse(pane,aimPoint,true);
            var charging=await Wait(pane,s=>PlayerProbeDrawn(s,coast) && (string)SceneProbeRenderer(s)["rodPhase"]=="charging" &&
                Field(Field(s,"inputState"),"aim")!=null && gameCast.Snapshot.PowerPermille==1000,20000);
            var aim=Field(Field(charging,"inputState"),"aim");
            Require(Number(aim,"geometryVersion")==coast.Geometry.Version && gameWaterPlan.U==Number(aimPoint,"u") &&
                gameWaterPlan.V==Number(aimPoint,"v") && Number(aim,"u")==gameWaterPlan.U && Number(aim,"v")==gameWaterPlan.V &&
                game.Snapshot.State.Sequence==1 && gameFlightEmissions==0,"Coast ray did not freeze the chosen hosted cast.");
            await SceneProbeMouse(pane,aimPoint,false);
            await Wait(pane,s=>game.Snapshot.State.Sequence==2 && game.Snapshot.State.Session!=null && gameFlightEmissions==1);
            var flight=await WaterProbeLanding(pane,true);
            var landing=gameWaterLanding.Sample;
            await Wait(pane,s=>PlayerProbeDrawn(s,coast) && (string)SceneProbeRenderer(s)["rodPhase"]=="waiting" &&
                (bool)SceneProbeRenderer(s)["bobberVisible"] && (bool)SceneProbeRenderer(s)["lineVisible"]);
            Require(landing.WaterId==coast.Id && landing.GeometryVersion==coast.Geometry.Version && flight.PowerPermille==1000 &&
                game.Snapshot.State.Creel.Count==0,"Coast release resolved in the wrong water or awarded a fish.");
            checks.Add("the sea camera receives a real browser hold and release, sends one full cast and displays its actual saltwater contact");

            await SceneProbeButton(pane,"scene-recall");
            await Wait(pane,s=>Field(s,"snapshot")!=null && Number(Field(s,"snapshot"),"Sequence")==3 && game.Snapshot.State.Session==null);
            checks.Add("the visible coast HUD reel-in control closes the single paid cast without awarding a fish");

            string oldDocument=pane.Ticket.DocumentId, journal=GameShopJournalDigest();
            Reload(pane);
            var reopened=await Wait(pane,s=>(string)s["documentId"]!=oldDocument && s["lease"]==null && PlayerProbeDrawn(s,coast),30000);
            Require(AtlasProbeProjected(reopened,coast.Id) && gameFishingWater==coast && gameWater==coast.Geometry &&
                GameShopJournalDigest()==journal && windows.Count==1 && !windows.ContainsKey(false),"Reload reset the current station or game resources.");
            checks.Add("reloading the player page keeps the same coast inside the running host and requires a fresh interaction grant");

            var final=game.Snapshot.State;
            Require(final.SchemaVersion==3 && final.Sequence==3 && final.WalletMinor==12000 && final.BaitCount==11 && final.Stamina==98 &&
                final.Equipment.Instances.Count==6 && final.Equipment.RemainingLineMm==50000 && final.Equipment.LostLineMm==0 &&
                final.Creel.Count==0 && final.Session==null && gameFlightEmissions==1,"Single-window resource conservation failed.");
            PrepareGameShutdown();Require(ExitCode==0 && gameShutdownCode=="no-active-session","Player shutdown attempted an extra recovery.");
            checks.Add("three station changes and a document reload cost nothing while one cast spends one bait and two stamina and exits cleanly");
            File.WriteAllText(Path.Combine(root,"webview-summary.json"),json.Serialize(new {
                passed=true,checks=checks.ToArray(),gameLoaded=true,playerWindowConnected=true,singleInteractionWindow=true,
                observerOpened=false,desktopLayer=false,personalSaveRead=false,playerPreviewCount=3,distinctScenery=true,
                mapCount=3,speciesCount=final.Rules.Species.Count,coastalCast=true,reloadRetainedStation=true,
                devToolsMouseInput=true,nativePhysicalMouse=false,controlledCharge=false,realHostFlight=true,
                schemaVersion=final.SchemaVersion,sequence=final.Sequence,value=final.WalletMinor,baitCount=final.BaitCount,stamina=final.Stamina,
                equipmentInstances=final.Equipment.Instances.Count,remainingLineMm=final.Equipment.RemainingLineMm,lostLineMm=final.Equipment.LostLineMm,
                gameFlightEmissions,landingXmm=landing.Xmm,landingZmm=landing.Zmm,landingDepthMm=landing.DepthMm,shutdown=gameShutdownCode}),Encoding.UTF8);
            Shutdown();
        }catch(Exception error){Fail(error);}
    }
}
