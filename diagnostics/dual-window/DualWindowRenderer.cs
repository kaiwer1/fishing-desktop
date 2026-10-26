using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using FishingDesktop.Diagnostics.HostPolicy;

sealed partial class DualWindowContext
{
    readonly Dictionary<ProbeWindow,RendererSuspender> renderers=new Dictionary<ProbeWindow,RendererSuspender>();
    bool rendererEnabled;
    object rendererResult;
    sealed class WebViewRenderer : IRendererBackend
    {
        readonly DualWindowContext context;
        readonly ProbeWindow pane;
        readonly CoreWebView2 core;
        internal WebViewRenderer(DualWindowContext context,ProbeWindow pane){this.context=context;this.pane=pane;core=pane.View.CoreWebView2;}
        public bool IsAlive {get{try{return context.Live(pane) && !pane.View.IsDisposed && Object.ReferenceEquals(pane.View.CoreWebView2,core);}catch(Exception){return false;}}}
        public bool IsSuspended {get{return core.IsSuspended;}}
        public async Task PreparePauseAsync(){await core.ExecuteScriptAsync("window.dualProbe && window.dualProbe.visual.pause()");}
        public void SetVisible(bool visible){pane.View.Visible=visible;}
        public Task<bool> TrySuspendAsync(){return core.TrySuspendAsync();}
        public void Resume(){core.Resume();context.SendFreshPresentation(pane);}
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
        if(!renderers.TryGetValue(pane,out renderer)){renderer=new RendererSuspender(new WebViewRenderer(this,pane));renderers.Add(pane,renderer);}
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
        double paused=activityClock.ActiveSeconds;
        Reply(await Command(control,lease,Guid.NewGuid().ToString("D"),1,2),false,"host-paused");
        await Task.Delay(250);
        Require(read.View.CoreWebView2.IsSuspended && activityClock.ActiveSeconds==paused,"Suspended view auto-resumed or owner time advanced.");LockHeld();
        checks.Add("real read-only WebView suspends while control messages remain responsive and owner stays paused");
        SetHostPause(PauseReason.Manual,false);await WaitRenderer(read,"active");await Ready(read);
        Require(!read.View.CoreWebView2.IsSuspended && read.View.Visible,"Renderer did not resume visibility.");
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
        Require(renderers.Count==0 && Live(control) && owner.Snapshot().OwnerId==ownerId,"Disposing suspended view disposed owner or retained renderer.");
        SetHostPause(PauseReason.Manual,false);read=await Open(false);await Ready(read);await WaitRenderer(read,"active");
        lease=await Activate(control);Reply(await Command(control,lease,originalRequest,7,0),true,null);
        Require(owner.Snapshot().Sequence==2 && owner.Snapshot().Value==10,"Renderer recreation duplicated state.");
        checks.Add("disposing a suspended WebView and rebuilding it retains owner lock state and request deduplication");
        rendererResult=new{passed=true,realWebViewSuspended=true,controlRemainedResponsive=true,pausedReloadResuspended=true,
            suspendedDisposeRebuilt=true,automaticLeaseRestore=false,actualCpuMemoryMeasured=false};
    }
}
