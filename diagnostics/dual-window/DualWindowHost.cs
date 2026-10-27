using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

sealed class ProbeWindow : Form
{
    internal readonly DualWindowOwner.Ticket Ticket;
    internal readonly WebView2 View=new WebView2 { Dock=DockStyle.Fill };
    internal ulong Navigation;
    internal int NavigationGeneration;
    internal bool AwaitingNavigationStart=true;
    internal bool Ready;
    internal bool DrawExpected;
    internal long DrawEpoch;
    readonly bool automated;
    readonly DualWindowContext context;
    internal readonly bool DesktopReadOnly;
    internal readonly long DisplayGeneration;
    internal ProbeWindow(DualWindowContext context,DualWindowOwner.Ticket ticket,bool automated,bool desktop)
    {
        Ticket=ticket;this.automated=automated;this.context=context;DesktopReadOnly=desktop && !ticket.CanControl;
        DisplayGeneration=context.DisplayGeneration;
        Text=context.GameMode?"钓鱼桌面 · 数据接入预览":ticket.CanControl?"Fishing Dual Window - Interaction":"Fishing Dual Window - Read Only";
        Size=new Size(650,570);StartPosition=FormStartPosition.Manual;
        if(context.GameMode)Size=new Size(1000,650);
        Location=new Point(ticket.CanControl?720:60,120);
        if(context.ManualMode && ticket.CanControl){Size=new Size(1000,800);StartPosition=FormStartPosition.CenterScreen;}
        if(automated){if(!DesktopReadOnly)Opacity=0;ShowInTaskbar=false;}
        if(context.ManualUnattended && ticket.CanControl){Opacity=0;ShowInTaskbar=false;}
        if(DesktopReadOnly){FormBorderStyle=FormBorderStyle.None;AutoScaleMode=AutoScaleMode.None;Size=Screen.PrimaryScreen.Bounds.Size;}
        var bar=new FlowLayoutPanel {Dock=DockStyle.Top,AutoSize=true,WrapContents=true};
        AddButton(bar,context.ManualMode?"打开桌面页":"Open read only",delegate{context.OpenFromUser(false);});
        AddButton(bar,context.ManualMode?"打开交互窗":"Open interaction",delegate{context.OpenFromUser(true);});
        if(ticket.CanControl){
            AddButton(bar,context.ManualMode?"开启交互":"Enable interaction",delegate{context.GrantFromUser(this);});
            AddButton(bar,context.ManualMode?"关闭交互":"Observe / cancel",delegate{context.Revoke(this);});
        }
        AddButton(bar,context.ManualMode?"重载记录":"Reload page",delegate{context.Reload(this);});
        AddButton(bar,context.ManualMode?"退出本轮测试":"Exit both + owner",delegate{context.Shutdown();});
        Controls.Add(View);if(!DesktopReadOnly)Controls.Add(bar);else bar.Dispose();
        Deactivate+=delegate{context.Revoke(this,"host-deactivate");};
        FormClosing+=delegate{context.Retire(this);};
    }
    static void AddButton(FlowLayoutPanel panel,string text,Action action)
    {
        var button=new Button {Text=text,AutoSize=true};button.Click+=delegate{action();};panel.Controls.Add(button);
    }
    protected override void Dispose(bool disposing)
    {
        // Dispose() can bypass FormClosing. Release the native parent while the
        // HWND still exists, before WebView child handles are destroyed.
        if(disposing)context.Retire(this);
        base.Dispose(disposing);
    }
    protected override void WndProc(ref Message message)
    {
        if(message.Msg==0x2E0 && context!=null)context.OnReadOnlyDpiMessage(this);
        base.WndProc(ref message);
    }
    protected override bool ShowWithoutActivation {get{return automated || DesktopReadOnly || (context!=null && context.ManualMode);}}
    protected override CreateParams CreateParams {
        get{var value=base.CreateParams;if(automated || DesktopReadOnly || (context!=null && context.ManualUnattended))value.ExStyle|=0x08000000|0x20|0x80;return value;}
    }
}

// No MainForm is assigned: closing either (or both) view windows cannot terminate
// the owner. Only explicit application exit or a conservative failure path does.
sealed partial class DualWindowContext : ApplicationContext
{
    string PageUrl {get{return GameMode?"https://fishing-dual.localhost/game.html":"https://fishing-dual.localhost/view.html";}}
    readonly string root=AppDomain.CurrentDomain.BaseDirectory;
    readonly JavaScriptSerializer json=new JavaScriptSerializer {MaxJsonLength=32768};
    readonly Control dispatcher=new Control();
    readonly Dictionary<bool,ProbeWindow> windows=new Dictionary<bool,ProbeWindow>();
    readonly DualWindowOwner owner;
    readonly ActivationHandshake activation=new ActivationHandshake();
    readonly bool automated;
    readonly bool desktopMode;
    readonly bool visibleDesktopTest;
    readonly bool policySelfTest;
    readonly bool nativePolicySelfTest;
    readonly bool rendererSelfTest;
    readonly bool wallpaperPolicySelfTest;
    readonly bool displayOnlySelfTest;
    readonly int soakSeconds;
    readonly NotifyIcon tray;
    CoreWebView2Environment environment;
    bool stopping;
    bool finalized;
    List<ProbeWindow> closingPanes;
    public int ExitCode {get;private set;}
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

    internal DualWindowContext(bool automated,bool desktopMode,bool visibleDesktopTest,bool policySelfTest,bool nativePolicySelfTest,bool rendererSelfTest,bool wallpaperPolicySelfTest,int soakSeconds,bool displayOnlySelfTest,int manualSeconds=0,bool manualUnattended=false,bool startupPreparation=false,bool nativeOperatorStart=false,bool threeScene=false,bool sceneViewport=false,bool sceneContext=false,bool sceneFaultSelfTest=false,string sceneWatchdogCase=null,string gameMode=null)
    {
        this.gameMode=gameMode;
        this.manualSeconds=manualSeconds;this.manualUnattended=manualUnattended;this.startupPreparation=startupPreparation;this.nativeOperatorStart=nativeOperatorStart;this.threeScene=threeScene;this.sceneViewport=sceneViewport;this.sceneContext=sceneContext;this.sceneFaultSelfTest=sceneFaultSelfTest;this.sceneWatchdogCase=sceneWatchdogCase;
        this.automated=automated;this.desktopMode=desktopMode;this.visibleDesktopTest=visibleDesktopTest;this.policySelfTest=policySelfTest;
        this.nativePolicySelfTest=nativePolicySelfTest;
        this.rendererSelfTest=rendererSelfTest;
        this.wallpaperPolicySelfTest=wallpaperPolicySelfTest;
        this.displayOnlySelfTest=displayOnlySelfTest;
        this.soakSeconds=soakSeconds;
        owner=new DualWindowOwner(Path.Combine(root,"counter.journal.jsonl"));
        InitializeGame();
        if(dispatcher.Handle==IntPtr.Zero)throw new InvalidOperationException("Owner dispatcher handle unavailable.");
        InitializeActivityPolicy();
        InitializeSceneWatchdog();
        if(!automated && !ManualUnattended){
            var menu=new ContextMenuStrip();
            menu.Items.Add("Open read only",null,delegate{OpenFromUser(false);});
            menu.Items.Add("Open interaction",null,delegate{OpenFromUser(true);});
            menu.Items.Add("Exit both windows and owner",null,delegate{Shutdown();});
            tray=new NotifyIcon {Icon=SystemIcons.Application,Text="Fishing dual-window diagnostic",ContextMenuStrip=menu,Visible=true};
            tray.DoubleClick+=delegate{OpenFromUser(true);};
        }
        dispatcher.BeginInvoke(new Action(Start));
    }
    async void Start()
    {
        try{
            if(automated){if(nativePolicySelfTest)deadline.Interval=85000+soakSeconds*1000+(nativeOperatorStart?180000:0);deadline.Tick+=delegate{Fail(new InvalidOperationException("Dual-window self-test deadline exceeded."));};deadline.Start();}
            StartManualLifetime();
            environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"private-profile"));
            if(stopping)return;
            environment.BrowserProcessExited+=delegate(object sender,CoreWebView2BrowserProcessExitedEventArgs e){
                if(!stopping && e.BrowserProcessExitKind==CoreWebView2BrowserProcessExitKind.Failed)
                    Fail(new InvalidOperationException("Dedicated browser failed; explicit application restart required."));
            };
            await Open(false);var interaction=await Open(true);
            if(ManualMode && !ManualUnattended && Live(interaction)){GuardPresentation(()=>{interaction.Hide();interaction.Show();});}
            if(GameMode && !stopping)await StartGamePresentation();
            if(automated && !stopping){if(GameSelfTest)RunGameProbe();else RunProbe();}
        }catch(Exception e){Fail(e);}
    }
    bool Live(ProbeWindow pane)
    {
        ProbeWindow current;
        return !stopping && pane!=null && !pane.IsDisposed && windows.TryGetValue(pane.Ticket.CanControl,out current)
            && Object.ReferenceEquals(current,pane) && owner.IsCurrent(pane.Ticket);
    }
    internal async void OpenFromUser(bool canControl)
    {
        if(automated || stopping || environment==null)return;
        ProbeWindow pending;
        if(!canControl && displayBarrier.Blocked && windows.TryGetValue(false,out pending) && pending.DisplayGeneration!=displayBarrier.Revision)return;
        try{var pane=await Open(canControl);if(Live(pane) && !pane.DesktopReadOnly){pane.Show();pane.Activate();}}
        catch(Exception e){Fail(e);}
    }
    async Task<ProbeWindow> Open(bool canControl)
    {
        if(stopping)throw new InvalidOperationException("Owner is stopping.");
        ProbeWindow existing;if(windows.TryGetValue(canControl,out existing))return existing;
        var pane=new ProbeWindow(this,owner.Open(canControl),automated,desktopMode);
        windows.Add(canControl,pane);
        GameOpen(pane);
        UpdateViewAvailability();
        try{
            if(pane.DesktopReadOnly)AttachDesktop(pane);else pane.Show();
            await pane.View.EnsureCoreWebView2Async(environment);
            if(!Live(pane))return pane;
            var core=pane.View.CoreWebView2;
            core.Settings.AreBrowserAcceleratorKeysEnabled=false;
            core.Settings.AreDefaultContextMenusEnabled=false;
            core.Settings.AreDevToolsEnabled=false;
            core.SetVirtualHostNameToFolderMapping("fishing-dual.localhost",Path.Combine(root,"page"),CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting+=delegate(object sender,CoreWebView2NavigationStartingEventArgs e){
                if(!Live(pane)){e.Cancel=true;return;}
                if(e.Uri!=PageUrl){e.Cancel=true;Revoke(pane);return;}
                activation.Cancel(pane.Ticket.ViewId);
                ForgetVisual(pane);
                RendererNavigating(pane);
                GameNavigating(pane);owner.Navigating(pane.Ticket);pane.Navigation=e.NavigationId;pane.Ready=false;
                UpdateViewAvailability();
                pane.NavigationGeneration=checked(pane.NavigationGeneration+1);pane.AwaitingNavigationStart=false;
            };
            core.NavigationCompleted+=async delegate(object sender,CoreWebView2NavigationCompletedEventArgs e){
                if(!Live(pane) || pane.AwaitingNavigationStart || e.NavigationId!=pane.Navigation)return;
                if(!e.IsSuccess){Fail(new InvalidOperationException("Diagnostic page navigation failed."));return;}
                ulong navigation=e.NavigationId;int generation=pane.NavigationGeneration;
                try{
                    string document=null;
                    for(int attempt=0;attempt<200;attempt++){
                        string data=await core.ExecuteScriptAsync("window.dualProbe ? window.dualProbe.documentId : null");
                        if(!Live(pane) || pane.Navigation!=navigation || pane.NavigationGeneration!=generation)return;
                        document=json.Deserialize<string>(data);
                        if(document!=null)break;
                        await Task.Delay(25);
                    }
                    if(document==null)throw new InvalidOperationException("Diagnostic page module did not initialize.");
                    if(!Live(pane) || pane.Navigation!=navigation || pane.NavigationGeneration!=generation)return;
                    if(threeScene && !canControl && !await LoadScenePage(pane,navigation,generation))return;
                    if(!Live(pane) || pane.Navigation!=navigation || pane.NavigationGeneration!=generation)return;
                    owner.Attach(pane.Ticket,document);GameAttached(pane);pane.Ready=true;
                    UpdateViewAvailability();
                    PolicyPageReady();DisplayPageReady(pane);
                    if(!Live(pane))return;
                    Send(pane,new {kind="role",documentId=document,role=canControl?"独立交互窗口":"独立只读窗口"});
                    SendLease(pane,null);Broadcast();SendPolicy(pane);RendererPageReady(pane);ManualPageReady(pane);
                }catch(Exception error){NavigationError(pane,navigation,generation,error);}
            };
            core.WebMessageReceived+=delegate(object sender,CoreWebView2WebMessageReceivedEventArgs e){
                if(Live(pane))Receive(pane,e.Source,e.WebMessageAsJson);
            };
            core.ProcessFailed+=delegate{if(Live(pane))Fail(new InvalidOperationException("Dedicated browser process failed; explicit restart required."));};
            core.NewWindowRequested+=delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e){e.Handled=true;};
            core.PermissionRequested+=delegate(object sender,CoreWebView2PermissionRequestedEventArgs e){e.State=CoreWebView2PermissionState.Deny;};
            core.DownloadStarting+=delegate(object sender,CoreWebView2DownloadStartingEventArgs e){e.Cancel=true;};
            core.Navigate(PageUrl);return pane;
        }catch{if(Live(pane)){Retire(pane);pane.Dispose();throw;}return pane;}
    }
    internal void Retire(ProbeWindow pane)
    {
        ForgetVisual(pane);
        RetireRenderer(pane);
        if(Live(pane)){
            // Remove identity before child controls dispose or their asynchronous events return.
            activation.Cancel(pane.Ticket.ViewId);manualLeases.Remove(pane);GameRetire(pane);owner.Close(pane.Ticket);windows.Remove(pane.Ticket.CanControl);pane.Ready=false;
            UpdateViewAvailability();
        }
        ReleaseDesktop(pane);
    }
    internal void Reload(ProbeWindow pane)
    {
        if(PrepareReload(pane))pane.View.CoreWebView2.Reload();
    }
    bool PrepareReload(ProbeWindow pane)
    {
        if(!Live(pane) || pane.View.CoreWebView2==null)return false;
        RendererNavigating(pane);
        ForgetVisual(pane);
        Revoke(pane);GameNavigating(pane);owner.Navigating(pane.Ticket);pane.Ready=false;pane.AwaitingNavigationStart=true;
        pane.NavigationGeneration=checked(pane.NavigationGeneration+1);
        UpdateViewAvailability();return true;
    }
    internal void GrantFromUser(ProbeWindow pane)
    {
        if(automated || !Live(pane) || !pane.Ready || !Foreground(pane) || !pane.Ticket.CanControl)return;
        if(GameMode && pane.View.ClientSize.Width<=pane.View.ClientSize.Height)return;
        try{pane.View.Focus();BeginActivation(pane);}catch(Exception e){Fail(e);}
    }
    bool Foreground(ProbeWindow pane)
    {
        return automated || (Form.ActiveForm==pane && GetForegroundWindow()==pane.Handle);
    }
    string BeginActivation(ProbeWindow pane)
    {
        if(!Live(pane) || !pane.Ready || !pane.Ticket.CanControl || !Foreground(pane) || !owner.ActivityState().CanRun)return null;
        if(GameMode && pane.View.ClientSize.Width<=pane.View.ClientSize.Height)return null;
        string request=activation.Begin(pane.Ticket.ViewId,pane.Ticket.DocumentId,pane.NavigationGeneration,owner.ActivityState().Revision);
        Send(pane,new{kind="prepare-activation",documentId=pane.Ticket.DocumentId,requestId=request,allowUnfocused=automated});
        return request;
    }
    void NavigationError(ProbeWindow pane,ulong navigation,int generation,Exception error)
    {
        if(Live(pane) && ActivationHandshake.CurrentNavigation(navigation,generation,pane.Navigation,pane.NavigationGeneration,pane.AwaitingNavigationStart))Fail(error);
    }
    internal void Revoke(ProbeWindow pane,string reason="host-revoke")
    {
        if(!Live(pane) || !pane.Ticket.CanControl)return;
        activation.Cancel(pane.Ticket.ViewId);
        owner.Revoke(pane.Ticket);
        try{SendLease(pane,null,reason);}catch(Exception e){Fail(e);}
    }
    void SendLease(ProbeWindow pane,string lease,string reason="host-revoke")
    {
        if(!Live(pane) || pane.Ticket.DocumentId==null)return;
        GameLease(pane,lease);
        ManualLease(pane,lease);
        Send(pane,new{kind="lease",documentId=pane.Ticket.DocumentId,revision=owner.NextRevision(pane.Ticket),cancellationEpoch=pane.Ticket.Epoch,lease=lease,reason=reason});
    }
    void Send(ProbeWindow pane,object value)
    {
        if(Live(pane) && pane.Ticket.DocumentId!=null && !RendererIsSuspended(pane))pane.View.CoreWebView2.PostWebMessageAsJson(json.Serialize(value));
    }
    void Broadcast()
    {
        if(GameMode){foreach(var pane in windows.Values)SendGameSnapshot(pane);return;}
        var snapshot=owner.Snapshot();
        foreach(var pane in windows.Values)Send(pane,new{kind="snapshot",documentId=pane.Ticket.DocumentId,snapshot=snapshot});
    }
    static bool StringField(Dictionary<string,object> value,string key,out string result)
    {
        object field;result=null;
        if(!value.TryGetValue(key,out field) || !(field is string))return false;
        result=(string)field;return true;
    }
    void Receive(ProbeWindow pane,string source,string payload)
    {
        if(!Live(pane) || !pane.Ready || source!=PageUrl || payload.Length>16384)return;
        Dictionary<string,object> m;
        try{m=json.Deserialize<Dictionary<string,object>>(payload);}catch(ArgumentException){return;}catch(InvalidOperationException){return;}
        string kind,document,lease,transport;
        if(m==null || !StringField(m,"kind",out kind) || !StringField(m,"documentId",out document) ||
            !StringField(m,"lease",out lease) || !StringField(m,"transportId",out transport) || transport.Length>64)return;
        try{
            if(kind=="scene-fault"){ReceiveSceneFault(pane,m,document);return;}
            if(kind=="input-evidence"){ReceiveManualInput(pane,m,document);return;}
            if(kind=="activation-ready"){
                string requestId;object epoch;
                if(!StringField(m,"requestId",out requestId) || !m.TryGetValue("cancellationEpoch",out epoch) || !(epoch is int))return;
                bool accepted=activation.Complete(requestId,pane.Ticket.ViewId,document,pane.NavigationGeneration,
                    document==pane.Ticket.DocumentId && Foreground(pane) && owner.ActivityState().CanRun,(int)epoch,pane.Ticket.Epoch,owner.ActivityState().Revision);
                if(accepted){
                    pane.Ticket.Epoch=(int)epoch;
                    SendLease(pane,owner.Grant(pane.Ticket));
                }
                Send(pane,new{kind="activation-reply",documentId=pane.Ticket.DocumentId,requestId=requestId,transportId=transport,accepted=accepted});
                return;
            }
            if(kind=="revoke"){
                object epoch;if(!m.TryGetValue("cancellationEpoch",out epoch) || !(epoch is int) || (int)epoch<0)return;
                string activationRequest;bool cancelledGrant=false;
                if(document==pane.Ticket.DocumentId && (int)epoch>=pane.Ticket.Epoch && StringField(m,"activationRequestId",out activationRequest)){
                    cancelledGrant=activation.PageCancelled(pane.Ticket.ViewId,activationRequest);
                    if(cancelledGrant)owner.Revoke(pane.Ticket);
                }
                bool revoked=owner.Cancel(pane.Ticket,document,lease,(int)epoch) || cancelledGrant;
                if(revoked)SendLease(pane,null);
                Send(pane,new{kind="revoke-reply",documentId=pane.Ticket.DocumentId,transportId=transport,revoked=revoked});return;
            }
            if(GameMode){ReceiveGame(pane,m,document,lease,transport);return;}
            string request;object delta,expected;
            if(kind!="command" || !StringField(m,"requestId",out request) || !m.TryGetValue("delta",out delta) || !(delta is int) ||
                !m.TryGetValue("expectedSequence",out expected) || !(expected is int))return;
            var reply=owner.Execute(pane.Ticket,document,lease,request,(int)delta,(int)expected);
            Send(pane,new{kind="reply",documentId=pane.Ticket.DocumentId,transportId=transport,requestId=request,reply=reply,snapshot=reply.Snapshot});
            Broadcast();
        }catch(Exception e){Fail(e);}
    }
    void Fail(Exception error)
    {
        if(stopping)return;
        ExitCode=4;
        try{File.WriteAllText(Path.Combine(root,"dual-window-error.txt"),error.ToString(),Encoding.UTF8);}catch(Exception){}
        Shutdown();
    }
    internal void Shutdown()
    {
        if(stopping)return;
        PrepareGameShutdown();
        closingPanes=new List<ProbeWindow>(windows.Values);
        foreach(var pane in closingPanes)Retire(pane);
        stopping=true;deadline.Stop();StopManualLifetime();StopDesktopWatch();StopActivityPolicy();StopSceneWatchdog();
        if(tray!=null)tray.Visible=false;
        // Defer WebView disposal until after the current browser event returns.
        dispatcher.BeginInvoke(new Action(FinishShutdown));
    }
    void FinishShutdown()
    {
        if(finalized)return;finalized=true;
        try{
            if(tray!=null){tray.ContextMenuStrip.Dispose();tray.Dispose();}
            foreach(var pane in closingPanes){
                try{pane.Close();pane.Dispose();}catch(Exception){ExitCode=4;}
            }
        }finally{
            try{try{WriteDesktopShutdownReport();DisposeActivityPolicy();DisposeSceneWatchdog();FinishManualLifetime();}finally{try{DisposeGame();}finally{owner.Dispose();}}}finally{ExitThread();}
        }
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing){Shutdown();FinishShutdown();deadline.Dispose();dispatcher.Dispose();}
        base.Dispose(disposing);
    }
    [STAThread] static int Main(string[] args)
    {
        string gameMode=null;
        if(args.Length==1){if(args[0]=="--game-new")gameMode="new";else if(args[0]=="--game-open")gameMode="open";else if(args[0]=="--game-self-test")gameMode="self-test";}
        bool gameUser=gameMode=="new" || gameMode=="open",gameSelfTest=gameMode=="self-test";
        int manualSeconds=0;bool manualUnattended=args.Length>0 && args[0]=="--manual-desktop-timeout-probe";
        bool manual=manualUnattended || (args.Length>0 && args[0]=="--manual-desktop-probe");
        if(manual && (args.Length!=2 || !Int32.TryParse(args[1],out manualSeconds) || manualSeconds<60 || manualSeconds>600))return 5;
        bool threeSceneSoak=args.Length>0 && args[0]=="--three-scene-soak-self-test";
        bool displaySoak=args.Length>0 && args[0]=="--display-soak-self-test";
        int soak=0;bool soakRequested=threeSceneSoak || displaySoak || (args.Length>0 && args[0]=="--wallpaper-soak-self-test");
        if(soakRequested && (args.Length!=2 || !Int32.TryParse(args[1],out soak) || soak<120 || soak>7200))return 5;
        string sceneWatchdogCase=null;
        if(args.Length>0 && args[0]=="--three-watchdog-self-test"){
            if(args.Length!=2 || (args[1]!="silent-stop" && args[1]!="stale-flag" && args[1]!="no-response" && args[1]!="transition-pending" && args[1]!="record-failure"))return 5;
            sceneWatchdogCase=args[1];
        }
        bool sceneFaultSelfTest=sceneWatchdogCase!=null || (args.Length==1 && args[0]=="--three-fault-policy-self-test");
        bool restartCheck=args.Length==1 && args[0]=="--scene-fault-restart-check";
        bool sceneContext=args.Length==1 && args[0]=="--three-context-self-test";
        bool sceneViewport=sceneContext || (args.Length==1 && args[0]=="--three-viewport-self-test");
        bool threeScene=sceneFaultSelfTest || sceneViewport || threeSceneSoak || (args.Length==1 && args[0]=="--three-scene-self-test");
        bool displayOnly=(threeScene && !sceneViewport && !sceneFaultSelfTest) || displaySoak || (args.Length==1 && args[0]=="--display-policy-self-test");
        bool operatorStart=args.Length==1 && args[0]=="--wallpaper-operator-self-test";
        bool wallpaperPolicy=operatorStart || displayOnly || soakRequested || (args.Length==1 && args[0]=="--wallpaper-policy-self-test");
        bool visibleDesktop=gameUser || manual || wallpaperPolicy || (args.Length==1 && args[0]=="--desktop-visible-self-test");
        bool nativePolicy=gameUser || manual || wallpaperPolicy || (args.Length==1 && args[0]=="--native-policy-self-test");
        bool renderer=args.Length==1 && args[0]=="--renderer-self-test";
        bool policy=gameSelfTest || sceneFaultSelfTest || renderer || nativePolicy || (args.Length==1 && args[0]=="--policy-self-test");
        bool startup=args.Length==1 && args[0]=="--desktop-startup-self-test";
        bool desktop=startup || visibleDesktop || (args.Length==1 && args[0]=="--desktop-self-test");
        bool automated=!gameUser && !manual && (sceneViewport || desktop || policy || (args.Length==1 && args[0]=="--self-test"));
        if(args.Length>0 && !automated && !manual && !restartCheck && gameMode==null)return 5;
        try{
            using(var guard=new HostInstanceGuard()){
                if(!guard.IsOwner)return 2;
                if(restartCheck)return DualWindowContext.VerifySceneFaultRestart();
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                using(var context=new DualWindowContext(automated,desktop,visibleDesktop,policy,nativePolicy,renderer,wallpaperPolicy,soak,displayOnly,manualSeconds,manualUnattended,startup,operatorStart,threeScene,sceneViewport,sceneContext,sceneFaultSelfTest,sceneWatchdogCase,gameMode)){Application.Run(context);return context.ExitCode;}
            }
        }catch(Exception error){
            if(restartCheck)try{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"scene-fault-restart-error.txt"),error.ToString(),Encoding.UTF8);}catch(Exception){}
            return 3;
        }
    }
}
