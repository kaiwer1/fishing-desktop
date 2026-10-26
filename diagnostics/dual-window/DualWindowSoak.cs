using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using FishingDesktop.Diagnostics.HostPolicy;

sealed partial class DualWindowContext
{
    object soakResult;
    [DllImport("user32.dll",SetLastError=true)]static extern uint GetGuiResources(IntPtr process,uint flags);
    [DllImport("kernel32.dll")]static extern void SetLastError(uint error);
    static uint? GuiCount(IntPtr process,uint flags){SetLastError(0);uint count=GetGuiResources(process,flags);return count==0 && Marshal.GetLastWin32Error()!=0?(uint?)null:count;}
    async Task RunSoak(ProbeWindow read,ProbeWindow control,string ownerId,string originalRequest)
    {
        var clock=Stopwatch.StartNew();int samples=0,rebuilds=0,transitions=0;double rebuildAt=180;
        bool? lastManual=null;long pausedRevision=-1;double pausedClock=-1;
        string path=Path.Combine(root,"soak-heartbeats.jsonl");
        using(var writer=new StreamWriter(new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read),new UTF8Encoding(false))){
            writer.AutoFlush=true;
            while(clock.Elapsed.TotalSeconds<soakSeconds && !stopping){
                bool manual=((int)(clock.Elapsed.TotalSeconds/30)%2)==1;
                if(lastManual!=manual){SetHostPause(PauseReason.Manual,manual);lastManual=manual;transitions++;}
                var policy=owner.ActivityState();
                if(clock.Elapsed.TotalSeconds>=rebuildAt && !manual && policy.CanRun){
                    read.Dispose();LockHeld();read=await Open(false);await Ready(read);
                    desktopShown.Add(read);GuardPresentation(()=>read.Show());CheckDesktop(read);
                    await read.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.visual.start()");
                    await WaitRenderer(read,"active");rebuilds++;
                    while(rebuildAt<=clock.Elapsed.TotalSeconds)rebuildAt+=180;
                }
                Require(Live(read) && Live(control),"Soak lost a current view.");CheckDesktop(read);
                var snapshot=owner.Snapshot();
                Require(snapshot.OwnerId==ownerId && snapshot.Sequence==2 && snapshot.Value==10,"Soak changed committed owner state.");
                if(!policy.CanRun){
                    if(pausedRevision==policy.Revision)Require(activityClock.ActiveSeconds==pausedClock,"Soak paused activity time advanced.");
                    pausedRevision=policy.Revision;pausedClock=activityClock.ActiveSeconds;
                }else{pausedRevision=-1;pausedClock=-1;}
                var renderer=renderers[read];var backend=rendererBackends[read];
                bool suspended=read.View.CoreWebView2.IsSuspended;
                if(!policy.CanRun && !renderer.Pending)Require(suspended && backend.Surface.IsShown,"Soak pause lost suspension or frozen frame.");
                long? frames=null;
                if(policy.CanRun && !suspended && !renderer.Pending)frames=Number(await VisualState(read),"frames");
                // Awaiting page telemetry permits owner-thread notifications to
                // run. Mark such a crossed revision unclassified for resources.
                bool policyConsistent=owner.ActivityState().Revision==policy.Revision;
                suspended=read.View.CoreWebView2.IsSuspended;
                object row;
                using(var process=Process.GetCurrentProcess()){
                    process.Refresh();row=new{kind="soak-heartbeat",atUtc=DateTime.UtcNow.ToString("o"),elapsedSeconds=clock.Elapsed.TotalSeconds,
                        requestedSeconds=soakSeconds,ownerId=ownerId,sequence=snapshot.Sequence,value=snapshot.Value,
                        manualPause=manual,canRun=policy.CanRun,reasons=policy.Reasons.ToString(),policyRevision=policy.Revision,
                        policyConsistent=policyConsistent,
                        activeSeconds=activityClock.ActiveSeconds,droppedActiveSeconds=activityClock.DroppedActiveSeconds,
                        rendererOutcome=renderer.Outcome,rendererPending=renderer.Pending,isSuspended=suspended,frozenShown=backend.Surface.IsShown,
                        frozenWidth=backend.Surface.ImageSize.Width,frozenHeight=backend.Surface.ImageSize.Height,captures=backend.Captures,
                        captureIssue=backend.CaptureIssue,frames=frames,rebuilds=rebuilds,viewCount=windows.Count,rendererCount=renderers.Count,
                        ownerPrivateBytes=process.PrivateMemorySize64,ownerWorkingSetBytes=process.WorkingSet64,ownerCpuSeconds=process.TotalProcessorTime.TotalSeconds,
                        ownerHandleCount=process.HandleCount,ownerGdiObjects=GuiCount(process.Handle,0),ownerUserObjects=GuiCount(process.Handle,1),
                        runtimeVersion=environment.BrowserVersionString,diagnosticSceneOnly=true};
                }
                string line=json.Serialize(row);writer.WriteLine(line);File.WriteAllText(Path.Combine(root,"soak-status.json"),line,Encoding.UTF8);samples++;
                await Task.Delay(2000);
            }
        }
        Require(!stopping && samples>=soakSeconds/4,"Soak ended early or heartbeat coverage was insufficient.");
        SetHostPause(PauseReason.Manual,false);
        if(owner.ActivityState().CanRun){string lease=await Activate(control);Reply(await Command(control,lease,originalRequest,7,0),true,null);}
        LockHeld();Require(owner.Snapshot().OwnerId==ownerId && owner.Snapshot().Sequence==2,"Soak lost owner or deduplication.");
        checks.Add("bounded desktop soak cycles preserve owner state clock suspension frozen frame and view reconstruction");
        soakResult=new{passed=true,requestedSeconds=soakSeconds,elapsedSeconds=clock.Elapsed.TotalSeconds,heartbeatCount=samples,
            viewRebuilds=rebuilds,manualPolicyApplications=transitions,diagnosticSceneOnly=true,resourceTrendAccepted=false};
        File.WriteAllText(Path.Combine(root,"soak-summary.json"),json.Serialize(soakResult),Encoding.UTF8);
    }
}
