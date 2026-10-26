using System;
using System.IO;
using System.Threading;
using FishingDesktop.Diagnostics.HostPolicy;

static class MultiOutputOwnerTests
{
    static string root;
    static int passed,failed;
    static string Id(){return Guid.NewGuid().ToString("D");}
    static string Journal(){return Path.Combine(root,Id()+".jsonl");}
    static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Reject<T>(Action action) where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    static void Error(OwnerReply reply,string code){Need(!reply.Ok && reply.Error==code,"Unexpected command result.");}
    static DualWindowOwner.Ticket Output(DualWindowOwner owner,string key){var ticket=owner.OpenOutput(key);Need(owner.Attach(ticket,Id()),"Output did not attach.");return ticket;}
    static DualWindowOwner.Ticket Control(DualWindowOwner owner){var ticket=owner.Open(true);Need(owner.Attach(ticket,Id()),"Control did not attach.");return ticket;}
    static void Locked(string path){Reject<IOException>(()=>{using(var second=new DurableCounterStore(path)){};});}
    static void Test(string name,Action test){try{test();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
    static int Main(string[] args)
    {
        if(args.Length!=1 || !Directory.Exists(args[0]))return 2;root=args[0];
        Test("distinct host output keys create independent read-only tickets under one owner",()=>{
            using(var owner=new DualWindowOwner(Journal())){
                DualWindowOwner.Ticket a=Output(owner,"DISPLAY-1"),b=Output(owner,"display-2");var control=Control(owner);
                Need(a.OutputKey=="display-1" && b.OutputKey=="display-2" && control.OutputKey==null,"Output keys were not canonical or control acquired an output key.");
                Need(!a.CanControl && !b.CanControl && control.CanControl && a.ViewId!=b.ViewId && a.Role!=b.Role && owner.IsCurrent(a) && owner.IsCurrent(b),"Output identities collided.");
            }
        });
        Test("legacy observer and default output are one singleton in both opening orders",()=>{
            using(var owner=new DualWindowOwner(Journal())){
                var legacy=owner.Open(false);Need(legacy.OutputKey==DualWindowOwner.DefaultOutputKey && legacy.Role=="observer","Legacy default changed.");
                Reject<InvalidOperationException>(()=>owner.OpenOutput("DEFAULT"));owner.Close(legacy);
                var named=owner.OpenOutput("Default");Reject<InvalidOperationException>(()=>owner.Open(false));Need(owner.IsCurrent(named),"Alias rejection removed the current default.");
            }
        });
        Test("case aliases reject duplicate outputs without replacing their current identity",()=>{
            using(var owner=new DualWindowOwner(Journal())){DualWindowOwner.Ticket a=Output(owner,"Display_1.Main");Reject<InvalidOperationException>(()=>owner.OpenOutput("display_1.main"));Need(owner.IsCurrent(a) && a.OutputKey=="display_1.main","Duplicate changed the output.");}
        });
        Test("invalid host keys reject before allocating output or changing confirmed state",()=>{
            using(var owner=new DualWindowOwner(Journal())){
                foreach(var key in new[]{null,""," "," leading","trailing ","a/b","a\\b","a:b","..","-x","_x",".x","a\nline","a\0b","屏幕1","\u0130","a@b",new String('a',65)})Reject<ArgumentException>(()=>owner.OpenOutput(key));
                var valid=Output(owner,"A");Need(owner.IsCurrent(valid) && owner.Snapshot().Sequence==0 && owner.Snapshot().Value==0,"Rejected key changed owner state.");
            }
        });
        Test("the canonical key length boundary and ASCII case are culture independent",()=>{
            using(var owner=new DualWindowOwner(Journal())){
                var boundary=Output(owner,new String('A',64));Need(boundary.OutputKey==new String('a',64),"Maximum key changed.");
                var prior=Thread.CurrentThread.CurrentCulture;try{Thread.CurrentThread.CurrentCulture=new System.Globalization.CultureInfo("tr-TR");Need(Output(owner,"I-1").OutputKey=="i-1","Culture changed output identity.");}finally{Thread.CurrentThread.CurrentCulture=prior;}
            }
        });
        Test("all outputs remain ungrantable and cannot steal the current control lease",()=>{
            using(var owner=new DualWindowOwner(Journal())){
                DualWindowOwner.Ticket a=Output(owner,"one"),b=Output(owner,"two");var control=Control(owner);string lease=owner.Grant(control);
                foreach(var output in new[]{a,b}){Need(owner.Grant(output)==null,"Output received authority.");Error(owner.Execute(output,output.DocumentId,lease,Id(),1,0),"read-only-view");Need(!owner.Cancel(output,output.DocumentId,lease,1),"Output cancelled a control lease.");}
                Need(owner.Execute(control,control.DocumentId,lease,Id(),7,0).Ok && owner.Snapshot().Value==7,"Output altered valid control.");
            }
        });
        Test("closing one output preserves another output control and journal lock",()=>{
            string path=Journal();using(var owner=new DualWindowOwner(path)){
                DualWindowOwner.Ticket a=Output(owner,"one"),b=Output(owner,"two");var control=Control(owner);string lease=owner.Grant(control),identity=owner.Snapshot().OwnerId;
                owner.Close(a);Locked(path);Need(!owner.IsCurrent(a) && owner.IsCurrent(b) && owner.IsCurrent(control),"Output close crossed roles.");
                Need(owner.Execute(control,control.DocumentId,lease,Id(),3,0).Ok && owner.Snapshot().OwnerId==identity,"Output close replaced owner or control.");
            }
        });
        Test("late calls from a replaced output cannot touch its replacement or other roles",()=>{
            using(var owner=new DualWindowOwner(Journal())){
                var old=Output(owner,"screen");string oldDocument=old.DocumentId;var other=Output(owner,"other");var control=Control(owner);string lease=owner.Grant(control);
                owner.Close(old);var current=Output(owner,"SCREEN");string newDocument=current.DocumentId;
                owner.Navigating(old);owner.Close(old);owner.Revoke(old);Need(!owner.Attach(old,Id()) && !owner.Cancel(old,oldDocument,lease,8) && owner.Grant(old)==null,"Old ticket regained authority.");
                Reject<InvalidOperationException>(()=>owner.NextRevision(old));Error(owner.Execute(old,newDocument,lease,Id(),1,0),"stale-document");
                Need(current.DocumentId==newDocument && owner.IsCurrent(current) && owner.IsCurrent(other) && owner.Execute(control,control.DocumentId,lease,Id(),2,0).Ok,"Late output action changed a live role.");
            }
        });
        Test("navigating one output rejects its old document without cancelling another role",()=>{
            using(var owner=new DualWindowOwner(Journal())){
                DualWindowOwner.Ticket a=Output(owner,"one"),b=Output(owner,"two");var control=Control(owner);string lease=owner.Grant(control),oldDocument=a.DocumentId;
                owner.Navigating(a);Need(a.DocumentId==null && owner.IsCurrent(b),"Navigation crossed output roles.");owner.Attach(a,Id());
                Error(owner.Execute(a,oldDocument,lease,Id(),1,0),"stale-document");Need(owner.Execute(control,control.DocumentId,lease,Id(),2,0).Ok,"Read-only navigation revoked control.");
            }
        });
        Test("output replacement during pause cannot clear policy or restore old control",()=>{
            using(var owner=new DualWindowOwner(Journal())){
                DualWindowOwner.Ticket a=Output(owner,"one");var control=Control(owner);string lease=owner.Grant(control);owner.SetPause(PauseReason.Manual,true);
                owner.Close(a);a=Output(owner,"ONE");Need(!owner.ActivityState().CanRun && owner.Grant(a)==null && owner.Grant(control)==null,"Output replacement cleared pause.");
                Error(owner.Execute(control,control.DocumentId,lease,Id(),1,0),"host-paused");owner.SetPause(PauseReason.Manual,false);
                Error(owner.Execute(control,control.DocumentId,lease,Id(),1,0),"inactive-lease");Need(owner.Grant(control)!=null,"Explicit new control was unavailable.");
            }
        });
        Test("replacing every output preserves the same confirmed receipt and owner",()=>{
            using(var owner=new DualWindowOwner(Journal())){
                DualWindowOwner.Ticket a=Output(owner,"one"),b=Output(owner,"two");var control=Control(owner);string lease=owner.Grant(control),request=Id(),identity=owner.Snapshot().OwnerId;
                Need(owner.Execute(control,control.DocumentId,lease,request,7,0).Ok,"Initial commit failed.");owner.Close(a);owner.Close(b);Output(owner,"ONE");Output(owner,"TWO");
                var duplicate=owner.Execute(control,control.DocumentId,lease,request,7,0);Need(duplicate.Ok && duplicate.Receipt.Duplicate && duplicate.Snapshot.Sequence==1 && duplicate.Snapshot.Value==7 && duplicate.Snapshot.OwnerId==identity,"Output replacement duplicated committed work.");
            }
        });
        Test("output names resembling roles cannot alias the single control or default observer",()=>{
            using(var owner=new DualWindowOwner(Journal())){
                var control=Control(owner);DualWindowOwner.Ticket one=Output(owner,"interaction"),two=Output(owner,"observer"),defaultView=owner.Open(false);
                Need(owner.IsCurrent(control) && owner.IsCurrent(one) && owner.IsCurrent(two) && owner.IsCurrent(defaultView) && !one.CanControl && !two.CanControl,"Output key escaped its namespace.");
                Reject<InvalidOperationException>(()=>owner.Open(true));
            }
        });
        Test("closing every output and control keeps writer ownership until explicit owner disposal",()=>{
            string path=Journal();using(var owner=new DualWindowOwner(path)){
                DualWindowOwner.Ticket a=Output(owner,"one"),b=Output(owner,"two");var control=Control(owner);owner.Close(a);owner.Close(b);owner.Close(control);Locked(path);
                Need(owner.Snapshot().Sequence==0 && owner.IsCurrent(Output(owner,"one")),"No-view owner lost state or ability to reopen.");
            }using(var reopened=new DurableCounterStore(path)){Need(reopened.Sequence==0,"Explicit disposal corrupted journal.");}
        });
        Test("foreign threads cannot register an output even when its key is invalid",()=>{
            using(var owner=new DualWindowOwner(Journal())){
                Exception first=null,second=null;var thread=new Thread(()=>{try{owner.OpenOutput("one");}catch(Exception error){first=error;}try{owner.OpenOutput(" ");}catch(Exception error){second=error;}});thread.Start();thread.Join();
                Need(first is InvalidOperationException && second is InvalidOperationException && owner.IsCurrent(Output(owner,"one")),"Foreign access allocated an output.");
            }
        });
        Test("tickets from another owner cannot mutate a matching output key",()=>{
            using(var first=new DualWindowOwner(Journal()))using(var second=new DualWindowOwner(Journal())){
                DualWindowOwner.Ticket foreign=Output(first,"one"),current=Output(second,"one");string document=current.DocumentId;
                second.Close(foreign);second.Navigating(foreign);Need(!second.Attach(foreign,Id()) && !second.IsCurrent(foreign) && second.IsCurrent(current) && current.DocumentId==document,"Foreign ticket crossed owner identities.");
            }
        });
        Test("disposed owners reject new output registration and release their writer lock once",()=>{
            string path=Journal();var owner=new DualWindowOwner(path);Output(owner,"one");owner.Dispose();owner.Dispose();Reject<ObjectDisposedException>(()=>owner.OpenOutput("two"));
            using(var restarted=new DualWindowOwner(path)){Need(restarted.Snapshot().Sequence==0 && restarted.IsCurrent(Output(restarted,"one")),"Disposed output prevented a new owner.");}
        });
        Console.WriteLine("TOTAL passed="+passed+" failed="+failed);return failed==0 && passed==16?0:1;
    }
}
