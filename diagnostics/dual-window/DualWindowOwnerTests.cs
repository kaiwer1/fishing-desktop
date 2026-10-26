using System;
using System.IO;
using System.Threading;
using FishingDesktop.Diagnostics.HostPolicy;

static class DualWindowOwnerTests
{
    static string root;
    static int failures;
    static string Id(){return Guid.NewGuid().ToString("D");}
    static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    static void Reject(OwnerReply reply,string error){Assert(!reply.Ok && reply.Error==error,"Expected "+error);}
    static string Journal(){return Path.Combine(root,Id()+".jsonl");}
    static DualWindowOwner.Ticket View(DualWindowOwner owner,bool control){var ticket=owner.Open(control);owner.Attach(ticket,Id());return ticket;}
    static void Locked(string path)
    {
        bool locked=false;
        try{using(var unexpected=new DurableCounterStore(path)) { }}catch(IOException){locked=true;}
        Assert(locked,"Owner released journal while still alive.");
    }
    static void Test(string name,Action check)
    {
        try{check();Console.WriteLine("PASS "+name);}catch(Exception e){failures++;Console.WriteLine("FAIL "+name+": "+e.Message);}
    }
    static int Main(string[] args)
    {
        if(args.Length!=1 || !Directory.Exists(args[0]))return 2;
        root=args[0];
        Test("closing and replacing both windows preserves owner, sequence and writer lock",delegate {
            var path=Journal();
            using(var owner=new DualWindowOwner(path)){
                var read=View(owner,false);var control=View(owner,true);var id=owner.Snapshot().OwnerId;
                var lease=owner.Grant(control);var request=Id();
                Assert(owner.Execute(control,control.DocumentId,lease,request,7,0).Ok,"Commit failed.");
                owner.Close(control);Locked(path);owner.Close(read);Locked(path);
                Assert(owner.Snapshot().OwnerId==id && owner.Snapshot().Sequence==1,"Zero windows disposed the owner.");
                control=View(owner,true);read=View(owner,false);
                Reject(owner.Execute(control,control.DocumentId,lease,Id(),1,1),"inactive-lease");
                var retry=owner.Execute(control,control.DocumentId,owner.Grant(control),request,7,0);
                Assert(retry.Ok && retry.Receipt.Duplicate && owner.Snapshot().Value==7,"Reopen duplicated state.");
                Assert(owner.Snapshot().OwnerId==id,"Reopen created another owner.");
            }
            using(var reopened=new DurableCounterStore(path))Assert(reopened.Value==7 && reopened.Sequence==1,"Final dispose did not release journal.");
        });
        Test("old window callbacks cannot close, bind, revoke or mutate replacement",delegate {
            using(var owner=new DualWindowOwner(Journal())){
                var old=View(owner,true);var document=old.DocumentId;var lease=owner.Grant(old);
                owner.Close(old);var current=View(owner,true);var currentLease=owner.Grant(current);
                owner.Close(old);owner.Navigating(old);
                Assert(!owner.Attach(old,Id()) && !owner.Cancel(old,document,lease,100),"Detached ticket affected authority.");
                Reject(owner.Execute(old,document,lease,Id(),1,0),"stale-document");
                Assert(owner.Execute(current,current.DocumentId,currentLease,Id(),2,0).Ok,"Old callback revoked replacement.");
            }
        });
        Test("reload rejects old document and lease until explicit new activation",delegate {
            using(var owner=new DualWindowOwner(Journal())){
                var view=View(owner,true);var doc=view.DocumentId;var lease=owner.Grant(view);
                owner.Navigating(view);Assert(owner.Grant(view)==null,"Navigating view received grant.");
                owner.Attach(view,Id());
                Reject(owner.Execute(view,doc,lease,Id(),1,0),"stale-document");
                Reject(owner.Execute(view,view.DocumentId,lease,Id(),1,0),"inactive-lease");
                var current=owner.Grant(view);
                Assert(!owner.Cancel(view,doc,lease,100),"Old document cancellation was accepted.");
                Assert(owner.Execute(view,view.DocumentId,current,Id(),1,0).Ok,"New activation failed.");
            }
        });
        Test("read-only recreation does not revoke interaction authority",delegate {
            using(var owner=new DualWindowOwner(Journal())){
                var read=View(owner,false);var control=View(owner,true);var lease=owner.Grant(control);
                Assert(owner.Grant(read)==null,"Read-only view received grant.");
                Reject(owner.Execute(read,read.DocumentId,lease,Id(),1,0),"read-only-view");
                owner.Close(read);View(owner,false);
                Assert(owner.Execute(control,control.DocumentId,lease,Id(),1,0).Ok,"Read-only close revoked another view.");
            }
        });
        Test("revocation and stale lease cancellation preserve lease boundaries",delegate {
            using(var owner=new DualWindowOwner(Journal())){
                var control=View(owner,true);var old=owner.Grant(control);var current=owner.Grant(control);
                Assert(!owner.Cancel(control,control.DocumentId,old,1),"Old lease cancelled new lease.");
                Assert(owner.Execute(control,control.DocumentId,current,Id(),1,0).Ok,"Current lease invalidated.");
                owner.Revoke(control);
                Reject(owner.Execute(control,control.DocumentId,current,Id(),1,1),"inactive-lease");
            }
        });
        Test("duplicate roles and foreign threads cannot acquire owner mutation",delegate {
            using(var owner=new DualWindowOwner(Journal())){
                View(owner,true);bool duplicate=false;try{owner.Open(true);}catch(InvalidOperationException){duplicate=true;}
                Assert(duplicate,"Duplicate role allowed.");
                Exception failure=null;var thread=new Thread(delegate(){try{owner.Open(false);}catch(Exception e){failure=e;}});thread.Start();thread.Join();
                Assert(failure is InvalidOperationException,"Foreign thread changed lifecycle.");
            }
        });
        Test("activation handshake accepts advanced page epoch and consumes exact request",delegate {
            var gate=new ActivationHandshake();var request=gate.Begin("view","doc",3);
            Assert(gate.Complete(request,"view","doc",3,true,4,2),"Advanced page epoch rejected.");
            Assert(!gate.Complete(request,"view","doc",3,true,4,2),"Handshake reused.");
            Assert(gate.PageCancelled("view",request),"Cancellation missed already issued request.");
        });
        Test("cancelled activation cannot revive after focus returns or supersede newer request",delegate {
            var gate=new ActivationHandshake();var old=gate.Begin("view","doc",3);gate.Cancel("view");
            Assert(!gate.Complete(old,"view","doc",3,true,1,0),"Focus return revived cancelled activation.");
            var current=gate.Begin("view","doc",3);
            Assert(!gate.Complete(old,"view","doc",3,true,1,0),"Old request accepted.");
            Assert(gate.Complete(current,"view","doc",3,true,1,0),"Old reply consumed current request.");
            current=gate.Begin("view","doc",3);gate.PageCancelled("view",current);
            Assert(!gate.Complete(current,"view","doc",3,true,2,0),"Page cancellation revived.");
        });
        Test("activation requires matching view document generation focus and epoch",delegate {
            var gate=new ActivationHandshake();
            Assert(!gate.Complete(gate.Begin("view","doc",3),"other","doc",3,true,1,0),"Wrong view accepted.");
            Assert(!gate.Complete(gate.Begin("view","doc",3),"view","old",3,true,1,0),"Wrong document accepted.");
            Assert(!gate.Complete(gate.Begin("view","doc",3),"view","doc",4,true,1,0),"Wrong generation accepted.");
            var id=gate.Begin("view","doc",3);
            Assert(!gate.Complete(id,"view","doc",3,false,1,0) && !gate.Complete(id,"view","doc",3,true,1,0),"Unfocused reply revived.");
            Assert(!gate.Complete(gate.Begin("view","doc",3),"view","doc",3,true,1,2),"Stale epoch accepted.");
        });
        Test("obsolete navigation exceptions cannot terminate current generation",delegate {
            Assert(!ActivationHandshake.CurrentNavigation(7,1,8,2,false),"Old navigation considered current.");
            Assert(!ActivationHandshake.CurrentNavigation(7,1,7,2,false),"Old generation considered current.");
            Assert(!ActivationHandshake.CurrentNavigation(7,2,7,2,true),"Pending reload considered current.");
            Assert(ActivationHandshake.CurrentNavigation(8,3,8,3,false),"Current failure would be ignored.");
        });
        Test("owner pause blocks grants and commands including receipt lookup until a fresh resume grant",delegate {
            using(var owner=new DualWindowOwner(Journal())){
                var control=View(owner,true);var lease=owner.Grant(control);var request=Id();
                Assert(owner.Execute(control,control.DocumentId,lease,request,7,0).Ok,"Initial commit failed.");
                owner.SetPause(PauseReason.Manual,true);
                Assert(owner.Grant(control)==null,"Paused owner issued a lease.");
                Reject(owner.Execute(control,control.DocumentId,lease,request,7,0),"host-paused");
                owner.SetPause(PauseReason.Manual,false);
                Reject(owner.Execute(control,control.DocumentId,lease,request,7,0),"inactive-lease");
                var retry=owner.Execute(control,control.DocumentId,owner.Grant(control),request,7,0);
                Assert(retry.Ok && retry.Receipt.Duplicate && owner.Snapshot().Sequence==1,"Resume lost deduplication.");
            }
        });
        Test("view reconstruction does not clear owner pause or release its writer lock",delegate {
            var path=Journal();using(var owner=new DualWindowOwner(path)){
                var control=View(owner,true);string id=owner.Snapshot().OwnerId;
                owner.SetPause(PauseReason.SessionUnavailable,true);owner.Close(control);control=View(owner,true);Locked(path);
                Assert(owner.Grant(control)==null && !owner.ActivityState().CanRun && owner.Snapshot().OwnerId==id,"View reconstruction reset pause or owner.");
                owner.SetPause(PauseReason.Manual,true);owner.SetPause(PauseReason.SessionUnavailable,false);
                Assert(!owner.ActivityState().CanRun,"Clearing one pause resumed owner.");
            }
        });
        Test("foreign thread cannot change owner pause state",delegate {
            using(var owner=new DualWindowOwner(Journal())){
                bool rejected=false;var thread=new Thread(()=>{try{owner.SetPause(PauseReason.Manual,true);}catch(InvalidOperationException){rejected=true;}});
                thread.Start();thread.Join();Assert(rejected && owner.ActivityState().CanRun,"Foreign thread changed policy.");
            }
        });
        Test("activation acknowledgements are bound to host run revision",delegate {
            var gate=new ActivationHandshake();var request=gate.Begin("view","doc",1,10);
            Assert(!gate.Complete(request,"view","doc",1,true,0,0,12),"Old run revision authorized input.");
            Assert(!gate.Complete(request,"view","doc",1,true,0,0,10),"Rejected acknowledgement was reusable.");
            request=gate.Begin("view","doc",1,12);Assert(gate.Complete(request,"view","doc",1,true,0,0,12),"Fresh host revision rejected.");
        });
        return failures==0?0:1;
    }
}
