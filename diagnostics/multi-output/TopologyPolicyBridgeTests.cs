using System;
using System.Threading;
using FishingDesktop.Diagnostics.MultiOutput;
using FishingDesktop.Diagnostics.OutputTopology;

static class TopologyPolicyBridgeTests
{
    static int passed,failed;
    static void Need(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
    static void Reject<T>(Action action) where T:Exception {try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    static TopologyOutput A(int dpi=96,int width=1920,bool primary=true){return new TopologyOutput("output-1",0,0,width,1080,dpi,dpi,primary);}
    static TopologyOutput B(bool primary=false){return new TopologyOutput("output-2",-1920,0,1920,1080,96,96,primary);}
    static TopologySample Known(long serial,params TopologyOutput[] rows){return TopologySample.CreateKnown(serial,rows);}
    static TopologySample Unknown(long serial){return TopologySample.CreateUnavailable(serial,TopologyUnavailableReason.EnumerationFailed);}
    static void Ready(OutputPolicy p,string key,long topology,long view){Need(p.BindView(key,topology,view),"Bind failed.");Need(p.Observe(key,topology,view,true,true,false,true),"Observe failed.");}
    static TopologyPolicyBridge Running(out OutputPolicy p)
    {
        p=new OutputPolicy();var b=new TopologyPolicyBridge(p);b.Apply(Known(1,A(),B()));
        Ready(p,"output-1",1,1);Ready(p,"output-2",1,1);p.SetGlobal(OutputGlobalBlock.Startup,false);p.SelectControlTarget("output-1");return b;
    }
    static int Main()
    {
        Test("construction blocks unverified topology and preserves startup",()=>{
            var p=new OutputPolicy();var b=new TopologyPolicyBridge(p);var s=p.Snapshot();
            Need(!b.HasVerifiedTopology&&b.TopologyGeneration==0&&!s.CanRun&&s.GlobalBlocks==(OutputGlobalBlock.Startup|OutputGlobalBlock.TopologyChanging),"Unverified topology eligible.");
        });
        Test("first known sample binds no views and does not clear startup",()=>{
            var p=new OutputPolicy();var b=new TopologyPolicyBridge(p);Need(b.Apply(Known(83,A()))==TopologyApplyResult.Replaced,"Not replaced.");var s=p.Snapshot();
            Need(b.TopologyGeneration==1&&b.LastCaptureSerial==83&&b.HasVerifiedTopology&&s.GlobalBlocks==OutputGlobalBlock.Startup&&!s.Outputs[0].Bound&&!s.CanRun,"Serial became generation or view became ready.");
        });
        Test("identical samples in different order do not churn topology view or epoch",()=>{
            OutputPolicy p;var b=Running(out p);var before=p.Snapshot();Need(b.Apply(Known(80,B(),A()))==TopologyApplyResult.Unchanged,"Order became topology change.");var after=p.Snapshot();
            Need(before.Revision==after.Revision&&before.InputEpoch==after.InputEpoch&&after.ControlEligible&&b.TopologyGeneration==1&&b.LastCaptureSerial==80,"Stable polling revoked healthy state.");
        });
        Test("unavailable sample blocks all output and old activation scope",()=>{
            OutputPolicy p;var b=Running(out p);var before=p.Snapshot();Need(b.Apply(Unknown(2))==TopologyApplyResult.Unavailable,"Unknown not applied.");var after=p.Snapshot();
            Need(!b.HasVerifiedTopology&&!after.CanRun&&!after.ControlEligible&&after.InputEpoch>before.InputEpoch&&!p.CanActivate("output-1",1,1,before.InputEpoch),"Unknown reused positive state.");
        });
        Test("recovery with identical dimensions still retires all prior readiness",()=>{
            OutputPolicy p;var b=Running(out p);b.Apply(Unknown(2));var paused=p.Snapshot();Need(b.Apply(Known(3,A(),B()))==TopologyApplyResult.Replaced,"Recovery not replaced.");var s=p.Snapshot();
            Need(s.TopologyGeneration==2&&s.InputEpoch>paused.InputEpoch&&!s.CanRun&&!s.ControlEligible&&!s.Outputs[0].Bound&&!s.Outputs[1].Bound,"Recovery inherited old ready views.");
            Need(!p.Observe("output-1",1,1,true,true,false,true),"Late prior topology callback revived view.");
        });
        Test("recovery needs explicit new bind observation and fresh activation epoch",()=>{
            OutputPolicy p;var b=Running(out p);var old=p.Snapshot();b.Apply(Unknown(2));b.Apply(Known(3,A(),B()));Ready(p,"output-1",2,2);var now=p.Snapshot();
            Need(now.CanRun&&now.ControlEligible&&!p.CanActivate("output-1",1,1,old.InputEpoch)&&p.CanActivate("output-1",2,2,now.InputEpoch),"Recovery scope not separated.");
        });
        Test("stale known cannot clear a later unavailable observation",()=>{
            OutputPolicy p;var b=Running(out p);b.Apply(Unknown(90));var before=p.Snapshot();
            Need(b.Apply(Known(89,A(),B()))==TopologyApplyResult.Stale&&b.Apply(Known(90,A(),B()))==TopologyApplyResult.Stale&&!b.HasVerifiedTopology&&p.Snapshot().Revision==before.Revision,"Late known cleared barrier.");
        });
        Test("stale unavailable cannot poison a later verified sample",()=>{
            OutputPolicy p;var b=Running(out p);b.Apply(Known(90,A(),B()));var before=p.Snapshot();
            Need(b.Apply(Unknown(89))==TopologyApplyResult.Stale&&p.Snapshot().Revision==before.Revision&&b.HasVerifiedTopology&&p.Snapshot().ControlEligible,"Late unknown changed current state.");
        });
        Test("repeated unavailable samples do not churn generation or input epoch",()=>{
            OutputPolicy p;var b=Running(out p);b.Apply(Unknown(2));var before=p.Snapshot();b.Apply(Unknown(3));var after=p.Snapshot();
            Need(after.Revision==before.Revision&&after.InputEpoch==before.InputEpoch&&b.TopologyGeneration==1&&b.LastCaptureSerial==3,"Failure polling churned state.");
        });
        Test("dimension and DPI changes each require replacement and new bindings",()=>{
            OutputPolicy p;var b=Running(out p);b.Apply(Known(2,A(120),B()));Need(b.TopologyGeneration==2&&!p.Snapshot().CanRun,"DPI change ignored.");
            Ready(p,"output-1",2,2);b.Apply(Known(3,A(120,1600),B()));Need(b.TopologyGeneration==3&&!p.Snapshot().CanRun,"Geometry change inherited view.");
        });
        Test("primary assignment change also replaces generation",()=>{
            OutputPolicy p;var b=Running(out p);Need(b.Apply(Known(2,A(96,1920,false),B(true)))==TopologyApplyResult.Replaced&&b.TopologyGeneration==2&&!p.Snapshot().CanRun,"Primary change ignored.");
        });
        Test("removed target is not silently redirected to surviving output",()=>{
            OutputPolicy p;var b=Running(out p);p.SelectControlTarget("output-2");b.Apply(Known(2,A()));Ready(p,"output-1",2,2);var s=p.Snapshot();
            Need(s.CanRun&&s.ControlTarget==null&&!s.ControlEligible,"Removed target was redirected.");
        });
        Test("topology recovery never clears manual session or scene blockers",()=>{
            OutputPolicy p;var b=Running(out p);var mask=OutputGlobalBlock.Manual|OutputGlobalBlock.SessionUnavailable|OutputGlobalBlock.SceneFault;p.ReplaceGlobal(mask,mask);
            b.Apply(Unknown(2));b.Apply(Known(3,A(),B()));Ready(p,"output-1",2,2);Need(p.Snapshot().GlobalBlocks==mask&&!p.Snapshot().CanRun,"Recovery cleared another subsystem barrier.");
        });
        Test("model capacity rejection blocks old topology without partial replacement",()=>{
            var p=new OutputPolicy(1);var b=new TopologyPolicyBridge(p);b.Apply(Known(1,A()));Ready(p,"output-1",1,1);p.SetGlobal(OutputGlobalBlock.Startup,false);
            Need(b.Apply(Known(2,A(),B()))==TopologyApplyResult.Rejected&&b.TopologyGeneration==1&&p.Snapshot().Outputs.Count==1&&!p.Snapshot().CanRun&&!b.HasVerifiedTopology,"Rejected candidate exposed old ready state.");
            Need(b.Apply(Known(3,A()))==TopologyApplyResult.Replaced&&b.TopologyGeneration==2&&!p.Snapshot().Outputs[0].Bound,"Recovery from rejected capture reused ready.");
        });
        Test("another topology writer latches divergence and cannot be adopted",()=>{
            OutputPolicy p;var b=Running(out p);p.ReplaceTopology(8,new[]{new OutputDefinition("other",0,0,100,100,96,96)});
            Need(b.Apply(Known(2,A(),B()))==TopologyApplyResult.Diverged&&b.Apply(Known(3,A(),B()))==TopologyApplyResult.Diverged&&!b.HasVerifiedTopology&&!p.Snapshot().CanRun&&p.Snapshot().TopologyGeneration==8,"External topology was silently adopted.");
        });
        Test("stale sample still detects external topology drift without advancing serial",()=>{
            OutputPolicy p;var b=Running(out p);b.Apply(Known(90,A(),B()));p.ReplaceTopology(8,new[]{new OutputDefinition("other",0,0,100,100,96,96)});
            Ready(p,"other",8,1);Need(p.Snapshot().CanRun&&!b.HasVerifiedTopology,"Drift reported verified.");
            Need(b.Apply(Known(89,A(),B()))==TopologyApplyResult.Diverged&&b.LastCaptureSerial==90&&!p.Snapshot().CanRun&&!b.HasVerifiedTopology,"Stale sample bypassed drift barrier.");
        });
        Test("thread checks and bad arguments do not mutate policy",()=>{
            var p=new OutputPolicy();var b=new TopologyPolicyBridge(p);var before=p.Snapshot();Reject<ArgumentNullException>(()=>b.Apply(null));Exception caught=null;
            var thread=new Thread(()=>{try{b.Apply(Known(1,A()));}catch(Exception e){caught=e;}});thread.Start();thread.Join();
            Need(caught is InvalidOperationException&&p.Snapshot().Revision==before.Revision&&b.LastCaptureSerial==0,"Wrong thread or invalid argument mutated state.");
            Reject<ArgumentException>(()=>new TopologyPolicyBridge(newConfigured()));
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
    static OutputPolicy newConfigured(){var p=new OutputPolicy();p.ReplaceTopology(1,new[]{new OutputDefinition("one",0,0,10,10,96,96)});return p;}
}
