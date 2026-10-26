using System;
using System.Diagnostics;
using System.Threading.Tasks;
using FishingDesktop.Diagnostics.HostPolicy;

sealed partial class DualWindowContext
{
    NativeSignalState nativeSignals;
    NativeSignalWindow nativeSignalWindow;
    int nativeSession;
    object nativeSignalsResult;
    void StartNativeSignals()
    {
        if(nativeSignalWindow!=null)throw new InvalidOperationException("Native signal receiver already started.");
        using(var process=Process.GetCurrentProcess())nativeSession=process.SessionId;
        nativeSignals=new NativeSignalState(nativeSession);
        nativeSignalWindow=new NativeSignalWindow(OnNativePower,OnNativeSession,Fail);
    }
    void OnNativePower(int code,long unused)
    {
        if(stopping || nativeSignals==null || !nativeSignals.Power(code))return;
        ApplyPolicy(owner.ReplacePause(NativeSignalState.Mask,nativeSignals.BlockingReasons));
        // A resume notification cannot reuse a pre-suspend observation. The
        // synchronous query is new, on the same owner thread, after the barrier.
        SampleNativePolicy();
    }
    void OnNativeSession(int code,long session)
    {
        if(stopping || nativeSignals==null || !nativeSignals.Session(code,session))return;
        ApplyPolicy(owner.ReplacePause(NativeSignalState.Mask,nativeSignals.BlockingReasons));
        SampleNativePolicy();
    }
    void DisposeNativeSignals()
    {
        if(nativeSignalWindow==null)return;
        try{nativeSignalWindow.Dispose();}finally{nativeSignalWindow=null;}
    }
    async Task RunControlledSignals(ProbeWindow read,ProbeWindow control,string ownerId,string originalRequest)
    {
        Require(nativeSignalWindow!=null && nativeSignalWindow.Handle!=IntPtr.Zero,"No registered notification receiver.");
        string lease=(string)(await Ready(control))["lease"];
        long revision=owner.ActivityState().Revision;
        OnNativeSession(7,(long)nativeSession+1);
        Require(owner.ActivityState().Revision==revision,"Foreign session changed owner policy.");
        OnNativeSession(7,nativeSession);
        double frozen=activityClock.ActiveSeconds;
        await Task.Delay(450);SampleNativePolicy();
        Require(!owner.ActivityState().CanRun && activityClock.ActiveSeconds==frozen && (owner.ActivityState().Reasons&PauseReason.SessionUnavailable)!=0,"Controlled lock did not survive environment polling.");
        Reply(await Command(control,lease,Guid.NewGuid().ToString("D"),1,2),false,"host-paused");LockHeld();
        if(wallpaperPolicySelfTest)await WaitRenderer(read,"suspended");
        checks.Add("controlled current-session lock latches over native polling while foreign-session signals cannot change policy");
        OnNativeSession(8,nativeSession);
        await Wait(control,s=>Field(s,"hostPolicy")!=null && (bool)Field(s,"hostPolicy")["canRun"] && s["lease"]==null);
        Reply(await Command(control,lease,Guid.NewGuid().ToString("D"),1,2),false,"inactive-lease");
        OnNativePower(4,0);frozen=activityClock.ActiveSeconds;
        await Task.Delay(450);SampleNativePolicy();
        Require(!owner.ActivityState().CanRun && activityClock.ActiveSeconds==frozen && (owner.ActivityState().Reasons&PauseReason.PowerSuspended)!=0,"Controlled suspend was cleared by polling.");
        if(wallpaperPolicySelfTest){await WaitRenderer(read,"suspended");Require(read.View.CoreWebView2.IsSuspended && rendererBackends[read].Surface.IsShown,"Controlled suspend did not freeze visible renderer.");}
        checks.Add("controlled power suspend freezes activity and remains blocked across fresh environment observations");
        SetHostPause(PauseReason.Manual,true);OnNativePower(18,0);
        Require(!owner.ActivityState().CanRun && (owner.ActivityState().Reasons&PauseReason.Manual)!=0,"Controlled resume cleared an unrelated pause.");
        SetHostPause(PauseReason.Manual,false);
        await Wait(control,s=>Field(s,"hostPolicy")!=null && (bool)Field(s,"hostPolicy")["canRun"] && s["lease"]==null);
        if(wallpaperPolicySelfTest)await WaitRenderer(read,"active");
        Reply(await Command(control,lease,Guid.NewGuid().ToString("D"),1,2),false,"inactive-lease");
        double resumed=activityClock.ActiveSeconds;await Task.Delay(150);double delta=activityClock.ActiveSeconds-resumed;
        Require(delta>0 && delta<.35,"Controlled signal resume caught up inactive time.");
        string fresh=await Activate(control);Reply(await Command(control,fresh,originalRequest,7,0),true,null);
        SameOwner(await Ready(control),ownerId,2,10);LockHeld();
        nativeSignalsResult=new{passed=true,notificationWindowRegistered=true,controlledCallbacks=true,actualLockOrPowerTransition=false,
            foreignSessionIgnored=true,unrelatedPausePreserved=true,freshAuthorityRequired=true};
        checks.Add("controlled notification recovery preserves unrelated pauses and requires fresh authority without duplicate commits");
    }
}
