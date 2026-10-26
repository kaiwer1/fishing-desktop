using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FishingDesktop.Diagnostics.HostPolicy;

sealed partial class DualWindowContext
{
    readonly bool sceneFaultSelfTest;
    string faultProbeLease;
    async Task RunSceneFaultProbe(ProbeWindow read,ProbeWindow control)
    {
        string ownerId=owner.Snapshot().OwnerId,request=Guid.NewGuid().ToString("D");
        rendererEnabled=true;RendererPageReady(read);await WaitRenderer(read,"active");
        await StartVisual(read);
        await VerifyThreeScene(read,true);
        faultProbeLease=await Activate(control);Reply(await Command(control,faultProbeLease,request,7,0),true,null);
        SameOwner(await Ready(read),ownerId,1,7);LockHeld();
        checks.Add("healthy real interaction commits once before the drawing fault while owner storage stays locked");
        double before=activityClock.ActiveSeconds;
        for(int scenario=0;scenario<6;scenario++){
            var message=new Dictionary<string,object>{{"kind","scene-fault"},{"documentId",read.Ticket.DocumentId},
                {"lease",""},{"transportId",Guid.NewGuid().ToString("D")},{"scene","webgl3d"},{"reason","webgl-context-lost"}};
            var sender=read;
            if(scenario==0){sender=control;message["documentId"]=control.Ticket.DocumentId;}
            if(scenario==1)message["documentId"]=Guid.NewGuid().ToString("D");
            if(scenario==2)message["reason"]="untrusted driver text";
            if(scenario==3)message["unexpected"]="field";
            if(scenario==4)message["scene"]="canvas2d";
            if(scenario==5)message["transportId"]="not-a-guid";
            await sender.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.sendRaw("+json.Serialize(message)+")");
            string transport=(string)message["transportId"];
            await Wait(sender,s=>{var reply=Field(s,"lastSceneFaultReply");return reply!=null && (string)reply["transportId"]==transport && (bool)reply["ignored"];});
            Require(Live(read) && Live(control) && owner.ActivityState().CanRun && ExitCode==0,"An ineligible fault report stopped the owner.");
        }
        checks.Add("real control old-document unknown-reason extra-field wrong-scene and invalid-transport fault reports do not stop the owner");
        await Task.Delay(150);
        Require(activityClock.ActiveSeconds>before,"Negative fault reports stopped active owner time.");
        var current=await Ready(control);Require((string)current["lease"]==faultProbeLease,"Negative reports revoked healthy interaction.");
        checks.Add("ignored fault reports preserve active time and the existing valid interaction lease");
        File.WriteAllText(Path.Combine(root,"scene-fault-preflight.json"),json.Serialize(new{passed=true,checks=checks.ToArray(),ownerId=ownerId,
            requestId=request,oldLease=faultProbeLease,expectedFaultReason="webgl-context-lost",sequence=1,value=7,invalidReportsIgnored=6,ignoredReportsAcknowledged=true,selectiveRendererEnabled=true,
            ownContextExtensionOnly=true,desktopAttachmentTested=false,hardwareDeviceLossTested=false,gameLoaded=false}),Encoding.UTF8);
        string injected=await read.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.visual.loseContextForProbe()");
        if(stopping)return;
        Require(injected=="true","No own context fault was injected.");
        await Task.Delay(3000);
        if(!stopping)throw new InvalidOperationException("Real context fault did not close the running policy host.");
    }
    internal static int VerifySceneFaultRestart()
    {
        string directory=AppDomain.CurrentDomain.BaseDirectory;var serializer=new JavaScriptSerializer();
        var before=serializer.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(directory,"scene-fault-preflight.json")));
        var fault=serializer.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(directory,"scene-fault-exit.json")));
        Require((bool)before["passed"] && !(bool)fault["canRun"] && (bool)fault["oldCommandRejectedByPause"] &&
            (string)fault["reason"]==(string)before["expectedFaultReason"],"Fault boundary evidence was missing.");
        using(var restarted=new DualWindowOwner(Path.Combine(directory,"counter.journal.jsonl"))){
            // Match InitializeActivityPolicy in a policy host. The reusable
            // counter owner alone deliberately defaults to an unpaused gate.
            restarted.SetPause(PauseReason.Startup,true);
            var snapshot=restarted.Snapshot();
            Require(snapshot.Sequence==1 && snapshot.Value==7 && snapshot.OwnerId!=(string)before["ownerId"],"Restart lost the confirmed counter or reused the old owner identity.");
            Require(!restarted.ActivityState().CanRun && restarted.ActivityState().Reasons==PauseReason.Startup,"Restart inherited a runnable owner.");
            var control=restarted.Open(true);string document=Guid.NewGuid().ToString("D");restarted.Attach(control,document);
            Require(restarted.Grant(control)==null,"Startup granted input without an explicit current action.");
            restarted.SetPause(PauseReason.Startup,false);
            var rejected=restarted.Execute(control,document,(string)before["oldLease"],Guid.NewGuid().ToString("D"),1,1);
            Require(!rejected.Ok && rejected.Error=="inactive-lease","Old input authority survived process restart.");
            string lease=restarted.Grant(control);Require(lease!=null,"Explicit new authority was unavailable.");
            var duplicate=restarted.Execute(control,document,lease,(string)before["requestId"],7,0);
            Require(duplicate.Ok && duplicate.Snapshot.Sequence==1 && duplicate.Snapshot.Value==7,"Restart repeated the committed request.");
            File.WriteAllText(Path.Combine(directory,"scene-fault-restart.json"),serializer.Serialize(new{passed=true,newProcess=true,
                confirmedStatePreserved=true,policyStartupApplied=true,initialStartupBlocked=true,oldLeaseRejected=true,explicitNewLeaseRequired=true,
                duplicateReceiptPreserved=true,sequence=1,value=7,gameLoaded=false}),Encoding.UTF8);
        }
        return 0;
    }
}
