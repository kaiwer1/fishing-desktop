using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using FishingDesktop.Diagnostics.HostPolicy;
using FishingDesktop.Diagnostics.MultiOutput;

sealed class LogicalPane : Form
{
    internal readonly string Key;
    internal readonly DualWindowOwner.Ticket Ticket;
    internal readonly WebView2 View=new WebView2{Dock=DockStyle.Fill};
    internal bool Ready;
    internal ulong Navigation;
    internal int Generation;
    internal long PolicyView,PolicyTopology;
    internal bool SyntheticFullscreen;
    readonly MultiOutputContext context;
    internal LogicalPane(MultiOutputContext context,string key,DualWindowOwner.Ticket ticket)
    {
        this.context=context;Key=key;Ticket=ticket;Text="Fishing logical output probe";
        Size=new Size(640,360);Opacity=0;ShowInTaskbar=false;Controls.Add(View);
        FormClosing+=delegate{context.Retire(this);};
    }
    protected override bool ShowWithoutActivation{get{return true;}}
    protected override CreateParams CreateParams{get{var value=base.CreateParams;value.ExStyle|=0x08000000|0x20|0x80;return value;}}
    protected override void Dispose(bool disposing){if(disposing)context.Retire(this);base.Dispose(disposing);}
}

// Separate logical-output protocol probe, not the production desktop controller.
sealed class MultiOutputContext : ApplicationContext
{
    const string PageUrl="https://fishing-multi.localhost/page.html";
    readonly string root=AppDomain.CurrentDomain.BaseDirectory;
    readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=16384};
    readonly Control dispatcher=new Control();
    readonly Timer deadline=new Timer{Interval=55000};
    readonly Dictionary<string,LogicalPane> panes=new Dictionary<string,LogicalPane>();
    readonly List<string> checks=new List<string>();
    readonly DualWindowOwner owner;
    readonly OutputPolicy outputPolicy=new OutputPolicy();
    long nextViewGeneration,lastAppliedInputEpoch=-1;
    CoreWebView2Environment environment;
    List<LogicalPane> closing;
    bool stopping,finished;
    int loadedDocuments;
    internal int ExitCode{get;private set;}
    internal MultiOutputContext()
    {
        owner=new DualWindowOwner(Path.Combine(root,"counter.journal.jsonl"));
        outputPolicy.ReplaceTopology(1,new[]{new OutputDefinition("left",-1920,0,1920,1080,96,96),new OutputDefinition("right",0,0,2560,1440,144,144)});
        outputPolicy.SelectControlTarget("right");ApplyOutputPolicy();
        if(dispatcher.Handle==IntPtr.Zero)throw new InvalidOperationException("Dispatcher unavailable.");
        deadline.Tick+=delegate{Fail(new InvalidOperationException("Logical multi-output deadline exceeded."));};deadline.Start();
        dispatcher.BeginInvoke(new Action(Start));
    }
    static void Need(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    static string Id(){return Guid.NewGuid().ToString("D");}
    bool Live(LogicalPane pane)
    {
        LogicalPane current;return !stopping&&pane!=null&&!pane.IsDisposed&&panes.TryGetValue(pane.Key,out current)&&
            Object.ReferenceEquals(current,pane)&&owner.IsCurrent(pane.Ticket);
    }
    async void Start()
    {
        try{
            environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"private-profile"));
            environment.BrowserProcessExited+=delegate(object sender,CoreWebView2BrowserProcessExitedEventArgs e){if(!stopping&&e.BrowserProcessExitKind==CoreWebView2BrowserProcessExitKind.Failed)Fail(new InvalidOperationException("Private browser failed."));};
            var left=await Open("left",false);var right=await Open("right",false);var control=await Open("control",true);
            await Probe(left,right,control);Shutdown();
        }catch(Exception error){Fail(error);}
    }
    async Task<LogicalPane> Open(string key,bool control)
    {
        Need(!stopping&&!panes.ContainsKey(key),"Logical output is already open or host is stopping.");
        var pane=new LogicalPane(this,key,control?owner.Open(true):owner.OpenOutput(key));panes.Add(key,pane);
        try{
            pane.Show();await pane.View.EnsureCoreWebView2Async(environment);if(!Live(pane))return pane;
            var core=pane.View.CoreWebView2;core.Settings.AreBrowserAcceleratorKeysEnabled=false;core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.AreDevToolsEnabled=false;
            core.SetVirtualHostNameToFolderMapping("fishing-multi.localhost",Path.Combine(root,"page"),CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting+=delegate(object sender,CoreWebView2NavigationStartingEventArgs e){
                if(!Live(pane)||e.Uri!=PageUrl){e.Cancel=true;if(Live(pane))owner.Revoke(pane.Ticket);return;}
                try{
                    owner.Navigating(pane.Ticket);pane.Ready=false;pane.Generation=checked(pane.Generation+1);pane.Navigation=e.NavigationId;
                    if(!pane.Ticket.CanControl){pane.PolicyTopology=outputPolicy.Snapshot().TopologyGeneration;pane.PolicyView=checked(++nextViewGeneration);
                        Need(outputPolicy.BindView(pane.Key,pane.PolicyTopology,pane.PolicyView),"Current output view did not bind.");ApplyOutputPolicy();}
                }catch(Exception error){Fail(error);}
            };
            core.NavigationCompleted+=async delegate(object sender,CoreWebView2NavigationCompletedEventArgs e){
                if(!Live(pane)||e.NavigationId!=pane.Navigation)return;ulong navigation=pane.Navigation;int generation=pane.Generation;long policyView=pane.PolicyView,policyTopology=pane.PolicyTopology;
                try{
                    if(!e.IsSuccess)throw new InvalidOperationException("Logical output navigation failed.");
                    string document=null;
                    for(int i=0;i<100;i++){
                        string raw=await core.ExecuteScriptAsync("window.multiProbe?window.multiProbe.documentId:null");
                        if(!Live(pane)||pane.Navigation!=navigation||pane.Generation!=generation)return;
                        document=json.Deserialize<string>(raw);if(document!=null)break;await Task.Delay(25);
                    }
                    Need(document!=null,"Page module did not become ready.");
                    if(!control)Need(pane.PolicyView==policyView&&pane.PolicyTopology==policyTopology&&outputPolicy.Observe(pane.Key,policyTopology,policyView,true,true,pane.SyntheticFullscreen,true),"Ready observation belongs to an old output view.");
                    owner.Attach(pane.Ticket,document);pane.Ready=true;loadedDocuments++;ApplyOutputPolicy();
                    Send(pane,new{kind="role",documentId=document,role=control?"control":pane.Ticket.OutputKey});Broadcast();
                }catch(Exception error){if(Live(pane)&&pane.Navigation==navigation&&pane.Generation==generation)Fail(error);}
            };
            core.WebMessageReceived+=delegate(object sender,CoreWebView2WebMessageReceivedEventArgs e){if(Live(pane))Receive(pane,e.Source,e.WebMessageAsJson);};
            core.ProcessFailed+=delegate{if(Live(pane))Fail(new InvalidOperationException("Private WebView failed."));};
            core.NewWindowRequested+=delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e){e.Handled=true;};
            core.PermissionRequested+=delegate(object sender,CoreWebView2PermissionRequestedEventArgs e){e.State=CoreWebView2PermissionState.Deny;};
            core.DownloadStarting+=delegate(object sender,CoreWebView2DownloadStartingEventArgs e){e.Cancel=true;};core.Navigate(PageUrl);
            return pane;
        }catch{Retire(pane);pane.Dispose();throw;}
    }
    internal void Retire(LogicalPane pane)
    {
        if(!Live(pane))return;owner.Close(pane.Ticket);panes.Remove(pane.Key);pane.Ready=false;
        if(!pane.Ticket.CanControl){outputPolicy.RetireView(pane.Key,pane.PolicyTopology,pane.PolicyView);ApplyOutputPolicy();}
    }
    void Send(LogicalPane pane,object message){if(Live(pane)&&pane.Ready)pane.View.CoreWebView2.PostWebMessageAsJson(json.Serialize(message));}
    void Broadcast(){var snapshot=owner.Snapshot();foreach(var pane in panes.Values){Send(pane,new{kind="snapshot",documentId=pane.Ticket.DocumentId,snapshot=snapshot});SendPolicy(pane);}}
    void SendPolicy(LogicalPane pane)
    {
        var state=outputPolicy.Snapshot();var rows=new List<object>();foreach(var output in state.Outputs)rows.Add(new{key=output.Definition.Key,view=output.ViewGeneration,ready=output.Ready,
            fullscreen=output.Fullscreen,localCanDraw=output.LocalCanDraw,canDraw=output.CanDraw});
        Send(pane,new{kind="policy",documentId=pane.Ticket.DocumentId,revision=state.Revision,topology=state.TopologyGeneration,inputEpoch=state.InputEpoch,
            canRun=state.CanRun,controlTarget=state.ControlTarget,controlEligible=state.ControlEligible,globalBlocks=state.GlobalBlocks.ToString(),outputs=rows.ToArray()});
    }
    void ApplyOutputPolicy()
    {
        var state=outputPolicy.Snapshot();PauseReason blocked=PauseReason.None;
        // These are separate enums. Keep the bridge explicit rather than casting.
        if((state.GlobalBlocks&OutputGlobalBlock.Startup)!=0)blocked|=PauseReason.Startup;
        if((state.GlobalBlocks&OutputGlobalBlock.Manual)!=0)blocked|=PauseReason.Manual;
        if((state.GlobalBlocks&OutputGlobalBlock.SessionUnavailable)!=0)blocked|=PauseReason.SessionUnavailable;
        if((state.GlobalBlocks&OutputGlobalBlock.PowerSuspended)!=0)blocked|=PauseReason.PowerSuspended;
        if((state.GlobalBlocks&OutputGlobalBlock.EnvironmentUnknown)!=0)blocked|=PauseReason.EnvironmentUnknown;
        if((state.GlobalBlocks&OutputGlobalBlock.TopologyChanging)!=0)blocked|=PauseReason.DisplayChanging;
        if((state.GlobalBlocks&OutputGlobalBlock.SceneFault)!=0)blocked|=PauseReason.SceneFault;
        bool anyLocal=false;foreach(var output in state.Outputs)anyLocal|=output.LocalCanDraw;
        if(state.Outputs.Count==0)blocked|=PauseReason.NoViews;else if(!anyLocal)blocked|=PauseReason.RendererUnavailable;
        const PauseReason mask=PauseReason.Startup|PauseReason.Manual|PauseReason.SessionUnavailable|PauseReason.PowerSuspended|
            PauseReason.EnvironmentUnknown|PauseReason.DisplayChanging|PauseReason.SceneFault|PauseReason.NoViews|PauseReason.RendererUnavailable;
        owner.ReplacePause(mask,blocked);
        if(lastAppliedInputEpoch!=state.InputEpoch){LogicalPane control;if(panes.TryGetValue("control",out control))owner.Revoke(control.Ticket);lastAppliedInputEpoch=state.InputEpoch;}
        Need(owner.ActivityState().CanRun==state.CanRun,"Output aggregation and owner eligibility differ.");
        foreach(var pane in panes.Values)SendPolicy(pane);
    }
    string GrantForTest(LogicalPane control)
    {
        var state=outputPolicy.Snapshot();LogicalPane target;
        if(!Live(control)||!control.Ready||!control.Ticket.CanControl||state.ControlTarget==null||!panes.TryGetValue(state.ControlTarget,out target)||!Live(target)||!target.Ready)return null;
        return outputPolicy.CanActivate(target.Key,state.TopologyGeneration,target.PolicyView,state.InputEpoch)?owner.Grant(control.Ticket):null;
    }
    void FullscreenForTest(LogicalPane pane,bool fullscreen)
    {
        pane.SyntheticFullscreen=fullscreen;
        Need(outputPolicy.Observe(pane.Key,pane.PolicyTopology,pane.PolicyView,pane.Ready,true,fullscreen,true),"Synthetic observation targeted a stale view.");ApplyOutputPolicy();
    }
    static bool Text(Dictionary<string,object> data,string key,out string value){object field;value=null;if(!data.TryGetValue(key,out field)||!(field is string))return false;value=(string)field;return true;}
    void Receive(LogicalPane pane,string source,string payload)
    {
        if(!Live(pane)||!pane.Ready||source!=PageUrl||payload.Length>8192)return;
        Dictionary<string,object> data;try{data=json.Deserialize<Dictionary<string,object>>(payload);}catch(ArgumentException){return;}catch(InvalidOperationException){return;}
        string kind,document,lease,request,transport;object delta,expected;
        if(data==null||data.Count!=7||!Text(data,"kind",out kind)||kind!="command"||!Text(data,"documentId",out document)||
            !Text(data,"lease",out lease)||!Text(data,"requestId",out request)||!Text(data,"transportId",out transport)||transport.Length>64||
            !data.TryGetValue("delta",out delta)||!(delta is int)||!data.TryGetValue("expectedSequence",out expected)||!(expected is int))return;
        try{var reply=owner.Execute(pane.Ticket,document,lease,request,(int)delta,(int)expected);
            Send(pane,new{kind="reply",documentId=pane.Ticket.DocumentId,transportId=transport,reply=reply,snapshot=reply.Snapshot});Broadcast();
        }catch(Exception error){Fail(error);}
    }
    async Task<Dictionary<string,object>> Wait(LogicalPane pane,Func<Dictionary<string,object>,bool> accept)
    {
        var timer=Stopwatch.StartNew();while(timer.ElapsedMilliseconds<6000&&Live(pane)){
            if(pane.Ready){var raw=await pane.View.CoreWebView2.ExecuteScriptAsync("window.multiProbe.state()");if(!Live(pane))break;
                var state=json.Deserialize<Dictionary<string,object>>(raw);if(state!=null&&accept(state))return state;}
            await Task.Delay(25);
        }throw new InvalidOperationException("Logical page acknowledgement timed out.");
    }
    static Dictionary<string,object> Field(Dictionary<string,object> data,string key){object value;return data!=null&&data.TryGetValue(key,out value)?value as Dictionary<string,object>:null;}
    static long Number(Dictionary<string,object> data,string key){return Convert.ToInt64(data[key]);}
    Task<Dictionary<string,object>> Ready(LogicalPane pane){return Wait(pane,s=>Field(s,"snapshot")!=null);}
    async Task<Dictionary<string,object>> Command(LogicalPane pane,string lease,string request,int delta,int expected,string document=null)
    {
        string transport=Id();var message=new{kind="command",documentId=document??pane.Ticket.DocumentId,lease=lease,requestId=request,transportId=transport,delta=delta,expectedSequence=expected};
        await pane.View.CoreWebView2.ExecuteScriptAsync("window.multiProbe.send("+json.Serialize(message)+")");
        return await Wait(pane,s=>{var reply=Field(s,"lastReply");return reply!=null&&(string)reply["transportId"]==transport;});
    }
    static void Reply(Dictionary<string,object> state,bool ok,string error){var r=Field(state,"lastReply");Need(r!=null&&(bool)r["Ok"]==ok&&(string)r["Error"]==error,"Unexpected logical command reply.");}
    async Task Same(LogicalPane pane,string id,long sequence,long value){await Wait(pane,s=>{var x=Field(s,"snapshot");return x!=null&&(string)x["OwnerId"]==id&&Number(x,"Sequence")==sequence&&Number(x,"Value")==value;});}
    async Task PolicyOnPage(LogicalPane pane,bool canRun,string target,bool leftDraw,bool rightDraw)
    {
        long revision=outputPolicy.Snapshot().Revision;
        Dictionary<string,object> lastPolicy=null;
        try{
            await Wait(pane,s=>{var p=Field(s,"policy");lastPolicy=p;if(p==null||Number(p,"revision")!=revision||(bool)p["canRun"]!=canRun||(string)p["controlTarget"]!=target)return false;
                // JavaScriptSerializer may materialize nested JSON arrays as an
                // ArrayList rather than object[]. Both implement IList.
                var rows=p["outputs"] as System.Collections.IList;if(rows==null)return false;bool left=false,right=false;
                foreach(var row in rows){var value=row as Dictionary<string,object>;if(value==null)return false;
                    if((string)value["key"]=="left")left=(bool)value["canDraw"]==leftDraw;if((string)value["key"]=="right")right=(bool)value["canDraw"]==rightDraw;}
                return left&&right;});
        }catch{
            try{
                object actualRevision=null,rows=null,actualCanRun=null,actualTarget=null;
                if(lastPolicy!=null){lastPolicy.TryGetValue("revision",out actualRevision);lastPolicy.TryGetValue("outputs",out rows);
                    lastPolicy.TryGetValue("canRun",out actualCanRun);lastPolicy.TryGetValue("controlTarget",out actualTarget);}
                File.WriteAllText(Path.Combine(root,"policy-ack-error.json"),json.Serialize(new{role=pane.Key,expectedRevision=revision,
                    actualRevision=actualRevision is int||actualRevision is long?(long?)Convert.ToInt64(actualRevision):null,
                    revisionType=actualRevision==null?null:actualRevision.GetType().FullName,
                    outputsType=rows==null?null:rows.GetType().FullName,
                    canRunType=actualCanRun==null?null:actualCanRun.GetType().FullName,
                    targetType=actualTarget==null?null:actualTarget.GetType().FullName}),Encoding.UTF8);
            }catch(Exception){}
            throw;
        }
    }
    void Locked(){bool locked=false;try{using(var other=new DurableCounterStore(Path.Combine(root,"counter.journal.jsonl"))) { }}catch(IOException){locked=true;}Need(locked,"Views released the owner journal lock.");}
    async Task Probe(LogicalPane left,LogicalPane right,LogicalPane control)
    {
        await Ready(left);await Ready(right);await Ready(control);string id=owner.Snapshot().OwnerId;
        Need(!owner.ActivityState().CanRun&&outputPolicy.Snapshot().GlobalBlocks==OutputGlobalBlock.Startup,"Startup output policy was bypassed.");
        outputPolicy.SetGlobal(OutputGlobalBlock.Startup,false);ApplyOutputPolicy();await PolicyOnPage(control,true,"right",true,true);
        Need(left.Ticket.OutputKey=="left"&&right.Ticket.OutputKey=="right"&&!left.Ticket.CanControl&&!right.Ticket.CanControl&&control.Ticket.CanControl,"Logical roles differ from native tickets.");
        Need(owner.Grant(left.Ticket)==null&&owner.Grant(right.Ticket)==null,"Readonly output granted authority.");Locked();checks.Add("three real hidden pages have separate logical tickets and one owner without readonly authority");
        string lease=GrantForTest(control),request=Id();Need(lease!=null,"Explicit policy-eligible test authority missing.");
        Reply(await Command(left,lease,Id(),1,0),false,"read-only-view");Reply(await Command(right,lease,Id(),1,0),false,"read-only-view");checks.Add("both readonly pages reject real commands even when supplied the current control lease");
        Reply(await Command(control,lease,request,7,0),true,null);await Same(left,id,1,7);await Same(right,id,1,7);await Same(control,id,1,7);checks.Add("one real control command commits plus seven and broadcasts the same owner sequence to both outputs");
        string oldLeft=left.Ticket.DocumentId;var oldTicket=left.Ticket;long oldLeftView=left.PolicyView,topology=left.PolicyTopology,epoch=outputPolicy.Snapshot().InputEpoch;left.Close();left.Dispose();Locked();
        Need(Live(right)&&Live(control),"Closing one output retired another view.");left=await Open("left",false);await Same(left,id,1,7);
        Need(!owner.Attach(oldTicket,Id())&&owner.Grant(oldTicket)==null,"Retired output ticket affected its replacement.");owner.Close(oldTicket);Need(Live(left)&&Live(right)&&Live(control),"Old close retired a replacement.");
        Need(left.PolicyView>oldLeftView&&!outputPolicy.Observe("left",topology,oldLeftView,true,true,false,true)&&!outputPolicy.RetireView("left",topology,oldLeftView),"Old output callbacks revived or retired a replacement.");
        Need(outputPolicy.Snapshot().InputEpoch==epoch,"Non-target replacement invalidated healthy target input.");
        Reply(await Command(left,lease,Id(),1,1,oldLeft),false,"stale-document");Reply(await Command(control,lease,request,7,0),true,null);
        checks.Add("non-target replacement preserves control and rejects old document ticket and policy callbacks");
        string oldRight=right.Ticket.DocumentId;long oldRightView=right.PolicyView;var cached=outputPolicy.Snapshot();right.View.CoreWebView2.Reload();await Wait(right,s=>(string)s["documentId"]!=oldRight);
        Need(right.PolicyView>oldRightView&&!outputPolicy.CanActivate("right",cached.TopologyGeneration,oldRightView,cached.InputEpoch),"Target reload accepted old activation tokens.");
        Reply(await Command(right,lease,Id(),1,1,oldRight),false,"stale-document");Reply(await Command(control,lease,Id(),1,1),false,"inactive-lease");
        lease=GrantForTest(control);Need(lease!=null,"Reload did not allow a fresh explicit target grant.");checks.Add("target reload revokes old control and old activation tokens without replacing the owner");
        cached=outputPolicy.Snapshot();Need(outputPolicy.SelectControlTarget("left"),"Target did not switch.");ApplyOutputPolicy();
        Need(!outputPolicy.CanActivate("right",cached.TopologyGeneration,right.PolicyView,cached.InputEpoch),"Old target tokens survived selection.");
        Reply(await Command(control,lease,Id(),1,1),false,"inactive-lease");lease=GrantForTest(control);Need(lease!=null,"Selected healthy output could not explicitly activate.");
        checks.Add("target selection changes input epoch and requires fresh explicit policy-bound authority");
        var duplicate=await Command(control,lease,request,7,0);Reply(duplicate,true,null);Need((bool)Field(Field(duplicate,"lastReply"),"Receipt")["Duplicate"],"Repeated request was not a receipt replay.");checks.Add("receipt replay after policy and view changes does not duplicate the confirmed counter");
        var globals=new[]{OutputGlobalBlock.Startup,OutputGlobalBlock.Manual,OutputGlobalBlock.SessionUnavailable,OutputGlobalBlock.PowerSuspended,OutputGlobalBlock.EnvironmentUnknown,OutputGlobalBlock.TopologyChanging,OutputGlobalBlock.SceneFault};
        var reasons=new[]{PauseReason.Startup,PauseReason.Manual,PauseReason.SessionUnavailable,PauseReason.PowerSuspended,PauseReason.EnvironmentUnknown,PauseReason.DisplayChanging,PauseReason.SceneFault};
        for(int i=0;i<globals.Length;i++){
            lease=GrantForTest(control);Need(lease!=null,"Global mapping case lacked healthy input.");outputPolicy.SetGlobal(globals[i],true);ApplyOutputPolicy();
            Need(owner.ActivityState().Reasons==reasons[i],"Independent policy enums were mapped incorrectly.");Reply(await Command(control,lease,Id(),1,1),false,"host-paused");
            outputPolicy.SetGlobal(globals[i],false);ApplyOutputPolicy();Reply(await Command(control,lease,Id(),1,1),false,"inactive-lease");
        }
        lease=GrantForTest(control);Need(lease!=null,"Mapping checks did not restore explicit eligibility.");checks.Add("seven global reasons map by semantics and clearing never restores an old lease");
        FullscreenForTest(left,true);await PolicyOnPage(left,true,"left",false,true);await PolicyOnPage(right,true,"left",false,true);
        Need(owner.ActivityState().CanRun&&GrantForTest(control)==null,"A covered target granted authority or stopped a healthy other output.");
        Reply(await Command(control,lease,Id(),1,1),false,"inactive-lease");checks.Add("one synthetic covered output blocks target input while the other keeps owner activity eligible");
        outputPolicy.SelectControlTarget("right");ApplyOutputPolicy();Reply(await Command(control,lease,Id(),1,1),false,"inactive-lease");lease=GrantForTest(control);Need(lease!=null,"Healthy other target could not explicitly activate.");
        checks.Add("switching from a blocked target to the healthy output still requires a fresh grant");
        FullscreenForTest(right,true);await PolicyOnPage(control,false,"right",false,false);Need(owner.ActivityState().Reasons==PauseReason.RendererUnavailable,"No drawable output did not block owner.");
        Reply(await Command(control,lease,Id(),1,1),false,"host-paused");checks.Add("covering both synthetic outputs blocks owner commands without conflating local fullscreen with a global flag");
        outputPolicy.SetGlobal(OutputGlobalBlock.Manual,true);ApplyOutputPolicy();FullscreenForTest(left,false);
        await PolicyOnPage(control,false,"right",false,false);Need(owner.ActivityState().Reasons==PauseReason.Manual,"Local recovery cleared global manual pause.");
        Reply(await Command(control,lease,Id(),1,1),false,"host-paused");checks.Add("recovering a local output cannot clear independent global manual pause");
        outputPolicy.SetGlobal(OutputGlobalBlock.Manual,false);ApplyOutputPolicy();await PolicyOnPage(control,true,"right",true,false);
        Need(GrantForTest(control)==null,"Still-covered target received input after manual recovery.");Reply(await Command(control,lease,Id(),1,1),false,"inactive-lease");
        checks.Add("manual recovery permits the healthy output but does not activate a still-covered control target");
        FullscreenForTest(right,false);Reply(await Command(control,lease,Id(),1,1),false,"inactive-lease");lease=GrantForTest(control);Need(lease!=null,"Recovered target did not permit fresh activation.");
        Reply(await Command(control,lease,Id(),3,1),true,null);await Same(left,id,2,10);await Same(right,id,2,10);await Same(control,id,2,10);checks.Add("target recovery requires fresh authority and final two ten reaches every page");
        left.Close();left.Dispose();right.Close();right.Dispose();control.Close();control.Dispose();Locked();
        Need(panes.Count==0&&owner.Snapshot().OwnerId==id&&!owner.ActivityState().CanRun,"Zero views replaced the owner or kept activity eligible.");left=await Open("left",false);await Same(left,id,2,10);Locked();checks.Add("closing all views blocks activity but preserves storage and restored output snapshots");
        var finalPolicy=outputPolicy.Snapshot();
        File.WriteAllText(Path.Combine(root,"multi-output-summary.json"),json.Serialize(new{passed=true,checks=checks.ToArray(),logicalOutputCount=2,
            simultaneousWebViews=3,loadedDocuments=loadedDocuments,runtimeVersion=environment.BrowserVersionString,sequence=2,value=10,singleOwner=true,realWebViewMessages=true,
            outputPolicyIntegrated=true,syntheticObservationsOnly=true,semanticGlobalMappingsVerified=7,hostViewGeneration=nextViewGeneration,
            topologyGeneration=finalPolicy.TopologyGeneration,policyRevision=finalPolicy.Revision,inputEpoch=finalPolicy.InputEpoch,
            controlTarget=finalPolicy.ControlTarget,policyCanRun=finalPolicy.CanRun,controlEligible=finalPolicy.ControlEligible,
            explicitNativeTestGrant=true,nativeInputTested=false,logicalOutputsOnly=true,desktopAttachmentTested=false,
            physicalMonitorsTested=false,dpiChanged=false,gameLoaded=false}),Encoding.UTF8);
    }
    void Fail(Exception error){if(stopping)return;ExitCode=4;try{File.WriteAllText(Path.Combine(root,"error.txt"),error.ToString(),Encoding.UTF8);}catch(Exception){}Shutdown();}
    void Shutdown(){if(stopping)return;closing=new List<LogicalPane>(panes.Values);foreach(var pane in closing)Retire(pane);stopping=true;deadline.Stop();dispatcher.BeginInvoke(new Action(Finish));}
    void Finish(){if(finished)return;finished=true;try{if(closing!=null)foreach(var pane in closing){try{pane.Close();pane.Dispose();}catch(Exception){ExitCode=4;}}}finally{owner.Dispose();ExitThread();}}
    protected override void Dispose(bool disposing){if(disposing){Shutdown();Finish();deadline.Dispose();dispatcher.Dispose();}base.Dispose(disposing);}
    [STAThread] static int Main(string[] args)
    {
        if(args.Length!=1||args[0]!="--self-test")return 5;
        try{using(var guard=new HostInstanceGuard()){if(!guard.IsOwner)return 2;Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);using(var context=new MultiOutputContext()){Application.Run(context);return context.ExitCode;}}}catch(Exception){return 3;}
    }
}
