using System;
using System.Collections.Generic;
using System.IO;

namespace FishingDesktop.Diagnostics.DesktopLayer
{
    public struct DesktopRect
    {
        public int Left, Top, Right, Bottom;
        public DesktopRect(int left,int top,int right,int bottom){Left=left;Top=top;Right=right;Bottom=bottom;}
        public bool IsValid {get{return (long)Right-Left>0 && (long)Right-Left<=Int32.MaxValue && (long)Bottom-Top>0 && (long)Bottom-Top<=Int32.MaxValue;}}
        public bool Contains(DesktopRect other){return IsValid && other.IsValid && Left<=other.Left && Top<=other.Top && Right>=other.Right && Bottom>=other.Bottom;}
        public bool Same(DesktopRect other){return Left==other.Left && Top==other.Top && Right==other.Right && Bottom==other.Bottom;}
    }
    public sealed class ProcessIdentity
    {
        public int Id;
        public long StartTimeUtcTicks;
        public string Path;
        public bool Same(ProcessIdentity other){return other!=null && Id==other.Id && StartTimeUtcTicks==other.StartTimeUtcTicks && String.Equals(Path,other.Path,StringComparison.OrdinalIgnoreCase);}
    }
    public sealed class WindowState
    {
        public long Handle, Parent, DpiContext, Style, ExtendedStyle;
        public int ProcessId, ThreadId, ForeignDescendantCount;
        public string ClassName;
        public bool Visible;
        public DesktopRect Bounds;
        public WindowState Copy(){return (WindowState)MemberwiseClone();}
    }
    public sealed class DesktopSnapshot
    {
        public bool Stable;
        public long ShellWindow, ThreadDpiContext;
        public ProcessIdentity ShellProcess;
        public DesktopRect VirtualBounds;
        // Both lists contain shell-owned windows only. TopLevels is in Z order.
        public List<WindowState> TopLevels=new List<WindowState>();
        public List<WindowState> Descendants=new List<WindowState>();
    }
    public sealed class NativeResult
    {
        public bool Success;
        public int ErrorCode;
        public long PreviousValue;
        public static NativeResult Ok(){return new NativeResult{Success=true};}
        public static NativeResult Error(int code){return new NativeResult{Success=false,ErrorCode=code};}
        // SetParent and SetWindowLong may legitimately return a previous value
        // of NULL/zero. The native adapter clears last-error BEFORE the call.
        public static NativeResult FromPreviousValue(long value,int error){return new NativeResult{Success=value!=0 || error==0,PreviousValue=value,ErrorCode=error};}
    }
    public interface IDesktopLayerApi
    {
        int CurrentProcessId {get;}
        int CurrentThreadId {get;}
        string ExpectedExplorerPath {get;}
        DesktopSnapshot CaptureDesktop();
        WindowState ReadWindow(long handle);
        bool DpiContextsEqual(long left,long right);
        NativeResult SetStyle(long ownedWindow,long style);
        NativeResult SetExtendedStyle(long ownedWindow,long style);
        NativeResult SetParent(long ownedWindow,long parent);
        NativeResult Position(long ownedWindow,long parent,DesktopRect screenBounds,uint flags);
        NativeResult Hide(long ownedWindow);
        NativeResult Close(long ownedWindow);
    }
    public sealed class DiagnosticIssue
    {
        public string Stage;
        public int NativeError;
        public DiagnosticIssue(string stage,int error){Stage=stage;NativeError=error;}
    }
    public sealed class DesktopCandidate
    {
        internal readonly ProcessIdentity Shell;
        internal readonly WindowState Worker, Icons, DefView;
        internal readonly long ShellWindow, ThreadDpi;
        internal readonly DesktopRect RequestedBounds, VirtualBounds;
        internal DesktopCandidate(DesktopSnapshot snapshot,WindowState worker,WindowState icons,WindowState defView,DesktopRect requested){
            Shell=new ProcessIdentity{Id=snapshot.ShellProcess.Id,StartTimeUtcTicks=snapshot.ShellProcess.StartTimeUtcTicks,Path=snapshot.ShellProcess.Path};
            Worker=worker.Copy();Icons=icons.Copy();DefView=defView.Copy();ShellWindow=snapshot.ShellWindow;
            ThreadDpi=snapshot.ThreadDpiContext;RequestedBounds=requested;VirtualBounds=snapshot.VirtualBounds;
        }
        public long WorkerHandle {get{return Worker.Handle;}}
        public long IconHostHandle {get{return Icons.Handle;}}
        public long DefViewHandle {get{return DefView.Handle;}}
        public DesktopRect TargetBounds {get{return RequestedBounds;}}
    }
    public sealed class DiscoveryResult
    {
        public DesktopCandidate Candidate;
        public string Reason;
        public int NativeError;
        public bool Available {get{return Candidate!=null;}}
    }
    public sealed class AttachmentLease
    {
        internal readonly DesktopCandidate Candidate;
        internal readonly WindowState Original;
        internal bool Active;
        internal AttachmentLease(DesktopCandidate candidate,WindowState original){Candidate=candidate;Original=original.Copy();Active=true;}
        public long WindowHandle {get{return Original.Handle;}}
        public bool IsActive {get{return Active;}}
    }
    public sealed class AttachmentResult
    {
        public bool Success;
        public string Reason, Cleanup;
        public AttachmentLease Lease;
        public List<DiagnosticIssue> Issues=new List<DiagnosticIssue>();
    }
    public sealed class DesktopLayerController
    {
        public const long ChildStyle=0x40000000L, PopupStyle=0x80000000L, NoActivateStyle=0x08000000L;
        public const uint SafePositionFlags=0x0004|0x0010|0x0020|0x0200; // NOZORDER, NOACTIVATE, FRAMECHANGED, NOOWNERZORDER
        const long NoRedirectionBitmap=0x00200000L, Layered=0x00080000L;
        readonly IDesktopLayerApi api;
        readonly int threadId;
        readonly Dictionary<long,AttachmentLease> currentLeases=new Dictionary<long,AttachmentLease>();
        public DesktopLayerController(IDesktopLayerApi api){if(api==null)throw new ArgumentNullException("api");this.api=api;threadId=api.CurrentThreadId;}
        bool OnThread(){return api.CurrentThreadId==threadId;}
        bool Current(AttachmentLease lease){AttachmentLease current;return lease!=null && currentLeases.TryGetValue(lease.WindowHandle,out current) && Object.ReferenceEquals(current,lease);}
        void ReleaseRecovered(AttachmentLease lease,AttachmentResult result){if(Current(lease) && (result.Cleanup=="restored-hidden" || result.Cleanup=="own-window-closed" || result.Cleanup=="original-window-no-longer-owned"))currentLeases.Remove(lease.WindowHandle);}
        static DiscoveryResult Unavailable(string reason,int error){return new DiscoveryResult{Reason=reason,NativeError=error};}
        static bool SameWindow(WindowState first,WindowState second){return first!=null && second!=null && first.Handle==second.Handle && first.Parent==second.Parent && first.ProcessId==second.ProcessId && first.ThreadId==second.ThreadId && first.ClassName==second.ClassName && first.Bounds.Same(second.Bounds);}
        public DiscoveryResult Discover(DesktopRect requested)
        {
            if(!OnThread())return Unavailable("wrong-owner-thread",0);
            if(!requested.IsValid)return Unavailable("invalid-target-bounds",0);
            try{
                var snapshot=api.CaptureDesktop();
                if(snapshot==null || !snapshot.Stable || snapshot.ShellWindow==0 || snapshot.ShellProcess==null)return Unavailable("shell-identity-unavailable",0);
                var shell=snapshot.ShellProcess;
                if(shell.Id<=0 || shell.StartTimeUtcTicks<=0 || String.IsNullOrEmpty(shell.Path) ||
                    !String.Equals(Path.GetFullPath(shell.Path),Path.GetFullPath(api.ExpectedExplorerPath),StringComparison.OrdinalIgnoreCase))return Unavailable("unexpected-shell-process",0);
                if(!snapshot.VirtualBounds.Contains(requested))return Unavailable("target-outside-virtual-desktop",0);
                WindowState shellWindow=null,iconHost=null,defView=null;int iconIndex=-1,iconCount=0;
                var handles=new HashSet<long>();
                for(int i=0;i<snapshot.TopLevels.Count;i++){
                    var top=snapshot.TopLevels[i];
                    if(top==null || top.Handle==0 || !handles.Add(top.Handle) || top.ProcessId!=shell.Id || top.Parent!=0)return Unavailable("invalid-shell-window-ancestry",0);
                    if(top.Handle==snapshot.ShellWindow)shellWindow=top;
                    foreach(var child in snapshot.Descendants){
                        if(child.Parent!=top.Handle || child.ClassName!="SHELLDLL_DefView")continue;
                        if(child.ProcessId!=shell.Id || child.Handle==0)return Unavailable("invalid-defview-ancestry",0);
                        iconCount++;iconHost=top;defView=child;iconIndex=i;
                    }
                }
                if(shellWindow==null || shellWindow.ClassName!="Progman")return Unavailable("unsupported-shell-window",0);
                if(iconCount!=1 || (iconHost.ClassName!="Progman" && iconHost.ClassName!="WorkerW"))return Unavailable("icon-host-unavailable-or-ambiguous",0);
                if((shellWindow.ExtendedStyle&NoRedirectionBitmap)!=0 || (defView.ExtendedStyle&Layered)!=0)return Unavailable("raised-desktop-not-supported",0);
                if(!iconHost.Visible || !defView.Visible || !iconHost.Bounds.Contains(requested) || !defView.Bounds.Contains(requested))return Unavailable("icon-host-geometry-unavailable",0);
                WindowState worker=null;
                for(int i=iconIndex+1;i<snapshot.TopLevels.Count;i++){if(snapshot.TopLevels[i].ClassName=="WorkerW"){worker=snapshot.TopLevels[i];break;}}
                if(worker==null)return Unavailable("existing-workerw-unavailable",0);
                foreach(var child in snapshot.Descendants){if(child.Parent==worker.Handle && child.ClassName=="SHELLDLL_DefView")return Unavailable("workerw-contains-icons",0);}
                if(!worker.Visible || !worker.Bounds.Contains(requested))return Unavailable("workerw-geometry-unavailable",0);
                if(worker.ForeignDescendantCount!=0)return Unavailable("workerw-already-occupied",0);
                if(snapshot.ThreadDpiContext==0 || worker.DpiContext==0 || iconHost.DpiContext==0 || defView.DpiContext==0 ||
                    !api.DpiContextsEqual(snapshot.ThreadDpiContext,worker.DpiContext) || !api.DpiContextsEqual(worker.DpiContext,iconHost.DpiContext) ||
                    !api.DpiContextsEqual(worker.DpiContext,defView.DpiContext))return Unavailable("dpi-context-incompatible",0);
                return new DiscoveryResult{Candidate=new DesktopCandidate(snapshot,worker,iconHost,defView,requested)};
            }catch(System.ComponentModel.Win32Exception ex){return Unavailable("desktop-query-failed",ex.NativeErrorCode);}
            catch(Exception){return Unavailable("desktop-query-failed",0);}
        }
        bool Revalidate(DesktopCandidate candidate,out string reason)
        {
            try{
            var current=Discover(candidate.RequestedBounds);
            if(!current.Available){reason=current.Reason;return false;}
            var now=current.Candidate;
            bool matches=candidate.Shell.Same(now.Shell) && candidate.ShellWindow==now.ShellWindow && candidate.VirtualBounds.Same(now.VirtualBounds) &&
                SameWindow(candidate.Worker,now.Worker) && SameWindow(candidate.Icons,now.Icons) && SameWindow(candidate.DefView,now.DefView) &&
                api.DpiContextsEqual(candidate.Worker.DpiContext,now.Worker.DpiContext) && api.DpiContextsEqual(candidate.ThreadDpi,now.ThreadDpi);
            reason=matches?null:"desktop-parent-identity-changed";return matches;
            }catch(Exception){reason="desktop-revalidation-failed";return false;}
        }
        bool Owned(WindowState state){return state!=null && state.Handle!=0 && state.ProcessId==api.CurrentProcessId && state.ThreadId==threadId;}
        bool SameOwned(WindowState original,WindowState now){return Owned(now) && now.Handle==original.Handle && now.ClassName==original.ClassName;}
        NativeResult Call(string stage,Func<NativeResult> action,AttachmentResult result)
        {
            NativeResult call;
            try{call=action();if(call==null)call=NativeResult.Error(0);}
            catch(System.ComponentModel.Win32Exception ex){call=NativeResult.Error(ex.NativeErrorCode);}
            catch(Exception){call=NativeResult.Error(0);}
            if(!call.Success)result.Issues.Add(new DiagnosticIssue(stage,call.ErrorCode));
            return call;
        }
        WindowState ReadOwn(WindowState original){try{var current=api.ReadWindow(original.Handle);return SameOwned(original,current)?current:null;}catch(Exception){return null;}}
        void Cleanup(WindowState original,AttachmentResult result)
        {
            if(ReadOwn(original)==null){result.Cleanup="original-window-no-longer-owned";return;}
            bool restored=Call("rollback-hide",()=>api.Hide(original.Handle),result).Success;
            // Never detach a visible child to the desktop. A failed hide must
            // close this process's own window before any parent/style rollback.
            var hidden=ReadOwn(original);
            if(hidden==null){result.Cleanup="original-window-no-longer-owned";return;}
            if(!restored || hidden.Visible){CloseOrHide(original,result);return;}
            restored=Call("rollback-parent",()=>api.SetParent(original.Handle,0),result).Success && restored;
            restored=Call("rollback-style",()=>api.SetStyle(original.Handle,original.Style),result).Success && restored;
            restored=Call("rollback-extended-style",()=>api.SetExtendedStyle(original.Handle,original.ExtendedStyle),result).Success && restored;
            restored=Call("rollback-position",()=>api.Position(original.Handle,0,original.Bounds,SafePositionFlags),result).Success && restored;
            var current=ReadOwn(original);
            restored=restored && current!=null && !current.Visible && current.Parent==0 && current.Style==original.Style && current.ExtendedStyle==original.ExtendedStyle && current.Bounds.Same(original.Bounds);
            if(restored){result.Cleanup="restored-hidden";return;}
            result.Issues.Add(new DiagnosticIssue("rollback-verification-failed",0));
            CloseOrHide(original,result);
        }
        void CloseOrHide(WindowState original,AttachmentResult result)
        {
            if(ReadOwn(original)==null){result.Cleanup="original-window-no-longer-owned";return;}
            bool closed=Call("close-own-window",()=>api.Close(original.Handle),result).Success;
            if(closed && ReadOwn(original)==null){result.Cleanup="own-window-closed";return;}
            Call("final-hide",()=>api.Hide(original.Handle),result);
            var current=ReadOwn(original);
            result.Cleanup=current==null?"original-window-no-longer-owned":current.Visible?"cleanup-failed-visible":"cleanup-failed-hidden";
        }
        public AttachmentResult Attach(DesktopCandidate candidate,long ownedWindow)
        {
            var result=new AttachmentResult{Reason="invalid-attachment-request",Cleanup="not-needed"};
            if(!OnThread()){result.Reason="wrong-owner-thread";return result;}
            if(candidate==null)return result;
            if(currentLeases.ContainsKey(ownedWindow)){result.Reason="window-already-leased-or-cleanup-pending";return result;}
            WindowState original;
            try{original=api.ReadWindow(ownedWindow);}catch(Exception){result.Reason="owned-window-query-failed";return result;}
            if(!Owned(original)){result.Reason="window-not-owned-by-current-ui-thread";return result;}
            if(original.Parent!=0){result.Reason="owned-window-must-start-top-level";return result;}
            if(original.Visible){result.Reason="owned-window-must-start-hidden";return result;}
            if(!original.Bounds.IsValid){result.Reason="owned-window-bounds-invalid";return result;}
            string reason;
            if(!Revalidate(candidate,out reason)){result.Reason=reason;return result;}
            bool complete=false;
            try{
                if(original.DpiContext==0 || !api.DpiContextsEqual(original.DpiContext,candidate.Worker.DpiContext)){result.Reason="owned-window-dpi-incompatible";return result;}
                if(!Call("set-child-style",()=>api.SetStyle(ownedWindow,(original.Style|ChildStyle)&~PopupStyle),result).Success)return result;
                if(!Call("set-noactivate-style",()=>api.SetExtendedStyle(ownedWindow,original.ExtendedStyle|NoActivateStyle),result).Success)return result;
                if(!Revalidate(candidate,out reason)){result.Reason=reason;return result;}
                if(!Call("set-parent",()=>api.SetParent(ownedWindow,candidate.Worker.Handle),result).Success)return result;
                if(!Revalidate(candidate,out reason)){result.Reason=reason;return result;}
                if(!Call("set-child-bounds",()=>api.Position(ownedWindow,candidate.Worker.Handle,candidate.RequestedBounds,SafePositionFlags),result).Success)return result;
                var current=ReadOwn(original);
                if(current==null || current.Visible || current.Parent!=candidate.Worker.Handle || (current.Style&ChildStyle)==0 || (current.Style&PopupStyle)!=0 ||
                    (current.ExtendedStyle&NoActivateStyle)==0 || !current.Bounds.Same(candidate.RequestedBounds)){
                    result.Reason="attachment-verification-failed";return result;
                }
                if(!Revalidate(candidate,out reason)){result.Reason=reason;return result;}
                result.Success=true;result.Reason=null;result.Lease=new AttachmentLease(candidate,original);currentLeases[ownedWindow]=result.Lease;complete=true;return result;
            }catch(System.ComponentModel.Win32Exception ex){result.Issues.Add(new DiagnosticIssue("attachment-exception",ex.NativeErrorCode));result.Reason="attachment-exception";return result;}
            catch(Exception){result.Issues.Add(new DiagnosticIssue("attachment-exception",0));result.Reason="attachment-exception";return result;}
            finally{if(!complete){if(result.Reason=="invalid-attachment-request")result.Reason="native-attachment-failed";Cleanup(original,result);}}
        }
        public bool Validate(AttachmentLease lease,out string reason)
        {
            reason="inactive-attachment";
            if(!OnThread()){reason="wrong-owner-thread";return false;}
            if(!Current(lease)){reason="stale-attachment-lease";return false;}
            if(lease==null || !lease.Active)return false;
            if(!Revalidate(lease.Candidate,out reason)){lease.Active=false;return false;}
            var current=ReadOwn(lease.Original);
            if(current==null || current.Parent!=lease.Candidate.Worker.Handle || (current.Style&ChildStyle)==0 || (current.Style&PopupStyle)!=0 ||
                (current.ExtendedStyle&NoActivateStyle)==0 || !current.Bounds.Same(lease.Candidate.RequestedBounds)){
                lease.Active=false;reason="attached-window-changed";return false;
            }
            reason=null;return true;
        }
        public AttachmentResult Detach(AttachmentLease lease)
        {
            var result=new AttachmentResult{Reason="inactive-attachment",Cleanup="not-needed"};
            if(!OnThread()){result.Reason="wrong-owner-thread";return result;}
            if(!Current(lease)){result.Reason="stale-attachment-lease";return result;}
            lease.Active=false;Cleanup(lease.Original,result);
            ReleaseRecovered(lease,result);
            result.Success=result.Cleanup=="restored-hidden" || result.Cleanup=="own-window-closed" || result.Cleanup=="original-window-no-longer-owned";
            result.Reason=result.Success?null:"detach-cleanup-failed";return result;
        }
        // Use on the owner's UI-thread heartbeat and shell/display-change
        // notifications. Invalidity preserves the cause while failing closed.
        public AttachmentResult ValidateAndRecover(AttachmentLease lease)
        {
            string reason;
            if(Validate(lease,out reason))return new AttachmentResult{Success=true,Lease=lease,Cleanup="not-needed"};
            var result=new AttachmentResult{Reason=reason,Cleanup="not-needed",Lease=lease};
            if(!OnThread() || !Current(lease))return result;
            lease.Active=false;Cleanup(lease.Original,result);ReleaseRecovered(lease,result);return result;
        }
    }
}
