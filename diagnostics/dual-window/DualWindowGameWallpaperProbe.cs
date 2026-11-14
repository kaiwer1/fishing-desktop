using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using FishingDesktop.Diagnostics.DesktopLayer;
using FishingDesktop.Diagnostics.HostPolicy;
using FishingDesktop.GameCore;
using Microsoft.Web.WebView2.Core;

sealed partial class DualWindowContext
{
    bool GameWallpaperPrepareSelfTest {get{return gameMode=="wallpaper-prepare-self-test";}}
    bool GameWallpaperSelfTest {get{return gameMode=="wallpaper-self-test" || GameWallpaperPrepareSelfTest;}}
    bool wallpaperInitialLayerAvailable,wallpaperPreparedLayer;
    int WallpaperMessageAttempts {get{return shellLayerRequest==null?0:shellLayerRequest.MessageAttempts;}}

    void WallpaperProbeBlocked(string reason,EnvironmentSample sample,int nativeError=0)
    {
        var report=new {passed=false,blocked=true,reason,checks=checks.ToArray(),gameLoaded=true,
            environmentKnown=sample!=null&&sample.Known,sessionInteractive=sample!=null&&sample.InteractiveSession,
            fullscreenObserved=sample!=null&&sample.Fullscreen,environmentReason=sample==null?null:sample.Reason,
            attachmentCountAtStop=desktopAttachments,observerWasAttached=desktopAttachments>0,nativeError,
            prepareEnabled=GameWallpaperPrepareSelfTest,initialLayerAvailable=wallpaperInitialLayerAvailable,
            preparedMissingLayer=wallpaperPreparedLayer,prepareCalls=startupCalls,requestCalls=startupRequestCalls,messageAttempts=WallpaperMessageAttempts,
            passiveExistingLayer=WallpaperMessageAttempts==0,systemWallpaperPreferenceChanged=false,
            wallpaperAccepted=false,personalSaveRead=false,nativeInputTested=false,desktopCompositionCaptured=false};
        File.WriteAllText(Path.Combine(root,"webview-summary.json"),json.Serialize(report),Encoding.UTF8);
        File.WriteAllText(Path.Combine(root,"wallpaper-environment.json"),json.Serialize(report),Encoding.UTF8);
        Shutdown();
    }

    // First discover passively. Only the explicit prepare mode can request
    // an absent shell layer through the same one-shot coordinator as the host.
    bool GameWallpaperEnvironmentReady()
    {
        Require(GameWallpaperSelfTest && desktopMode && automated && !PlayerMode && startupPreparation==GameWallpaperPrepareSelfTest,
            "Wallpaper diagnostics require private state and the selected shell preparation mode.");
        var screen=Screen.PrimaryScreen;
        if(screen==null){WallpaperProbeBlocked("target-screen-unavailable",null);return false;}
        var b=screen.Bounds;var environment=new NativeEnvironment().Capture(b);
        if(!environment.Known || !environment.InteractiveSession || environment.Fullscreen || b.Width<=b.Height){
            WallpaperProbeBlocked(b.Width<=b.Height?"portrait-display":environment.Reason,environment);return false;
        }
        var target=new DesktopRect(b.Left,b.Top,b.Right,b.Bottom);
        var api=new Win32DesktopApi();var discovery=new DesktopLayerController(api).Discover(target);
        wallpaperInitialLayerAvailable=discovery.Available;
        if(!discovery.Available && GameWallpaperPrepareSelfTest && discovery.Reason=="existing-workerw-unavailable"){
            desktopApi=api;
            discovery=DiscoverForHost(target);
            wallpaperPreparedLayer=discovery.Available && WallpaperMessageAttempts==1;
            environment=new NativeEnvironment().Capture(b);
            if(!environment.Known || !environment.InteractiveSession || environment.Fullscreen){
                WallpaperProbeBlocked(environment.Reason,environment,discovery.NativeError);return false;
            }
        }
        if(!discovery.Available){WallpaperProbeBlocked(discovery.Reason,environment,discovery.NativeError);return false;}
        File.WriteAllText(Path.Combine(root,"wallpaper-environment.json"),json.Serialize(new {
            preflightPassed=true,existingLayer=true,initialLayerAvailable=wallpaperInitialLayerAvailable,prepareEnabled=GameWallpaperPrepareSelfTest,
            preparedMissingLayer=wallpaperPreparedLayer,prepareCalls=startupCalls,requestCalls=startupRequestCalls,messageAttempts=WallpaperMessageAttempts,
            passiveExistingLayer=WallpaperMessageAttempts==0,environmentKnown=environment.Known,sessionInteractive=environment.InteractiveSession,
            fullscreenObserved=environment.Fullscreen,environmentReason=environment.Reason,width=b.Width,height=b.Height,creationRequested=WallpaperMessageAttempts!=0}),Encoding.UTF8);
        return true;
    }

    bool WallpaperProbeCanRun()
    {
        Require(nativeEnvironment!=null && nativePolicyTimer.Enabled,"The real wallpaper environment sampling loop did not start.");
        SampleNativePolicy();
        if(nativeSample!=null && nativeSample.Known && nativeSample.InteractiveSession && !nativeSample.Fullscreen &&
            owner.ActivityState().CanRun && !game.Snapshot.HostPaused)return true;
        string reason=nativeSample==null?"native-sample-unavailable":nativeSample.BlockingReasons!=PauseReason.None?
            nativeSample.Reason:owner.ActivityState().Reasons.ToString();
        WallpaperProbeBlocked(reason,nativeSample);return false;
    }

    async Task<int> WallpaperProbeCapture(ProbeWindow read,int width,int height)
    {
        string raw=await read.View.CoreWebView2.ExecuteScriptAsync(@"(()=>{
            const c=document.getElementById('scene-canvas'),r=c.getBoundingClientRect(),p=[];
            for(let i=1;i<13;i++)for(let j=1;j<10;j++){
                const x=r.left+r.width*i/13,y=r.top+r.height*j/10;
                if(document.elementFromPoint(x,y)===c)p.push([x/innerWidth,y/innerHeight]);
            }return p;
        })()");
        var points=json.Deserialize<double[][]>(raw);
        Require(points!=null && points.Length>=50,"Desktop game canvas did not cover the expected viewport.");
        string path=Path.Combine(root,"game-wallpaper.png");
        using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            await read.View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
        var palette=new HashSet<int>();
        using(var image=new Bitmap(path)){
            Require(image.Width==width && image.Height==height,"Captured WebView dimensions differ from the primary display.");
            foreach(var p in points){
                var color=image.GetPixel((int)(p[0]*width),(int)(p[1]*height));
                palette.Add(((color.R>>3)<<10)|((color.G>>3)<<5)|(color.B>>3));
            }
        }
        Require(palette.Count>=4,"Wallpaper image did not contain distinguishable canvas colors.");return palette.Count;
    }

    async void RunGameWallpaperProbe()
    {
        try{
            Require(GameWallpaperSelfTest && !PlayerMode && automated && desktopMode && windows.Count==2 && startupPreparation==GameWallpaperPrepareSelfTest &&
                startupRequestCalls<=(GameWallpaperPrepareSelfTest?1:0) && WallpaperMessageAttempts<=startupRequestCalls,
                "Wallpaper diagnostic used the wrong mode, window count or shell preparation.");
            var read=windows[false];var pane=windows[true];var bounds=Screen.PrimaryScreen.Bounds;
            // Open() schedules navigation; Startup remains a normal transient
            // pause until both documents have completed their handshake.
            if(nativeSample==null || !nativeSample.Known || !nativeSample.InteractiveSession || nativeSample.Fullscreen){
                WallpaperProbeBlocked(nativeSample==null?"native-sample-unavailable":nativeSample.Reason,nativeSample);return;
            }
            await Ready(pane);await Ready(read);
            if(!WallpaperProbeCanRun())return;
            Require(read.DesktopReadOnly && !read.Ticket.CanControl && !pane.DesktopReadOnly && pane.Opacity==0 && !pane.ShowInTaskbar &&
                GetForegroundWindow()!=read.Handle && GetForegroundWindow()!=pane.Handle,"Wallpaper or test interaction window took foreground.");
            var s=game.Snapshot.State;
            Require(s.Sequence==1 && s.Rules.Species.Count==7 && s.BaitCount==12 && s.Stamina==100 &&
                s.Equipment.Instances.Count==6 && s.WalletMinor==12000,"Wallpaper profile did not start from the private seven-species fixture.");
            checks.Add("an interactive nonfullscreen native sample permits a private seven-species game with one read-only desktop child and one invisible test control");

            var binding=desktopLeases[read];var candidate=binding.Candidate;long hwnd=binding.WindowHandle;
            var state=desktopApi.ReadWindow(hwnd);CheckDesktop(read);
            wallpaperPreparedLayer=WallpaperMessageAttempts==1;
            Require(desktopAttachments==1 && desktopDetachments==0 && binding.IsActive && state!=null && state.ProcessId==desktopApi.CurrentProcessId &&
                state.Handle==read.Handle.ToInt64() && state.Parent==candidate.WorkerHandle && candidate.WorkerHandle!=candidate.IconHostHandle &&
                candidate.DefViewHandle!=0 && candidate.IconHostHandle!=0 && state.Visible && read.Visible &&
                (state.Style&DesktopLayerController.ChildStyle)!=0 && (state.ExtendedStyle&DesktopLayerController.NoActivateStyle)!=0,
                "Observer is not the owned visible no-activate child of the discovered WorkerW behind Explorer icons.");
            checks.Add("validated WorkerW ancestry places the owned no-activate child behind the real Explorer icon host");

            var requested=new DesktopRect(bounds.Left,bounds.Top,bounds.Right,bounds.Bottom);
            Require(state.Bounds.Same(requested) && candidate.TargetBounds.Same(requested) && read.View.ClientSize.Width==bounds.Width &&
                read.View.ClientSize.Height==bounds.Height && read.View.Visible,"Desktop child or its WebView does not fill the primary display.");
            checks.Add("the actual attached window bounds and WebView client size match the full primary monitor");

            var water=FloatFishingAtlas.Find("development-still-water");
            var page=await Wait(read,v=>PlayerProbeDrawn(v,water),25000);
            long frames=Number(SceneProbeRenderer(page),"frames"),triangles=Number(SceneProbeRenderer(page),"triangles");
            var advancing=await Wait(read,v=>PlayerProbeDrawn(v,water) && Number(SceneProbeRenderer(v),"frames")>=frames+2,15000);
            var gl=await SceneProbeScript(read,@"(()=>{
                const c=document.getElementById('scene-canvas'),g=c.getContext('webgl2');if(!g||g.isContextLost())return null;
                const ext=g.getExtension('WEBGL_debug_renderer_info');
                return {version:g.getParameter(g.VERSION),renderer:g.getParameter(ext?ext.UNMASKED_RENDERER_WEBGL:g.RENDERER),
                    cssWidth:innerWidth,cssHeight:innerHeight,ratio:devicePixelRatio,role:document.body.dataset.role,
                    hudHidden:getComputedStyle(document.querySelector('.scene-hud')).display==='none'};
            })()");
            double ratio=Convert.ToDouble(gl["ratio"]);
            Require(ratio>0 && Math.Abs(Number(gl,"cssWidth")*ratio-bounds.Width)<=2 && Math.Abs(Number(gl,"cssHeight")*ratio-bounds.Height)<=2 &&
                ((string)gl["version"]).StartsWith("WebGL 2",StringComparison.Ordinal) && !String.IsNullOrEmpty((string)gl["renderer"]) &&
                (string)gl["role"]=="observer" && (bool)gl["hudHidden"] && advancing["lease"]==null && triangles>0,
                "Visible desktop renderer, physical viewport scaling or observer UI proof is missing.");
            checks.Add("the wallpaper WebGL2 canvas draws multiple frames and geometry at the monitor CSS scale with the observer HUD hidden");

            if(!WallpaperProbeCanRun())return;
            int colors=await WallpaperProbeCapture(read,bounds.Width,bounds.Height);
            checks.Add("the full-resolution PNG contains rendered lake colors and captures only the application's own wallpaper WebView");

            string digest=GameShopJournalDigest();string lease=await Activate(pane);
            GameCode(await WaterProbeWire(read,WaterProbeMessage(read,lease,"begin",1)),"control-required",false);
            Require(game.Snapshot.State.Sequence==1 && gameFlightEmissions==0 && gameViews[read].CastHighWater==0 &&
                GameShopJournalDigest()==digest && GetForegroundWindow()!=pane.Handle && GetForegroundWindow()!=read.Handle,
                "Wallpaper observer acquired player control or spent game resources.");
            checks.Add("the invisible interaction control can be authorized but its grant cannot cast from the read-only wallpaper document");

            await GameMenu(pane,lease,true);
            await SceneProbeButton(pane,"spot-select-river-bend");var river=FloatFishingAtlas.Find("river-bend");
            await Wait(read,v=>AtlasProbeProjected(v,river.Id),10000);await GameMenu(pane,lease,false);
            await Wait(read,v=>PlayerProbeDrawn(v,river),15000);
            Revoke(pane);await Wait(pane,v=>v["lease"]==null);
            await Wait(read,v=>PlayerProbeDrawn(v,river) && v["lease"]==null);
            if(!WallpaperProbeCanRun())return;
            CheckDesktop(read);Require(gameFishingWater==river && GameShopJournalDigest()==digest && presentationChecks>0 && !presentationFailed &&
                GetForegroundWindow()!=pane.Handle && GetForegroundWindow()!=read.Handle,"Travel or revocation changed the desktop lease, foreground or save.");
            checks.Add("changing station through the separate interaction pane updates the wallpaper and revocation leaves its desktop parent and game progress intact");

            PrepareGameShutdown();Require(gameShutdownCode=="no-active-session" && game.Snapshot.State.Sequence==1 && gameFlightEmissions==0,
                "Wallpaper exit changed the idle private game.");
            checks.Add("closing the idle game requires no recovery, fish reward or extra transaction");

            // Retire both by the normal shutdown route. FinishShutdown is posted
            // to this same UI thread, so the synchronous evidence below is
            // written after the attachment cleanup and before process exit.
            Shutdown();
            var after=desktopApi.ReadWindow(hwnd);
            Require(ExitCode==0 && !binding.IsActive && desktopAttachments==1 && desktopDetachments==1 && desktopLeases.Count==0 &&
                desktopShown.Count==0 && !desktopCleanupFailed && (after==null || (!after.Visible && after.Parent==0)),
                "Real shutdown did not restore or destroy the owned desktop child.");
            checks.Add("normal shutdown detaches exactly the one owned wallpaper layer and leaves no live attachment or visible child");
            File.WriteAllText(Path.Combine(root,"webview-summary.json"),json.Serialize(new {
                passed=true,blocked=false,wallpaperAccepted=true,checks=checks.ToArray(),gameLoaded=true,desktopLayer=true,
                actualWebGL=true,observerReadOnly=true,ownedWindowBehindIcons=true,noActivateVerified=true,interactionInvisible=true,
                passiveExistingLayer=WallpaperMessageAttempts==0,prepareEnabled=GameWallpaperPrepareSelfTest,initialLayerAvailable=wallpaperInitialLayerAvailable,
                preparedMissingLayer=wallpaperPreparedLayer,prepareCalls=startupCalls,requestCalls=startupRequestCalls,messageAttempts=WallpaperMessageAttempts,
                fullscreenPolicyApplied=true,externalFullscreenTested=false,actualMonitorWidth=bounds.Width,actualMonitorHeight=bounds.Height,
                cssWidth=Number(gl,"cssWidth"),cssHeight=Number(gl,"cssHeight"),devicePixelRatio=ratio,rendererName=(string)gl["renderer"],triangles,
                sampleColors=colors,ownWebViewCapture=true,desktopCompositionCaptured=false,nativeInputTested=false,desktopIconInteractionTested=false,
                systemWallpaperPreferenceChanged=false,personalSaveRead=false,attachments=desktopAttachments,detachments=desktopDetachments,
                remainingLeases=desktopLeases.Count,presentationChecks,sequence=s.Sequence,value=s.WalletMinor,speciesCount=s.Rules.Species.Count,
                baitCount=s.BaitCount,stamina=s.Stamina,gameFlightEmissions,shutdown=gameShutdownCode}),Encoding.UTF8);
        }catch(Exception error){
            if(stopping){
                ExitCode=4;
                File.WriteAllText(Path.Combine(root,"dual-window-error.txt"),error.ToString(),Encoding.UTF8);
            }else if(nativeEnvironment!=null && nativeSample!=null && (!nativeSample.Known || nativeSample.Fullscreen || !nativeSample.InteractiveSession)){
                WallpaperProbeBlocked(nativeSample.Reason,nativeSample);
            }else{Fail(error);}
        }
    }
}
