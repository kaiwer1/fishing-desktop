using System;
using System.Threading;

namespace FishingDesktop.Diagnostics.HostPolicy
{
    [Flags]
    public enum PauseReason
    {
        None=0, Startup=1, Manual=2, NoViews=4, RendererUnavailable=8,
        EnvironmentUnknown=16, Fullscreen=32, SessionUnavailable=64,
        PowerSuspended=128, DisplayChanging=256, Portrait=512, SceneFault=1024
    }
    public sealed class RunState
    {
        public readonly PauseReason Reasons;
        public readonly long Revision,ControlEpoch;
        public bool CanRun {get{return Reasons==PauseReason.None;}}
        internal RunState(PauseReason reasons,long revision,long epoch){Reasons=reasons;Revision=revision;ControlEpoch=epoch;}
    }
    public sealed class RunTransition
    {
        public readonly RunState State;
        public readonly bool Changed,RevokeInput;
        internal RunTransition(RunState state,bool changed,bool revoke){State=state;Changed=changed;RevokeInput=revoke;}
    }
    // C# owner-thread policy only. Pages cannot assign arbitrary signals; the
    // host may close conservatively after a validated current scene fault.
    public sealed class RunGate
    {
        const PauseReason All=(PauseReason)2047;
        readonly int thread=Thread.CurrentThread.ManagedThreadId;
        PauseReason reasons=PauseReason.Startup;
        long revision,epoch;
        void Check(){if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("Wrong run-gate owner thread.");}
        public RunState Snapshot(){Check();return new RunState(reasons,revision,epoch);}
        public RunTransition Set(PauseReason reason,bool blocked)
        {
            int bits=(int)reason;
            if(bits<=0 || (bits&(bits-1))!=0)throw new ArgumentOutOfRangeException("reason");
            return Replace(reason,blocked?reason:PauseReason.None);
        }
        // Atomically replace a set of related native observations. A transition
        // from fullscreen to unavailable session must never briefly say running.
        public RunTransition Replace(PauseReason mask,PauseReason blocked)
        {
            Check();
            if(mask==PauseReason.None || (mask&~All)!=0 || (blocked&~mask)!=0)throw new ArgumentOutOfRangeException("mask");
            PauseReason next=(reasons&~mask)|blocked;
            if(next==reasons)return new RunTransition(Snapshot(),false,false);
            bool revoke=(next&~reasons)!=0;
            long nextRevision=checked(revision+1),nextEpoch=revoke?checked(epoch+1):epoch;
            reasons=next;revision=nextRevision;epoch=nextEpoch;
            return new RunTransition(Snapshot(),true,revoke);
        }
        public RunTransition CancelInput()
        {
            Check();long nextRevision=checked(revision+1),nextEpoch=checked(epoch+1);
            revision=nextRevision;epoch=nextEpoch;return new RunTransition(Snapshot(),true,true);
        }
        public bool CanActivate(long expectedRevision)
        {
            Check();return reasons==PauseReason.None && expectedRevision==revision;
        }
    }
    // Diagnostic active-time clock, not game physics or a save format. Rebase
    // both edges of a pause; cap a stalled active sample rather than catching up.
    public sealed class ActiveClock
    {
        readonly int thread=Thread.CurrentThread.ManagedThreadId;
        readonly double maxStep;
        bool initialized,previousRunning;
        double last;
        public double ActiveSeconds {get;private set;}
        public double DroppedActiveSeconds {get;private set;}
        public ActiveClock(double maxStepSeconds)
        {
            if(Double.IsNaN(maxStepSeconds) || Double.IsInfinity(maxStepSeconds) || maxStepSeconds<=0)throw new ArgumentOutOfRangeException("maxStepSeconds");
            maxStep=maxStepSeconds;
        }
        public double Advance(double monotonicSeconds,bool running)
        {
            if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("Wrong active-clock owner thread.");
            if(Double.IsNaN(monotonicSeconds) || Double.IsInfinity(monotonicSeconds) || monotonicSeconds<0 || (initialized && monotonicSeconds<last))throw new ArgumentOutOfRangeException("monotonicSeconds");
            if(!initialized){initialized=true;last=monotonicSeconds;previousRunning=running;return 0;}
            double elapsed=monotonicSeconds-last;last=monotonicSeconds;
            double step=running && previousRunning?Math.Min(maxStep,elapsed):0;
            if(running && previousRunning)DroppedActiveSeconds+=elapsed-step;
            previousRunning=running;ActiveSeconds+=step;return step;
        }
    }
}
