using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using FishingDesktop.Diagnostics.HostPolicy;

sealed partial class DualWindowContext
{
    readonly Dictionary<ProbeWindow,RendererSuspender> renderers=new Dictionary<ProbeWindow,RendererSuspender>();
    readonly Dictionary<ProbeWindow,WebViewRenderer> rendererBackends=new Dictionary<ProbeWindow,WebViewRenderer>();
    bool rendererEnabled;
    object rendererResult;
    sealed class WebViewRenderer : IRendererBackend,IDisposable
    {
        readonly DualWindowContext context;
        readonly ProbeWindow pane;
        readonly CoreWebView2 core;
        internal readonly FrozenFrameSurface Surface;
        Task capture;
        long captureGeneration;
        bool disposed;
        internal int Captures;
        internal string CaptureIssue;
        internal WebViewRenderer(DualWindowContext context,ProbeWindow pane){this.context=context;this.pane=pane;core=pane.View.CoreWebView2;Surface=new FrozenFrameSurface(pane.View);}
        public bool IsAlive {get{try{return !disposed && context.Live(pane) && !pane.View.IsDisposed && Object.ReferenceEquals(pane.View.CoreWebView2,core);}catch(Exception){return false;}}}
        public bool IsSuspended {get{return core.IsSuspended;}}
        public async Task PreparePauseAsync()
        {
            long generation=checked(++captureGeneration);
            await core.ExecuteScriptAsync("window.dualProbe && window.dualProbe.visual.pause()");
            if(!IsAlive || generation!=captureGeneration)return;
            if(capture!=null && !capture.IsCompleted){CaptureIssue="previous-capture-pending";Surface.Show();return;}
            var stream=new BoundedFrameStream(48L*1024*1024);Task pending;
            try{pending=core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);capture=pending;}
            catch(Exception error){stream.Dispose();if(IsAlive && generation==captureGeneration){CaptureIssue=error.GetType().Name;Surface.Show();}return;}
            if(await Task.WhenAny(pending,Task.Delay(1500))!=pending){
                CaptureIssue="capture-timeout";DisposeLateCapture(pending,stream);
                if(IsAlive && generation==captureGeneration)Surface.Show();return;
            }
            try{
                await pending;
                var frame=stream.Decode(8388608);
                try{if(IsAlive && generation==captureGeneration){Surface.Assign(frame);frame=null;Surface.Show();Captures++;CaptureIssue=null;}}
                finally{if(frame!=null)frame.Dispose();}
            }catch(Exception error){if(IsAlive && generation==captureGeneration){CaptureIssue=error.GetType().Name;Surface.Show();}}
            finally{stream.Dispose();if(Object.ReferenceEquals(capture,pending))capture=null;}
        }
        async void DisposeLateCapture(Task pending,BoundedFrameStream stream)
        {
            try{await pending;}catch(Exception){}
            finally{stream.Dispose();if(Object.ReferenceEquals(capture,pending))capture=null;}
        }
        public void SetVisible(bool visible){context.GuardPresentation(()=>{pane.View.Visible=visible;if(visible)Surface.Hide();else Surface.Show();});}
        public Task<bool> TrySuspendAsync(){return core.TrySuspendAsync();}
        public void Resume(){captureGeneration=checked(captureGeneration+1);core.Resume();context.SendFreshPresentation(pane);}
        public void Dispose(){if(disposed)return;disposed=true;captureGeneration=checked(captureGeneration+1);Surface.Dispose();}
    }
    void SendFreshPresentation(ProbeWindow pane)
    {
        if(!Live(pane) || !pane.Ready)return;
        Send(pane,new{kind="role",documentId=pane.Ticket.DocumentId,role=pane.Ticket.CanControl?"独立交互窗口":"独立只读窗口"});
        Send(pane,new{kind="snapshot",documentId=pane.Ticket.DocumentId,snapshot=owner.Snapshot()});SendPolicy(pane);
    }
    bool RendererIsSuspended(ProbeWindow pane){return renderers.ContainsKey(pane) && pane.View.CoreWebView2!=null && pane.View.CoreWebView2.IsSuspended;}
    async void ObserveRenderer(ProbeWindow pane,RendererSuspender renderer,Task pending)
    {
        await pending;
        RendererSuspender current;
        if(Live(pane) && renderers.TryGetValue(pane,out current) && Object.ReferenceEquals(current,renderer) && renderer.Outcome=="failed")
            Fail(new InvalidOperationException("Renderer policy failed: "+renderer.Error));
    }
    void RequestRenderer(ProbeWindow pane,RendererSuspender renderer,bool run,bool force=false){ObserveRenderer(pane,renderer,renderer.RequestRun(run,force));}
    void RendererPageReady(ProbeWindow pane)
    {
        if(!rendererEnabled || pane.Ticket.CanControl || !Live(pane))return;
        RendererSuspender renderer;
        if(!renderers.TryGetValue(pane,out renderer)){var backend=new WebViewRenderer(this,pane);rendererBackends.Add(pane,backend);renderer=new RendererSuspender(backend);renderers.Add(pane,renderer);}
        RequestRenderer(pane,renderer,owner.ActivityState().CanRun,true);
    }
    void RendererNavigating(ProbeWindow pane)
    {
        RendererSuspender renderer;
        if(renderers.TryGetValue(pane,out renderer))RequestRenderer(pane,renderer,true,true);
    }
    void RetireRenderer(ProbeWindow pane)
    {
        RendererSuspender renderer;
        if(pane!=null && renderers.TryGetValue(pane,out renderer)){renderer.Dispose();renderers.Remove(pane);}
        WebViewRenderer backend;if(pane!=null && rendererBackends.TryGetValue(pane,out backend)){backend.Dispose();rendererBackends.Remove(pane);}
    }
    void ApplyRendererPolicy()
    {
        foreach(var pair in new List<KeyValuePair<ProbeWindow,RendererSuspender>>(renderers))
            if(Live(pair.Key) && pair.Key.Ready)RequestRenderer(pair.Key,pair.Value,owner.ActivityState().CanRun);
    }
    async Task WaitRenderer(ProbeWindow pane,string outcome)
    {
        var timer=Stopwatch.StartNew();
        while(timer.ElapsedMilliseconds<6000 && Live(pane)){
            RendererSuspender renderer;
            if(renderers.TryGetValue(pane,out renderer) && !renderer.Pending && renderer.Outcome==outcome)return;
            await Task.Delay(25);
        }
        throw new InvalidOperationException("Renderer transition timed out: "+outcome);
    }
    async Task RunRendererProbe(ProbeWindow read,ProbeWindow control,string ownerId,string originalRequest)
    {
        rendererEnabled=true;RendererPageReady(read);await WaitRenderer(read,"active");
        await read.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.visual.start()");
        string lease=await Activate(control);SetHostPause(PauseReason.Manual,true);
        await WaitRenderer(read,"suspended");
        Require(read.View.CoreWebView2.IsSuspended && !read.View.Visible && !control.View.CoreWebView2.IsSuspended,"Selective renderer suspension failed.");
        var frozen=rendererBackends[read];
        Require(frozen.Captures==1 && frozen.Surface.IsShown && frozen.Surface.ImageSize==read.View.ClientSize,"Own-view frozen frame missing or wrong size.");
        double paused=activityClock.ActiveSeconds;
        Reply(await Command(control,lease,Guid.NewGuid().ToString("D"),1,2),false,"host-paused");
        await Task.Delay(250);
        Require(read.View.CoreWebView2.IsSuspended && activityClock.ActiveSeconds==paused,"Suspended view auto-resumed or owner time advanced.");LockHeld();
        checks.Add("real read-only WebView suspends while control messages remain responsive and owner stays paused");
        SetHostPause(PauseReason.Manual,false);await WaitRenderer(read,"active");await Ready(read);
        Require(!read.View.CoreWebView2.IsSuspended && read.View.Visible,"Renderer did not resume visibility.");
        Require(!frozen.Surface.IsShown && frozen.Surface.HasImage,"Resuming did not hide the retained frozen frame.");
        Reply(await Command(control,lease,Guid.NewGuid().ToString("D"),1,2),false,"inactive-lease");
        var visual=await VisualState(read);Require((bool)visual["running"],"Paused drawing was not restored.");
        checks.Add("real renderer resume restores its drawing state without restoring the revoked input lease");
        SetHostPause(PauseReason.Manual,true);await WaitRenderer(read,"suspended");
        string oldDocument=read.Ticket.DocumentId;Reload(read);
        var loading=Stopwatch.StartNew();
        while(loading.ElapsedMilliseconds<6000 && Live(read) && (!read.Ready || read.Ticket.DocumentId==oldDocument || !read.View.CoreWebView2.IsSuspended))await Task.Delay(25);
        Require(Live(read) && read.Ready && read.Ticket.DocumentId!=oldDocument && read.View.CoreWebView2.IsSuspended,"Paused renderer reload did not re-suspend the new document.");
        SetHostPause(PauseReason.Manual,false);await WaitRenderer(read,"active");SameOwner(await Ready(read),ownerId,2,10);
        checks.Add("paused renderer reload resumes only for navigation and re-suspends the new document before explicit host resume");
        SetHostPause(PauseReason.Manual,true);await WaitRenderer(read,"suspended");read.Dispose();LockHeld();
        Require(renderers.Count==0 && rendererBackends.Count==0 && !frozen.Surface.HasImage && Live(control) && owner.Snapshot().OwnerId==ownerId,"Disposing suspended view disposed owner or retained renderer/frame.");
        SetHostPause(PauseReason.Manual,false);read=await Open(false);await Ready(read);await WaitRenderer(read,"active");
        lease=await Activate(control);Reply(await Command(control,lease,originalRequest,7,0),true,null);
        Require(owner.Snapshot().Sequence==2 && owner.Snapshot().Value==10,"Renderer recreation duplicated state.");
        checks.Add("disposing a suspended WebView and rebuilding it retains owner lock state and request deduplication");
        rendererResult=new{passed=true,realWebViewSuspended=true,controlRemainedResponsive=true,pausedReloadResuspended=true,
            suspendedDisposeRebuilt=true,ownFrozenFrameVerified=true,frameBytesLimit=48*1024*1024,framePixelsLimit=8388608,
            automaticLeaseRestore=false,actualCpuMemoryMeasured=false};
    }
}
