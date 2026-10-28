using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using FishingDesktop.Diagnostics.HostPolicy;

sealed partial class DualWindowContext
{
    readonly ActiveClock activityClock=new ActiveClock(.25);
    readonly Stopwatch activityWatch=Stopwatch.StartNew();
    readonly System.Windows.Forms.Timer activityTimer=new System.Windows.Forms.Timer{Interval=50};
    object policyResult;
    void InitializeActivityPolicy()
    {
        if(!policySelfTest)return;
        SetHostPause(PauseReason.Startup,true);
        UpdateViewAvailability();
        activityTimer.Tick+=delegate{
            try{AdvanceActivity();}
            catch(Exception error){Fail(error);}
        };
        activityTimer.Start();
    }
    void PolicyPageReady()
    {
        if(!policySelfTest || windows.Count!=2)return;
        foreach(var pane in windows.Values)if(!pane.Ready)return;
        SetHostPause(PauseReason.Startup,false);
    }
    void SetHostPause(PauseReason reason,bool blocked)
    {
        ApplyPolicy(owner.SetPause(reason,blocked));
    }
    void AdvanceActivity()
    {
        double step=activityClock.Advance(activityWatch.Elapsed.TotalSeconds,owner.ActivityState().CanRun && GameCanSimulate);
        AdvanceGameCast(step);
        AdvanceGameFlight(step);
        AdvanceGameBite(step);
        AdvanceGameFight(step);
    }
    void ApplyPolicy(RunTransition transition)
    {
        if(transition.Changed)ResetSceneWatchdog();
        SyncGamePolicy();
        AdvanceActivity();
        if(transition.RevokeInput){
            foreach(var pane in windows.Values)if(pane.Ticket.CanControl){activation.Cancel(pane.Ticket.ViewId);SendLease(pane,null);}
        }
        if(transition.Changed)foreach(var pane in windows.Values)SendPolicy(pane);
        if(transition.Changed && GameMode)Broadcast();
        if(transition.Changed)ApplyRendererPolicy();
    }
    void SendPolicy(ProbeWindow pane)
    {
        if(!Live(pane) || !pane.Ready)return;
        var state=owner.ActivityState();
        Send(pane,new{kind="host-policy",documentId=pane.Ticket.DocumentId,canRun=state.CanRun,reasons=state.Reasons.ToString(),
            revision=state.Revision,controlEpoch=state.ControlEpoch,activeSeconds=activityClock.ActiveSeconds});
    }
    void StopActivityPolicy(){activityTimer.Stop();StopNativePolicy();}
    void DisposeActivityPolicy(){activityTimer.Dispose();DisposeNativePolicy();}
    async Task RunPolicyProbe(ProbeWindow read,ProbeWindow control,string ownerId,string originalRequest)
    {
        string lease=await Activate(control);
        SetHostPause(PauseReason.Manual,true);
        await Wait(control,s=>Field(s,"hostPolicy")!=null && !(bool)Field(s,"hostPolicy")["canRun"] && s["lease"]==null);
        double paused=activityClock.ActiveSeconds;
        Reply(await Command(control,lease,Guid.NewGuid().ToString("D"),1,2),false,"host-paused");
        await Task.Delay(350);
        Require(activityClock.ActiveSeconds==paused,"Paused owner clock advanced.");LockHeld();
        checks.Add("host manual pause rejects real page commands and freezes owner activity time without releasing storage");
        SetHostPause(PauseReason.Fullscreen,true);SetHostPause(PauseReason.Manual,false);
        await Wait(read,s=>Field(s,"hostPolicy")!=null && (string)Field(s,"hostPolicy")["reasons"]=="Fullscreen");
        Require(!owner.ActivityState().CanRun && activityClock.ActiveSeconds==paused,"Partial reason clearing resumed activity.");
        checks.Add("controlled overlapping host pause reasons remain paused after only one reason clears");
        SetHostPause(PauseReason.Fullscreen,false);
        await Wait(control,s=>Field(s,"hostPolicy")!=null && (bool)Field(s,"hostPolicy")["canRun"] && s["lease"]==null);
        Reply(await Command(control,lease,Guid.NewGuid().ToString("D"),1,2),false,"inactive-lease");
        double resumedAt=activityClock.ActiveSeconds;await Task.Delay(150);
        double activeDelta=activityClock.ActiveSeconds-resumedAt;
        Require(activeDelta>0 && activeDelta<.35,"Resume caught up paused time or did not advance.");
        SameOwner(await Ready(read),ownerId,2,10);
        checks.Add("host resume rebases monotonic activity time and does not restore the previous input lease");
        string pending=BeginActivation(control);Require(pending!=null,"Pending policy request missing.");
        SetHostPause(PauseReason.Manual,true);SetHostPause(PauseReason.Manual,false);
        var reply=await Wait(control,s=>{var r=Field(s,"lastActivationReply");return r!=null && (string)r["requestId"]==pending;});
        Require(!(bool)Field(reply,"lastActivationReply")["accepted"] && reply["lease"]==null,"Late activation crossed pause boundary.");
        lease=await Activate(control);Reply(await Command(control,lease,originalRequest,7,0),true,null);
        Require(owner.Snapshot().Sequence==2 && owner.Snapshot().Value==10,"Policy resume duplicated stored state.");
        checks.Add("queued real activation acknowledgement cannot cross a host pause and explicit retry preserves deduplication");
        policyResult=new{passed=true,controlledSignals=true,nativeSignalsTested=false,pausedClockStable=true,
            noAutomaticLeaseRestore=true,activeSeconds=activityClock.ActiveSeconds,droppedActiveSeconds=activityClock.DroppedActiveSeconds};
    }
}
