using System;
using System.Collections.Generic;
using System.Threading;
using FishingDesktop.Diagnostics.OutputTopology;

static class OutputTopologyTests
{
    static int passed,failed;
    static void Need(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    static void Reject<T>(Action action) where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
    static RawMonitor Left(){return new RawMonitor(101,-1920,-120,1920,1080,false);}
    static RawMonitor Right(){return new RawMonitor(202,0,0,2560,1440,true);}
    static RawMonitor[] Pair(){return new[]{Left(),Right()};}
    static DpiProbeResult Good(uint dpi){return new DpiProbeResult(dpi,true,true,true,true,true,true);}
    static IEnumerable<RawMonitor> Interrupted(){yield return Left();throw new InvalidOperationException("Private native detail must not escape.");}
    static IEnumerable<RawMonitor> Endless(){while(true)yield return Right();}
    sealed class Fake : ITopologyQuery
    {
        internal int Enumerations,Measurements,AwarenessReads;
        internal Func<int,bool> Awareness=delegate{return true;};
        internal Func<int,IEnumerable<RawMonitor>> Read=delegate{return Pair();};
        internal Func<int,RawMonitor,DpiProbeResult> Dpi=delegate(int call,RawMonitor row){return Good(row.Handle==101?120u:144u);};
        public bool IsPerMonitorV2{get{return Awareness(++AwarenessReads);}}
        public IEnumerable<RawMonitor> Enumerate(){return Read(++Enumerations);}
        public DpiProbeResult Measure(RawMonitor monitor){return Dpi(++Measurements,monitor);}
    }
    static void Unknown(TopologySample sample,TopologyUnavailableReason reason)
    {Need(!sample.Known&&sample.Reason==reason&&sample.Outputs.Count==0,"Unavailable capture exposed usable geometry or wrong reason: "+sample.Reason);}
    static void RawRejected(string name,Func<IEnumerable<RawMonitor>> rows,TopologyUnavailableReason reason)
    {Test(name,()=>{var fake=new Fake{Read=delegate{return rows();}};Unknown(new TopologyCapture(fake).Capture(),reason);Need(fake.Measurements==0,"Invalid descriptors reached HWND measurement.");});}
    static void SafetyRejected(string name,DpiProbeResult result,TopologyUnavailableReason reason)
    {Test(name,()=>{var fake=new Fake{Dpi=delegate{return result;}};Unknown(new TopologyCapture(fake).Capture(),reason);Need(fake.Measurements==1,"Failed measurement did not stop capture.");});}
    static int Main()
    {
        Test("stable synthetic negative coordinates and distinct effective DPI are retained",()=>{
            var fake=new Fake();var sample=new TopologyCapture(fake).Capture();
            Need(sample.Known&&sample.Reason==TopologyUnavailableReason.None&&sample.Outputs.Count==2,"Stable capture unavailable.");
            var left=sample.Outputs[0];var right=sample.Outputs[1];
            Need(left.Key=="output-1"&&left.X==-1920&&left.Y==-120&&left.Width==1920&&left.Height==1080&&!left.Primary&&left.DpiX==120&&left.DpiY==120,"Negative output changed.");
            Need(right.Key=="output-2"&&right.Primary&&right.DpiX==144&&right.DpiY==144&&fake.Enumerations==3&&fake.Measurements==4,"Capture did not fully bracket two DPI passes.");
        });
        Test("enumeration ordering has no effect on logical aliases or stability",()=>{
            var fake=new Fake{Read=call=>call%2==0?new[]{Right(),Left()}:Pair()};
            Need(new TopologyCapture(fake).Capture().Known,"Enumeration reorder looked like changed topology.");
        });
        Test("capture serial increases while unchanged topology remains ordinary observation",()=>{
            var fake=new Fake();var capture=new TopologyCapture(fake);var first=capture.Capture();var second=capture.Capture();
            Need(first.Known&&second.Known&&first.CaptureSerial==1&&second.CaptureSerial==2&&fake.Measurements==8,"A new capture reused prior observations.");
        });
        Test("wrong thread returns unavailable without touching the query",()=>{
            var fake=new Fake();var capture=new TopologyCapture(fake);TopologySample sample=null;
            var thread=new Thread(()=>sample=capture.Capture());thread.Start();thread.Join();Unknown(sample,TopologyUnavailableReason.WrongThread);
            Need(fake.Enumerations==0&&fake.Measurements==0&&fake.AwarenessReads==0,"Wrong thread accessed native boundary.");
        });
        Test("unconfirmed PMv2 stops before enumeration",()=>{
            var fake=new Fake{Awareness=delegate{return false;}};Unknown(new TopologyCapture(fake).Capture(),TopologyUnavailableReason.AwarenessUnconfirmed);
            Need(fake.Enumerations==0&&fake.Measurements==0,"Unconfirmed awareness reached monitor queries.");
        });
        Test("lost PMv2 after final enumeration rejects the entire sample",()=>{
            var fake=new Fake{Awareness=call=>call==1};Unknown(new TopologyCapture(fake).Capture(),TopologyUnavailableReason.AwarenessUnconfirmed);
        });
        RawRejected("null enumeration is unavailable",()=>null,TopologyUnavailableReason.EnumerationFailed);
        RawRejected("throwing enumeration is unavailable",()=>{throw new InvalidOperationException("Sensitive native error.");},TopologyUnavailableReason.EnumerationFailed);
        RawRejected("deferred enumeration failure discards partial descriptors",()=>Interrupted(),TopologyUnavailableReason.EnumerationFailed);
        RawRejected("empty topology cannot supply primary defaults",()=>new RawMonitor[0],TopologyUnavailableReason.EmptyTopology);
        RawRejected("null monitor is invalid",()=>new RawMonitor[]{Right(),null},TopologyUnavailableReason.InvalidTopology);
        RawRejected("zero monitor handle is invalid",()=>new[]{new RawMonitor(0,0,0,1920,1080,true)},TopologyUnavailableReason.InvalidTopology);
        RawRejected("duplicate monitor handle is invalid even at separate coordinates",()=>new[]{Right(),new RawMonitor(202,-1920,0,1920,1080,false)},TopologyUnavailableReason.InvalidTopology);
        RawRejected("duplicate rectangles are ambiguous",()=>new[]{Right(),new RawMonitor(303,0,0,2560,1440,false)},TopologyUnavailableReason.InvalidTopology);
        RawRejected("partially overlapping rectangles are ambiguous",()=>new[]{Right(),new RawMonitor(303,-100,0,200,1080,false)},TopologyUnavailableReason.InvalidTopology);
        RawRejected("no primary is invalid",()=>new[]{Left()},TopologyUnavailableReason.InvalidTopology);
        RawRejected("multiple primaries are invalid",()=>new[]{Right(),new RawMonitor(303,-1920,0,1920,1080,true)},TopologyUnavailableReason.InvalidTopology);
        RawRejected("zero width is invalid",()=>new[]{new RawMonitor(202,0,0,0,1080,true)},TopologyUnavailableReason.InvalidTopology);
        RawRejected("negative height is invalid",()=>new[]{new RawMonitor(202,0,0,1920,-1080,true)},TopologyUnavailableReason.InvalidTopology);
        RawRejected("rectangle addition cannot overflow",()=>new[]{new RawMonitor(202,Int32.MaxValue,0,2,1080,true)},TopologyUnavailableReason.InvalidTopology);
        Test("configured monitor cap rejects extra descriptors before measurement",()=>{
            var fake=new Fake();Unknown(new TopologyCapture(fake,1).Capture(),TopologyUnavailableReason.InvalidTopology);Need(fake.Measurements==0,"Capacity overflow created a HWND.");
        });
        RawRejected("unbounded invalid enumeration is stopped by duplicate detection",()=>Endless(),TopologyUnavailableReason.InvalidTopology);
        Test("identity replacement between first and middle enumeration is rejected",()=>{
            var fake=new Fake{Read=call=>call==1?Pair():new[]{Left(),new RawMonitor(303,0,0,2560,1440,true)}};
            Unknown(new TopologyCapture(fake).Capture(),TopologyUnavailableReason.TopologyChanged);Need(fake.Measurements==2,"Changed topology reached second DPI pass.");
        });
        Test("geometry change is rejected",()=>{
            var fake=new Fake{Read=call=>call==1?Pair():new[]{Left(),new RawMonitor(202,0,0,1920,1080,true)}};
            Unknown(new TopologyCapture(fake).Capture(),TopologyUnavailableReason.TopologyChanged);
        });
        Test("primary change is rejected",()=>{
            var fake=new Fake{Read=call=>call==1?Pair():new[]{new RawMonitor(101,-1920,-120,1920,1080,true),new RawMonitor(202,0,0,2560,1440,false)}};
            Unknown(new TopologyCapture(fake).Capture(),TopologyUnavailableReason.TopologyChanged);
        });
        Test("topology change after second DPI pass rejects every result",()=>{
            var fake=new Fake{Read=call=>call<3?Pair():new[]{Right()}};
            Unknown(new TopologyCapture(fake).Capture(),TopologyUnavailableReason.TopologyChanged);Need(fake.Measurements==4,"Second DPI pass was not exercised.");
        });
        Test("final query failure cannot reuse middle descriptors",()=>{
            var fake=new Fake{Read=call=>call<3?Pair():null};Unknown(new TopologyCapture(fake).Capture(),TopologyUnavailableReason.EnumerationFailed);
        });
        Test("DPI change with unchanged geometry is rejected",()=>{
            var fake=new Fake{Dpi=(call,row)=>Good(call<=2?120u:144u)};Unknown(new TopologyCapture(fake).Capture(),TopologyUnavailableReason.DpiChanged);
        });
        SafetyRejected("zero DPI is never replaced with 96",Good(0),TopologyUnavailableReason.DpiUnavailable);
        SafetyRejected("DPI cannot overflow downstream signed values",Good(UInt32.MaxValue),TopologyUnavailableReason.DpiUnavailable);
        SafetyRejected("null measurement is unavailable",null,TopologyUnavailableReason.DpiUnavailable);
        SafetyRejected("foreign or unconfirmed window is rejected",new DpiProbeResult(120,false,true,true,true,true,true),TopologyUnavailableReason.WindowSafetyUnconfirmed);
        SafetyRejected("visible own window cannot qualify",new DpiProbeResult(120,true,false,true,true,true,true),TopologyUnavailableReason.WindowSafetyUnconfirmed);
        SafetyRejected("missing NoActivate cannot qualify",new DpiProbeResult(120,true,true,false,true,true,true),TopologyUnavailableReason.WindowSafetyUnconfirmed);
        SafetyRejected("non-PMv2 window cannot qualify",new DpiProbeResult(120,true,true,true,false,true,true),TopologyUnavailableReason.AwarenessUnconfirmed);
        SafetyRejected("wrong monitor placement cannot qualify",new DpiProbeResult(120,true,true,true,true,false,true),TopologyUnavailableReason.WindowSafetyUnconfirmed);
        SafetyRejected("unconfirmed HWND release rejects even a positive DPI",new DpiProbeResult(120,true,true,true,true,true,false),TopologyUnavailableReason.CleanupUnconfirmed);
        Test("measurement exception is a fixed unavailable reason",()=>{
            var fake=new Fake{Dpi=delegate{throw new InvalidOperationException("Private native error.");}};Unknown(new TopologyCapture(fake).Capture(),TopologyUnavailableReason.QueryFailed);
        });
        Test("a failed later capture never reuses an earlier positive sample",()=>{
            var fake=new Fake();var capture=new TopologyCapture(fake);var prior=capture.Capture();fake.Read=delegate{return null;};var next=capture.Capture();
            Need(prior.Known&&prior.Outputs.Count==2&&prior.CaptureSerial==1,"Prior immutable evidence changed.");Unknown(next,TopologyUnavailableReason.EnumerationFailed);Need(next.CaptureSerial==2,"Failed observation lost its serial.");
        });
        Test("a later capture samples fresh DPI even when descriptors did not change",()=>{
            var fake=new Fake();var capture=new TopologyCapture(fake);var prior=capture.Capture();fake.Dpi=delegate{return Good(192);};var next=capture.Capture();
            Need(prior.Outputs[0].DpiX==120&&next.Known&&next.Outputs[0].DpiX==192&&fake.Measurements==8,"DPI cache survived between captures.");
        });
        Test("known sample copies its collection and exposes read-only rows",()=>{
            var rows=new List<TopologyOutput>{new TopologyOutput("OUT",-1920,0,1920,1080,120,120,true)};var sample=TopologySample.CreateKnown(1,rows);rows.Clear();
            Need(sample.Outputs.Count==1&&sample.Outputs[0].Key=="out","Mutable input altered snapshot.");Reject<NotSupportedException>(()=>((IList<TopologyOutput>)sample.Outputs).Clear());
        });
        Test("declared known factory rejects invalid aliases and ambiguous collections",()=>{
            Reject<ArgumentException>(()=>new TopologyOutput("../physical",0,0,1,1,96,96,true));
            Reject<ArgumentException>(()=>TopologySample.CreateKnown(1,new TopologyOutput[0]));
            var row=new TopologyOutput("one",0,0,1,1,96,96,true);Reject<ArgumentException>(()=>TopologySample.CreateKnown(1,new[]{row,row}));
            Reject<ArgumentOutOfRangeException>(()=>new TopologyOutput("one",0,0,1,1,0,0,true));
        });
        Test("unavailable factory requires a fixed non-success reason and positive serial",()=>{
            Reject<ArgumentOutOfRangeException>(()=>TopologySample.CreateUnavailable(1,TopologyUnavailableReason.None));
            Reject<ArgumentOutOfRangeException>(()=>TopologySample.CreateUnavailable(1,(TopologyUnavailableReason)999));
            Reject<ArgumentOutOfRangeException>(()=>TopologySample.CreateUnavailable(0,TopologyUnavailableReason.QueryFailed));
        });
        Test("capture constructor rejects missing boundary and invalid cap",()=>{
            Reject<ArgumentNullException>(()=>new TopologyCapture(null));Reject<ArgumentOutOfRangeException>(()=>new TopologyCapture(new Fake(),0));Reject<ArgumentOutOfRangeException>(()=>new TopologyCapture(new Fake(),65));
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed; synthetic query only, no native HWND or display settings.");return failed==0?0:1;
    }
}
