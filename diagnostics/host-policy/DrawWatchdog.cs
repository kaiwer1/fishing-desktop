using System;

namespace FishingDesktop.Diagnostics.HostPolicy
{
    // A current native monitor owns the scope and sample ordering. This class
    // cannot grant input, clear host pause reasons, or inspect a WebView.
    public sealed class DrawWatchdog
    {
        readonly double graceSeconds;
        bool sampled;
        double lastObserved,lastProgress;
        long lastFrames;
        public string Failure {get;private set;}
        public DrawWatchdog(double graceSeconds)
        {
            if(Double.IsNaN(graceSeconds) || Double.IsInfinity(graceSeconds) || graceSeconds<=0)
                throw new ArgumentOutOfRangeException("graceSeconds");
            this.graceSeconds=graceSeconds;
        }
        // The native caller resets on an intentional stop/pause or a changed
        // document/navigation/policy scope, before accepting that scope's data.
        public void Reset()
        {
            sampled=false;lastObserved=lastProgress=0;lastFrames=0;Failure=null;
        }
        public string Observe(double monotonicSeconds,long frames,bool running)
        {
            if(Double.IsNaN(monotonicSeconds) || Double.IsInfinity(monotonicSeconds) || monotonicSeconds<0 ||
                (sampled && monotonicSeconds<lastObserved))throw new ArgumentOutOfRangeException("monotonicSeconds");
            if(frames<0)throw new ArgumentOutOfRangeException("frames");
            if(Failure!=null){lastObserved=monotonicSeconds;return Failure;}
            if(!sampled){
                sampled=true;lastObserved=lastProgress=monotonicSeconds;lastFrames=frames;return null;
            }
            lastObserved=monotonicSeconds;
            if(frames<lastFrames)return Failure="draw-counter-rollback";
            // A stopped loop cannot buy another grace period merely by
            // supplying a changed counter. It must report active progress.
            if(running && frames>lastFrames)lastProgress=monotonicSeconds;
            lastFrames=frames;
            if(monotonicSeconds-lastProgress>=graceSeconds)
                return Failure=running?"draw-progress-stalled":"draw-loop-stopped";
            return null;
        }
    }
}
