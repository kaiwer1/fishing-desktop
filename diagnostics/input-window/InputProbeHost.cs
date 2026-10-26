using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// Isolated ordinary window. No WorkerW embedding, hooks or global hotkeys.
sealed partial class InputProbeHost : Form
{
    const string PageUrl = "https://fishing-input.localhost/index.html";
    readonly WebView2 view = new WebView2();
    readonly Label status = new Label();
    readonly string root = AppDomain.CurrentDomain.BaseDirectory;
    readonly InputHostLifecycle lifecycle = new InputHostLifecycle();
    readonly ActivationHandshake activation=new ActivationHandshake();
    readonly string activationView=Guid.NewGuid().ToString("D");
    readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 16384 };
    readonly string probeMode;
    ulong navigationId;
    bool closing;
    bool capturing;
    public int ExitCode { get; private set; }

    InputProbeHost(string mode,int manualSeconds,bool unattendedManual)
    {
        probeMode = mode;this.manualSeconds=manualSeconds;this.unattendedManual=unattendedManual;
        Text = "Fishing Input Window Probe";
        Size = new Size(1100,760);
        StartPosition = FormStartPosition.CenterScreen;
        if (probeMode != null || unattendedManual) { ShowInTaskbar = false; Opacity = 0; }
        var bar = new FlowLayoutPanel { Dock=DockStyle.Top, Height=70 };
        var interact = new Button { Text="Enable input (F8)", AutoSize=true };
        var observe = new Button { Text="Observe / cancel (F6)", AutoSize=true };
        var capture = new Button { Text="Save page image (F7)", AutoSize=true };
        var reload = new Button { Text="Reload diagnostic (F5)", AutoSize=true };
        status.AutoSize=true;
        bar.Controls.AddRange(new Control[]{interact,observe,capture,reload,status});
        view.Dock=DockStyle.Fill;
        Controls.Add(view); Controls.Add(bar);
        interact.Click += delegate { SetMode(true,"explicit-enable"); };
        observe.Click += delegate { SetMode(false,"explicit-observe"); };
        capture.Click += delegate { CapturePage(); };
        reload.Click += delegate { ReloadPage(); };
        view.KeyDown += delegate(object sender,KeyEventArgs e) {
            if(HandleProbeKey(e.KeyCode)) { e.Handled=true; e.SuppressKeyPress=true; }
        };
        Deactivate += delegate { SetMode(false,"host-deactivate"); };
        Resize += delegate { if(WindowState==FormWindowState.Minimized)SetMode(false,"host-minimized"); };
        FormClosing += delegate { closing=true;activation.Cancel(activationView); lifecycle.Close(); };
        FormClosed += delegate { FinishManualProbe();view.Dispose(); };
        Shown += async delegate {
            StartManualProbe();
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"private-profile"));
                if(closing)return;
                await view.EnsureCoreWebView2Async(environment);
                if(closing)return;
                var core = view.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled=false;
                core.Settings.AreBrowserAcceleratorKeysEnabled=false;
                core.SetVirtualHostNameToFolderMapping("fishing-input.localhost",Path.Combine(root,"page"),CoreWebView2HostResourceAccessKind.DenyCors);
                core.WebMessageReceived += ReceiveMessage;
                core.NavigationStarting += delegate(object sender,CoreWebView2NavigationStartingEventArgs e) {
                    if(e.Uri != PageUrl) { e.Cancel=true; SetMode(false,"navigation-blocked"); return; }
                    activation.Cancel(activationView);lifecycle.Invalidate(); navigationId=e.NavigationId;
                    status.Text="Loading; input disabled";
                };
                core.NavigationCompleted += async delegate(object sender,CoreWebView2NavigationCompletedEventArgs e) {
                    if(closing || e.NavigationId != navigationId)return;
                    int generation=lifecycle.Generation;
                    if(!e.IsSuccess) { lifecycle.Invalidate(); status.Text="Load failed; use F5 to retry"; return; }
                    try {
                        var result=await core.ExecuteScriptAsync("window.inputProbeDocumentId");
                        if(closing || e.NavigationId != navigationId)return;
                        var documentId=json.Deserialize<string>(result);
                        if(!lifecycle.Bind(generation,documentId))return;
                        SetMode(false,"page-ready");
                        if(probeMode != null)StartLifecycleProbe();
                    }
                    catch(Exception) { FailClosed("document-bind-failed"); }
                };
                core.ProcessFailed += delegate(object sender,CoreWebView2ProcessFailedEventArgs e) {
                    try { File.WriteAllText(Path.Combine(root,"process-failure-detail.json"),json.Serialize(new {
                        kind=e.ProcessFailedKind.ToString(),reason=e.Reason.ToString(),exitCode=e.ExitCode }),Encoding.UTF8); }
                    catch(Exception) { }
                    FailClosed("webview-process-failed");
                };
                core.NewWindowRequested += delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e) { e.Handled=true; };
                core.Navigate(PageUrl);
                if(probeMode != null)ArmProbeTimeout();
            }
            catch(Exception) { FailClosed("initialization-failed"); }
        };
    }
    protected override bool ShowWithoutActivation { get { return probeMode != null || manualSeconds>0; } }
    protected override CreateParams CreateParams {
        get {
            var value=base.CreateParams;
            if(probeMode!=null || unattendedManual)value.ExStyle |= 0x08000000 | 0x00000020; // no activate; mouse passes through
            return value;
        }
    }
    void ReceiveMessage(object sender,CoreWebView2WebMessageReceivedEventArgs e)
    {
        if(closing || e.Source != PageUrl)return;
        try {
            var data=e.WebMessageAsJson;
            if(data.Length>16384)return;
            var message=json.Deserialize<Dictionary<string,object>>(data);
            object documentId, epoch, kind;
            if(message==null || !message.TryGetValue("documentId",out documentId) ||
                !message.TryGetValue("cancellationEpoch",out epoch) || !message.TryGetValue("kind",out kind) ||
                !(documentId is string) || !(epoch is int) || !(kind is string))return;
            if(!lifecycle.Observe((string)documentId,(int)epoch))return;
            object requestId,focused;
            if((string)kind=="cancel" && message.TryGetValue("activationRequestId",out requestId) && requestId is string)activation.PageCancelled(activationView,(string)requestId);
            if((string)kind=="input-ready" && message.TryGetValue("requestId",out requestId) && requestId is string && message.TryGetValue("focused",out focused) && focused is bool){
                bool accepted=activation.Complete((string)requestId,activationView,lifecycle.DocumentId,lifecycle.Generation,
                    IsInputForeground() && (bool)focused,(int)epoch,lifecycle.CancellationEpoch);
                if(accepted){var command=lifecycle.Command(true,"explicit-enable",IsInputForeground(),(string)requestId);if(command!=null)view.CoreWebView2.PostWebMessageAsJson(json.Serialize(command));}
            }
            File.AppendAllText(Path.Combine(root,"input-events.jsonl"),DateTime.UtcNow.ToString("o")+" "+data+Environment.NewLine,Encoding.UTF8);
            status.Text="Input diagnostic: "+(string)kind;
        }
        catch(Exception) { FailClosed("bridge-or-log-failed"); }
    }
    bool HandleProbeKey(Keys key)
    {
        if(key==Keys.F5) { ReloadPage(); return true; }
        if(key==Keys.F6) { SetMode(false,"explicit-observe"); return true; }
        if(key==Keys.F7) { CapturePage(); return true; }
        if(key==Keys.F8) { SetMode(true,"explicit-enable"); return true; }
        return false;
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
    {
        if(HandleProbeKey(keyData))return true;
        return base.ProcessCmdKey(ref msg,keyData);
    }
    async void CapturePage()
    {
        if(closing || !lifecycle.Ready || capturing)return;
        capturing=true;
        int generation=lifecycle.Generation;
        try {
            using(var stream=File.Create(Path.Combine(root,"input-page.png")))
                await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
            if(!closing && generation==lifecycle.Generation)status.Text="Saved input-page.png";
        }
        catch(Exception) { if(!closing)status.Text="Capture unavailable"; }
        finally { capturing=false; }
    }
    void ReloadPage()
    {
        if(closing || view.CoreWebView2==null)return;
        SetMode(false,"explicit-reload");
        lifecycle.Invalidate();
        try { view.CoreWebView2.Reload(); }
        catch(Exception) { FailClosed("reload-failed"); }
    }
    void SetMode(bool enabled,string reason)
    {
        if(closing || view.IsDisposed)return;
        try {
            if(enabled){
                if(!lifecycle.Ready || !IsInputForeground())return;
                view.Focus();if(!IsInputForeground())return;
                string request=activation.Begin(activationView,lifecycle.DocumentId,lifecycle.Generation);
                view.CoreWebView2.PostWebMessageAsJson(json.Serialize(new{kind="prepare-input",documentId=lifecycle.DocumentId,requestId=request}));
            }else{
                activation.Cancel(activationView);
                var command=lifecycle.Command(false,reason,IsInputForeground());if(command!=null)view.CoreWebView2.PostWebMessageAsJson(json.Serialize(command));
            }
        }
        catch(Exception) { FailClosed("mode-dispatch-failed"); }
    }
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    bool IsInputForeground(){return Form.ActiveForm==this && WindowState!=FormWindowState.Minimized && IsHandleCreated && GetForegroundWindow()==Handle;}
    void FailClosed(string reason)
    {
        if(closing)return;
        lifecycle.Close();
        closing=true;
        ExitCode=4;
        try { File.WriteAllText(Path.Combine(root,"host-failure.json"),json.Serialize(new {reason=reason}),Encoding.UTF8); }
        catch(Exception) { }
        // Dispose after the WebView2 callback returns. Restart is always explicit.
        BeginInvoke(new Action(Close));
    }
    [STAThread]
    static int Main(string[] args)
    {
        int manualSeconds=0;
        bool unattended=args.Length>0 && args[0]=="--manual-timeout-probe";
        bool manual=unattended || (args.Length>0 && args[0]=="--manual-input-probe");
        if(manual && (args.Length!=2 || !Int32.TryParse(args[1],out manualSeconds) || manualSeconds<60 || manualSeconds>600))return 5;
        bool probe=args.Length==1 && args[0]=="--instance-probe";
        string lifecycleProbe=args.Length==1 && (args[0]=="--lifecycle-probe" || args[0]=="--failure-probe") ? args[0] : null;
        try
        {
            using(var guard=new HostInstanceGuard())
            {
                int pid=Process.GetCurrentProcess().Id;
                string folder=AppDomain.CurrentDomain.BaseDirectory;
                if(probe)
                {
                    string result="{\"pid\":"+pid+",\"owner\":"+(guard.IsOwner?"true":"false")+",\"abandoned\":"+(guard.WasAbandoned?"true":"false")+"}";
                    File.WriteAllText(Path.Combine(folder,"instance-probe."+pid+".json"),result,Encoding.UTF8);
                }
                if(!guard.IsOwner)return 2;
                if(probe)
                {
                    var timer=Stopwatch.StartNew();
                    string release=Path.Combine(folder,"instance-release."+pid+".signal");
                    while(!File.Exists(release) && timer.Elapsed.TotalSeconds<20)Thread.Sleep(50);
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using(var host=new InputProbeHost(lifecycleProbe,manualSeconds,unattended)) {
                    Application.Run(host);
                    return host.ExitCode;
                }
            }
        }
        catch(Exception) { return 3; }
    }
}
