using System;
using System.Threading;
using FishingDesktop.Diagnostics.HostPolicy;

static class NativeSignalStateTests
{
    static int passed;
    static EnvironmentSample Active(bool fullscreen){return new EnvironmentSample(true,true,fullscreen,"fixed");}
    static void Require(bool value){if(!value)throw new Exception("Invariant failed.");}
    static void Test(string name,Action action){action();passed++;Console.WriteLine("PASS "+name);}
    static bool Has(PauseReason reasons,PauseReason reason){return (reasons&reason)==reason;}
    static int Main()
    {
        try{
            Test("starts blocked until first fresh environment observation",delegate{
                var state=new NativeSignalState(3);Require(Has(state.BlockingReasons,PauseReason.EnvironmentUnknown));state.Observe(Active(false));Require(state.BlockingReasons==PauseReason.None);
            });
            Test("lock survives a stale apparently interactive poll",delegate{
                var state=new NativeSignalState(3);state.Observe(Active(false));Require(state.Session(7,3));state.Observe(Active(false));Require(Has(state.BlockingReasons,PauseReason.SessionUnavailable));
            });
            Test("foreign session cannot pause or clear this session",delegate{
                var state=new NativeSignalState(3);state.Observe(Active(false));Require(!state.Session(7,4));Require(state.BlockingReasons==PauseReason.None);state.Session(7,3);Require(!state.Session(8,4));Require(Has(state.BlockingReasons,PauseReason.SessionUnavailable));
            });
            Test("unlock awaits a new valid observation and preserves fullscreen",delegate{
                var state=new NativeSignalState(3);state.Observe(Active(false));state.Session(7,3);state.Session(8,3);Require(Has(state.BlockingReasons,PauseReason.EnvironmentUnknown));state.Observe(new EnvironmentSample(false,false,false,"fixed"));Require(Has(state.BlockingReasons,PauseReason.EnvironmentUnknown));state.Observe(Active(true));Require(state.BlockingReasons==PauseReason.Fullscreen);
            });
            Test("suspend remains blocked until resume notification",delegate{
                var state=new NativeSignalState(3);state.Observe(Active(false));state.Power(4);state.Observe(Active(false));Require(Has(state.BlockingReasons,PauseReason.PowerSuspended));state.Power(18);Require(Has(state.BlockingReasons,PauseReason.EnvironmentUnknown));state.Observe(Active(false));Require(state.BlockingReasons==PauseReason.None);
            });
            Test("power resume does not clear a locked session",delegate{
                var state=new NativeSignalState(3);state.Observe(Active(false));state.Session(7,3);state.Power(4);state.Power(7);state.Observe(Active(false));Require(state.BlockingReasons==PauseReason.SessionUnavailable);
            });
            Test("duplicate automatic and user resume require fresh observations",delegate{
                var state=new NativeSignalState(3);state.Observe(Active(false));state.Power(18);state.Observe(Active(false));state.Power(7);Require(Has(state.BlockingReasons,PauseReason.EnvironmentUnknown));state.Observe(Active(false));Require(state.BlockingReasons==PauseReason.None);
            });
            Test("disconnect logoff and reconnect are local-session barriers",delegate{
                foreach(int code in new[]{2,4,6}){var state=new NativeSignalState(3);state.Observe(Active(false));state.Session(code,3);Require(Has(state.BlockingReasons,PauseReason.SessionUnavailable));state.Session(code==6?5:1,3);state.Observe(Active(false));Require(state.BlockingReasons==PauseReason.None);}
            });
            Test("reconnect unlock and desktop-ready cannot clear unrelated session barriers",delegate{
                var state=new NativeSignalState(3);state.Observe(Active(false));state.Session(7,3);state.Session(2,3);state.Session(1,3);state.Session(15,3);state.Observe(Active(false));Require(Has(state.BlockingReasons,PauseReason.SessionUnavailable));
                state.Session(2,3);state.Session(8,3);state.Observe(Active(false));Require(Has(state.BlockingReasons,PauseReason.SessionUnavailable));state.Session(1,3);state.Observe(Active(false));Require(state.BlockingReasons==PauseReason.None);
            });
            Test("unknown messages and remote control never unlock a locked session",delegate{
                var state=new NativeSignalState(3);state.Observe(Active(false));state.Session(7,3);Require(!state.Power(99) && !state.Session(99,3));state.Session(9,3);state.Observe(Active(false));Require(state.BlockingReasons==PauseReason.SessionUnavailable);
            });
            Test("resume cannot restore old activation revision or catch up paused time",delegate{
                var state=new NativeSignalState(3);state.Observe(Active(false));var gate=new RunGate();gate.Set(PauseReason.Startup,false);long revision=gate.Snapshot().Revision;
                var clock=new ActiveClock(.25);clock.Advance(0,true);clock.Advance(.1,true);double initial=clock.ActiveSeconds;
                state.Power(4);gate.Replace(NativeSignalState.Mask,state.BlockingReasons);clock.Advance(.2,gate.Snapshot().CanRun);
                clock.Advance(3600,false);state.Power(18);gate.Replace(NativeSignalState.Mask,state.BlockingReasons);state.Observe(Active(false));gate.Replace(NativeSignalState.Mask,state.BlockingReasons);clock.Advance(3601,true);
                Require(gate.Snapshot().CanRun && !gate.CanActivate(revision) && clock.ActiveSeconds==initial);
            });
            Test("signals reject foreign threads",delegate{
                var state=new NativeSignalState(3);Exception error=null;var worker=new Thread(delegate(){try{state.Power(4);}catch(Exception value){error=value;}});worker.Start();worker.Join();Require(error is InvalidOperationException);
            });
            Console.WriteLine("TOTAL "+passed);return 0;
        }catch(Exception error){Console.WriteLine("FAIL "+error.GetType().Name+": "+error.Message);return 1;}
    }
}
