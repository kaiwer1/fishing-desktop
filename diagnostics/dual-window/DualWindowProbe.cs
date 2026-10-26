using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

sealed partial class DualWindowContext
{
    readonly System.Windows.Forms.Timer deadline=new System.Windows.Forms.Timer {Interval=55000};
    readonly List<string> checks=new List<string>();
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static Dictionary<string,object> Field(Dictionary<string,object> value,string key)
    {
        object field;return value!=null && value.TryGetValue(key,out field)?field as Dictionary<string,object>:null;
    }
    static long Number(Dictionary<string,object> value,string key){return Convert.ToInt64(value[key]);}
    async Task<Dictionary<string,object>> Wait(ProbeWindow pane,Func<Dictionary<string,object>,bool> condition)
    {
        var timer=Stopwatch.StartNew();
        while(timer.ElapsedMilliseconds<6000 && Live(pane)){
            if(pane.Ready){
                string raw=await pane.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.state()");
                if(!Live(pane))break;
                var value=json.Deserialize<Dictionary<string,object>>(raw);
                if(value!=null && condition(value))return value;
            }
            await Task.Delay(25);
        }
        throw new InvalidOperationException("Real page acknowledgement timed out.");
    }
    Task<Dictionary<string,object>> Ready(ProbeWindow pane)
    {
        return Wait(pane,state=>Field(state,"snapshot")!=null);
    }
    async Task<string> Activate(ProbeWindow pane)
    {
        string previous=(string)(await Ready(pane))["lease"];
        string request=BeginActivation(pane);
        Require(request!=null,"Explicit activation request was not created.");
        var state=await Wait(pane,s=>{var reply=Field(s,"lastActivationReply");return reply!=null &&
            (string)reply["requestId"]==request && (bool)reply["accepted"] && s["lease"]!=null && (string)s["lease"]!=previous;});
        return (string)state["lease"];
    }
    async Task<Dictionary<string,object>> Command(ProbeWindow pane,string lease,string request,int delta,int expected,string document=null)
    {
        string transport=Guid.NewGuid().ToString("D");
        var command=new {kind="command",documentId=document??pane.Ticket.DocumentId,lease=lease,requestId=request,transportId=transport,delta=delta,expectedSequence=expected};
        await pane.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.sendRaw("+json.Serialize(command)+")");
        return await Wait(pane,state=>{var reply=Field(state,"lastReply");return reply!=null && (string)reply["transportId"]==transport;});
    }
    static void Reply(Dictionary<string,object> state,bool ok,string error)
    {
        var reply=Field(state,"lastReply");Require((bool)reply["Ok"]==ok && (string)reply["Error"]==error,"Unexpected command reply.");
    }
    void LockHeld()
    {
        bool blocked=false;
        try{using(var other=new DurableCounterStore(Path.Combine(root,"counter.journal.jsonl"))) { }}catch(IOException){blocked=true;}
        Require(blocked,"Closing a view released the owner's journal lock.");
    }
    void SameOwner(Dictionary<string,object> state,string ownerId,long sequence,long value)
    {
        var snapshot=Field(state,"snapshot");Require((string)snapshot["OwnerId"]==ownerId && Number(snapshot,"Sequence")==sequence && Number(snapshot,"Value")==value,"Owner or committed state changed with window lifetime.");
    }
    async void RunProbe()
    {
        try{
            var read=windows[false];var control=windows[true];
            var a=await Ready(read);var b=await Ready(control);
            if(sceneFaultSelfTest){await RunSceneFaultProbe(read,control);return;}
            if(sceneContext){await RunContextProbe(read,control);Shutdown();return;}
            if(sceneViewport){await RunViewportProbe(read,control);Shutdown();return;}
            if(desktopMode){CheckDesktop(read);checks.Add("real read-only WebView initializes inside a hidden existing desktop layer without activation");}
            if(startupPreparation){
                Require(startupCalls==1 && startupRequestCalls<=1 && shellLayerRequest.MessageAttempts<=1,"Startup preparation violated its bounded initial path.");
                checks.Add("explicit startup preparation precedes native attachment and retains one request budget across view lifetime");
            }
            if(visibleDesktopTest)await VerifyVisibleDesktop(read);
            string ownerId=owner.Snapshot().OwnerId;
            Require(owner.Snapshot().Sequence==0,"Self-test requires a new private journal.");
            SameOwner(a,ownerId,0,0);SameOwner(b,ownerId,0,0);Require(b["lease"]==null,"Fresh page had authority.");
            checks.Add("two independent real WebViews share one owner and start without authority");
            string fake=Guid.NewGuid().ToString("D"),request=Guid.NewGuid().ToString("D");
            Reply(await Command(read,fake,Guid.NewGuid().ToString("D"),1,0),false,"read-only-view");
            Reply(await Command(control,fake,Guid.NewGuid().ToString("D"),1,0),false,"inactive-lease");
            checks.Add("read-only and unactivated interaction commands rejected through WebView messages");
            string lease=await Activate(control);
            b=await Command(control,lease,request,7,0);Reply(b,true,null);
            a=await Wait(read,s=>Number(Field(s,"snapshot"),"Sequence")==1);SameOwner(a,ownerId,1,7);
            checks.Add("committed state is acknowledged and published to the other window");
            string oldDocument=control.Ticket.DocumentId,oldLease=lease;
            control.Close();control.Dispose();LockHeld();
            Require(Live(read) && owner.Snapshot().OwnerId==ownerId,"Interaction close disposed read-only view or owner.");
            control=await Open(true);b=await Ready(control);SameOwner(b,ownerId,1,7);Require(b["lease"]==null,"Reopened interaction was authorized.");
            Reply(await Command(control,oldLease,Guid.NewGuid().ToString("D"),1,1,oldDocument),false,"stale-document");
            Reply(await Command(control,oldLease,Guid.NewGuid().ToString("D"),1,1),false,"inactive-lease");
            checks.Add("closing and reopening interaction preserves owner lock and rejects old document and lease");
            lease=await Activate(control);
            string revokeTransport=Guid.NewGuid().ToString("D");
            var late=new{kind="revoke",documentId=oldDocument,lease=oldLease,cancellationEpoch=100,transportId=revokeTransport};
            await control.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.sendRaw("+json.Serialize(late)+")");
            b=await Wait(control,s=>{var reply=Field(s,"lastRevoke");return reply!=null && (string)reply["transportId"]==revokeTransport;});
            Require(!(bool)Field(b,"lastRevoke")["revoked"] && (string)b["lease"]==lease,"Late closed-document revoke affected replacement.");
            b=await Command(control,lease,request,7,0);Reply(b,true,null);
            Require((bool)Field(Field(b,"lastReply"),"Receipt")["Duplicate"] && owner.Snapshot().Sequence==1,"Retry duplicated a commit.");
            Reply(await Command(control,lease,request,8,1),false,"request-conflict");
            checks.Add("replayed closed-document cancellation and duplicate/conflicting requests preserve current state");
            // The desktop path also tests direct Dispose, which does not raise
            // FormClosing, while the ordinary path retains its Close coverage.
            if(desktopMode)read.Dispose();else{read.Close();read.Dispose();}
            LockHeld();read=await Open(false);a=await Ready(read);SameOwner(a,ownerId,1,7);
            b=await Command(control,lease,Guid.NewGuid().ToString("D"),3,1);Reply(b,true,null);
            a=await Wait(read,s=>Number(Field(s,"snapshot"),"Sequence")==2);SameOwner(a,ownerId,2,10);
            checks.Add("read-only window recreation preserves interaction lease and receives later commits");
            control.Close();control.Dispose();read.Close();read.Dispose();LockHeld();
            Require(windows.Count==0 && owner.Snapshot().OwnerId==ownerId && owner.Snapshot().Sequence==2,"No-window interval terminated owner.");
            double zeroWindowClock=activityClock.ActiveSeconds;
            if(policySelfTest)Require(owner.ActivityState().Reasons==FishingDesktop.Diagnostics.HostPolicy.PauseReason.NoViews,"Zero windows were not paused.");
            // Message-loop yield proves continuation does not depend on an open Form.
            await Task.Delay(policySelfTest?250:50);
            if(policySelfTest){Require(activityClock.ActiveSeconds==zeroWindowClock,"Zero-window activity clock advanced.");zeroWindowPolicyVerified=true;}
            read=await Open(false);control=await Open(true);a=await Ready(read);b=await Ready(control);
            SameOwner(a,ownerId,2,10);SameOwner(b,ownerId,2,10);Require(b["lease"]==null,"Both-window rebuild restored old authority.");
            checks.Add("zero-window interval retains controller, journal lock and committed sequence");
            lease=await Activate(control);
            b=await Command(control,lease,request,7,0);Reply(b,true,null);
            Require((bool)Field(Field(b,"lastReply"),"Receipt")["Duplicate"] && owner.Snapshot().Sequence==2,"Both-window rebuild lost deduplication.");
            oldDocument=control.Ticket.DocumentId;oldLease=lease;
            ulong obsoleteNavigation=control.Navigation;int obsoleteGeneration=control.NavigationGeneration;Reload(control);
            b=await Wait(control,s=>(string)s["documentId"]!=oldDocument && Field(s,"snapshot")!=null);
            SameOwner(b,ownerId,2,10);Require(b["lease"]==null,"Reload granted authority.");
            Reply(await Command(control,oldLease,Guid.NewGuid().ToString("D"),1,2,oldDocument),false,"stale-document");
            Reply(await Command(control,oldLease,Guid.NewGuid().ToString("D"),1,2),false,"inactive-lease");
            checks.Add("reloading an independent page keeps state but requires a fresh authority grant");
            NavigationError(control,obsoleteNavigation,obsoleteGeneration,new OperationCanceledException("Controlled obsolete navigation completion."));
            Require(!stopping,"An obsolete navigation exception stopped the owner.");
            lease=await Activate(control);
            Reply(await Command(control,lease,request,7,0),true,null);
            checks.Add("controlled obsolete navigation exception is ignored while current real page remains usable");
            await control.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.cancel()");
            await Wait(control,s=>s["lease"]==null && Field(s,"lastRevoke")!=null && (bool)Field(s,"lastRevoke")["revoked"]);
            Reply(await Command(control,lease,Guid.NewGuid().ToString("D"),1,2),false,"inactive-lease");
            checks.Add("page cancellation acknowledgement prevents later commands using the revoked lease");
            string cancelledRequest=BeginActivation(control);Revoke(control);
            b=await Wait(control,s=>{var r=Field(s,"lastActivationReply");return r!=null && (string)r["requestId"]==cancelledRequest;});
            Require(!(bool)Field(b,"lastActivationReply")["accepted"] && b["lease"]==null,"Cancellation allowed late activation.");
            string replayTransport=Guid.NewGuid().ToString("D");
            var replay=new{kind="activation-ready",documentId=control.Ticket.DocumentId,requestId=cancelledRequest,
                cancellationEpoch=(int)Number(b,"epoch"),lease="",transportId=replayTransport};
            await control.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.sendRaw("+json.Serialize(replay)+")");
            b=await Wait(control,s=>{var r=Field(s,"lastActivationReply");return r!=null && (string)r["transportId"]==replayTransport;});
            Require(!(bool)Field(b,"lastActivationReply")["accepted"] && b["lease"]==null,"Repeated activation acknowledgement revived authority.");
            lease=await Activate(control);
            Reply(await Command(control,lease,request,7,0),true,null);
            checks.Add("epoch handshake requires current explicit request and cancelled acknowledgements cannot revive authority");
            if(desktopMode){
                CheckDesktop(read);
                Require(desktopAttachments==3 && desktopDetachments==2 && desktopLeases.Count==1,"Desktop close/reopen did not release old native parents.");
                checks.Add("desktop WebView direct dispose and close release prior native attachments while retaining owner state");
                // Change only our hidden child's native geometry. The real UI
                // timer must detect it; no Explorer window is altered/restarted.
                var ownLease=desktopLeases[read];
                var ownState=desktopApi.ReadWindow(ownLease.WindowHandle);
                var changed=ownState.Bounds;changed.Right--;
                var moved=desktopApi.Position(ownLease.WindowHandle,ownState.Parent,changed,FishingDesktop.Diagnostics.DesktopLayer.DesktopLayerController.SafePositionFlags);
                Require(moved.Success,"Controlled own-window geometry change failed.");
                var lossWait=Stopwatch.StartNew();
                while(windows.ContainsKey(false) && !stopping && lossWait.ElapsedMilliseconds<4000)await Task.Delay(25);
                Require(!stopping && !windows.ContainsKey(false) && desktopLosses==1 && desktopLastLossReason=="attached-window-changed","Timer did not retire the invalid desktop view.");
                Require(Live(control) && owner.Snapshot().OwnerId==ownerId,"Desktop loss terminated interaction or owner.");
                LockHeld();
                await Wait(control,s=>s["lease"]==null);
                Reply(await Command(control,lease,Guid.NewGuid().ToString("D"),1,2),false,"inactive-lease");
                checks.Add("real timer detects controlled own-window geometry loss and revokes control without releasing owner lock");
                read=await Open(false);a=await Ready(read);SameOwner(a,ownerId,2,10);CheckDesktop(read);
                lease=await Activate(control);Reply(await Command(control,lease,request,7,0),true,null);
                Require(desktopAttachments==4 && desktopDetachments==3 && desktopLeases.Count==1 && owner.Snapshot().Sequence==2,"Explicit view recovery lost owner or duplicated state.");
                checks.Add("explicit desktop view reopen after controlled loss retains committed state and deduplication");
            }
            if(policySelfTest)await RunPolicyProbe(read,control,ownerId,request);
            if(policySelfTest)await RunAvailabilityProbe(read,control,ownerId,request);
            if(nativePolicySelfTest){
                if(displayOnlySelfTest){await PrepareNativePresentation(read);StartNativePolicy();}
                else await RunNativePolicyProbe(read,control,ownerId,request);
            }
            if(nativePolicySelfTest)await RunControlledSignals(read,control,ownerId,request);
            if(nativePolicySelfTest)read=await RunControlledDisplay(read,control,ownerId,request);
            if(rendererSelfTest)await RunRendererProbe(read,control,ownerId,request);
            if(soakSeconds>0)read=await RunSoak(read,control,ownerId,request);
            if(threeScene)await VerifyThreeScene(read,false);
            File.WriteAllText(Path.Combine(root,"webview-summary.json"),json.Serialize(new{
                passed=true,checks=checks.ToArray(),ownerId=ownerId,value=owner.Snapshot().Value,sequence=owner.Snapshot().Sequence,
                realWebViewMessages=true,independentWindows=true,nativeInputTested=false,gameLoaded=false,scene=sceneResult,
                invisibleNoActivateMode=!visibleDesktopTest,browserCrashTested=false,desktopLayer=desktopMode,hostPolicy=policyResult,availability=availabilityResult,nativePolicy=nativePolicyResult,nativeSignals=nativeSignalsResult,display=displayResult,renderer=rendererResult,soak=soakResult,nativeForegroundTestSkipped=displayOnlySelfTest
            }),Encoding.UTF8);
            Shutdown();
        }catch(Exception e){Fail(e);}
    }
}
