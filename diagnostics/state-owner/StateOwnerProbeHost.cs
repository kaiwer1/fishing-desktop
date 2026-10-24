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

sealed partial class StateOwnerProbeHost : Form
{
    const string PageUrl="https://fishing-owner.localhost/view.html";
    sealed class Pane
    {
        public string Id;
        public bool CanControl;
        public WebView2 View=new WebView2 { Dock=DockStyle.Fill };
        public string DocumentId;
        public ulong Navigation;
        public int CancellationEpoch;
        public int LeaseRevision;
    }
    readonly Pane observer=new Pane { Id="wallpaper",CanControl=false };
    readonly Pane control=new Pane { Id="interaction",CanControl=true };
    readonly string root=AppDomain.CurrentDomain.BaseDirectory;
    readonly JavaScriptSerializer json=new JavaScriptSerializer { MaxJsonLength=32768 };
    readonly DurableCounterStore store;
    readonly ViewAuthority authority;
    readonly bool automated;
    bool closing;
    public int ExitCode {get;private set;}

    StateOwnerProbeHost(bool automated)
    {
        this.automated=automated;
        store=new DurableCounterStore(Path.Combine(root,"counter.journal.jsonl"));
        authority=new ViewAuthority(store);
        Text="Fishing Shared State Probe"; Size=new Size(1100,650);
        if(automated){Opacity=0;ShowInTaskbar=false;}
        var bar=new FlowLayoutPanel { Dock=DockStyle.Top,Height=45 };
        var enable=new Button { Text="Enable interaction",AutoSize=true };
        var cancel=new Button { Text="Observe / cancel",AutoSize=true };
        var reload=new Button { Text="Reload interaction",AutoSize=true };
        bar.Controls.AddRange(new Control[]{enable,cancel,reload});
        enable.Click+=delegate {
            if(Form.ActiveForm!=this || control.DocumentId==null)return;
            control.View.Focus();
            GrantControl();
        };
        cancel.Click+=delegate { RevokeControl(); };
        reload.Click+=delegate { ReloadControl(); };
        var panels=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2};
        panels.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));panels.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        panels.Controls.Add(observer.View,0,0);panels.Controls.Add(control.View,1,0);
        Controls.Add(panels); Controls.Add(bar);
        Deactivate+=delegate { RevokeControl(); };
        FormClosing+=delegate { closing=true; authority.Detach(control.Id);authority.Detach(observer.Id); };
        FormClosed+=delegate {observer.View.Dispose();control.View.Dispose();store.Dispose();};
        Shown+=async delegate {
            try {
                var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"private-profile"));
                if(closing)return;
                await Initialize(observer,environment);
                if(closing)return;
                await Initialize(control,environment);
                if(automated)RunOwnerProbe();
            } catch(Exception e) { Fail(e); }
        };
    }
    protected override bool ShowWithoutActivation {get{return automated;}}
    protected override CreateParams CreateParams {
        get{var p=base.CreateParams;if(automated)p.ExStyle|=0x08000000|0x20;return p;}
    }
    async Task Initialize(Pane pane,CoreWebView2Environment environment)
    {
        await pane.View.EnsureCoreWebView2Async(environment);
        if(closing)return;
        var core=pane.View.CoreWebView2;
        core.Settings.AreBrowserAcceleratorKeysEnabled=false;
        core.Settings.AreDefaultContextMenusEnabled=false;
        core.SetVirtualHostNameToFolderMapping("fishing-owner.localhost",Path.Combine(root,"page"),CoreWebView2HostResourceAccessKind.DenyCors);
        core.NavigationStarting+=delegate(object sender,CoreWebView2NavigationStartingEventArgs e){
            if(e.Uri!=PageUrl){e.Cancel=true;RevokeControl();return;}
            authority.Detach(pane.Id);pane.DocumentId=null;pane.Navigation=e.NavigationId;
            pane.CancellationEpoch=0;pane.LeaseRevision=0;
        };
        core.NavigationCompleted+=async delegate(object sender,CoreWebView2NavigationCompletedEventArgs e){
            if(closing || e.NavigationId!=pane.Navigation)return;
            if(!e.IsSuccess){Fail(new InvalidOperationException("Diagnostic page failed to load."));return;}
            ulong navigation=e.NavigationId;
            try{
                var value=await core.ExecuteScriptAsync("window.ownerProbe.documentId");
                if(closing || navigation!=pane.Navigation)return;
                pane.DocumentId=json.Deserialize<string>(value);
                authority.Attach(pane.Id,pane.DocumentId,pane.CanControl);
                Send(pane,new {kind="role",documentId=pane.DocumentId,role=pane.CanControl?"交互视图":"壁纸只读视图"});
                SendLease(pane,null);
                Broadcast();
            }catch(Exception ex){Fail(ex);}
        };
        core.WebMessageReceived+=delegate(object sender,CoreWebView2WebMessageReceivedEventArgs e){Receive(pane,e);};
        core.ProcessFailed+=delegate{Fail(new InvalidOperationException("Dedicated WebView process failed."));};
        core.NewWindowRequested+=delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e){e.Handled=true;};
        core.Navigate(PageUrl);
    }
    void Send(Pane pane,object data)
    {
        if(!closing && pane.DocumentId!=null)pane.View.CoreWebView2.PostWebMessageAsJson(json.Serialize(data));
    }
    void Broadcast()
    {
        var snapshot=authority.Snapshot();
        foreach(var pane in new[]{observer,control})Send(pane,new{kind="snapshot",documentId=pane.DocumentId,snapshot=snapshot});
    }
    void SendLease(Pane pane,string lease){Send(pane,new{kind="lease",documentId=pane.DocumentId,
        revision=++pane.LeaseRevision,cancellationEpoch=pane.CancellationEpoch,lease=lease});}
    string GrantControl()
    {
        var lease=authority.Activate(control.Id);
        SendLease(control,lease);
        return lease;
    }
    void RevokeControl()
    {
        if(closing)return;
        authority.Revoke(control.Id);
        try{SendLease(control,null);}catch(Exception e){Fail(e);}
    }
    void ReloadControl()
    {
        if(closing || control.View.CoreWebView2==null)return;
        RevokeControl();authority.Detach(control.Id);control.DocumentId=null;
        control.View.CoreWebView2.Reload();
    }
    static bool StringField(Dictionary<string,object> m,string key,out string value)
    {
        object v;value=null;
        if(!m.TryGetValue(key,out v)||!(v is string))return false;
        value=(string)v;return true;
    }
    void Receive(Pane pane,CoreWebView2WebMessageReceivedEventArgs e)
    {
        if(closing || e.Source!=PageUrl)return;
        try{
            if(e.WebMessageAsJson.Length>16384)return;
            var m=json.Deserialize<Dictionary<string,object>>(e.WebMessageAsJson);
            string document,kind;
            if(m==null || !StringField(m,"documentId",out document) || !StringField(m,"kind",out kind))return;
            if(document!=pane.DocumentId)return;
            if(kind=="revoke"){
                object epoch;string previousLease,ack;
                if(!m.TryGetValue("cancellationEpoch",out epoch)||!(epoch is int)||(int)epoch<0||
                    !StringField(m,"lease",out previousLease)||!StringField(m,"transportId",out ack)||ack.Length>64)return;
                bool revoked=false;
                if((int)epoch>=pane.CancellationEpoch){
                    pane.CancellationEpoch=(int)epoch;
                    revoked=authority.Revoke(pane.Id,previousLease);
                    if(revoked)SendLease(pane,null);
                }
                Send(pane,new{kind="revoke-reply",documentId=document,transportId=ack,revoked=revoked});return;
            }
            if(kind!="command")return;
            string lease,requestId,transportId;object delta,expected;
            if(!StringField(m,"lease",out lease)||!StringField(m,"requestId",out requestId)||
                !StringField(m,"transportId",out transportId)||transportId.Length>64||
                !m.TryGetValue("delta",out delta)||!(delta is int)||
                !m.TryGetValue("expectedSequence",out expected)||!(expected is int))return;
            var reply=authority.Execute(pane.Id,document,lease,requestId,(int)delta,(int)expected);
            Send(pane,new{kind="reply",documentId=document,requestId=requestId,transportId=transportId,reply=reply,snapshot=reply.Snapshot});
            Broadcast();
        }catch(Exception ex){Fail(ex);}
    }
    void Fail(Exception error)
    {
        if(closing)return;
        closing=true;ExitCode=4;
        try{File.WriteAllText(Path.Combine(root,"owner-error.txt"),error.Message,Encoding.UTF8);}catch(Exception){}
        BeginInvoke(new Action(Close));
    }
    [STAThread] static int Main(string[] args)
    {
        bool automated=args.Length==1 && args[0]=="--self-test";
        try{
            using(var guard=new HostInstanceGuard()){
                if(!guard.IsOwner)return 2;
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                using(var host=new StateOwnerProbeHost(automated)){Application.Run(host);return host.ExitCode;}
            }
        }catch(Exception){return 3;}
    }
}
