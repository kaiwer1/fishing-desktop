using System;
using System.Diagnostics;
using System.IO;
using System.Text;

sealed partial class InputProbeHost
{
    readonly int manualSeconds;
    readonly bool unattendedManual;
    readonly Stopwatch manualClock=new Stopwatch();
    readonly System.Windows.Forms.Timer manualTimer=new System.Windows.Forms.Timer{Interval=1000};
    bool manualStarted;
    string manualEnd="user-or-host-close";
    void StartManualProbe()
    {
        if(manualSeconds==0 || manualStarted)return;
        manualStarted=true;manualClock.Start();
        manualTimer.Tick+=delegate{
            if(File.Exists(Path.Combine(root,"manual-exit.request"))){manualEnd="requested-close";Close();return;}
            int remaining=Math.Max(0,manualSeconds-(int)manualClock.Elapsed.TotalSeconds);
            status.Text="手动验证剩余 "+remaining+" 秒 · 点击窗口后按 F8";
            if(remaining==0){manualEnd="deadline";Close();}
        };
        manualTimer.Start();
        // Undo a hidden launch hint, without requesting foreground activation.
        Hide();Show();
    }
    void FinishManualProbe()
    {
        manualTimer.Stop();manualTimer.Dispose();
        if(!manualStarted)return;
        manualClock.Stop();
        try{File.WriteAllText(Path.Combine(root,"manual-session.json"),json.Serialize(new{
            mode="bounded-manual-input",requestedSeconds=manualSeconds,elapsedSeconds=manualClock.Elapsed.TotalSeconds,
            unattended=unattendedManual,
            endedAtUtc=DateTime.UtcNow.ToString("o"),endReason=manualEnd,exitCode=ExitCode,nativeInputAccepted=false,
            gameLoaded=false,desktopEmbedded=false,keyboardContentsRecorded=false}),Encoding.UTF8);}
        catch(Exception){ExitCode=4;}
    }
}
