using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FishingDesktop.GameCore;

static class FloatEquipmentArchiveTests
{
    static int passed,failed,nextId,nextFile;static string root;
    static Guid G(){return new Guid(++nextId,0,0,new byte[]{0,0,0,0,0,0,0,94});}
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    static void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error);}}
    static string P(string label){return Path.Combine(root,(++nextFile)+"-"+label+".fltj");}
    static byte[] Bytes(string path){using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){var bytes=new byte[(int)file.Length];int at=0;while(at<bytes.Length){int count=file.Read(bytes,at,bytes.Length-at);if(count==0)throw new EndOfStreamException();at+=count;}return bytes;}}
    static string Sha(byte[] bytes){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    static bool Same(byte[] a,byte[] b){return a.Length==b.Length&&Sha(a)==Sha(b);}
    static FloatGameRules Rules(){return new FloatGameRules(1,"float-rig","worm",2,100,1,1000000,new[]{new FloatSpeciesRule("carp",100000,2000000,120)});}
    static FloatCommerceCatalog Commerce(){return new FloatCommerceCatalog(1,"worm","net","bread",2400,5,20,20,20,1000,1000,100);}
    static FloatEquipmentCatalog Catalog(){return new FloatEquipmentCatalog(7,"worm",new[]{new FloatEquipmentDefinition("rod",FloatEquipmentKind.Rod,150000,0,1000,25000),new FloatEquipmentDefinition("reel",FloatEquipmentKind.Reel,200000),new FloatEquipmentDefinition("line",FloatEquipmentKind.Line,0,180),new FloatEquipmentDefinition("hook",FloatEquipmentKind.Hook,200),new FloatEquipmentDefinition("bobber",FloatEquipmentKind.Bobber,3000,0,2000,3000),new FloatEquipmentDefinition("sinker",FloatEquipmentKind.Sinker,2500)},new[]{new FloatEquipmentPairRule("rod","reel",true)},new[]{new FloatEquipmentSpoolRule("reel","line",180,50000)},new[]{new FloatEquipmentPairRule("hook","worm",true)},new[]{new FloatEquipmentLossRule("terminal",new[]{FloatEquipmentSlot.Hook})});}
    sealed class Fixture
    {
        internal readonly Guid Rig=G(),Rod=G(),Reel=G(),Line=G(),Hook=G(),Bobber=G(),Sinker=G(),OwnerA=G(),OwnerB=G();
        internal readonly List<FloatEquipmentInstance> Items=new List<FloatEquipmentInstance>();
        internal readonly List<Guid> LostIds=new List<Guid>();
        internal Guid LostLineA,LostLineB;
        internal Fixture(bool complete=true,int lostHooks=2,bool lostLines=true)
        {
            if(complete){
                Items.Add(Item(Rod,"rod",FloatEquipmentKind.Rod,FloatEquipmentLocation.RigSlot(Rig,FloatEquipmentSlot.Rod)));
                Items.Add(Item(Reel,"reel",FloatEquipmentKind.Reel,FloatEquipmentLocation.RigSlot(Rig,FloatEquipmentSlot.Reel)));
                Items.Add(new FloatEquipmentInstance(Line,"line",FloatEquipmentKind.Line,1,FloatEquipmentLocation.ReelSpool(Reel),50000,50000));
                Items.Add(Item(Hook,"hook",FloatEquipmentKind.Hook,FloatEquipmentLocation.RigSlot(Rig,FloatEquipmentSlot.Hook)));
                Items.Add(Item(Bobber,"bobber",FloatEquipmentKind.Bobber,FloatEquipmentLocation.RigSlot(Rig,FloatEquipmentSlot.Bobber)));
                Items.Add(Item(Sinker,"sinker",FloatEquipmentKind.Sinker,FloatEquipmentLocation.RigSlot(Rig,FloatEquipmentSlot.Sinker)));
            }
            for(int i=0;i<lostHooks;i++){Guid id=G();LostIds.Add(id);Items.Add(Item(id,"hook",FloatEquipmentKind.Hook,FloatEquipmentLocation.Lost((i&1)==0?OwnerA:OwnerB)));}
            if(lostLines){LostLineA=G();LostLineB=G();LostIds.Add(LostLineA);LostIds.Add(LostLineB);Items.Add(new FloatEquipmentInstance(LostLineA,"line",FloatEquipmentKind.Line,1,FloatEquipmentLocation.Lost(OwnerA),23000,0));Items.Add(new FloatEquipmentInstance(LostLineB,"line",FloatEquipmentKind.Line,1,FloatEquipmentLocation.Lost(OwnerB),17000,0));}
        }
        internal FloatGameState State(){return FloatGameState.CreateNewWithEquipment(G(),G(),12000,100,G(),"worm",12,new FloatEquipmentLedger(Rig,1,1,Catalog(),Items),Rules(),Commerce(),G());}
    }
    static FloatEquipmentInstance Item(Guid id,string key,FloatEquipmentKind kind,FloatEquipmentLocation place){return new FloatEquipmentInstance(id,key,kind,1,place);}
    static FloatEquipmentOffers Offers(FloatGameState s){return new FloatEquipmentOffers(1,s.Equipment.Catalog,new[]{new FloatEquipmentOffer("hook-one","hook",FloatEquipmentKind.Hook,35)},s.Rules.MaximumWalletMinor);}
    static ConfigureFloatEquipmentOffers Configure(FloatGameState s){return new ConfigureFloatEquipmentOffers(s.ProfileId,s.BranchId,G(),G(),FloatEquipmentOffersCodec.Encode(Offers(s)),s.Sequence,s.Rig.Revision,s.RulesVersion,s.Equipment.InventoryRevision,s.Equipment.RulesVersion);}
    static ArchiveLostFloatEquipment Archive(FloatGameState s,IEnumerable<Guid> ids,Guid? request=null,Guid? businessEvent=null,long? sequence=null,long? inventory=null)
    {return new ArchiveLostFloatEquipment(s.ProfileId,s.BranchId,request??G(),businessEvent??G(),ids,sequence??s.Sequence,s.Rig.Revision,s.RulesVersion,inventory??s.Equipment.InventoryRevision,s.Equipment.RulesVersion);}
    static BuyFloatEquipment Buy(FloatGameState s,Guid? id=null,Guid? request=null,Guid? businessEvent=null){return new BuyFloatEquipment(s.ProfileId,s.BranchId,request??G(),businessEvent??G(),FloatEquipmentPurchaseQuote.Create(s,"hook-one"),id??G());}
    static CastFloatWithEquipment Cast(FloatGameState s,Guid? session=null){return new CastFloatWithEquipment(s.ProfileId,s.BranchId,G(),G(),session??G(),15000,s.Sequence,s.Rig.Revision,s.RulesVersion,s.Equipment.InventoryRevision,s.Equipment.RulesVersion);}
    static HostFloatFish Fish(FloatGameState s,Guid? id=null,Guid? encounter=null){return new HostFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,id??G(),encounter??G(),"carp",500000,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static HostFloatTerminalLoss Loss(FloatGameState s){var line=s.Equipment.Find(s.Rig.LineId.Value);return new HostFloatTerminalLoss(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,G(),line.Id,line.Revision,"terminal",line.RemainingLineMm,0,s.Sequence,s.Rig.Revision,s.RulesVersion,s.Equipment.InventoryRevision,s.Equipment.RulesVersion,1);}
    static FloatGameStore Store(string path,Fixture f,Action<FloatSaveFaultPoint> hook=null){var store=FloatGameStore.CreateNew(path,f.State(),hook);Commit(store.Execute(Configure(store.State)));return store;}
    static void Commit(FloatCommitResult r){Need(r.Committed&&r.Written&&!r.IsReplay,"Expected commit: "+r.Code);}
    static void Refused(FloatCommitResult r,string code){Need(!r.Committed&&!r.Written&&!r.IsReplay&&r.Code==code,"Expected "+code+" got "+r.Code);}
    static void Reject(FloatGameState s,FloatCommand command,string code){var r=FloatGameReducer.Reduce(s,command,s.Rules);Need(!r.HasCandidate&&!r.IsReplay&&r.Code==code,"Expected "+code+" got "+r.Code);}
    static void Resources(FloatGameState a,FloatGameState b)
    {
        Need(a.WalletMinor==b.WalletMinor&&a.BaitStackId==b.BaitStackId&&a.BaitCount==b.BaitCount&&a.Stamina==b.Stamina&&a.FoodStackId==b.FoodStackId&&a.FoodCount==b.FoodCount&&a.EquippedNetId==b.EquippedNetId&&(a.OwnedNet==null?Guid.Empty:a.OwnedNet.Id)==(b.OwnedNet==null?Guid.Empty:b.OwnedNet.Id)&&a.Creel.Count==b.Creel.Count,"Archive changed resources.");
        for(int i=0;i<a.Creel.Count;i++)Need(a.Creel[i].Id==b.Creel[i].Id&&a.Creel[i].MassMg==b.Creel[i].MassMg&&a.Creel[i].SaleValueMinor==b.Creel[i].SaleValueMinor,"Archive changed fish.");
        Need(a.Rig.Id==b.Rig.Id&&a.Rig.Revision==b.Rig.Revision&&a.Rig.RodId==b.Rig.RodId&&a.Rig.ReelId==b.Rig.ReelId&&a.Rig.LineId==b.Rig.LineId&&a.Rig.HookId==b.Rig.HookId&&a.Rig.BobberId==b.Rig.BobberId&&a.Rig.SinkerId==b.Rig.SinkerId&&a.Rig.LineLengthMm==b.Rig.LineLengthMm,"Archive changed current rig.");
        Need(a.Equipment.InitialLineMm+a.ArchivedEquipmentLineMm==b.Equipment.InitialLineMm+b.ArchivedEquipmentLineMm&&a.Equipment.RemainingLineMm==b.Equipment.RemainingLineMm&&a.Equipment.LostLineMm+a.ArchivedEquipmentLineMm==b.Equipment.LostLineMm+b.ArchivedEquipmentLineMm,"Archive erased cumulative line loss.");
        for(int i=0;i<a.Receipts.Count;i++)Need(a.Receipts[i].Fingerprint==b.Receipts[i].Fingerprint&&a.Receipts[i].Outcome==b.Receipts[i].Outcome&&a.Receipts[i].EventId==b.Receipts[i].EventId,"Archive changed old receipt.");
    }
    static void AppendFrame(string path,long sequence,byte[] payload)
    {
        byte[] existing=Bytes(path),previous=new byte[32];Array.Copy(existing,existing.Length-32,previous,0,32);byte[] body;
        using(var memory=new MemoryStream()){using(var writer=new BinaryWriter(memory,Encoding.UTF8,true)){writer.Write(payload.Length);writer.Write(sequence);writer.Write(previous);writer.Write(payload);}body=memory.ToArray();}
        using(var output=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.None)){output.Write(body,0,body.Length);using(var hash=SHA256.Create()){var digest=hash.ComputeHash(body);output.Write(digest,0,digest.Length);}}
    }
    static int Main(string[] args)
    {
        if(args.Length!=1||!Directory.Exists(args[0]))return 2;root=Path.GetFullPath(args[0]);
        Test("archive selections clone and sort IDs while rejecting empty duplicates zero and over 128",()=>{
            var f=new Fixture();var s=f.State();var ids=new List<Guid>(f.LostIds);ids.Reverse();var first=Archive(s,ids);var second=Archive(s,f.LostIds,first.RequestId,first.EventId);Need(first.Fingerprint==second.Fingerprint,"Selection order changed identity.");ids.Clear();Need(first.ItemIds.Count==4,"Caller mutated selection.");for(int i=1;i<first.ItemIds.Count;i++)Need(first.ItemIds[i-1].CompareTo(first.ItemIds[i])<0,"IDs not canonical.");Throws<ArgumentException>(()=>Archive(s,new Guid[0]));Throws<ArgumentException>(()=>Archive(s,new[]{G(),Guid.Empty}));Throws<ArgumentException>(()=>Archive(s,new[]{f.LostIds[0],f.LostIds[0]}));Throws<ArgumentNullException>(()=>Archive(s,null));var tooMany=new List<Guid>();for(int i=0;i<129;i++)tooMany.Add(G());Throws<ArgumentException>(()=>Archive(s,tooMany));
        });
        Test("partial archive changes inventory revision only and records initial loss owner without refund",()=>{
            var f=new Fixture();using(var store=Store(P("partial-selection"),f)){var before=store.State;var command=Archive(before,new[]{f.LostIds[0]});var r=store.Execute(command);Commit(r);Resources(before,store.State);Need(store.State.Equipment.InventoryRevision==before.Equipment.InventoryRevision+1&&store.State.Equipment.Instances.Count==9&&store.State.Equipment.Find(f.LostIds[0])==null,"Wrong current inventory change.");Need(r.Receipt.Outcome==FloatOutcome.EquipmentArchived&&r.Receipt.EquipmentArchive.ItemIds.Count==1&&r.Receipt.EquipmentArchive.ItemIds[0]==f.LostIds[0]&&r.Receipt.EquipmentArchive.LossOwnerIds.Count==1&&r.Receipt.EquipmentArchive.LossOwnerIds[0]==f.OwnerA&&r.Receipt.EquipmentArchive.LineMm==0,"Archive facts not derived from Lost item.");Need(store.State.ArchivedEquipmentIds.Contains(f.LostIds[0])&&store.State.ArchivedLossOwnerIds.Contains(f.OwnerA),"Archived identities not retained.");}
        });
        Test("128 lost instances can be archived together so a new paid spare fits",()=>{
            var f=new Fixture(false,128,false);using(var store=Store(P("full"),f)){Refused(store.Execute(Buy(store.State)),"equipment-capacity-exceeded");var before=store.State;Commit(store.Execute(Archive(before,f.LostIds)));Resources(before,store.State);Need(store.State.Equipment.Instances.Count==0&&store.State.ArchivedEquipmentIds.Count==128&&store.State.ArchivedLossOwnerIds.Count==2,"Complete archive lost history or left capacity occupied.");var buy=Buy(store.State);Commit(store.Execute(buy));Need(store.State.Equipment.Instances.Count==1&&store.State.Equipment.Find(buy.NewItemId).Location.Place==FloatEquipmentPlace.Inventory&&store.State.WalletMinor==11965&&store.State.ArchivedEquipmentIds.Count==128,"Purchase after archive was free or restored Lost equipment.");}
        });
        Test("separate depleted-line archives preserve total acquired and lost millimetres",()=>{
            var f=new Fixture();using(var store=Store(P("line-history"),f)){var start=store.State;Commit(store.Execute(Archive(store.State,new[]{f.LostLineA})));Need(store.State.ArchivedEquipmentLineMm==23000&&store.State.Equipment.InitialLineMm==67000&&store.State.Receipts[1].EquipmentArchive.LineMm==23000,"First archived line amount incorrect.");Resources(start,store.State);var second=store.State;Commit(store.Execute(Archive(second,new[]{f.LostLineB})));Resources(second,store.State);Need(store.State.ArchivedEquipmentLineMm==40000&&store.State.Equipment.InitialLineMm==50000&&store.State.Equipment.RemainingLineMm==50000&&store.State.ArchivedLossOwnerIds.Count==2,"Cumulative archived line length incorrect.");}
        });
        Test("a maximum 128-line archive retains an Int64 total and deduplicates shared loss ownership",()=>{
            var f=new Fixture(false,0,false);for(int i=0;i<128;i++){Guid id=G();f.LostIds.Add(id);f.Items.Add(new FloatEquipmentInstance(id,"line",FloatEquipmentKind.Line,1,FloatEquipmentLocation.Lost(f.OwnerA),100000000,0));}string path=P("long-line-total");using(var store=Store(path,f)){var before=store.State;Commit(store.Execute(Archive(before,f.LostIds)));Resources(before,store.State);Need(store.State.ArchivedEquipmentLineMm==12800000000L&&store.State.ArchivedLossOwnerIds.Count==1&&store.State.Receipts[1].EquipmentArchive.LossOwnerIds.Count==1,"Archive narrowed the line total or duplicated one owner.");}using(var store=FloatGameStore.OpenExisting(path))Need(store.State.ArchivedEquipmentLineMm==12800000000L&&store.State.Equipment.Instances.Count==0,"Large archive total did not replay.");
        });
        Test("real confirmed terminal loss remains in history after archive and exact file reopen",()=>{
            var f=new Fixture(true,0,false);string path=P("confirmed-loss");ArchiveLostFloatEquipment command;HostFloatTerminalLoss loss;byte[] prefix;using(var store=Store(path,f)){Commit(store.Execute(Cast(store.State)));loss=Loss(store.State);Commit(store.Execute(loss));var before=store.State;prefix=Bytes(path);command=Archive(before,new[]{f.Line,f.Hook});Commit(store.Execute(command));Resources(before,store.State);Need(store.State.Receipts[2].EquipmentLoss.LostLineMm==50000&&store.State.ArchivedEquipmentLineMm==50000&&store.State.Equipment.RemainingLineMm==0,"Confirmed loss record changed.");}
            var full=Bytes(path);for(int i=0;i<prefix.Length;i++)Need(prefix[i]==full[i],"Historical journal bytes changed.");using(var store=FloatGameStore.OpenExisting(path)){var s=store.State;Need(s.Sequence==4&&s.ArchivedEquipmentIds.Contains(f.Line)&&s.ArchivedEquipmentIds.Contains(f.Hook)&&s.ArchivedLossOwnerIds.Contains(loss.EventId)&&s.Receipts[2].EquipmentLoss.LostLineMm==50000&&s.Equipment.Find(f.Line)==null,"Reopen lost archive facts.");Need(store.Execute(command).IsReplay&&Same(full,Bytes(path)),"Archive replay wrote twice.");}
        });
        Test("mixed Lost and equipped live inventory or missing selection rejects the entire batch",()=>{
            var f=new Fixture();Guid spare=G();string path=P("invalid-mixed");f.Items.Add(Item(spare,"hook",FloatEquipmentKind.Hook,FloatEquipmentLocation.Inventory()));using(var store=Store(path,f)){foreach(Guid invalid in new[]{f.Hook,f.Line,spare,G()}){var before=store.State;byte[] bytes=Bytes(path);var c=Archive(before,new[]{f.LostIds[0],invalid});var r=store.Execute(c);Refused(r,invalid==f.Hook||invalid==f.Line||invalid==spare?"item-not-lost":"item-not-found");Need(Object.ReferenceEquals(before,store.State)&&Same(bytes,Bytes(path)),"Invalid batch partly archived.");}byte[] unchanged=Bytes(path);Refused(store.Execute(Archive(store.State,new[]{f.LostIds[0]},request:f.LostIds[0])),"archive-identity-invalid");Refused(store.Execute(Archive(store.State,new[]{f.LostIds[0]},businessEvent:f.OwnerA)),"archive-identity-invalid");Need(Same(unchanged,Bytes(path)),"Archive reused its own command identity.");}
        });
        Test("active casts and saved pending fish refuse archive without ending or changing their sessions",()=>{
            foreach(bool pending in new[]{false,true}){var f=new Fixture();string path=P("busy");using(var store=Store(path,f)){Commit(store.Execute(Cast(store.State)));if(pending){Commit(store.Execute(Fish(store.State)));Commit(store.Execute(Cast(store.State)));Commit(store.Execute(Fish(store.State)));}var before=store.State;byte[] bytes=Bytes(path);Refused(store.Execute(Archive(before,f.LostIds)),"session-busy");Need(Object.ReferenceEquals(before,store.State)&&Same(bytes,Bytes(path))&&store.State.Session!=null,"Archive altered busy session.");}}
        });
        Test("stale inventory or state versions and legacy schemas refuse archive",()=>{
            var f=new Fixture();using(var store=Store(P("stale"),f)){Refused(store.Execute(Archive(store.State,f.LostIds,sequence:0)),"version-conflict");Refused(store.Execute(Archive(store.State,f.LostIds,inventory:2)),"equipment-version-conflict");}
            foreach(int schema in new[]{1,2}){var rig=new FixedFloatRig(G(),1,"float-rig",G(),G(),G(),50000,G(),G(),G());var s=schema==1?FloatGameState.CreateNew(G(),G(),12000,100,G(),"worm",12,rig,Rules()):FloatGameState.CreateNewWithCommerce(G(),G(),12000,100,G(),"worm",12,rig,Rules(),Commerce(),G());Reject(s,new ArchiveLostFloatEquipment(s.ProfileId,s.BranchId,G(),G(),new[]{G()},0,1,1,1,7),"equipment-schema-required");}
        });
        Test("exact archive replay is read only and same request with changed selection conflicts",()=>{
            var f=new Fixture();string path=P("idempotent");using(var store=Store(path,f)){var before=store.State;var c=Archive(before,new[]{f.LostIds[0]});Commit(store.Execute(c));byte[] bytes=Bytes(path);Need(store.Execute(c).IsReplay,"Original receipt unavailable.");Refused(store.Execute(Archive(before,new[]{f.LostIds[1]},c.RequestId,c.EventId)),"request-conflict");Refused(store.Execute(Archive(store.State,new[]{f.LostIds[0]})),"item-not-found");Need(Same(bytes,Bytes(path))&&store.State.ArchivedEquipmentIds.Count==1,"Repeat changed archive.");}
        });
        Test("archived item and initial loss owner IDs cannot become purchase net session request or event IDs",()=>{
            var f=new Fixture();using(var store=Store(P("identity-idle"),f)){Commit(store.Execute(Archive(store.State,f.LostIds)));var s=store.State;foreach(Guid id in new[]{f.LostIds[0],f.LostLineA,f.OwnerA,f.OwnerB}){Reject(s,Buy(s,id),"asset-identity-used");Reject(s,Cast(s,id),"session-identity-used");Reject(s,Buy(s,request:id),"request-identity-used");Reject(s,Buy(s,businessEvent:id),"event-identity-used");var net=new BuyFloatSupply(s.ProfileId,s.BranchId,G(),G(),FloatPurchaseQuote.Create(s,FloatSupplyProduct.Net,1),id,s.Sequence,s.Rig.Revision,s.RulesVersion);Reject(s,net,"asset-identity-used");}}
        });
        Test("archived item and initial loss owner IDs cannot become fish or encounter IDs",()=>{
            var f=new Fixture();using(var store=Store(P("identity-active"),f)){Commit(store.Execute(Archive(store.State,f.LostIds)));Commit(store.Execute(Cast(store.State)));var s=store.State;foreach(Guid id in new[]{f.LostIds[0],f.LostLineA,f.OwnerA,f.OwnerB}){Reject(s,Fish(s,id),"fish-identity-used");Reject(s,Fish(s,encounter:id),"encounter-identity-used");Reject(s,new HostFloatUnhooked(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,id,s.Sequence,s.Rig.Revision,s.RulesVersion,2),"encounter-identity-used");}}
        });
        Test("type19 codec preserves canonical identity and rejects truncation trailing data and old schemas",()=>{
            var f=new Fixture();var c=Archive(f.State(),f.LostIds);byte[] bytes=FloatJournalCodec.EncodeCommand(c);Need(bytes[4]==3&&bytes[7]==19&&FloatJournalCodec.DecodeCommand(bytes).Fingerprint==c.Fingerprint&&Same(bytes,FloatJournalCodec.EncodeCommand(FloatJournalCodec.DecodeCommand(bytes))),"Archive codec changed fingerprint.");for(int n=0;n<bytes.Length;n++){var cut=new byte[n];Array.Copy(bytes,cut,n);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(cut));}var tail=new byte[bytes.Length+1];Array.Copy(bytes,tail,bytes.Length);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(tail));foreach(byte schema in new byte[]{1,2}){Throws<InvalidDataException>(()=>FloatJournalCodec.EncodeCommand(c,schema));var bad=(byte[])bytes.Clone();bad[4]=schema;Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bad));}
            var unordered=(byte[])bytes.Clone();Array.Copy(bytes,130,unordered,146,16);Array.Copy(bytes,146,unordered,130,16);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(unordered));var duplicate=(byte[])bytes.Clone();Array.Copy(bytes,130,duplicate,146,16);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(duplicate));foreach(ushort count in new ushort[]{0,129}){var bad=(byte[])bytes.Clone();Array.Copy(BitConverter.GetBytes(count),0,bad,128,2);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bad));}var session=(byte[])bytes.Clone();Array.Copy(G().ToByteArray(),0,session,72,16);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(session));
        });
        Test("known prewrite archive failure preserves ownership and exact retry records only one summary",()=>{
            var f=new Fixture();bool armed=false;string path=P("known");using(var store=Store(path,f,point=>{if(armed&&point==FloatSaveFaultPoint.BeforeWrite)throw new IOException("private archive prewrite");})){var before=store.State;var c=Archive(before,f.LostIds);byte[] bytes=Bytes(path);armed=true;Throws<IOException>(()=>store.Execute(c));Need(!store.IsFaulted&&Object.ReferenceEquals(before,store.State)&&Same(bytes,Bytes(path)),"Known failure archived data.");armed=false;Commit(store.Execute(c));Need(store.Execute(c).IsReplay&&store.State.ArchivedEquipmentIds.Count==4&&store.State.ArchivedEquipmentLineMm==40000&&store.State.Sequence==2,"Retry double-counted archive.");}
        });
        Test("unknown complete archive freezes writer and reopens exactly one preserved archive",()=>{
            foreach(var point in new[]{FloatSaveFaultPoint.AfterFrame,FloatSaveFaultPoint.AfterFlush}){var f=new Fixture();bool armed=false;string path=P("unknown");ArchiveLostFloatEquipment c;using(var store=Store(path,f,at=>{if(armed&&at==point)throw new IOException("private archive unknown");})){c=Archive(store.State,f.LostIds);armed=true;Throws<FloatSaveUncertainException>(()=>store.Execute(c));Need(store.IsFaulted,"Unknown writer remained available.");Throws<InvalidOperationException>(()=>store.Execute(c));}byte[] bytes=Bytes(path);using(var store=FloatGameStore.OpenExisting(path)){Need(store.State.Sequence==2&&store.State.ArchivedEquipmentIds.Count==4&&store.State.ArchivedEquipmentLineMm==40000&&store.Execute(c).IsReplay&&Same(bytes,Bytes(path)),"Unknown reopen repeated or lost archive.");}}
        });
        Test("partial archive frame refuses reopen and preserves all incomplete bytes",()=>{
            var f=new Fixture();bool armed=false;string path=P("partial-write");using(var store=Store(path,f,point=>{if(armed&&point==FloatSaveFaultPoint.AfterPrefix)throw new IOException("private archive partial");})){armed=true;Throws<FloatSaveUncertainException>(()=>store.Execute(Archive(store.State,f.LostIds)));}byte[] before=Bytes(path);Throws<InvalidDataException>(()=>{using(var store=FloatGameStore.OpenExisting(path)){}});Need(Same(before,Bytes(path)),"Partial archive was repaired or deleted.");
        });
        Test("correctly hashed forged live-item archive is rejected by strict journal replay",()=>{
            var f=new Fixture();string path=P("forged");ArchiveLostFloatEquipment c;using(var store=Store(path,f))c=Archive(store.State,new[]{f.Hook});AppendFrame(path,2,FloatJournalCodec.EncodeCommand(c));byte[] before=Bytes(path);Throws<InvalidDataException>(()=>{using(var store=FloatGameStore.OpenExisting(path)){}});Need(Same(before,Bytes(path)),"Forged archive was accepted or repaired.");
        });
        Test("only current interaction authority may archive and ordinary genesis bytes remain unchanged",()=>{
            var f=new Fixture();var initial=f.State();byte[] genesis=FloatJournalCodec.EncodeGenesis(initial);string path=P("owner");using(var owner=FloatGameOwner.CreateNewWithEquipmentOffers(path,initial,Offers(initial))){var view=owner.OpenView(FloatViewRole.Interaction);var document=owner.AttachDocument(view);owner.SetHostPaused(false);var lease=owner.GrantControl(view,document);var command=Archive(owner.Snapshot.State,f.LostIds);var observer=owner.OpenView(FloatViewRole.Observer);var observed=owner.AttachDocument(observer);Refused(owner.ExecuteUser(observer,observed,lease,command),"control-required");Refused(owner.ExecuteHost(command),"host-command-not-allowed");owner.SetMenuOpen(true);Commit(owner.ExecuteUser(view,document,lease,command));Need(owner.ExecuteUser(view,document,lease,command).IsReplay,"Authorized archive receipt unavailable.");owner.RevokeControl(lease);Refused(owner.ExecuteUser(view,document,lease,command),"control-required");Need(Same(genesis,FloatJournalCodec.EncodeGenesis(initial))&&Same(genesis,FloatJournalCodec.EncodeGenesis(FloatJournalCodec.DecodeGenesis(genesis))),"Archive changed original genesis encoding.");}
        });
        return failed==0?0:1;
    }
}
