using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using FishingDesktop.Diagnostics.HostPolicy;

static class NativeEnvironmentTests
{
    static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [STAThread]static int Main(string[] args)
    {
        if(args.Length!=1 || !Directory.Exists(args[0]))return 2;
        string root=Path.GetFullPath(args[0]),exe=Path.Combine(root,"NativeEnvironmentFixture.exe");
        Process fixture=null;long startTicks=0;int result=1;
        try{
            Need(NativeEnvironment.Covers(new Rectangle(0,0,1920,1080),new Rectangle(0,0,1920,1080)),"Exact coverage rejected.");
            Need(NativeEnvironment.Covers(new Rectangle(-1920,0,1920,1080),new Rectangle(-1920,0,1920,1080)),"Negative monitor origin rejected.");
            Need(!NativeEnvironment.Covers(new Rectangle(0,0,1920,1080),new Rectangle(-1920,0,1920,1080)),"Other-monitor fullscreen accepted.");
            Need(!NativeEnvironment.Covers(new Rectangle(0,0,1918,1080),new Rectangle(0,0,1920,1080)),"Incomplete coverage accepted.");
            Need(!NativeEnvironment.Covers(Rectangle.Empty,new Rectangle(0,0,1920,1080)),"Empty coverage accepted.");
            Console.WriteLine("PASS coverage geometry handles exact negative-origin other-monitor incomplete and empty rectangles");
            var sampler=new NativeEnvironment();var gate=new RunGate();gate.Set(PauseReason.Startup,false);
            var active=new ActiveClock(.25);var clock=Stopwatch.StartNew();int normal=0,full=0,restored=0;double pausedAt=-1;
            var reasons=new Dictionary<string,int>();int ownFixtureForeground=0;
            fixture=Process.Start(new ProcessStartInfo(exe,"\""+root+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
            startTicks=fixture.StartTime.ToUniversalTime().Ticks;
            int consecutiveFull=0,maxConsecutiveFull=0;
            while(!fixture.HasExited && clock.ElapsedMilliseconds<60000){
                var sample=sampler.Capture(Screen.PrimaryScreen.Bounds);gate.Replace(NativeEnvironment.EnvironmentMask,sample.BlockingReasons);
                if(!reasons.ContainsKey(sample.Reason))reasons[sample.Reason]=0;reasons[sample.Reason]++;
                active.Advance(clock.Elapsed.TotalSeconds,gate.Snapshot().CanRun);
                uint foregroundPid;GetWindowThreadProcessId(GetForegroundWindow(),out foregroundPid);
                string phase="";try{phase=File.ReadAllText(Path.Combine(root,"fixture-phase.txt"));}catch(IOException){}
                if(foregroundPid==fixture.Id && sample.Known && sample.InteractiveSession){
                    ownFixtureForeground++;
                    if(phase=="normal" && !sample.Fullscreen)normal++;
                    if(phase=="fullscreen" && sample.Fullscreen){
                        full++;Need(!gate.Snapshot().CanRun,"Fullscreen did not pause.");
                        consecutiveFull++;maxConsecutiveFull=Math.Max(maxConsecutiveFull,consecutiveFull);
                        if(pausedAt<0)pausedAt=active.ActiveSeconds;else Need(active.ActiveSeconds==pausedAt,"Activity clock advanced under fullscreen.");
                    }
                    if(phase=="restored" && !sample.Fullscreen){restored++;Need(gate.Snapshot().CanRun,"Restored normal window stayed paused.");}
                }
                if(foregroundPid!=fixture.Id || !sample.Fullscreen){pausedAt=-1;consecutiveFull=0;}
                Thread.Sleep(100);
            }
            Need(fixture.HasExited && fixture.ExitCode==0,"Own fixture failed to exit normally.");
            Console.WriteLine("OBSERVED fixtureForeground="+ownFixtureForeground+" normal="+normal+" fullscreen="+full+" restored="+restored);
            foreach(var pair in reasons)Console.WriteLine("REASON "+pair.Key+"="+pair.Value);
            Need(normal>=5 && maxConsecutiveFull>=10 && restored>=5,"Not enough confirmed fixture-foreground samples; result inconclusive.");
            Console.WriteLine("PASS real foreground fixture transitions normal fullscreen normal through read-only native policy sampling");
            Console.WriteLine("PASS fullscreen native samples freeze activity time and restoration resumes without replay");
            Console.WriteLine("SAMPLES normal="+normal+" fullscreen="+full+" restored="+restored+" activeSeconds="+active.ActiveSeconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
            result=0;
        }catch(Exception error){Console.WriteLine("FAIL "+error.Message);}
        finally{
            if(fixture!=null){
                if(!fixture.HasExited){
                    File.WriteAllText(Path.Combine(root,"exit.request"),"exit");
                    if(!fixture.WaitForExit(3000)){
                        if(fixture.StartTime.ToUniversalTime().Ticks==startTicks && fixture.MainModule.FileName.Equals(exe,StringComparison.OrdinalIgnoreCase))fixture.Kill();
                        fixture.WaitForExit(3000);result=1;
                    }
                }
                fixture.Dispose();
            }
        }
        return result;
    }
}
