using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using FishingDesktop.Diagnostics.MultiOutput;

static class EnvironmentPolicyTests
{
    static int passed,failed;
    static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Reject<T>(Action action) where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
    static OutputDefinition D(string key){return new OutputDefinition(key,key=="left"?-1920:0,0,1920,1080,96,96);}
    static OutputEnvironmentCoverage C(string key,bool full=false){return new OutputEnvironmentCoverage(key,full);}
    static OutputEnvironmentCoverage[] Both(bool left=false,bool right=false){return new[]{C("left",left),C("right",right)};}
    static OutputState Row(OutputPolicyState s,string key){foreach(var row in s.Outputs)if(row.Definition.Key==key)return row;throw new InvalidOperationException("Missing output.");}
    static void Ready(OutputPolicy p,string key,long topology=1,long view=1,bool full=false){Need(p.BindView(key,topology,view)&&p.Observe(key,topology,view,true,true,full,true),"Could not bind and observe current output.");}
    static OutputPolicy Plain()
    {
        var p=new OutputPolicy(4);p.ReplaceTopology(1,new[]{D("left"),D("right")});Ready(p,"left");Ready(p,"right");p.SetGlobal(OutputGlobalBlock.Startup,false);p.SelectControlTarget("left");return p;
    }
    static OutputPolicy Running(out OutputEnvironmentSource source)
    {var p=Plain();source=p.RegisterEnvironmentSource();Need(Known(p,source,1)==EnvironmentBatchResult.Applied,"Initial environment did not clear its barrier.");return p;}
    static EnvironmentBatchResult Known(OutputPolicy p,OutputEnvironmentSource source,long serial,bool left=false,bool right=false,long topology=1)
    {return p.ApplyEnvironmentBatch(source,topology,serial,true,Both(left,right));}
    static EnvironmentBatchResult Unknown(OutputPolicy p,OutputEnvironmentSource source,long serial,OutputGlobalBlock blocks=OutputGlobalBlock.None,long topology=1)
    {return p.ApplyEnvironmentBatch(source,topology,serial,false,null,blocks);}
    static void SameQualification(OutputPolicyState a,OutputPolicyState b)
    {
        Need(a.Revision==b.Revision&&a.InputEpoch==b.InputEpoch&&a.TopologyGeneration==b.TopologyGeneration&&a.GlobalBlocks==b.GlobalBlocks&&a.CanRun==b.CanRun&&a.ControlEligible==b.ControlEligible,"Qualification changed unexpectedly.");
    }
    static IEnumerable<OutputEnvironmentCoverage> Interrupted(){yield return C("left",true);throw new InvalidOperationException("Scripted host iterator failure.");}
    static IEnumerable<OutputEnvironmentCoverage> DisposeFailure(){try{yield return C("left",true);yield return C("right",false);}finally{throw new InvalidOperationException("Scripted iterator disposal failure.");}}
    static IEnumerable<OutputEnvironmentCoverage> ThrowBeforeRead(Action read){read();yield return null;throw new InvalidOperationException("Batch should not enumerate.");}
    static IEnumerable<OutputEnvironmentCoverage> Endless(Action read){read();yield return C("left",true);read();yield return C("right",false);while(true){read();yield return C("left",false);}}
    static IEnumerable<OutputEnvironmentCoverage> Reentrant(Action mutate,bool fail=false)
    {yield return C("left",true);mutate();if(fail)throw new InvalidOperationException("Interrupted after reentrant update.");yield return C("right",false);}
    static IEnumerable<OutputDefinition> TopologyDuringMoveNext(Action change){yield return D("left");change();yield return D("right");}
    sealed class TopologyOnDispose : IEnumerable<OutputDefinition>
    {
        readonly Action change;
        internal TopologyOnDispose(Action change){this.change=change;}
        public IEnumerator<OutputDefinition> GetEnumerator(){return new Iterator(change);}
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator(){return GetEnumerator();}
        sealed class Iterator : IEnumerator<OutputDefinition>
        {
            readonly Action change;int index=-1;bool disposed;
            internal Iterator(Action change){this.change=change;}
            public OutputDefinition Current{get{return D(index==0?"left":"right");}}
            object System.Collections.IEnumerator.Current{get{return Current;}}
            public bool MoveNext(){return ++index<2;}
            public void Reset(){throw new NotSupportedException();}
            public void Dispose(){if(disposed)return;disposed=true;change();}
        }
    }
    static void BadKnown(string name,Func<IEnumerable<OutputEnvironmentCoverage>> rows)
    {
        Test(name,()=>{
            OutputEnvironmentSource source;var p=Running(out source);Known(p,source,2,false,true);var before=p.Snapshot();
            Need(p.ApplyEnvironmentBatch(source,1,3,true,rows())==EnvironmentBatchResult.Rejected,"Malformed current Known was accepted.");var after=p.Snapshot();
            Need(!after.EnvironmentKnown&&after.EnvironmentCaptureSerial==3&&after.GlobalBlocks==OutputGlobalBlock.EnvironmentUnknown&&!after.CanRun&&after.Revision==before.Revision+1,"Malformed batch did not atomically establish Unknown.");
            Need(!Row(after,"left").EnvironmentFullscreen&&Row(after,"right").EnvironmentFullscreen&&Row(after,"left").Ready&&Row(after,"right").Ready,"Malformed batch partially replaced coverage or view readiness.");
            Need(Known(p,source,2)==EnvironmentBatchResult.Stale&&!p.Snapshot().EnvironmentKnown,"Older positive batch cleared rejected observation.");
        });
    }
    // Test-only overflow injection, never compiled into a native/UI diagnostic.
    static void Field(OutputPolicy p,string name,long value){var field=typeof(OutputPolicy).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);Need(field!=null,"Missing overflow test field.");field.SetValue(p,value);}
    static int Main()
    {
        Test("registering the reserved environment source atomically blocks ready outputs",()=>{
            var p=Plain();var before=p.Snapshot();p.RegisterEnvironmentSource();var after=p.Snapshot();
            Need(after.EnvironmentRegistered&&!after.EnvironmentKnown&&after.EnvironmentCaptureSerial==0&&after.GlobalBlocks==OutputGlobalBlock.EnvironmentUnknown&&!after.CanRun&&after.Revision==before.Revision+1&&after.InputEpoch==before.InputEpoch+1,"Reserved source registration was not one blocking commit.");
            Need(Row(after,"left").Ready&&!Row(after,"left").EnvironmentFullscreen,"Registration invented coverage or changed a view.");
        });
        Test("no environment registration preserves the legacy view-only behavior",()=>{
            var p=Plain();p.Observe("left",1,1,true,true,true,true);Need(Row(p.Snapshot(),"left").ViewFullscreen&&!Row(p.Snapshot(),"left").EnvironmentFullscreen,"Legacy fullscreen changed side.");
            Ready(p,"left",1,2);var s=p.Snapshot();Need(!s.EnvironmentRegistered&&!s.EnvironmentKnown&&s.EnvironmentCaptureSerial==0&&s.GlobalBlocks==OutputGlobalBlock.None&&!Row(s,"left").Fullscreen&&s.ControlEligible,"Unregistered environment altered legacy rebinding.");
        });
        Test("reserved environment registration is unique and does not consume generic source capacity",()=>{
            var p=new OutputPolicy(4,1);p.RegisterGlobalSource(OutputGlobalBlock.Manual);var source=p.RegisterEnvironmentSource();var before=p.Snapshot();
            Reject<InvalidOperationException>(()=>p.RegisterEnvironmentSource());Reject<InvalidOperationException>(()=>p.RegisterGlobalSource(OutputGlobalBlock.Manual));SameQualification(before,p.Snapshot());
            Need(source!=null&&p.Snapshot().EnvironmentRegistered,"Unique registration failed beside a full generic pool.");
        });
        Test("reserved vote survives wide legacy clears and ordinary source clears",()=>{
            var p=Plain();var generic=p.RegisterGlobalSource(OutputGlobalBlock.EnvironmentUnknown,OutputGlobalBlock.EnvironmentUnknown);p.RegisterEnvironmentSource();
            p.ReplaceGlobal((OutputGlobalBlock)127,OutputGlobalBlock.None);p.ReplaceGlobalForSource(generic,OutputGlobalBlock.None);
            Need(p.Snapshot().GlobalBlocks==OutputGlobalBlock.EnvironmentUnknown&&!p.Snapshot().EnvironmentKnown&&!p.Snapshot().CanRun,"Another API force-cleared the reserved vote.");
        });
        Test("environment registration hidden by a legacy vote is retained without churn",()=>{
            var p=Plain();p.SetGlobal(OutputGlobalBlock.EnvironmentUnknown,true);var before=p.Snapshot();p.RegisterEnvironmentSource();SameQualification(before,p.Snapshot());
            p.SetGlobal(OutputGlobalBlock.EnvironmentUnknown,false);Need(!p.Snapshot().CanRun&&p.Snapshot().EnvironmentRegistered&&!p.Snapshot().EnvironmentKnown,"Hidden reserved vote was not stored.");
        });
        Test("known coverage includes unbound outputs and does not fabricate readiness",()=>{
            var p=new OutputPolicy(4);var source=p.RegisterEnvironmentSource();p.ReplaceTopology(1,new[]{D("left"),D("right")});p.SetGlobal(OutputGlobalBlock.Startup,false);
            Need(Known(p,source,1,false,true)==EnvironmentBatchResult.Applied,"Complete unbound coverage rejected.");var s=p.Snapshot();
            Need(s.EnvironmentKnown&&!s.CanRun&&!Row(s,"left").Bound&&!Row(s,"right").Ready&&Row(s,"right").EnvironmentFullscreen,"Coverage fabricated view state.");
            Ready(p,"left");Ready(p,"right");Need(p.Snapshot().CanRun&&!Row(p.Snapshot(),"right").CanDraw&&Row(p.Snapshot(),"right").EnvironmentFullscreen,"Rebinding cleared unbound environment coverage.");
        });
        Test("omitting an unbound output cannot clear the environment barrier",()=>{
            var p=Plain();p.RetireView("right",1,1);var source=p.RegisterEnvironmentSource();var before=p.Snapshot();
            Need(p.ApplyEnvironmentBatch(source,1,1,true,new[]{C("left",true)})==EnvironmentBatchResult.Rejected&&!p.Snapshot().EnvironmentKnown&&p.Snapshot().EnvironmentCaptureSerial==1,"Only bound views were treated as complete coverage.");
            SameQualification(before,p.Snapshot());Need(!Row(p.Snapshot(),"left").EnvironmentFullscreen&&!Row(p.Snapshot(),"right").Bound,"Incomplete batch changed a covered or unbound output.");
        });
        Test("two-output coverage swap commits one revision and one target recovery epoch",()=>{
            OutputEnvironmentSource source;var p=Running(out source);Known(p,source,2,true,false);var before=p.Snapshot();Need(!before.ControlEligible&&before.CanRun,"Swap baseline wrong.");
            Known(p,source,3,false,true);var after=p.Snapshot();Need(after.Revision==before.Revision+1&&after.InputEpoch==before.InputEpoch+1&&after.CanRun&&after.ControlEligible,"Swap was partially published or advanced epoch more than once.");
            Need(!Row(after,"left").EnvironmentFullscreen&&Row(after,"right").EnvironmentFullscreen&&Row(before,"left").EnvironmentFullscreen&&!Row(before,"right").EnvironmentFullscreen,"Swap mutated earlier evidence or mixed rows.");
        });
        Test("reverse coverage swap revokes the target once while another output remains usable",()=>{
            OutputEnvironmentSource source;var p=Running(out source);Known(p,source,2,false,true);var before=p.Snapshot();Known(p,source,3,true,false);var after=p.Snapshot();
            Need(after.Revision==before.Revision+1&&after.InputEpoch==before.InputEpoch+1&&after.CanRun&&!after.ControlEligible&&!p.CanActivate("left",1,1,before.InputEpoch),"Target block did not atomically revoke once.");
        });
        Test("non-target coverage changes preserve the healthy target input scope",()=>{
            OutputEnvironmentSource source;var p=Running(out source);var before=p.Snapshot();Known(p,source,2,false,true);var after=p.Snapshot();
            Need(after.Revision==before.Revision+1&&after.InputEpoch==before.InputEpoch&&p.CanActivate("left",1,1,before.InputEpoch)&&Row(after,"right").ViewGeneration==1&&Row(after,"right").Ready,"Non-target coverage revoked target or changed view lifecycle.");
        });
        Test("page false cannot clear environment true coverage",()=>{
            OutputEnvironmentSource source;var p=Running(out source);Known(p,source,2,true,false);var before=p.Snapshot();p.Observe("left",1,1,true,true,false,true);var after=p.Snapshot();
            Need(Row(after,"left").EnvironmentFullscreen&&!Row(after,"left").ViewFullscreen&&Row(after,"left").Fullscreen&&!after.ControlEligible&&after.InputEpoch==before.InputEpoch,"Page cleared native-side coverage.");
        });
        Test("environment false cannot clear page true coverage",()=>{
            OutputEnvironmentSource source;var p=Running(out source);p.Observe("left",1,1,true,true,true,true);var before=p.Snapshot();Known(p,source,2,false,false);var after=p.Snapshot();
            Need(Row(after,"left").ViewFullscreen&&!Row(after,"left").EnvironmentFullscreen&&Row(after,"left").Fullscreen&&!after.ControlEligible&&after.InputEpoch==before.InputEpoch,"Environment cleared view-side fullscreen.");
        });
        Test("underlying fullscreen sides change without spurious effective target epochs",()=>{
            OutputEnvironmentSource source;var p=Running(out source);p.Observe("left",1,1,true,true,true,true);var before=p.Snapshot();Known(p,source,2,true,false);var both=p.Snapshot();
            Need(Row(both,"left").ViewFullscreen&&Row(both,"left").EnvironmentFullscreen&&both.Revision==before.Revision+1&&both.InputEpoch==before.InputEpoch,"Hidden environment side caused a duplicate target revoke.");
            p.Observe("left",1,1,true,true,false,true);Need(Row(p.Snapshot(),"left").Fullscreen&&p.Snapshot().InputEpoch==before.InputEpoch,"Clearing only one side restored target.");
        });
        Test("retire and replacement bind retain environment coverage across the view gap",()=>{
            OutputEnvironmentSource source;var p=Running(out source);Known(p,source,2,true,false);p.RetireView("left",1,1);var retired=p.Snapshot();
            Need(!Row(retired,"left").Bound&&Row(retired,"left").EnvironmentFullscreen,"Retire lost environment coverage.");Ready(p,"left",1,2);var rebound=p.Snapshot();
            Need(Row(rebound,"left").Ready&&Row(rebound,"left").EnvironmentFullscreen&&!Row(rebound,"left").ViewFullscreen&&!rebound.ControlEligible,"Replacement view reopened a covered target.");
            Need(!p.Observe("left",1,1,true,true,false,true)&&!p.RetireView("left",1,1)&&Row(p.Snapshot(),"left").EnvironmentFullscreen,"Old view scope cleared current coverage.");
        });
        Test("known batch changes only environment fields and its own vote",()=>{
            OutputEnvironmentSource source;var p=Running(out source);p.Observe("right",1,1,false,false,true,false);var before=p.Snapshot();Known(p,source,2,false,true);var after=p.Snapshot();
            var a=Row(before,"right");var b=Row(after,"right");Need(a.ViewGeneration==b.ViewGeneration&&a.Bound==b.Bound&&a.Ready==b.Ready&&a.Landscape==b.Landscape&&a.ViewFullscreen==b.ViewFullscreen&&a.RendererAvailable==b.RendererAvailable&&b.EnvironmentFullscreen,"Batch overwrote view or renderer observations.");
        });
        Test("unknown retains previous local coverage while blocking the entire environment",()=>{
            OutputEnvironmentSource source;var p=Running(out source);Known(p,source,2,false,true);var before=p.Snapshot();Need(Unknown(p,source,3)==EnvironmentBatchResult.Unavailable,"Unknown not accepted.");var after=p.Snapshot();
            Need(!after.EnvironmentKnown&&after.EnvironmentCaptureSerial==3&&after.GlobalBlocks==OutputGlobalBlock.EnvironmentUnknown&&!after.CanRun&&Row(after,"right").EnvironmentFullscreen&&!Row(after,"left").EnvironmentFullscreen&&after.Revision==before.Revision+1,"Unknown cleared coverage or failed to block globally.");
        });
        Test("unknown may add session unavailable without touching independent native power or manual votes",()=>{
            OutputEnvironmentSource source;var p=Running(out source);var native=p.RegisterGlobalSource(OutputGlobalBlock.SessionUnavailable|OutputGlobalBlock.PowerSuspended,OutputGlobalBlock.SessionUnavailable|OutputGlobalBlock.PowerSuspended);p.SetGlobal(OutputGlobalBlock.Manual,true);
            Unknown(p,source,2,OutputGlobalBlock.SessionUnavailable);Known(p,source,3);Need(p.Snapshot().EnvironmentKnown&&p.Snapshot().GlobalBlocks==(OutputGlobalBlock.Manual|OutputGlobalBlock.SessionUnavailable|OutputGlobalBlock.PowerSuspended),"Known cleared another source's barrier.");
            p.ReplaceGlobalForSource(native,OutputGlobalBlock.None);Need(p.Snapshot().GlobalBlocks==OutputGlobalBlock.Manual,"Native source recovery cleared legacy Manual.");
        });
        Test("unknown never enumerates unusable coverage payloads",()=>{
            OutputEnvironmentSource source;var p=Running(out source);int reads=0;
            Need(p.ApplyEnvironmentBatch(source,1,2,false,ThrowBeforeRead(()=>reads++))==EnvironmentBatchResult.Unavailable&&reads==0&&!p.Snapshot().EnvironmentKnown,"Unknown enumerated irrelevant coverage.");
        });
        Test("late known cannot restore a newer unknown or consume its capture high-water",()=>{
            OutputEnvironmentSource source;var p=Running(out source);Unknown(p,source,9);var before=p.Snapshot();
            Need(Known(p,source,8)==EnvironmentBatchResult.Stale&&Known(p,source,9)==EnvironmentBatchResult.Stale,"Late known was not stale.");SameQualification(before,p.Snapshot());Need(p.Snapshot().EnvironmentCaptureSerial==9&&!p.Snapshot().EnvironmentKnown,"Late sample changed the current observation.");
        });
        Test("late unknown cannot poison a newer known",()=>{
            OutputEnvironmentSource source;var p=Running(out source);Known(p,source,9);var before=p.Snapshot();Need(Unknown(p,source,8)==EnvironmentBatchResult.Stale,"Late Unknown was accepted.");SameQualification(before,p.Snapshot());Need(p.Snapshot().EnvironmentKnown&&p.Snapshot().EnvironmentCaptureSerial==9,"Late Unknown altered environment state.");
        });
        Test("new identical known samples update serial without qualification churn",()=>{
            OutputEnvironmentSource source;var p=Running(out source);var before=p.Snapshot();Need(Known(p,source,2)==EnvironmentBatchResult.Unchanged,"Identical Known was treated as a policy replacement.");SameQualification(before,p.Snapshot());Need(p.Snapshot().EnvironmentCaptureSerial==2&&before.EnvironmentCaptureSerial==1,"Capture high-water not updated immutably.");
        });
        Test("repeated unknown updates only serial and fresh recovery rejects paused requests",()=>{
            OutputEnvironmentSource source;var p=Running(out source);Unknown(p,source,2);var before=p.Snapshot();Unknown(p,source,3);SameQualification(before,p.Snapshot());
            Need(p.Snapshot().EnvironmentCaptureSerial==3&&Known(p,source,4)==EnvironmentBatchResult.Applied&&!p.CanActivate("left",1,1,before.InputEpoch)&&p.Snapshot().ControlEligible,"Unknown recovery restored an earlier activation scope.");
        });
        Test("non-positive capture serials are stale without reading payload or mutating state",()=>{
            OutputEnvironmentSource source;var p=Running(out source);var before=p.Snapshot();int reads=0;
            foreach(long serial in new[]{0L,-1L,Int64.MinValue})Need(p.ApplyEnvironmentBatch(source,1,serial,true,ThrowBeforeRead(()=>reads++))==EnvironmentBatchResult.Stale,"Non-positive serial was accepted.");
            SameQualification(before,p.Snapshot());Need(reads==0&&p.Snapshot().EnvironmentCaptureSerial==1,"Bad serial touched coverage or high-water.");
        });
        Test("wrong past future or unconfigured topology is rejected before reading coverage",()=>{
            OutputEnvironmentSource source;var p=Running(out source);var before=p.Snapshot();int reads=0;
            foreach(long generation in new[]{0L,-1L,2L,Int64.MaxValue})Need(p.ApplyEnvironmentBatch(source,generation,99,true,ThrowBeforeRead(()=>reads++))==EnvironmentBatchResult.WrongTopology,"Wrong topology was accepted.");
            SameQualification(before,p.Snapshot());Need(reads==0&&p.Snapshot().EnvironmentCaptureSerial==1,"Wrong topology poisoned current serial.");
            var empty=new OutputPolicy();var token=empty.RegisterEnvironmentSource();Need(Known(empty,token,1,topology:0)==EnvironmentBatchResult.WrongTopology&&!empty.Snapshot().EnvironmentKnown,"Unconfigured topology became known.");
        });
        Test("topology replacement atomically restores unknown and blocks newly ready views",()=>{
            OutputEnvironmentSource source;var p=Running(out source);Known(p,source,8,false,true);var before=p.Snapshot();p.ReplaceTopology(2,new[]{D("left"),D("right")});var replaced=p.Snapshot();
            Need(replaced.Revision==before.Revision+1&&replaced.InputEpoch==before.InputEpoch+1&&!replaced.EnvironmentKnown&&replaced.EnvironmentCaptureSerial==0&&replaced.GlobalBlocks==OutputGlobalBlock.EnvironmentUnknown&&!Row(replaced,"left").Bound,"Topology did not atomically reset environment scope.");
            Ready(p,"left",2,2);Ready(p,"right",2,2);Need(!p.Snapshot().CanRun&&!p.Snapshot().ControlEligible,"Ready escaped the unknown environment gap.");
            Need(Known(p,source,99,topology:1)==EnvironmentBatchResult.WrongTopology&&p.Snapshot().EnvironmentCaptureSerial==0,"Old topology advanced the new high-water.");
            Need(Known(p,source,1,topology:2)==EnvironmentBatchResult.Applied&&p.Snapshot().CanRun&&!p.CanActivate("left",1,1,before.InputEpoch),"Fresh same-generation environment could not finish the gap safely.");
        });
        Test("capture serial high-water resets per topology even after the prior maximum serial",()=>{
            OutputEnvironmentSource source;var p=Running(out source);Known(p,source,Int64.MaxValue);p.ReplaceTopology(2,new[]{D("left"),D("right")});
            Need(p.Snapshot().EnvironmentCaptureSerial==0&&Known(p,source,Int64.MaxValue,topology:1)==EnvironmentBatchResult.WrongTopology,"Prior serial poisoned a new topology.");
            Ready(p,"left",2,2);Ready(p,"right",2,2);Need(Known(p,source,1,topology:2)==EnvironmentBatchResult.Applied&&p.Snapshot().EnvironmentCaptureSerial==1,"New topology did not have an independent serial scope.");
        });
        Test("rejected topology declarations do not reset environment known state or high-water",()=>{
            OutputEnvironmentSource source;var p=Running(out source);Known(p,source,9);var before=p.Snapshot();Reject<ArgumentException>(()=>p.ReplaceTopology(2,new[]{D("left"),D("LEFT")}));
            SameQualification(before,p.Snapshot());Need(p.Snapshot().EnvironmentKnown&&p.Snapshot().EnvironmentCaptureSerial==9,"Failed topology declaration invalidated current environment.");
        });
        Test("known coverage canonicalizes aliases and accepts arbitrary complete row order",()=>{
            OutputEnvironmentSource source;var p=Running(out source);Need(p.ApplyEnvironmentBatch(source,1,2,true,new[]{C("RIGHT",true),C("LEFT",false)})==EnvironmentBatchResult.Applied&&Row(p.Snapshot(),"right").EnvironmentFullscreen,"Complete canonical key set was not matched.");
        });
        BadKnown("null known coverage is conservatively unknown",()=>null);
        BadKnown("empty known coverage is conservatively unknown",()=>new OutputEnvironmentCoverage[0]);
        BadKnown("missing bound output is conservatively unknown",()=>new[]{C("left",true)});
        BadKnown("null known row is conservatively unknown",()=>new OutputEnvironmentCoverage[]{C("left",true),null});
        BadKnown("duplicate canonical known keys are conservatively unknown",()=>new[]{C("left",true),C("LEFT",false)});
        BadKnown("unknown known key is conservatively unknown",()=>new[]{C("left",true),C("other",false)});
        BadKnown("illegal known alias is conservatively unknown",()=>new[]{C("left",true),C("../right",false)});
        BadKnown("excess known rows never partially replace coverage",()=>new[]{C("left",true),C("right",false),C("third",false)});
        BadKnown("interrupted known enumeration never partially replaces coverage",()=>Interrupted());
        BadKnown("known enumeration disposal failure never partially replaces coverage",()=>DisposeFailure());
        Test("endless known enumeration is bounded by current output count plus one",()=>{
            OutputEnvironmentSource source;var p=Running(out source);int reads=0;Need(p.ApplyEnvironmentBatch(source,1,2,true,Endless(()=>reads++))==EnvironmentBatchResult.Rejected&&reads==3&&!p.Snapshot().EnvironmentKnown,"Endless enumeration escaped its bound.");
        });
        Test("invalid environment flags are isolated and cannot set unrelated global reasons",()=>{
            OutputEnvironmentSource source;var p=Running(out source);Need(p.ApplyEnvironmentBatch(source,1,2,true,Both(),OutputGlobalBlock.Manual)==EnvironmentBatchResult.Rejected&&p.Snapshot().GlobalBlocks==OutputGlobalBlock.EnvironmentUnknown,"Invalid environment flag altered Manual.");
            Need(p.ApplyEnvironmentBatch(source,1,3,false,null,OutputGlobalBlock.PowerSuspended)==EnvironmentBatchResult.Rejected&&p.Snapshot().GlobalBlocks==OutputGlobalBlock.EnvironmentUnknown,"Unknown altered native power vote.");
        });
        Test("contradictory known global blocks are rejected instead of declaring partial availability",()=>{
            foreach(var blocks in new[]{OutputGlobalBlock.EnvironmentUnknown,OutputGlobalBlock.SessionUnavailable}){
                OutputEnvironmentSource source;var p=Running(out source);Need(p.ApplyEnvironmentBatch(source,1,2,true,Both(),blocks)==EnvironmentBatchResult.Rejected&&!p.Snapshot().EnvironmentKnown&&!p.Snapshot().CanRun,"Contradictory Known cleared global environment safety.");
            }
        });
        Test("malformed known retains previous session unavailability and local coverage",()=>{
            OutputEnvironmentSource source;var p=Running(out source);Known(p,source,2,false,true);Unknown(p,source,3,OutputGlobalBlock.SessionUnavailable);
            Need(p.ApplyEnvironmentBatch(source,1,4,true,null)==EnvironmentBatchResult.Rejected&&p.Snapshot().GlobalBlocks==(OutputGlobalBlock.EnvironmentUnknown|OutputGlobalBlock.SessionUnavailable)&&Row(p.Snapshot(),"right").EnvironmentFullscreen,"Malformed recovery lost prior conservative state.");
        });
        Test("known cannot clear a distinct same-bit legacy or ordinary source vote",()=>{
            OutputEnvironmentSource source;var p=Running(out source);var other=p.RegisterGlobalSource(OutputGlobalBlock.EnvironmentUnknown,OutputGlobalBlock.EnvironmentUnknown);p.SetGlobal(OutputGlobalBlock.EnvironmentUnknown,true);Unknown(p,source,2);Known(p,source,3);
            Need(p.Snapshot().EnvironmentKnown&&p.Snapshot().GlobalBlocks==OutputGlobalBlock.EnvironmentUnknown&&!p.Snapshot().CanRun,"Own Known cleared another same-bit source.");
            p.SetGlobal(OutputGlobalBlock.EnvironmentUnknown,false);Need(!p.Snapshot().CanRun,"Legacy clear bypassed remaining source.");p.ReplaceGlobalForSource(other,OutputGlobalBlock.None);Need(p.Snapshot().CanRun,"Final independent source could not recover.");
        });
        Test("reserved token null unknown or foreign identities cannot mutate the active environment",()=>{
            OutputEnvironmentSource source,foreign;var p=Running(out source);Running(out foreign);var before=p.Snapshot();
            Reject<ArgumentNullException>(()=>p.ApplyEnvironmentBatch(null,1,2,true,Both()));Reject<ArgumentException>(()=>p.ApplyEnvironmentBatch(foreign,1,2,true,Both()));
            Reject<ArgumentException>(()=>p.ApplyEnvironmentBatch(new OutputEnvironmentSource(),1,2,true,Both()));SameQualification(before,p.Snapshot());Need(p.Snapshot().EnvironmentCaptureSerial==1&&p.Snapshot().EnvironmentKnown,"Foreign environment token changed current scope.");
        });
        Test("unregistered forged token cannot block or update an opt-out policy",()=>{
            var p=Plain();var before=p.Snapshot();Reject<ArgumentException>(()=>p.ApplyEnvironmentBatch(new OutputEnvironmentSource(),1,1,false,null));SameQualification(before,p.Snapshot());Need(!p.Snapshot().EnvironmentRegistered,"Unregistered token created a source.");
        });
        Test("wrong-thread registration or batch rejects before registration or payload reads",()=>{
            var plain=Plain();OutputEnvironmentSource source;var p=Running(out source);var before=p.Snapshot();int rejected=0,reads=0;var worker=new Thread(()=>{
                try{plain.RegisterEnvironmentSource();}catch(InvalidOperationException){rejected++;}
                try{p.ApplyEnvironmentBatch(source,1,2,true,ThrowBeforeRead(()=>reads++));}catch(InvalidOperationException){rejected++;}
            });worker.Start();worker.Join();Need(rejected==2&&reads==0&&!plain.Snapshot().EnvironmentRegistered,"Wrong thread changed source registration or read payload.");SameQualification(before,p.Snapshot());
        });
        Test("reentrant topology change cannot apply the outer old-generation batch",()=>{
            OutputEnvironmentSource source;var p=Running(out source);
            var result=p.ApplyEnvironmentBatch(source,1,2,true,Reentrant(()=>p.ReplaceTopology(2,new[]{D("left"),D("right")})));var s=p.Snapshot();
            Need(result==EnvironmentBatchResult.WrongTopology&&s.TopologyGeneration==2&&s.EnvironmentCaptureSerial==0&&!s.EnvironmentKnown&&s.GlobalBlocks==OutputGlobalBlock.EnvironmentUnknown,"Outer batch overwrote reentrant new topology.");
        });
        Test("topology MoveNext reentry cannot roll back an equal or newer generation and environment serial",()=>{
            foreach(long generation in new[]{2L,3L}){
                OutputEnvironmentSource source;var p=Running(out source);OutputPolicyState inner=null;
                Action change=()=>{p.ReplaceTopology(generation,new[]{D("left"),D("right")});Known(p,source,9,false,true,generation);inner=p.Snapshot();};
                Need(!p.ReplaceTopology(2,TopologyDuringMoveNext(change)),"Outer topology was committed after reentry.");var after=p.Snapshot();SameQualification(inner,after);
                Need(after.TopologyGeneration==generation&&after.EnvironmentKnown&&after.EnvironmentCaptureSerial==9&&Row(after,"right").EnvironmentFullscreen,"Outer topology reset newer environment scope.");
            }
        });
        Test("topology Dispose reentry cannot overwrite an equal or newer generation and environment vote",()=>{
            foreach(long generation in new[]{2L,3L}){
                OutputEnvironmentSource source;var p=Running(out source);OutputPolicyState inner=null;
                Action change=()=>{p.ReplaceTopology(generation,new[]{D("left"),D("right")});Known(p,source,9,false,true,generation);inner=p.Snapshot();};
                Need(!p.ReplaceTopology(2,new TopologyOnDispose(change)),"Outer topology committed after iterator Dispose reentry.");var after=p.Snapshot();SameQualification(inner,after);
                Need(after.TopologyGeneration==generation&&after.EnvironmentKnown&&after.EnvironmentCaptureSerial==9&&Row(after,"right").EnvironmentFullscreen,"Dispose reentry lost its environment state.");
            }
        });
        Test("reentrant newer unknown cannot be cleared by the outer older known",()=>{
            OutputEnvironmentSource source;var p=Running(out source);var result=p.ApplyEnvironmentBatch(source,1,2,true,Reentrant(()=>Unknown(p,source,9)));var s=p.Snapshot();
            Need(result==EnvironmentBatchResult.Stale&&s.EnvironmentCaptureSerial==9&&!s.EnvironmentKnown&&!Row(s,"left").EnvironmentFullscreen&&!Row(s,"right").EnvironmentFullscreen,"Outer Known restored after newer Unknown.");
        });
        Test("reentrant newer known coverage cannot be overwritten by an older outer batch",()=>{
            OutputEnvironmentSource source;var p=Running(out source);var result=p.ApplyEnvironmentBatch(source,1,2,true,Reentrant(()=>Known(p,source,9,false,true)));var s=p.Snapshot();
            Need(result==EnvironmentBatchResult.Stale&&s.EnvironmentCaptureSerial==9&&s.EnvironmentKnown&&!Row(s,"left").EnvironmentFullscreen&&Row(s,"right").EnvironmentFullscreen,"Older outer coverage overwrote newer evidence.");
        });
        Test("interrupted outer iterator cannot replace a reentrant later observation with rejected fallback",()=>{
            OutputEnvironmentSource source;var p=Running(out source);Need(p.ApplyEnvironmentBatch(source,1,2,true,Reentrant(()=>Unknown(p,source,9),true))==EnvironmentBatchResult.Stale&&p.Snapshot().EnvironmentCaptureSerial==9&&!p.Snapshot().EnvironmentKnown,"Malformed outer batch regressed a newer serial.");
        });
        Test("snapshot and input coverage remain independent immutable evidence",()=>{
            OutputEnvironmentSource source;var p=Running(out source);var before=p.Snapshot();var coverage=new List<OutputEnvironmentCoverage>(Both(true,false));p.ApplyEnvironmentBatch(source,1,2,true,coverage);coverage.Clear();var after=p.Snapshot();
            Need(!Row(before,"left").EnvironmentFullscreen&&Row(after,"left").EnvironmentFullscreen&&before.EnvironmentCaptureSerial==1&&after.EnvironmentCaptureSerial==2,"Batch mutated an older sample or retained caller collection.");
            Reject<NotSupportedException>(()=>((IList<OutputState>)after.Outputs).Clear());
        });
        Test("revision overflow during known coverage cannot partially change coverage vote or serial",()=>{
            OutputEnvironmentSource source;var p=Running(out source);var before=p.Snapshot();Field(p,"revision",Int64.MaxValue);Reject<OverflowException>(()=>Known(p,source,2,true,false));var after=p.Snapshot();
            Need(after.Revision==Int64.MaxValue&&after.InputEpoch==before.InputEpoch&&after.EnvironmentCaptureSerial==1&&after.EnvironmentKnown&&!Row(after,"left").EnvironmentFullscreen&&after.GlobalBlocks==OutputGlobalBlock.None,"Overflow partially applied coverage or ledger.");
            Field(p,"revision",before.Revision);Need(Known(p,source,2,true,false)==EnvironmentBatchResult.Applied,"Failed serial was incorrectly consumed.");
        });
        Test("input epoch overflow cannot partially publish environment coverage",()=>{
            OutputEnvironmentSource source;var p=Running(out source);var before=p.Snapshot();Field(p,"inputEpoch",Int64.MaxValue);Reject<OverflowException>(()=>Known(p,source,2,true,false));var after=p.Snapshot();
            Need(after.Revision==before.Revision&&after.InputEpoch==Int64.MaxValue&&after.EnvironmentCaptureSerial==1&&!Row(after,"left").EnvironmentFullscreen&&after.EnvironmentKnown,"Epoch overflow partially committed.");
            Field(p,"inputEpoch",before.InputEpoch);Need(Known(p,source,2,true,false)==EnvironmentBatchResult.Applied,"Epoch failure consumed environment serial.");
        });
        Test("revision overflow on unknown or malformed fallback leaves the prior positive vote intact",()=>{
            foreach(bool known in new[]{false,true}){
                OutputEnvironmentSource source;var p=Running(out source);var before=p.Snapshot();Field(p,"revision",Int64.MaxValue);
                Reject<OverflowException>(()=>p.ApplyEnvironmentBatch(source,1,2,known,null,known?OutputGlobalBlock.None:OutputGlobalBlock.SessionUnavailable));var after=p.Snapshot();
                Need(after.EnvironmentKnown&&after.EnvironmentCaptureSerial==1&&after.GlobalBlocks==OutputGlobalBlock.None&&after.InputEpoch==before.InputEpoch,"Fallback overflow partly updated the reserved vote.");
            }
        });
        Test("failed registration overflow does not reserve the sole environment token",()=>{
            var p=Plain();var before=p.Snapshot();Field(p,"revision",Int64.MaxValue);Reject<OverflowException>(()=>p.RegisterEnvironmentSource());var after=p.Snapshot();
            Need(!after.EnvironmentRegistered&&after.GlobalBlocks==OutputGlobalBlock.None&&after.EnvironmentCaptureSerial==0&&after.InputEpoch==before.InputEpoch,"Registration overflow left a partial source.");
            Field(p,"revision",before.Revision);Need(p.RegisterEnvironmentSource()!=null&&p.Snapshot().EnvironmentRegistered,"Rejected registration consumed the unique slot.");
        });
        Test("topology overflow cannot reset the reserved vote or capture high-water",()=>{
            OutputEnvironmentSource source;var p=Running(out source);Known(p,source,8,false,true);var before=p.Snapshot();Field(p,"revision",Int64.MaxValue);Reject<OverflowException>(()=>p.ReplaceTopology(2,new[]{D("left")}));var after=p.Snapshot();
            Need(after.TopologyGeneration==1&&after.EnvironmentKnown&&after.EnvironmentCaptureSerial==8&&Row(after,"right").EnvironmentFullscreen&&after.GlobalBlocks==OutputGlobalBlock.None,"Failed topology partially reset the environment source.");
            Field(p,"revision",before.Revision);p.ReplaceTopology(2,new[]{D("left")});Need(!p.Snapshot().EnvironmentKnown&&p.Snapshot().EnvironmentCaptureSerial==0&&p.Snapshot().GlobalBlocks==OutputGlobalBlock.EnvironmentUnknown,"Topology retry did not reset environment atomically.");
        });
        Test("identical new serial may advance without overflowing an unchanged qualification version",()=>{
            OutputEnvironmentSource source;var p=Running(out source);Field(p,"revision",Int64.MaxValue);Field(p,"inputEpoch",Int64.MaxValue);
            Need(Known(p,source,2)==EnvironmentBatchResult.Unchanged&&p.Snapshot().EnvironmentCaptureSerial==2&&p.Snapshot().Revision==Int64.MaxValue&&p.Snapshot().InputEpoch==Int64.MaxValue,"Unchanged observation churned saturated policy versions.");
        });
        Test("empty configured topology remains conservatively unknown even for an empty Known payload",()=>{
            var p=new OutputPolicy();var source=p.RegisterEnvironmentSource();p.ReplaceTopology(1,new OutputDefinition[0]);Need(p.ApplyEnvironmentBatch(source,1,1,true,new OutputEnvironmentCoverage[0])==EnvironmentBatchResult.Rejected&&!p.Snapshot().EnvironmentKnown&&!p.Snapshot().CanRun,"Empty topology produced a positive environment declaration.");
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed; pure declarations only, no OS/window/lease execution.");return failed==0?0:1;
    }
}
