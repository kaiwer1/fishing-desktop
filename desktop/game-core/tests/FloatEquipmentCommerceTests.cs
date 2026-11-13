using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FishingDesktop.GameCore;

static class FloatEquipmentCommerceTests
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
    static FloatEquipmentOffers Offers(FloatGameState s,long version=4,long hookPrice=100)
    {return new FloatEquipmentOffers(version,s.Equipment.Catalog,new[]{new FloatEquipmentOffer("hook-one","hook",FloatEquipmentKind.Hook,hookPrice),new FloatEquipmentOffer("bobber-one","bobber",FloatEquipmentKind.Bobber,150),new FloatEquipmentOffer("sinker-one","sinker",FloatEquipmentKind.Sinker,80),new FloatEquipmentOffer("line-50m","line",FloatEquipmentKind.Line,500,50000)},s.Rules.MaximumWalletMinor);}
    static ConfigureFloatEquipmentOffers Configure(FloatGameState s,byte[] payload=null)
    {return new ConfigureFloatEquipmentOffers(s.ProfileId,s.BranchId,G(),G(),payload??FloatEquipmentOffersCodec.Encode(Offers(s)),s.Sequence,s.Rig.Revision,s.RulesVersion,s.Equipment.InventoryRevision,s.Equipment.RulesVersion);}
    static FloatGameState Configured(Fixture f,long wallet=12000,FloatEquipmentLedger ledger=null)
    {var s=f.State(wallet:wallet,ledger:ledger);return Adopt(s,Configure(s));}
    static BuyFloatEquipment Purchase(FloatGameState s,string sku="hook-one",Guid? id=null)
    {return new BuyFloatEquipment(s.ProfileId,s.BranchId,G(),G(),FloatEquipmentPurchaseQuote.Create(s,sku),id??G());}
    static FloatEquipmentPurchaseQuote Quote(FloatEquipmentPurchaseQuote q,Guid? profile=null,Guid? branch=null,long? sequence=null,long? rig=null,long? rules=null,long? inventory=null,long? equipment=null,long? catalog=null,string signature=null,string sku=null,string definition=null,FloatEquipmentKind? kind=null,long? price=null,long? length=null)
    {return new FloatEquipmentPurchaseQuote(profile??q.ProfileId,branch??q.BranchId,sequence??q.StateSequence,rig??q.RigRevision,rules??q.RulesVersion,inventory??q.InventoryRevision,equipment??q.EquipmentVersion,catalog??q.CatalogVersion,signature??q.CatalogSignature,sku??q.Sku,definition??q.DefinitionKey,kind??q.Kind,price??q.PriceMinor,length??q.LineLengthMm);}
    static void Reject(FloatGameState s,FloatCommand c,string code){var r=FloatGameReducer.Reduce(s,c,s.Rules);Need(r.Code==code&&!r.HasCandidate&&!r.IsReplay&&r.Receipt==null,"Expected "+code+" got "+r.Code);}
    static HostFloatTerminalLoss Loss(FloatGameState s,long outLine=50000,long cut=0)
    {var line=s.Equipment.Find(s.Rig.LineId.Value);return new HostFloatTerminalLoss(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,G(),line.Id,line.Revision,"terminal",outLine,cut,s.Sequence,s.Rig.Revision,s.RulesVersion,s.Equipment.InventoryRevision,s.Equipment.RulesVersion,1);}
    static void OtherResources(FloatGameState a,FloatGameState b)
    {
        Need(a.BaitCount==b.BaitCount&&a.BaitStackId==b.BaitStackId&&a.Stamina==b.Stamina&&a.FoodCount==b.FoodCount&&a.FoodStackId==b.FoodStackId&&a.EquippedNetId==b.EquippedNetId&&(a.OwnedNet==null?Guid.Empty:a.OwnedNet.Id)==(b.OwnedNet==null?Guid.Empty:b.OwnedNet.Id)&&a.Creel.Count==b.Creel.Count,"Purchase changed unrelated resources.");
        for(int i=0;i<a.Creel.Count;i++)Need(a.Creel[i].Id==b.Creel[i].Id,"Purchase changed fish identity.");
    }
    static FloatGameState CloneWithOffers(FloatGameState s,FloatEquipmentOffers offers)
    {return new FloatGameState(s.ProfileId,s.BranchId,s.Sequence,s.WalletMinor,s.Stamina,s.BaitStackId,s.BaitDefinitionKey,s.BaitCount,s.LegacyRig,s.Session,s.Creel,s.Receipts,s.Rules,s.SchemaVersion,s.CommerceCatalog,s.FoodStackId,s.FoodCount,s.OwnedNet,s.EquippedNetId,s.UpgradeOrigin,s.Equipment,offers);}
    static int Main(string[] args)
    {
        if(args.Length!=1||!Directory.Exists(args[0]))return 2;root=Path.GetFullPath(args[0]);
        Test("new owner factory confirms exactly one explicit offer configuration without gifts",()=>{
            var f=new Fixture();var initial=f.State();string path=PathFor("factory");var offers=Offers(initial);using(var owner=FloatGameOwner.CreateNewWithEquipmentOffers(path,initial,offers)){var s=owner.Snapshot.State;Need(s.Sequence==1&&s.SchemaVersion==3&&s.EquipmentOffers.Signature==offers.Signature&&s.Receipts[0].Outcome==FloatOutcome.EquipmentOffersConfigured&&s.Session==null&&s.WalletMinor==initial.WalletMinor&&Same(FloatEquipmentCodec.Encode(initial.Equipment),FloatEquipmentCodec.Encode(s.Equipment)),"Factory configuration altered assets.");OtherResources(initial,s);Need(owner.Snapshot.HostPaused,"Factory granted control.");}
            var before=Bytes(path);using(var owner=FloatGameOwner.OpenExisting(path,null,3))Need(owner.Snapshot.State.Sequence==1&&Same(before,Bytes(path)),"Reopen configured again.");
        });
        Test("old schema3 create and reopen keep zero sequence no offers and exact bytes",()=>{
            string path=PathFor("old3");var initial=new Fixture().State();byte[] payload=FloatJournalCodec.EncodeGenesis(initial);using(var owner=FloatGameOwner.CreateNew(path,initial))Need(owner.Snapshot.State.Sequence==0&&owner.Snapshot.State.EquipmentOffers==null,"Old factory installed policy.");var before=Bytes(path);using(var owner=FloatGameOwner.OpenExisting(path,null,3)){Need(owner.Snapshot.State.EquipmentOffers==null&&owner.Snapshot.State.Sequence==0&&Same(payload,FloatJournalCodec.EncodeGenesis(owner.Snapshot.State))&&Same(before,Bytes(path)),"Old reopen silently changed policy.");Throws<ArgumentException>(()=>FloatEquipmentPurchaseQuote.Create(owner.Snapshot.State,"hook-one"));}
        });
        Test("genesis cannot embed offer policy and new factory never overwrites files",()=>{
            var s=new Fixture().State();var offers=Offers(s);var forbidden=CloneWithOffers(s,offers);Throws<InvalidDataException>(()=>FloatJournalCodec.EncodeGenesis(forbidden));string invalid=PathFor("forbidden");Throws<InvalidDataException>(()=>{using(var store=FloatGameStore.CreateNew(invalid,forbidden)){}});Need(!File.Exists(invalid),"Invalid genesis created a file.");string path=PathFor("existing");File.WriteAllText(path,"private existing invalid file");var before=Bytes(path);Throws<IOException>(()=>{using(var owner=FloatGameOwner.CreateNewWithEquipmentOffers(path,s,offers)){}});Need(Same(before,Bytes(path)),"Factory overwrote existing path.");
        });
        Test("configuration freezes original payload and exact replay cannot replace policy",()=>{
            var s=new Fixture().State();byte[] raw=FloatEquipmentOffersCodec.Encode(Offers(s));var c=Configure(s,raw);var expected=c.CopyPayload();raw[0]=0;c.CopyPayload()[0]=0;Need(Same(expected,c.CopyPayload())&&c.PayloadSha256==Sha(expected),"Raw policy was mutable.");s=Adopt(s,c);Need(FloatGameReducer.Reduce(s,c,s.Rules).IsReplay,"Configuration exact replay failed.");Reject(s,Configure(s),"equipment-offers-already-configured");var changed=new ConfigureFloatEquipmentOffers(c.ProfileId,c.BranchId,c.RequestId,c.EventId,FloatEquipmentOffersCodec.Encode(Offers(s,hookPrice:101)),c.ExpectedSequence,c.ExpectedRigRevision,c.ExpectedRulesVersion,c.ExpectedInventoryRevision,c.ExpectedEquipmentVersion);Reject(s,changed,"request-conflict");
        });
        Test("contextless command decoder preserves opaque bytes but reducer rejects invalid policy",()=>{
            var s=new Fixture().State();var raw=new byte[]{70,69,79,83,1,0};var c=Configure(s,raw);var encoded=FloatJournalCodec.EncodeCommand(c);var decoded=(ConfigureFloatEquipmentOffers)FloatJournalCodec.DecodeCommand(encoded);Need(Same(raw,decoded.CopyPayload())&&decoded.Fingerprint==c.Fingerprint,"Context-free decode invented or changed policy.");Reject(s,decoded,"equipment-offers-invalid");using(var store=FloatGameStore.CreateNew(PathFor("badpolicy"),s)){var before=FloatEquipmentCodec.Encode(s.Equipment);Refused(store.Execute(decoded),"equipment-offers-invalid");Need(store.State.Sequence==0&&store.State.EquipmentOffers==null&&Same(before,FloatEquipmentCodec.Encode(store.State.Equipment)),"Invalid FEOS published a policy.");}
        });
        Test("configuration validates actual complete equipment catalogue and wallet maximum",()=>{
            var s=new Fixture().State();var c=Catalog();var changed=new List<FloatEquipmentDefinition>(c.Definitions);changed[0]=new FloatEquipmentDefinition(changed[0].Key,changed[0].Kind,changed[0].MassMg+1,changed[0].LineDiameterUm,changed[0].MinimumLoadMg,changed[0].MaximumLoadMg);var other=new FloatEquipmentCatalog(c.Version,c.BaitDefinitionKey,changed,c.RodReelRules,c.SpoolRules,c.HookBaitRules,c.LossRules);var wrong=new FloatEquipmentOffers(4,other,new[]{new FloatEquipmentOffer("hook-one","hook",FloatEquipmentKind.Hook,100)},s.Rules.MaximumWalletMinor);Reject(s,Configure(s,FloatEquipmentOffersCodec.Encode(wrong)),"equipment-offers-invalid");var expensive=new FloatEquipmentOffers(4,s.Equipment.Catalog,new[]{new FloatEquipmentOffer("hook-one","hook",FloatEquipmentKind.Hook,s.Rules.MaximumWalletMinor+1)},s.Rules.MaximumWalletMinor+1);Reject(s,Configure(s,FloatEquipmentOffersCodec.Encode(expensive)),"equipment-offers-invalid");
        });
        Test("configuration requires first policy no session and all current binding versions",()=>{
            var s=new Fixture().State();var c=Configure(s);var bytes=c.CopyPayload();Reject(s,new ConfigureFloatEquipmentOffers(s.ProfileId,s.BranchId,G(),G(),bytes,1,1,1,1,7),"version-conflict");Reject(s,new ConfigureFloatEquipmentOffers(s.ProfileId,s.BranchId,G(),G(),bytes,0,1,1,2,7),"equipment-version-conflict");s=Adopt(s,Cast(s));Reject(s,Configure(s),"session-busy");s=Adopt(s,Fish(s));s=Adopt(s,Cast(s));s=Adopt(s,Fish(s));Reject(s,Configure(s),"session-busy");
        });
        Test("configuration is rejected by user and ordinary host fact channels",()=>{
            var s=new Fixture().State();using(var owner=FloatGameOwner.CreateNew(PathFor("configauthority"),s)){var c=new Control(owner);var configure=Configure(c.State);Refused(c.Send(configure),"user-command-not-allowed");Refused(owner.ExecuteHost(configure),"host-command-not-allowed");Need(c.State.Sequence==0&&c.State.EquipmentOffers==null,"Owner exposed policy installation route.");}
        });
        Test("known configuration prewrite failure leaves old genesis and will not retry factory",()=>{
            var s=new Fixture().State();string path=PathFor("config-known");int writes=0;Throws<IOException>(()=>{using(var owner=FloatGameOwner.CreateNewWithEquipmentOffers(path,s,Offers(s),at=>{if(at==FloatSaveFaultPoint.BeforeWrite&&++writes==2)throw new IOException("private config fault");})){}});Need(File.Exists(path),"Failed factory deleted evidence.");var before=Bytes(path);using(var owner=FloatGameOwner.OpenExisting(path,null,3))Need(owner.Snapshot.State.Sequence==0&&owner.Snapshot.State.EquipmentOffers==null&&Same(before,Bytes(path)),"Failed configuration was automatically installed.");Throws<IOException>(()=>{using(var owner=FloatGameOwner.CreateNewWithEquipmentOffers(path,s,Offers(s))){}});Need(Same(before,Bytes(path)),"Factory retry overwrote retained genesis.");
        });
        Test("unknown complete configuration reopens once while partial configuration stays invalid",()=>{
            foreach(var fault in new[]{FloatSaveFaultPoint.AfterFrame,FloatSaveFaultPoint.AfterFlush,FloatSaveFaultPoint.AfterPrefix}){var s=new Fixture().State();string path=PathFor("config-unknown");int hits=0;Throws<FloatSaveUncertainException>(()=>{using(var owner=FloatGameOwner.CreateNewWithEquipmentOffers(path,s,Offers(s),at=>{if(at==fault&&++hits==2)throw new IOException("private config unknown");})){}});var before=Bytes(path);if(fault==FloatSaveFaultPoint.AfterPrefix)Throws<InvalidDataException>(()=>{using(var owner=FloatGameOwner.OpenExisting(path,null,3)){}});else using(var owner=FloatGameOwner.OpenExisting(path,null,3))Need(owner.Snapshot.State.Sequence==1&&owner.Snapshot.State.EquipmentOffers.Signature==Offers(s).Signature&&owner.Snapshot.State.WalletMinor==s.WalletMinor,"Uncertain configuration repeated or granted resources.");Need(Same(before,Bytes(path)),"Reopen rewrote configuration tail.");}
        });
        Test("buying each non-line category creates one inventory instance without equipping",()=>{
            var f=new Fixture();var initial=f.State();using(var owner=FloatGameOwner.CreateNewWithEquipmentOffers(PathFor("categories"),initial,Offers(initial))){var c=new Control(owner);foreach(var sku in new[]{"hook-one","bobber-one","sinker-one"}){var command=Purchase(c.State,sku);var before=c.State;var result=c.Send(command);Commit(result);var item=c.State.Equipment.Find(command.NewItemId);Need(result.Code=="equipment-purchase-candidate"&&result.Receipt.Outcome==FloatOutcome.EquipmentPurchased&&result.Receipt.FishIds.Count==0&&result.Receipt.EquipmentLoss==null&&item.Location.Place==FloatEquipmentPlace.Inventory&&item.Revision==1&&item.DefinitionKey==command.Quote.DefinitionKey&&item.Kind==command.Quote.Kind&&c.State.WalletMinor==before.WalletMinor-command.Quote.PriceMinor&&c.State.Equipment.InventoryRevision==before.Equipment.InventoryRevision+1&&c.State.Rig.Revision==before.Rig.Revision&&c.State.Rig.HookId==f.Hook&&c.State.Rig.BobberId==f.Bobber&&c.State.Rig.SinkerId==f.Sinker,"Purchase auto-equipped or wrong debit.");OtherResources(before,c.State);}}
        });
        Test("buying line increases original and remaining budgets equally without repairing old line",()=>{
            var f=new Fixture();var s=Configured(f);s=Adopt(s,Cast(s));s=Adopt(s,Loss(s,30000,10000));var before=s;var command=Purchase(s,"line-50m");s=Adopt(s,command);var item=s.Equipment.Find(command.NewItemId);Need(item.InitialLineMm==50000&&item.RemainingLineMm==50000&&item.Location.Place==FloatEquipmentPlace.Inventory&&s.Equipment.InitialLineMm==before.Equipment.InitialLineMm+50000&&s.Equipment.RemainingLineMm==before.Equipment.RemainingLineMm+50000&&s.Equipment.LostLineMm==20000&&s.Rig.LineId==f.Line&&s.Rig.LineLengthMm==30000&&s.Rig.HookId==null,"Purchased line restored old line or equipped itself.");OtherResources(before,s);
        });
        Test("real owner purchase equip cast loss purchase repair and recast survives reopen",()=>{
            var f=new Fixture();var initial=f.State();string path=PathFor("closed-loop");Guid lostHook,newHook,newLine;using(var owner=FloatGameOwner.CreateNewWithEquipmentOffers(path,initial,Offers(initial))){var c=new Control(owner);var first=Purchase(c.State);lostHook=first.NewItemId;Commit(c.Send(first));Commit(c.Send(Replace(c.State,FloatEquipmentSlot.Hook,lostHook)));Commit(c.Send(Cast(c.State)));Commit(owner.ExecuteHost(Loss(c.State)));Need(c.State.Rig.HookId==null&&c.State.Rig.LineId==null,"Full internal loss did not leave missing gear.");Refused(c.Send(Cast(c.State)),"missing-line");var hook=Purchase(c.State);newHook=hook.NewItemId;Commit(c.Send(hook));var line=Purchase(c.State,"line-50m");newLine=line.NewItemId;Commit(c.Send(line));Commit(c.Send(Replace(c.State,FloatEquipmentSlot.Hook,newHook)));Commit(c.Send(Move(c.State,newLine,f.Reel)));Commit(c.Send(Cast(c.State)));Commit(c.Send(Recall(c.State)));Need(c.State.WalletMinor==11300&&c.State.BaitCount==10&&c.State.Stamina==96&&c.State.Sequence==11,"Repair cycle cost or transaction count wrong.");}
            var before=Bytes(path);using(var owner=FloatGameOwner.OpenExisting(path,null,3)){var s=owner.Snapshot.State;Need(s.Sequence==11&&s.Rig.HookId==newHook&&s.Rig.LineId==newLine&&s.Equipment.Find(lostHook).Location.Place==FloatEquipmentPlace.Lost&&s.Equipment.Find(f.Line).Location.Place==FloatEquipmentPlace.Lost&&s.Equipment.InitialLineMm==120000&&s.Equipment.RemainingLineMm==70000&&s.Equipment.LostLineMm==50000&&Same(before,Bytes(path)),"Restart changed repaired ownership or budgets.");}
        });
        Test("insufficient money and missing offers refuse with zero written resources",()=>{
            var f=new Fixture();var initial=f.State(wallet:99);string path=PathFor("poor");using(var owner=FloatGameOwner.CreateNewWithEquipmentOffers(path,initial,Offers(initial))){var c=new Control(owner);var before=Bytes(path);Refused(c.Send(Purchase(c.State)),"wallet-insufficient");Need(Same(before,Bytes(path))&&c.State.Equipment.Instances.Count==9&&c.State.WalletMinor==99,"Poor purchase received relief or partial charge.");}
            var no=f.State();var configured=Configured(f);var q=FloatEquipmentPurchaseQuote.Create(configured,"hook-one");q=Quote(q,profile:no.ProfileId,branch:no.BranchId,sequence:0);Reject(no,new BuyFloatEquipment(no.ProfileId,no.BranchId,G(),G(),q,G()),"equipment-offers-missing");
        });
        Test("128 instance cap includes lost tombstones and never charges a full inventory",()=>{
            var f=new Fixture();f.Items.Add(new FloatEquipmentInstance(G(),"hook",FloatEquipmentKind.Hook,1,FloatEquipmentLocation.Lost(G())));while(f.Items.Count<128)f.Items.Add(Item(G(),"hook",FloatEquipmentKind.Hook,FloatEquipmentLocation.Inventory()));var initial=f.State();string path=PathFor("capacity");using(var owner=FloatGameOwner.CreateNewWithEquipmentOffers(path,initial,Offers(initial))){var c=new Control(owner);var before=Bytes(path);Refused(c.Send(Purchase(c.State)),"equipment-capacity-exceeded");Need(Same(before,Bytes(path))&&c.State.Equipment.Instances.Count==128&&c.State.WalletMinor==12000,"Capacity removed tombstone or charged.");}
        });
        Test("inventory revision overflow refuses whole purchase without a partial debit",()=>{
            var f=new Fixture();var s=Configured(f,ledger:new FloatEquipmentLedger(f.Rig,1,Int64.MaxValue,Catalog(),f.Items));var before=FloatEquipmentCodec.Encode(s.Equipment);Reject(s,Purchase(s),"arithmetic-overflow");Need(s.WalletMinor==12000&&Same(before,FloatEquipmentCodec.Encode(s.Equipment)),"Overflow partially changed inventory.");
        });
        Test("quotes expire on any sequence rig inventory or rules binding change",()=>{
            var f=new Fixture();var s=Configured(f);var buy=Purchase(s);s=Adopt(s,Buy(s,FloatSupplyProduct.Food));Reject(s,buy,"version-conflict");var q=FloatEquipmentPurchaseQuote.Create(s,"hook-one");foreach(var changed in new[]{Quote(q,rig:q.RigRevision+1),Quote(q,rules:q.RulesVersion+1)})Reject(s,new BuyFloatEquipment(s.ProfileId,s.BranchId,G(),G(),changed,G()),"version-conflict");foreach(var changed in new[]{Quote(q,inventory:q.InventoryRevision+1),Quote(q,equipment:q.EquipmentVersion+1)})Reject(s,new BuyFloatEquipment(s.ProfileId,s.BranchId,G(),G(),changed,G()),"equipment-version-conflict");
        });
        Test("host policy rejects fake price category definition length sku and catalogue signatures",()=>{
            var s=Configured(new Fixture());var q=FloatEquipmentPurchaseQuote.Create(s,"hook-one");foreach(var changed in new[]{Quote(q,price:1),Quote(q,kind:FloatEquipmentKind.Sinker),Quote(q,definition:"sinker"),Quote(q,sku:"missing"),Quote(q,catalog:5),Quote(q,signature:new string('0',64)),Quote(q,kind:FloatEquipmentKind.Line,length:50000)})Reject(s,new BuyFloatEquipment(s.ProfileId,s.BranchId,G(),G(),changed,G()),"equipment-quote-invalid");foreach(var changed in new[]{Quote(q,profile:G()),Quote(q,branch:G())})Reject(s,new BuyFloatEquipment(s.ProfileId,s.BranchId,G(),G(),changed,G()),"equipment-quote-stale");
        });
        Test("quote construction rejects unsupported categories and malformed numeric or identity data",()=>{
            var s=Configured(new Fixture());var q=FloatEquipmentPurchaseQuote.Create(s,"hook-one");Throws<ArgumentException>(()=>FloatEquipmentPurchaseQuote.Create(s,"unknown"));Throws<ArgumentOutOfRangeException>(()=>Quote(q,kind:FloatEquipmentKind.Rod));Throws<ArgumentOutOfRangeException>(()=>Quote(q,price:0));Throws<ArgumentException>(()=>Quote(q,length:1));Throws<ArgumentOutOfRangeException>(()=>Quote(q,kind:FloatEquipmentKind.Line,length:0));Throws<ArgumentOutOfRangeException>(()=>Quote(q,catalog:0));Throws<ArgumentException>(()=>Quote(q,signature:"not-a-signature"));Throws<ArgumentNullException>(()=>new BuyFloatEquipment(s.ProfileId,s.BranchId,G(),G(),null,G()));Throws<ArgumentException>(()=>Purchase(s,id:Guid.Empty));
        });
        Test("duplicate request returns same item and every frozen field change conflicts",()=>{
            var s=Configured(new Fixture());var command=Purchase(s);var q=command.Quote;s=Adopt(s,command);Need(FloatGameReducer.Reduce(s,command,s.Rules).IsReplay&&s.WalletMinor==11900&&s.Equipment.Instances.Count==10,"Duplicate repeated purchase.");var variants=new[]{Quote(q,profile:G()),Quote(q,branch:G()),Quote(q,sequence:2),Quote(q,rig:2),Quote(q,rules:2),Quote(q,inventory:2),Quote(q,equipment:8),Quote(q,catalog:5),Quote(q,signature:new string('0',64)),Quote(q,sku:"other"),Quote(q,definition:"sinker"),Quote(q,kind:FloatEquipmentKind.Sinker),Quote(q,price:101),Quote(q,kind:FloatEquipmentKind.Line,length:1)};
            foreach(var changed in variants){var c=new BuyFloatEquipment(command.ProfileId,command.BranchId,command.RequestId,command.EventId,changed,command.NewItemId);Need(c.Fingerprint!=command.Fingerprint,"Quote field missing from fingerprint.");Reject(s,c,"request-conflict");}Reject(s,new BuyFloatEquipment(command.ProfileId,command.BranchId,command.RequestId,G(),q,command.NewItemId),"request-conflict");Reject(s,new BuyFloatEquipment(command.ProfileId,command.BranchId,command.RequestId,command.EventId,q,G()),"request-conflict");Reject(s,new BuyFloatEquipment(command.ProfileId,command.BranchId,G(),command.EventId,q,G()),"event-already-used");
        });
        Test("new item identity excludes all assets current transaction and historic business identities",()=>{
            var f=new Fixture();var s=Configured(f);s=Adopt(s,Buy(s,FloatSupplyProduct.Net));s=Adopt(s,Cast(s));var fish=Fish(s);s=Adopt(s,fish);s=Adopt(s,new SellFloatFish(s.ProfileId,s.BranchId,G(),G(),new[]{fish.FishId},s.Sequence,s.Rig.Revision,s.RulesVersion));var command=Purchase(s);var ids=new List<Guid>{s.ProfileId,s.BranchId,s.Rig.Id,s.BaitStackId,s.FoodStackId,s.OwnedNet.Id,fish.FishId,command.RequestId,command.EventId};foreach(var item in s.Equipment.Instances)ids.Add(item.Id);foreach(var receipt in s.Receipts){ids.Add(receipt.RequestId);ids.Add(receipt.EventId);if(receipt.SessionId!=Guid.Empty)ids.Add(receipt.SessionId);if(receipt.EncounterId!=Guid.Empty)ids.Add(receipt.EncounterId);}foreach(var id in ids)Reject(s,new BuyFloatEquipment(command.ProfileId,command.BranchId,command.RequestId,command.EventId,command.Quote,id),"asset-identity-used");
        });
        Test("new item identity cannot reuse lost items loss event encounter or genesis tombstone owner",()=>{
            var f=new Fixture();Guid oldLoss=G();f.Items.Add(new FloatEquipmentInstance(G(),"hook",FloatEquipmentKind.Hook,1,FloatEquipmentLocation.Lost(oldLoss)));var s=Configured(f);s=Adopt(s,Cast(s));var loss=Loss(s);s=Adopt(s,loss);foreach(var id in new[]{f.Hook,f.Line,loss.EventId,loss.EncounterId,oldLoss})Reject(s,Purchase(s,id:id),"asset-identity-used");
        });
        Test("active and capacity pending sessions cannot purchase replacement gear",()=>{
            var s=Configured(new Fixture());s=Adopt(s,Cast(s));Reject(s,Purchase(s),"session-busy");s=Adopt(s,Fish(s));s=Adopt(s,Cast(s));s=Adopt(s,Fish(s));Reject(s,Purchase(s),"session-busy");Need(s.Session.CapacityBlocked&&s.Creel.Count==1,"Purchase discarded pending fish.");
        });
        Test("authorized idle menu can purchase but observer old document and paused owner cannot",()=>{
            var initial=new Fixture().State();using(var owner=FloatGameOwner.CreateNewWithEquipmentOffers(PathFor("authority"),initial,Offers(initial))){var c=new Control(owner);var buy=Purchase(c.State);var observer=owner.OpenView(FloatViewRole.Observer);var doc=owner.AttachDocument(observer);Refused(owner.ExecuteUser(observer,doc,c.Lease,buy),"control-required");Refused(owner.ExecuteHost(buy),"host-command-not-allowed");var old=c.Document;var lease=c.Lease;c.Document=owner.AttachDocument(c.View);c.Lease=owner.GrantControl(c.View,c.Document);Refused(owner.ExecuteUser(c.View,old,lease,buy),"control-required");owner.SetMenuOpen(true);Commit(c.Send(buy));Need(c.Send(buy).IsReplay,"Authorized purchase receipt missing.");owner.SetHostPaused(true);Refused(c.Send(buy),"control-required");}
        });
        Test("known prewrite purchase failure preserves wallet inventory and exact retry",()=>{
            var initial=new Fixture().State();bool armed=false;string path=PathFor("purchase-known");using(var owner=FloatGameOwner.CreateNewWithEquipmentOffers(path,initial,Offers(initial),at=>{if(armed&&at==FloatSaveFaultPoint.BeforeWrite)throw new IOException("private purchase failure");})){var c=new Control(owner);var command=Purchase(c.State);var before=c.State;var bytes=Bytes(path);armed=true;Throws<IOException>(()=>c.Send(command));Need(!owner.Snapshot.Faulted&&Object.ReferenceEquals(before,c.State)&&Same(bytes,Bytes(path)),"Known failed purchase partially charged.");armed=false;Commit(c.Send(command));Need(c.Send(command).IsReplay&&c.State.WalletMinor==11900&&c.State.Sequence==2&&c.State.Equipment.Find(command.NewItemId)!=null,"Same request retry did not buy exactly once.");}
        });
        Test("uncertain complete purchase reopens as same paid item and never repeats debit",()=>{
            foreach(var fault in new[]{FloatSaveFaultPoint.AfterFrame,FloatSaveFaultPoint.AfterFlush}){var initial=new Fixture().State();bool armed=false;string path=PathFor("purchase-unknown");BuyFloatEquipment command;using(var owner=FloatGameOwner.CreateNewWithEquipmentOffers(path,initial,Offers(initial),at=>{if(armed&&at==fault)throw new IOException("private purchase unknown");})){var c=new Control(owner);command=Purchase(c.State,"line-50m");armed=true;Throws<FloatSaveUncertainException>(()=>c.Send(command));Need(owner.Snapshot.Faulted&&owner.Snapshot.State==null,"Unknown purchase exposed stale state.");Refused(c.Send(command),"control-required");}var before=Bytes(path);using(var owner=FloatGameOwner.OpenExisting(path,null,3)){var c=new Control(owner);Need(c.State.Sequence==2&&c.State.WalletMinor==11500&&c.State.Equipment.Find(command.NewItemId).RemainingLineMm==50000&&c.Send(command).IsReplay&&Same(before,Bytes(path)),"Reopen duplicated or lost purchased line.");}}
        });
        Test("partial purchase frame refuses restart and leaves evidence untouched",()=>{
            var initial=new Fixture().State();bool armed=false;string path=PathFor("purchase-partial");using(var owner=FloatGameOwner.CreateNewWithEquipmentOffers(path,initial,Offers(initial),at=>{if(armed&&at==FloatSaveFaultPoint.AfterPrefix)throw new IOException("private partial purchase");})){var c=new Control(owner);armed=true;Throws<FloatSaveUncertainException>(()=>c.Send(Purchase(c.State)));}var before=Bytes(path);Throws<InvalidDataException>(()=>{using(var owner=FloatGameOwner.OpenExisting(path,null,3)){}});Need(Same(before,Bytes(path)),"Partial purchase file was trimmed.");
        });
        Test("type16 and type17 preserve exact fingerprints and reject every truncated prefix",()=>{
            var initial=new Fixture().State();var configured=Adopt(initial,Configure(initial));foreach(var c in new FloatCommand[]{Configure(initial),Purchase(configured),Purchase(configured,"line-50m")}){var bytes=FloatJournalCodec.EncodeCommand(c);Need(bytes[4]==3&&bytes[7]==(c is ConfigureFloatEquipmentOffers?16:17)&&FloatJournalCodec.DecodeCommand(bytes).Fingerprint==c.Fingerprint&&Same(bytes,FloatJournalCodec.EncodeCommand(FloatJournalCodec.DecodeCommand(bytes))),"New command codec altered identity.");for(int n=0;n<bytes.Length;n++){var prefix=new byte[n];Array.Copy(bytes,prefix,n);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(prefix));}var tail=new byte[bytes.Length+1];Array.Copy(bytes,tail,bytes.Length);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(tail));foreach(int schema in new[]{1,2}){Throws<InvalidDataException>(()=>FloatJournalCodec.EncodeCommand(c,schema));var bad=(byte[])bytes.Clone();bad[4]=(byte)schema;Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bad));}}
        });
        Test("codec rejects base quote disagreement session injection and payload bounds",()=>{
            var s=Configured(new Fixture());var bytes=FloatJournalCodec.EncodeCommand(Purchase(s));foreach(int offset in new[]{88,96,104,112,120}){var bad=(byte[])bytes.Clone();bad[offset]++;Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bad));}var session=(byte[])bytes.Clone();Array.Copy(G().ToByteArray(),0,session,72,16);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(session));var configure=FloatJournalCodec.EncodeCommand(Configure(s));foreach(int length in new[]{-1,0,5,ConfigureFloatEquipmentOffers.MaximumPayloadBytes+1,Int32.MaxValue}){var bad=(byte[])configure.Clone();Array.Copy(BitConverter.GetBytes(length),0,bad,128,4);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bad));}Throws<ArgumentException>(()=>Configure(s,new byte[5]));Throws<ArgumentException>(()=>Configure(s,new byte[ConfigureFloatEquipmentOffers.MaximumPayloadBytes+1]));
        });
        Test("replay rejects correctly hashed invalid configuration or fake purchase price",()=>{
            var initial=new Fixture().State();string policy=PathFor("forged-policy");using(var store=FloatGameStore.CreateNew(policy,initial)){}AppendFixtureFrame(policy,1,FloatJournalCodec.EncodeCommand(Configure(initial,new byte[]{70,69,79,83,1,0})));Throws<InvalidDataException>(()=>{using(var store=FloatGameStore.OpenExisting(policy)){}});
            string purchase=PathFor("forged-price");FloatGameState s;using(var owner=FloatGameOwner.CreateNewWithEquipmentOffers(purchase,initial,Offers(initial)))s=owner.Snapshot.State;var q=Quote(FloatEquipmentPurchaseQuote.Create(s,"hook-one"),price:1);AppendFixtureFrame(purchase,2,FloatJournalCodec.EncodeCommand(new BuyFloatEquipment(s.ProfileId,s.BranchId,G(),G(),q,G())));var before=Bytes(purchase);Throws<InvalidDataException>(()=>{using(var store=FloatGameStore.OpenExisting(purchase)){}});Need(Same(before,Bytes(purchase)),"Replay accepted fake purchase or rewrote file.");
        });
        Test("schema1 and schema2 reject new commands and factory without migration or gifts",()=>{
            foreach(int schema in new[]{1,2}){var rig=new FixedFloatRig(G(),1,"float-rig",G(),G(),G(),50000,G(),G(),G());var s=schema==1?FloatGameState.CreateNew(G(),G(),12000,100,G(),"worm",12,rig,Rules()):FloatGameState.CreateNewWithCommerce(G(),G(),12000,100,G(),"worm",12,rig,Rules(),Commerce(),G());var temp=new Fixture().State();var configure=new ConfigureFloatEquipmentOffers(s.ProfileId,s.BranchId,G(),G(),FloatEquipmentOffersCodec.Encode(Offers(temp)),0,1,1,1,7);Reject(s,configure,"equipment-schema-required");var q=new FloatEquipmentPurchaseQuote(s.ProfileId,s.BranchId,0,1,1,1,7,4,Offers(temp).Signature,"hook-one","hook",FloatEquipmentKind.Hook,100,0);Reject(s,new BuyFloatEquipment(s.ProfileId,s.BranchId,G(),G(),q,G()),"equipment-schema-required");string path=PathFor("legacy-factory");Throws<ArgumentException>(()=>{using(var owner=FloatGameOwner.CreateNewWithEquipmentOffers(path,s,Offers(temp))){}});Need(!File.Exists(path),"Legacy factory created file.");}
        });
        Test("confirmed purchase policy and inventory survive normal shutdown without extra transactions",()=>{
            var initial=new Fixture().State();string path=PathFor("shutdown");BuyFloatEquipment buy;using(var owner=FloatGameOwner.CreateNewWithEquipmentOffers(path,initial,Offers(initial))){var c=new Control(owner);buy=Purchase(c.State);Commit(c.Send(buy));var before=Bytes(path);Need(owner.Shutdown().NoActiveSession&&c.State.Sequence==2&&Same(before,Bytes(path)),"Idle shutdown altered purchase.");}using(var owner=FloatGameOwner.OpenExisting(path,null,3)){var c=new Control(owner);Need(c.State.EquipmentOffers!=null&&c.State.Equipment.Find(buy.NewItemId)!=null&&c.State.Sequence==2&&c.Send(buy).IsReplay,"Confirmed purchased item missing on restart.");}
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
}
