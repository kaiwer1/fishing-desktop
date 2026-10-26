using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

// Real WebView2 transport/navigation tests, not native mouse acceptance tests.
sealed partial class InputProbeHost
{
    bool probeStarted;
    readonly List<string> probeChecks = new List<string>();
    async void ArmProbeTimeout()
    {
        await Task.Delay(20000);
        if(!closing)FailClosed("probe-timeout");
    }
    async Task<Dictionary<string,object>> PageState()
    {
        return json.Deserialize<Dictionary<string,object>>(await view.CoreWebView2.ExecuteScriptAsync("window.inputProbeSnapshot()"));
    }
    static void Check(bool condition,string failure)
    {
        if(!condition)throw new InvalidOperationException(failure);
    }
    async Task<Dictionary<string,object>> WaitState(Func<Dictionary<string,object>,bool> condition)
    {
        var timer=Stopwatch.StartNew();
        while(timer.ElapsedMilliseconds<5000 && !closing) {
            if(lifecycle.Ready) {
                var state=await PageState();
                if(state!=null && condition(state))return state;
            }
            await Task.Delay(30);
        }
        throw new InvalidOperationException("page-state-timeout");
    }
    async void StartLifecycleProbe()
    {
        if(probeStarted)return;
        probeStarted=true;
        try {
            var state=await WaitState(s=>(int)s["cancellationEpoch"]>0);
            Check(!(bool)state["enabled"] && (int)state["held"]==0,"startup-must-be-disabled");
            Check(!(bool)state["focused"],"hidden-probe-must-not-take-focus");
            probeChecks.Add("real page starts disabled without focus");
            if(probeMode=="--failure-probe") {
                // Only this private-profile control's own browser PID, supplied by WebView2.
                using(var browser=Process.GetProcessById((int)view.CoreWebView2.BrowserProcessId)) {
                    File.WriteAllText(Path.Combine(root,"failure-probe-target.json"),json.Serialize(
                        new { browserPid=browser.Id, startTicks=browser.StartTime.ToUniversalTime().Ticks, dedicatedProfile=true }),Encoding.UTF8);
                    browser.Kill();
                }
                return; // ProcessFailed must terminate the host with exit code 4.
            }
            for(int attempt=0;attempt<3;attempt++) {
                string previous=lifecycle.DocumentId;
                ReloadPage();
                state=await WaitState(s=>(string)s["documentId"]!=previous && (int)s["cancellationEpoch"]>0);
                Check(!(bool)state["enabled"] && (int)state["held"]==0,"reload-must-be-disabled");
                int before=(int)state["revision"];
                // An old-document cancel with a huge revision must not mutate the new document either.
                view.CoreWebView2.PostWebMessageAsJson(json.Serialize(new { kind="mode",documentId=previous,
                    revision=before+10000,cancellationEpoch=0,enabled=false,reason="old-document-probe" }));
                state=await WaitState(s=> {
                    var receipt=s["lastModeReceipt"] as Dictionary<string,object>;
                    return receipt!=null && (string)receipt["documentId"]==previous &&
                        (int)receipt["revision"]==before+10000 && !(bool)receipt["accepted"];
                });
                Check((int)state["revision"]==before,"old-document-command-was-accepted");
                Check(!(bool)state["enabled"] && (int)state["held"]==0,"reload-command-leaked");
                probeChecks.Add("reload "+(attempt+1)+": fresh document rejects old command");
            }
            string preparation=Guid.NewGuid().ToString("D");
            view.CoreWebView2.PostWebMessageAsJson(json.Serialize(new{kind="prepare-input",documentId=lifecycle.DocumentId,requestId=preparation}));
            state=await WaitState(s=>{var ack=s["lastInputPreparation"] as Dictionary<string,object>;return ack!=null && (string)ack["requestId"]==preparation;});
            var prepared=state["lastInputPreparation"] as Dictionary<string,object>;
            Check(!(bool)prepared["focused"] && !(bool)state["enabled"] && (int)state["held"]==0,"unfocused-preparation-was-authorized");
            probeChecks.Add("real unfocused preparation reports current epoch without granting input");
            // A real host-to-page request must not enable an unfocused diagnostic page.
            int rejectedRevision=(int)state["revision"]+1;
            view.CoreWebView2.PostWebMessageAsJson(json.Serialize(new {kind="mode",documentId=lifecycle.DocumentId,
                revision=rejectedRevision,cancellationEpoch=(int)state["cancellationEpoch"],enabled=true,reason="unfocused-probe"}));
            state=await WaitState(s=>(int)s["revision"]==rejectedRevision);
            Check(!(bool)state["enabled"] && (int)state["held"]==0,"background-enable-was-accepted");
            probeChecks.Add("unfocused page rejects enable through real WebView2 bridge");
            File.WriteAllText(Path.Combine(root,"lifecycle-summary.json"),json.Serialize(new {
                passed=true, checks=probeChecks.ToArray(), nativeMouseTested=false, gameLoaded=false
            }),Encoding.UTF8);
            Close();
        }
        catch(Exception e) {
            try { File.WriteAllText(Path.Combine(root,"lifecycle-error.txt"),e.Message,Encoding.UTF8); } catch(Exception) {}
            FailClosed("lifecycle-probe-failed");
        }
    }
}
