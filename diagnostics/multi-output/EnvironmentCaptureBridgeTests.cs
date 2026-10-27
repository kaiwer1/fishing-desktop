using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using FishingDesktop.Diagnostics.HostPolicy;
using FishingDesktop.Diagnostics.MultiOutput;
using FishingDesktop.Diagnostics.OutputEnvironment;

static class EnvironmentCaptureBridgeTests
{
    static int passed,failed;
    static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Reject<T>(Action action) where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
    static OutputDefinition Left(){return new OutputDefinition("left",-1920,0,1920,1080,96,96);}
    static OutputDefinition Right(){return new OutputDefinition("right",0,0,1920,1080,96,96);}
    static OutputDefinition[] Pair(){return new[]{Left(),Right()};}
    static OutputState Row(OutputPolicyState state,string key){foreach(var row in state.Outputs)if(row.Definition.Key==key)return row;throw new InvalidOperationException("Missing output.");}
    static void Ready(OutputPolicy policy,string key,long topology,long view)
    {Need(policy.BindView(key,topology,view)&&policy.Observe(key,topology,view,true,true,false,true),"View did not become ready.");}
    sealed class Queries : INativeEnvironmentQueries
    {
        readonly Dictionary<string,int> counts=new Dictionary<string,int>();
        internal readonly List<string> Calls=new List<string>();
        internal Action<string,int> At;
        internal string ThrowAt;
        internal bool Active=true,Desktop=true,Aware=true;
        internal uint ForegroundPid=300;
        internal EnvironmentWindowRect Rectangle=new EnvironmentWindowRect(0,0,1920,1080);
        internal int Count(string key){int count;return counts.TryGetValue(key,out count)?count:0;}
        int Touch(string key){int count=Count(key)+1;counts[key]=count;Calls.Add(key);if(At!=null)At(key,count);if(ThrowAt==key+"#"+count)throw new InvalidOperationException("Synthetic private query detail.");return count;}
        internal bool Awareness(){Touch("awareness");return Aware;}
        public bool SessionActive(int sessionId){Need(sessionId==7,"Wrong session.");Touch("session");return Active;}
        public bool DefaultInputDesktop(){Touch("desktop");return Desktop;}
        public IntPtr ForegroundWindow(){Touch("foreground");return new IntPtr(11);}
        public IntPtr ShellWindow(){Touch("shell");return new IntPtr(22);}
        public bool WindowExists(IntPtr window){Need(window==new IntPtr(11)||window==new IntPtr(22),"Unknown synthetic window.");Touch("exists");return true;}
        public bool WindowVisible(IntPtr window){Need(window==new IntPtr(11),"Wrong visibility window.");Touch("visible");return true;}
        public bool WindowIconic(IntPtr window){Need(window==new IntPtr(11),"Wrong iconic window.");Touch("iconic");return false;}
        public bool TryProcessId(IntPtr window,out uint pid){Need(window==new IntPtr(11)||window==new IntPtr(22),"Wrong PID window.");Touch("pid");pid=window==new IntPtr(11)?ForegroundPid:200u;return true;}
        public bool TryWindowRect(IntPtr window,out EnvironmentWindowRect rect){Need(window==new IntPtr(11),"Wrong geometry window.");Touch("rect");rect=Rectangle;return true;}
    }
    sealed class Fixture
    {
        internal readonly OutputPolicy Policy;
        internal readonly Queries Query;
        internal readonly EnvironmentCaptureBridge Bridge;
        internal Fixture(OutputDefinition[] definitions=null,bool ready=true,int captureMaximum=16)
        {
            Policy=new OutputPolicy();var rows=definitions??Pair();Policy.ReplaceTopology(1,rows);
            if(ready)foreach(var row in rows)Ready(Policy,row.Key,1,1);
            if(rows.Length>0)Policy.SelectControlTarget(rows[0].Key);
            Policy.SetGlobal(OutputGlobalBlock.Startup,false);Query=new Queries();
            Bridge=new EnvironmentCaptureBridge(Policy,new OutputEnvironmentCapture(Query,100,7,Query.Awareness,captureMaximum));
        }
    }
    static int Main()
    {
        Test("construction registers one private environment source and fails closed",()=>{
            var f=new Fixture();var s=f.Policy.Snapshot();
            Need(s.EnvironmentRegistered&&!s.EnvironmentKnown&&s.GlobalBlocks==OutputGlobalBlock.EnvironmentUnknown&&!s.CanRun&&f.Query.Calls.Count==0,"Construction sampled or allowed activity.");
            Reject<InvalidOperationException>(()=>f.Policy.RegisterEnvironmentSource());
        });
        Test("one real capture class observes all policy outputs in one bracketed query sequence",()=>{
            var f=new Fixture();Need(f.Bridge.Poll()==EnvironmentCaptureBridgeResult.Applied,"Known capture not applied.");var s=f.Policy.Snapshot();
            Need(s.EnvironmentKnown&&s.CanRun&&s.ControlEligible&&!Row(s,"left").EnvironmentFullscreen&&Row(s,"right").EnvironmentFullscreen,"Coverage not mapped to declared outputs.");
            foreach(string key in new[]{"awareness","session","desktop","foreground","shell","rect"})Need(f.Query.Count(key)==2,"Queries repeated by output: "+key);
            Need(s.EnvironmentCaptureSerial==1&&f.Bridge.LastPollSerial==1&&f.Bridge.LastCaptureTopology==1,"Serial or generation changed meaning.");
        });
        Test("adding a second output does not multiply raw environment sampling",()=>{
            var one=new Fixture(new[]{Right()});var two=new Fixture();one.Bridge.Poll();two.Bridge.Poll();
            Need(String.Join(",",one.Query.Calls.ToArray())==String.Join(",",two.Query.Calls.ToArray()),"Per-output raw sampling repeated.");
        });
        Test("coverage derives from the actual frozen policy rectangles not conventional screen coordinates",()=>{
            var f=new Fixture(new[]{new OutputDefinition("left",4000,-100,640,480,120,120),new OutputDefinition("right",4640,-100,800,600,144,144)});
            f.Query.Rectangle=new EnvironmentWindowRect(4000,-100,4640,380);f.Bridge.Poll();var s=f.Policy.Snapshot();
            Need(Row(s,"left").EnvironmentFullscreen&&!Row(s,"right").EnvironmentFullscreen,"Capture ignored custom rectangles.");
        });
        Test("stable polls advance serial without revision or input epoch churn",()=>{
            var f=new Fixture();f.Bridge.Poll();var before=f.Policy.Snapshot();Need(f.Bridge.CaptureAndApply()==EnvironmentCaptureBridgeResult.Unchanged,"Stable batch was not unchanged.");var after=f.Policy.Snapshot();
            Need(after.Revision==before.Revision&&after.InputEpoch==before.InputEpoch&&after.EnvironmentCaptureSerial==2&&f.Bridge.LastPollSerial==2,"Stable polling invalidated control or reused a serial.");
        });
        Test("one batch of two coverage changes commits once and revokes the target exactly once",()=>{
            var f=new Fixture();f.Bridge.Poll();var before=f.Policy.Snapshot();f.Query.Rectangle=new EnvironmentWindowRect(-1920,0,0,1080);f.Bridge.Poll();var after=f.Policy.Snapshot();
            Need(after.Revision==before.Revision+1&&after.InputEpoch==before.InputEpoch+1&&!after.ControlEligible&&after.CanRun&&Row(after,"left").Fullscreen&&!Row(after,"right").Fullscreen,"Batch exposed partial state or repeated epoch changes.");
        });
        Test("non-target coverage recovery preserves healthy target activation scope",()=>{
            var f=new Fixture();f.Bridge.Poll();var before=f.Policy.Snapshot();f.Query.ForegroundPid=100;f.Bridge.Poll();var after=f.Policy.Snapshot();
            Need(after.InputEpoch==before.InputEpoch&&f.Policy.CanActivate("left",1,1,before.InputEpoch)&&!Row(after,"right").EnvironmentFullscreen,"Other output revoked healthy control.");
        });
        Test("unknown session maps semantic bits without overwriting independent legacy blockers",()=>{
            var f=new Fixture();f.Policy.SetGlobal(OutputGlobalBlock.Manual,true);f.Policy.SetGlobal(OutputGlobalBlock.PowerSuspended,true);f.Query.Active=false;
            Need(f.Bridge.Poll()==EnvironmentCaptureBridgeResult.Unavailable&&f.Bridge.LastReason==OutputEnvironmentReason.SessionUnavailable,"Unavailable session not retained.");
            Need(f.Policy.Snapshot().GlobalBlocks==(OutputGlobalBlock.EnvironmentUnknown|OutputGlobalBlock.SessionUnavailable|OutputGlobalBlock.Manual|OutputGlobalBlock.PowerSuspended),"Environment 1/2 flags were cast to policy bits or independent votes were cleared.");
        });
        Test("known recovery clears only this source and retains another source with the same bits",()=>{
            var f=new Fixture();var other=f.Policy.RegisterGlobalSource(OutputGlobalBlock.EnvironmentUnknown|OutputGlobalBlock.SessionUnavailable,OutputGlobalBlock.EnvironmentUnknown|OutputGlobalBlock.SessionUnavailable);
            f.Policy.SetGlobal(OutputGlobalBlock.SceneFault,true);f.Query.Active=false;f.Bridge.Poll();f.Query.Active=true;f.Bridge.Poll();var s=f.Policy.Snapshot();
            Need(s.EnvironmentKnown&&!s.CanRun&&s.GlobalBlocks==(OutputGlobalBlock.EnvironmentUnknown|OutputGlobalBlock.SessionUnavailable|OutputGlobalBlock.SceneFault),"Recovery erased another source.");
            f.Policy.ReplaceGlobalForSource(other,OutputGlobalBlock.None);f.Policy.SetGlobal(OutputGlobalBlock.SceneFault,false);Need(f.Policy.Snapshot().CanRun,"Independent source could not recover itself.");
        });
        Test("legacy clearing cannot clear the reserved environment source",()=>{
            var f=new Fixture();f.Query.Active=false;f.Bridge.Poll();f.Policy.ReplaceGlobal((OutputGlobalBlock)127,OutputGlobalBlock.None);
            Need(!f.Policy.Snapshot().CanRun&&f.Policy.Snapshot().GlobalBlocks==(OutputGlobalBlock.EnvironmentUnknown|OutputGlobalBlock.SessionUnavailable),"Legacy reset cleared environment vote.");
        });
        Test("repeated unknown observations do not churn revision or epoch",()=>{
            var f=new Fixture();f.Bridge.Poll();f.Query.Active=false;f.Bridge.Poll();var before=f.Policy.Snapshot();f.Bridge.Poll();var after=f.Policy.Snapshot();
            Need(after.Revision==before.Revision&&after.InputEpoch==before.InputEpoch&&after.EnvironmentCaptureSerial==3,"Unknown polling churned policy.");
        });
        Test("coverage is installed for unbound outputs and survives their first Ready",()=>{
            var f=new Fixture(ready:false);f.Bridge.Poll();Need(Row(f.Policy.Snapshot(),"right").EnvironmentFullscreen&&!Row(f.Policy.Snapshot(),"right").Bound,"Unbound output was omitted.");
            Ready(f.Policy,"right",1,1);var row=Row(f.Policy.Snapshot(),"right");Need(row.EnvironmentFullscreen&&!row.ViewFullscreen&&!row.CanDraw,"Ready cleared native coverage.");
        });
        Test("RetireView and newer BindView preserve environment coverage",()=>{
            var f=new Fixture();f.Bridge.Poll();Need(f.Policy.RetireView("right",1,1),"Retire failed.");Need(Row(f.Policy.Snapshot(),"right").EnvironmentFullscreen,"Retire cleared coverage.");
            Ready(f.Policy,"right",1,2);Need(Row(f.Policy.Snapshot(),"right").EnvironmentFullscreen&&!Row(f.Policy.Snapshot(),"right").CanDraw,"Rebind cleared coverage.");
        });
        Test("view fullscreen and environment fullscreen remain independent OR inputs",()=>{
            var f=new Fixture();f.Policy.Observe("left",1,1,true,true,true,true);f.Bridge.Poll();var s=f.Policy.Snapshot();
            Need(Row(s,"left").ViewFullscreen&&!Row(s,"left").EnvironmentFullscreen&&Row(s,"left").Fullscreen,"Environment cleared view fullscreen.");
            f.Policy.Observe("right",1,1,true,true,false,true);Need(Row(f.Policy.Snapshot(),"right").EnvironmentFullscreen,"View cleared environment fullscreen.");
        });
        Test("no topology refuses capture and keeps registration Unknown",()=>{
            var p=new OutputPolicy();var q=new Queries();var b=new EnvironmentCaptureBridge(p,new OutputEnvironmentCapture(q,100,7,q.Awareness));
            Need(b.Poll()==EnvironmentCaptureBridgeResult.NoTopology&&q.Calls.Count==0&&b.LastPollSerial==1&&!p.Snapshot().EnvironmentKnown&&!p.Snapshot().CanRun,"No-topology poll sampled or allowed activity.");
        });
        Test("positive but empty topology refuses capture without partial coverage",()=>{
            var f=new Fixture(new OutputDefinition[0]);Need(f.Bridge.Poll()==EnvironmentCaptureBridgeResult.NoTopology&&f.Query.Calls.Count==0&&f.Policy.Snapshot().EnvironmentCaptureSerial==1&&!f.Policy.Snapshot().EnvironmentKnown,"Empty topology sampled or became known.");
        });
        Test("TopologyChanging refuses raw queries and clearing it alone cannot resume",()=>{
            var f=new Fixture();f.Bridge.Poll();f.Policy.SetGlobal(OutputGlobalBlock.TopologyChanging,true);int calls=f.Query.Calls.Count;
            Need(f.Bridge.Poll()==EnvironmentCaptureBridgeResult.TopologyBlocked&&f.Query.Calls.Count==calls,"Barrier poll touched raw queries.");var before=f.Policy.Snapshot();f.Bridge.Poll();
            Need(f.Policy.Snapshot().InputEpoch==before.InputEpoch&&f.Policy.Snapshot().Revision==before.Revision,"Repeated barrier churned policy.");
            f.Policy.SetGlobal(OutputGlobalBlock.TopologyChanging,false);Need(!f.Policy.Snapshot().CanRun,"Barrier clearing reused earlier Known.");f.Bridge.Poll();Need(f.Policy.Snapshot().CanRun,"Fresh capture failed to recover.");
        });
        Test("topology replacement inside a raw query discards the old batch",()=>{
            var f=new Fixture();f.Query.At=(key,count)=>{if(key=="rect"&&count==1)f.Policy.ReplaceTopology(2,new[]{new OutputDefinition("new",0,0,1920,1080,96,96)});};
            Need(f.Bridge.Poll()==EnvironmentCaptureBridgeResult.ScopeChanged,"Old capture applied to new topology.");var s=f.Policy.Snapshot();
            Need(s.TopologyGeneration==2&&!s.EnvironmentKnown&&!s.CanRun&&s.Outputs.Count==1&&s.Outputs[0].Definition.Key=="new"&&!s.Outputs[0].EnvironmentFullscreen&&s.EnvironmentCaptureSerial==1&&f.Bridge.LastCaptureTopology==1,"Old coverage was relabelled with a new generation.");
        });
        Test("same keys with changed geometry cannot accept results from prior rectangles",()=>{
            var f=new Fixture();f.Query.At=(key,count)=>{if(key=="rect"&&count==1)f.Policy.ReplaceTopology(2,new[]{new OutputDefinition("left",0,0,1920,1080,96,96),new OutputDefinition("right",1920,0,1920,1080,96,96)});};
            Need(f.Bridge.Poll()==EnvironmentCaptureBridgeResult.ScopeChanged&&!f.Policy.Snapshot().EnvironmentKnown&&!Row(f.Policy.Snapshot(),"right").EnvironmentFullscreen,"Same aliases allowed stale geometry.");
            f.Query.At=null;Ready(f.Policy,"left",2,2);Ready(f.Policy,"right",2,2);f.Bridge.Poll();Need(Row(f.Policy.Snapshot(),"left").EnvironmentFullscreen&&!Row(f.Policy.Snapshot(),"right").EnvironmentFullscreen,"Fresh capture did not use new geometry.");
        });
        Test("a barrier raised during capture cannot be cleared by its Known result",()=>{
            var f=new Fixture();f.Query.At=(key,count)=>{if(key=="rect"&&count==1)f.Policy.SetGlobal(OutputGlobalBlock.TopologyChanging,true);};
            Need(f.Bridge.Poll()==EnvironmentCaptureBridgeResult.ScopeChanged&&(f.Policy.Snapshot().GlobalBlocks&(OutputGlobalBlock.TopologyChanging|OutputGlobalBlock.EnvironmentUnknown))==(OutputGlobalBlock.TopologyChanging|OutputGlobalBlock.EnvironmentUnknown),"Known cleared an in-flight barrier.");
        });
        Test("a same-generation barrier raise and clear pulse invalidates the capture revision",()=>{
            var f=new Fixture();f.Query.At=(key,count)=>{if(key=="rect"&&count==1){f.Policy.SetGlobal(OutputGlobalBlock.TopologyChanging,true);f.Policy.SetGlobal(OutputGlobalBlock.TopologyChanging,false);}};
            Need(f.Bridge.Poll()==EnvironmentCaptureBridgeResult.ScopeChanged&&f.Policy.Snapshot().TopologyGeneration==1&&!f.Policy.Snapshot().EnvironmentKnown&&!f.Policy.Snapshot().CanRun,"Barrier ABA passed scope checks.");
        });
        Test("other policy revision changes conservatively require a new observation",()=>{
            var f=new Fixture();f.Query.At=(key,count)=>{if(key=="rect"&&count==1)f.Policy.Observe("right",1,1,false,true,false,true);};
            Need(f.Bridge.Poll()==EnvironmentCaptureBridgeResult.ScopeChanged&&!f.Policy.Snapshot().EnvironmentKnown,"Policy changed inside a supposedly frozen scope.");
            f.Query.At=null;f.Bridge.Poll();Need(f.Policy.Snapshot().EnvironmentKnown,"Stable next observation could not recover.");
        });
        Test("topology replacement resets only policy serial while bridge serial never repeats",()=>{
            var f=new Fixture();f.Bridge.Poll();f.Policy.ReplaceTopology(2,Pair());Need(!f.Policy.Snapshot().EnvironmentKnown&&f.Policy.Snapshot().EnvironmentCaptureSerial==0,"New topology inherited Known.");
            f.Bridge.Poll();Need(f.Bridge.LastPollSerial==2&&f.Policy.Snapshot().EnvironmentCaptureSerial==2&&f.Bridge.LastCaptureTopology==2,"Bridge serial reset with topology.");
        });
        Test("unknown and recovery invalidate old activation scopes without issuing authority",()=>{
            var f=new Fixture();f.Bridge.Poll();var old=f.Policy.Snapshot();f.Query.Active=false;f.Bridge.Poll();var blocked=f.Policy.Snapshot();f.Query.Active=true;f.Bridge.Poll();var recovered=f.Policy.Snapshot();
            Need(blocked.InputEpoch==old.InputEpoch+1&&recovered.InputEpoch==blocked.InputEpoch+1&&!f.Policy.CanActivate("left",1,1,old.InputEpoch)&&!f.Policy.CanActivate("left",1,1,blocked.InputEpoch)&&f.Policy.CanActivate("left",1,1,recovered.InputEpoch),"Recovery accepted an old activation epoch.");
        });
        Test("raw query exceptions become fixed Unknown reasons and later polls recover",()=>{
            var f=new Fixture();f.Query.ThrowAt="rect#1";Need(f.Bridge.Poll()==EnvironmentCaptureBridgeResult.Unavailable&&f.Bridge.LastReason==OutputEnvironmentReason.QueryFailed&&!f.Policy.Snapshot().EnvironmentKnown,"Raw exception leaked Known.");
            f.Query.ThrowAt=null;f.Bridge.Poll();Need(f.Policy.Snapshot().EnvironmentKnown&&f.Bridge.LastPollSerial==2,"Exception left poll guard stuck.");
        });
        Test("wrong bridge thread rejects before serial allocation or raw queries",()=>{
            var f=new Fixture();Exception seen=null;var thread=new Thread(()=>{try{f.Bridge.Poll();}catch(Exception error){seen=error;}});thread.Start();thread.Join();
            Need(seen is InvalidOperationException&&f.Query.Calls.Count==0&&f.Bridge.LastPollSerial==0&&!f.Policy.Snapshot().EnvironmentKnown,"Wrong thread mutated scope or queried environment.");
        });
        Test("wrong-thread capture object remains Unknown even when bridge thread is valid",()=>{
            var p=new OutputPolicy();p.ReplaceTopology(1,new[]{Right()});var q=new Queries();OutputEnvironmentCapture capture=null;
            var thread=new Thread(()=>capture=new OutputEnvironmentCapture(q,100,7,q.Awareness));thread.Start();thread.Join();var b=new EnvironmentCaptureBridge(p,capture);
            Need(b.Poll()==EnvironmentCaptureBridgeResult.Unavailable&&b.LastReason==OutputEnvironmentReason.WrongThread&&q.Calls.Count==0&&!p.Snapshot().EnvironmentKnown,"Capture object crossed its owner thread.");
        });
        Test("caught reentrant polling is refused without allocating an inner serial",()=>{
            var f=new Fixture();bool rejected=false;f.Query.At=(key,count)=>{if(key=="rect"&&count==1){try{f.Bridge.Poll();}catch(InvalidOperationException){rejected=true;}}};
            Need(f.Bridge.Poll()==EnvironmentCaptureBridgeResult.Applied&&rejected&&f.Bridge.LastPollSerial==1&&f.Query.Count("rect")==2,"Reentry performed nested sampling or consumed serial.");
        });
        Test("uncaught reentry is captured as Unknown and does not leave the guard set",()=>{
            var f=new Fixture();f.Query.At=(key,count)=>{if(key=="rect"&&count==1)f.Bridge.Poll();};
            Need(f.Bridge.Poll()==EnvironmentCaptureBridgeResult.Unavailable&&f.Bridge.LastReason==OutputEnvironmentReason.QueryFailed&&!f.Policy.Snapshot().EnvironmentKnown,"Reentrant failure became Known.");
            f.Query.At=null;f.Bridge.Poll();Need(f.Policy.Snapshot().EnvironmentKnown&&f.Bridge.LastPollSerial==2,"Reentry failure poisoned later sampling.");
        });
        Test("capture capacity and overlapping policy declarations fail before raw queries",()=>{
            var limited=new Fixture(captureMaximum:1);Need(limited.Bridge.Poll()==EnvironmentCaptureBridgeResult.Unavailable&&limited.Bridge.LastReason==OutputEnvironmentReason.InvalidTopology&&limited.Query.Calls.Count==0&&!limited.Policy.Snapshot().EnvironmentKnown,"Capacity mismatch was partially sampled.");
            var overlap=new Fixture(new[]{Left(),new OutputDefinition("overlap",-100,0,1920,1080,96,96)});Need(overlap.Bridge.Poll()==EnvironmentCaptureBridgeResult.Unavailable&&overlap.Query.Calls.Count==0&&!overlap.Policy.Snapshot().EnvironmentKnown,"Overlapping declarations were accepted.");
        });
        Test("bridge exposes no sample target or environment capability submission API",()=>{
            var type=typeof(EnvironmentCaptureBridge);Need(type.GetFields(BindingFlags.Public|BindingFlags.Instance).Length==0,"Public mutable host fields leaked.");
            foreach(var method in type.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly)){
                Need(method.GetParameters().Length==0,"Public method accepts caller observations or targets.");
                Need(method.ReturnType!=typeof(OutputEnvironmentSource)&&method.ReturnType!=typeof(OutputEnvironmentSample),"Environment capability or raw sample leaked.");
            }
        });
        Test("invalid construction leaves policy registration and raw queries untouched",()=>{
            var p=new OutputPolicy();var q=new Queries();var capture=new OutputEnvironmentCapture(q,100,7,q.Awareness);
            Reject<ArgumentNullException>(()=>new EnvironmentCaptureBridge(null,capture));Reject<ArgumentNullException>(()=>new EnvironmentCaptureBridge(p,null));
            Exception seen=null;var thread=new Thread(()=>{try{new EnvironmentCaptureBridge(p,capture);}catch(Exception error){seen=error;}});thread.Start();thread.Join();
            Need(seen is InvalidOperationException&&!p.Snapshot().EnvironmentRegistered&&q.Calls.Count==0,"Invalid construction registered a source or queried environment.");
        });
        Test("poll serial overflow throws before capture and does not wrap or reuse a serial",()=>{
            var f=new Fixture();typeof(EnvironmentCaptureBridge).GetField("lastPollSerial",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(f.Bridge,Int64.MaxValue);
            Reject<OverflowException>(()=>f.Bridge.Poll());Need(f.Bridge.LastPollSerial==Int64.MaxValue&&f.Query.Calls.Count==0&&!f.Policy.Snapshot().EnvironmentKnown,"Serial overflow wrapped or touched capture.");
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
}
