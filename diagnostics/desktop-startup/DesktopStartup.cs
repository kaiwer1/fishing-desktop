using System;
using System.Collections.Generic;
using FishingDesktop.Diagnostics.DesktopLayer;

namespace FishingDesktop.Diagnostics.DesktopStartup
{
    public sealed class ShellLayerIntent
    {
        internal readonly ProcessIdentity Shell;
        internal readonly long ShellWindow,IconHost,DefView,Dpi;
        internal readonly DesktopRect Target,Virtual;
        internal ShellLayerIntent(DesktopSnapshot snapshot,WindowState icons,WindowState view,DesktopRect target){
            Shell=new ProcessIdentity{Id=snapshot.ShellProcess.Id,StartTimeUtcTicks=snapshot.ShellProcess.StartTimeUtcTicks,Path=snapshot.ShellProcess.Path};
            ShellWindow=snapshot.ShellWindow;IconHost=icons.Handle;DefView=view.Handle;Dpi=snapshot.ThreadDpiContext;Target=target;Virtual=snapshot.VirtualBounds;
        }
        internal bool Matches(ShellLayerIntent other){return other!=null && Shell.Same(other.Shell) && ShellWindow==other.ShellWindow &&
            IconHost==other.IconHost && DefView==other.DefView && Dpi==other.Dpi && Target.Same(other.Target) && Virtual.Same(other.Virtual);}
    }
    public interface IShellLayerRequest {NativeResult Request(ShellLayerIntent intent);}
    public sealed class StartupResult
    {
        public DesktopCandidate Candidate;
        public bool Available {get{return Candidate!=null;}}
        public bool ReusedExisting,RequestAttempted;
        public string Reason;
        public int NativeError;
    }
    // Optional preparation is separate from the existing attach-only controller.
    // It never hides/destroys Explorer windows and never attaches a caller window.
    public sealed class DesktopStartupCoordinator
    {
        readonly IDesktopLayerApi api;
        readonly IShellLayerRequest request;
        readonly DesktopLayerController controller;
        readonly int thread;
        bool requested;
        public DesktopStartupCoordinator(IDesktopLayerApi api,IShellLayerRequest request){
            if(api==null || request==null)throw new ArgumentNullException();
            this.api=api;this.request=request;controller=new DesktopLayerController(api);thread=api.CurrentThreadId;
        }
        static StartupResult Failure(string reason,bool attempted,int native=0){return new StartupResult{Reason=reason,RequestAttempted=attempted,NativeError=native};}
        internal static ShellLayerIntent MissingIntent(IDesktopLayerApi api,DesktopSnapshot snapshot,DesktopRect target)
        {
            if(snapshot==null || !snapshot.Stable || snapshot.ShellProcess==null || snapshot.ShellProcess.Id<=0 || snapshot.ShellProcess.Id==api.CurrentProcessId || snapshot.ShellProcess.StartTimeUtcTicks<=0 ||
                !String.Equals(snapshot.ShellProcess.Path,api.ExpectedExplorerPath,StringComparison.OrdinalIgnoreCase) || !target.IsValid || !snapshot.VirtualBounds.Contains(target) || snapshot.ThreadDpiContext==0)return null;
            WindowState shell=null,icons=null,view=null;int count=0,iconIndex=-1;var handles=new HashSet<long>();
            for(int index=0;index<snapshot.TopLevels.Count;index++){
                var top=snapshot.TopLevels[index];
                if(top==null || top.Handle==0 || !handles.Add(top.Handle) || top.Parent!=0 || top.ProcessId!=snapshot.ShellProcess.Id || top.ThreadId<=0)return null;
                if(top.Handle==snapshot.ShellWindow)shell=top;
                if(top.ClassName=="WorkerW" && top.ForeignDescendantCount>0)return null;
                foreach(var child in snapshot.Descendants)if(child!=null && child.Parent==top.Handle && child.ClassName=="SHELLDLL_DefView"){
                    count++;icons=top;view=child;iconIndex=index;
                }
            }
            if(shell==null || shell.ClassName!="Progman" || (shell.ExtendedStyle&0x00200000L)!=0 || count!=1 || view.Handle==0 || view.ProcessId!=snapshot.ShellProcess.Id || view.ThreadId<=0 ||
                (icons.ClassName!="Progman" && icons.ClassName!="WorkerW") || !icons.Visible || !view.Visible || (view.ExtendedStyle&0x00080000L)!=0 ||
                !icons.Bounds.Contains(target) || !view.Bounds.Contains(target) || icons.DpiContext==0 || view.DpiContext==0 ||
                !api.DpiContextsEqual(snapshot.ThreadDpiContext,icons.DpiContext) || !api.DpiContextsEqual(icons.DpiContext,view.DpiContext))return null;
            for(int index=iconIndex+1;index<snapshot.TopLevels.Count;index++)if(snapshot.TopLevels[index].ClassName=="WorkerW")return null;
            return new ShellLayerIntent(snapshot,icons,view,target);
        }
        public StartupResult Prepare(DesktopRect target,bool allowMissingLayerRequest)
        {
            if(api.CurrentThreadId!=thread)return Failure("wrong-thread",false);
            bool attempted=false;
            try{
                var existing=controller.Discover(target);
                if(existing.Available)return new StartupResult{Candidate=existing.Candidate,ReusedExisting=true};
                if(existing.Reason!="existing-workerw-unavailable" || !allowMissingLayerRequest)return Failure(existing.Reason,false,existing.NativeError);
                if(requested)return Failure("request-already-attempted",false);
                var intent=MissingIntent(api,api.CaptureDesktop(),target);if(intent==null)return Failure("missing-layer-preconditions-failed",false);
                // A layer may appear between discovery and preparation. Reuse it;
                // never issue a request merely because an earlier query lacked it.
                existing=controller.Discover(target);
                if(existing.Available)return new StartupResult{Candidate=existing.Candidate,ReusedExisting=true};
                if(existing.Reason!="existing-workerw-unavailable")return Failure(existing.Reason,false,existing.NativeError);
                var current=MissingIntent(api,api.CaptureDesktop(),target);
                if(!intent.Matches(current))return Failure("shell-changed-before-request",false);
                requested=true;attempted=true;var sent=request.Request(intent);
                if(sent==null || !sent.Success)return Failure("shell-request-failed-or-timed-out",true,sent==null?0:sent.ErrorCode);
                existing=controller.Discover(target);
                if(!existing.Available)return Failure("layer-unavailable-after-request:"+existing.Reason,true,existing.NativeError);
                var after=api.CaptureDesktop();
                if(!after.Stable || !intent.Shell.Same(after.ShellProcess) || after.ShellWindow!=intent.ShellWindow || !after.VirtualBounds.Same(intent.Virtual))return Failure("shell-changed-after-request",true);
                return new StartupResult{Candidate=existing.Candidate,RequestAttempted=true};
            }catch(Exception){return Failure("startup-query-or-request-failed",attempted);}
        }
    }
}
