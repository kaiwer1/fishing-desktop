using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using FishingDesktop.Diagnostics.HostPolicy;

static class NativeEnvironmentQueryTests
{
    static readonly Rectangle Target=new Rectangle(0,0,1920,1080);
    static int passed,failed;
    static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static void Throws<T>(Action action) where T:Exception
    {
        try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);
    }
    static void Test(string name,Action action)
    {
        try{action();passed++;Console.WriteLine("PASS "+name);}
        catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}
    }
    struct ProcessAnswer
    {
        internal readonly bool Success;internal readonly uint Pid;
        internal ProcessAnswer(bool success,uint pid){Success=success;Pid=pid;}
    }
    // Supplies scripted raw query responses. No coverage/pause/session policy
    // is implemented here; every decision is made by the real Capture method.
    sealed class Queries : INativeEnvironmentQueries
    {
        internal readonly List<string> Calls=new List<string>();
        internal readonly Queue<IntPtr> Foregrounds=new Queue<IntPtr>();
        internal readonly Queue<ProcessAnswer> ForegroundProcesses=new Queue<ProcessAnswer>();
        internal readonly IntPtr Foreground=new IntPtr(11),Shell=new IntPtr(22);
        internal bool Active=true,Default=true,Exists=true,Visible=true,Iconic,ShellAvailable=true,ShellPidAvailable=true,RectAvailable=true;
        internal uint ForegroundPid=300,ShellPid=200;
        internal int SessionArgument=-1;
        internal Exception QueryFailure;
        internal EnvironmentWindowRect Bounds=new EnvironmentWindowRect(0,0,1920,1080);
        public bool SessionActive(int sessionId){Calls.Add("session");SessionArgument=sessionId;if(QueryFailure!=null)throw QueryFailure;return Active;}
        public bool DefaultInputDesktop(){Calls.Add("desktop");return Default;}
        public IntPtr ForegroundWindow(){Calls.Add("foreground");return Foregrounds.Count>0?Foregrounds.Dequeue():Foreground;}
        public IntPtr ShellWindow(){Calls.Add("shell");return ShellAvailable?Shell:IntPtr.Zero;}
        public bool WindowExists(IntPtr window){Need(window==Foreground,"Unexpected existence target.");Calls.Add("exists");return Exists;}
        public bool WindowVisible(IntPtr window){Need(window==Foreground,"Unexpected visibility target.");Calls.Add("visible");return Visible;}
        public bool WindowIconic(IntPtr window){Need(window==Foreground,"Unexpected iconic target.");Calls.Add("iconic");return Iconic;}
        public bool TryProcessId(IntPtr window,out uint processId)
        {
            if(window==Shell){Calls.Add("shell-pid");processId=ShellPid;return ShellPidAvailable;}
            Need(window==Foreground,"Unexpected process target.");Calls.Add("foreground-pid");
            var answer=ForegroundProcesses.Count>0?ForegroundProcesses.Dequeue():new ProcessAnswer(true,ForegroundPid);
            processId=answer.Pid;return answer.Success;
        }
        public bool TryWindowRect(IntPtr window,out EnvironmentWindowRect rect)
        {
            Need(window==Foreground,"Unexpected geometry target.");Calls.Add("rect");rect=Bounds;return RectAvailable;
        }
    }
    static NativeEnvironment Sampler(Queries queries){return new NativeEnvironment(queries,100,7);}
    static void Unknown(EnvironmentSample sample,string reason,bool interactive)
    {
        Need(!sample.Known && sample.InteractiveSession==interactive && !sample.Fullscreen && sample.Reason==reason,"Unexpected unknown observation.");
        Need(sample.BlockingReasons==(PauseReason.EnvironmentUnknown|(interactive?PauseReason.None:PauseReason.SessionUnavailable)),"Unknown observation did not block expected reasons.");
    }
    static void Known(EnvironmentSample sample,string reason,bool fullscreen)
    {
        Need(sample.Known && sample.InteractiveSession && sample.Fullscreen==fullscreen && sample.Reason==reason,"Unexpected known observation.");
        Need(sample.BlockingReasons==(fullscreen?PauseReason.Fullscreen:PauseReason.None),"Known observation has incorrect pause mask.");
    }
    static int Count(Queries queries,string call){return queries.Calls.FindAll(value=>value==call).Count;}
    static int Main()
    {
        Test("injected sampler rejects invalid identity or missing query provider",delegate{
            Throws<ArgumentNullException>(()=>new NativeEnvironment(null,100,7));
            Throws<ArgumentOutOfRangeException>(()=>new NativeEnvironment(new Queries(),0,7));
            Throws<ArgumentOutOfRangeException>(()=>new NativeEnvironment(new Queries(),100,-1));
        });
        Test("invalid targets fail before any environment query",delegate{
            var q=new Queries();var sampler=Sampler(q);
            Throws<ArgumentOutOfRangeException>(()=>sampler.Capture(Rectangle.Empty));
            Throws<ArgumentOutOfRangeException>(()=>sampler.Capture(new Rectangle(Int32.MaxValue,0,2,1)));
            Need(q.Calls.Count==0,"Invalid target queried the environment.");
        });
        Test("wrong-thread capture rejects before touching query provider",delegate{
            var q=new Queries();var sampler=Sampler(q);Exception observed=null;
            var worker=new Thread(delegate(){try{sampler.Capture(Target);}catch(Exception error){observed=error;}});
            worker.Start();worker.Join();Need(observed is InvalidOperationException && q.Calls.Count==0,"Thread affinity was bypassed.");
        });
        Test("inactive or unavailable WTS session blocks without foreground access",delegate{
            var q=new Queries{Active=false};Unknown(Sampler(q).Capture(Target),"session-not-active-or-unavailable",false);
            Need(q.SessionArgument==7 && q.Calls.Count==1,"Wrong session queried or queries continued.");
        });
        Test("unavailable or non-default input desktop blocks before foreground query",delegate{
            var q=new Queries{Default=false};Unknown(Sampler(q).Capture(Target),"input-desktop-unavailable",false);
            Need(q.Calls.Count==2 && Count(q,"foreground")==0,"Desktop failure queried foreground.");
        });
        Test("null foreground is unknown even with an active default desktop",delegate{
            var q=new Queries();q.Foregrounds.Enqueue(IntPtr.Zero);Unknown(Sampler(q).Capture(Target),"foreground-unavailable",true);
            Need(Count(q,"exists")==0 && Count(q,"shell")==0,"Null foreground queried window data.");
        });
        Test("destroyed foreground is unknown",delegate{
            var q=new Queries{Exists=false};Unknown(Sampler(q).Capture(Target),"foreground-unavailable",true);
            Need(Count(q,"visible")==0,"Destroyed window visibility was queried.");
        });
        Test("hidden foreground is unknown",delegate{
            var q=new Queries{Visible=false};Unknown(Sampler(q).Capture(Target),"foreground-unavailable",true);
            Need(Count(q,"iconic")==0,"Hidden window was treated as eligible.");
        });
        Test("minimized foreground is unknown",delegate{
            var q=new Queries{Iconic=true};Unknown(Sampler(q).Capture(Target),"foreground-unavailable",true);
            Need(Count(q,"foreground-pid")==0,"Minimized window process was used.");
        });
        Test("initial foreground PID lookup failure is unknown",delegate{
            var q=new Queries();q.ForegroundProcesses.Enqueue(new ProcessAnswer(false,0));
            Unknown(Sampler(q).Capture(Target),"foreground-unavailable",true);Need(Count(q,"shell")==0,"Missing PID proceeded to shell.");
        });
        Test("missing shell blocks instead of trusting covering foreground geometry",delegate{
            var q=new Queries{ShellAvailable=false};Unknown(Sampler(q).Capture(Target),"shell-unavailable",true);
            Need(Count(q,"rect")==0 && Count(q,"shell-pid")==0,"Missing shell proceeded to geometry.");
        });
        Test("unqueryable shell process is unknown",delegate{
            var q=new Queries{ShellPidAvailable=false};Unknown(Sampler(q).Capture(Target),"shell-unavailable",true);
            Need(Count(q,"rect")==0,"Missing shell identity proceeded to geometry.");
        });
        Test("foreign foreground geometry query failure is unknown",delegate{
            var q=new Queries{RectAvailable=false};Unknown(Sampler(q).Capture(Target),"foreground-geometry-unavailable",true);
            Need(Count(q,"foreground")==1,"Geometry failure pretended stable observation.");
        });
        Test("foreground HWND change invalidates a covering observation",delegate{
            var q=new Queries();q.Foregrounds.Enqueue(q.Foreground);q.Foregrounds.Enqueue(new IntPtr(33));
            Unknown(Sampler(q).Capture(Target),"foreground-changed-during-sample",true);
            Need(Count(q,"foreground-pid")==1,"Changed HWND was trusted for PID comparison.");
        });
        Test("second foreground PID query failure invalidates observation",delegate{
            var q=new Queries();q.ForegroundProcesses.Enqueue(new ProcessAnswer(true,300));q.ForegroundProcesses.Enqueue(new ProcessAnswer(false,0));
            Unknown(Sampler(q).Capture(Target),"foreground-changed-during-sample",true);
        });
        Test("same HWND with a different PID is rejected",delegate{
            var q=new Queries();q.ForegroundProcesses.Enqueue(new ProcessAnswer(true,300));q.ForegroundProcesses.Enqueue(new ProcessAnswer(true,301));
            Unknown(Sampler(q).Capture(Target),"foreground-changed-during-sample",true);
        });
        Test("stable own-process foreground is ignored without reading geometry",delegate{
            var q=new Queries{ForegroundPid=100,RectAvailable=false};Known(Sampler(q).Capture(Target),"own-or-shell-foreground",false);
            Need(Count(q,"rect")==0 && Count(q,"foreground-pid")==2,"Own window skipped stability or queried geometry.");
        });
        Test("stable shell-process foreground is ignored without reading geometry",delegate{
            var q=new Queries{ForegroundPid=200,RectAvailable=false};Known(Sampler(q).Capture(Target),"own-or-shell-foreground",false);
            Need(Count(q,"rect")==0 && Count(q,"foreground-pid")==2,"Shell window skipped stability or queried geometry.");
        });
        Test("ignored own foreground still requires a stable PID",delegate{
            var q=new Queries();q.ForegroundProcesses.Enqueue(new ProcessAnswer(true,100));q.ForegroundProcesses.Enqueue(new ProcessAnswer(true,300));
            Unknown(Sampler(q).Capture(Target),"foreground-changed-during-sample",true);
            Need(Count(q,"rect")==0,"Initially own window queried geometry.");
        });
        Test("stable foreign covering window sets only the fullscreen reason",delegate{
            var q=new Queries();Known(Sampler(q).Capture(Target),"target-covered",true);
            Need(Count(q,"rect")==1 && Count(q,"foreground")==2 && q.SessionArgument==7,"Capture did not use raw boundary queries.");
        });
        Test("incomplete foreign coverage remains known and non-fullscreen",delegate{
            var q=new Queries{Bounds=new EnvironmentWindowRect(0,0,1918,1080)};Known(Sampler(q).Capture(Target),"target-not-covered",false);
        });
        Test("negative-origin target works while other-monitor coverage is rejected",delegate{
            var q=new Queries{Bounds=new EnvironmentWindowRect(-1920,0,0,1080)};var sampler=Sampler(q);
            Known(sampler.Capture(new Rectangle(-1920,0,1920,1080)),"target-covered",true);
            Known(sampler.Capture(Target),"target-not-covered",false);
        });
        Test("zero inverted or overflowing raw rectangles remain unknown",delegate{
            foreach(var bounds in new[]{new EnvironmentWindowRect(0,0,0,1080),new EnvironmentWindowRect(10,0,0,1080),
                new EnvironmentWindowRect(Int32.MinValue,0,Int32.MaxValue,1080),new EnvironmentWindowRect(0,0,1920,0)})
                Unknown(Sampler(new Queries{Bounds=bounds}).Capture(Target),"invalid-foreground-geometry",true);
        });
        Test("query exceptions propagate without yielding a runnable sample",delegate{
            var q=new Queries{QueryFailure=new InvalidOperationException("scripted query failure")};
            Throws<InvalidOperationException>(()=>Sampler(q).Capture(Target));Need(q.Calls.Count==1,"Exception did not stop capture.");
        });
        Test("each capture rechecks session and never reuses a prior positive sample",delegate{
            var q=new Queries();var sampler=Sampler(q);Known(sampler.Capture(Target),"target-covered",true);
            int before=q.Calls.Count;q.Active=false;Unknown(sampler.Capture(Target),"session-not-active-or-unavailable",false);
            Need(q.Calls.Count==before+1 && Count(q,"session")==2,"Capture reused stale environment.");
        });
        Console.WriteLine("TOTAL passed="+passed+" failed="+failed);return failed==0?0:1;
    }
}
