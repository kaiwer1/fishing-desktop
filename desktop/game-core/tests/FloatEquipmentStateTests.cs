using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FishingDesktop.GameCore;

static class FloatEquipmentStateTests
{
    static int passed,failed,nextId,nextFile;static string root;
    static Guid G(){return new Guid(++nextId,0,0,new byte[]{0,0,0,0,0,0,0,73});}
    static Guid GoldenId(int n){return new Guid(n,0,0,new byte[]{0,0,0,0,0,0,0,77});}
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    static void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error);}}
    static string PathFor(string label){return Path.Combine(root,(++nextFile)+"-"+label+".fltj");}
    static string Sha(byte[] bytes){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    static byte[] Bytes(string path){using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){var bytes=new byte[(int)file.Length];int at=0;while(at<bytes.Length){int n=file.Read(bytes,at,bytes.Length-at);if(n==0)throw new EndOfStreamException();at+=n;}return bytes;}}
    static bool Same(byte[] a,byte[] b){return Sha(a)==Sha(b);}
    static FloatGameRules Rules(){return new FloatGameRules(1,"float-rig","worm",2,100,1,1000000,new[]{new FloatSpeciesRule("carp",100000,2000000,1000)});}
    static FloatCommerceCatalog Commerce(){return new FloatCommerceCatalog(1,"worm","landing-net","bread",2400,5,20,20,20,1000,1000,100);}
    static FloatEquipmentCatalog Catalog()
    {
        return new FloatEquipmentCatalog(7,"worm",new[]{
            new FloatEquipmentDefinition("rod",FloatEquipmentKind.Rod,200000,0,1000,20000),new FloatEquipmentDefinition("reel",FloatEquipmentKind.Reel,250000),
            new FloatEquipmentDefinition("line",FloatEquipmentKind.Line,0,200),new FloatEquipmentDefinition("hook",FloatEquipmentKind.Hook,500),
            new FloatEquipmentDefinition("bobber",FloatEquipmentKind.Bobber,1000,0,500,8000),new FloatEquipmentDefinition("sinker",FloatEquipmentKind.Sinker,3000)},
            new[]{new FloatEquipmentPairRule("rod","reel",true)},new[]{new FloatEquipmentSpoolRule("reel","line",200,50000)},
            new[]{new FloatEquipmentPairRule("hook","worm",true)},new[]{new FloatEquipmentLossRule("terminal",new[]{FloatEquipmentSlot.Hook})});
    }
    sealed class Fixture
    {
        internal readonly Guid Rig=G(),Rod=G(),Reel=G(),Line=G(),Hook=G(),Bobber=G(),Sinker=G(),SpareHook=G(),SpareReel=G(),SpareLine=G();
        internal readonly List<FloatEquipmentInstance> Items=new List<FloatEquipmentInstance>();
        internal Fixture(){
            Items.Add(Item(Rod,"rod",FloatEquipmentKind.Rod,FloatEquipmentLocation.RigSlot(Rig,FloatEquipmentSlot.Rod)));
            Items.Add(Item(Reel,"reel",FloatEquipmentKind.Reel,FloatEquipmentLocation.RigSlot(Rig,FloatEquipmentSlot.Reel)));
            Items.Add(new FloatEquipmentInstance(Line,"line",FloatEquipmentKind.Line,1,FloatEquipmentLocation.ReelSpool(Reel),50000,50000));
            Items.Add(Item(Hook,"hook",FloatEquipmentKind.Hook,FloatEquipmentLocation.RigSlot(Rig,FloatEquipmentSlot.Hook)));
            Items.Add(Item(Bobber,"bobber",FloatEquipmentKind.Bobber,FloatEquipmentLocation.RigSlot(Rig,FloatEquipmentSlot.Bobber)));
            Items.Add(Item(Sinker,"sinker",FloatEquipmentKind.Sinker,FloatEquipmentLocation.RigSlot(Rig,FloatEquipmentSlot.Sinker)));
            Items.Add(Item(SpareHook,"hook",FloatEquipmentKind.Hook,FloatEquipmentLocation.Inventory()));
            Items.Add(Item(SpareReel,"reel",FloatEquipmentKind.Reel,FloatEquipmentLocation.Inventory()));
            Items.Add(new FloatEquipmentInstance(SpareLine,"line",FloatEquipmentKind.Line,1,FloatEquipmentLocation.Inventory(),20000,20000));
        }
        internal FloatEquipmentLedger Ledger(){return new FloatEquipmentLedger(Rig,1,1,Catalog(),Items);}
        internal FloatGameState State(int bait=12,int stamina=100,long wallet=12000,FloatEquipmentLedger ledger=null)
        {return FloatGameState.CreateNewWithEquipment(G(),G(),wallet,stamina,G(),"worm",bait,ledger??Ledger(),Rules(),Commerce(),G());}
    }
    static FloatEquipmentInstance Item(Guid id,string key,FloatEquipmentKind kind,FloatEquipmentLocation location){return new FloatEquipmentInstance(id,key,kind,1,location);}
    static ReplaceFloatSlot Replace(FloatGameState s,FloatEquipmentSlot slot,Guid? id){return new ReplaceFloatSlot(s.ProfileId,s.BranchId,G(),G(),slot,id,s.Sequence,s.Rig.Revision,s.RulesVersion,s.Equipment.InventoryRevision,s.Equipment.RulesVersion);}
    static MoveFloatLine Move(FloatGameState s,Guid line,Guid? reel){return new MoveFloatLine(s.ProfileId,s.BranchId,G(),G(),line,reel,s.Sequence,s.Rig.Revision,s.RulesVersion,s.Equipment.InventoryRevision,s.Equipment.RulesVersion);}
    static CastFloatWithEquipment Cast(FloatGameState s,long required=15000,Guid? session=null){return new CastFloatWithEquipment(s.ProfileId,s.BranchId,G(),G(),session??G(),required,s.Sequence,s.Rig.Revision,s.RulesVersion,s.Equipment.InventoryRevision,s.Equipment.RulesVersion);}
    static RecallFloat Recall(FloatGameState s){return new RecallFloat(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static HostFloatFish Fish(FloatGameState s,Guid? id=null){return new HostFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,id??G(),G(),"carp",500000,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static BuyFloatSupply Buy(FloatGameState s,FloatSupplyProduct product,Guid? id=null){return new BuyFloatSupply(s.ProfileId,s.BranchId,G(),G(),FloatPurchaseQuote.Create(s,product,1),product==FloatSupplyProduct.Net?(id??G()):Guid.Empty,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static void Commit(FloatCommitResult result){Need(result.Committed&&result.Written&&!result.IsReplay,"Expected new commit: "+result.Code);}
    static void Refused(FloatCommitResult result,string code){Need(!result.Committed&&!result.Written&&result.Receipt==null&&result.Code==code,"Expected refusal "+code+" got "+result.Code);}
    static FloatGameState Adopt(FloatGameState state,FloatCommand command){var r=FloatGameReducer.Reduce(state,command,state.Rules);Need(r.HasCandidate,"Expected candidate: "+r.Code);return r.Candidate;}
    sealed class Control
    {
        internal readonly FloatGameOwner Owner;internal readonly FloatViewTicket View;internal FloatDocumentTicket Document;internal FloatControlLease Lease;
        internal Control(FloatGameOwner owner){Owner=owner;View=owner.OpenView(FloatViewRole.Interaction);Document=owner.AttachDocument(View);owner.SetHostPaused(false);Lease=owner.GrantControl(View,Document);}
        internal FloatGameState State{get{return Owner.Snapshot.State;}}
        internal FloatCommitResult Send(FloatCommand command){return Owner.ExecuteUser(View,Document,Lease,command);}
    }
    static void AppendFixtureFrame(string path,long sequence,byte[] payload)
    {
        byte[] existing=Bytes(path),previous=new byte[32];Array.Copy(existing,existing.Length-32,previous,0,32);
        byte[] body;using(var memory=new MemoryStream()){using(var writer=new BinaryWriter(memory,Encoding.UTF8,true)){writer.Write(payload.Length);writer.Write(sequence);writer.Write(previous);writer.Write(payload);}body=memory.ToArray();}
        using(var file=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.None)){file.Write(body,0,body.Length);using(var hash=SHA256.Create()){var digest=hash.ComputeHash(body);file.Write(digest,0,digest.Length);}}
    }
    static int Main(string[] args)
    {
        if(args.Length!=1||!Directory.Exists(args[0]))return 2;root=Path.GetFullPath(args[0]);
        Test("schema3 genesis round trips the sole ledger and no supplies are gifted",()=>{
            var f=new Fixture();var s=f.State(0,0,0);string path=PathFor("genesis");byte[] expected=FloatEquipmentCodec.Encode(s.Equipment);
            using(var store=FloatGameStore.CreateNew(path,s))Need(store.State.LegacyRig==null,"Shadow legacy rig present.");
            using(var store=FloatGameStore.OpenExisting(path)){s=store.State;Need(s.SchemaVersion==3&&s.LegacyRig==null&&Same(expected,FloatEquipmentCodec.Encode(s.Equipment))&&s.Rig.LineId==f.Line&&s.Rig.LineLengthMm==50000&&s.WalletMinor==0&&s.BaitCount==0&&s.Stamina==0&&s.FoodCount==0&&s.OwnedNet==null,"Genesis changed ownership or granted supplies.");}
        });
        Test("empty and incomplete assemblies survive schema3 genesis without invented IDs",()=>{
            var f=new Fixture();var empty=new FloatEquipmentLedger(f.Rig,1,1,Catalog(),new FloatEquipmentInstance[0]);var s=f.State(ledger:empty);var copy=FloatJournalCodec.DecodeGenesis(FloatJournalCodec.EncodeGenesis(s));Need(copy.Rig.RodId==null&&copy.Rig.ReelId==null&&copy.Rig.LineId==null&&copy.Rig.LineLengthMm==null&&copy.Equipment.Instances.Count==0,"Missing items were invented.");
            var partial=f.Ledger().ReplaceSlot(FloatEquipmentGate.Idle,1,1,FloatEquipmentSlot.Hook,null).Candidate;s=f.State(ledger:partial);copy=FloatJournalCodec.DecodeGenesis(FloatJournalCodec.EncodeGenesis(s));Need(copy.Rig.HookId==null&&copy.Equipment.Find(f.Hook).Location.Place==FloatEquipmentPlace.Inventory,"Missing hook cannot be saved.");
        });
        Test("state rejects dual ownership cross inventory identity and invalid equipment config",()=>{
            var f=new Fixture();var s=f.State();var legacy=new FixedFloatRig(G(),1,"float-rig",G(),G(),G(),50000,G(),G(),G());
            Throws<ArgumentException>(()=>new FloatGameState(s.ProfileId,s.BranchId,0,0,0,s.BaitStackId,"worm",0,legacy,null,new FloatFish[0],new FloatReceipt[0],Rules(),3,Commerce(),s.FoodStackId,0,null,Guid.Empty,null,s.Equipment));
            Throws<ArgumentException>(()=>new FloatGameState(s.ProfileId,s.BranchId,0,0,0,s.BaitStackId,"worm",0,legacy,null,new FloatFish[0],new FloatReceipt[0],Rules(),2,Commerce(),s.FoodStackId,0,null,Guid.Empty,null,s.Equipment));
            Throws<ArgumentException>(()=>FloatGameState.CreateNewWithEquipment(G(),G(),0,0,f.SpareHook,"worm",0,f.Ledger(),Rules(),Commerce(),G()));
            Throws<ArgumentException>(()=>FloatGameState.CreateNewWithEquipment(G(),G(),0,0,G(),"worm",0,f.Ledger(),Rules(),Commerce(),f.Line));
        });
        Test("new command codec preserves exact typed identity and rejects all truncations",()=>{
            var f=new Fixture();var s=f.State();foreach(var command in new FloatCommand[]{Replace(s,FloatEquipmentSlot.Hook,null),Replace(s,FloatEquipmentSlot.Hook,f.SpareHook),Move(s,f.Line,null),Move(s,f.Line,f.SpareReel),Cast(s)}){
                byte[] bytes=FloatJournalCodec.EncodeCommand(command);Need(FloatJournalCodec.CommandSchema(bytes)==3&&FloatJournalCodec.DecodeCommand(bytes).Fingerprint==command.Fingerprint,"New command changed fields.");
                for(int n=0;n<bytes.Length;n++){var shortBytes=new byte[n];Array.Copy(bytes,shortBytes,n);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(shortBytes));}
                var trailing=new byte[bytes.Length+1];Array.Copy(bytes,trailing,bytes.Length);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(trailing));Throws<InvalidDataException>(()=>FloatJournalCodec.EncodeCommand(command,2));
            }
        });
        Test("owner remove hook reopen refuses cast with zero cost then restores original hook",()=>{
            var f=new Fixture();string path=PathFor("hook-flow");ReplaceFloatSlot remove;
            using(var owner=FloatGameOwner.CreateNew(path,f.State())){var c=new Control(owner);remove=Replace(c.State,FloatEquipmentSlot.Hook,null);Commit(c.Send(remove));Need(c.State.Rig.HookId==null&&c.State.Equipment.Find(f.Hook).Location.Place==FloatEquipmentPlace.Inventory,"Hook duplicated or lost.");}
            using(var owner=FloatGameOwner.OpenExisting(path,null,3)){var c=new Control(owner);byte[] before=Bytes(path);Refused(c.Send(Cast(c.State)),"missing-component");Need(Same(before,Bytes(path))&&c.State.BaitCount==12&&c.State.Stamina==100&&c.State.Sequence==1,"Missing hook charged a cast.");
                Need(c.Send(remove).IsReplay&&c.State.Sequence==1,"Old detach repeated.");Commit(c.Send(Replace(c.State,FloatEquipmentSlot.Hook,f.Hook)));Commit(c.Send(Cast(c.State)));Need(c.State.BaitCount==11&&c.State.Stamina==98&&c.State.Session.RequiredLineMm==15000&&c.State.Session.EquipmentInventoryRevision==3,"Restored rig did not cast exactly once.");Commit(c.Send(Recall(c.State)));}
            using(var store=FloatGameStore.OpenExisting(path))Need(store.State.Rig.HookId==f.Hook&&store.State.Sequence==4&&store.State.Equipment.Instances.Count==9,"Replay duplicated assets.");
        });
        Test("detaching loaded reel keeps original line on that reel through restart",()=>{
            var f=new Fixture();string path=PathFor("reel");using(var store=FloatGameStore.CreateNew(path,f.State())){Commit(store.Execute(Move(store.State,f.SpareLine,f.SpareReel)));Commit(store.Execute(Replace(store.State,FloatEquipmentSlot.Reel,f.SpareReel)));Need(store.State.Rig.LineId==f.SpareLine&&store.State.Rig.LineLengthMm==20000&&store.State.Equipment.Spool(f.Reel).Id==f.Line,"Changing reel moved old line to new reel.");}
            using(var store=FloatGameStore.OpenExisting(path)){Need(store.State.Equipment.Find(f.Reel).Location.Place==FloatEquipmentPlace.Inventory&&store.State.Equipment.Find(f.Line).Location.OwnerId==f.Reel&&store.State.Equipment.RemainingLineMm==70000,"Loaded reel lost ownership or length.");Commit(store.Execute(Replace(store.State,FloatEquipmentSlot.Reel,null)));Need(store.State.Rig.ReelId==null&&store.State.Rig.LineId==null&&store.State.Rig.LineLengthMm==null&&store.State.Equipment.Spool(f.SpareReel).Id==f.SpareLine,"Detached line leaked into rig descriptor.");}
        });
        Test("whole line moves conserve millimetres and cannot overwrite occupied spools",()=>{
            var f=new Fixture();string path=PathFor("line");using(var store=FloatGameStore.CreateNew(path,f.State())){Commit(store.Execute(Move(store.State,f.Line,null)));Refused(store.Execute(Cast(store.State)),"missing-line");Commit(store.Execute(Move(store.State,f.SpareLine,f.Reel)));var bytes=Bytes(path);Refused(store.Execute(Move(store.State,f.Line,f.Reel)),"spool-occupied");Need(Same(bytes,Bytes(path)),"Occupied spool rejection wrote.");Refused(store.Execute(Cast(store.State,20001)),"insufficient-line");Commit(store.Execute(Move(store.State,f.Line,f.SpareReel)));Need(store.State.Equipment.RemainingLineMm==70000&&store.State.Equipment.LostLineMm==0&&store.State.Rig.LineLengthMm==20000,"Whole line movement shortened or cloned line.");}
            using(var store=FloatGameStore.OpenExisting(path))Need(store.State.Equipment.Spool(f.Reel).Id==f.SpareLine&&store.State.Equipment.Spool(f.SpareReel).Id==f.Line,"Restart changed spool references.");
        });
        Test("current menu can edit but cannot cast and no-op edits never append",()=>{
            var f=new Fixture();using(var owner=FloatGameOwner.CreateNew(PathFor("menu"),f.State())){var c=new Control(owner);owner.SetMenuOpen(true);Refused(c.Send(Cast(c.State)),"domain-paused");Refused(c.Send(Replace(c.State,FloatEquipmentSlot.Hook,f.Hook)),"assembly-unchanged");Refused(c.Send(Move(c.State,f.Line,f.Reel)),"line-unchanged");Commit(c.Send(Replace(c.State,FloatEquipmentSlot.Hook,null)));Need(c.State.Sequence==1&&!owner.Snapshot.CanSimulate,"Menu edit was rejected or simulated.");}
        });
        Test("active and pending sessions block all equipment edits without changing resources",()=>{
            var f=new Fixture();string path=PathFor("busy");using(var owner=FloatGameOwner.CreateNew(path,f.State())){var c=new Control(owner);Commit(c.Send(Cast(c.State)));Refused(c.Send(Replace(c.State,FloatEquipmentSlot.Hook,null)),"session-busy");Refused(c.Send(Move(c.State,f.Line,null)),"session-busy");Commit(owner.ExecuteHost(Fish(c.State)));Commit(c.Send(Cast(c.State)));Commit(owner.ExecuteHost(Fish(c.State)));Need(c.State.Session.CapacityBlocked,"Pending fixture missing.");Refused(c.Send(Replace(c.State,FloatEquipmentSlot.Hook,null)),"session-busy");Refused(c.Send(Move(c.State,f.Line,null)),"session-busy");}
            using(var owner=FloatGameOwner.OpenExisting(path,null,3))Need(owner.Snapshot.State.Session.CapacityBlocked&&owner.Snapshot.State.Rig.HookId==f.Hook&&owner.Snapshot.State.Sequence==4,"Restart discarded pending fish or altered equipment.");
        });
        Test("stale equipment inventory and catalogue versions reject before writing",()=>{
            var f=new Fixture();using(var store=FloatGameStore.CreateNew(PathFor("versions"),f.State())){var s=store.State;
                Refused(store.Execute(new ReplaceFloatSlot(s.ProfileId,s.BranchId,G(),G(),FloatEquipmentSlot.Hook,null,0,1,1,2,7)),"equipment-version-conflict");
                Refused(store.Execute(new MoveFloatLine(s.ProfileId,s.BranchId,G(),G(),f.Line,null,0,1,1,1,8)),"equipment-version-conflict");
                Refused(store.Execute(new CastFloatWithEquipment(s.ProfileId,s.BranchId,G(),G(),G(),15000,0,1,1,2,7)),"equipment-version-conflict");Need(store.State.Sequence==0,"Stale command changed state.");}
        });
        Test("duplicate equipment requests replay while changed payload and branch reject",()=>{
            var f=new Fixture();using(var store=FloatGameStore.CreateNew(PathFor("duplicate"),f.State())){var s=store.State;var first=Replace(s,FloatEquipmentSlot.Hook,null);Commit(store.Execute(first));byte[] before=FloatEquipmentCodec.Encode(store.State.Equipment);Need(store.Execute(first).IsReplay,"Confirmed edit not replayed.");
                Refused(store.Execute(new ReplaceFloatSlot(s.ProfileId,s.BranchId,first.RequestId,first.EventId,FloatEquipmentSlot.Hook,f.SpareHook,0,1,1,1,7)),"request-conflict");
                Refused(store.Execute(new ReplaceFloatSlot(s.ProfileId,G(),first.RequestId,first.EventId,FloatEquipmentSlot.Hook,null,0,1,1,1,7)),"branch-mismatch");Need(Same(before,FloatEquipmentCodec.Encode(store.State.Equipment))&&store.State.Sequence==1,"Replay changed equipment.");}
        });
        Test("observer revoked document and host channels cannot issue equipment changes",()=>{
            var f=new Fixture();using(var owner=FloatGameOwner.CreateNew(PathFor("authority"),f.State())){var c=new Control(owner);var edit=Replace(c.State,FloatEquipmentSlot.Hook,null);var observer=owner.OpenView(FloatViewRole.Observer);var doc=owner.AttachDocument(observer);var denied=owner.ExecuteUser(observer,doc,c.Lease,edit);Refused(denied,"control-required");Need(denied.State==null,"Observer learned command state.");Refused(owner.ExecuteHost(edit),"host-command-not-allowed");Commit(c.Send(edit));owner.RevokeControl(c.Lease);denied=c.Send(edit);Refused(denied,"control-required");Need(denied.State==null,"Revoked receipt leaked.");c.Lease=owner.GrantControl(c.View,c.Document);var old=c.Document;var lease=c.Lease;c.Document=owner.AttachDocument(c.View);c.Lease=owner.GrantControl(c.View,c.Document);Refused(owner.ExecuteUser(c.View,old,lease,edit),"control-required");Need(c.Send(edit).IsReplay,"Current lease cannot query own known receipt.");}
        });
        Test("known prewrite failure leaves bytes and equipment unchanged for exact retry",()=>{
            var f=new Fixture();string path=PathFor("known");bool armed=false;using(var owner=FloatGameOwner.CreateNew(path,f.State(),point=>{if(armed&&point==FloatSaveFaultPoint.BeforeWrite)throw new IOException("private fixture");})){var c=new Control(owner);var edit=Replace(c.State,FloatEquipmentSlot.Hook,null);byte[] before=Bytes(path);armed=true;Throws<IOException>(()=>c.Send(edit));Need(!owner.Snapshot.Faulted&&c.State.Rig.HookId==f.Hook&&Same(before,Bytes(path)),"Known failure partially changed equipment.");armed=false;Commit(c.Send(edit));Need(c.Send(edit).IsReplay&&c.State.Sequence==1,"Exact retry changed equipment twice.");}
        });
        Test("uncertain complete equipment write hides state and reopens exactly once",()=>{
            var f=new Fixture();string path=PathFor("unknown");bool armed=false;ReplaceFloatSlot edit;
            using(var owner=FloatGameOwner.CreateNew(path,f.State(),point=>{if(armed&&point==FloatSaveFaultPoint.AfterFlush)throw new IOException("private fixture");})){var c=new Control(owner);edit=Replace(c.State,FloatEquipmentSlot.Hook,null);armed=true;Throws<FloatSaveUncertainException>(()=>c.Send(edit));Need(owner.Snapshot.Faulted&&owner.Snapshot.State==null,"Uncertain write exposed old equipment.");Refused(c.Send(edit),"control-required");}
            using(var owner=FloatGameOwner.OpenExisting(path,null,3)){var c=new Control(owner);Need(c.State.Rig.HookId==null&&c.State.Sequence==1&&c.Send(edit).IsReplay,"Uncertain saved edit lost or applied twice.");}
        });
        Test("partial equipment frame refuses restart and is never trimmed or repaired",()=>{
            var f=new Fixture();string path=PathFor("partial");bool armed=false;using(var owner=FloatGameOwner.CreateNew(path,f.State(),point=>{if(armed&&point==FloatSaveFaultPoint.AfterPrefix)throw new IOException("private fixture");})){var c=new Control(owner);armed=true;Throws<FloatSaveUncertainException>(()=>c.Send(Move(c.State,f.Line,null)));}
            byte[] before=Bytes(path);Throws<InvalidDataException>(()=>{using(var owner=FloatGameOwner.OpenExisting(path,null,3)){}});Need(Same(before,Bytes(path)),"Partial tail was repaired.");
        });
        Test("schema2 to schema3 frames are rejected even with correct frame hash",()=>{
            var f=new Fixture();var s=f.State();var legacy=new FixedFloatRig(f.Rig,1,"float-rig",f.Rod,f.Reel,f.Line,50000,f.Hook,f.Bobber,f.Sinker);var old=FloatGameState.CreateNewWithCommerce(s.ProfileId,s.BranchId,12000,100,s.BaitStackId,"worm",12,legacy,Rules(),Commerce(),s.FoodStackId);string path=PathFor("no-upgrade");using(var store=FloatGameStore.CreateNew(path,old)){}
            AppendFixtureFrame(path,1,FloatJournalCodec.EncodeCommand(Replace(s,FloatEquipmentSlot.Hook,null)));byte[] before=Bytes(path);Throws<InvalidDataException>(()=>{using(var store=FloatGameStore.OpenExisting(path)){}});Need(Same(before,Bytes(path)),"Unsupported upgrade changed source.");
        });
        Test("schema3 cannot silently downgrade to an older command payload",()=>{
            var f=new Fixture();var s=f.State();string path=PathFor("no-downgrade");using(var store=FloatGameStore.CreateNew(path,s)){}
            AppendFixtureFrame(path,1,FloatJournalCodec.EncodeCommand(Buy(s,FloatSupplyProduct.Bait),2));Throws<InvalidDataException>(()=>{using(var store=FloatGameStore.OpenExisting(path)){}});
        });
        Test("legacy cast cannot bypass readiness and new equipment commands reject old profiles",()=>{
            var f=new Fixture();var s=f.State();using(var store=FloatGameStore.CreateNew(PathFor("old-cast"),s))Refused(store.Execute(new CastFloat(s.ProfileId,s.BranchId,G(),G(),G(),0,1,1)),"equipment-cast-required");
            var legacy=new FixedFloatRig(f.Rig,1,"float-rig",f.Rod,f.Reel,f.Line,50000,f.Hook,f.Bobber,f.Sinker);var old=FloatGameState.CreateNewWithCommerce(s.ProfileId,s.BranchId,12000,100,s.BaitStackId,"worm",12,legacy,Rules(),Commerce(),s.FoodStackId);
            using(var store=FloatGameStore.CreateNew(PathFor("old-profile"),old))foreach(var command in new FloatCommand[]{Cast(s),Replace(s,FloatEquipmentSlot.Hook,null),Move(s,f.Line,null)})Refused(store.Execute(command),"equipment-schema-required");
        });
        Test("new cast uses actual installed line and resources with no spare line credit",()=>{
            var f=new Fixture();foreach(var state in new[]{f.State(0),f.State(stamina:1),f.State()})using(var store=FloatGameStore.CreateNew(PathFor("resources"),state)){
                string expected=state.BaitCount==0?"bait-empty":state.Stamina==1?"stamina-insufficient":"insufficient-line";Refused(store.Execute(Cast(state,state.BaitCount==0||state.Stamina==1?15000:50001)),expected);Need(store.State.Sequence==0&&store.State.BaitCount==state.BaitCount&&store.State.Stamina==state.Stamina,"Rejected cast consumed resources.");}
        });
        Test("cast preparation emits schema3 debit only after release and detects changed equipment",()=>{
            var f=new Fixture();using(var owner=FloatGameOwner.CreateNew(PathFor("preparation"),f.State())){var c=new Control(owner);var preparation=new FloatCastPreparation(owner,new FloatCastPreparationRules(1000,150),t=>true);var target=new FloatCastTarget(0,0,15000,1,15000);var first=preparation.Begin(c.View,c.Document,c.Lease,target);Need(first.Accepted&&c.State.Sequence==0,"Preparation debited.");Commit(c.Send(Replace(c.State,FloatEquipmentSlot.Hook,null)));Need(!preparation.Release(first.Gesture).Accepted&&c.State.BaitCount==12,"Changed preparation still valid.");Need(!preparation.Begin(c.View,c.Document,c.Lease,target).Accepted,"Missing hook began charging.");Commit(c.Send(Replace(c.State,FloatEquipmentSlot.Hook,f.Hook)));var next=preparation.Begin(c.View,c.Document,c.Lease,target);Need(next.Accepted&&preparation.Release(next.Gesture).Accepted,"Restored preparation rejected.");var result=preparation.CommitPrepared(next.Gesture);Need(result.ShouldStartFlight&&c.State.Sequence==3&&c.State.BaitCount==11&&!preparation.CommitPrepared(next.Gesture).ShouldStartFlight,"Flight/debit repeated.");}
        });
        Test("startup recovers a paid schema3 cast without restoring spent resources or changing gear",()=>{
            var f=new Fixture();string path=PathFor("startup");byte[] equipment;using(var store=FloatGameStore.CreateNew(path,f.State())){Commit(store.Execute(Cast(store.State)));equipment=FloatEquipmentCodec.Encode(store.State.Equipment);}
            using(var owner=FloatGameOwner.OpenExisting(path,null,3)){var s=owner.Snapshot.State;Need(s.Session==null&&s.Sequence==2&&s.BaitCount==11&&s.Stamina==98&&Same(equipment,FloatEquipmentCodec.Encode(s.Equipment)),"Startup refunded or changed equipment.");}
            using(var owner=FloatGameOwner.OpenExisting(path,null,3))Need(owner.Snapshot.State.Sequence==2,"Startup recovered twice.");
        });
        Test("existing supplies and unhooked terminal remain valid in schema3",()=>{
            var f=new Fixture();string path=PathFor("supplies");using(var owner=FloatGameOwner.CreateNew(path,f.State(stamina:70))){var c=new Control(owner);Commit(c.Send(Buy(c.State,FloatSupplyProduct.Net)));Commit(c.Send(Buy(c.State,FloatSupplyProduct.Food)));var s=c.State;Commit(c.Send(new EquipFloatNet(s.ProfileId,s.BranchId,G(),G(),s.OwnedNet.Id,s.Sequence,s.Rig.Revision,s.RulesVersion)));s=c.State;Commit(c.Send(new UseFloatFood(s.ProfileId,s.BranchId,G(),G(),s.FoodStackId,s.Sequence,s.Rig.Revision,s.RulesVersion)));Commit(c.Send(Cast(c.State)));s=c.State;Commit(owner.ExecuteHost(new HostFloatUnhooked(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,G(),s.Sequence,s.Rig.Revision,s.RulesVersion,2)));Need(c.State.Session==null&&c.State.Stamina==88&&c.State.FoodCount==0&&c.State.Equipment.RemainingLineMm==70000,"Schema3 existing flow changed equipment or failed.");}
            using(var store=FloatGameStore.OpenExisting(path))Need(store.State.Sequence==6&&store.State.EquippedNetId==store.State.OwnedNet.Id,"Schema3 supply history lost.");
        });
        Test("lost inventory identities cannot be reused by supplies fish or sessions",()=>{
            var f=new Fixture();var ledger=f.Ledger();var loss=ledger.PlanTerminalLoss(FloatEquipmentGate.ActiveSession,ledger.LockForSession(G()),G(),"terminal",1000,0);var s=f.State(ledger:loss.Candidate);using(var store=FloatGameStore.CreateNew(PathFor("lost-ids"),s)){
                Refused(store.Execute(Buy(store.State,FloatSupplyProduct.Net,f.Hook)),"asset-identity-used");Refused(store.Execute(Replace(store.State,FloatEquipmentSlot.Hook,f.Hook)),"item-unavailable");Commit(store.Execute(Replace(store.State,FloatEquipmentSlot.Hook,f.SpareHook)));Refused(store.Execute(Cast(store.State,15000,f.Hook)),"session-identity-used");Commit(store.Execute(Cast(store.State)));Refused(store.Execute(Fish(store.State,f.Hook)),"fish-identity-used");Need(store.State.Equipment.Find(f.Hook).Location.Place==FloatEquipmentPlace.Lost,"Lost identity resurrected.");}
        });
        Test("schema3 required schema gate refuses old active files before lifecycle writes",()=>{
            var rig=new FixedFloatRig(G(),1,"float-rig",G(),G(),G(),50000,G(),G(),G());var s=FloatGameState.CreateNewWithCommerce(G(),G(),12000,100,G(),"worm",12,rig,Rules(),Commerce(),G());string path=PathFor("old-open");using(var store=FloatGameStore.CreateNew(path,s))Commit(store.Execute(new CastFloat(s.ProfileId,s.BranchId,G(),G(),G(),0,1,1)));byte[] before=Bytes(path);Throws<InvalidOperationException>(()=>{using(var owner=FloatGameOwner.OpenExisting(path,null,3)){}});Need(Same(before,Bytes(path)),"Wrong-schema open recovered old active file.");
        });
        Test("schema1 and schema2 bytes and fingerprints match committed prechange golden hashes",Golden);
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
    static void Golden()
    {
        var rig=new FixedFloatRig(GoldenId(1),1,"float-rig",GoldenId(2),GoldenId(3),GoldenId(4),50000,GoldenId(5),GoldenId(6),GoldenId(7));var rules=Rules();var catalog=Commerce();
        var a=FloatGameState.CreateNew(GoldenId(8),GoldenId(9),12000,100,GoldenId(10),"worm",12,rig,rules);var b=FloatGameState.CreateNewWithCommerce(GoldenId(8),GoldenId(9),12000,100,GoldenId(10),"worm",12,rig,rules,catalog,GoldenId(11));
        var cast=new CastFloat(GoldenId(8),GoldenId(9),GoldenId(12),GoldenId(13),GoldenId(14),0,1,1);var buy=new BuyFloatSupply(GoldenId(8),GoldenId(9),GoldenId(15),GoldenId(16),FloatPurchaseQuote.Create(b,FloatSupplyProduct.Bait,2),Guid.Empty,0,1,1);
        var unhook=new HostFloatUnhooked(GoldenId(8),GoldenId(9),GoldenId(17),GoldenId(18),GoldenId(14),GoldenId(19),1,1,1,2);var upgrade=new UpgradeFloatProfile(GoldenId(8),GoldenId(9),GoldenId(20),GoldenId(21),GoldenId(22),GoldenId(11),new string('a',64),new string('b',64),500,0,1,1,catalog);
        var bytes=new[]{FloatJournalCodec.EncodeGenesis(a),FloatJournalCodec.EncodeGenesis(b),FloatJournalCodec.EncodeCommand(cast,1),FloatJournalCodec.EncodeCommand(cast,2),FloatJournalCodec.EncodeCommand(buy,2),FloatJournalCodec.EncodeCommand(unhook,2),FloatJournalCodec.EncodeCommand(upgrade,2),Encoding.UTF8.GetBytes(cast.Fingerprint+"\n"+buy.Fingerprint+"\n"+unhook.Fingerprint+"\n"+upgrade.Fingerprint)};
        string[] hashes={"9483417941efe1061409cf570fdc685fa04a5b3f777a79918f2b4f882ae2cfa4","953353914546189b94f9967a4d6b31aec2488a6fe0ee10e36b5aea2f04952b3e","58ba05b4ae5c1660758df5d0a39961c0531e05ff6f4d222d5c04b6bad352f23f","09f27b3672418cdadc062142e1803a503a8960f7aaeac1d11712113ae711cce5","320902d0248924b6851cf148676f31a2742926086440df81acd8a364344901ae","c5c4b64077fe27f41eef6a75f318d38a8e074b8c8f81ffad09381b8f8a89a409","a92f693b5d329953d8d9cba53791061e2c3f4dc3a66381b85b7fee16abad1c56","bfa1ef7bdbf806429a343e1f8c27976acabfd2f18743e6b390047a4521bb1ba5"};
        for(int i=0;i<bytes.Length;i++)Need(Sha(bytes[i])==hashes[i],"Legacy golden mismatch "+i);
    }
}
