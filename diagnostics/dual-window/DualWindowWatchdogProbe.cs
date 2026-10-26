using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FishingDesktop.Diagnostics.HostPolicy;

sealed partial class DualWindowContext
{
    readonly string sceneWatchdogCase;
    sealed class PendingSuspendProbeBackend : IRendererBackend
    {
        readonly Func<bool> alive;
        readonly TaskCompletionSource<bool> pending=new TaskCompletionSource<bool>();
        internal PendingSuspendProbeBackend(Func<bool> alive){this.alive=alive;}
        public bool IsAlive {get{return alive();}}
        public bool IsSuspended {get{return false;}}
        public Task PreparePauseAsync(){return Task.FromResult(true);}
        public void SetVisible(bool visible){}
        public Task<bool> TrySuspendAsync(){return pending.Task;}
        public void Resume(){}
    }
    async Task RunWatchdogProbe(ProbeWindow read,ProbeWindow control)
    {
        string ownerId=owner.Snapshot().OwnerId,request=Guid.NewGuid().ToString("D");
        rendererEnabled=true;RendererPageReady(read);await WaitRenderer(read,"active");
        await StartVisual(read);await VerifyThreeScene(read,true);
        faultProbeLease=await Activate(control);Reply(await Command(control,faultProbeLease,request,7,0),true,null);
        checks.Add("healthy watchdog policy host confirms one committed input before fault injection");
        SetHostPause(PauseReason.Manual,true);await WaitRenderer(read,"suspended");
        double frozen=activityClock.ActiveSeconds;await Task.Delay(3500);
        Require(Live(read) && Live(control) && activityClock.ActiveSeconds==frozen,"Watchdog treated a policy pause as a stall.");
        SetHostPause(PauseReason.Manual,false);await WaitRenderer(read,"active");
        await Task.Delay(750);Require(Live(read) && owner.ActivityState().CanRun,"Healthy renderer did not survive pause longer than the grace period.");
        checks.Add("pause exceeding watchdog grace freezes time and resumes without a false failure");
        int beforeReloadSamples=watchdogSamples;
        string old=read.Ticket.DocumentId;Reload(read);await Wait(read,s=>(string)s["documentId"]!=old);
        await StartVisual(read);await WaitRenderer(read,"active");
        var sampleWait=System.Diagnostics.Stopwatch.StartNew();
        while(!stopping && sampleWait.ElapsedMilliseconds<2500 && watchdogSamples<beforeReloadSamples+3)await Task.Delay(50);
        Require(Live(read) && owner.ActivityState().CanRun && watchdogSamples>=beforeReloadSamples+3 && BindingMatches(drawBinding,read),"Recreated document did not establish fresh monitored drawing.");
        SameOwner(await Ready(read),ownerId,1,7);faultProbeLease=await Activate(control);LockHeld();
        checks.Add("reload resets draw intent and document binding while confirmed state and current monitored drawing survive");
        string reason=sceneWatchdogCase=="silent-stop" || sceneWatchdogCase=="record-failure"?"draw-loop-stopped":sceneWatchdogCase=="stale-flag"?"draw-progress-stalled":
            sceneWatchdogCase=="transition-pending"?"draw-renderer-transition-timeout":"draw-response-timeout";
        File.WriteAllText(Path.Combine(root,"scene-fault-preflight.json"),json.Serialize(new{passed=true,checks=checks.ToArray(),ownerId=ownerId,
            requestId=request,oldLease=faultProbeLease,expectedFaultReason=reason,sequence=1,value=7,watchdogCase=sceneWatchdogCase,
            selectiveRendererEnabled=true,pauseLongerThanGraceVerified=true,reloadVerified=true,watchdogSamples=watchdogSamples,
            syntheticPendingBackend=sceneWatchdogCase=="transition-pending",faultRecordPathBlocked=sceneWatchdogCase=="record-failure",
            ownContextExtensionOnly=false,desktopAttachmentTested=false,hardwareDeviceLossTested=false,gameLoaded=false}),Encoding.UTF8);
        if(sceneWatchdogCase=="record-failure")Directory.CreateDirectory(Path.Combine(root,"scene-fault.json"));
        if(sceneWatchdogCase=="transition-pending"){
            renderers[read].Dispose();rendererBackends[read].Dispose();rendererBackends.Remove(read);
            var pending=new RendererSuspender(new PendingSuspendProbeBackend(()=>Live(read)));renderers[read]=pending;
            SetHostPause(PauseReason.Manual,true);await Task.Delay(100);
            SetHostPause(PauseReason.Manual,false);
            Require(pending.Pending,"Synthetic suspend request did not remain pending.");
        }else if(sceneWatchdogCase=="silent-stop" || sceneWatchdogCase=="record-failure")
            await read.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.visual.stop()");
        else if(sceneWatchdogCase=="stale-flag")
            await read.View.CoreWebView2.ExecuteScriptAsync("(()=>{const v=window.dualProbe.visual;v.stop();const original=v.state;v.state=()=>({...original(),running:true})})()");
        else
            await read.View.CoreWebView2.ExecuteScriptAsync("setTimeout(()=>{const began=performance.now();while(performance.now()-began<8000){}},100)");
        await Task.Delay(7000);
        if(!stopping)throw new InvalidOperationException("Watchdog did not close an active view without drawing progress or telemetry response.");
    }
}
