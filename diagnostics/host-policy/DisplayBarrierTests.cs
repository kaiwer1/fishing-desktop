using System;
using System.Threading;
using FishingDesktop.Diagnostics.HostPolicy;

static class DisplayBarrierTests
{
    static int passed;
    static void Require(bool value){if(!value)throw new Exception("Invariant failed.");}
    static void Test(string name,Action action){action();passed++;Console.WriteLine("PASS "+name);}
    static int Main()
    {
        try{
            Test("new display generation blocks and requires verified current completion",delegate{
                var barrier=new DisplayBarrier();Require(!barrier.Blocked && barrier.Revision==0);long revision=barrier.Begin();Require(revision==1 && barrier.Blocked);Require(!barrier.Complete(revision,false) && barrier.Blocked);Require(barrier.Complete(revision,true) && !barrier.Blocked);Require(!barrier.Complete(revision,true));
            });
            Test("delayed completion cannot cross a repeated display notification",delegate{
                var barrier=new DisplayBarrier();long first=barrier.Begin(),second=barrier.Begin();Require(second>first && !barrier.Complete(first,true) && barrier.Blocked);Require(barrier.Complete(second,true));
            });
            Test("successful older view cannot resume a later display generation",delegate{
                var barrier=new DisplayBarrier();long first=barrier.Begin();barrier.Complete(first,true);long second=barrier.Begin();Require(!barrier.Complete(first,true) && barrier.Blocked && second>first);
            });
            Test("clearing display does not clear manual or session pause",delegate{
                var gate=new RunGate();gate.Set(PauseReason.Startup,false);gate.Set(PauseReason.Manual,true);gate.Set(PauseReason.SessionUnavailable,true);gate.Set(PauseReason.DisplayChanging,true);gate.Set(PauseReason.DisplayChanging,false);Require(gate.Snapshot().Reasons==(PauseReason.Manual|PauseReason.SessionUnavailable));
            });
            Test("display recovery cannot revive a queued old activation",delegate{
                var gate=new RunGate();gate.Set(PauseReason.Startup,false);long old=gate.Snapshot().Revision;gate.Set(PauseReason.DisplayChanging,true);gate.Set(PauseReason.DisplayChanging,false);Require(gate.Snapshot().CanRun && !gate.CanActivate(old));
            });
            Test("display pause and first resume interval never catch up activity",delegate{
                var clock=new ActiveClock(.25);clock.Advance(0,true);clock.Advance(.1,true);double before=clock.ActiveSeconds;clock.Advance(.2,false);clock.Advance(1000,false);clock.Advance(1001,true);Require(clock.ActiveSeconds==before);clock.Advance(1001.1,true);Require(clock.ActiveSeconds>before && clock.ActiveSeconds<before+.11);
            });
            Test("portrait invalid and empty targets cannot qualify for display completion",delegate{
                Require(DisplayBarrier.Landscape(1920,1080) && DisplayBarrier.Landscape(3440,1440) && DisplayBarrier.Landscape(1280,800));Require(!DisplayBarrier.Landscape(1080,1920) && !DisplayBarrier.Landscape(0,0) && !DisplayBarrier.Landscape(-1,1));
            });
            Test("foreign thread cannot complete display generation",delegate{
                var barrier=new DisplayBarrier();long revision=barrier.Begin();Exception error=null;var worker=new Thread(delegate(){try{barrier.Complete(revision,true);}catch(Exception value){error=value;}});worker.Start();worker.Join();Require(error is InvalidOperationException && barrier.Blocked);
            });
            Console.WriteLine("TOTAL "+passed);return 0;
        }catch(Exception error){Console.WriteLine("FAIL "+error.GetType().Name+": "+error.Message);return 1;}
    }
}
