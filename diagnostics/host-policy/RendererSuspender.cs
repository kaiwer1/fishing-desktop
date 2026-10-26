using System;
using System.Threading;
using System.Threading.Tasks;

namespace FishingDesktop.Diagnostics.HostPolicy
{
    public interface IRendererBackend
    {
        bool IsAlive {get;}
        bool IsSuspended {get;}
        Task PreparePauseAsync();
        void SetVisible(bool visible);
        Task<bool> TrySuspendAsync();
        void Resume();
    }
    // One serialized asynchronous pipeline per Core, owned by its UI thread.
    // A late successful suspension must not override a newer resume request.
    public sealed class RendererSuspender : IDisposable
    {
        readonly IRendererBackend backend;
        readonly int thread=Thread.CurrentThread.ManagedThreadId;
        bool desiredRun=true,pumping,disposed,failed;
        long revision;
        Task completion=Task.FromResult(true);
        public string Outcome {get;private set;}
        public string Error {get;private set;}
        public bool Pending {get{return pumping;}}
        public RendererSuspender(IRendererBackend backend)
        {
            if(backend==null)throw new ArgumentNullException("backend");this.backend=backend;Outcome="new";
        }
        void Check(){if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("Renderer suspension requires its owner thread.");}
        bool Alive(){return !disposed && !failed && backend.IsAlive;}
        void ResumeAndShow(){backend.Resume();backend.SetVisible(true);}
        void RecordFailure(Exception error)
        {
            failed=true;Error=error.GetType().Name;Outcome="failed";
            try{if(backend.IsAlive && !desiredRun)backend.SetVisible(false);}catch(Exception){}
        }
        public Task RequestRun(bool run,bool force=false)
        {
            Check();if(disposed || failed)return Task.FromResult(true);
            bool stable=Outcome=="active" || Outcome=="suspended" || Outcome=="hidden-not-suspended";
            if(!force && run==desiredRun && (pumping || stable))return completion;
            desiredRun=run;revision=checked(revision+1);Error=null;
            // Resume immediately while an old TrySuspendAsync is still pending.
            // The pipeline repeats Resume after that old attempt completes.
            if(run && pumping && Alive()){
                try{ResumeAndShow();}catch(Exception error){RecordFailure(error);return Task.FromResult(true);}
            }
            if(!pumping){pumping=true;completion=Reconcile();}
            return completion;
        }
        async Task Reconcile()
        {
            try{
                while(Alive()){
                    Check();long current=revision;
                    if(desiredRun){ResumeAndShow();Outcome="active";return;}
                    if(backend.IsSuspended){backend.SetVisible(false);Outcome="suspended";return;}
                    Outcome="preparing";
                    try{await backend.PreparePauseAsync();}
                    catch(Exception){if(!Alive())return;if(current!=revision)continue;throw;}
                    Check();if(!Alive())return;if(current!=revision)continue;
                    backend.SetVisible(false);Outcome="suspending";
                    bool suspended;
                    try{suspended=await backend.TrySuspendAsync();}
                    catch(Exception){if(!Alive())return;if(current!=revision)continue;throw;}
                    Check();if(!Alive())return;
                    if(current!=revision)continue;
                    Outcome=suspended && backend.IsSuspended?"suspended":"hidden-not-suspended";
                    return;
                }
            }catch(Exception error){
                if(!disposed)RecordFailure(error);
            }finally{pumping=false;if(disposed || !backend.IsAlive)Outcome="retired";}
        }
        public void Dispose(){Check();disposed=true;revision=checked(revision+1);Outcome="retired";}
    }
}
