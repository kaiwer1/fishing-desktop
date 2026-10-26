using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

sealed partial class DualWindowContext
{
    readonly bool threeScene;
    readonly bool sceneContext;
    int sceneLoads;
    double sceneDrawRate;
    object sceneResult;
    void ReceiveSceneFault(ProbeWindow pane,Dictionary<string,object> message,string document)
    {
        // Standalone context/viewport probes deliberately test page-local faults
        // without a running owner clock. Only the host's policy mode closes it.
        string reason=null;
        if(!threeScene || !policySelfTest || pane.Ticket.CanControl || !Live(pane) || !pane.Ready ||
            pane.AwaitingNavigationStart || document!=pane.Ticket.DocumentId || !SceneFaultRecord.TryRead(message,pane.Ticket.DocumentId,out reason)){
            Send(pane,new{kind="scene-fault-reply",documentId=pane.Ticket.DocumentId,transportId=(string)message["transportId"],ignored=true});return;
        }
        StopForSceneFault(reason,"page");
    }
    void StopForSceneFault(string reason,string origin)
    {
        if(stopping)return;
        Dictionary<string,object> record=null;
        try{
            SetHostPause(FishingDesktop.Diagnostics.HostPolicy.PauseReason.SceneFault,true);
            var snapshot=owner.Snapshot();var policy=owner.ActivityState();
            bool? oldCommandRejectedByPause=null;ProbeWindow control;
            if(sceneFaultSelfTest && windows.TryGetValue(true,out control)){
                var rejected=owner.Execute(control.Ticket,control.Ticket.DocumentId,faultProbeLease,Guid.NewGuid().ToString("D"),1,snapshot.Sequence);
                oldCommandRejectedByPause=!rejected.Ok && rejected.Error=="host-paused" && rejected.Snapshot.Sequence==snapshot.Sequence;
            }
            record=new Dictionary<string,object>{{"schema",1},{"reason",reason},{"origin",origin},{"currentReadOnlyDocument",true},
                {"ownerId",snapshot.OwnerId},{"sequence",snapshot.Sequence},{"value",snapshot.Value},{"canRun",policy.CanRun},
                {"reasons",policy.Reasons.ToString()},{"activeSeconds",activityClock.ActiveSeconds},{"oldCommandRejectedByPause",oldCommandRejectedByPause},
                {"watchdogSamples",watchdogSamples},{"watchdogRequests",watchdogRequests},{"watchdogLateIgnored",watchdogLateIgnored},
                {"recordWriteFailed",false},{"explicitRestartRequired",true},{"gameLoaded",false}};
            try{System.IO.File.WriteAllText(System.IO.Path.Combine(root,"scene-fault.json"),json.Serialize(record),System.Text.Encoding.UTF8);}
            catch(Exception){record["recordWriteFailed"]=true;}
        }catch(Exception){/* Diagnostic evidence is best effort; failure must still close. */}
        finally{
            if(record!=null)try{System.IO.File.WriteAllText(System.IO.Path.Combine(root,"scene-fault-exit.json"),json.Serialize(record),System.Text.Encoding.UTF8);}catch(Exception){}
            Fail(new InvalidOperationException("Current three-dimensional draw view failed: "+reason+"; explicit host restart required."));
        }
    }
    async Task RunContextProbe(ProbeWindow read,ProbeWindow control)
    {
        string ownerId=owner.Snapshot().OwnerId,oldDocument=read.Ticket.DocumentId;
        await StartVisual(read);
        await VerifyThreeScene(read,true);
        string injected=await read.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.visual.loseContextForProbe()");
        Require(injected=="true","Own WebGL context loss extension unavailable; no fault was injected.");
        Dictionary<string,object> state=null;
        for(int attempt=0;attempt<100;attempt++){
            state=await VisualState(read);
            if((string)state["error"]=="webgl-context-lost" && !(bool)state["running"] && (bool)state["contextLost"])break;
            await Task.Delay(25);
        }
        Require(state!=null && (string)state["error"]=="webgl-context-lost" && !(bool)state["running"] &&
            (bool)state["contextLost"] && state["proof"]==null,"Context loss failed to stop drawing and invalidate old proof.");
        checks.Add("own WebGL context loss is observed and invalidates geometry pixel proof without a silent fallback");
        long stoppedFrames=Number(state,"frames");await Task.Delay(600);state=await VisualState(read);
        Require(Number(state,"frames")==stoppedFrames && !(bool)state["running"],"Faulted context continued scheduling draws.");
        checks.Add("draw callback count remains frozen after the real context loss event");
        string blocked=await read.View.CoreWebView2.ExecuteScriptAsync("(()=>{const v=window.dualProbe.visual;v.start();v.pause();v.resume();try{v.inspectViewport();return false}catch{return true}})()");
        await Task.Delay(200);state=await VisualState(read);
        Require(blocked=="true" && Number(state,"frames")==stoppedFrames && !(bool)state["running"] && state["proof"]==null &&
            (string)state["error"]=="webgl-context-lost","Faulted context restarted or supplied stale proof.");
        checks.Add("start resume and controlled draws cannot bypass a latched context fault");
        read.Close();read.Dispose();LockHeld();read=await Open(false);await Ready(read);
        Require(read.Ticket.DocumentId!=oldDocument,"Context recovery reused the old document.");
        await StartVisual(read);
        await VerifyThreeScene(read,true);
        SameOwner(await Ready(read),ownerId,0,0);var current=await Ready(control);SameOwner(current,ownerId,0,0);
        Require(current["lease"]==null,"Recreated view granted interaction authority.");LockHeld();
        checks.Add("explicit replacement creates a healthy new scene while preserving owner lock state and disabled interaction");
        System.IO.File.WriteAllText(System.IO.Path.Combine(root,"context-summary.json"),json.Serialize(new{passed=true,checks=checks.ToArray(),
            ownContextExtensionOnly=true,hardwareDeviceLossTested=false,desktopAttachmentTested=false,gameLoaded=false,sceneLoads=sceneLoads,
            sequence=owner.Snapshot().Sequence,value=owner.Snapshot().Value,oldDocumentReplaced=true,normalExplicitReplacement=true}),System.Text.Encoding.UTF8);
    }
    async Task<bool> LoadScenePage(ProbeWindow pane,ulong navigation,int generation)
    {
        await pane.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.installLakeScene()");
        for(int attempt=0;attempt<240;attempt++){
            if(!Live(pane) || pane.Navigation!=navigation || pane.NavigationGeneration!=generation || pane.AwaitingNavigationStart)return false;
            string raw=await pane.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.state()");
            var state=json.Deserialize<Dictionary<string,object>>(raw);
            if(state!=null){
                if(state["sceneLoadError"]!=null)throw new InvalidOperationException("Placeholder lake module could not initialize.");
                if((string)state["sceneKind"]=="webgl3d"){sceneLoads++;return true;}
            }
            await Task.Delay(25);
        }
        throw new InvalidOperationException("Placeholder lake module readiness timed out.");
    }
    async Task VerifyThreeScene(ProbeWindow pane,bool measureRate)
    {
        var timer=Stopwatch.StartNew();Dictionary<string,object> state=null,proof=null;
        while(timer.ElapsedMilliseconds<6000 && Live(pane)){
            state=await VisualState(pane);proof=Field(state,"proof");
            if(state["error"]!=null)throw new InvalidOperationException("Placeholder lake renderer reported an error.");
            if(proof!=null && Number(state,"frames")>=3)break;
            await Task.Delay(30);
        }
        Require(state!=null && (string)state["kind"]=="webgl3d" && proof!=null && Number(proof,"triangles")>10000 &&
            Number(proof,"distinctSampleColors")>=3 && Number(proof,"glError")==0 && !(bool)proof["contextLost"],"WebGL scene geometry or pixel proof was missing.");
        if(measureRate){
            var before=state;await Task.Delay(2000);state=await VisualState(pane);
            double seconds=(Convert.ToDouble(state["sampledAt"])-Convert.ToDouble(before["sampledAt"]))/1000;
            sceneDrawRate=(Number(state,"frames")-Number(before,"frames"))/seconds;
            Require(seconds>=1.8 && sceneDrawRate>=20 && sceneDrawRate<=33 && (bool)state["running"],"Placeholder lake draw callback cadence was outside its diagnostic range.");
            checks.Add("real WebView renders the original placeholder environment with nonuniform WebGL pixels and geometry without loading game state");
            checks.Add("placeholder lake uses the shared scheduler with measured draw callbacks within its 30 per second target tolerance");
        }else{
            Require(sceneLoads>=5,"Recreated desktop views did not retain three-dimensional scene selection.");
            Require(!policySelfTest || watchdogSamples>=3,"Runtime draw watchdog coverage was insufficient.");
            sceneResult=new{passed=true,sceneKind="webgl3d",placeholderEnvironment=true,gameLoaded=false,sceneLoads=sceneLoads,
                finalRecreatedViewVerified=true,triangles=Number(proof,"triangles"),sampleColors=Number(proof,"distinctSampleColors"),glError=Number(proof,"glError"),
                width=Number(state,"width"),height=Number(state,"height"),targetDrawCallbacksPerSecond=30,measuredDrawCallbacksPerSecond=sceneDrawRate,
                runtimeWatchdogEnabled=policySelfTest,watchdogSamples=watchdogSamples,watchdogRequests=watchdogRequests,watchdogLateIgnored=watchdogLateIgnored,
                presentedFpsMeasured=false,nativeFullscreenTested=false};
        }
    }
}
