using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using FishingDesktop.Diagnostics.HostPolicy;

sealed partial class DualWindowContext
{
    readonly Timer nativePolicyTimer=new Timer{Interval=200};
    NativeEnvironment nativeEnvironment;
    EnvironmentSample nativeSample;
    Process nativeFixture;
    long nativeFixtureStart;
    object nativePolicyResult;
    readonly bool nativeOperatorStart;
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    void StartNativePolicy()
    {
        nativeEnvironment=new NativeEnvironment();
        StartNativeSignals();
        nativePolicyTimer.Tick+=delegate{try{SampleNativePolicy();}catch(Exception error){Fail(error);}};
        SampleNativePolicy();nativePolicyTimer.Start();
    }
    void SampleNativePolicy()
    {
        if(stopping)return;
        var screen=Screen.PrimaryScreen;
        nativeSample=screen==null?new EnvironmentSample(false,false,false,"target-screen-unavailable"):nativeEnvironment.Capture(screen.Bounds);
        nativeSignals.Observe(nativeSample);
        bool portrait=screen==null || !DisplayBarrier.Landscape(screen.Bounds.Width,screen.Bounds.Height);
        ApplyPolicy(owner.ReplacePause(NativeSignalState.Mask|PauseReason.Portrait,nativeSignals.BlockingReasons|(portrait?PauseReason.Portrait:PauseReason.None)));
        TryCompleteDisplayFromSample();
    }
    void StopNativePolicy()
    {
        nativePolicyTimer.Stop();
        if(nativeFixture!=null && !nativeFixture.HasExited){
            try{File.WriteAllText(Path.Combine(root,"exit.request"),"exit");}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    void DisposeNativePolicy()
    {
        nativePolicyTimer.Dispose();
        try{DisposeNativeSignals();}finally{
            if(nativeFixture!=null){
                try{
                    if(!nativeFixture.HasExited && !nativeFixture.WaitForExit(3000)){
                        string path=Path.Combine(root,"NativeEnvironmentFixture.exe");
                        if(nativeFixture.StartTime.ToUniversalTime().Ticks==nativeFixtureStart && nativeFixture.MainModule.FileName.Equals(path,StringComparison.OrdinalIgnoreCase))nativeFixture.Kill();
                        nativeFixture.WaitForExit(3000);ExitCode=4;
                    }
                }finally{nativeFixture.Dispose();nativeFixture=null;}
            }
        }
    }
    async Task PrepareNativePresentation(ProbeWindow read)
    {
        if(wallpaperPolicySelfTest){
            desktopShown.Add(read);GuardPresentation(()=>read.Show());CheckDesktop(read);
            await read.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.visual.start()");
            rendererEnabled=true;RendererPageReady(read);await WaitRenderer(read,"active");
            // Foreground changes by the owned fixture are intentional. Verify
            // foreground around our own show/hide actions instead of globally.
            desktopAllowForegroundChanges=!displayOnlySelfTest;
        }
    }
    async Task RunNativePolicyProbe(ProbeWindow read,ProbeWindow control,string ownerId,string originalRequest)
    {
        string oldLease=(string)(await Ready(control))["lease"];
        await PrepareNativePresentation(read);
        StartNativePolicy();
        string path=Path.Combine(root,"NativeEnvironmentFixture.exe");
        if(!File.Exists(path))throw new InvalidOperationException("Build with NativePolicy to include the owned fixture.");
        nativeFixture=Process.Start(new ProcessStartInfo(path,"\""+root.TrimEnd(Path.DirectorySeparatorChar)+"\""+(nativeOperatorStart?" --operator-start":"")){
            UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
        nativeFixtureStart=nativeFixture.StartTime.ToUniversalTime().Ticks;
        var timer=Stopwatch.StartNew();int full=0,consecutive=0,maxConsecutive=0,normal=0,restored=0;
        double frozen=-1;bool checkedPaused=false,checkedRestored=false;
        long fullRevision=-1;
        while(!nativeFixture.HasExited && !stopping && timer.ElapsedMilliseconds<65000+(nativeOperatorStart?180000:0)){
            string phase="";try{phase=File.ReadAllText(Path.Combine(root,"fixture-phase.txt"));}catch(IOException){}
            uint foregroundPid;GetWindowThreadProcessId(GetForegroundWindow(),out foregroundPid);
            bool ownFullscreen=foregroundPid==nativeFixture.Id && nativeSample!=null && nativeSample.Known && nativeSample.Fullscreen;
            if(phase=="normal" && nativeSample!=null && nativeSample.Known && !nativeSample.Fullscreen)normal++;
            if(ownFullscreen){
                long currentRevision=owner.ActivityState().Revision;
                if(currentRevision!=fullRevision){fullRevision=currentRevision;frozen=-1;consecutive=0;}
                full++;consecutive++;maxConsecutive=Math.Max(maxConsecutive,consecutive);
                Require(!owner.ActivityState().CanRun,"Native fullscreen did not pause owner.");
                if(frozen<0)frozen=activityClock.ActiveSeconds;else Require(activityClock.ActiveSeconds==frozen,"Native fullscreen owner clock advanced.");
                if(!checkedPaused){
                    await Wait(control,s=>Field(s,"hostPolicy")!=null && !(bool)Field(s,"hostPolicy")["canRun"] && s["lease"]==null);
                    if(wallpaperPolicySelfTest){
                        await WaitRenderer(read,"suspended");var backend=rendererBackends[read];
                        Require(read.View.CoreWebView2.IsSuspended && backend.Surface.IsShown && backend.Surface.ImageSize==read.View.ClientSize,"Visible wallpaper did not retain its own frozen frame while suspended.");
                        CheckDesktop(read);
                    }
                    Reply(await Command(control,oldLease,Guid.NewGuid().ToString("D"),1,2),false,"host-paused");
                    SameOwner(await Ready(wallpaperPolicySelfTest?control:read),ownerId,2,10);LockHeld();checkedPaused=true;
                }
                if(wallpaperPolicySelfTest)Require(read.View.CoreWebView2.IsSuspended,"Wallpaper renderer resumed during confirmed fullscreen.");
            }else{frozen=-1;consecutive=0;fullRevision=-1;}
            if(phase=="restored" && nativeSample!=null && nativeSample.Known && !nativeSample.Fullscreen){
                restored++;
                if(checkedPaused && !checkedRestored){
                    await Wait(control,s=>Field(s,"hostPolicy")!=null && (bool)Field(s,"hostPolicy")["canRun"] && s["lease"]==null);
                    if(wallpaperPolicySelfTest){
                        await WaitRenderer(read,"active");await Ready(read);CheckDesktop(read);
                        Require(!read.View.CoreWebView2.IsSuspended && !rendererBackends[read].Surface.IsShown && (bool)(await VisualState(read))["running"],"Visible wallpaper did not restore active drawing after native fullscreen.");
                    }
                    Reply(await Command(control,oldLease,Guid.NewGuid().ToString("D"),1,2),false,"inactive-lease");
                    double start=activityClock.ActiveSeconds;await Task.Delay(150);double delta=activityClock.ActiveSeconds-start;
                    Require(delta>0 && delta<.35,"Native restoration caught up paused time.");
                    string lease=await Activate(control);Reply(await Command(control,lease,originalRequest,7,0),true,null);checkedRestored=true;
                }
            }
            await Task.Delay(100);
        }
        File.WriteAllText(Path.Combine(root,"native-observation.json"),json.Serialize(new{
            normalSamples=normal,fullSamples=full,maximumConsecutiveFullSamples=maxConsecutive,restoredSamples=restored,
            checkedPaused=checkedPaused,checkedRestored=checkedRestored,lastSampleKnown=nativeSample!=null && nativeSample.Known,
            lastSampleReason=nativeSample==null?"none":nativeSample.Reason,actualDisplayOrDpiChange=false,
            operatorStartRequested=nativeOperatorStart,fixtureExited=nativeFixture.HasExited,fixtureExitCode=nativeFixture.HasExited?(int?)nativeFixture.ExitCode:null}));
        Require(!stopping && nativeFixture.HasExited && nativeFixture.ExitCode==0,"Owned native fixture failed or timed out.");
        Require(normal>=1 && maxConsecutive>=10 && restored>=3 && checkedPaused && checkedRestored,"Native/WebView joint result inconclusive; foreground samples missing.");
        if(nativeOperatorStart){
            var operatorReport=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(File.ReadAllText(Path.Combine(root,"fixture-operator.json")));
            Require((bool)operatorReport["operatorStarted"] && (bool)operatorReport["normalExit"],"Operator fixture did not record its complete bounded start/exit.");
            checks.Add("explicit operator-started fixture completes its native normal-fullscreen-normal interval before joint acceptance");
        }
        Require(owner.Snapshot().OwnerId==ownerId && owner.Snapshot().Sequence==2,"Native policy changed owner or committed sequence.");
        checks.Add("native owned fullscreen observations automatically pause the real WebView owner and revoke page commands");
        checks.Add("confirmed native fullscreen samples freeze owner activity while committed state and storage lock remain intact");
        checks.Add("native fullscreen exit resumes activity without old authority and explicit activation retains request deduplication");
        if(wallpaperPolicySelfTest){
            Require(rendererBackends[read].Captures>=1 && desktopShown.Contains(read),"Combined wallpaper had no retained frame or visible layer.");
            checks.Add("visible desktop wallpaper freezes its own bounded image and suspends WebView under native fullscreen");
            checks.Add("native fullscreen exit restores visible wallpaper drawing while old interaction authority stays revoked");
            rendererResult=new{passed=true,realWebViewSuspended=true,ownFrozenFrameVerified=true,nativeFullscreenCombined=true,
                visibleDesktopCombined=true,actualCpuMemoryMeasured=false,captures=rendererBackends[read].Captures};
        }
        nativePolicyResult=new{passed=true,nativeSignalsTested=true,controlledOwnFixture=true,fullSamples=full,
            maximumConsecutiveFullSamples=maxConsecutive,normalSamples=normal,restoredSamples=restored,
            ownerClockFrozen=true,noAutomaticLeaseRestore=true,fixtureNormalExit=true,lockOrPowerTransitionTested=false,operatorStartUsed=nativeOperatorStart};
    }
}
