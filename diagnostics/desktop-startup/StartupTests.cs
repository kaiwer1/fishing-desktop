using System;
using FishingDesktop.Diagnostics.DesktopLayer;
using FishingDesktop.Diagnostics.DesktopStartup;

static class StartupTests
{
    static readonly DesktopRect Target=new DesktopRect(0,0,1920,1080);
    static int passed,failed;
    sealed class Fake : IDesktopLayerApi,IShellLayerRequest
    {
        internal int Thread=77,Captures,Requests;
        internal Action<int> OnCapture;
        internal Func<NativeResult> OnRequest;
        internal DesktopSnapshot Snapshot=new DesktopSnapshot{Stable=true,ShellWindow=1,ThreadDpiContext=7,VirtualBounds=Target,
            ShellProcess=new ProcessIdentity{Id=10,StartTimeUtcTicks=100,Path=@"C:\Windows\explorer.exe"}};
        internal Fake(){Snapshot.TopLevels.Add(Window(1,0,"Progman"));Snapshot.Descendants.Add(Window(2,1,"SHELLDLL_DefView"));}
        internal WindowState Window(long handle,long parent,string name){return new WindowState{Handle=handle,Parent=parent,ProcessId=10,ThreadId=11,ClassName=name,Visible=true,Bounds=Target,DpiContext=7};}
        internal void AddWorker(){Snapshot.TopLevels.Add(Window(4,0,"WorkerW"));}
        public int CurrentProcessId {get{return 99;}}public int CurrentThreadId{get{return Thread;}}public string ExpectedExplorerPath{get{return @"C:\Windows\explorer.exe";}}
        public DesktopSnapshot CaptureDesktop(){Captures++;if(OnCapture!=null)OnCapture(Captures);return Snapshot;}
        public bool DpiContextsEqual(long left,long right){return left==right;}
        public WindowState ReadWindow(long handle){throw new Exception("must not read/attach a caller window");}
        public NativeResult Request(ShellLayerIntent intent){Requests++;if(OnRequest!=null)return OnRequest();AddWorker();return NativeResult.Ok();}
        public NativeResult SetStyle(long h,long s){throw new Exception("no window mutation");}
        public NativeResult SetExtendedStyle(long h,long s){throw new Exception("no window mutation");}
        public NativeResult SetParent(long h,long p){throw new Exception("no window mutation");}
        public NativeResult Position(long h,long p,DesktopRect b,uint f){throw new Exception("no window mutation");}
        public NativeResult Hide(long h){throw new Exception("no window mutation");}
        public NativeResult Close(long h){throw new Exception("no window mutation");}
    }
    static void Assert(bool value){if(!value)throw new Exception("unexpected startup result");}
    static void Test(string name,Action check){try{check();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
    static StartupResult Prepare(Fake api){return new DesktopStartupCoordinator(api,api).Prepare(Target,true);}
    static int Main()
    {
        Test("existing layer reused without request",()=>{var api=new Fake();api.AddWorker();var r=Prepare(api);Assert(r.Available && r.ReusedExisting && !r.RequestAttempted && api.Requests==0);});
        Test("missing layer remains read only by default",()=>{var api=new Fake();var r=new DesktopStartupCoordinator(api,api).Prepare(Target,false);Assert(!r.Available && r.Reason=="existing-workerw-unavailable" && api.Requests==0);});
        Test("one successful request followed by valid rediscovery",()=>{var api=new Fake();var r=Prepare(api);Assert(r.Available && r.RequestAttempted && !r.ReusedExisting && api.Requests==1);});
        Test("concurrent appearance is reused without request",()=>{var api=new Fake();api.OnCapture=n=>{if(n==3)api.AddWorker();};var r=Prepare(api);Assert(r.Available && r.ReusedExisting && api.Requests==0);});
        Test("occupied existing layer never triggers preparation",()=>{var api=new Fake();api.AddWorker();api.Snapshot.TopLevels[1].ForeignDescendantCount=1;var r=Prepare(api);Assert(!r.Available && r.Reason=="workerw-already-occupied" && api.Requests==0);});
        Test("raised desktop is unsupported without a request",()=>{var api=new Fake();api.Snapshot.TopLevels[0].ExtendedStyle=0x00200000;Assert(!Prepare(api).Available && api.Requests==0);});
        Test("unexpected shell path rejected",()=>{var api=new Fake();api.Snapshot.ShellProcess.Path=@"C:\Other\explorer.exe";Assert(!Prepare(api).Available && api.Requests==0);});
        Test("ambiguous icon hosts rejected",()=>{var api=new Fake();api.Snapshot.Descendants.Add(api.Window(3,1,"SHELLDLL_DefView"));Assert(!Prepare(api).Available && api.Requests==0);});
        Test("missing icon DPI cannot authorize a request",()=>{var api=new Fake();api.Snapshot.Descendants[0].DpiContext=0;Assert(!Prepare(api).Available && api.Requests==0);});
        Test("mismatched DPI cannot authorize a request",()=>{var api=new Fake();api.Snapshot.ThreadDpiContext=8;Assert(!Prepare(api).Available && api.Requests==0);});
        Test("foreign worker branch blocks a missing-layer request",()=>{var api=new Fake();var w=api.Window(8,0,"WorkerW");w.ForeignDescendantCount=1;api.Snapshot.TopLevels.Insert(0,w);Assert(!Prepare(api).Available && api.Requests==0);});
        Test("shell identity change before request rejected",()=>{var api=new Fake();api.OnCapture=n=>{if(n==4)api.Snapshot.ShellProcess.StartTimeUtcTicks++;};var r=Prepare(api);Assert(!r.Available && r.Reason=="shell-changed-before-request" && api.Requests==0);});
        Test("geometry change before request rejected",()=>{var api=new Fake();api.OnCapture=n=>{if(n==4)api.Snapshot.VirtualBounds=new DesktopRect(-1920,0,1920,1080);};Assert(!Prepare(api).Available && api.Requests==0);});
        Test("new shell after request does not validate the old intent",()=>{var api=new Fake();api.OnRequest=()=>{api.AddWorker();api.Snapshot.ShellProcess.StartTimeUtcTicks++;return NativeResult.Ok();};var r=Prepare(api);Assert(!r.Available && r.RequestAttempted && r.Reason=="shell-changed-after-request");});
        Test("successful message without a layer remains unavailable",()=>{var api=new Fake();api.OnRequest=()=>NativeResult.Ok();var r=Prepare(api);Assert(!r.Available && r.RequestAttempted && r.Reason.StartsWith("layer-unavailable-after-request:"));});
        Test("request failure retains native error and does not retry",()=>{var api=new Fake();api.OnRequest=()=>NativeResult.Error(5);var c=new DesktopStartupCoordinator(api,api);var first=c.Prepare(Target,true);var second=c.Prepare(Target,true);Assert(!first.Available && first.NativeError==5 && first.RequestAttempted && !second.Available && second.Reason=="request-already-attempted" && api.Requests==1);});
        Test("request exception is fail closed and consumed",()=>{var api=new Fake();api.OnRequest=()=>{throw new Exception("fixture");};var c=new DesktopStartupCoordinator(api,api);Assert(!c.Prepare(Target,true).Available && !c.Prepare(Target,true).Available && api.Requests==1);});
        Test("wrong UI thread cannot query or request",()=>{var api=new Fake();var c=new DesktopStartupCoordinator(api,api);api.Thread++;var r=c.Prepare(Target,true);Assert(!r.Available && r.Reason=="wrong-thread" && api.Captures==0 && api.Requests==0);});
        Test("out of bounds target cannot request",()=>{var api=new Fake();var r=new DesktopStartupCoordinator(api,api).Prepare(new DesktopRect(-1,0,1920,1080),true);Assert(!r.Available && api.Requests==0);});
        Test("unstable snapshot cannot request",()=>{var api=new Fake();api.Snapshot.Stable=false;Assert(!Prepare(api).Available && api.Requests==0);});
        Test("post-request occupied layer rejected",()=>{var api=new Fake();api.OnRequest=()=>{api.AddWorker();api.Snapshot.TopLevels[1].ForeignDescendantCount=1;return NativeResult.Ok();};Assert(!Prepare(api).Available && api.Requests==1);});
        Test("post-request DPI mismatch rejected",()=>{var api=new Fake();api.OnRequest=()=>{api.AddWorker();api.Snapshot.TopLevels[1].DpiContext=8;return NativeResult.Ok();};Assert(!Prepare(api).Available && api.Requests==1);});
        Test("caller process cannot masquerade as a cross-process shell",()=>{var api=new Fake();api.Snapshot.ShellProcess.Id=99;foreach(var w in api.Snapshot.TopLevels)w.ProcessId=99;foreach(var w in api.Snapshot.Descendants)w.ProcessId=99;Assert(!Prepare(api).Available && api.Requests==0);});
        Test("later query failure does not claim another request",()=>{var api=new Fake();api.OnRequest=()=>NativeResult.Error(0);var c=new DesktopStartupCoordinator(api,api);Assert(c.Prepare(Target,true).RequestAttempted);api.OnCapture=n=>{throw new Exception("query fixture");};Assert(!c.Prepare(Target,true).RequestAttempted && api.Requests==1);});
        Console.WriteLine("RESULT passed="+passed+" failed="+failed);return failed==0?0:1;
    }
}
