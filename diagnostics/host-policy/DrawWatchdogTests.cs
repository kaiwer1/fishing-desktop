using System;
using FishingDesktop.Diagnostics.HostPolicy;

static class DrawWatchdogTests
{
    static int passed,failed;
    static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static void Healthy(string result){Need(result==null,"Unexpected failure: "+result);}
    static void Failure(string result,string expected){Need(result==expected,"Unexpected failure category: "+result);}
    static void Reject(Action action)
    {
        try{action();}catch(ArgumentOutOfRangeException){return;}
        throw new InvalidOperationException("Invalid observation was accepted.");
    }
    static DrawWatchdog Monitor(){return new DrawWatchdog(3);}
    static void Test(string name,Action action)
    {
        try{action();passed++;Console.WriteLine("PASS "+name);}
        catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}
    }
    static int Main()
    {
        Test("grace must be a finite positive duration",()=>{
            foreach(double value in new[]{0.0,-1,Double.NaN,Double.PositiveInfinity,Double.NegativeInfinity})Reject(()=>new DrawWatchdog(value));
        });
        Test("a new scope grants warmup even before its loop starts",()=>{
            var monitor=Monitor();Healthy(monitor.Observe(10,0,false));Healthy(monitor.Observe(12.75,0,false));
        });
        Test("active frame progress renews the deadline beyond the initial grace",()=>{
            var monitor=Monitor();Healthy(monitor.Observe(0,0,true));Healthy(monitor.Observe(2.5,1,true));
            Healthy(monitor.Observe(5,2,true));Healthy(monitor.Observe(7.5,3,true));Healthy(monitor.Observe(10,4,true));
        });
        Test("a short active frame stall is tolerated",()=>{
            var monitor=Monitor();Healthy(monitor.Observe(0,100,true));Healthy(monitor.Observe(2.75,100,true));
        });
        Test("an active unchanged counter fails at the exact grace boundary",()=>{
            var monitor=Monitor();Healthy(monitor.Observe(0,100,true));Failure(monitor.Observe(3,100,true),"draw-progress-stalled");
        });
        Test("a stopped loop gets grace then fails with a distinct reason",()=>{
            var monitor=Monitor();Healthy(monitor.Observe(0,0,false));Healthy(monitor.Observe(2.75,0,false));
            Failure(monitor.Observe(3,0,false),"draw-loop-stopped");
        });
        Test("changed counters from a stopped loop do not extend its deadline",()=>{
            var monitor=Monitor();Healthy(monitor.Observe(0,100,true));Healthy(monitor.Observe(1,101,false));
            Healthy(monitor.Observe(2,102,false));Failure(monitor.Observe(3,103,false),"draw-loop-stopped");
        });
        Test("the grace window starts at the last real active progress",()=>{
            var monitor=Monitor();Healthy(monitor.Observe(0,10,true));Healthy(monitor.Observe(2,11,true));
            Healthy(monitor.Observe(4.75,11,false));Failure(monitor.Observe(5,11,false),"draw-loop-stopped");
        });
        Test("counter rollback fails immediately while running",()=>{
            var monitor=Monitor();Healthy(monitor.Observe(0,100,true));Failure(monitor.Observe(.01,99,true),"draw-counter-rollback");
        });
        Test("counter rollback is not hidden by a stopped warmup loop",()=>{
            var monitor=Monitor();Healthy(monitor.Observe(0,100,false));Failure(monitor.Observe(.01,0,false),"draw-counter-rollback");
        });
        Test("a latched failure cannot be cleared by later apparent progress",()=>{
            var monitor=Monitor();Healthy(monitor.Observe(0,1,true));Failure(monitor.Observe(3,1,true),"draw-progress-stalled");
            Failure(monitor.Observe(4,1000,true),"draw-progress-stalled");Need(monitor.Failure=="draw-progress-stalled","Failure was not latched.");
            Reject(()=>monitor.Observe(3.5,1001,true));
        });
        Test("an explicit reset clears a fault for a fresh native scope",()=>{
            var monitor=Monitor();monitor.Observe(0,100,true);monitor.Observe(3,100,false);monitor.Reset();
            Need(monitor.Failure==null,"Reset retained a fault.");Healthy(monitor.Observe(50,0,false));Healthy(monitor.Observe(52.75,0,false));
        });
        Test("pause and resume rebasing does not charge the inactive interval",()=>{
            var monitor=Monitor();Healthy(monitor.Observe(0,100,true));Healthy(monitor.Observe(2.75,100,true));monitor.Reset();
            Healthy(monitor.Observe(1000,0,false));Healthy(monitor.Observe(1002.75,0,false));Healthy(monitor.Observe(1003,1,true));
        });
        Test("invalid or reversed clocks do not corrupt the prior baseline",()=>{
            var monitor=Monitor();Healthy(monitor.Observe(10,1,true));
            foreach(double value in new[]{-1.0,9,Double.NaN,Double.PositiveInfinity,Double.NegativeInfinity})Reject(()=>monitor.Observe(value,500,true));
            Healthy(monitor.Observe(11,1,true));Failure(monitor.Observe(13,1,true),"draw-progress-stalled");
        });
        Test("invalid frame counts cannot move the deadline or clock baseline",()=>{
            var monitor=Monitor();Healthy(monitor.Observe(0,1,true));Reject(()=>monitor.Observe(2,-1,true));
            Healthy(monitor.Observe(1,1,true));Failure(monitor.Observe(3,1,true),"draw-progress-stalled");
        });
        Test("same-time samples do not add time and maximum counters do not overflow",()=>{
            var monitor=Monitor();Healthy(monitor.Observe(0,Int64.MaxValue-1,true));Healthy(monitor.Observe(0,Int64.MaxValue,true));
            Healthy(monitor.Observe(2.75,Int64.MaxValue,true));Failure(monitor.Observe(3,Int64.MaxValue,true),"draw-progress-stalled");
        });
        Console.WriteLine("RESULT "+passed+" passed / "+failed+" failed");return failed==0?0:1;
    }
}
