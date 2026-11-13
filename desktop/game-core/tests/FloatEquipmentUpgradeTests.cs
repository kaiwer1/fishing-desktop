using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FishingDesktop.GameCore;

static class FloatEquipmentUpgradeTests
{
    static int passed,failed,nextId,nextFile;static string root;
    static Guid G(){return new Guid(++nextId,0,0,new byte[]{0,0,0,0,0,0,0,85});}
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    static void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error);}}
    static string P(string label){return Path.Combine(root,(++nextFile)+"-"+label+".fltj");}
    static byte[] Bytes(string path){using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){var bytes=new byte[(int)file.Length];int at=0;while(at<bytes.Length){int count=file.Read(bytes,at,bytes.Length-at);if(count==0)throw new EndOfStreamException();at+=count;}return bytes;}}
    static string Sha(byte[] bytes){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    static bool Same(byte[] a,byte[] b){return a.Length==b.Length&&Sha(a)==Sha(b);}
    static void Prefix(byte[] prefix,byte[] bytes){Need(bytes.Length>prefix.Length,"No appended frame.");for(int i=0;i<prefix.Length;i++)Need(prefix[i]==bytes[i],"Historical prefix changed.");}
    static FloatCommerceCatalog Commerce(){return new FloatCommerceCatalog(1,"worm","net","bread",2400,5,20,20,20,1000,1000,100);}
    static FloatEquipmentCatalog Catalog(long rodMass=150000)
    {
        return new FloatEquipmentCatalog(1,"worm",new[]{new FloatEquipmentDefinition("rod",FloatEquipmentKind.Rod,rodMass,0,1000,25000),
            new FloatEquipmentDefinition("reel",FloatEquipmentKind.Reel,200000),new FloatEquipmentDefinition("line",FloatEquipmentKind.Line,0,180),
            new FloatEquipmentDefinition("hook",FloatEquipmentKind.Hook,200),new FloatEquipmentDefinition("bobber",FloatEquipmentKind.Bobber,3000,0,2000,3000),
            new FloatEquipmentDefinition("sinker",FloatEquipmentKind.Sinker,2500)},new[]{new FloatEquipmentPairRule("rod","reel",true)},
            new[]{new FloatEquipmentSpoolRule("reel","line",180,50000)},new[]{new FloatEquipmentPairRule("hook","worm",true)},
            new[]{new FloatEquipmentLossRule("terminal",new[]{FloatEquipmentSlot.Hook})});
    }
    static FloatEquipmentMigrationPolicy Policy(){return new FloatEquipmentMigrationPolicy(1,"legacy-float",Catalog(),"rod","reel","line","hook","bobber","sinker");}
    static FloatEquipmentOffers Offers(FloatEquipmentCatalog catalog=null){return new FloatEquipmentOffers(1,catalog??Catalog(),new[]{new FloatEquipmentOffer("spare-hook","hook",FloatEquipmentKind.Hook,35)},1000000);}
    static FloatGameState New(int schema=2,long wallet=12000,int stamina=100,int bait=12)
    {
        var rig=new FixedFloatRig(G(),7,"legacy-float",G(),G(),G(),17321,G(),G(),G());
        var rules=new FloatGameRules(2,"legacy-float","worm",2,100,1,1000000,new[]{new FloatSpeciesRule("carp",100000,10000000,120)});
        return schema==1?FloatGameState.CreateNew(G(),G(),wallet,stamina,G(),"worm",bait,rig,rules):FloatGameState.CreateNewWithCommerce(G(),G(),wallet,stamina,G(),"worm",bait,rig,rules,Commerce(),G());
    }
    static string Source(FloatGameState state=null){string path=P("source");using(var store=FloatGameStore.CreateNew(path,state??New())){}return path;}
    static void Commit(FloatCommitResult result){Need(result.Committed&&result.Written&&!result.IsReplay,"Expected commit: "+result.Code);}
    static void Refused(FloatCommitResult result,string code){Need(!result.Committed&&!result.Written&&result.Code==code,"Expected "+code+" got "+result.Code);}
    static void Reject(FloatGameState s,FloatCommand command,string code){var r=FloatGameReducer.Reduce(s,command,s.Rules);Need(!r.HasCandidate&&r.Code==code,"Expected "+code+" got "+r.Code);}
    static CastFloat Cast(FloatGameState s){return new CastFloat(s.ProfileId,s.BranchId,G(),G(),G(),s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static HostFloatFish Fish(FloatGameState s){return new HostFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,G(),G(),"carp",500000,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static BuyFloatSupply Buy(FloatGameState s,FloatSupplyProduct product){return new BuyFloatSupply(s.ProfileId,s.BranchId,G(),G(),FloatPurchaseQuote.Create(s,product,1),product==FloatSupplyProduct.Net?G():Guid.Empty,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static UpgradeFloatEquipment Command(FloatGameState s,string sha,string tip,long length,byte[] equipment=null,byte[] offers=null)
    {return new UpgradeFloatEquipment(s.ProfileId,s.BranchId,G(),G(),G(),sha,tip,length,s.Sequence,s.Rig.Revision,s.RulesVersion,1,"legacy-float",equipment??FloatEquipmentCodec.Encode(Policy().Map(s)),offers??FloatEquipmentOffersCodec.Encode(Offers()));}
    static UpgradeFloatEquipment Command(FloatJournalInspection source,byte[] equipment=null,byte[] offers=null){return Command(source.State,source.Sha256,source.TipSha256,source.Length,equipment,offers);}
    static string Copy(string source){string candidate=P("candidate");File.Copy(source,candidate);return candidate;}
    static string Receipt(FloatReceipt r)
    {
        string value=r.RequestId+"|"+r.EventId+"|"+r.SessionId+"|"+r.EncounterId+"|"+r.Sequence+"|"+r.RulesVersion+"|"+r.WalletBefore+"|"+r.WalletAfter+"|"+r.StaminaBefore+"|"+r.StaminaAfter+"|"+r.BaitBefore+"|"+r.BaitAfter+"|"+r.FoodBefore+"|"+r.FoodAfter+"|"+r.OwnedNetBefore+"|"+r.OwnedNetAfter+"|"+r.EquippedNetBefore+"|"+r.EquippedNetAfter+"|"+r.Outcome+"|"+r.RecoveryReason+"|"+r.Fingerprint;
        foreach(Guid id in r.FishIds)value+="|"+id;return value;
    }
    static string Origin(FloatUpgradeOrigin o){return o==null?"none":o.SourceBranchId+"|"+o.SourceSha256+"|"+o.SourceTipSha256+"|"+o.SourceLength+"|"+o.SourceSequence;}
    static void Preserved(FloatGameState a,FloatGameState b)
    {
        Need(b.SchemaVersion==3&&b.LegacyRig==null&&b.ProfileId==a.ProfileId&&b.BranchId!=a.BranchId&&b.Sequence==a.Sequence+1&&b.Session==null,"Wrong schema transition.");
        Need(a.WalletMinor==b.WalletMinor&&a.Stamina==b.Stamina&&a.BaitStackId==b.BaitStackId&&a.BaitCount==b.BaitCount&&a.BaitDefinitionKey==b.BaitDefinitionKey&&a.FoodStackId==b.FoodStackId&&a.FoodCount==b.FoodCount&&a.EquippedNetId==b.EquippedNetId&&(a.OwnedNet==null?Guid.Empty:a.OwnedNet.Id)==(b.OwnedNet==null?Guid.Empty:b.OwnedNet.Id),"Resources changed.");
        Need(a.Rules.Signature==b.Rules.Signature&&a.CommerceCatalog.Signature==b.CommerceCatalog.Signature&&Origin(a.UpgradeOrigin)==Origin(b.UpgradeOrigin),"Old rules or origin changed.");
        Need(b.Equipment.Instances.Count==6&&b.Equipment.InitialLineMm==17321&&b.Equipment.RemainingLineMm==17321&&b.Equipment.LostLineMm==0&&b.Rig.Id==a.Rig.Id&&b.Rig.Revision==a.Rig.Revision,"Rig length or count changed.");
        foreach(Guid id in new[]{a.LegacyRig.RodId,a.LegacyRig.ReelId,a.LegacyRig.LineId,a.LegacyRig.HookId,a.LegacyRig.BobberId,a.LegacyRig.SinkerId})Need(b.Equipment.Find(id)!=null,"Legacy identity changed.");
        Need(a.Creel.Count==b.Creel.Count&&b.Receipts.Count==a.Receipts.Count+1,"Fish or receipts lost.");
        for(int i=0;i<a.Creel.Count;i++){var x=a.Creel[i];var y=b.Creel[i];Need(x.Id==y.Id&&x.SessionId==y.SessionId&&x.EncounterId==y.EncounterId&&x.SpeciesKey==y.SpeciesKey&&x.MassMg==y.MassMg&&x.SaleValueMinor==y.SaleValueMinor&&x.RulesVersion==y.RulesVersion,"Fish repriced or replaced.");}
        for(int i=0;i<a.Receipts.Count;i++)Need(Receipt(a.Receipts[i])==Receipt(b.Receipts[i]),"Historical receipt changed.");
    }
    static int Main(string[] args)
    {
        if(args.Length!=1||!Directory.Exists(args[0]))return 2;root=Path.GetFullPath(args[0]);
        Test("legacy genesis payloads roundtrip byte-identically before and after pure mapping",()=>{
            foreach(int schema in new[]{1,2}){var s=New(schema);byte[] bytes=FloatJournalCodec.EncodeGenesis(s);Need(Same(bytes,FloatJournalCodec.EncodeGenesis(FloatJournalCodec.DecodeGenesis(bytes))),"Legacy genesis roundtrip changed.");if(schema==2)Policy().Map(s);Need(Same(bytes,FloatJournalCodec.EncodeGenesis(s)),"Mapping mutated legacy genesis.");}
        });
        Test("one equipment upgrade preserves original source prefix assets and exact nonstandard line length",()=>{
            string source=Source(),candidate=Copy(source);byte[] before=Bytes(source);var inspection=FloatJournalArchive.Inspect(source);var command=Command(inspection);
            using(var store=FloatGameStore.OpenExisting(candidate)){Commit(store.AppendEquipmentUpgrade(command));Preserved(inspection.State,store.State);Need(store.State.EquipmentOffers.Signature==Offers().Signature&&store.State.EquipmentUpgradeOrigin.SourceSha256==inspection.Sha256,"Upgrade evidence missing.");}
            Need(Same(before,Bytes(source)),"Source changed.");Prefix(before,Bytes(candidate));var reopened=FloatJournalArchive.Inspect(candidate);Preserved(inspection.State,reopened.State);Need(reopened.Sequence==1&&reopened.State.Receipts[0].Outcome==FloatOutcome.EquipmentProfileUpgraded,"Upgrade receipt missing.");
        });
        Test("prior schema1 origin net food fish and all old receipts survive the second upgrade",()=>{
            string source=Source(New(1));var first=FloatJournalArchive.Inspect(source);
            using(var store=FloatGameStore.OpenExisting(source)){var s=store.State;Commit(store.AppendUpgrade(new UpgradeFloatProfile(s.ProfileId,s.BranchId,G(),G(),G(),G(),first.Sha256,first.TipSha256,first.Length,s.Sequence,s.Rig.Revision,s.RulesVersion,Commerce())));Commit(store.Execute(Buy(store.State,FloatSupplyProduct.Net)));s=store.State;Commit(store.Execute(new EquipFloatNet(s.ProfileId,s.BranchId,G(),G(),s.OwnedNet.Id,s.Sequence,s.Rig.Revision,s.RulesVersion)));Commit(store.Execute(Buy(store.State,FloatSupplyProduct.Food)));Commit(store.Execute(Cast(store.State)));Commit(store.Execute(Fish(store.State)));s=store.State;Commit(store.Execute(new SellFloatFish(s.ProfileId,s.BranchId,G(),G(),new[]{s.Creel[0].Id},s.Sequence,s.Rig.Revision,s.RulesVersion)));Commit(store.Execute(Cast(store.State)));Commit(store.Execute(Fish(store.State)));}
            var inspection=FloatJournalArchive.Inspect(source);string candidate=Copy(source);using(var store=FloatGameStore.OpenExisting(candidate))Commit(store.AppendEquipmentUpgrade(Command(inspection)));var final=FloatJournalArchive.Inspect(candidate);Preserved(inspection.State,final.State);Need(final.State.UpgradeOrigin!=null&&final.State.EquipmentUpgradeOrigin.SourceBranchId==inspection.BranchId&&final.State.EquipmentUpgradeOrigin.SourceTipSha256==inspection.TipSha256&&final.State.EquipmentUpgradeOrigin.SourceLength==inspection.Length&&final.State.EquipmentUpgradeOrigin.SourceSequence==inspection.Sequence,"Two origins not preserved.");
        });
        Test("zero resources remain zero with no gifted supplies on upgrade",()=>{
            string source=Source(New(wallet:0,stamina:0,bait:0));var inspection=FloatJournalArchive.Inspect(source);string candidate=Copy(source);using(var store=FloatGameStore.OpenExisting(candidate)){Commit(store.AppendEquipmentUpgrade(Command(inspection)));Preserved(inspection.State,store.State);Need(store.State.OwnedNet==null&&store.State.FoodCount==0,"Free supplies added.");}
        });
        Test("active and pending sessions refuse equipment migration without changing files",()=>{
            foreach(bool pending in new[]{false,true}){string source=Source();using(var store=FloatGameStore.OpenExisting(source)){Commit(store.Execute(Cast(store.State)));if(pending){Commit(store.Execute(Fish(store.State)));Commit(store.Execute(Cast(store.State)));Commit(store.Execute(Fish(store.State)));}}var inspection=FloatJournalArchive.Inspect(source);byte[] before=Bytes(source);using(var store=FloatGameStore.OpenExisting(source))Refused(store.AppendEquipmentUpgrade(Command(inspection)),"session-busy");Need(Same(before,Bytes(source)),"Busy source changed.");}
        });
        Test("schema1 and schema3 refuse the schema2 equipment upgrade path",()=>{
            var s=New();byte[] equipment=FloatEquipmentCodec.Encode(Policy().Map(s)),offers=FloatEquipmentOffersCodec.Encode(Offers());var one=New(1);Reject(one,Command(one,new string('a',64),new string('b',64),100,equipment,offers),"equipment-upgrade-source-required");
            var three=FloatGameState.CreateNewWithEquipment(s.ProfileId,s.BranchId,s.WalletMinor,s.Stamina,s.BaitStackId,s.BaitDefinitionKey,s.BaitCount,Policy().Map(s),s.Rules,s.CommerceCatalog,s.FoodStackId);Reject(three,Command(three,new string('a',64),new string('b',64),100,equipment,offers),"equipment-upgrade-source-required");
        });
        Test("incorrect source hash tip or length cannot append even a valid mapped candidate",()=>{
            string source=Source();var inspection=FloatJournalArchive.Inspect(source);foreach(int field in new[]{0,1,2}){string candidate=Copy(source);byte[] before=Bytes(candidate);var c=Command(inspection.State,field==0?new string('a',64):inspection.Sha256,field==1?new string('b',64):inspection.TipSha256,inspection.Length+(field==2?1:0));using(var store=FloatGameStore.OpenExisting(candidate))Refused(store.AppendEquipmentUpgrade(c),"upgrade-source-mismatch");Need(Same(before,Bytes(candidate)),"Forged prefix proof wrote bytes.");}
        });
        Test("substituted equipment identity or line length refuses before any frame write",()=>{
            string source=Source();var inspection=FloatJournalArchive.Inspect(source);var mapped=Policy().Map(inspection.State);
            foreach(bool replaceId in new[]{false,true}){var items=new List<FloatEquipmentInstance>();foreach(var item in mapped.Instances){if(item.Kind==FloatEquipmentKind.Line)items.Add(new FloatEquipmentInstance(replaceId?G():item.Id,item.DefinitionKey,item.Kind,item.Revision,item.Location,replaceId?17321:17322,replaceId?17321:17322));else items.Add(item);}var forged=new FloatEquipmentLedger(mapped.RigId,mapped.RigRevision,mapped.InventoryRevision,mapped.Catalog,items);string candidate=Copy(source);byte[] before=Bytes(candidate);using(var store=FloatGameStore.OpenExisting(candidate))Refused(store.AppendEquipmentUpgrade(Command(inspection,FloatEquipmentCodec.Encode(forged))),"equipment-upgrade-invalid");Need(Same(before,Bytes(candidate)),"Forged mapping wrote bytes.");}
        });
        Test("offers bound to a different full equipment catalogue are refused",()=>{
            string source=Source();var inspection=FloatJournalArchive.Inspect(source);string candidate=Copy(source);byte[] before=Bytes(candidate);using(var store=FloatGameStore.OpenExisting(candidate))Refused(store.AppendEquipmentUpgrade(Command(inspection,null,FloatEquipmentOffersCodec.Encode(Offers(Catalog(150001))))),"equipment-upgrade-invalid");Need(Same(before,Bytes(candidate)),"Foreign catalogue wrote bytes.");
        });
        Test("type18 codec roundtrip rejects every truncated prefix trailing bytes and wrong schema",()=>{
            var command=Command(FloatJournalArchive.Inspect(Source()));byte[] bytes=FloatJournalCodec.EncodeCommand(command,3);var decoded=FloatJournalCodec.DecodeCommand(bytes);Need(decoded is UpgradeFloatEquipment&&decoded.Fingerprint==command.Fingerprint&&Same(bytes,FloatJournalCodec.EncodeCommand(decoded,3)),"Upgrade codec changed fingerprint.");
            for(int n=0;n<bytes.Length;n++){var cut=new byte[n];Array.Copy(bytes,cut,n);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(cut));}var extra=new byte[bytes.Length+1];Array.Copy(bytes,extra,bytes.Length);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(extra));foreach(byte schema in new byte[]{1,2}){var bad=(byte[])bytes.Clone();bad[4]=schema;Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bad));}
        });
        Test("upgrade payload arrays are defensively cloned for command identity",()=>{
            var i=FloatJournalArchive.Inspect(Source());var e=FloatEquipmentCodec.Encode(Policy().Map(i.State));var o=FloatEquipmentOffersCodec.Encode(Offers());var c=Command(i,e,o);string fingerprint=c.Fingerprint;e[0]^=1;o[0]^=1;var copy=c.CopyEquipmentPayload();copy[0]^=1;copy=c.CopyOffersPayload();copy[0]^=1;Need(c.Fingerprint==fingerprint&&c.ValidateMapping(i.State).RemainingLineMm==17321,"Mutable payload changed saved command.");
        });
        Test("new branch rejects old upgrade and legacy commands without duplicating migration",()=>{
            string source=Source();var i=FloatJournalArchive.Inspect(source);var c=Command(i);string candidate=Copy(source);using(var store=FloatGameStore.OpenExisting(candidate)){Commit(store.AppendEquipmentUpgrade(c));byte[] before=Bytes(candidate);Refused(store.AppendEquipmentUpgrade(c),"branch-mismatch");Refused(store.Execute(Cast(i.State)),"branch-mismatch");Need(store.State.Sequence==1&&Same(before,Bytes(candidate)),"Old branch wrote again.");}
        });
        Test("known prewrite upgrade failure preserves schema2 and retries the same command once",()=>{
            string source=Source(),candidate=Copy(source);var c=Command(FloatJournalArchive.Inspect(source));bool armed=true;using(var store=FloatGameStore.OpenExisting(candidate,point=>{if(armed&&point==FloatSaveFaultPoint.BeforeWrite)throw new IOException("private prewrite fault");})){byte[] before=Bytes(candidate);Throws<IOException>(()=>store.AppendEquipmentUpgrade(c));Need(!store.IsFaulted&&store.State.SchemaVersion==2&&Same(before,Bytes(candidate)),"Known failure published migration.");armed=false;Commit(store.AppendEquipmentUpgrade(c));Need(store.State.Receipts[0].RequestId==c.RequestId&&store.State.Receipts[0].EventId==c.EventId&&store.State.Sequence==1,"Retry changed migration identity.");}
        });
        Test("unknown after-flush migration freezes writer and complete reopen confirms one upgrade",()=>{
            string source=Source(),candidate=Copy(source);var c=Command(FloatJournalArchive.Inspect(source));using(var store=FloatGameStore.OpenExisting(candidate,point=>{if(point==FloatSaveFaultPoint.AfterFlush)throw new IOException("private after-flush fault");})){Throws<FloatSaveUncertainException>(()=>store.AppendEquipmentUpgrade(c));Need(store.IsFaulted,"Unknown write did not freeze.");Throws<InvalidOperationException>(()=>store.AppendEquipmentUpgrade(c));}var final=FloatJournalArchive.Inspect(candidate);Need(final.State.SchemaVersion==3&&final.Sequence==1&&final.State.Receipts[0].RequestId==c.RequestId,"Complete unknown frame was not recovered exactly.");
        });
        Test("partial migration is retained unchanged and strict reopen refuses repair",()=>{
            string source=Source(),candidate=Copy(source);var c=Command(FloatJournalArchive.Inspect(source));using(var store=FloatGameStore.OpenExisting(candidate,point=>{if(point==FloatSaveFaultPoint.AfterPrefix)throw new IOException("private partial fault");}))Throws<FloatSaveUncertainException>(()=>store.AppendEquipmentUpgrade(c));byte[] partial=Bytes(candidate);Throws<InvalidDataException>(()=>FloatJournalArchive.Inspect(candidate));Need(Same(partial,Bytes(candidate))&&partial.Length==Bytes(source).Length+12,"Partial bytes repaired or discarded.");
        });
        Test("upgraded branch can buy and install one spare then cast without replacing old identities",()=>{
            string source=Source();var i=FloatJournalArchive.Inspect(source);string candidate=Copy(source);Guid spare=G();using(var store=FloatGameStore.OpenExisting(candidate)){Commit(store.AppendEquipmentUpgrade(Command(i)));var s=store.State;Commit(store.Execute(new BuyFloatEquipment(s.ProfileId,s.BranchId,G(),G(),FloatEquipmentPurchaseQuote.Create(s,"spare-hook"),spare)));s=store.State;Commit(store.Execute(new ReplaceFloatSlot(s.ProfileId,s.BranchId,G(),G(),FloatEquipmentSlot.Hook,spare,s.Sequence,s.Rig.Revision,s.RulesVersion,s.Equipment.InventoryRevision,s.Equipment.RulesVersion)));s=store.State;Commit(store.Execute(new CastFloatWithEquipment(s.ProfileId,s.BranchId,G(),G(),G(),15000,s.Sequence,s.Rig.Revision,s.RulesVersion,s.Equipment.InventoryRevision,s.Equipment.RulesVersion)));Need(store.State.WalletMinor==11965&&store.State.BaitCount==11&&store.State.Stamina==98&&store.State.Equipment.Find(i.State.LegacyRig.HookId).Location.Place==FloatEquipmentPlace.Inventory&&store.State.Rig.HookId==spare,"Continued branch changed unrelated assets or costs.");}var final=FloatJournalArchive.Inspect(candidate);Need(final.Sequence==4&&final.State.Session!=null&&final.State.Equipment.Instances.Count==7&&final.State.Equipment.RemainingLineMm==17321&&final.State.EquipmentUpgradeOrigin!=null,"Continued candidate replay failed.");
        });
        return failed==0?0:1;
    }
}
