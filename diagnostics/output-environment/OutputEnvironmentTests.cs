using System;
using System.Collections.Generic;
using System.Threading;
using FishingDesktop.Diagnostics.HostPolicy;
using FishingDesktop.Diagnostics.OutputEnvironment;

static class OutputEnvironmentTests
{
    static int passed,failed;
    static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Reject<T>(Action action) where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
    static OutputEnvironmentTarget A(){return new OutputEnvironmentTarget("left",-1920,0,1920,1080);}
    static OutputEnvironmentTarget B(){return new OutputEnvironmentTarget("right",0,0,1920,1080);}
    static OutputEnvironmentTarget[] Pair(){return new[]{A(),B()};}
    struct Pid{internal readonly bool Ok;internal readonly uint Value;internal Pid(bool ok,uint value){Ok=ok;Value=value;}}
    struct Bounds{internal readonly bool Ok;internal readonly EnvironmentWindowRect Rect;internal Bounds(bool ok,int l,int t,int r,int b){Ok=ok;Rect=new EnvironmentWindowRect(l,t,r,b);}}
    sealed class Queries : INativeEnvironmentQueries
    {
        internal readonly IntPtr Foreground=new IntPtr(11),Shell=new IntPtr(22);
        internal readonly List<string> Calls=new List<string>();
        readonly Dictionary<string,int> counts=new Dictionary<string,int>();
        internal string ThrowAt;
        internal Func<int,bool> Active=delegate{return true;},Desktop=delegate{return true;},Awareness=delegate{return true;};
        internal Func<int,IntPtr> Foregrounds,Shells;
        internal Func<int,bool> ForegroundExists=delegate{return true;},ShellExists=delegate{return true;},Visible=delegate{return true;},Iconic=delegate{return false;};
        internal Func<int,Pid> ForegroundPid=delegate{return new Pid(true,300);},ShellPid=delegate{return new Pid(true,200);};
        internal Func<int,Bounds> Geometry=delegate{return new Bounds(true,0,0,1920,1080);};
        internal Queries(){Foregrounds=delegate{return Foreground;};Shells=delegate{return Shell;};}
        internal int Count(string name){int value;return counts.TryGetValue(name,out value)?value:0;}
        int Enter(string name){int value=Count(name)+1;counts[name]=value;Calls.Add(name);if(ThrowAt==name+"#"+value)throw new InvalidOperationException("Private HWND PID and other native detail must not leak.");return value;}
        internal bool Physical(){return Awareness(Enter("awareness"));}
        public bool SessionActive(int sessionId){Need(sessionId==7,"Wrong session queried.");return Active(Enter("session"));}
        public bool DefaultInputDesktop(){return Desktop(Enter("desktop"));}
        public IntPtr ForegroundWindow(){return Foregrounds(Enter("foreground"));}
        public IntPtr ShellWindow(){return Shells(Enter("shell"));}
        public bool WindowExists(IntPtr window){if(window==Foreground)return ForegroundExists(Enter("foreground-exists"));Need(window==Shell,"Unexpected existence query.");return ShellExists(Enter("shell-exists"));}
        public bool WindowVisible(IntPtr window){Need(window==Foreground,"Unexpected visibility query.");return Visible(Enter("visible"));}
        public bool WindowIconic(IntPtr window){Need(window==Foreground,"Unexpected iconic query.");return Iconic(Enter("iconic"));}
        public bool TryProcessId(IntPtr window,out uint pid){Pid answer;if(window==Foreground)answer=ForegroundPid(Enter("foreground-pid"));else{Need(window==Shell,"Unexpected process query.");answer=ShellPid(Enter("shell-pid"));}pid=answer.Value;return answer.Ok;}
        public bool TryWindowRect(IntPtr window,out EnvironmentWindowRect rect){Need(window==Foreground,"Unexpected geometry query.");var answer=Geometry(Enter("rect"));rect=answer.Rect;return answer.Ok;}
    }
    static OutputEnvironmentCapture Capture(Queries q,int cap=16){return new OutputEnvironmentCapture(q,100,7,q.Physical,cap);}
    static void Unknown(OutputEnvironmentSample sample,OutputEnvironmentReason reason)
    {
        var global=OutputEnvironmentGlobalBlock.EnvironmentUnknown;
        if(reason==OutputEnvironmentReason.SessionUnavailable||reason==OutputEnvironmentReason.InputDesktopUnavailable||reason==OutputEnvironmentReason.SessionChanged||reason==OutputEnvironmentReason.DesktopChanged)global|=OutputEnvironmentGlobalBlock.SessionUnavailable;
        Need(!sample.Known&&sample.Reason==reason&&sample.GlobalBlocks==global&&sample.Outputs.Count==0,"Unknown sample exposed local state or wrong global reason: "+sample.Reason);
    }
    static void BadTarget(string name,Func<IEnumerable<OutputEnvironmentTarget>> rows)
    {Test(name,()=>{var q=new Queries();Unknown(Capture(q).Capture(19,rows()),OutputEnvironmentReason.InvalidTopology);Need(q.Calls.Count==0,"Malformed topology queried native state.");});}
    static IEnumerable<OutputEnvironmentTarget> Interrupted(){yield return A();throw new InvalidOperationException("Private enumeration error.");}
    static IEnumerable<OutputEnvironmentTarget> TooMany(){for(int i=0;;i++)yield return new OutputEnvironmentTarget("out"+i,i*10,0,10,10);}
    static void Modified(string name,Action<Queries> setup,OutputEnvironmentReason reason)
    {Test(name,()=>{var q=new Queries();setup(q);Unknown(Capture(q).Capture(19,Pair()),reason);});}
    static int Main()
    {
        Test("one coherent observation assigns fullscreen only to the covered synthetic output",()=>{
            var q=new Queries();var sample=Capture(q).Capture(19,Pair());
            Need(sample.Known&&sample.TopologyGeneration==19&&sample.GlobalBlocks==OutputEnvironmentGlobalBlock.None&&sample.Reason==OutputEnvironmentReason.None,"Stable batch unavailable.");
            Need(sample.Outputs.Count==2&&sample.Outputs[0].Key=="left"&&!sample.Outputs[0].Fullscreen&&sample.Outputs[1].Key=="right"&&sample.Outputs[1].Fullscreen,"One output coverage became a global pause or covered both outputs.");
            foreach(string name in new[]{"awareness","session","desktop","foreground","foreground-exists","visible","iconic","foreground-pid","shell","shell-exists","shell-pid","rect"})Need(q.Count(name)==2,"Environment was not bracketed: "+name);
        });
        Test("adding outputs never repeats environment sampling per output",()=>{
            var one=new Queries();var many=new Queries();Capture(one).Capture(19,new[]{B()});Capture(many).Capture(19,Pair());
            Need(String.Join(",",one.Calls.ToArray())==String.Join(",",many.Calls.ToArray()),"Outputs caused separate foreground/session observations.");
        });
        Test("synthetic spanning foreground can cover both declared outputs",()=>{
            var q=new Queries{Geometry=delegate{return new Bounds(true,-1920,0,1920,1080);}};var s=Capture(q).Capture(19,Pair());
            Need(s.Known&&s.Outputs[0].Fullscreen&&s.Outputs[1].Fullscreen&&s.GlobalBlocks==OutputEnvironmentGlobalBlock.None,"Spanning coverage became a global Fullscreen flag.");
        });
        Test("negative origin coverage does not mark its neighboring output",()=>{
            var q=new Queries{Geometry=delegate{return new Bounds(true,-1920,0,0,1080);}};var s=Capture(q).Capture(19,Pair());
            Need(s.Known&&s.Outputs[0].Fullscreen&&!s.Outputs[1].Fullscreen,"Negative coordinates targeted the wrong output.");
        });
        Test("partial foreground leaves both output fullscreen flags clear",()=>{
            var q=new Queries{Geometry=delegate{return new Bounds(true,0,0,1920,1000);}};var s=Capture(q).Capture(19,Pair());Need(s.Known&&!s.Outputs[0].Fullscreen&&!s.Outputs[1].Fullscreen,"Partial window counted as coverage.");
        });
        Test("existing Covers one physical pixel tolerance is reused",()=>{
            var q=new Queries{Geometry=delegate{return new Bounds(true,1,1,1919,1079);}};Need(Capture(q).Capture(19,new[]{B()}).Outputs[0].Fullscreen,"One pixel tolerance changed.");
            q.Geometry=delegate{return new Bounds(true,2,0,1920,1080);};Need(!Capture(q).Capture(19,new[]{B()}).Outputs[0].Fullscreen,"Two pixel gap counted as covered.");
        });
        Test("extreme valid negative coordinates avoid arithmetic overflow",()=>{
            var q=new Queries{Geometry=delegate{return new Bounds(true,Int32.MinValue,Int32.MinValue,Int32.MinValue+1920,Int32.MinValue+1080);}};
            var s=Capture(q).Capture(19,new[]{new OutputEnvironmentTarget("edge",Int32.MinValue,Int32.MinValue,1920,1080)});Need(s.Known&&s.Outputs[0].Fullscreen,"Negative endpoint overflowed.");
        });
        BadTarget("null target batch is globally unknown",()=>null);
        BadTarget("empty target batch is globally unknown",()=>new OutputEnvironmentTarget[0]);
        BadTarget("null target row is globally unknown",()=>new OutputEnvironmentTarget[]{A(),null});
        BadTarget("bad logical alias is rejected",()=>new[]{new OutputEnvironmentTarget("../physical",0,0,1920,1080)});
        BadTarget("case-insensitive duplicate aliases are rejected",()=>new[]{A(),new OutputEnvironmentTarget("LEFT",0,0,1920,1080)});
        BadTarget("zero target width is rejected",()=>new[]{new OutputEnvironmentTarget("bad",0,0,0,1080)});
        BadTarget("negative target height is rejected",()=>new[]{new OutputEnvironmentTarget("bad",0,0,1920,-1)});
        BadTarget("target X endpoint overflow is rejected",()=>new[]{new OutputEnvironmentTarget("bad",Int32.MaxValue,0,2,1080)});
        BadTarget("target Y endpoint overflow is rejected",()=>new[]{new OutputEnvironmentTarget("bad",0,Int32.MaxValue,1920,2)});
        BadTarget("overlapping target rectangles are rejected",()=>new[]{B(),new OutputEnvironmentTarget("other",100,0,1920,1080)});
        BadTarget("interrupted target enumeration exposes no partial output",()=>Interrupted());
        BadTarget("unbounded target input is stopped at the configured capacity",()=>TooMany());
        Test("configured one-output capacity rejects two before native queries",()=>{
            var q=new Queries();Unknown(Capture(q,1).Capture(19,Pair()),OutputEnvironmentReason.InvalidTopology);Need(q.Calls.Count==0,"Capacity rejection queried environment.");
        });
        Modified("unconfirmed coordinate context stops before native queries",q=>q.Awareness=delegate{return false;},OutputEnvironmentReason.AwarenessUnconfirmed);
        Modified("lost coordinate context after metadata verification rejects all rows",q=>q.Awareness=call=>call==1,OutputEnvironmentReason.AwarenessUnconfirmed);
        Modified("inactive session is globally unavailable",q=>q.Active=delegate{return false;},OutputEnvironmentReason.SessionUnavailable);
        Modified("non-default or unavailable desktop is globally unavailable",q=>q.Desktop=delegate{return false;},OutputEnvironmentReason.InputDesktopUnavailable);
        Modified("null foreground has no implicit fullscreen default",q=>q.Foregrounds=delegate{return IntPtr.Zero;},OutputEnvironmentReason.ForegroundUnavailable);
        Modified("destroyed foreground is unknown",q=>q.ForegroundExists=delegate{return false;},OutputEnvironmentReason.ForegroundUnavailable);
        Modified("hidden foreground is unknown",q=>q.Visible=delegate{return false;},OutputEnvironmentReason.ForegroundUnavailable);
        Modified("iconic foreground is unknown",q=>q.Iconic=delegate{return true;},OutputEnvironmentReason.ForegroundUnavailable);
        Modified("foreground PID lookup failure is unknown",q=>q.ForegroundPid=delegate{return new Pid(false,0);},OutputEnvironmentReason.ForegroundUnavailable);
        Modified("zero foreground PID is unknown even if query returned true",q=>q.ForegroundPid=delegate{return new Pid(true,0);},OutputEnvironmentReason.ForegroundUnavailable);
        Modified("missing shell is unknown",q=>q.Shells=delegate{return IntPtr.Zero;},OutputEnvironmentReason.ShellUnavailable);
        Modified("destroyed shell is unknown",q=>q.ShellExists=delegate{return false;},OutputEnvironmentReason.ShellUnavailable);
        Modified("shell PID lookup failure is unknown",q=>q.ShellPid=delegate{return new Pid(false,0);},OutputEnvironmentReason.ShellUnavailable);
        Modified("zero shell PID is unknown",q=>q.ShellPid=delegate{return new Pid(true,0);},OutputEnvironmentReason.ShellUnavailable);
        Modified("geometry lookup failure is unknown",q=>q.Geometry=delegate{return new Bounds(false,0,0,0,0);},OutputEnvironmentReason.GeometryUnavailable);
        Modified("zero geometry width is unknown",q=>q.Geometry=delegate{return new Bounds(true,0,0,0,1080);},OutputEnvironmentReason.InvalidGeometry);
        Modified("reversed geometry is unknown",q=>q.Geometry=delegate{return new Bounds(true,10,10,0,0);},OutputEnvironmentReason.InvalidGeometry);
        Modified("geometry wider than signed rectangle capacity is unknown",q=>q.Geometry=delegate{return new Bounds(true,Int32.MinValue,0,Int32.MaxValue,1080);},OutputEnvironmentReason.InvalidGeometry);
        Test("own foreground is ignored without any geometry access but still rechecked",()=>{
            var q=new Queries{ForegroundPid=delegate{return new Pid(true,100);},ThrowAt="rect#1"};var s=Capture(q).Capture(19,Pair());
            Need(s.Known&&s.Reason==OutputEnvironmentReason.OwnOrShellForeground&&!s.Outputs[0].Fullscreen&&!s.Outputs[1].Fullscreen&&q.Count("rect")==0&&q.Count("foreground-pid")==2&&q.Count("session")==2,"Own window bypassed scope revalidation.");
        });
        Test("shell-process foreground is ignored without geometry access",()=>{
            var q=new Queries{ForegroundPid=delegate{return new Pid(true,200);},ThrowAt="rect#1"};var s=Capture(q).Capture(19,Pair());Need(s.Known&&s.Reason==OutputEnvironmentReason.OwnOrShellForeground&&q.Count("rect")==0&&!s.Outputs[1].Fullscreen,"Shell foreground was treated as foreign coverage.");
        });
        Modified("own foreground cannot bypass missing shell verification",q=>{q.ForegroundPid=delegate{return new Pid(true,100);};q.Shells=delegate{return IntPtr.Zero;};},OutputEnvironmentReason.ShellUnavailable);
        Modified("foreground handle replacement rejects all output results",q=>q.Foregrounds=call=>call==1?q.Foreground:new IntPtr(33),OutputEnvironmentReason.ForegroundChanged);
        Modified("foreground disappearance during closing verification is unknown",q=>q.ForegroundExists=call=>call==1,OutputEnvironmentReason.ForegroundChanged);
        Modified("foreground becoming hidden during closing verification is unknown",q=>q.Visible=call=>call==1,OutputEnvironmentReason.ForegroundChanged);
        Modified("foreground becoming iconic during closing verification is unknown",q=>q.Iconic=call=>call>1,OutputEnvironmentReason.ForegroundChanged);
        Modified("foreground PID replacement is unknown",q=>q.ForegroundPid=call=>new Pid(true,call==1?300u:301u),OutputEnvironmentReason.ForegroundChanged);
        Modified("closing foreground PID lookup failure is unknown",q=>q.ForegroundPid=call=>new Pid(call==1,300),OutputEnvironmentReason.ForegroundChanged);
        Modified("shell handle replacement is unknown",q=>q.Shells=call=>call==1?q.Shell:new IntPtr(44),OutputEnvironmentReason.ShellChanged);
        Modified("shell disappearance during closing verification is unknown",q=>q.ShellExists=call=>call==1,OutputEnvironmentReason.ShellChanged);
        Modified("shell PID replacement is unknown",q=>q.ShellPid=call=>new Pid(true,call==1?200u:201u),OutputEnvironmentReason.ShellChanged);
        Modified("closing shell PID lookup failure is unknown",q=>q.ShellPid=call=>new Pid(call==1,200),OutputEnvironmentReason.ShellChanged);
        Modified("closing geometry query failure is unknown",q=>q.Geometry=call=>new Bounds(call==1,0,0,1920,1080),OutputEnvironmentReason.GeometryChanged);
        Modified("same HWND PID with changed geometry is unknown",q=>q.Geometry=call=>new Bounds(true,call==1?0:1,0,1920,1080),OutputEnvironmentReason.GeometryChanged);
        Modified("closing invalid geometry is unknown",q=>q.Geometry=call=>new Bounds(true,0,0,call==1?1920:0,1080),OutputEnvironmentReason.GeometryChanged);
        Modified("foreground switch inside second geometry query cannot qualify the old HWND",q=>q.Geometry=call=>{
            if(call==2)q.Foregrounds=delegate{return new IntPtr(33);};return new Bounds(true,0,0,1920,1080);
        },OutputEnvironmentReason.ForegroundChanged);
        Modified("foreground PID change inside second geometry query is detected",q=>q.Geometry=call=>{
            if(call==2)q.ForegroundPid=delegate{return new Pid(true,301);};return new Bounds(true,0,0,1920,1080);
        },OutputEnvironmentReason.ForegroundChanged);
        Modified("shell replacement inside second geometry query is detected",q=>q.Geometry=call=>{
            if(call==2)q.Shells=delegate{return new IntPtr(44);};return new Bounds(true,0,0,1920,1080);
        },OutputEnvironmentReason.ShellChanged);
        Modified("session deactivation during the observation blocks globally",q=>q.Active=call=>call==1,OutputEnvironmentReason.SessionChanged);
        Modified("desktop becoming unavailable during the observation blocks globally",q=>q.Desktop=call=>call==1,OutputEnvironmentReason.DesktopChanged);
        Test("query exceptions are fixed unknown reasons without native detail",()=>{
            foreach(string step in new[]{"awareness#1","session#1","desktop#1","foreground#1","shell#1","rect#1","foreground-pid#2","shell-pid#2","rect#2","session#2","desktop#2","awareness#2"}){
                var q=new Queries{ThrowAt=step};Unknown(Capture(q).Capture(19,Pair()),OutputEnvironmentReason.QueryFailed);
            }
        });
        Test("a later unavailable observation never reuses earlier per-output positives",()=>{
            var q=new Queries();var capture=Capture(q);var prior=capture.Capture(19,Pair());q.Active=delegate{return false;};var next=capture.Capture(19,Pair());
            Need(prior.Known&&prior.Outputs[1].Fullscreen&&prior.TopologyGeneration==19,"Prior immutable sample changed.");Unknown(next,OutputEnvironmentReason.SessionUnavailable);Need(next.TopologyGeneration==19,"Unknown lost its caller topology binding.");
        });
        Test("recovery samples fresh environment and keeps caller generation independent",()=>{
            var q=new Queries();var capture=Capture(q);var first=capture.Capture(700,Pair());q.Geometry=delegate{return new Bounds(true,-1920,0,0,1080);};var next=capture.Capture(900,Pair());
            Need(first.TopologyGeneration==700&&next.TopologyGeneration==900&&first.Outputs[1].Fullscreen&&!next.Outputs[1].Fullscreen&&next.Outputs[0].Fullscreen&&q.Count("rect")==4,"Sampling reused coverage or invented its own generation.");
        });
        Test("caller collection mutation cannot change an immutable sample",()=>{
            var q=new Queries();var targets=new List<OutputEnvironmentTarget>(Pair());var sample=Capture(q).Capture(19,targets);targets.Clear();
            Need(sample.Outputs.Count==2,"Clearing target collection changed returned evidence.");Reject<NotSupportedException>(()=>((IList<OutputEnvironmentRow>)sample.Outputs).Clear());
        });
        Test("wrong-thread observation is unavailable without querying native state",()=>{
            var q=new Queries();var capture=Capture(q);OutputEnvironmentSample sample=null;var thread=new Thread(()=>sample=capture.Capture(19,Pair()));thread.Start();thread.Join();
            Unknown(sample,OutputEnvironmentReason.WrongThread);Need(q.Calls.Count==0,"Wrong thread touched environment queries.");
        });
        Test("invalid construction and non-positive generation reject before queries",()=>{
            var q=new Queries();Reject<ArgumentNullException>(()=>new OutputEnvironmentCapture(null,100,7,q.Physical));Reject<ArgumentNullException>(()=>new OutputEnvironmentCapture(q,100,7,null));
            Reject<ArgumentOutOfRangeException>(()=>new OutputEnvironmentCapture(q,0,7,q.Physical));Reject<ArgumentOutOfRangeException>(()=>new OutputEnvironmentCapture(q,100,-1,q.Physical));
            Reject<ArgumentOutOfRangeException>(()=>Capture(q,0));Reject<ArgumentOutOfRangeException>(()=>Capture(q,65));Reject<ArgumentOutOfRangeException>(()=>Capture(q).Capture(0,Pair()));Need(q.Calls.Count==0,"Invalid construction queried native state.");
        });
        Test("declaration factories copy rows and prevent ambiguous known or unknown data",()=>{
            var rows=new List<OutputEnvironmentRow>{new OutputEnvironmentRow("RIGHT",true)};var sample=OutputEnvironmentSample.CreateKnown(19,rows);rows.Clear();Need(sample.Outputs.Count==1&&sample.Outputs[0].Key=="right","Factory kept mutable collection or alias.");
            Reject<ArgumentException>(()=>OutputEnvironmentSample.CreateKnown(19,new OutputEnvironmentRow[0]));Reject<ArgumentException>(()=>OutputEnvironmentSample.CreateKnown(19,new[]{new OutputEnvironmentRow("one",true)},true));
            Reject<ArgumentOutOfRangeException>(()=>OutputEnvironmentSample.CreateUnavailable(19,OutputEnvironmentReason.None));Reject<ArgumentOutOfRangeException>(()=>OutputEnvironmentSample.CreateUnavailable(19,(OutputEnvironmentReason)999));
            Need(typeof(OutputEnvironmentRow).GetFields().Length==2,"Output rows expose raw window/process data.");
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed; synthetic rectangles and raw query responses only, no native/UI execution.");return failed==0?0:1;
    }
}
