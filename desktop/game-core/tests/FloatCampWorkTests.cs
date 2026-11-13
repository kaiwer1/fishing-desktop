using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using FishingDesktop.GameCore;

static class FloatCampWorkTests
{
    static int passed,failed,nextId,nextFile;static string root;
    static Guid G(){return new Guid(++nextId,0,0,new byte[]{0,0,0,0,0,0,0,97});}
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    static void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error);}}
    static string P(string label){return Path.Combine(root,(++nextFile)+"-"+label+".fltj");}
    static byte[] Bytes(string path){using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){var bytes=new byte[(int)file.Length];int at=0;while(at<bytes.Length){int count=file.Read(bytes,at,bytes.Length-at);if(count==0)throw new EndOfStreamException();at+=count;}return bytes;}}
    static string Sha(byte[] bytes){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    static bool Same(byte[] a,byte[] b){return a.Length==b.Length&&Sha(a)==Sha(b);}
    static FloatGameState New(long wallet=0,int stamina=0,int bait=0,int schema=3)
    {
        var rig=new FixedFloatRig(G(),1,"float-rig",G(),G(),G(),50000,G(),G(),G());
        var rules=new FloatGameRules(1,"float-rig","worm",2,100,1,1000000,new[]{new FloatSpeciesRule("carp",100000,2000000,120)});
        var commerce=new FloatCommerceCatalog(1,"worm","net","bread",2400,5,20,20,20,1000,1000,100);
        if(schema==1)return FloatGameState.CreateNew(G(),G(),wallet,stamina,G(),"worm",bait,rig,rules);
        var s=FloatGameState.CreateNewWithCommerce(G(),G(),wallet,stamina,G(),"worm",bait,rig,rules,commerce,G());if(schema==2)return s;
        var catalog=new FloatEquipmentCatalog(1,"worm",new[]{new FloatEquipmentDefinition("rod",FloatEquipmentKind.Rod,150000,0,1000,25000),new FloatEquipmentDefinition("reel",FloatEquipmentKind.Reel,200000),new FloatEquipmentDefinition("line",FloatEquipmentKind.Line,0,180),new FloatEquipmentDefinition("hook",FloatEquipmentKind.Hook,200),new FloatEquipmentDefinition("bobber",FloatEquipmentKind.Bobber,3000,0,2000,3000),new FloatEquipmentDefinition("sinker",FloatEquipmentKind.Sinker,2500)},new[]{new FloatEquipmentPairRule("rod","reel",true)},new[]{new FloatEquipmentSpoolRule("reel","line",180,50000)},new[]{new FloatEquipmentPairRule("hook","worm",true)},new[]{new FloatEquipmentLossRule("terminal",new[]{FloatEquipmentSlot.Hook})});
        var policy=new FloatEquipmentMigrationPolicy(1,"float-rig",catalog,"rod","reel","line","hook","bobber","sinker");return FloatGameState.CreateNewWithEquipment(s.ProfileId,s.BranchId,wallet,stamina,s.BaitStackId,"worm",bait,policy.Map(s),rules,commerce,s.FoodStackId);
    }
    sealed class Control
    {
        internal readonly FloatGameOwner Owner;internal readonly FloatViewTicket View;internal FloatDocumentTicket Document;internal FloatControlLease Lease;
        internal Control(FloatGameOwner owner){Owner=owner;View=owner.OpenView(FloatViewRole.Interaction);Document=owner.AttachDocument(View);owner.SetHostPaused(false);Lease=owner.GrantControl(View,Document);owner.SetMenuOpen(true);}
        internal FloatGameState State{get{return Owner.Snapshot.State;}}
        internal string Start(FloatCampWork work){return work.Start(View,Document,Lease);}
        internal FloatCommitResult Claim(FloatCampWork work){return work.Claim(View,Document,Lease);}
        internal FloatCommitResult Send(FloatCommand command){return Owner.ExecuteUser(View,Document,Lease,command);}
        internal void Reload(){Document=Owner.AttachDocument(View);Lease=Owner.GrantControl(View,Document);}
    }
    static void Commit(FloatCommitResult result){Need(result.Committed&&result.Written&&!result.IsReplay,"Expected commit: "+result.Code);}
    static void Refused(FloatCommitResult result,string code){Need(!result.Committed&&!result.Written&&result.Code==code,"Expected "+code+" got "+result.Code);}
    static void Start(Control c,FloatCampWork work){Need(c.Start(work)=="camp-work-started","Camp work did not start.");}
    static void CompleteTime(FloatCampWork work){for(int i=0;i<240;i++)work.Advance(250);Need(work.Snapshot.Phase=="ready"&&work.Snapshot.ElapsedMilliseconds==60000,"Eligible active time did not reach ready.");}
    static BuyFloatSupply Buy(FloatGameState s,FloatSupplyProduct product){return new BuyFloatSupply(s.ProfileId,s.BranchId,G(),G(),FloatPurchaseQuote.Create(s,product,1),product==FloatSupplyProduct.Net?G():Guid.Empty,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static CastFloatWithEquipment Cast(FloatGameState s,Guid? session=null){return new CastFloatWithEquipment(s.ProfileId,s.BranchId,G(),G(),session??G(),15000,s.Sequence,s.Rig.Revision,s.RulesVersion,s.Equipment.InventoryRevision,s.Equipment.RulesVersion);}
    static HostFloatFish Fish(FloatGameState s,Guid? id=null,Guid? encounter=null){return new HostFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,id??G(),encounter??G(),"carp",500000,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static HostFloatCampReward Reward(FloatGameState s,int duration=60000,long policy=1,Guid? request=null,Guid? businessEvent=null)
    {return new HostFloatCampReward(s.ProfileId,s.BranchId,request??G(),businessEvent??G(),duration,policy,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static HostFloatCampReward FrozenCommand(FloatCampWork work)
    {return (HostFloatCampReward)typeof(FloatCampWork).GetField("command",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(work);}
    static FloatGameStore OwnerStore(FloatGameOwner owner)
    {return (FloatGameStore)typeof(FloatGameOwner).GetField("_store",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);}
    static void WithBudgetCounters(FloatGameOwner owner,long remainingTransactions,long remainingBytes,Action action)
    {
        // Isolated admission testing only: temporary in-memory counters, not
        // a claim that the fixture file contains 10,000 frames or 16 MiB.
        var store=OwnerStore(owner);var state=owner.Snapshot.State;
        var lengthField=typeof(FloatGameStore).GetField("_length",BindingFlags.Instance|BindingFlags.NonPublic);
        var sequenceField=typeof(FloatGameState).GetField("Sequence",BindingFlags.Instance|BindingFlags.Public);
        long length=(long)lengthField.GetValue(store),sequence=state.Sequence;
        try{lengthField.SetValue(store,FloatGameStore.MaximumFileBytes-remainingBytes);sequenceField.SetValue(state,FloatGameStore.MaximumTransactions-remainingTransactions);action();}
        finally{lengthField.SetValue(store,length);sequenceField.SetValue(state,sequence);}
    }
    static int Main(string[] args)
    {
        if(args.Length!=1||!Directory.Exists(args[0]))return 2;root=Path.GetFullPath(args[0]);
        Test("zero-resource player earns four confirmed rounds then buys bait food and net to cast again",()=>{
            using(var owner=FloatGameOwner.CreateNew(P("recovery-loop"),New())){var c=new Control(owner);var work=new FloatCampWork(owner);for(int round=1;round<=4;round++){Start(c,work);CompleteTime(work);Commit(c.Claim(work));Need(c.State.WalletMinor==round*800&&c.State.Sequence==round,"Reward did not add one fixed transaction.");Refused(c.Claim(work),"camp-work-not-ready");}Need(c.Start(work)=="camp-wallet-ineligible","Wallet threshold allowed another round.");Commit(c.Send(Buy(c.State,FloatSupplyProduct.Bait)));Commit(c.Send(Buy(c.State,FloatSupplyProduct.Food)));var s=c.State;Commit(c.Send(new UseFloatFood(s.ProfileId,s.BranchId,G(),G(),s.FoodStackId,s.Sequence,s.Rig.Revision,s.RulesVersion)));Commit(c.Send(Buy(c.State,FloatSupplyProduct.Net)));s=c.State;Commit(c.Send(new EquipFloatNet(s.ProfileId,s.BranchId,G(),G(),s.OwnedNet.Id,s.Sequence,s.Rig.Revision,s.RulesVersion)));Need(c.State.WalletMinor==760&&c.State.Stamina==20&&c.State.BaitCount==5&&c.State.EquippedNetId==c.State.OwnedNet.Id,"Recovery supplies incorrect.");owner.SetMenuOpen(false);Commit(c.Send(Cast(c.State)));Need(c.State.Sequence==10&&c.State.Session!=null&&c.State.Stamina==18&&c.State.BaitCount==4,"Recovered player could not cast.");}
        });
        Test("start requires current interaction authority and an open menu",()=>{
            using(var owner=FloatGameOwner.CreateNew(P("authority"),New())){var c=new Control(owner);var work=new FloatCampWork(owner);var observer=owner.OpenView(FloatViewRole.Observer);var doc=owner.AttachDocument(observer);Need(work.Start(observer,doc,c.Lease)=="control-required"&&work.Snapshot.Phase=="idle","Observer started work.");owner.SetMenuOpen(false);Need(c.Start(work)=="menu-required","Closed menu started work.");Refused(owner.ExecuteHost(Reward(c.State)),"simulation-paused");owner.SetMenuOpen(true);Start(c,work);work.Advance(250);Refused(work.Claim(observer,doc,c.Lease),"control-required");Need(work.Snapshot.ElapsedMilliseconds==250&&work.Snapshot.Phase=="working","Observer cancelled valid progress.");}
        });
        Test("legacy profile excess wallet active cast and owned fish are ineligible",()=>{
            foreach(int schema in new[]{1,2})using(var owner=FloatGameOwner.CreateNew(P("legacy"),New(schema:schema))){var c=new Control(owner);Need(c.Start(new FloatCampWork(owner))=="camp-schema-required","Legacy profile started work.");}
            using(var owner=FloatGameOwner.CreateNew(P("wealth"),New(wallet:3200))){var c=new Control(owner);Need(c.Start(new FloatCampWork(owner))=="camp-wallet-ineligible","Ineligible wallet accepted.");}
            using(var owner=FloatGameOwner.CreateNew(P("session"),New(stamina:100,bait:12))){var c=new Control(owner);var work=new FloatCampWork(owner);owner.SetMenuOpen(false);Commit(c.Send(Cast(c.State)));owner.SetMenuOpen(true);Need(c.Start(work)=="session-busy","Active session started work.");owner.SetMenuOpen(false);Commit(owner.ExecuteHost(Fish(c.State)));owner.SetMenuOpen(true);Need(c.Start(work)=="camp-creel-not-empty","Owned sellable fish ignored.");}
        });
        Test("only bounded supplied active milliseconds accumulate with no wall-clock or idle credit",()=>{
            using(var owner=FloatGameOwner.CreateNew(P("clock"),New())){var c=new Control(owner);var work=new FloatCampWork(owner);Need(work.Advance(250)=="camp-work-not-active"&&work.Snapshot.ElapsedMilliseconds==0,"Idle time accumulated.");Start(c,work);Thread.Sleep(20);Need(work.Snapshot.ElapsedMilliseconds==0,"Wall clock added work.");work.Advance(0);Throws<ArgumentOutOfRangeException>(()=>work.Advance(-1));Throws<ArgumentOutOfRangeException>(()=>work.Advance(251));Throws<ArgumentOutOfRangeException>(()=>work.Advance(Int32.MaxValue));Need(work.Snapshot.ElapsedMilliseconds==0,"Invalid delta changed progress.");work.Advance(249);work.Advance(1);Need(work.Snapshot.ElapsedMilliseconds==250,"Valid supplied steps were not exact.");}
        });
        Test("ready requires all 60000 active milliseconds and never commits automatically",()=>{
            string path=P("ready");using(var owner=FloatGameOwner.CreateNew(path,New())){var c=new Control(owner);var work=new FloatCampWork(owner);byte[] before=Bytes(path);Start(c,work);for(int i=0;i<239;i++)work.Advance(250);work.Advance(249);Need(work.Snapshot.ElapsedMilliseconds==59999&&work.Snapshot.Phase=="working","Round ready early.");Refused(c.Claim(work),"camp-work-not-ready");work.Advance(1);work.Advance(250);Need(work.Snapshot.Phase=="ready"&&work.Snapshot.ElapsedMilliseconds==60000&&c.State.Sequence==0&&Same(before,Bytes(path)),"Ready auto-paid or exceeded time cap.");Commit(c.Claim(work));Need(work.Snapshot.Phase=="confirmed"&&c.State.WalletMinor==800,"Explicit claim did not confirm.");}
        });
        Test("start while working ready or known retry never resets accumulated progress",()=>{
            bool fail=false;using(var owner=FloatGameOwner.CreateNew(P("busy-start"),New(),point=>{if(fail&&point==FloatSaveFaultPoint.BeforeWrite)throw new IOException("private camp failure");})){var c=new Control(owner);var work=new FloatCampWork(owner);Start(c,work);work.Advance(250);Need(c.Start(work)=="camp-work-busy"&&work.Snapshot.ElapsedMilliseconds==250,"Busy start reset work.");CompleteTime(work);Need(c.Start(work)=="camp-work-busy"&&work.Snapshot.ElapsedMilliseconds==60000,"Ready start reset work.");fail=true;Refused(c.Claim(work),"camp-work-not-written");var command=FrozenCommand(work);Need(c.Start(work)=="camp-work-busy"&&Object.ReferenceEquals(command,FrozenCommand(work))&&work.Snapshot.Phase=="retry","Retry start replaced the frozen command.");}
        });
        Test("host pause cancels working and ready rounds without returning progress on a new grant",()=>{
            foreach(bool ready in new[]{false,true})using(var owner=FloatGameOwner.CreateNew(P("paused"),New())){var c=new Control(owner);var work=new FloatCampWork(owner);Start(c,work);if(ready)CompleteTime(work);else work.Advance(250);owner.SetHostPaused(true);Need(work.Advance(0)=="control-required"&&work.Snapshot.Phase=="cancelled"&&work.Snapshot.ElapsedMilliseconds==0&&c.State.WalletMinor==0,"Paused round survived.");owner.SetHostPaused(false);c.Lease=owner.GrantControl(c.View,c.Document);Start(c,work);Need(work.Snapshot.ElapsedMilliseconds==0,"New grant inherited old progress.");}
        });
        Test("document reload or replaced control grant cancels ready work and old authority cannot claim",()=>{
            foreach(bool reload in new[]{false,true})using(var owner=FloatGameOwner.CreateNew(P("rebind"),New())){var c=new Control(owner);var work=new FloatCampWork(owner);Start(c,work);CompleteTime(work);var oldDoc=c.Document;var oldLease=c.Lease;if(reload)c.Reload();else{owner.RevokeControl(c.Lease);c.Lease=owner.GrantControl(c.View,c.Document);}Refused(work.Claim(c.View,oldDoc,oldLease),"control-required");Need(work.Snapshot.Phase=="cancelled"&&c.State.Sequence==0,"Old authority claimed or preserved ready work.");Start(c,work);Refused(c.Claim(work),"camp-work-not-ready");}
        });
        Test("closing the menu cancels working ready and retry phases on the next host step",()=>{
            foreach(string phase in new[]{"working","ready","retry"}){bool fail=false;using(var owner=FloatGameOwner.CreateNew(P("menu"),New(),point=>{if(fail&&point==FloatSaveFaultPoint.BeforeWrite)throw new IOException("private menu failure");})){var c=new Control(owner);var work=new FloatCampWork(owner);Start(c,work);if(phase!="working")CompleteTime(work);else work.Advance(250);if(phase=="retry"){fail=true;Refused(c.Claim(work),"camp-work-not-written");}owner.SetMenuOpen(false);Need(work.Advance(0)=="menu-required"&&work.Snapshot.Phase=="cancelled"&&work.Snapshot.ElapsedMilliseconds==0&&c.State.Sequence==0,"Menu close left claimable progress.");owner.SetMenuOpen(true);fail=false;Start(c,work);Need(work.Snapshot.ElapsedMilliseconds==0,"Reopened menu restored work.");}}
        });
        Test("another confirmed transaction cancels frozen working ready and retry rounds",()=>{
            foreach(string phase in new[]{"working","ready","retry"}){bool fail=false;using(var owner=FloatGameOwner.CreateNew(P("changed"),New(wallet:1000),point=>{if(fail&&point==FloatSaveFaultPoint.BeforeWrite)throw new IOException("private changed failure");})){var c=new Control(owner);var work=new FloatCampWork(owner);Start(c,work);if(phase!="working")CompleteTime(work);else work.Advance(250);if(phase=="retry"){fail=true;Refused(c.Claim(work),"camp-work-not-written");fail=false;}Commit(c.Send(Buy(c.State,FloatSupplyProduct.Bait)));if(phase=="ready")Refused(c.Claim(work),"camp-work-state-changed");else Need(work.Advance(0)=="camp-work-state-changed","Changed state not detected.");Need(work.Snapshot.Phase=="cancelled"&&c.State.WalletMinor==980&&c.State.Sequence==1,"Changed round added reward.");}}
        });
        Test("explicit cancel clears working ready and retry rounds with no refund or persistent elapsed time",()=>{
            foreach(string phase in new[]{"working","ready","retry"}){bool fail=false;string path=P("cancel");using(var owner=FloatGameOwner.CreateNew(path,New(),point=>{if(fail&&point==FloatSaveFaultPoint.BeforeWrite)throw new IOException("private cancel failure");})){var c=new Control(owner);var work=new FloatCampWork(owner);Start(c,work);if(phase!="working")CompleteTime(work);else work.Advance(250);if(phase=="retry"){fail=true;Refused(c.Claim(work),"camp-work-not-written");}byte[] before=Bytes(path);Need(work.Cancel()=="camp-work-cancelled"&&work.Snapshot.ElapsedMilliseconds==0&&FrozenCommand(work)==null&&Same(before,Bytes(path))&&c.State.Sequence==0,"Cancel saved or preserved a reward command.");fail=false;Start(c,work);Refused(c.Claim(work),"camp-work-not-ready");}using(var owner=FloatGameOwner.OpenExisting(path,null,3)){var c=new Control(owner);var work=new FloatCampWork(owner);Need(work.Snapshot.Phase=="idle"&&work.Snapshot.ElapsedMilliseconds==0&&c.State.WalletMinor==0,"Restart restored unfinished work.");}}
        });
        Test("known prewrite claim failure retains the exact command and explicit retry confirms once",()=>{
            bool fail=false;string path=P("known");using(var owner=FloatGameOwner.CreateNew(path,New(),point=>{if(fail&&point==FloatSaveFaultPoint.BeforeWrite)throw new IOException("private known camp failure");})){var c=new Control(owner);var work=new FloatCampWork(owner);Start(c,work);CompleteTime(work);byte[] before=Bytes(path);fail=true;Refused(c.Claim(work),"camp-work-not-written");var command=FrozenCommand(work);Need(command!=null&&work.Snapshot.Phase=="retry"&&Same(before,Bytes(path))&&!owner.Snapshot.Faulted,"Known failure lost exact retry.");work.Advance(250);Refused(c.Claim(work),"camp-work-not-written");Need(Object.ReferenceEquals(command,FrozenCommand(work)),"Second known attempt replaced command.");fail=false;var result=c.Claim(work);Commit(result);Need(result.Receipt.RequestId==command.RequestId&&result.Receipt.EventId==command.EventId&&c.State.WalletMinor==800&&c.State.Sequence==1,"Retry changed reward identity.");Refused(c.Claim(work),"camp-work-not-ready");}
        });
        Test("unknown completed claim freezes timer and a fresh owner reopens one reward without unfinished time",()=>{
            foreach(var fault in new[]{FloatSaveFaultPoint.AfterFrame,FloatSaveFaultPoint.AfterFlush}){bool fail=false;string path=P("unknown");using(var owner=FloatGameOwner.CreateNew(path,New(),point=>{if(fail&&point==fault)throw new IOException("private unknown camp failure");})){var c=new Control(owner);var work=new FloatCampWork(owner);Start(c,work);CompleteTime(work);fail=true;Refused(c.Claim(work),"camp-work-save-unknown");Need(owner.Snapshot.Faulted&&owner.Snapshot.State==null&&work.Snapshot.Phase=="unknown","Unknown claim exposed usable state.");Need(work.Cancel()=="camp-work-save-unknown"&&c.Start(work)=="camp-work-save-unknown"&&work.Advance(0)=="camp-work-save-unknown","Unknown phase could be bypassed.");Refused(c.Claim(work),"camp-work-save-unknown");}byte[] before=Bytes(path);using(var owner=FloatGameOwner.OpenExisting(path,null,3)){var c=new Control(owner);var work=new FloatCampWork(owner);Need(c.State.Sequence==1&&c.State.WalletMinor==800&&work.Snapshot.Phase=="idle"&&work.Snapshot.ElapsedMilliseconds==0&&Same(before,Bytes(path)),"Unknown reopen repeated or lost reward.");Start(c,work);Refused(c.Claim(work),"camp-work-not-ready");}}
        });
        Test("partial claim frame freezes the timer and strict restart preserves unreadable bytes",()=>{
            bool fail=false;string path=P("partial");using(var owner=FloatGameOwner.CreateNew(path,New(),point=>{if(fail&&point==FloatSaveFaultPoint.AfterPrefix)throw new IOException("private partial camp failure");})){var c=new Control(owner);var work=new FloatCampWork(owner);Start(c,work);CompleteTime(work);fail=true;Refused(c.Claim(work),"camp-work-save-unknown");Need(work.Snapshot.Phase=="unknown","Partial claim did not freeze.");}byte[] before=Bytes(path);Throws<InvalidDataException>(()=>{using(var owner=FloatGameOwner.OpenExisting(path,null,3)){}});Need(Same(before,Bytes(path)),"Partial claim was trimmed or repaired.");
        });
        Test("wrong thread reentrant tick and disposed owner cannot continue accumulating work",()=>{
            FloatCampWork work=null;bool reenter=false,blocked=false;var owner=FloatGameOwner.CreateNew(P("thread"),New(),point=>{if(reenter&&point==FloatSaveFaultPoint.BeforeWrite){try{work.Advance(0);}catch(InvalidOperationException){blocked=true;}}});var c=new Control(owner);work=new FloatCampWork(owner);Start(c,work);Exception threadError=null;var worker=new Thread(()=>{try{work.Advance(250);}catch(Exception error){threadError=error;}});worker.Start();worker.Join();Need(threadError is InvalidOperationException&&work.Snapshot.ElapsedMilliseconds==0,"Foreign thread changed time.");CompleteTime(work);reenter=true;Commit(c.Claim(work));Need(blocked,"Reentrant claim hook advanced work.");Start(c,work);owner.Dispose();Need(work.Advance(0)=="owner-unavailable"&&work.Snapshot.Phase=="cancelled","Disposed owner left work active.");
        });
        Test("unexpected non-IO prewrite exception propagates instead of claiming a known successful reward",()=>{
            bool fail=false;using(var owner=FloatGameOwner.CreateNew(P("unexpected"),New(),point=>{if(fail&&point==FloatSaveFaultPoint.BeforeWrite)throw new InvalidOperationException("private unexpected failure");})){var c=new Control(owner);var work=new FloatCampWork(owner);Start(c,work);CompleteTime(work);fail=true;Throws<InvalidOperationException>(()=>c.Claim(work));Need(c.State.Sequence==0&&c.State.WalletMinor==0&&work.Snapshot.Phase=="ready","Unexpected exception was silently converted to completion.");var command=FrozenCommand(work);fail=false;var result=c.Claim(work);Commit(result);Need(result.Receipt.RequestId==command.RequestId&&c.State.Sequence==1,"Explicit attempt after known injected error changed command.");}
        });
        Test("admission refuses exhausted transaction or 199-byte budget and exact 200-byte frame remains eligible",()=>{
            string path=P("budget-start");using(var owner=FloatGameOwner.CreateNew(path,New())){var c=new Control(owner);var work=new FloatCampWork(owner);var s=c.State;var command=new HostFloatCampReward(s.ProfileId,s.BranchId,G(),G(),60000,1,s.Sequence,s.Rig.Revision,s.RulesVersion);Need(FloatJournalCodec.EncodeCommand(command,3).Length+76==FloatCampWork.RequiredClaimFrameBytes&&FloatCampWork.RequiredClaimFrameBytes==200,"Claim frame budget differs from actual codec.");byte[] before=Bytes(path);
                WithBudgetCounters(owner,0,200,()=>Need(c.Start(work)=="save-capacity"&&work.Snapshot.Phase=="idle","Exhausted transaction budget allowed work."));
                WithBudgetCounters(owner,1,199,()=>Need(c.Start(work)=="save-capacity"&&work.Snapshot.ElapsedMilliseconds==0,"Insufficient bytes allowed work."));
                WithBudgetCounters(owner,1,200,()=>{Start(c,work);Need(work.Snapshot.Phase=="working","Exact frame capacity was refused.");work.Cancel();});
                Need(Same(before,Bytes(path))&&c.State.Sequence==0&&c.State.WalletMinor==0,"Counter admission wrote fixture bytes.");
            }
        });
        Test("ready and known retry recheck reward capacity before additional time or claim",()=>{
            foreach(bool retry in new[]{false,true}){bool fail=false;string path=P("budget-ready");using(var owner=FloatGameOwner.CreateNew(path,New(),point=>{if(fail&&point==FloatSaveFaultPoint.BeforeWrite)throw new IOException("private capacity retry");})){var c=new Control(owner);var work=new FloatCampWork(owner);Start(c,work);CompleteTime(work);if(retry){fail=true;Refused(c.Claim(work),"camp-work-not-written");fail=false;}byte[] before=Bytes(path);WithBudgetCounters(owner,10000,199,()=>{if(retry)Need(work.Advance(0)=="save-capacity","Retry tick ignored current budget.");else Refused(c.Claim(work),"save-capacity");Need(work.Snapshot.Phase=="cancelled"&&work.Snapshot.ElapsedMilliseconds==0,"Exhausted reward round stayed claimable.");});Need(Same(before,Bytes(path))&&c.State.Sequence==0,"Capacity recheck charged or wrote.");}}
        });
        Test("type20 codec is strict and invalid durations or policy versions never write rewards",()=>{
            var initial=New();var command=Reward(initial);byte[] bytes=FloatJournalCodec.EncodeCommand(command);Need(bytes[4]==3&&bytes[7]==20&&FloatJournalCodec.DecodeCommand(bytes).Fingerprint==command.Fingerprint&&Same(bytes,FloatJournalCodec.EncodeCommand(FloatJournalCodec.DecodeCommand(bytes))),"Reward codec changed identity.");
            for(int n=0;n<bytes.Length;n++){var prefix=new byte[n];Array.Copy(bytes,prefix,n);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(prefix));}var tail=new byte[bytes.Length+1];Array.Copy(bytes,tail,bytes.Length);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(tail));
            foreach(byte schema in new byte[]{1,2}){Throws<InvalidDataException>(()=>FloatJournalCodec.EncodeCommand(command,schema));var bad=(byte[])bytes.Clone();bad[4]=schema;Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bad));}var session=(byte[])bytes.Clone();Array.Copy(G().ToByteArray(),0,session,72,16);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(session));
            string path=P("invalid-policy");using(var owner=FloatGameOwner.CreateNew(path,initial)){var c=new Control(owner);byte[] before=Bytes(path);foreach(int duration in new[]{-1,0,59999,60001,Int32.MaxValue})Refused(owner.ExecuteHost(Reward(c.State,duration:duration)),"camp-duration-invalid");foreach(long policy in new long[]{-1,0,2,Int64.MaxValue})Refused(owner.ExecuteHost(Reward(c.State,policy:policy)),"camp-policy-unsupported");Need(c.State.Sequence==0&&c.State.WalletMinor==0&&Same(before,Bytes(path)),"Invalid completion fact wrote funds.");}
        });
        Test("confirmed reward event cannot become another request event session fish encounter or purchased asset",()=>{
            var initial=New(stamina:100,bait:12);var offers=new FloatEquipmentOffers(1,initial.Equipment.Catalog,new[]{new FloatEquipmentOffer("hook-one","hook",FloatEquipmentKind.Hook,35)},initial.Rules.MaximumWalletMinor);string path=P("reward-identities");
            using(var owner=FloatGameOwner.CreateNewWithEquipmentOffers(path,initial,offers)){var c=new Control(owner);var reward=Reward(c.State);Commit(owner.ExecuteHost(reward));long sequence=c.State.Sequence;byte[] before=Bytes(path);
                Refused(owner.ExecuteHost(Reward(c.State,request:reward.EventId)),"request-identity-used");Refused(owner.ExecuteHost(Reward(c.State,businessEvent:reward.EventId)),"event-already-used");var purchase=new BuyFloatEquipment(c.State.ProfileId,c.State.BranchId,G(),G(),FloatEquipmentPurchaseQuote.Create(c.State,"hook-one"),reward.EventId);Refused(c.Send(purchase),"asset-identity-used");Need(Same(before,Bytes(path)),"Reserved reward identity performed a write.");
                owner.SetMenuOpen(false);Refused(c.Send(Cast(c.State,reward.EventId)),"session-identity-used");Commit(c.Send(Cast(c.State)));Refused(owner.ExecuteHost(Fish(c.State,id:reward.EventId)),"fish-identity-used");Refused(owner.ExecuteHost(Fish(c.State,encounter:reward.EventId)),"encounter-identity-used");var s=c.State;Commit(c.Send(new RecallFloat(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,s.Rig.Revision,s.RulesVersion)));owner.SetMenuOpen(true);
                before=Bytes(path);var replay=owner.ExecuteHost(reward);Need(replay.IsReplay&&!replay.Written&&replay.Receipt.RequestId==reward.RequestId&&c.State.Sequence==sequence+2&&c.State.WalletMinor==800&&Same(before,Bytes(path)),"Reward replay paid again or could not return the original receipt.");
            }
        });
        return failed==0?0:1;
    }
}
