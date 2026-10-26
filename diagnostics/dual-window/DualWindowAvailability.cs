using System;
using System.Threading.Tasks;
using FishingDesktop.Diagnostics.HostPolicy;

sealed partial class DualWindowContext
{
    bool zeroWindowPolicyVerified;
    object availabilityResult;
    void UpdateViewAvailability()
    {
        if(!policySelfTest || stopping)return;
        bool ready=false;foreach(var pane in windows.Values)if(Live(pane) && pane.Ready){ready=true;break;}
        PauseReason blocked=windows.Count==0?PauseReason.NoViews:ready?PauseReason.None:PauseReason.RendererUnavailable;
        ApplyPolicy(owner.ReplacePause(PauseReason.NoViews|PauseReason.RendererUnavailable,blocked));
    }
    async Task RunAvailabilityProbe(ProbeWindow read,ProbeWindow control,string ownerId,string request)
    {
        Require(zeroWindowPolicyVerified,"Zero-window activity policy was not observed.");
        checks.Add("zero-window host policy freezes activity without releasing owner state or its journal lock");
        string oldLease=(string)(await Ready(control))["lease"];
        string readDocument=read.Ticket.DocumentId,controlDocument=control.Ticket.DocumentId;
        Require(PrepareReload(read) && PrepareReload(control),"Both current views could not enter reload.");
        Require(!owner.ActivityState().CanRun && owner.ActivityState().Reasons==PauseReason.RendererUnavailable,"No-ready-view interval was not paused.");
        double frozen=activityClock.ActiveSeconds;await Task.Delay(250);
        Require(activityClock.ActiveSeconds==frozen,"Unavailable renderer interval advanced activity.");LockHeld();
        read.View.CoreWebView2.Reload();control.View.CoreWebView2.Reload();
        var a=await Ready(read);var b=await Ready(control);
        Require((string)a["documentId"]!=readDocument && (string)b["documentId"]!=controlDocument && b["lease"]==null,"Current navigation did not replace documents without authority.");
        Require(owner.ActivityState().CanRun,"Ready current views did not clear availability pause.");
        Reply(await Command(control,oldLease,Guid.NewGuid().ToString("D"),1,2),false,"inactive-lease");
        string fresh=await Activate(control);Reply(await Command(control,fresh,request,7,0),true,null);
        SameOwner(await Ready(read),ownerId,2,10);LockHeld();
        checks.Add("all current pages becoming unavailable pause activity until fresh navigation is ready without reviving authority or duplicating commits");
        availabilityResult=new{passed=true,zeroWindowsObserved=true,bothDocumentsReplaced=true,unavailableClockFrozen=true,
            ownerAndLockPreserved=true,freshAuthorityRequired=true,rule="any current ready diagnostic view permits activity"};
    }
}
