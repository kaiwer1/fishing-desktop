using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using FishingDesktop.Diagnostics.DesktopLayer;

// Opt-in integration diagnostics. Only the explicit visible test shows its own
// no-activate child. Never creates an Explorer layer or changes global DPI.
sealed partial class DualWindowContext
{
    readonly Dictionary<ProbeWindow,AttachmentLease> desktopLeases=new Dictionary<ProbeWindow,AttachmentLease>();
    readonly HashSet<ProbeWindow> desktopShown=new HashSet<ProbeWindow>();
    readonly Timer desktopWatch=new Timer {Interval=500};
    Win32DesktopApi desktopApi;
    DesktopLayerController desktopController;
    int desktopAttachments,desktopDetachments,desktopValidations;
    bool desktopCleanupFailed;
    IntPtr desktopInitialForeground;
    int desktopLosses;
    string desktopLastLossReason;
    bool desktopAllowForegroundChanges;
    int presentationChecks;
    bool presentationFailed;
    void GuardPresentation(Action action)
    {
        IntPtr before=GetForegroundWindow();action();
        if(GetForegroundWindow()!=before){presentationFailed=true;throw new InvalidOperationException("Own presentation action changed foreground; source not attributed.");}
        presentationChecks++;
    }
    sealed class DesktopAttachmentLost : InvalidOperationException
    {
        internal readonly ProbeWindow Pane;
        internal readonly string Reason;
        internal DesktopAttachmentLost(ProbeWindow pane,AttachmentResult result):base("Desktop attachment invalidated: "+result.Reason+"; cleanup="+result.Cleanup){Pane=pane;Reason=result.Reason;}
    }

    void AttachDesktop(ProbeWindow pane)
    {
        if(!desktopMode || !pane.DesktopReadOnly)throw new InvalidOperationException("Desktop mode requires a hidden read-only view.");
        if(desktopController==null){
            desktopInitialForeground=GetForegroundWindow();
            desktopApi=new Win32DesktopApi();desktopController=new DesktopLayerController(desktopApi);
            desktopWatch.Tick+=delegate{
                try{foreach(var attached in new List<ProbeWindow>(desktopLeases.Keys))CheckDesktop(attached);}
                catch(DesktopAttachmentLost error){try{RetireLostDesktop(error);}catch(Exception failure){Fail(failure);}}
                catch(Exception error){Fail(error);}
            };
        }
        var bounds=Screen.PrimaryScreen.Bounds;
        var found=DiscoverForHost(new DesktopRect(bounds.Left,bounds.Top,bounds.Right,bounds.Bottom));
        if(!found.Available)throw new InvalidOperationException("Desktop layer unavailable: "+found.Reason);
        // Create only the native Form HWND here. Browser creation happens after
        // Attach; no Show()/ShowWindow() call is made for the desktop view.
        var attachedResult=desktopController.Attach(found.Candidate,pane.Handle.ToInt64());
        if(!attachedResult.Success)throw new InvalidOperationException("Desktop attachment failed: "+attachedResult.Reason+"; cleanup="+attachedResult.Cleanup);
        desktopLeases.Add(pane,attachedResult.Lease);desktopAttachments++;
        CheckDesktop(pane);desktopWatch.Start();
    }
    void CheckDesktop(ProbeWindow pane)
    {
        AttachmentLease lease;
        if(!desktopLeases.TryGetValue(pane,out lease))throw new InvalidOperationException("Missing desktop lease.");
        var result=desktopController.ValidateAndRecover(lease);
        if(!result.Success)throw new DesktopAttachmentLost(pane,result);
        var state=desktopApi.ReadWindow(lease.WindowHandle);
        bool shouldShow=desktopShown.Contains(pane);
        if(state==null || state.Visible!=shouldShow || pane.Visible!=shouldShow)throw new InvalidOperationException("Desktop diagnostic visibility changed unexpectedly.");
        if(!desktopAllowForegroundChanges && GetForegroundWindow()!=desktopInitialForeground){
            string kind="external-or-shell";IntPtr foreground=GetForegroundWindow();
            foreach(var view in windows.Values)if(view.IsHandleCreated && view.Handle==foreground)kind=view.Ticket.CanControl?"own-interaction":"own-observer";
            throw new InvalidOperationException("Foreground changed during desktop diagnostic; current category="+kind+". No external title or identity recorded.");
        }
        desktopValidations++;
    }
    void RetireLostDesktop(DesktopAttachmentLost error)
    {
        desktopLosses++;desktopLastLossReason=error.Reason;
        ProbeWindow control;
        if(windows.TryGetValue(true,out control))Revoke(control);
        if(stopping)return;
        // A failed presentation is not a new state owner. Cancel control and
        // retire only that view; retry requires an explicit Open, never a loop.
        Retire(error.Pane);
        try{error.Pane.Close();}finally{error.Pane.Dispose();}
        if(desktopCleanupFailed)Fail(new InvalidOperationException("Desktop cleanup failed; refusing view recovery."));
    }
    void ReleaseDesktop(ProbeWindow pane)
    {
        AttachmentLease lease;
        if(pane==null || !desktopLeases.TryGetValue(pane,out lease))return;
        var result=desktopController.Detach(lease);
        // A failed validation can already have recovered the same current
        // lease; confirm its retired window is hidden/top-level or destroyed.
        bool recovered=false;
        if(result.Reason=="stale-attachment-lease"){
            var state=desktopApi.ReadWindow(lease.WindowHandle);
            recovered=state==null || (!state.Visible && state.Parent==0);
        }
        if(!result.Success && !recovered){
            desktopCleanupFailed=true;ExitCode=4;
            try{File.WriteAllText(Path.Combine(root,"desktop-cleanup-error.txt"),json.Serialize(new{reason=result.Reason,cleanup=result.Cleanup,issues=result.Issues}),Encoding.UTF8);}catch(IOException){}catch(UnauthorizedAccessException){}
            return; // Keep identity for Dispose's retry; never claim cleanup.
        }
        desktopLeases.Remove(pane);desktopShown.Remove(pane);desktopDetachments++;
    }
    void StopDesktopWatch(){desktopWatch.Stop();}
    void WriteDesktopShutdownReport()
    {
        desktopWatch.Dispose();
        if(!desktopMode)return;
        bool foregroundUnchanged=GetForegroundWindow()==desktopInitialForeground;
        bool cleaned=desktopLeases.Count==0 && desktopAttachments==desktopDetachments && !desktopCleanupFailed;
        bool foregroundSafe=ManualMode?!presentationFailed:wallpaperPolicySelfTest?!presentationFailed && presentationChecks>=3:foregroundUnchanged;
        if(!cleaned || !foregroundSafe)ExitCode=4;
        File.WriteAllText(Path.Combine(root,"desktop-summary.json"),json.Serialize(new{
            passed=cleaned && foregroundSafe && ExitCode==0,attachments=desktopAttachments,detachments=desktopDetachments,
            validations=desktopValidations,remainingLeases=desktopLeases.Count,foregroundUnchanged=foregroundUnchanged,
            recoveredViewLosses=desktopLosses,lastLossReason=desktopLastLossReason,
            hiddenOnly=!visibleDesktopTest,realDesktopAttachment=desktopAttachments>0,nativeInputTested=false,multipleMonitorsTested=false,
            visual=visualResult,presentationChecks=presentationChecks,presentationChecksPassed=!presentationFailed,startup=StartupSummary(),
            foregroundScope=ManualMode || wallpaperPolicySelfTest?"own-presentation-actions":"entire-test"
        }),Encoding.UTF8);
    }
}
