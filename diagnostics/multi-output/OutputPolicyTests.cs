using System;
using System.Collections.Generic;
using System.Threading;
using FishingDesktop.Diagnostics.MultiOutput;

static class OutputPolicyTests
{
    static int passed,failed;
    static void Need(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    static void Reject<T>(Action action) where T:Exception {try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
    static OutputDefinition D(string key){return new OutputDefinition(key,key=="left"?-1920:0,0,1920,1080,96,96);}
    static OutputState Row(OutputPolicyState state,string key){foreach(var row in state.Outputs)if(row.Definition.Key==key)return row;throw new InvalidOperationException("Missing row.");}
    static OutputPolicy Running()
    {
        var p=new OutputPolicy(4);p.ReplaceTopology(1,new[]{D("left"),D("right")});
        p.BindView("left",1,1);p.Observe("left",1,1,true,true,false,true);
        p.BindView("right",1,1);p.Observe("right",1,1,true,true,false,true);p.SetGlobal(OutputGlobalBlock.Startup,false);return p;
    }
    static IEnumerable<OutputDefinition> Interrupted(){yield return D("third");throw new InvalidOperationException("Scripted enumeration failure.");}
    static int Main()
    {
        Test("startup and zero outputs never permit shared activity",()=>{
            var p=new OutputPolicy();Need(!p.Snapshot().CanRun&&p.Snapshot().GlobalBlocks==OutputGlobalBlock.Startup,"Startup was runnable.");
            p.SetGlobal(OutputGlobalBlock.Startup,false);Need(!p.Snapshot().CanRun&&!p.Snapshot().ControlEligible,"Empty topology became active.");
        });
        Test("one fullscreen output does not pause another usable output",()=>{
            var p=Running();p.Observe("left",1,1,true,true,true,true);var s=p.Snapshot();
            Need(s.CanRun&&!Row(s,"left").CanDraw&&Row(s,"right").CanDraw,"Local fullscreen became a global pause.");
        });
        Test("all unusable outputs stop shared eligibility without changing global reasons",()=>{
            var p=Running();p.Observe("left",1,1,true,false,false,true);p.Observe("right",1,1,true,true,false,false);
            var s=p.Snapshot();Need(!s.CanRun&&s.GlobalBlocks==OutputGlobalBlock.None,"No drawable output remained active.");
        });
        Test("manual session and power blockers survive partial global and local recovery",()=>{
            var p=Running();p.SetGlobal(OutputGlobalBlock.Manual,true);p.SetGlobal(OutputGlobalBlock.SessionUnavailable,true);p.SetGlobal(OutputGlobalBlock.PowerSuspended,true);
            p.Observe("left",1,1,true,true,true,true);p.Observe("left",1,1,true,true,false,true);p.SetGlobal(OutputGlobalBlock.Manual,false);
            Need(!p.Snapshot().CanRun&&p.Snapshot().GlobalBlocks==(OutputGlobalBlock.SessionUnavailable|OutputGlobalBlock.PowerSuspended),"Partial recovery cleared independent blockers.");
            p.SetGlobal(OutputGlobalBlock.SessionUnavailable,false);Need(!p.Snapshot().CanRun,"Power blocker lost.");p.SetGlobal(OutputGlobalBlock.PowerSuspended,false);Need(p.Snapshot().CanRun,"Final recovery failed.");
        });
        Test("global blockers suppress drawing as well as shared eligibility",()=>{
            var p=Running();p.SetGlobal(OutputGlobalBlock.SceneFault,true);var s=p.Snapshot();
            Need(!s.CanRun&&Row(s,"left").LocalCanDraw&&!Row(s,"left").CanDraw&&!Row(s,"right").CanDraw,"Global scene fault leaked drawing eligibility.");
        });
        Test("a non-target local failure does not invalidate healthy target control",()=>{
            var p=Running();p.SelectControlTarget("left");var before=p.Snapshot();p.Observe("right",1,1,false,true,false,true);
            Need(p.Snapshot().InputEpoch==before.InputEpoch&&p.CanActivate("left",1,1,before.InputEpoch),"Other output revoked healthy target control.");
        });
        Test("target failure and restoration each invalidate earlier activation requests",()=>{
            var p=Running();p.SelectControlTarget("left");var before=p.Snapshot();p.Observe("left",1,1,true,true,true,true);var paused=p.Snapshot();
            Need(paused.InputEpoch>before.InputEpoch&&!paused.ControlEligible&&paused.CanRun,"Target failure did not revoke independently.");
            p.Observe("left",1,1,true,true,false,true);var restored=p.Snapshot();
            Need(restored.InputEpoch>paused.InputEpoch&&restored.ControlEligible&&!p.CanActivate("left",1,1,before.InputEpoch)&&!p.CanActivate("left",1,1,paused.InputEpoch)&&p.CanActivate("left",1,1,restored.InputEpoch),"Restore reused old authority eligibility.");
        });
        Test("target switching and clearing have one current destination and invalidate old scope",()=>{
            var p=Running();p.SelectControlTarget("left");var left=p.Snapshot();p.SelectControlTarget("RIGHT");var right=p.Snapshot();
            Need(right.ControlTarget=="right"&&right.InputEpoch>left.InputEpoch&&!p.CanActivate("left",1,1,left.InputEpoch)&&p.CanActivate("right",1,1,right.InputEpoch),"Target switch retained old destination.");
            p.SelectControlTarget(null);Need(!p.Snapshot().ControlEligible&&p.Snapshot().ControlTarget==null&&p.Snapshot().InputEpoch>right.InputEpoch,"Clear retained a target.");
        });
        Test("topology removal clears the target and rejects every old generation observation",()=>{
            var p=Running();p.SelectControlTarget("left");var before=p.Snapshot();p.ReplaceTopology(2,new[]{D("right")});var next=p.Snapshot();
            Need(next.ControlTarget==null&&next.InputEpoch>before.InputEpoch&&!next.CanRun,"Removed target or fresh readiness survived topology replacement.");
            Need(!p.Observe("left",1,1,true,true,false,true)&&!p.BindView("right",1,2)&&!p.CanActivate("left",1,1,before.InputEpoch),"Old topology was accepted.");
        });
        Test("retained output keys need new bindings after topology replacement",()=>{
            var p=Running();p.SelectControlTarget("left");p.ReplaceTopology(2,new[]{D("left"),D("right")});
            Need(p.Snapshot().ControlTarget=="left"&&!p.Snapshot().ControlEligible&&Row(p.Snapshot(),"left").ViewGeneration==0,"Retained key reused old view.");
            p.BindView("left",2,1);p.Observe("left",2,1,true,true,false,true);Need(p.Snapshot().CanRun&&p.Snapshot().ControlEligible&&!p.Observe("left",1,1,false,false,true,false),"Fresh generation did not isolate old view.");
        });
        Test("view replacement preserves other output and rejects stale view callbacks",()=>{
            var p=Running();p.SelectControlTarget("left");var before=p.Snapshot();p.BindView("left",1,2);var replacement=p.Snapshot();
            Need(replacement.CanRun&&Row(replacement,"right").CanDraw&&!replacement.ControlEligible&&replacement.InputEpoch>before.InputEpoch,"View replacement changed another output.");
            long revision=replacement.Revision;Need(!p.Observe("left",1,1,true,true,false,true)&&!p.BindView("left",1,1)&&p.Snapshot().Revision==revision,"Old view changed replacement.");
        });
        Test("a new target barrier invalidates even when another local barrier remains",()=>{
            var p=Running();p.SelectControlTarget("left");p.Observe("left",1,1,true,true,true,true);long epoch=p.Snapshot().InputEpoch;
            p.Observe("left",1,1,false,true,true,true);Need(p.Snapshot().InputEpoch>epoch&&!p.Snapshot().ControlEligible,"New blocked context retained pending input scope.");
        });
        Test("retiring a target prevents late ready callbacks before a replacement exists",()=>{
            var p=Running();p.SelectControlTarget("left");long before=p.Snapshot().InputEpoch;
            Need(p.RetireView("left",1,1),"Current view did not retire.");var retired=p.Snapshot();
            Need(retired.CanRun&&!retired.ControlEligible&&!Row(retired,"left").Bound&&retired.InputEpoch>before,"Retirement did not isolate target.");
            Need(!p.Observe("left",1,1,true,true,false,true)&&!p.BindView("left",1,1)&&p.Snapshot().Revision==retired.Revision,"Retired generation became ready again.");
        });
        Test("view high water survives retirement and only a newer binding can recover",()=>{
            var p=Running();p.RetireView("left",1,1);Need(Row(p.Snapshot(),"left").ViewGeneration==1,"Retirement forgot view high water.");
            Need(p.BindView("left",1,2)&&p.Observe("left",1,2,true,true,false,true)&&Row(p.Snapshot(),"left").CanDraw,"New view failed to recover.");
            long revision=p.Snapshot().Revision;Need(!p.RetireView("left",1,1)&&p.Snapshot().Revision==revision&&Row(p.Snapshot(),"left").Bound,"Old retire removed replacement.");
        });
        Test("retiring a non-target view preserves control and stale topology retire is ignored",()=>{
            var p=Running();p.SelectControlTarget("left");var before=p.Snapshot();Need(!p.RetireView("left",2,1),"Unknown topology retired view.");
            p.RetireView("right",1,1);Need(p.CanActivate("left",1,1,before.InputEpoch)&&p.Snapshot().InputEpoch==before.InputEpoch,"Other view retirement revoked control.");
        });
        Test("duplicate samples target selection and global state do not churn versions",()=>{
            var p=Running();p.SelectControlTarget("left");var before=p.Snapshot();p.Observe("LEFT",1,1,true,true,false,true);
            Need(!p.SelectControlTarget("LEFT")&&!p.SetGlobal(OutputGlobalBlock.Manual,false)&&p.Snapshot().Revision==before.Revision&&p.Snapshot().InputEpoch==before.InputEpoch,"No-op invalidated versions.");
        });
        Test("selecting an unready target never queues automatic activation on readiness",()=>{
            var p=new OutputPolicy();p.ReplaceTopology(1,new[]{D("left")});p.SetGlobal(OutputGlobalBlock.Startup,false);p.SelectControlTarget("left");var pending=p.Snapshot();
            Need(!p.CanActivate("left",1,1,pending.InputEpoch),"Unbound target could activate.");p.BindView("left",1,1);p.Observe("left",1,1,true,true,false,true);
            Need(!p.CanActivate("left",1,1,pending.InputEpoch)&&p.Snapshot().ControlEligible,"Old unready request activated after readiness.");
        });
        Test("global pause restoration does not accept a request captured during pause",()=>{
            var p=Running();p.SelectControlTarget("left");p.SetGlobal(OutputGlobalBlock.Manual,true);long paused=p.Snapshot().InputEpoch;
            p.SetGlobal(OutputGlobalBlock.Manual,false);Need(!p.CanActivate("left",1,1,paused)&&p.Snapshot().ControlEligible,"Paused request survived global recovery.");
        });
        Test("adding another global blocker while already paused invalidates current input epoch",()=>{
            var p=Running();p.SelectControlTarget("left");p.SetGlobal(OutputGlobalBlock.Manual,true);long epoch=p.Snapshot().InputEpoch;
            p.SetGlobal(OutputGlobalBlock.PowerSuspended,true);Need(p.Snapshot().InputEpoch>epoch,"New global barrier did not invalidate.");
        });
        Test("canonical keys and negative declared geometry are preserved without hardware queries",()=>{
            var d=new OutputDefinition("LEFT-1.a_2",-1920,-1080,1920,1080,144,144);Need(d.Key=="left-1.a_2"&&d.X==-1920&&d.DpiX==144,"Declared geometry changed.");
            foreach(string key in new[]{null,""," ",".left","left/right","左",new String('a',65)})Reject<ArgumentException>(()=>D(key));
        });
        Test("invalid declared rectangles DPI and capacities reject before changing state",()=>{
            Reject<ArgumentOutOfRangeException>(()=>new OutputDefinition("x",0,0,0,1,96,96));Reject<ArgumentOutOfRangeException>(()=>new OutputDefinition("x",Int32.MaxValue,0,1,1,96,96));
            Reject<ArgumentOutOfRangeException>(()=>new OutputDefinition("x",0,0,1,1,0,96));Reject<ArgumentOutOfRangeException>(()=>new OutputPolicy(0));
        });
        Test("duplicate canonical topology keys atomically preserve prior target and versions",()=>{
            var p=Running();p.SelectControlTarget("left");var before=p.Snapshot();Reject<ArgumentException>(()=>p.ReplaceTopology(2,new[]{D("LEFT"),D("left")}));
            var after=p.Snapshot();Need(after.Revision==before.Revision&&after.TopologyGeneration==before.TopologyGeneration&&after.InputEpoch==before.InputEpoch&&after.ControlTarget==before.ControlTarget&&after.CanRun,"Rejected topology partially committed.");
        });
        Test("null excessive or interrupted topology enumeration never partially replaces outputs",()=>{
            var p=new OutputPolicy(1);p.ReplaceTopology(1,new[]{D("left")});var before=p.Snapshot();
            Reject<ArgumentNullException>(()=>p.ReplaceTopology(2,null));Reject<ArgumentException>(()=>p.ReplaceTopology(2,new OutputDefinition[]{null}));
            Reject<ArgumentException>(()=>p.ReplaceTopology(2,new[]{D("left"),D("right")}));Reject<InvalidOperationException>(()=>p.ReplaceTopology(2,Interrupted()));
            Need(p.Snapshot().Revision==before.Revision&&p.Snapshot().TopologyGeneration==1&&p.Snapshot().Outputs.Count==1,"Failed enumeration changed topology.");
        });
        Test("stale topology and unknown outputs are rejected without mutating snapshots",()=>{
            var p=Running();var before=p.Snapshot();Need(!p.ReplaceTopology(1,new[]{D("other")})&&!p.BindView("unknown",1,1)&&!p.Observe("unknown",1,1,true,true,false,true),"Stale or unknown scope was accepted.");
            Reject<ArgumentException>(()=>p.SelectControlTarget("unknown"));Need(p.Snapshot().Revision==before.Revision,"Rejected scope changed state.");
        });
        Test("snapshots and input lists cannot mutate later or earlier policy state",()=>{
            var p=new OutputPolicy();var definitions=new List<OutputDefinition>{D("left")};p.ReplaceTopology(1,definitions);definitions.Clear();var before=p.Snapshot();
            Reject<NotSupportedException>(()=>((IList<OutputState>)before.Outputs).Clear());p.BindView("left",1,1);p.Observe("left",1,1,true,true,false,true);p.SetGlobal(OutputGlobalBlock.Startup,false);
            Need(!before.CanRun&&before.Outputs.Count==1&&before.Outputs[0].ViewGeneration==0&&p.Snapshot().CanRun,"Snapshot or input list remained mutable.");
        });
        Test("invalid global masks cannot clear or set unrelated reasons",()=>{
            var p=Running();long revision=p.Snapshot().Revision;Reject<ArgumentOutOfRangeException>(()=>p.SetGlobal(OutputGlobalBlock.None,true));
            Reject<ArgumentOutOfRangeException>(()=>p.SetGlobal(OutputGlobalBlock.Manual|OutputGlobalBlock.PowerSuspended,false));Reject<ArgumentOutOfRangeException>(()=>p.SetGlobal((OutputGlobalBlock)128,true));
            Need(p.Snapshot().Revision==revision&&p.Snapshot().CanRun,"Invalid mask mutated policy.");
        });
        Test("foreign thread observations and reads are rejected without changing the owner",()=>{
            var p=Running();long revision=p.Snapshot().Revision;int rejected=0;var t=new Thread(()=>{
                try{p.Snapshot();}catch(InvalidOperationException){rejected++;}
                try{p.SetGlobal(OutputGlobalBlock.Manual,true);}catch(InvalidOperationException){rejected++;}
                try{p.Observe("left",1,1,false,false,true,false);}catch(InvalidOperationException){rejected++;}
            });t.Start();t.Join();Need(rejected==3&&p.Snapshot().Revision==revision&&p.Snapshot().CanRun,"Foreign thread touched policy.");
        });
        Test("atomic global replacement moves barriers without a runnable intermediate state",()=>{
            var p=Running();p.SelectControlTarget("left");p.SetGlobal(OutputGlobalBlock.SessionUnavailable,true);p.SetGlobal(OutputGlobalBlock.Manual,true);var before=p.Snapshot();
            p.ReplaceGlobal(OutputGlobalBlock.SessionUnavailable|OutputGlobalBlock.PowerSuspended,OutputGlobalBlock.PowerSuspended);var after=p.Snapshot();
            Need(!after.CanRun&&!after.ControlEligible&&after.GlobalBlocks==(OutputGlobalBlock.PowerSuspended|OutputGlobalBlock.Manual)&&after.Revision==before.Revision+1&&after.InputEpoch>before.InputEpoch,"Global replacement lost an independent barrier or scope.");
        });
        Test("invalid atomic global replacement preserves all reasons and versions",()=>{
            var p=Running();p.SetGlobal(OutputGlobalBlock.Manual,true);var before=p.Snapshot();
            Reject<ArgumentOutOfRangeException>(()=>p.ReplaceGlobal(OutputGlobalBlock.None,OutputGlobalBlock.None));
            Reject<ArgumentOutOfRangeException>(()=>p.ReplaceGlobal(OutputGlobalBlock.Manual,OutputGlobalBlock.PowerSuspended));
            Reject<ArgumentOutOfRangeException>(()=>p.ReplaceGlobal((OutputGlobalBlock)128,OutputGlobalBlock.None));
            Need(p.Snapshot().Revision==before.Revision&&p.Snapshot().InputEpoch==before.InputEpoch&&p.Snapshot().GlobalBlocks==before.GlobalBlocks,"Rejected global replacement mutated policy.");
        });
        Test("activation eligibility queries have no grant or mutation side effects",()=>{
            var p=Running();p.SelectControlTarget("left");var before=p.Snapshot();Need(p.CanActivate("left",1,1,before.InputEpoch)&&p.CanActivate("LEFT",1,1,before.InputEpoch),"Current target was ineligible.");
            Need(!p.CanActivate("left",1,2,before.InputEpoch)&&!p.CanActivate("right",1,1,before.InputEpoch)&&p.Snapshot().Revision==before.Revision&&p.Snapshot().InputEpoch==before.InputEpoch,"Eligibility query changed state or accepted wrong scope.");
        });
        Console.WriteLine("RESULT "+passed+" passed / "+failed+" failed");return failed==0?0:1;
    }
}
