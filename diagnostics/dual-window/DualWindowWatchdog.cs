using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FishingDesktop.Diagnostics.HostPolicy;

sealed partial class DualWindowContext
{
    const double WatchdogGraceSeconds=3;
    readonly System.Windows.Forms.Timer sceneWatchdogTimer=new System.Windows.Forms.Timer{Interval=250};
    readonly DrawWatchdog drawWatchdog=new DrawWatchdog(WatchdogGraceSeconds);
    sealed class DrawBinding
    {
        internal ProbeWindow Pane;
        internal string Document;
        internal int Navigation;
        internal long PolicyRevision,DrawEpoch;
        internal double Began;
    }
    DrawBinding drawBinding,pendingDrawBinding,rendererWaitBinding;
    Task<Dictionary<string,object>> pendingDrawSample;
    double pendingDrawStarted,nextWatchdogStatus;
    int watchdogSamples,watchdogLateIgnored,watchdogRequests;
    void InitializeSceneWatchdog()
    {
        if(!threeScene || !policySelfTest)return;
        sceneWatchdogTimer.Tick+=delegate{try{TickSceneWatchdog();}catch(Exception){if(!stopping)StopForSceneFault("draw-monitor-failed","watchdog");}};
        sceneWatchdogTimer.Start();
    }
    void ResetSceneWatchdog(){drawBinding=rendererWaitBinding=null;drawWatchdog.Reset();}
    void ForgetVisual(ProbeWindow pane)
    {
        if(pane==null)return;
        pane.DrawExpected=false;pane.DrawEpoch=checked(pane.DrawEpoch+1);
        if(drawBinding!=null && Object.ReferenceEquals(drawBinding.Pane,pane))ResetSceneWatchdog();
    }
    async Task StartVisual(ProbeWindow pane)
    {
        if(!Live(pane) || !pane.Ready)throw new InvalidOperationException("Drawing requires a current ready view.");
        pane.DrawExpected=true;pane.DrawEpoch=checked(pane.DrawEpoch+1);ResetSceneWatchdog();
        await pane.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.visual.start()");
    }
    async Task StopVisual(ProbeWindow pane)
    {
        ForgetVisual(pane);
        await pane.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.visual.stop()");
    }
    bool ExpectedDraw(out ProbeWindow pane)
    {
        pane=null;
        if(stopping || !threeScene || !policySelfTest || !owner.ActivityState().CanRun || !windows.TryGetValue(false,out pane) ||
            !Live(pane) || !pane.Ready || pane.AwaitingNavigationStart || !pane.DrawExpected)return false;
        return true;
    }
    bool BindingMatches(DrawBinding binding,ProbeWindow pane)
    {
        return binding!=null && Object.ReferenceEquals(binding.Pane,pane) && binding.Document==pane.Ticket.DocumentId &&
            binding.Navigation==pane.NavigationGeneration && binding.DrawEpoch==pane.DrawEpoch && binding.PolicyRevision==owner.ActivityState().Revision;
    }
    void ObserveCompletedDraw(bool current,double now)
    {
        var task=pendingDrawSample;pendingDrawSample=null;pendingDrawBinding=null;
        Dictionary<string,object> sample;
        try{sample=task.GetAwaiter().GetResult();}catch(Exception){if(current)StopForSceneFault("draw-telemetry-failed","watchdog");else watchdogLateIgnored++;return;}
        if(!current){watchdogLateIgnored++;return;}
        object kind,frames,running,error,lost;
        if(sample==null || !sample.TryGetValue("kind",out kind) || !(kind is string) || (string)kind!="webgl3d" ||
            !sample.TryGetValue("frames",out frames) || !(frames is int || frames is long) || Convert.ToInt64(frames)<0 ||
            !sample.TryGetValue("running",out running) || !(running is bool) || !sample.TryGetValue("error",out error) ||
            !sample.TryGetValue("contextLost",out lost) || !(lost is bool)){
            StopForSceneFault("draw-telemetry-invalid","watchdog");return;
        }
        if(error!=null || (bool)lost){StopForSceneFault("draw-reported-error","watchdog");return;}
        watchdogSamples++;
        string failure=drawWatchdog.Observe(now,Convert.ToInt64(frames),(bool)running);
        if(failure!=null)StopForSceneFault(failure,"watchdog");
    }
    void TickSceneWatchdog()
    {
        if(stopping)return;
        double now=activityWatch.Elapsed.TotalSeconds;ProbeWindow pane;
        if(!ExpectedDraw(out pane)){
            ResetSceneWatchdog();
            if(pendingDrawSample!=null && pendingDrawSample.IsCompleted)ObserveCompletedDraw(false,now);
            return;
        }
        RendererSuspender renderer;
        if((renderers.TryGetValue(pane,out renderer) && (renderer.Pending || renderer.Outcome!="active")) || RendererIsSuspended(pane)){
            if(!BindingMatches(rendererWaitBinding,pane))rendererWaitBinding=new DrawBinding{Pane=pane,Document=pane.Ticket.DocumentId,
                Navigation=pane.NavigationGeneration,DrawEpoch=pane.DrawEpoch,PolicyRevision=owner.ActivityState().Revision,Began=now};
            drawBinding=null;drawWatchdog.Reset();
            if(pendingDrawSample!=null && pendingDrawSample.IsCompleted)ObserveCompletedDraw(false,now);
            if(now-rendererWaitBinding.Began>=WatchdogGraceSeconds)StopForSceneFault("draw-renderer-transition-timeout","watchdog");
            return;
        }
        rendererWaitBinding=null;
        if(!BindingMatches(drawBinding,pane)){
            drawBinding=new DrawBinding{Pane=pane,Document=pane.Ticket.DocumentId,Navigation=pane.NavigationGeneration,
                DrawEpoch=pane.DrawEpoch,PolicyRevision=owner.ActivityState().Revision,Began=now};drawWatchdog.Reset();
        }
        if(pendingDrawSample!=null){
            if(pendingDrawSample.IsCompleted)ObserveCompletedDraw(Object.ReferenceEquals(pendingDrawBinding,drawBinding),now);
            else if(now-Math.Max(pendingDrawStarted,drawBinding.Began)>=WatchdogGraceSeconds)StopForSceneFault("draw-response-timeout","watchdog");
            if(stopping || pendingDrawSample!=null)return;
        }
        pendingDrawBinding=drawBinding;pendingDrawStarted=now;watchdogRequests++;
        pendingDrawSample=VisualState(pane);
        if(now>=nextWatchdogStatus){
            File.WriteAllText(Path.Combine(root,"draw-watchdog-status.json"),json.Serialize(new{schema=1,requests=watchdogRequests,
                samples=watchdogSamples,lateIgnored=watchdogLateIgnored,graceSeconds=WatchdogGraceSeconds,
                expectedDraw=true,policyRevision=drawBinding.PolicyRevision,navigation=drawBinding.Navigation,gameLoaded=false}),Encoding.UTF8);
            nextWatchdogStatus=now+1;
        }
    }
    void StopSceneWatchdog()
    {
        sceneWatchdogTimer.Stop();ResetSceneWatchdog();
        var task=pendingDrawSample;pendingDrawSample=null;pendingDrawBinding=null;
        if(task!=null)task.ContinueWith(completed=>completed.Exception.Handle(error=>true),TaskContinuationOptions.OnlyOnFaulted);
    }
    void DisposeSceneWatchdog(){StopSceneWatchdog();sceneWatchdogTimer.Dispose();}
}
