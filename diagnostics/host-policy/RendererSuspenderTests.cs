using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishingDesktop.Diagnostics.HostPolicy;

static class RendererSuspenderTests
{
    sealed class Pump : SynchronizationContext
    {
        readonly Queue<Action> queue=new Queue<Action>();
        public override void Post(SendOrPostCallback callback,object state){lock(queue)queue.Enqueue(()=>callback(state));}
        public void Drain(){while(true){Action next;lock(queue){if(queue.Count==0)return;next=queue.Dequeue();}next();}}
    }
    sealed class Backend : IRendererBackend
    {
        public bool Alive=true,Suspended,Visible=true,Refuse,PrepareFails,ResumeFails;
        public int Prepares,Attempts,Resumes,Mutations;
        public TaskCompletionSource<bool> PreparePending,SuspendPending;
        public bool IsAlive {get{return Alive;}}public bool IsSuspended {get{return Suspended;}}
        public Task PreparePauseAsync(){Prepares++;if(PrepareFails)throw new InvalidOperationException();return PreparePending==null?Task.FromResult(true):PreparePending.Task;}
        public void SetVisible(bool value){Visible=value;Mutations++;}
        public async Task<bool> TrySuspendAsync(){Attempts++;bool accepted=SuspendPending==null?!Refuse:await SuspendPending.Task;Suspended=accepted;return accepted;}
        public void Resume(){Resumes++;if(ResumeFails)throw new InvalidOperationException();Suspended=false;Mutations++;}
    }
    static int passed,failed;
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    static void Test(string name,Action<Pump> test)
    {
        var previous=SynchronizationContext.Current;var pump=new Pump();SynchronizationContext.SetSynchronizationContext(pump);
        try{test(pump);pump.Drain();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}finally{SynchronizationContext.SetSynchronizationContext(previous);}
    }
    static int Main()
    {
        Test("pause prepares then hides and suspends while resume restores visibility",p=>{var b=new Backend();var c=new RendererSuspender(b);c.RequestRun(false);Need(c.Outcome=="suspended" && !b.Visible && b.Suspended,"Pause failed.");c.RequestRun(true);Need(c.Outcome=="active" && b.Visible && !b.Suspended,"Resume failed.");});
        Test("repeated paused requests do not schedule repeated native suspensions",p=>{var b=new Backend();var c=new RendererSuspender(b);c.RequestRun(false);c.RequestRun(false);Need(b.Prepares==1 && b.Attempts==1,"Duplicate work.");});
        Test("late preparation cannot hide a newly resumed view",p=>{var b=new Backend{PreparePending=new TaskCompletionSource<bool>()};var c=new RendererSuspender(b);c.RequestRun(false);c.RequestRun(true);b.PreparePending.SetResult(true);p.Drain();Need(c.Outcome=="active" && b.Visible && b.Attempts==0,"Old preparation hid current view.");});
        Test("late successful suspension is followed by resume for the current request",p=>{var b=new Backend{SuspendPending=new TaskCompletionSource<bool>()};var c=new RendererSuspender(b);c.RequestRun(false);c.RequestRun(true);b.SuspendPending.SetResult(true);p.Drain();Need(c.Outcome=="active" && !b.Suspended && b.Visible && b.Resumes>=2,"Late suspend won.");});
        Test("pause resume pause while native work is pending ends suspended",p=>{var b=new Backend{SuspendPending=new TaskCompletionSource<bool>()};var c=new RendererSuspender(b);c.RequestRun(false);c.RequestRun(true);c.RequestRun(false);b.SuspendPending.SetResult(true);p.Drain();Need(c.Outcome=="suspended" && b.Suspended && !b.Visible && b.Attempts==1,"Latest pause lost.");});
        Test("disposal while native work is pending never resumes or shows the retired view",p=>{var b=new Backend{SuspendPending=new TaskCompletionSource<bool>()};var c=new RendererSuspender(b);c.RequestRun(false);c.Dispose();int mutations=b.Mutations;b.SuspendPending.SetResult(true);p.Drain();Need(c.Outcome=="retired" && b.Mutations==mutations,"Retired view mutated.");});
        Test("dead core after prepare is never hidden or suspended",p=>{var b=new Backend{PreparePending=new TaskCompletionSource<bool>()};var c=new RendererSuspender(b);c.RequestRun(false);b.Alive=false;b.PreparePending.SetResult(true);p.Drain();Need(c.Outcome=="retired" && b.Mutations==0 && b.Attempts==0,"Dead core touched.");});
        Test("refused suspension remains a visible-policy-safe hidden fallback",p=>{var b=new Backend{Refuse=true};var c=new RendererSuspender(b);c.RequestRun(false);Need(c.Outcome=="hidden-not-suspended" && !b.Visible && !b.Suspended && c.Error==null,"Refusal reported as success.");c.RequestRun(false);Need(b.Attempts==1,"Refusal busy loop.");});
        Test("current preparation failure is reported and hides the paused view",p=>{var b=new Backend{PrepareFails=true};var c=new RendererSuspender(b);c.RequestRun(false);Need(c.Outcome=="failed" && c.Error=="InvalidOperationException" && !b.Visible,"Failure hidden.");});
        Test("a stale preparation exception cannot fault a newer resume",p=>{var b=new Backend{PreparePending=new TaskCompletionSource<bool>()};var c=new RendererSuspender(b);c.RequestRun(false);c.RequestRun(true);b.PreparePending.SetException(new InvalidOperationException());p.Drain();Need(c.Outcome=="active" && c.Error==null && b.Visible,"Stale error faulted resume.");});
        Test("forced navigation resume invalidates old suspension without changing desired ownership",p=>{var b=new Backend{SuspendPending=new TaskCompletionSource<bool>()};var c=new RendererSuspender(b);c.RequestRun(false);c.RequestRun(true,true);b.SuspendPending.SetResult(true);p.Drain();Need(c.Outcome=="active" && b.Visible && !b.Suspended,"Navigation resume lost.");});
        Test("requests from a foreign thread are rejected before native mutations",p=>{var b=new Backend();var c=new RendererSuspender(b);bool rejected=false;var t=new Thread(()=>{try{c.RequestRun(false);}catch(InvalidOperationException){rejected=true;}});t.Start();t.Join();Need(rejected && b.Mutations==0,"Foreign request accepted.");});
        Test("failed immediate resume is observable before the old suspend completes",p=>{var b=new Backend{SuspendPending=new TaskCompletionSource<bool>()};var c=new RendererSuspender(b);c.RequestRun(false);b.ResumeFails=true;var failure=c.RequestRun(true);Need(failure.IsCompleted && c.Outcome=="failed" && c.Error=="InvalidOperationException","Immediate failure not observable.");int calls=b.Resumes;b.SuspendPending.SetResult(true);p.Drain();Need(c.Outcome=="failed" && b.Resumes==calls,"Failed coordinator resumed after old completion.");});
        Test("current resume failure is terminal until a new coordinator is created",p=>{var b=new Backend{ResumeFails=true};var c=new RendererSuspender(b);c.RequestRun(true);int calls=b.Resumes;b.ResumeFails=false;c.RequestRun(true,true);Need(c.Outcome=="failed" && b.Resumes==calls,"Terminal failure silently retried.");});
        Console.WriteLine("RESULT "+passed+" passed / "+failed+" failed");return failed==0?0:1;
    }
}
