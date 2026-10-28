using System;
using System.Threading;
using FishingDesktop.GameCore;

static class FloatFlightSimulationTests
{
    static int passed,failed,nextId;
    static Guid G(){return new Guid(++nextId,0,0,new byte[]{0,0,0,0,0,0,0,7});}
    static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Near(double actual,double expected,double tolerance=1e-7){Need(Math.Abs(actual-expected)<=tolerance,"Expected "+expected+"; got "+actual);}
    static void Reject<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    static void Test(string name,Action body){try{body();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
    static FloatFlightRules Rules(double speed=10000,double angle=30,int timeout=10000){return new FloatFlightRules(speed,speed,angle,9810,timeout);}
    static FloatFlightWater Water(){return new FloatFlightWater(0,-1000000,1000000,-1000000,1000000);}
    static FloatFlightLaunch Launch(FloatFlightRules rules=null,FloatFlightWater water=null,double line=1000000,int power=500,FloatFlightPoint origin=null,FloatFlightPoint aim=null)
    {return new FloatFlightLaunch(origin??new FloatFlightPoint(0,1000,0),aim??new FloatFlightPoint(0,0,10000),water??Water(),rules??Rules(),power,line);}
    static FloatFlightSimulation Flight(FloatFlightLaunch launch=null){return new FloatFlightSimulation(G(),launch??Launch());}
    static FloatFlightResult Finish(FloatFlightSimulation flight,int step=250)
    {
        FloatFlightResult result=null;Guid session=flight.Snapshot.SessionId;
        for(int iteration=0;iteration<600001 && flight.Snapshot.Phase==FloatFlightPhase.Flying;iteration++)result=flight.Advance(session,step);
        Need(result!=null && result.TerminalEvent && flight.Snapshot.Phase!=FloatFlightPhase.Flying,"Flight never terminated once.");return result;
    }
    static void SamePosition(FloatFlightSnapshot a,FloatFlightSnapshot b)
    {Need(a.Phase==b.Phase && a.ElapsedMilliseconds==b.ElapsedMilliseconds && a.Reason==b.Reason,"State/time differs.");Near(a.Xmm,b.Xmm,0);Near(a.Ymm,b.Ymm,0);Near(a.Zmm,b.Zmm,0);Near(a.LineUsedMm,b.LineUsedMm,0);}
    static void Finite(FloatFlightSnapshot state)
    {foreach(double value in new[]{state.Xmm,state.Ymm,state.Zmm,state.LineUsedMm})Need(!Double.IsNaN(value)&&!Double.IsInfinity(value),"Nonfinite output.");}
    static int Main()
    {
        Test("starts at immutable origin without emitting contact",()=>{
            var f=Flight();var s=f.Snapshot;Need(s.Phase==FloatFlightPhase.Flying && s.SessionId!=Guid.Empty && s.ElapsedMilliseconds==0 && s.PowerPermille==500,"Invalid initial state.");
            Near(s.Xmm,0);Near(s.Ymm,1000);Near(s.Zmm,0);Near(s.LineUsedMm,0);
        });
        Test("analytic position uses total active time",()=>{
            var f=Flight();var r=f.Advance(f.Snapshot.SessionId,200);Need(r.Accepted&&!r.TerminalEvent,"Early terminal.");
            Near(r.Snapshot.Zmm,10000*Math.Cos(Math.PI/6)*.2);Near(r.Snapshot.Ymm,1000+10000*Math.Sin(Math.PI/6)*.2-.5*9810*.2*.2);
        });
        Test("equal active time has identical position under different step partitions",()=>{
            var a=Flight();var b=Flight();for(int i=0;i<3;i++)a.Advance(a.Snapshot.SessionId,250);for(int i=0;i<15;i++)b.Advance(b.Snapshot.SessionId,50);SamePosition(a.Snapshot,b.Snapshot);
        });
        Test("zero active step freezes a flight without wall clock catchup",()=>{
            var f=Flight();f.Advance(f.Snapshot.SessionId,250);var before=f.Snapshot;
            for(int i=0;i<100;i++){var r=f.Advance(before.SessionId,0);Need(r.Accepted&&!r.TerminalEvent,"Paused step emitted contact.");}SamePosition(before,f.Snapshot);
        });
        Test("overshooting step clamps to exact descending water plane",()=>{
            var f=Flight();var r=Finish(f);Need(r.Snapshot.Phase==FloatFlightPhase.Waiting && r.Code=="water-contact","Water contact missing.");Near(r.Snapshot.Ymm,0,0);
            double t=(5000+Math.Sqrt(5000*5000+2*9810*1000))/9810;Near(r.Snapshot.Zmm,10000*Math.Cos(Math.PI/6)*t);
            Need(r.Snapshot.ElapsedMilliseconds==(int)Math.Ceiling(t*1000),"Terminal active time was frame dependent.");
        });
        Test("terminal contact is identical with 1 ms and 250 ms steps",()=>{
            var a=Flight();var b=Flight();Finish(a,250);Finish(b,1);SamePosition(a.Snapshot,b.Snapshot);
        });
        Test("out of rectangle contact becomes invalid at the plane",()=>{
            var f=Flight(Launch(water:new FloatFlightWater(0,-10,10,1,100)));Finish(f);
            Need(f.Snapshot.Phase==FloatFlightPhase.Invalid && f.Snapshot.Reason=="outside-water","Invalid landing entered waiting.");Near(f.Snapshot.Ymm,0,0);
        });
        Test("rectangle boundary is included but a point beyond it is rejected",()=>{
            var baseline=Flight();Finish(baseline);double z=baseline.Snapshot.Zmm;
            var at=Flight(Launch(water:new FloatFlightWater(0,-1,1,z,z+1)));Finish(at);Need(at.Snapshot.Phase==FloatFlightPhase.Waiting,"Exact edge excluded.");
            var beyond=Flight(Launch(water:new FloatFlightWater(0,-1,1,z+1,z+2)));Finish(beyond);Need(beyond.Snapshot.Reason=="outside-water","Outside edge accepted.");
        });
        Test("aim chooses heading rather than silently forcing a landing distance",()=>{
            var a=Flight(Launch(aim:new FloatFlightPoint(0,0,10000)));var b=Flight(Launch(aim:new FloatFlightPoint(0,5000,20000)));
            a.Advance(a.Snapshot.SessionId,250);b.Advance(b.Snapshot.SessionId,250);SamePosition(a.Snapshot,b.Snapshot);
        });
        Test("negative and diagonal heading preserves horizontal direction",()=>{
            var f=Flight(Launch(aim:new FloatFlightPoint(-3000,0,4000)));f.Advance(f.Snapshot.SessionId,250);
            Need(f.Snapshot.Xmm<0 && f.Snapshot.Zmm>0,"Direction lost.");Near(f.Snapshot.Xmm/f.Snapshot.Zmm,-.75);
        });
        Test("power endpoints and midpoint interpolate configured launch speed",()=>{
            var rules=new FloatFlightRules(6000,18000,0,9810,10000);
            foreach(int power in new[]{0,500,1000}){var f=Flight(Launch(rules:rules,power:power));f.Advance(f.Snapshot.SessionId,100);Near(f.Snapshot.Zmm,(6000+12000*power/1000)*.1);}
        });
        Test("line contact wins before water and span stays bounded",()=>{
            var f=Flight(Launch(line:1000));Finish(f);Need(f.Snapshot.Reason=="line-limit" && f.Snapshot.Phase==FloatFlightPhase.Invalid && f.Snapshot.Ymm>0,"Line limit did not stop flight.");
            Need(f.Snapshot.LineUsedMm>=0 && f.Snapshot.LineUsedMm<=1000,"Line budget exceeded.");Near(f.Snapshot.LineUsedMm,1000,1e-6);
        });
        Test("continuous interior line maximum cannot be hidden by valid landing length",()=>{
            var loose=Flight(Launch(rules:Rules(10000,80)));Finish(loose);Need(loose.Snapshot.LineUsedMm<5000,"Fixture landing was not within line.");
            var limited=Flight(Launch(rules:Rules(10000,80),line:5000));Finish(limited);Need(limited.Snapshot.Reason=="line-limit" && limited.Snapshot.ElapsedMilliseconds<1071,"Interior line excursion was missed.");
        });
        Test("tangent line maximum is contact and a slightly longer span clears it",()=>{
            double vy=10000*Math.Sin(80*Math.PI/180),vh=10000*Math.Cos(80*Math.PI/180),g=9810;
            double t=(3*vy-Math.Sqrt(vy*vy-8*vh*vh))/(2*g),y=vy*t-.5*g*t*t;
            double maximum=Math.Sqrt(vh*vh*t*t+y*y);
            var tangent=Flight(Launch(rules:Rules(10000,80),line:maximum));Finish(tangent);Need(tangent.Snapshot.Reason=="line-limit","Tangent maximum was skipped.");
            Near(tangent.Snapshot.DurationMilliseconds,Math.Ceiling(t*1000),1);
            var clear=Flight(Launch(rules:Rules(10000,80),line:maximum+1));Finish(clear);Need(clear.Snapshot.Phase==FloatFlightPhase.Waiting,"Longer line incorrectly hit tangent.");
        });
        Test("line and water contact tie resolves to line exhaustion",()=>{
            var baseline=Flight();Finish(baseline);double line=baseline.Snapshot.LineUsedMm;
            var f=Flight(Launch(line:line));Finish(f);Need(f.Snapshot.Reason=="line-limit","Water incorrectly won exact line tie.");
        });
        Test("timeout bounds extreme valid arcs and uses exact configured instant",()=>{
            var f=Flight(Launch(rules:Rules(10000,80,50)));Finish(f);Need(f.Snapshot.Reason=="flight-timeout" && f.Snapshot.ElapsedMilliseconds==50,"Timeout missing.");
            Near(f.Snapshot.Ymm,1000+10000*Math.Sin(80*Math.PI/180)*.05-.5*9810*.05*.05);
        });
        Test("terminal notification occurs once across advance and end",()=>{
            var f=Flight();Finish(f);var s=f.Snapshot;
            for(int i=0;i<5;i++)Need(!f.Advance(s.SessionId,250).TerminalEvent,"Repeated contact event.");
            var end=f.End(s.SessionId);Need(end.Accepted&&!end.TerminalEvent && end.Snapshot.Phase==FloatFlightPhase.Ended,"End duplicated contact.");
            Need(!f.End(s.SessionId).Accepted&&!f.End(s.SessionId).TerminalEvent,"Repeated end accepted.");
        });
        Test("explicit early end freezes position and emits only one termination",()=>{
            var f=Flight();f.Advance(f.Snapshot.SessionId,250);var before=f.Snapshot;var result=f.End(before.SessionId);
            Need(result.Accepted&&result.TerminalEvent&&result.Snapshot.Phase==FloatFlightPhase.Ended,"End missing.");Near(result.Snapshot.Ymm,before.Ymm,0);
            Need(!f.Advance(before.SessionId,250).Accepted && !f.End(before.SessionId).TerminalEvent,"Ended flight resumed.");
        });
        Test("stale session advance and end cannot affect a new flight",()=>{
            var old=Flight();var current=Flight();var before=current.Snapshot;
            Need(!current.Advance(old.Snapshot.SessionId,250).Accepted && !current.End(old.Snapshot.SessionId).Accepted,"Stale session controlled current flight.");SamePosition(before,current.Snapshot);
        });
        Test("invalid empty identity and missing dependencies are refused",()=>{
            Reject<ArgumentException>(()=>new FloatFlightSimulation(Guid.Empty,Launch()));Reject<ArgumentNullException>(()=>new FloatFlightSimulation(G(),null));
            Reject<ArgumentNullException>(()=>new FloatFlightLaunch(null,new FloatFlightPoint(0,0,1),Water(),Rules(),500,100));
            Reject<ArgumentNullException>(()=>new FloatFlightLaunch(new FloatFlightPoint(0,1,0),null,Water(),Rules(),500,100));
            Reject<ArgumentNullException>(()=>new FloatFlightLaunch(new FloatFlightPoint(0,1,0),new FloatFlightPoint(0,0,1),null,Rules(),500,100));
            Reject<ArgumentNullException>(()=>new FloatFlightLaunch(new FloatFlightPoint(0,1,0),new FloatFlightPoint(0,0,1),Water(),null,500,100));
        });
        Test("all numeric input families reject NaN and infinity",()=>{
            foreach(double bad in new[]{Double.NaN,Double.PositiveInfinity,Double.NegativeInfinity}){
                Reject<ArgumentOutOfRangeException>(()=>new FloatFlightPoint(bad,0,0));Reject<ArgumentOutOfRangeException>(()=>new FloatFlightPoint(0,bad,0));Reject<ArgumentOutOfRangeException>(()=>new FloatFlightPoint(0,0,bad));
                Reject<ArgumentOutOfRangeException>(()=>new FloatFlightWater(bad,-1,1,-1,1));Reject<ArgumentOutOfRangeException>(()=>new FloatFlightWater(0,bad,1,-1,1));Reject<ArgumentOutOfRangeException>(()=>new FloatFlightWater(0,-1,bad,-1,1));Reject<ArgumentOutOfRangeException>(()=>new FloatFlightWater(0,-1,1,bad,1));Reject<ArgumentOutOfRangeException>(()=>new FloatFlightWater(0,-1,1,-1,bad));
                Reject<ArgumentOutOfRangeException>(()=>new FloatFlightRules(bad,20000,40,9810,5000));Reject<ArgumentOutOfRangeException>(()=>new FloatFlightRules(5000,bad,40,9810,5000));Reject<ArgumentOutOfRangeException>(()=>new FloatFlightRules(5000,20000,bad,9810,5000));Reject<ArgumentOutOfRangeException>(()=>new FloatFlightRules(5000,20000,40,bad,5000));Reject<ArgumentOutOfRangeException>(()=>Launch(line:bad));
            }
        });
        Test("invalid geometry and configuration bounds are refused",()=>{
            Reject<ArgumentException>(()=>new FloatFlightWater(0,1,1,-1,1));Reject<ArgumentException>(()=>new FloatFlightWater(0,-1,1,2,1));
            Reject<ArgumentException>(()=>Launch(origin:new FloatFlightPoint(0,0,0)));Reject<ArgumentException>(()=>Launch(aim:new FloatFlightPoint(0,0,0)));
            Reject<ArgumentOutOfRangeException>(()=>Launch(line:0));Reject<ArgumentOutOfRangeException>(()=>Launch(power:-1));Reject<ArgumentOutOfRangeException>(()=>Launch(power:1001));
            Reject<ArgumentOutOfRangeException>(()=>new FloatFlightRules(2,1,40,9810,5000));Reject<ArgumentOutOfRangeException>(()=>Rules(angle:90));Reject<ArgumentOutOfRangeException>(()=>Rules(angle:-1));
            Reject<ArgumentOutOfRangeException>(()=>new FloatFlightRules(1,2,40,0,5000));Reject<ArgumentOutOfRangeException>(()=>Rules(timeout:0));Reject<ArgumentOutOfRangeException>(()=>Rules(timeout:600001));
            Reject<ArgumentOutOfRangeException>(()=>new FloatFlightPoint(1e10,0,0));
        });
        Test("invalid elapsed steps cannot change state",()=>{
            var f=Flight();var before=f.Snapshot;Reject<ArgumentOutOfRangeException>(()=>f.Advance(before.SessionId,-1));Reject<ArgumentOutOfRangeException>(()=>f.Advance(before.SessionId,251));Reject<ArgumentOutOfRangeException>(()=>f.Advance(before.SessionId,Int32.MaxValue));SamePosition(before,f.Snapshot);
        });
        Test("tiny heading components remain finite instead of underflowing normalization",()=>{
            var f=Flight(Launch(aim:new FloatFlightPoint(1e-300,0,1e-300)));f.Advance(f.Snapshot.SessionId,250);Finite(f.Snapshot);Near(f.Snapshot.Xmm,f.Snapshot.Zmm,0);
        });
        Test("large permitted configuration stays finite and bounded",()=>{
            var f=Flight(Launch(rules:new FloatFlightRules(1000000,1000000,89,1,600000),line:1000000000,origin:new FloatFlightPoint(1e9,1e9,1e9),aim:new FloatFlightPoint(-1e9,0,-1e9)));
            Finish(f);Finite(f.Snapshot);Need(f.Snapshot.ElapsedMilliseconds<=600000 && f.Snapshot.LineUsedMm<=1e9,"Bounded model overflowed.");
        });
        Test("snapshots are stable values and wrong thread cannot advance owner",()=>{
            var f=Flight();var before=f.Snapshot;Exception caught=null;
            var worker=new Thread(()=>{try{f.Advance(before.SessionId,250);}catch(Exception e){caught=e;}});worker.Start();worker.Join();
            Need(caught is InvalidOperationException,"Cross-thread change accepted.");SamePosition(before,f.Snapshot);
            f.Advance(before.SessionId,250);Need(before.ElapsedMilliseconds==0 && before.Ymm==1000,"Published snapshot mutated.");
        });
        Console.WriteLine("RESULT passed="+passed+" failed="+failed);return failed==0?0:1;
    }
}
