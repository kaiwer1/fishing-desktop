using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// Isolated ordinary window. No WorkerW embedding, hooks or global hotkeys.
sealed class InputProbeHost : Form
{
    readonly WebView2 view = new WebView2();
    readonly Label status = new Label();
    readonly string root = AppDomain.CurrentDomain.BaseDirectory;
    bool ready;
    int revision;
    InputProbeHost()
    {
        Text = "Fishing Input Window Probe";
        Size = new Size(1100,760);
        StartPosition = FormStartPosition.CenterScreen;
        var bar = new FlowLayoutPanel { Dock=DockStyle.Top, Height=48 };
        var interact = new Button { Text="Enable input (F8)", AutoSize=true };
        var observe = new Button { Text="Observe / cancel (F6)", AutoSize=true };
        var capture = new Button { Text="Save page image (F7)", AutoSize=true };
        status.AutoSize=true;
        bar.Controls.AddRange(new Control[]{interact,observe,capture,status});
        view.Dock=DockStyle.Fill;
        Controls.Add(view); Controls.Add(bar);
        interact.Click += delegate { SetMode(true,"explicit-enable"); };
        observe.Click += delegate { SetMode(false,"explicit-observe"); };
        capture.Click += delegate { CapturePage(); };
        view.KeyDown += delegate(object sender,KeyEventArgs e) {
            if(HandleProbeKey(e.KeyCode)) { e.Handled=true; e.SuppressKeyPress=true; }
        };
        AcceptButton=interact;
        Deactivate += delegate { SetMode(false,"host-deactivate"); };
        Resize += delegate { if(WindowState==FormWindowState.Minimized)SetMode(false,"host-minimized"); };
        Shown += async delegate {
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"private-profile"));
                await view.EnsureCoreWebView2Async(environment);
                view.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;
                view.CoreWebView2.SetVirtualHostNameToFolderMapping("fishing-input.localhost",Path.Combine(root,"page"),CoreWebView2HostResourceAccessKind.DenyCors);
                view.CoreWebView2.WebMessageReceived += delegate(object sender,CoreWebView2WebMessageReceivedEventArgs e) {
                    if(!e.Source.StartsWith("https://fishing-input.localhost/",StringComparison.OrdinalIgnoreCase))return;
                    var data=e.WebMessageAsJson;
                    if(data.Length>16384)return;
                    File.AppendAllText(Path.Combine(root,"input-events.jsonl"),DateTime.UtcNow.ToString("o")+" "+data+Environment.NewLine,Encoding.UTF8);
                    status.Text=data;
                    if(data.Contains("\"kind\":\"ready\"")) { ready=true; SetMode(false,"page-ready"); }
                };
                view.CoreWebView2.NavigationStarting += delegate(object sender,CoreWebView2NavigationStartingEventArgs e) {
                    if(!e.Uri.StartsWith("https://fishing-input.localhost/",StringComparison.OrdinalIgnoreCase))e.Cancel=true;
                };
                view.CoreWebView2.NewWindowRequested += delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e) { e.Handled=true; };
                view.CoreWebView2.Navigate("https://fishing-input.localhost/index.html");
            }
            catch(Exception e) { status.Text="Initialization failed: "+e.Message; }
        };
    }
    bool HandleProbeKey(Keys key)
    {
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
        if(!ready)return;
        try {
            using(var stream=File.Create(Path.Combine(root,"input-page.png")))
                await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
            status.Text="Saved input-page.png";
        }
        catch(Exception e) { status.Text="Capture failed: "+e.Message; }
    }
    async void SetMode(bool enabled,string reason)
    {
        if(!ready || view.IsDisposed)return;
        int current=++revision;
        // Parameters contain only host constants, boolean and monotonic integer.
        try
        {
            if(enabled)view.Focus();
            await view.CoreWebView2.ExecuteScriptAsync("window.hostInputMode({enabled:"+(enabled?"true":"false")+",revision:"+current+",reason:'"+reason+"'})");
        }
        catch(Exception e) { status.Text="Mode change failed: "+e.Message; }
    }
    [STAThread]
    static int Main(string[] args)
    {
        bool probe=args.Length==1 && args[0]=="--instance-probe";
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
                    // Headless lifecycle test: never creates a window or WebView profile.
                    var timer=Stopwatch.StartNew();
                    string release=Path.Combine(folder,"instance-release."+pid+".signal");
                    while(!File.Exists(release) && timer.Elapsed.TotalSeconds<20)Thread.Sleep(50);
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new InputProbeHost());
                return 0;
            }
        }
        catch(Exception)
        {
            // Fail closed rather than starting an unguarded second input host.
            return 3;
        }
    }
}
