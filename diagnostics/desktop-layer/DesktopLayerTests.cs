using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FishingDesktop.Diagnostics.DesktopLayer;

static class DesktopLayerTests
{
    static int passed,failed;
    static readonly DesktopRect Screen=new DesktopRect(0,0,1920,1080);
    static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex.Message);}}
    sealed class Fake : IDesktopLayerApi
    {
        public int Pid=99,Thread=77,Captures;
        public DesktopSnapshot Snapshot;
        public WindowState Own;
        public readonly List<string> Calls=new List<string>();
        public readonly Dictionary<string,int> Fail=new Dictionary<string,int>();
        public Action<int> OnCapture;
        public Action<string> Before;
        public bool DpiThrows;
        public Fake(){
            Snapshot=new DesktopSnapshot{Stable=true,ShellWindow=1,ThreadDpiContext=-4,VirtualBounds=Screen,
                ShellProcess=new ProcessIdentity{Id=10,StartTimeUtcTicks=123,Path=@"C:\Windows\explorer.exe"}};
            Snapshot.TopLevels.Add(Window(1,0,"Progman"));Snapshot.TopLevels.Add(Window(2,0,"WorkerW"));Snapshot.TopLevels.Add(Window(4,0,"WorkerW"));
            Snapshot.Descendants.Add(Window(3,2,"SHELLDLL_DefView"));
            Own=new WindowState{Handle=100,Parent=0,ClassName="Owned",ProcessId=99,ThreadId=77,DpiContext=-4,Style=DesktopLayerController.PopupStyle,
                Bounds=new DesktopRect(10,20,210,120)};
        }
        static WindowState Window(long handle,long parent,string name){return new WindowState{Handle=handle,Parent=parent,ClassName=name,ProcessId=10,ThreadId=20,DpiContext=-4,Visible=true,Bounds=Screen};}
        public int CurrentProcessId{get{return Pid;}}public int CurrentThreadId{get{return Thread;}}public string ExpectedExplorerPath{get{return @"C:\Windows\explorer.exe";}}
        public DesktopSnapshot CaptureDesktop(){Captures++;if(OnCapture!=null)OnCapture(Captures);return Snapshot;}
        public WindowState ReadWindow(long handle){return Own!=null && Own.Handle==handle?Own.Copy():null;}
        public bool DpiContextsEqual(long left,long right){if(DpiThrows)throw new Exception("dpi failed");return left==right;}
        NativeResult Op(string name,long handle){
            if(Before!=null)Before(name);
            if(Own==null || Own.Handle!=handle || Own.ProcessId!=Pid || Own.ThreadId!=Thread)return NativeResult.Error(5);
            Calls.Add(name);
            int count;if(Fail.TryGetValue(name,out count) && count>0){Fail[name]=count-1;return NativeResult.Error(5);}
            return NativeResult.Ok();
        }
        public NativeResult SetStyle(long handle,long style){var r=Op("style",handle);if(r.Success)Own.Style=style;return r;}
        public NativeResult SetExtendedStyle(long handle,long style){var r=Op("exstyle",handle);if(r.Success)Own.ExtendedStyle=style;return r;}
        public NativeResult SetParent(long handle,long parent){var r=Op("parent:"+parent,handle);if(r.Success){long old=Own.Parent;Own.Parent=parent;return NativeResult.FromPreviousValue(old,0);}return r;}
        public NativeResult Position(long handle,long parent,DesktopRect rect,uint flags){Assert(flags==DesktopLayerController.SafePositionFlags,"unsafe positioning flags");Assert((flags&0x10)!=0 && (flags&0x40)==0,"activation/show flag");var r=Op("position",handle);if(r.Success){if(Own.Parent!=parent)return NativeResult.Error(87);Own.Bounds=rect;}return r;}
        public NativeResult Hide(long handle){var r=Op("hide",handle);if(r.Success)Own.Visible=false;return r;}
        public NativeResult Close(long handle){var r=Op("close",handle);if(r.Success)Own=null;return r;}
    }
    static DesktopCandidate Candidate(Fake api,DesktopLayerController controller){var r=controller.Discover(Screen);Assert(r.Available,"discovery: "+r.Reason);return r.Candidate;}
    static AttachmentResult Attach(Fake api,DesktopLayerController controller){return controller.Attach(Candidate(api,controller),100);}
    static void Reject(Action<Fake> edit,string reason){var a=new Fake();edit(a);var r=new DesktopLayerController(a).Discover(Screen);Assert(!r.Available && r.Reason==reason,r.Reason);Assert(a.Calls.Count==0,"discovery mutated windows");}
    static void FixedTests()
    {
        Test("classic existing rear WorkerW selected without mutation",()=>{var a=new Fake();var c=Candidate(a,new DesktopLayerController(a));Assert(c.WorkerHandle==4 && c.IconHostHandle==2 && a.Calls.Count==0,"selection");});
        Test("missing existing layer unavailable",()=>Reject(a=>a.Snapshot.TopLevels.RemoveAt(2),"existing-workerw-unavailable"));
        Test("shell path mismatch rejected",()=>Reject(a=>a.Snapshot.ShellProcess.Path=@"C:\Elsewhere\explorer.exe","unexpected-shell-process"));
        Test("unstable shell rejected",()=>Reject(a=>a.Snapshot.Stable=false,"shell-identity-unavailable"));
        Test("top level ancestry rejected",()=>Reject(a=>a.Snapshot.TopLevels[2].Parent=1,"invalid-shell-window-ancestry"));
        Test("nested DefView unsupported",()=>Reject(a=>a.Snapshot.Descendants[0].Parent=6,"icon-host-unavailable-or-ambiguous"));
        Test("ambiguous icon hosts rejected",()=>Reject(a=>a.Snapshot.Descendants.Add(a.Snapshot.Descendants[0].Copy()),"icon-host-unavailable-or-ambiguous"));
        Test("raised desktop rejected",()=>Reject(a=>a.Snapshot.TopLevels[0].ExtendedStyle=0x200000,"raised-desktop-not-supported"));
        Test("hidden WorkerW rejected",()=>Reject(a=>a.Snapshot.TopLevels[2].Visible=false,"workerw-geometry-unavailable"));
        Test("undersized WorkerW rejected",()=>Reject(a=>a.Snapshot.TopLevels[2].Bounds=new DesktopRect(0,0,100,100),"workerw-geometry-unavailable"));
        Test("foreign occupied layer rejected",()=>Reject(a=>a.Snapshot.TopLevels[2].ForeignDescendantCount=1,"workerw-already-occupied"));
        Test("DPI mismatch unavailable without global changes",()=>Reject(a=>a.Snapshot.ThreadDpiContext=-2,"dpi-context-incompatible"));
        Test("DPI API failure unavailable",()=>Reject(a=>a.DpiThrows=true,"desktop-query-failed"));
        Test("invalid bounds rejected",()=>{var a=new Fake();Assert(!new DesktopLayerController(a).Discover(new DesktopRect(0,0,0,1)).Available,"zero size");});
        Test("outside virtual desktop rejected",()=>Reject(a=>a.Snapshot.VirtualBounds=new DesktopRect(0,0,500,500),"target-outside-virtual-desktop"));
        Test("SetParent NULL with last error zero means success",()=>{Assert(NativeResult.FromPreviousValue(0,0).Success,"NULL success");Assert(!NativeResult.FromPreviousValue(0,5).Success,"NULL error");});
        Test("attach sets child and noactivate while remaining hidden",()=>{var a=new Fake();var c=new DesktopLayerController(a);var r=Attach(a,c);Assert(r.Success && r.Lease.IsActive,"attach");Assert(a.Own.Parent==4 && !a.Own.Visible,"hidden child");Assert((a.Own.Style&DesktopLayerController.ChildStyle)!=0 && (a.Own.Style&DesktopLayerController.PopupStyle)==0,"style");Assert((a.Own.ExtendedStyle&DesktopLayerController.NoActivateStyle)!=0,"noactivate");Assert(c.ValidateAndRecover(r.Lease).Success,"valid lease");});
        Test("foreign process window never mutated",()=>{var a=new Fake();var c=new DesktopLayerController(a);var candidate=Candidate(a,c);a.Own.ProcessId=555;Assert(!c.Attach(candidate,100).Success && a.Calls.Count==0,"foreign mutation");});
        Test("ownership change during attachment blocks later mutations",()=>{var a=new Fake();a.Before=name=>{if(name=="parent:4")a.Own.ProcessId=555;};var r=Attach(a,new DesktopLayerController(a));Assert(!r.Success && r.Cleanup=="original-window-no-longer-owned" && !a.Calls.Contains("parent:4") && !a.Calls.Contains("parent:0"),"identity change");});
        Test("wrong UI thread never mutated",()=>{var a=new Fake();var c=new DesktopLayerController(a);var candidate=Candidate(a,c);a.Thread++;Assert(!c.Attach(candidate,100).Success && a.Calls.Count==0,"wrong thread");});
        Test("visible input window rejected before mutation",()=>{var a=new Fake();a.Own.Visible=true;var r=Attach(a,new DesktopLayerController(a));Assert(!r.Success && r.Reason=="owned-window-must-start-hidden" && a.Calls.Count==0,"visible");});
        Test("shell PID reuse before attach rejected",()=>{var a=new Fake();var c=new DesktopLayerController(a);var candidate=Candidate(a,c);a.Snapshot.ShellProcess.StartTimeUtcTicks++;var r=c.Attach(candidate,100);Assert(!r.Success && a.Calls.Count==0 && r.Reason=="desktop-parent-identity-changed","reuse");});
        Test("parent changes between style and attach restores hidden",()=>{var a=new Fake();a.OnCapture=n=>{if(n==3)a.Snapshot.ShellProcess.StartTimeUtcTicks++;};var r=Attach(a,new DesktopLayerController(a));Assert(!r.Success && r.Cleanup=="restored-hidden" && !a.Calls.Contains("parent:4"),"race cleanup");});
        Test("parent changes after SetParent restores hidden",()=>{var a=new Fake();a.OnCapture=n=>{if(n==4)a.Snapshot.TopLevels[2].Handle=40;};var r=Attach(a,new DesktopLayerController(a));Assert(!r.Success && r.Cleanup=="restored-hidden" && a.Own.Parent==0 && !a.Own.Visible,"post parent race");});
        Test("SetParent failure restores original hidden state and error",()=>{var a=new Fake();var original=a.Own.Copy();a.Fail["parent:4"]=1;var r=Attach(a,new DesktopLayerController(a));Assert(!r.Success && r.Reason=="native-attachment-failed" && r.Cleanup=="restored-hidden","failure");Assert(r.Issues.Exists(i=>i.Stage=="set-parent" && i.NativeError==5),"diagnostic");Assert(a.Own.Style==original.Style && a.Own.Bounds.Same(original.Bounds),"restore");});
        Test("rollback failure closes own window",()=>{var a=new Fake();a.Fail["position"]=2;var r=Attach(a,new DesktopLayerController(a));Assert(!r.Success && r.Cleanup=="own-window-closed" && a.Own==null,"close fallback");});
        Test("hide failure closes before detaching",()=>{var a=new Fake();var c=new DesktopLayerController(a);var r=Attach(a,c);a.Own.Visible=true;a.Calls.Clear();a.Fail["hide"]=1;var end=c.Detach(r.Lease);Assert(end.Success && end.Cleanup=="own-window-closed" && !a.Calls.Contains("parent:0"),"visible detach");});
        Test("parent disappearance invalidates and recovers visible owned child",()=>{var a=new Fake();var c=new DesktopLayerController(a);var r=Attach(a,c);a.Own.Visible=true;a.Snapshot.TopLevels.RemoveAt(2);var check=c.ValidateAndRecover(r.Lease);Assert(!check.Success && !r.Lease.IsActive && check.Reason=="existing-workerw-unavailable" && check.Cleanup=="restored-hidden","invalid parent");Assert(!a.Own.Visible && a.Own.Parent==0,"hidden recovery");});
        Test("parent destroys child lease becomes inactive without mutation",()=>{var a=new Fake();var c=new DesktopLayerController(a);var r=Attach(a,c);a.Own=null;a.Calls.Clear();var check=c.ValidateAndRecover(r.Lease);Assert(!check.Success && !r.Lease.IsActive && check.Cleanup=="original-window-no-longer-owned" && a.Calls.Count==0,"destroyed child");});
        Test("detach restores original bounds styles and remains hidden",()=>{var a=new Fake();var original=a.Own.Copy();var c=new DesktopLayerController(a);var r=Attach(a,c);a.Own.Visible=true;var end=c.Detach(r.Lease);Assert(end.Success && !r.Lease.IsActive && !a.Own.Visible && a.Own.Parent==0 && a.Own.Style==original.Style && a.Own.Bounds.Same(original.Bounds),"detach");});
        Test("cleanup failures remain diagnostic and never claim success",()=>{var a=new Fake();var c=new DesktopLayerController(a);var r=Attach(a,c);a.Own.Visible=true;a.Fail["hide"]=2;a.Fail["close"]=1;var end=c.Detach(r.Lease);Assert(!end.Success && end.Cleanup=="cleanup-failed-visible" && end.Issues.Count>=3,"false cleanup success");});
        Test("geometry changes invalidate lease",()=>{var a=new Fake();var c=new DesktopLayerController(a);var r=Attach(a,c);a.Own.Bounds=new DesktopRect(0,0,300,200);var check=c.ValidateAndRecover(r.Lease);Assert(!check.Success && check.Reason=="attached-window-changed" && check.Cleanup=="restored-hidden","bounds change");});
        Test("stale lease cannot detach or recover a newer attachment",()=>{var a=new Fake();var c=new DesktopLayerController(a);var old=Attach(a,c);Assert(c.Detach(old.Lease).Success,"old detach");var current=Attach(a,c);Assert(current.Success,"new attach");a.Calls.Clear();Assert(!c.Detach(old.Lease).Success,"old detach accepted");Assert(!c.ValidateAndRecover(old.Lease).Success,"old recovery accepted");Assert(a.Calls.Count==0 && a.Own.Parent==4 && current.Lease.IsActive && c.ValidateAndRecover(current.Lease).Success,"new lease disturbed");});
        Test("failed cleanup retains current inactive lease for retry",()=>{var a=new Fake();var c=new DesktopLayerController(a);var r=Attach(a,c);a.Own.Visible=true;a.Fail["hide"]=2;a.Fail["close"]=1;Assert(!c.Detach(r.Lease).Success,"cleanup failure");Assert(c.Detach(r.Lease).Success && !a.Own.Visible && a.Own.Parent==0,"inactive lease retry");});
    }
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
    [DllImport("user32.dll")]static extern IntPtr GetDesktopWindow();
    static void NativeHiddenBoundary()
    {
        // Explicit optional test: creates only this process's hidden window.
        // No CaptureDesktop(), Explorer query, or SetParent() is performed.
        Test("native own hidden window parent normalization positioning and close",()=>{
            IntPtr foreground=GetForegroundWindow();
            using(var form=new Form()){
                form.ShowInTaskbar=false;form.FormBorderStyle=FormBorderStyle.None;
                form.StartPosition=FormStartPosition.Manual;form.SetBounds(20,30,160,90);
                long handle=form.Handle.ToInt64();var api=new Win32DesktopApi();var state=api.ReadWindow(handle);
                bool rawParentIsDesktop=GetAncestor(form.Handle,1)==GetDesktopWindow();
                Assert(state!=null && state.Parent==0 && !state.Visible,"hidden top level parent");
                var position=api.Position(handle,0,new DesktopRect(30,40,190,130),DesktopLayerController.SafePositionFlags);
                Assert(position.Success,"position "+position.ErrorCode);
                state=api.ReadWindow(handle);Assert(state!=null && !state.Visible && state.Parent==0 && state.Bounds.Same(new DesktopRect(30,40,190,130)),"position verification");
                Assert(api.Close(handle).Success,"own close");
                Assert(GetForegroundWindow()==foreground,"foreground changed during boundary test");
                Console.WriteLine("NATIVE rawParentIsDesktop="+rawParentIsDesktop+" normalizedParent=0 visible=False bounds=30,40,190,130 positionSuccess=True closeSuccess=True foregroundUnchanged=True");
            }
        });
    }
    [STAThread]static int Main(string[] args){if(args.Length==1 && args[0]=="--native-hidden-boundary")NativeHiddenBoundary();else FixedTests();Console.WriteLine("RESULT "+passed+" passed / "+failed+" failed");return failed==0?0:1;}
}
