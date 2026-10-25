using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using FishingDesktop.Diagnostics.DesktopLayer;

// Opt-in hidden integration diagnostic only. Never makes a desktop child
// visible, creates an Explorer layer, or changes global DPI awareness.
sealed partial class DualWindowContext
{
    readonly Dictionary<ProbeWindow,AttachmentLease> desktopLeases=new Dictionary<ProbeWindow,AttachmentLease>();
    readonly Timer desktopWatch=new Timer {Interval=500};
    Win32DesktopApi desktopApi;
    DesktopLayerController desktopController;
    int desktopAttachments,desktopDetachments,desktopValidations;
    bool desktopCleanupFailed;
    IntPtr desktopInitialForeground;

    void AttachDesktop(ProbeWindow pane)
    {
        if(!desktopMode || !pane.DesktopReadOnly)throw new InvalidOperationException("Desktop mode requires a hidden read-only view.");
        if(desktopController==null){
            desktopInitialForeground=GetForegroundWindow();
            desktopApi=new Win32DesktopApi();desktopController=new DesktopLayerController(desktopApi);
            desktopWatch.Tick+=delegate{
                try{foreach(var attached in new List<ProbeWindow>(desktopLeases.Keys))CheckDesktop(attached);}
                catch(Exception error){Fail(error);}
            };
        }
        var bounds=Screen.PrimaryScreen.Bounds;
        var found=desktopController.Discover(new DesktopRect(bounds.Left,bounds.Top,bounds.Right,bounds.Bottom));
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
        if(!result.Success)throw new InvalidOperationException("Desktop attachment invalidated: "+result.Reason+"; cleanup="+result.Cleanup);
        var state=desktopApi.ReadWindow(lease.WindowHandle);
        if(state==null || state.Visible || pane.Visible)throw new InvalidOperationException("Desktop diagnostic must remain hidden.");
        if(GetForegroundWindow()!=desktopInitialForeground)throw new InvalidOperationException("Foreground changed during hidden desktop diagnostic.");
        desktopValidations++;
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
        desktopLeases.Remove(pane);desktopDetachments++;
    }
    void StopDesktopWatch(){desktopWatch.Stop();}
    void WriteDesktopShutdownReport()
    {
        desktopWatch.Dispose();
        if(!desktopMode)return;
        bool foregroundUnchanged=GetForegroundWindow()==desktopInitialForeground;
        bool cleaned=desktopLeases.Count==0 && desktopAttachments==desktopDetachments && !desktopCleanupFailed;
        if(!cleaned || !foregroundUnchanged)ExitCode=4;
        File.WriteAllText(Path.Combine(root,"desktop-summary.json"),json.Serialize(new{
            passed=cleaned && foregroundUnchanged && ExitCode==0,attachments=desktopAttachments,detachments=desktopDetachments,
            validations=desktopValidations,remainingLeases=desktopLeases.Count,foregroundUnchanged=foregroundUnchanged,
            hiddenOnly=true,realDesktopAttachment=desktopAttachments>0,nativeInputTested=false,multipleMonitorsTested=false
        }),Encoding.UTF8);
    }
}
