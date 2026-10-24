using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

sealed partial class StateOwnerProbeHost
{
    readonly System.Windows.Forms.Timer deadline = new System.Windows.Forms.Timer {Interval=25000};
    readonly List<string> checks=new List<string>();
    async Task<Dictionary<string,object>> Read(Pane pane)
    {
        return json.Deserialize<Dictionary<string,object>>(await pane.View.CoreWebView2.ExecuteScriptAsync("window.ownerProbe.state()"));
    }
    static Dictionary<string,object> ObjectField(Dictionary<string,object> obj,string key)
    {
        object value;return obj!=null && obj.TryGetValue(key,out value)?value as Dictionary<string,object>:null;
    }
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    async Task<Dictionary<string,object>> Wait(Pane pane,Func<Dictionary<string,object>,bool> condition)
    {
        var timer=Stopwatch.StartNew();
        while(timer.ElapsedMilliseconds<5000 && !closing){
            if(pane.DocumentId!=null){var state=await Read(pane);if(state!=null && condition(state))return state;}
            await Task.Delay(25);
        }
        throw new InvalidOperationException("View acknowledgement timed out: "+pane.Id);
    }
    async Task<Dictionary<string,object>> Command(Pane pane,string lease,string requestId,int delta,int expectedSequence)
    {
        string transportId=Guid.NewGuid().ToString("D");
        var payload=new{kind="command",lease=lease,requestId=requestId,transportId=transportId,delta=delta,expectedSequence=expectedSequence};
        await pane.View.CoreWebView2.ExecuteScriptAsync("window.ownerProbe.send("+json.Serialize(payload)+")");
        return await Wait(pane,s=>{
            var reply=ObjectField(s,"lastReply");
            return reply!=null && (string)reply["transportId"]==transportId && (string)reply["requestId"]==requestId;
        });
    }
    void CheckReply(Dictionary<string,object> state,bool ok,string error)
    {
        var reply=ObjectField(state,"lastReply");
        Require((bool)reply["Ok"]==ok && (string)reply["Error"]==error,"Unexpected command reply.");
    }
    async void RunOwnerProbe()
    {
        deadline.Tick+=delegate{deadline.Stop();Fail(new InvalidOperationException("Owner test deadline exceeded."));};
        deadline.Start();
        try{
            var a=await Wait(observer,s=>ObjectField(s,"snapshot")!=null);
            var b=await Wait(control,s=>ObjectField(s,"snapshot")!=null);
            var snapshotA=ObjectField(a,"snapshot");var snapshotB=ObjectField(b,"snapshot");
            Require((string)snapshotA["OwnerId"]==(string)snapshotB["OwnerId"],"Views have different owners.");
            Require(b["lease"]==null,"Startup granted control without activation.");
            checks.Add("two real WebViews start with one owner and no input lease");
            string recordPath=Path.Combine(root,"retry-request-id.txt");
            string requestA=File.Exists(recordPath)?File.ReadAllText(recordPath,Encoding.UTF8):Guid.NewGuid().ToString("D");
            bool restarting=File.Exists(recordPath);
            if(!restarting)File.WriteAllText(recordPath,requestA,Encoding.UTF8);
            string lease=Guid.NewGuid().ToString("D");
            b=await Command(control,lease,Guid.NewGuid().ToString("D"),1,(int)store.Sequence);
            CheckReply(b,false,"inactive-lease");
            checks.Add("interaction without a current host lease is rejected");
            a=await Command(observer,lease,Guid.NewGuid().ToString("D"),1,(int)store.Sequence);
            CheckReply(a,false,"read-only-view");
            checks.Add("wallpaper view cannot mutate shared state");
            lease=GrantControl();
            await Wait(control,s=>(string)s["lease"]==lease);
            b=await Command(control,lease,requestA,7,0);
            CheckReply(b,true,null);
            var receipt=ObjectField(ObjectField(b,"lastReply"),"Receipt");
            Require((int)receipt["Value"]==7 && (int)receipt["Sequence"]==1,"Original durable receipt changed.");
            Require((bool)receipt["Duplicate"]==restarting,"Restart deduplication disagrees with journal.");
            await Wait(observer,s=>(int)ObjectField(s,"snapshot")["Value"]==store.Value);
            checks.Add(restarting?"explicit restart retries durable request without reapplying":"committed change is published to both real views");
            if(!restarting){
                b=await Command(control,lease,requestA,7,0);CheckReply(b,true,null);
                Require((bool)ObjectField(ObjectField(b,"lastReply"),"Receipt")["Duplicate"] && store.Sequence==1,"Retry appended another change.");
                checks.Add("repeated request returns original receipt without another commit");
                b=await Command(control,lease,requestA,8,1);CheckReply(b,false,"request-conflict");
                checks.Add("same request ID with different payload is rejected");
                string previousLease=lease,revokeTransport=Guid.NewGuid().ToString("D");
                lease=GrantControl();await Wait(control,s=>(string)s["lease"]==lease);
                var lateRevoke=new {kind="revoke",lease=previousLease,cancellationEpoch=control.CancellationEpoch,transportId=revokeTransport};
                await control.View.CoreWebView2.ExecuteScriptAsync("window.ownerProbe.send("+json.Serialize(lateRevoke)+")");
                b=await Wait(control,s=>{var r=ObjectField(s,"lastRevoke");return r!=null&&(string)r["transportId"]==revokeTransport;});
                Require(!(bool)ObjectField(b,"lastRevoke")["revoked"] && (string)b["lease"]==lease,"Late revoke removed new lease.");
                b=await Command(control,lease,requestA,7,0);CheckReply(b,true,null);
                checks.Add("late old-lease cancellation preserves current control in both host and page");
                string oldLease=lease,oldDocument=control.DocumentId;
                ReloadControl();
                b=await Wait(control,s=>(string)s["documentId"]!=oldDocument && ObjectField(s,"snapshot")!=null);
                Require(b["lease"]==null && (int)ObjectField(b,"snapshot")["Value"]==7,"Reload duplicated or activated state.");
                b=await Command(control,oldLease,Guid.NewGuid().ToString("D"),1,1);CheckReply(b,false,"inactive-lease");
                checks.Add("reloaded view receives shared snapshot and cannot use old lease");
                lease=GrantControl();await Wait(control,s=>(string)s["lease"]==lease);
                b=await Command(control,lease,Guid.NewGuid().ToString("D"),3,1);CheckReply(b,true,null);
                await Wait(observer,s=>(int)ObjectField(s,"snapshot")["Value"]==10);
                RevokeControl();await Wait(control,s=>s["lease"]==null);
                b=await Command(control,lease,Guid.NewGuid().ToString("D"),1,2);CheckReply(b,false,"inactive-lease");
                checks.Add("revocation rejects queued commands without changing the committed value");
            }
            Require(store.Value==10 && store.Sequence==2,"Expected exactly two committed changes.");
            File.WriteAllText(Path.Combine(root,restarting?"restart-summary.json":"owner-summary.json"),json.Serialize(new{
                passed=true,checks=checks.ToArray(),value=store.Value,sequence=store.Sequence,
                realWebViews=2,nativeMouseTested=false,gameLoaded=false
            }),Encoding.UTF8);
            deadline.Stop();Close();
        }catch(Exception e){deadline.Stop();Fail(e);}
    }
}
