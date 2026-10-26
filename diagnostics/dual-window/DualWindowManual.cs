using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

sealed partial class DualWindowContext
{
    readonly Timer manualTimer=new Timer{Interval=500};
    readonly Stopwatch manualClock=new Stopwatch();
    readonly Dictionary<ProbeWindow,string> manualLeases=new Dictionary<ProbeWindow,string>();
    readonly int manualSeconds;
    readonly bool manualUnattended;
    bool manualPresentationShown;
    bool manualInputReady;
    string manualEndReason="explicit-exit";
    internal bool ManualMode {get{return manualSeconds>0;}}
    internal bool ManualUnattended {get{return ManualMode && manualUnattended;}}
    void StartManualLifetime()
    {
        if(!ManualMode)return;
        desktopAllowForegroundChanges=true;rendererEnabled=true;
        manualTimer.Tick+=delegate{
            try{
                if(File.Exists(Path.Combine(root,"manual-exit.request"))){manualEndReason="requested-close";Shutdown();}
                else if(manualClock.Elapsed.TotalSeconds>=manualSeconds){manualEndReason="deadline";Shutdown();}
            }catch(Exception error){Fail(error);}
        };
        manualClock.Start();manualTimer.Start();StartNativePolicy();
    }
    void ManualPageReady(ProbeWindow pane)
    {
        if(!ManualMode)return;
        if(pane.Ticket.CanControl)Send(pane,new{kind="manual-input",documentId=pane.Ticket.DocumentId});
        else dispatcher.BeginInvoke(new Action(delegate{
            if(!Live(pane) || !pane.Ready)return;
            try{desktopShown.Add(pane);GuardPresentation(()=>pane.Show());CheckDesktop(pane);manualPresentationShown=true;}
            catch(Exception error){Fail(error);}
        }));
    }
    void ManualLease(ProbeWindow pane,string lease){if(ManualMode)manualLeases[pane]=lease;}
    void ReceiveManualInput(ProbeWindow pane,Dictionary<string,object> message,string document)
    {
        if(!ManualMode || !pane.Ticket.CanControl || document!=pane.Ticket.DocumentId)return;
        object raw;string lease;
        bool eligible=owner.ActivityState().CanRun && Foreground(pane) && manualLeases.TryGetValue(pane,out lease) && !String.IsNullOrEmpty(lease);
        if(!message.TryGetValue("evidence",out raw))return;
        var clean=ManualInputRecord.Normalize(raw as Dictionary<string,object>,document,eligible);if(clean==null)return;
        if((string)clean["kind"]=="ready"){
            manualInputReady=true;
            if(manualUnattended)CaptureManualPage(pane);
        }
        string file=Path.Combine(root,"input-events.jsonl");
        if(File.Exists(file) && new FileInfo(file).Length>16*1024*1024)throw new InvalidOperationException("Private input evidence budget exceeded.");
        File.AppendAllText(file,DateTime.UtcNow.ToString("o")+" "+json.Serialize(clean)+Environment.NewLine,Encoding.UTF8);
    }
    async void CaptureManualPage(ProbeWindow pane)
    {
        try{using(var stream=File.Create(Path.Combine(root,"manual-panel.png")))await pane.View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);}
        catch(Exception error){if(!stopping && Live(pane))Fail(error);}
    }
    void StopManualLifetime(){manualTimer.Stop();}
    void FinishManualLifetime()
    {
        manualTimer.Dispose();if(!ManualMode)return;
        var state=owner.Snapshot();if(!manualPresentationShown || !manualInputReady)ExitCode=4;
        File.WriteAllText(Path.Combine(root,"manual-session.json"),json.Serialize(new{
            mode="bounded-desktop-input",requestedSeconds=manualSeconds,elapsedSeconds=manualClock.Elapsed.TotalSeconds,
            unattended=manualUnattended,endReason=manualEndReason,exitCode=ExitCode,presentationShown=manualPresentationShown,inputPanelReady=manualInputReady,
            sequence=state.Sequence,value=state.Value,nativeInputAccepted=false,desktopIconsAccepted=false,gameLoaded=false
        }),Encoding.UTF8);
    }
}
