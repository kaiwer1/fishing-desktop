using System;
using System.Threading;

namespace FishingDesktop.Diagnostics.HostPolicy
{
    // Notifications latch blocking facts. Only a NEW synchronous environment
    // observation may finish a resume; an unlock message never grants input.
    public sealed class NativeSignalState
    {
        public const PauseReason Mask=NativeEnvironment.EnvironmentMask|PauseReason.PowerSuspended;
        readonly int thread=Thread.CurrentThread.ManagedThreadId;
        readonly int session;
        bool locked,disconnected,loggedOff,powerSuspended,awaitingObservation;
        PauseReason observed=PauseReason.EnvironmentUnknown;
        public NativeSignalState(int currentSession){if(currentSession<0)throw new ArgumentOutOfRangeException("currentSession");session=currentSession;}
        void Check(){if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("Native signals require their owner thread.");}
        public PauseReason BlockingReasons {
            get{Check();return observed|((locked || disconnected || loggedOff)?PauseReason.SessionUnavailable:PauseReason.None)|
                (powerSuspended?PauseReason.PowerSuspended:PauseReason.None)|(awaitingObservation?PauseReason.EnvironmentUnknown:PauseReason.None);}
        }
        public bool Power(int code)
        {
            Check();
            if(code==4){powerSuspended=true;return true;}
            if(code==18 || code==7){powerSuspended=false;awaitingObservation=true;return true;}
            return false;
        }
        public bool Session(int code,long changedSession)
        {
            Check();if(changedSession!=session)return false;
            if(code==2 || code==4){disconnected=true;return true;}
            if(code==6){loggedOff=true;return true;}
            if(code==7){locked=true;return true;}
            if(code==1 || code==3){disconnected=false;awaitingObservation=true;return true;}
            if(code==5){loggedOff=false;awaitingObservation=true;return true;}
            if(code==8){locked=false;awaitingObservation=true;return true;}
            if(code==9 || code==15){awaitingObservation=true;return true;}
            return false;
        }
        public void Observe(EnvironmentSample sample)
        {
            Check();if(sample==null)throw new ArgumentNullException("sample");
            observed=sample.BlockingReasons;
            if(sample.Known && sample.InteractiveSession)awaitingObservation=false;
        }
    }
}
