using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using FishingDesktop.Diagnostics.HostPolicy;

sealed partial class DualWindowContext
{
    readonly DisplayBarrier displayBarrier=new DisplayBarrier();
    object displayResult;
    internal long DisplayGeneration {get{return displayBarrier.Revision;}}
    void OnNativeDisplay()
    {
        if(stopping || nativeSignals==null)return;
        long generation=displayBarrier.Begin();SetHostPause(PauseReason.DisplayChanging,true);
        ProbeWindow old;windows.TryGetValue(false,out old);
        // Never dispose a browser/Form while inside its native window callback.
        // Repeated notices invalidate this closure; only its captured old view
        // can be retired, never a newly opened window with the same role.
        dispatcher.BeginInvoke(new Action(delegate{
            if(stopping || displayBarrier.Revision!=generation || old==null || !Live(old))return;
            try{
                Retire(old);try{old.Close();}finally{old.Dispose();}
                if(desktopCleanupFailed)throw new InvalidOperationException("Display change cleanup failed; refusing recovery.");
            }catch(Exception error){Fail(error);}
        }));
    }
    internal void OnReadOnlyDpiMessage(ProbeWindow pane)
    {
        try{if(pane.DesktopReadOnly && Live(pane))OnNativeDisplay();}catch(Exception error){Fail(error);}
    }
    void DisplayPageReady(ProbeWindow pane)
    {
        if(nativeEnvironment==null || pane.Ticket.CanControl || !displayBarrier.Blocked)return;
        dispatcher.BeginInvoke(new Action(delegate{
            if(!Live(pane))return;
            try{SampleNativePolicy();}catch(Exception error){Fail(error);}
        }));
    }
    void TryCompleteDisplayFromSample()
    {
        if(stopping || !displayBarrier.Blocked || nativeSample==null || !nativeSample.Known || !nativeSample.InteractiveSession)return;
        ProbeWindow pane;var screen=Screen.PrimaryScreen;
        if(screen==null || !DisplayBarrier.Landscape(screen.Bounds.Width,screen.Bounds.Height) || !windows.TryGetValue(false,out pane) ||
            !Live(pane) || !pane.Ready || pane.DisplayGeneration!=displayBarrier.Revision)return;
        if(pane.DesktopReadOnly){
            if(pane.View.ClientSize!=screen.Bounds.Size)return;
            try{CheckDesktop(pane);}catch(DesktopAttachmentLost error){
                dispatcher.BeginInvoke(new Action(delegate{if(Live(pane)){try{RetireLostDesktop(error);}catch(Exception failure){Fail(failure);}}}));
                return;
            }
        }
        if(displayBarrier.Complete(pane.DisplayGeneration,true))SetHostPause(PauseReason.DisplayChanging,false);
    }
    async Task<ProbeWindow> RunControlledDisplay(ProbeWindow read,ProbeWindow control,string ownerId,string request)
    {
        IntPtr receiver=nativeSignalWindow.Handle;string lease=(string)(await Ready(control))["lease"];
        OnNativeDisplay();long first=displayBarrier.Revision;
        Require(!owner.ActivityState().CanRun && displayBarrier.Blocked,"Display notification did not immediately pause.");
        double frozen=activityClock.ActiveSeconds;
        var wait=Stopwatch.StartNew();while(!read.IsDisposed && wait.ElapsedMilliseconds<3000)await Task.Delay(25);
        Require(read.IsDisposed && !windows.ContainsKey(false),"Old display view was not retired.");
        await Task.Delay(250);Require(activityClock.ActiveSeconds==frozen,"Missing display view advanced activity.");
        Reply(await Command(control,lease,Guid.NewGuid().ToString("D"),1,2),false,"host-paused");LockHeld();
        checks.Add("controlled display notification immediately pauses authority and retires only the old presentation while owner state survives");
        OnNativeDisplay();long current=displayBarrier.Revision;
        Require(current>first && !displayBarrier.Complete(first,true) && displayBarrier.Blocked,"Old display completion cleared a newer barrier.");
        SetHostPause(PauseReason.Manual,true);
        read=await Open(false);await Ready(read);
        await Wait(control,s=>Field(s,"hostPolicy")!=null && (string)Field(s,"hostPolicy")["reasons"]=="Manual");
        Require(!displayBarrier.Blocked && !owner.ActivityState().CanRun,"Display reopen lost an unrelated pause.");
        checks.Add("display generations reject stale completion and verified explicit reopen preserves unrelated pause reasons");
        SetHostPause(PauseReason.Manual,false);
        await Wait(control,s=>Field(s,"hostPolicy")!=null && (bool)Field(s,"hostPolicy")["canRun"] && s["lease"]==null);
        if(wallpaperPolicySelfTest){
            desktopShown.Add(read);GuardPresentation(()=>read.Show());CheckDesktop(read);
            await StartVisual(read);await WaitRenderer(read,"active");
        }
        Reply(await Command(control,lease,Guid.NewGuid().ToString("D"),1,2),false,"inactive-lease");
        string fresh=await Activate(control);Reply(await Command(control,fresh,request,7,0),true,null);
        SameOwner(await Ready(control),ownerId,2,10);LockHeld();
        Require(nativeSignalWindow.Handle==receiver && !displayBarrier.Blocked && read.DisplayGeneration==current,"Display recovery replaced receiver or used stale geometry.");
        checks.Add("display view reconstruction retains notification receiver and owner deduplication without reviving old authority");
        displayResult=new{passed=true,controlledCallbacks=true,actualDisplayOrDpiChange=false,staleCompletionRejected=true,
            explicitViewReopen=true,ownerPreserved=true,receiverPreserved=true};
        if(displayOnlySelfTest)rendererResult=new{passed=true,realWebViewSuspended=true,ownFrozenFrameVerified=true,
            controlledSignalsCombined=true,nativeFullscreenCombined=false,visibleDesktopCombined=true,actualCpuMemoryMeasured=false};
        return read;
    }
}
