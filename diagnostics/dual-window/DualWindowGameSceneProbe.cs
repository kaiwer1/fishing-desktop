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
    static Dictionary<string,object> SceneProbeRenderer(Dictionary<string,object> page)
    {
        return Field(Field(page,"sceneState"),"renderer");
    }

    static bool SceneProbeDrawn(Dictionary<string,object> page)
    {
        var scene=Field(page,"sceneState");
        var renderer=SceneProbeRenderer(page);
        return scene!=null && (string)scene["mode"]=="scene" && renderer!=null &&
            (bool)renderer["ready"] && Number(renderer,"frames")>=1 && Number(renderer,"geometryVersion")==1 &&
            Number(renderer,"width")>0 && Number(renderer,"height")>0 &&
            Number(renderer,"triangles")>0 && Number(renderer,"calls")>0 && renderer["error"]==null;
    }

    async Task<Dictionary<string,object>> SceneProbeScript(ProbeWindow pane,string script)
    {
        string raw=await pane.View.CoreWebView2.ExecuteScriptAsync(script);
        var result=json.Deserialize<Dictionary<string,object>>(raw);
        Require(result!=null,"Game scene page returned no observation.");
        return result;
    }

    async Task<Dictionary<string,object>> SceneProbePoint(ProbeWindow pane)
    {
        // Quantize to a CSS pixel first. Browser mouse events use this pixel;
        // the expected target is independently obtained through the camera ray.
        return await SceneProbeScript(pane,@"(()=>{
            const canvas=document.getElementById('scene-canvas');
            canvas.scrollIntoView({block:'center',inline:'nearest'});
            for(const [u,v] of [[6500,6500],[4000,7000],[5000,5000],[5500,8500]]) {
                const p=dualProbe.projectSceneAim(u,v); if(!p)continue;
                const x=Math.round(p.clientX), y=Math.round(p.clientY);
                if(document.elementFromPoint(x,y)!==canvas)continue;
                const aim=dualProbe.pickSceneAim(x,y);
                if(aim)return {x,y,u:aim.u,v:aim.v,geometryVersion:aim.geometryVersion};
            }
            return null;
        })()");
    }

    async Task SceneProbeMouse(ProbeWindow pane,Dictionary<string,object> point,bool down)
    {
        await pane.View.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",json.Serialize(new {
            type=down?"mousePressed":"mouseReleased",x=Number(point,"x"),y=Number(point,"y"),
            button="left",buttons=down?1:0,clickCount=1}));
    }

    async Task SceneProbeButton(ProbeWindow pane,string id)
    {
        var point=await SceneProbeScript(pane,@"(()=>{
            const b=document.getElementById("+json.Serialize(id)+@");
            b.scrollIntoView({block:'center',inline:'nearest'});
            const r=b.getBoundingClientRect(), x=Math.round(r.left+r.width/2), y=Math.round(r.top+r.height/2);
            return !b.disabled && b.contains(document.elementFromPoint(x,y)) ? {x,y} : null;
        })()");
        await SceneProbeMouse(pane,point,true);
        await SceneProbeMouse(pane,point,false);
    }

    async Task<int> SceneProbeCapture(ProbeWindow pane)
    {
        // Sample only pixels where browser hit-testing found the actual canvas.
        // Exclude overlaid controls and scale viewport coordinates to the PNG.
        string raw=await pane.View.CoreWebView2.ExecuteScriptAsync(@"(()=>{
            const canvas=document.getElementById('scene-canvas');
            canvas.scrollIntoView({block:'center',inline:'nearest'});
            const r=canvas.getBoundingClientRect(), pts=[];
            for(let row=1;row<12;row++)for(let col=1;col<16;col++){
                const x=Math.round(r.left+r.width*col/16), y=Math.round(r.top+r.height*row/12);
                if(document.elementFromPoint(x,y)===canvas)pts.push([x/innerWidth,y/innerHeight]);
            }
            return pts;
        })()");
        var points=json.Deserialize<double[][]>(raw);
        Require(points!=null && points.Length>=16,"Too few visible canvas samples in the game viewport.");
        string path=Path.Combine(root,"game-scene-panel.png");
        using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            await pane.View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
        var colors=new HashSet<int>();
        using(var bitmap=new Bitmap(path)){
            foreach(var p in points){
                int x=Math.Min(bitmap.Width-1,(int)(p[0]*bitmap.Width));
                int y=Math.Min(bitmap.Height-1,(int)(p[1]*bitmap.Height));
                var color=bitmap.GetPixel(x,y);
                colors.Add(((color.R>>3)<<10)|((color.G>>3)<<5)|(color.B>>3));
            }
        }
        Require(colors.Count>=4,"Captured game canvas did not contain a range of rendered colors.");
        return colors.Count;
    }

    async void RunGameSceneProbe()
    {
        try{
            Require(GameSceneSelfTest,"Game scene probe requires its explicit private mode.");
            var read=windows[false];var pane=windows[true];
            await Ready(read);await Ready(pane);
            var initial=game.Snapshot.State;
            Require(initial.Sequence==1 && initial.WalletMinor==12000 && initial.BaitCount==12 && initial.Stamina==100 &&
                initial.EquipmentOffers!=null && initial.Equipment.Instances.Count==6 &&
                initial.Equipment.RemainingLineMm==50000 && initial.Session==null,"Unexpected private scene profile.");
            checks.Add("the private scene profile starts with the original six parts and fifty metres of line");

            var controlPage=await Wait(pane,SceneProbeDrawn,20000);
            var readPage=await Wait(read,SceneProbeDrawn,20000);
            long triangles=Number(SceneProbeRenderer(controlPage),"triangles");
            Require(readPage["lease"]==null && Number(SceneProbeRenderer(readPage),"triangles")>0,"Observer scene projection did not draw.");
            var gl=await SceneProbeScript(pane,@"(()=>{
                const canvas=document.getElementById('scene-canvas');
                const gl=canvas.getContext('webgl2'); if(!gl || gl.isContextLost())return null;
                const info=gl.getExtension('WEBGL_debug_renderer_info');
                return {version:gl.getParameter(gl.VERSION),renderer:gl.getParameter(info?info.UNMASKED_RENDERER_WEBGL:gl.RENDERER)};
            })()");
            string glVersion=(string)gl["version"], rendererName=(string)gl["renderer"];
            Require(glVersion.StartsWith("WebGL 2",StringComparison.Ordinal) && !String.IsNullOrWhiteSpace(rendererName),
                "Actual game canvas did not report WebGL2 and a renderer.");
            checks.Add("both game panes draw geometry and the interaction canvas exposes its live WebGL2 renderer");

            string lease=await Activate(pane);string journal=GameShopJournalDigest();
            var observerPoint=await SceneProbePoint(read);
            await SceneProbeMouse(read,observerPoint,true);await SceneProbeMouse(read,observerPoint,false);
            GameCode(await WaterProbeWire(read,WaterProbeMessage(read,lease,"begin",1)),"control-required",false);
            Require(gameCast.Snapshot.Phase!=FloatCastPhase.Charging && gameFlightEmissions==0 &&
                gameViews[read].CastHighWater==0 && GameShopJournalDigest()==journal,"Observer scene input reached the game owner.");
            checks.Add("observer camera picking, real browser clicks and a forwarded cast request cannot create a paid gesture");

            await SceneProbeButton(pane,"scene-to-map");
            await Wait(pane,s=>{
                var scene=Field(s,"sceneState");var renderer=SceneProbeRenderer(s);
                return scene!=null && (string)scene["mode"]=="map" && (renderer==null || !(bool)renderer["running"]);
            });
            await SceneProbeButton(pane,"scene-to-water");
            await Wait(pane,SceneProbeDrawn,15000);
            Require(GameShopJournalDigest()==journal && gameFlightEmissions==0,"Changing scene views performed a game action.");
            checks.Add("switching from the shore to the overhead map stops drawing and returning resumes the same private game");

            var point=await SceneProbePoint(pane);
            await SceneProbeMouse(pane,point,true);
            var held=await Wait(pane,s=>SceneProbeDrawn(s) &&
                (string)SceneProbeRenderer(s)["rodPhase"]=="charging" && Field(Field(s,"inputState"),"aim")!=null &&
                gameCast.Snapshot.Phase==FloatCastPhase.Charging && gameCast.Snapshot.PowerPermille==1000,12000);
            var aim=Field(Field(held,"inputState"),"aim");
            Require(Number(aim,"u")==Number(point,"u") && Number(aim,"v")==Number(point,"v") &&
                Number(aim,"geometryVersion")==gameWater.Version && gameWaterPlan.U==Number(aim,"u") && gameWaterPlan.V==Number(aim,"v") &&
                gameFlightEmissions==0 && GameShopJournalDigest()==journal,"Camera pick or held real-time gesture changed the frozen target.");
            checks.Add("holding the browser mouse on visible lake pixels charges the rod to full power against the host clock and freezes its picked target");

            await SceneProbeMouse(pane,point,false);
            await Wait(pane,s=>gameFlightEmissions==1 && game.Snapshot.State.Sequence==2 && game.Snapshot.State.Session!=null);
            var landed=await WaterProbeLanding(pane,true);
            var contact=gameWaterLanding.Sample;
            Func<Dictionary<string,object>,bool> waiting=s=>{
                var renderer=SceneProbeRenderer(s);var landing=Field(Field(s,"waterState"),"landing");
                return SceneProbeDrawn(s) && (string)renderer["rodPhase"]=="waiting" && (bool)renderer["bobberVisible"] &&
                    (bool)renderer["lineVisible"] && landing!=null && (bool)landing["accepted"] &&
                    Number(landing,"xMm")==contact.Xmm && Number(landing,"zMm")==contact.Zmm && Number(landing,"depthMm")==contact.DepthMm;
            };
            await Wait(pane,waiting,10000);await Wait(read,waiting,10000);
            Require(landed.PowerPermille==1000 && gameFlightEmissions==1 && game.Snapshot.State.Creel.Count==0,
                "One full release did not produce a single natural cast.");
            checks.Add("one paid cast lands at its picked world location and both real pages show the same float, line and host-sampled water depth");

            int colors=await SceneProbeCapture(pane);
            checks.Add("the saved viewport image contains nonuniform colors sampled only from hit-tested game canvas pixels");

            await SceneProbeButton(pane,"scene-recall");
            await Wait(pane,s=>Field(s,"snapshot")!=null && Number(Field(s,"snapshot"),"Sequence")==3 &&
                game.Snapshot.State.Session==null);
            string recalled=GameShopJournalDigest();
            string loss=await pane.View.CoreWebView2.ExecuteScriptAsync(@"(()=>{
                const gl=document.getElementById('scene-canvas').getContext('webgl2');
                const ext=gl && gl.getExtension('WEBGL_lose_context');
                if(!ext)return false; ext.loseContext(); return true;
            })()");
            Require(loss=="true","Owned game canvas did not support controlled WebGL context loss.");
            await Wait(pane,s=>{
                var scene=Field(s,"sceneState");
                return scene!=null && (string)scene["mode"]=="map" && scene["fault"]!=null;
            });
            await Wait(read,SceneProbeDrawn);
            Require(GameShopJournalDigest()==recalled,"Render failure wrote to the private profile.");
            checks.Add("losing the owned interaction WebGL context falls back to the overhead map while the observer keeps its scene and the journal stays intact");

            await WaterProbeMouse(pane,5000,5000,true);
            await Wait(pane,s=>Field(Field(s,"inputState"),"aim")!=null && gameCast.Snapshot.Phase==FloatCastPhase.Charging);
            Revoke(pane);await Wait(pane,s=>s["lease"]==null && gameCast.Snapshot.Phase==FloatCastPhase.Cancelled);
            await WaterProbeMouse(pane,5000,5000,false);
            Require(GameShopJournalDigest()==recalled && gameFlightEmissions==1,"Fallback aiming or cancellation charged another cast.");
            checks.Add("the fallback map accepts a real aiming press and revoking control cancels it before any additional debit");

            var final=game.Snapshot.State;
            Require(final.SchemaVersion==3 && final.Sequence==3 && final.Session==null && final.Creel.Count==0 &&
                final.WalletMinor==12000 && final.BaitCount==11 && final.Stamina==98 && final.Equipment.Instances.Count==6 &&
                final.Equipment.RemainingLineMm==50000 && final.Equipment.LostLineMm==0 && gameFlightEmissions==1,
                "Scene lifetime changed final game resources.");
            await Wait(read,s=>Field(s,"snapshot")!=null && Number(Field(s,"snapshot"),"Sequence")==final.Sequence);
            PrepareGameShutdown();Require(ExitCode==0 && gameShutdownCode=="no-active-session","Closing the idle game scene performed recovery.");
            checks.Add("one cast and recall consume one bait and two stamina with all equipment conserved and no recovery on exit");
            string rendererLower=rendererName.ToLowerInvariant();
            bool softwareRenderer=rendererLower.Contains("swiftshader") || rendererLower.Contains("llvmpipe") ||
                rendererLower.Contains("software") || rendererLower.Contains("basic render driver") || rendererLower.Contains("warp");
            File.WriteAllText(Path.Combine(root,"webview-summary.json"),json.Serialize(new {
                passed=true,checks=checks.ToArray(),gameLoaded=true,gameSceneConnected=true,sceneKind="webgl3d",actualWebGL=true,
                glVersion,rendererName,softwareRendererObserved=softwareRenderer,triangles,sampleColors=colors,
                devToolsMouseInput=true,nativePhysicalMouse=false,controlledCharge=false,realHostFlight=true,
                observerReadOnly=true,controlledContextLoss=true,fallbackAimVerified=true,desktopLayer=false,personalSaveRead=false,
                schemaVersion=final.SchemaVersion,sequence=final.Sequence,value=final.WalletMinor,baitCount=final.BaitCount,
                stamina=final.Stamina,equipmentInstances=final.Equipment.Instances.Count,remainingLineMm=final.Equipment.RemainingLineMm,
                lostLineMm=final.Equipment.LostLineMm,gameFlightEmissions,landingXmm=contact.Xmm,landingZmm=contact.Zmm,
                landingDepthMm=contact.DepthMm,shutdown=gameShutdownCode}),Encoding.UTF8);
            Shutdown();
        }catch(Exception error){Fail(error);}
    }
}
