using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using FishingDesktop.Diagnostics.HostPolicy;
using FishingDesktop.Diagnostics.MultiOutput;
using FishingDesktop.Diagnostics.OutputTopology;

sealed partial class MultiOutputContext
{
    const int CapturedOutputLimit=4;
    readonly TopologyPolicyBridge topologyBridge;
    readonly List<object> capturedObservations=new List<object>();

    // Only this wrapper injects the one declared failure. It never changes the
    // system topology, DPI awareness or the native query's real measurement.
    sealed class FailingEnumerationQuery : ITopologyQuery
    {
        readonly ITopologyQuery real;
        internal bool FailNextEnumeration;
        internal int InjectedFailures;
        internal FailingEnumerationQuery(ITopologyQuery real){this.real=real;}
        public bool IsPerMonitorV2{get{return real.IsPerMonitorV2;}}
        public IEnumerable<RawMonitor> Enumerate()
        {
            if(FailNextEnumeration){FailNextEnumeration=false;InjectedFailures++;throw new InvalidOperationException("Declared synthetic enumeration failure.");}
            return real.Enumerate();
        }
        public DpiProbeResult Measure(RawMonitor monitor){return real.Measure(monitor);}
    }
    static void VerifyCapturedWindow(LogicalPane pane)
    {
        // These exact HWNDs belong to the just-created pane and its WebView.
        // No external window enumeration, position or content query is used.
        uint pid;uint thread=GetWindowThreadProcessId(pane.Handle,out pid);
        using(var process=Process.GetCurrentProcess()){
            Need(thread==GetCurrentThreadId()&&pid==(uint)process.Id&&!pane.Visible&&!IsWindowVisible(pane.Handle)&&!IsWindowVisible(pane.View.Handle),"Protocol window is not an owned hidden window.");
        }
        long style=GetWindowLongPtr(pane.Handle,-20).ToInt64();
        Need((style&0x08000000L)!=0,"Protocol window lost NoActivate.");
    }
    static bool SameCapturedContent(TopologySample a,TopologySample b)
    {
        if(!a.Known||!b.Known||a.Outputs.Count!=b.Outputs.Count)return false;
        for(int i=0;i<a.Outputs.Count;i++){
            var x=a.Outputs[i];var y=b.Outputs[i];
            if(x.Key!=y.Key||x.X!=y.X||x.Y!=y.Y||x.Width!=y.Width||x.Height!=y.Height||x.DpiX!=y.DpiX||x.DpiY!=y.DpiY||x.Primary!=y.Primary)return false;
        }
        return true;
    }
    bool CapturedLandscape(LogicalPane pane)
    {
        var state=outputPolicy.Snapshot();Need(state.TopologyGeneration==pane.PolicyTopology,"Landscape observation belongs to a retired topology.");
        foreach(var output in state.Outputs)if(output.Definition.Key==pane.Key)return output.Definition.Width>=output.Definition.Height;
        throw new InvalidOperationException("Captured output geometry is unavailable.");
    }
    TopologySample CaptureForProtocol(TopologyCapture capture,Win32TopologyQuery query,string phase,out TopologyApplyResult applied)
    {
        var sample=capture.Capture();applied=topologyBridge.Apply(sample);ApplyOutputPolicy();
        var rows=new List<object>();foreach(var row in sample.Outputs)rows.Add(new{key=row.Key,x=row.X,y=row.Y,width=row.Width,height=row.Height,dpiX=row.DpiX,dpiY=row.DpiY,primary=row.Primary});
        capturedObservations.Add(new{phase=phase,captureSerial=sample.CaptureSerial,known=sample.Known,reason=sample.Reason.ToString(),outputs=rows.ToArray(),
            applied=applied.ToString(),topologyGeneration=topologyBridge.TopologyGeneration,windowsCreated=query.WindowsCreated,windowsReleased=query.WindowsReleased,outstandingWindows=query.OutstandingWindows});
        File.WriteAllText(Path.Combine(root,"captured-topology-observations.json"),json.Serialize(capturedObservations.ToArray()),Encoding.UTF8);
        Need(!query.CleanupFailed&&query.OutstandingWindows==0&&query.WindowsCreated==query.WindowsReleased,"Native topology window cleanup is unconfirmed.");
        return sample;
    }
    async Task CapturedPolicyOnPage(LogicalPane pane)
    {
        var expected=outputPolicy.Snapshot();long? actualRevision=null;
        try{
            await Wait(pane,s=>{
                var policy=Field(s,"policy");if(policy==null)return false;actualRevision=Number(policy,"revision");
                if(actualRevision!=expected.Revision||Number(policy,"topology")!=expected.TopologyGeneration||Number(policy,"inputEpoch")!=expected.InputEpoch||
                    (bool)policy["canRun"]!=expected.CanRun||(bool)policy["controlEligible"]!=expected.ControlEligible||
                    (string)policy["controlTarget"]!=expected.ControlTarget||(string)policy["globalBlocks"]!=expected.GlobalBlocks.ToString())return false;
                var rows=policy["outputs"] as System.Collections.IList;if(rows==null||rows.Count!=expected.Outputs.Count)return false;
                var seen=new HashSet<string>(StringComparer.Ordinal);
                foreach(var item in rows){
                    var row=item as Dictionary<string,object>;if(row==null)return false;string key=(string)row["key"];if(!seen.Add(key))return false;
                    OutputState match=null;foreach(var output in expected.Outputs)if(output.Definition.Key==key){match=output;break;}
                    if(match==null||Number(row,"view")!=match.ViewGeneration||(bool)row["ready"]!=match.Ready||
                        (bool)row["canDraw"]!=match.CanDraw||(bool)row["localCanDraw"]!=match.LocalCanDraw)return false;
                }
                return true;
            });
        }catch{
            File.WriteAllText(Path.Combine(root,"captured-policy-ack-error.json"),json.Serialize(new{role=pane.Key,expectedRevision=expected.Revision,actualRevision=actualRevision}),Encoding.UTF8);
            throw;
        }
    }
    async Task BroadcastCaptured(List<LogicalPane> outputs,LogicalPane control,string id,long sequence,long value)
    {
        foreach(var pane in outputs){VerifyCapturedWindow(pane);await Same(pane,id,sequence,value);await CapturedPolicyOnPage(pane);}
        VerifyCapturedWindow(control);await Same(control,id,sequence,value);await CapturedPolicyOnPage(control);
    }
    async Task ProbeCapturedTopology()
    {
        var query=new Win32TopologyQuery();var injected=new FailingEnumerationQuery(query);var capture=new TopologyCapture(injected,CapturedOutputLimit);
        TopologyApplyResult applied;var first=CaptureForProtocol(capture,query,"actual-initial",out applied);
        Need(first.Known&&first.CaptureSerial==1&&applied==TopologyApplyResult.Replaced&&topologyBridge.TopologyGeneration==1&&topologyBridge.HasVerifiedTopology,"Initial actual topology is unavailable.");
        Need(first.Outputs.Count>0&&first.Outputs.Count<=CapturedOutputLimit&&!outputPolicy.Snapshot().CanRun,"Initial topology bound pages implicitly.");
        checks.Add("actual PMv2 capture establishes bounded topology generation one without Ready or authority");
        string target=null;foreach(var row in first.Outputs)if(row.Primary)target=row.Key;Need(target!=null,"Actual topology has no declared primary alias.");
        outputPolicy.SelectControlTarget(target);ApplyOutputPolicy();
        var outputs=new List<LogicalPane>();foreach(var row in first.Outputs)outputs.Add(await Open(row.Key,false));var control=await Open("control",true);
        foreach(var pane in outputs)await Ready(pane);await Ready(control);
        string ownerId=owner.Snapshot().OwnerId;Locked();
        foreach(var pane in outputs)Need(pane.Ticket.OutputKey==pane.Key&&!pane.Ticket.CanControl&&owner.Grant(pane.Ticket)==null,"Actual output ticket received control authority.");
        Need(outputPolicy.Snapshot().GlobalBlocks==OutputGlobalBlock.Startup&&!owner.ActivityState().CanRun,"Page readiness cleared Startup.");
        outputPolicy.SetGlobal(OutputGlobalBlock.Startup,false);ApplyOutputPolicy();await BroadcastCaptured(outputs,control,ownerId,0,0);
        checks.Add("each actual logical output and one control load separate strictly hidden NoActivate pages sharing one owner");
        string lease=GrantForTest(control),firstRequest=Id();Need(lease!=null,"Actual target did not permit explicit native test grant.");
        foreach(var pane in outputs)Reply(await Command(pane,lease,Id(),1,0),false,"read-only-view");
        checks.Add("every actual-output readonly page rejects a real command carrying the control lease");
        Reply(await Command(control,lease,firstRequest,7,0),true,null);await BroadcastCaptured(outputs,control,ownerId,1,7);
        checks.Add("explicit test authority commits plus seven once and broadcasts owner one seven to every page");
        var before=outputPolicy.Snapshot();var priorDocuments=new Dictionary<string,string>();foreach(var pane in outputs)priorDocuments.Add(pane.Key,pane.Ticket.DocumentId);
        int beforeLoads=loadedDocuments;var second=CaptureForProtocol(capture,query,"actual-unchanged",out applied);var unchanged=outputPolicy.Snapshot();
        Need(second.Known&&second.CaptureSerial==2&&SameCapturedContent(first,second)&&applied==TopologyApplyResult.Unchanged&&
            unchanged.TopologyGeneration==1&&unchanged.Revision==before.Revision&&unchanged.InputEpoch==before.InputEpoch&&loadedDocuments==beforeLoads,"Second actual observation changed topology or Ready.");
        foreach(var pane in outputs)Need(pane.Ready&&pane.Ticket.DocumentId==priorDocuments[pane.Key]&&pane.PolicyTopology==1,"Stable polling retired a current document.");
        var duplicate=await Command(control,lease,firstRequest,7,0);Reply(duplicate,true,null);Need((bool)Field(Field(duplicate,"lastReply"),"Receipt")["Duplicate"],"Stable capture did not preserve original receipt.");
        checks.Add("second identical actual capture advances only capture serial and preserves views epoch lease and original receipt");
        int createdBeforeFailure=query.WindowsCreated;injected.FailNextEnumeration=true;
        var unknown=CaptureForProtocol(capture,query,"synthetic-enumeration-failure",out applied);var blocked=outputPolicy.Snapshot();
        Need(!unknown.Known&&unknown.CaptureSerial==3&&unknown.Outputs.Count==0&&unknown.Reason==TopologyUnavailableReason.EnumerationFailed&&
            applied==TopologyApplyResult.Unavailable&&!topologyBridge.HasVerifiedTopology&&query.WindowsCreated==createdBeforeFailure&&injected.InjectedFailures==1,"Injected observation failure reused a positive topology.");
        Need(!blocked.CanRun&&!blocked.ControlEligible&&blocked.InputEpoch>before.InputEpoch&&owner.ActivityState().Reasons==PauseReason.DisplayChanging&&GrantForTest(control)==null,"Unknown topology did not block owner and input.");
        Reply(await Command(control,lease,Id(),1,1),false,"host-paused");await BroadcastCaptured(outputs,control,ownerId,1,7);
        checks.Add("one explicitly synthetic Enumerate failure exposes no outputs blocks owner and rejects old control without changing the system");
        outputPolicy.SetGlobal(OutputGlobalBlock.Manual,true);ApplyOutputPolicy();
        var recovered=CaptureForProtocol(capture,query,"actual-recovery",out applied);var replaced=outputPolicy.Snapshot();
        Need(recovered.Known&&recovered.CaptureSerial==4&&SameCapturedContent(first,recovered)&&applied==TopologyApplyResult.Replaced&&
            replaced.TopologyGeneration==2&&topologyBridge.HasVerifiedTopology&&!replaced.CanRun&&replaced.GlobalBlocks==OutputGlobalBlock.Manual,"Actual recovery did not replace topology or preserved another barrier incorrectly.");
        foreach(var output in replaced.Outputs)Need(!output.Bound&&!output.Ready,"Recovered topology reused old Ready.");
        foreach(var pane in outputs)Need(!outputPolicy.Observe(pane.Key,pane.PolicyTopology,pane.PolicyView,true,true,false,true)&&
            !outputPolicy.CanActivate(pane.Key,pane.PolicyTopology,pane.PolicyView,before.InputEpoch),"Old topology observation or activation revived a page.");
        checks.Add("actual recovery with identical content creates generation two rejects all prior view scopes and preserves Manual");
        foreach(var pane in outputs){var ticket=pane.Ticket;pane.Close();pane.Dispose();Need(!owner.Attach(ticket,Id())&&owner.Grant(ticket)==null,"Retired actual-output ticket revived.");owner.Close(ticket);}
        outputs.Clear();Locked();Need(Live(control)&&owner.Snapshot().OwnerId==ownerId,"Output rebuild retired control or replaced owner.");
        foreach(var row in recovered.Outputs)outputs.Add(await Open(row.Key,false));foreach(var pane in outputs)await Ready(pane);
        await BroadcastCaptured(outputs,control,ownerId,1,7);
        Need(!owner.ActivityState().CanRun&&owner.ActivityState().Reasons==PauseReason.Manual&&GrantForTest(control)==null,"New Ready cleared independent Manual pause.");
        checks.Add("explicit output retirement and hidden rebuild keep journal lock control page owner and independent pause");
        outputPolicy.SetGlobal(OutputGlobalBlock.Manual,false);ApplyOutputPolicy();
        Reply(await Command(control,lease,Id(),1,1),false,"inactive-lease");Need(owner.ActivityState().CanRun,"Verified ready recovery did not restore activity eligibility.");
        foreach(var pane in outputs)Reply(await Command(pane,lease,Id(),1,1,priorDocuments[pane.Key]),false,"stale-document");
        lease=GrantForTest(control);Need(lease!=null,"Rebuilt target did not permit a fresh explicit grant.");
        foreach(var pane in outputs)Reply(await Command(pane,lease,Id(),1,1),false,"read-only-view");
        checks.Add("clearing Manual never revives old lease or documents and only a new policy-bound native grant can write");
        string secondRequest=Id();Reply(await Command(control,lease,secondRequest,3,1),true,null);await BroadcastCaptured(outputs,control,ownerId,2,10);
        checks.Add("fresh authority commits plus three and broadcasts the same owner two ten to every rebuilt page");
        duplicate=await Command(control,lease,firstRequest,7,0);Reply(duplicate,true,null);Need((bool)Field(Field(duplicate,"lastReply"),"Receipt")["Duplicate"],"Original receipt lost across topology recovery.");
        duplicate=await Command(control,lease,secondRequest,3,1);Reply(duplicate,true,null);Need((bool)Field(Field(duplicate,"lastReply"),"Receipt")["Duplicate"],"Second receipt duplicated after recovery.");
        await BroadcastCaptured(outputs,control,ownerId,2,10);checks.Add("both original request identities replay receipts after recovery without another journal commit");
        Need(loadedDocuments==2*first.Outputs.Count+1&&nextViewGeneration==2*first.Outputs.Count&&query.WindowsCreated==6*first.Outputs.Count&&query.OutstandingWindows==0,"Document or native observation accounting differs.");
        foreach(var pane in outputs){pane.Close();pane.Dispose();}control.Close();control.Dispose();Locked();
        Need(panes.Count==0&&!owner.ActivityState().CanRun&&owner.Snapshot().OwnerId==ownerId&&owner.Snapshot().Sequence==2&&owner.Snapshot().Value==10,"Closing hidden pages lost owner or persistence.");
        checks.Add("all hidden views close normally while the same owner retains the journal lock and confirmed two ten");
        var final=outputPolicy.Snapshot();
        File.WriteAllText(Path.Combine(root,"captured-topology-summary.json"),json.Serialize(new{schemaVersion=1,passed=true,checks=checks.ToArray(),mode="captured-topology",
            currentOutputCount=first.Outputs.Count,maximumOutputs=CapturedOutputLimit,simultaneousWebViews=first.Outputs.Count+1,loadedDocuments=loadedDocuments,
            hostViewGeneration=nextViewGeneration,sequence=2,value=10,singleOwner=true,realWebViewMessages=true,explicitNativeTestGrant=true,
            topologyCaptureIntegrated=true,topologyBridgeIntegrated=true,actualKnownCaptures=3,injectedEnumerationFailures=injected.InjectedFailures,
            captureSerial=topologyBridge.LastCaptureSerial,topologyGeneration=final.TopologyGeneration,recoverySameContent=true,
            windowsCreated=query.WindowsCreated,windowsReleased=query.WindowsReleased,outstandingWindows=query.OutstandingWindows,nativeWindowCleanupConfirmed=!query.CleanupFailed,
            hiddenNoActivateProtocolWindowsVerified=true,fullscreenObservationSynthetic=true,rendererObservationSynthetic=true,landscapeObservationFromCapturedBounds=true,
            outputKeysAreCaptureAliases=true,currentDeviceOnly=true,physicalMultiMonitorAcceptance=false,mixedDpiAcceptance=false,displaySettingsChanged=false,
            externalWindowsQueried=false,contentCaptured=false,desktopAttachmentTested=false,nativeInputTested=false,gameLoaded=false,clockIntegrated=false,sdkRendererPolicyIntegrated=false,
            runtimeVersion=environment.BrowserVersionString,normalCompletion=true}),Encoding.UTF8);
    }
    [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
}
