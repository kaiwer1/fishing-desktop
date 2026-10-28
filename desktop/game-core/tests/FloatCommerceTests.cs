using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FishingDesktop.GameCore;

static class FloatCommerceTests
{
    static int passed,failed,nextId,nextFile;static string root;
    static Guid G(){return new Guid(++nextId,0,0,new byte[]{0,0,0,0,0,0,0,5});}
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    static void Throws<T>(Action action) where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e);}}
    static string PathFor(string label){return Path.Combine(root,(++nextFile)+"-"+label+".fltj");}
    static FloatCommerceCatalog Catalog(int maximumBait=1000,int maximumFood=1000,long baitPrice=20,int baitPack=5,int restore=20)
    {return new FloatCommerceCatalog(1,"worm","landing-net","bread",2400,baitPack,baitPrice,20,restore,maximumBait,maximumFood,100);}
    static FloatGameState New(bool commerce=true,long wallet=12000,int stamina=100,int bait=12,int capacity=2,FloatCommerceCatalog catalog=null)
    {
        var rules=new FloatGameRules(1,"float-rig","worm",2,100,capacity,Int64.MaxValue,new[]{new FloatSpeciesRule("carp",100000,2000000,1000)});
        var rig=new FixedFloatRig(G(),1,"float-rig",G(),G(),G(),80000,G(),G(),G());Guid profile=G(),branch=G(),stack=G();
        return commerce?FloatGameState.CreateNewWithCommerce(profile,branch,wallet,stamina,stack,"worm",bait,rig,rules,catalog??Catalog(),G()):FloatGameState.CreateNew(profile,branch,wallet,stamina,stack,"worm",bait,rig,rules);
    }
    static FloatReduction Reduce(FloatGameState s,FloatCommand c){return FloatGameReducer.Reduce(s,c,s.Rules);}
    static FloatGameState Apply(FloatGameState s,FloatCommand c){var r=Reduce(s,c);Need(r.HasCandidate&&!r.IsReplay,"No candidate: "+r.Code);return r.Candidate;}
    static void Refused(FloatGameState s,FloatCommand c,string code){var r=Reduce(s,c);Need(r.Code==code&&!r.HasCandidate&&!r.IsReplay&&r.Receipt==null,"Expected "+code+" got "+r.Code);}
    static CastFloat Cast(FloatGameState s){return new CastFloat(s.ProfileId,s.BranchId,G(),G(),G(),s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static RecallFloat Recall(FloatGameState s){return new RecallFloat(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static HostFloatFish Fish(FloatGameState s,Guid id=default(Guid)){return new HostFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,id==Guid.Empty?G():id,G(),"carp",500000,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static ReleaseCurrentFloatFish Release(FloatGameState s){return new ReleaseCurrentFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static SellFloatFish Sell(FloatGameState s){return new SellFloatFish(s.ProfileId,s.BranchId,G(),G(),new[]{s.Creel[0].Id},s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static BuyFloatSupply Buy(FloatGameState s,FloatSupplyProduct product,int packs=1,Guid net=default(Guid),FloatPurchaseQuote quote=null)
    {return new BuyFloatSupply(s.ProfileId,s.BranchId,G(),G(),quote??FloatPurchaseQuote.Create(s,product,packs),product==FloatSupplyProduct.Net?(net==Guid.Empty?G():net):net,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static EquipFloatNet Equip(FloatGameState s,Guid net){return new EquipFloatNet(s.ProfileId,s.BranchId,G(),G(),net,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static UseFloatFood Food(FloatGameState s,Guid stack=default(Guid)){return new UseFloatFood(s.ProfileId,s.BranchId,G(),G(),stack==Guid.Empty?s.FoodStackId:stack,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static UpgradeFloatProfile Upgrade(FloatGameState s,string sha=null,string tip=null,long length=100,Guid food=default(Guid),FloatCommerceCatalog catalog=null)
    {return new UpgradeFloatProfile(s.ProfileId,s.BranchId,G(),G(),G(),food==Guid.Empty?G():food,sha??new string('0',64),tip??new string('1',64),length,s.Sequence,s.Rig.Revision,s.RulesVersion,catalog??Catalog());}
    static FloatGameState Land(FloatGameState s){s=Apply(s,Cast(s));return Apply(s,Fish(s));}
    static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    static byte[] Bytes(string path){using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){var b=new byte[(int)input.Length];int at=0;while(at<b.Length){int n=input.Read(b,at,b.Length-at);if(n==0)throw new EndOfStreamException();at+=n;}return b;}}
    static bool Same(byte[] a,byte[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
    static byte[] Prefix(byte[] bytes,int n){var b=new byte[n];Array.Copy(bytes,b,n);return b;}
    static string Tip(byte[] bytes){var b=new byte[32];Array.Copy(bytes,bytes.Length-32,b,0,32);return BitConverter.ToString(b).Replace("-","").ToLowerInvariant();}
    static UpgradeFloatProfile BoundUpgrade(FloatGameStore store,string path){byte[] b=Bytes(path);return Upgrade(store.State,Hash(b),Tip(b),b.Length);}
    static void Commit(FloatCommitResult r){Need(r.Committed&&r.Written&&!r.IsReplay&&r.Receipt!=null,"Commit failed: "+r.Code);}
    static string View(FloatGameState s)
    {
        string v=s.SchemaVersion+"|"+s.ProfileId+"|"+s.BranchId+"|"+s.Sequence+"|"+s.WalletMinor+"|"+s.Stamina+"|"+s.BaitStackId+"|"+s.BaitCount+"|"+s.FoodStackId+"|"+s.FoodCount+"|"+s.EquippedNetId+"|"+(s.OwnedNet==null?"none":s.OwnedNet.Id.ToString());
        foreach(var f in s.Creel)v+="|f:"+f.Id+":"+f.SaleValueMinor;
        if(s.Session!=null)v+="|s:"+s.Session.Id+":"+(s.Session.PendingFish==null?"none":s.Session.PendingFish.Id.ToString());
        foreach(var r in s.Receipts)v+="|r:"+r.Sequence+":"+r.Fingerprint+":"+r.Outcome+":"+r.FoodBefore+":"+r.FoodAfter;
        if(s.UpgradeOrigin!=null)v+="|o:"+s.UpgradeOrigin.SourceBranchId+":"+s.UpgradeOrigin.SourceSha256+":"+s.UpgradeOrigin.SourceTipSha256+":"+s.UpgradeOrigin.SourceLength+":"+s.UpgradeOrigin.SourceSequence;
        return v;
    }
    static void AppendRaw(string path,long sequence,byte[] payload)
    {
        byte[] prior=Bytes(path);byte[] tip=new byte[32];Array.Copy(prior,prior.Length-32,tip,0,32);byte[] body;
        using(var m=new MemoryStream()){using(var w=new BinaryWriter(m,Encoding.UTF8,true)){w.Write(payload.Length);w.Write(sequence);w.Write(tip);w.Write(payload);}body=m.ToArray();}
        using(var f=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.None)){f.Write(body,0,body.Length);using(var sha=SHA256.Create()){byte[] hash=sha.ComputeHash(body);f.Write(hash,0,hash.Length);}}
    }
    static void BadJournal(string path){byte[] before=Bytes(path);Throws<InvalidDataException>(()=>{using(var store=FloatGameStore.OpenExisting(path)){}});Need(Same(before,Bytes(path)),"Rejected journal changed.");}
    static void QuoteBad(FloatGameState s,FloatPurchaseQuote good,long version,FloatSupplyProduct product,int packs,int units,long total,string signature,string expected="quote-invalid",Guid profile=default(Guid),Guid branch=default(Guid))
    {var q=new FloatPurchaseQuote(profile==Guid.Empty?s.ProfileId:profile,branch==Guid.Empty?s.BranchId:branch,s.Sequence,version,product,packs,units,total,signature);Refused(s,Buy(s,product,quote:q),expected);}
    static Guid GoldenId(int x){return new Guid(x,0,0,new byte[]{1,2,3,4,5,6,7,8});}
    static byte[] LegacyGoldenBytes()
    {
        var rules=new FloatGameRules(1,"float-rig","worm",2,100,2,100000,new[]{new FloatSpeciesRule("carp",100000,2000000,1000)});
        var rig=new FixedFloatRig(GoldenId(1),1,"float-rig",GoldenId(2),GoldenId(3),GoldenId(4),80000,GoldenId(5),GoldenId(6),GoldenId(7));
        var state=FloatGameState.CreateNew(GoldenId(8),GoldenId(9),12000,100,GoldenId(10),"worm",12,rig,rules);
        FloatCommand[] commands={new CastFloat(GoldenId(8),GoldenId(9),GoldenId(11),GoldenId(12),GoldenId(13),0,1,1),new RecallFloat(GoldenId(8),GoldenId(9),GoldenId(14),GoldenId(15),GoldenId(13),1,1,1),new ReleaseCurrentFloatFish(GoldenId(8),GoldenId(9),GoldenId(16),GoldenId(17),GoldenId(13),2,1,1),new SellFloatFish(GoldenId(8),GoldenId(9),GoldenId(18),GoldenId(19),new[]{GoldenId(20),GoldenId(21)},3,1,1),new HostFloatFish(GoldenId(8),GoldenId(9),GoldenId(22),GoldenId(23),GoldenId(13),GoldenId(24),GoldenId(25),"carp",500000,4,1,1),new RecoverFloatSession(GoldenId(8),GoldenId(9),GoldenId(26),GoldenId(27),GoldenId(13),5,1,1,FloatRecoveryReason.StartupInterrupted)};
        using(var bytes=new MemoryStream()){var b=FloatJournalCodec.EncodeGenesis(state);bytes.Write(b,0,b.Length);foreach(var c in commands){b=FloatJournalCodec.EncodeCommand(c);bytes.Write(b,0,b.Length);}return bytes.ToArray();}
    }
    static int Main(string[] args)
    {
        if(args.Length!=1||!Directory.Exists(args[0]))return 2;root=Path.GetFullPath(args[0]);
        Test("schema2 zero-resource genesis adds only an empty food identity",()=>{
            var s=New(wallet:0,stamina:0,bait:0);var decoded=FloatJournalCodec.DecodeGenesis(FloatJournalCodec.EncodeGenesis(s));
            Need(View(s)==View(decoded)&&s.OwnedNet==null&&s.EquippedNetId==Guid.Empty&&s.FoodCount==0&&s.FoodStackId!=Guid.Empty,"Genesis gifted supplies.");
        });
        Test("explicit starter resources remain explicit with no owned net or food",()=>{var s=New();Need(s.WalletMinor==12000&&s.BaitCount==12&&s.OwnedNet==null&&s.FoodCount==0&&s.SchemaVersion==2,"Starter state changed.");});
        Test("catalogue constructors reject invalid values keys duplicates and excessive pack limits",()=>{
            Throws<ArgumentException>(()=>Catalog(baitPrice:0));Throws<ArgumentException>(()=>Catalog(baitPack:0));Throws<ArgumentException>(()=>Catalog(restore:0));Throws<ArgumentException>(()=>Catalog(maximumFood:0));
            Throws<ArgumentException>(()=>new FloatCommerceCatalog(1,"worm","worm","bread",1,1,1,1,1,10,10,1));Throws<ArgumentException>(()=>new FloatCommerceCatalog(1,"worm","net","bread",1,1,1,1,1,10,10,101));
        });
        Test("schema2 creation rejects food identity collision and incompatible bait policy",()=>{
            var s=New(false);Throws<ArgumentException>(()=>FloatGameState.CreateNewWithCommerce(s.ProfileId,s.BranchId,0,0,s.BaitStackId,"worm",0,s.LegacyRig,s.Rules,Catalog(),s.Rig.Id));
            Throws<ArgumentException>(()=>New(catalog:Catalog(maximumBait:5)));
        });
        Test("upgrade preserves zero resources rig history and profile while switching branch",()=>{
            var s=New(false,0,0,0);var c=Upgrade(s);var n=Apply(s,c);Need(n.SchemaVersion==2&&n.ProfileId==s.ProfileId&&n.BranchId==c.NewBranchId&&n.LegacyRig==s.LegacyRig&&n.Sequence==1&&n.WalletMinor==0&&n.Stamina==0&&n.BaitCount==0&&n.FoodCount==0&&n.OwnedNet==null&&n.EquippedNetId==Guid.Empty,"Upgrade granted or lost assets.");
            Need(n.UpgradeOrigin.SourceBranchId==s.BranchId&&n.UpgradeOrigin.SourceSequence==0&&n.Receipts[0].Outcome==FloatOutcome.ProfileUpgraded,"Upgrade origin missing.");
        });
        Test("upgrade preserves active cast and its debits until explicit new-branch recall",()=>{
            var s=New(false);var cast=Cast(s);s=Apply(s,cast);var n=Apply(s,Upgrade(s));Need(n.Session==s.Session&&n.BaitCount==s.BaitCount&&n.Stamina==s.Stamina&&n.Receipts[0]==s.Receipts[0],"Active cast altered.");
            n=Apply(n,Recall(n));Need(n.Session==null&&n.BaitCount==11&&n.Stamina==98,"Recall refunded active resources.");
        });
        Test("upgrade retains full creel pending fish value IDs and release semantics",()=>{
            var s=Land(New(false,capacity:1));s=Apply(s,Cast(s));s=Apply(s,Fish(s));var n=Apply(s,Upgrade(s));
            Need(n.Creel[0]==s.Creel[0]&&n.Session==s.Session&&n.Session.PendingFish==s.Session.PendingFish&&n.RulesVersion==s.RulesVersion,"Pending migration changed fish.");
            n=Apply(n,Release(n));Need(n.Session==null&&n.Creel.Count==1&&n.Creel[0].SaleValueMinor==500,"Pending release altered stored fish.");
        });
        Test("upgrade is one way and old branch commands cannot replay into candidate",()=>{
            var s=New(false);var old=Cast(s);s=Apply(s,old);var upgrade=Upgrade(s);var n=Apply(s,upgrade);
            Refused(n,upgrade,"branch-mismatch");Refused(n,old,"branch-mismatch");Refused(n,Upgrade(n),"profile-already-upgraded");
            var altered=new CastFloat(n.ProfileId,n.BranchId,old.RequestId,old.EventId,old.SessionId,n.Sequence,n.Rig.Revision,n.RulesVersion);Refused(n,altered,"request-conflict");
        });
        Test("upgrade rejects reused owned or historical fish food-stack identities",()=>{
            var s=Land(New(false));Guid oldFish=s.Creel[0].Id;s=Apply(s,Sell(s));Refused(s,Upgrade(s,food:oldFish),"asset-identity-used");Refused(s,Upgrade(s,food:s.Rig.HookId.Value),"asset-identity-used");Refused(s,Upgrade(s,catalog:Catalog(maximumBait:1)),"commerce-configuration-mismatch");
        });
        Test("net purchase charges once and does not equip automatically",()=>{
            var s=New();var buy=Buy(s,FloatSupplyProduct.Net);var n=Apply(s,buy);Need(n.WalletMinor==9600&&n.OwnedNet.Id==buy.NewNetId&&n.EquippedNetId==Guid.Empty&&n.FoodCount==0&&n.BaitCount==12,"Net purchase side effects.");
            var replay=Reduce(n,buy);Need(replay.IsReplay&&!replay.HasCandidate&&replay.Receipt.OwnedNetAfter==buy.NewNetId,"Net duplicate lost receipt.");Refused(n,Buy(n,FloatSupplyProduct.Net),"net-already-owned");
        });
        Test("equip and unequip require actual owned net without purchase or deletion",()=>{
            var s=New();Refused(s,Equip(s,G()),"net-not-owned");Refused(s,Equip(s,Guid.Empty),"net-equipment-unchanged");s=Apply(s,Buy(s,FloatSupplyProduct.Net));Guid id=s.OwnedNet.Id;s=Apply(s,Equip(s,id));Refused(s,Equip(s,id),"net-equipment-unchanged");s=Apply(s,Equip(s,Guid.Empty));Need(s.OwnedNet.Id==id&&s.WalletMinor==9600&&s.EquippedNetId==Guid.Empty,"Unequip lost/refunded net.");
        });
        Test("bait packs use integer units existing stack and exact checked debit",()=>{
            var s=New();var n=Apply(s,Buy(s,FloatSupplyProduct.Bait,2));Need(n.BaitCount==22&&n.BaitStackId==s.BaitStackId&&n.WalletMinor==11960&&n.Stamina==100&&n.FoodCount==0,"Bait pack conservation failed.");
        });
        Test("food purchase does not consume or restore stamina and explicit use restores one portion",()=>{
            var s=New(stamina:70);s=Apply(s,Buy(s,FloatSupplyProduct.Food,3));Need(s.Stamina==70&&s.FoodCount==3&&s.WalletMinor==11940,"Food bought and consumed together.");
            s=Apply(s,Food(s));Need(s.Stamina==90&&s.FoodCount==2,"Food restoration wrong.");s=Apply(s,Food(s));Need(s.Stamina==100&&s.FoodCount==1,"Food cap wrong.");Refused(s,Food(s),"stamina-full");
        });
        Test("empty or foreign food cannot restore stamina",()=>{var s=New(stamina:0);Refused(s,Food(s),"food-empty");Refused(s,Food(s,G()),"food-not-owned");});
        Test("full-stamina use neither consumes food nor advances the sequence",()=>{
            var s=New();s=Apply(s,Buy(s,FloatSupplyProduct.Food));string before=View(s);Refused(s,Food(s),"stamina-full");Need(View(s)==before&&s.FoodCount==1,"Full-stamina action consumed or mutated state.");
        });
        Test("insufficient funds reject each product without partial asset change",()=>{
            foreach(FloatSupplyProduct p in new[]{FloatSupplyProduct.Net,FloatSupplyProduct.Bait,FloatSupplyProduct.Food}){var s=New(wallet:0);string before=View(s);Refused(s,Buy(s,p),"wallet-insufficient");Need(View(s)==before,"Failed purchase mutated input.");}
        });
        Test("all commerce actions are blocked throughout active and pending sessions",()=>{
            var s=New(capacity:1);s=Apply(s,Buy(s,FloatSupplyProduct.Net));s=Apply(s,Buy(s,FloatSupplyProduct.Food));s=Apply(s,Cast(s));
            foreach(var x in new[]{s,Apply(s,Fish(s))}){var active=x.Session==null?Apply(x,Cast(x)):x;if(active.Creel.Count==1)active=Apply(active,Fish(active));Refused(active,Buy(active,FloatSupplyProduct.Bait),"session-busy");Refused(active,Equip(active,active.OwnedNet.Id),"session-busy");Refused(active,Food(active),"session-busy");}
        });
        Test("legacy profile cannot purchase equip or use supplies before explicit upgrade",()=>{
            var s=New(false);var q=new FloatPurchaseQuote(s.ProfileId,s.BranchId,0,1,FloatSupplyProduct.Bait,1,5,20,Catalog().Signature);Refused(s,Buy(s,FloatSupplyProduct.Bait,quote:q),"commerce-upgrade-required");Refused(s,Equip(s,G()),"commerce-upgrade-required");Refused(s,Food(s,G()),"commerce-upgrade-required");Throws<ArgumentException>(()=>FloatPurchaseQuote.Create(s,FloatSupplyProduct.Bait,1));
        });
        Test("a quote cannot be renewed by replacing command sequence after another transaction",()=>{
            var s=New();var q=FloatPurchaseQuote.Create(s,FloatSupplyProduct.Bait,1);s=Apply(s,Buy(s,FloatSupplyProduct.Food));Refused(s,Buy(s,FloatSupplyProduct.Bait,quote:q),"quote-stale");
        });
        Test("forged quote price units catalogue version and policy cannot alter debit",()=>{
            var s=New();var q=FloatPurchaseQuote.Create(s,FloatSupplyProduct.Bait,1);
            QuoteBad(s,q,1,q.Product,1,5,1,q.CatalogSignature);QuoteBad(s,q,1,q.Product,1,500,20,q.CatalogSignature);QuoteBad(s,q,2,q.Product,1,5,20,q.CatalogSignature);QuoteBad(s,q,1,q.Product,1,5,20,"another-policy");QuoteBad(s,q,1,q.Product,1,5,20,q.CatalogSignature,"quote-stale",profile:G());QuoteBad(s,q,1,q.Product,1,5,20,q.CatalogSignature,"quote-stale",branch:G());
        });
        Test("invalid pack counts products and overflow never become quotes",()=>{
            var s=New();Throws<ArgumentException>(()=>FloatPurchaseQuote.Create(s,FloatSupplyProduct.Bait,0));Throws<ArgumentException>(()=>FloatPurchaseQuote.Create(s,FloatSupplyProduct.Bait,101));Throws<ArgumentException>(()=>FloatPurchaseQuote.Create(s,FloatSupplyProduct.Net,2));Throws<ArgumentException>(()=>FloatPurchaseQuote.Create(s,(FloatSupplyProduct)99,1));
            var large=New(catalog:Catalog(baitPrice:Int64.MaxValue));Throws<OverflowException>(()=>FloatPurchaseQuote.Create(large,FloatSupplyProduct.Bait,2));
            var units=New(catalog:Catalog(baitPack:Int32.MaxValue));Throws<OverflowException>(()=>FloatPurchaseQuote.Create(units,FloatSupplyProduct.Bait,2));
        });
        Test("purchase validates net identity against all assets and historical fish",()=>{
            var s=Land(New());Guid historical=s.Creel[0].Id;s=Apply(s,Sell(s));foreach(var id in new[]{s.Rig.Id,s.Rig.LineId.Value,s.BaitStackId,s.FoodStackId,historical})Refused(s,Buy(s,FloatSupplyProduct.Net,net:id),"asset-identity-used");
            var q=FloatPurchaseQuote.Create(s,FloatSupplyProduct.Net,1);Refused(s,new BuyFloatSupply(s.ProfileId,s.BranchId,G(),G(),q,Guid.Empty,s.Sequence,1,1),"asset-identity-used");Refused(s,Buy(s,FloatSupplyProduct.Bait,net:G()),"purchase-asset-invalid");
        });
        Test("stock limits and arithmetic overflow reject without charging",()=>{
            var s=New(catalog:Catalog(maximumBait:12,maximumFood:1));Refused(s,Buy(s,FloatSupplyProduct.Bait),"bait-limit-exceeded");s=Apply(s,Buy(s,FloatSupplyProduct.Food));Refused(s,Buy(s,FloatSupplyProduct.Food),"food-limit-exceeded");
            s=New(catalog:Catalog(baitPack:Int32.MaxValue));Refused(s,Buy(s,FloatSupplyProduct.Bait),"arithmetic-overflow");
        });
        Test("purchase requests are atomic idempotent and altered content conflicts",()=>{
            var s=New();var c=Buy(s,FloatSupplyProduct.Food,2);var n=Apply(s,c);Need(Reduce(n,c).IsReplay,"Duplicate was not replayed.");var changed=new BuyFloatSupply(c.ProfileId,c.BranchId,c.RequestId,c.EventId,c.Quote,G(),c.ExpectedSequence,1,1);Refused(n,changed,"request-conflict");
            var other=new EquipFloatNet(n.ProfileId,n.BranchId,G(),c.EventId,G(),n.Sequence,1,1);Refused(n,other,"event-already-used");Need(n.FoodCount==2&&n.WalletMinor==11960,"Duplicate charged twice.");
        });
        Test("old fishing commands preserve schema2 equipment supplies and original fish prices",()=>{
            var s=New(capacity:1);s=Apply(s,Buy(s,FloatSupplyProduct.Net));s=Apply(s,Equip(s,s.OwnedNet.Id));s=Apply(s,Buy(s,FloatSupplyProduct.Food,2));Guid net=s.EquippedNetId,food=s.FoodStackId;s=Land(s);s=Apply(s,Cast(s));s=Apply(s,Fish(s));s=Apply(s,Release(s));s=Apply(s,Sell(s));s=Apply(s,Cast(s));s=Apply(s,Recall(s));s=Apply(s,Cast(s));s=Apply(s,new RecoverFloatSession(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,1,1,FloatRecoveryReason.OwnerShutdown));
            Need(s.SchemaVersion==2&&s.EquippedNetId==net&&s.OwnedNet.Id==net&&s.FoodStackId==food&&s.FoodCount==2&&s.RulesVersion==1&&s.WalletMinor==10060,"Fishing erased commerce or repriced sale.");
        });
        Test("schema2 command payloads round trip exact fingerprints and remain bounded",()=>{
            var s=New();FloatCommand[] commands={Buy(s,FloatSupplyProduct.Bait,100),Buy(s,FloatSupplyProduct.Net),Equip(s,G()),Food(s),Upgrade(New(false)),Cast(s),Recall(Apply(s,Cast(s)))};
            foreach(var c in commands){byte[] b=FloatJournalCodec.EncodeCommand(c,2);var d=FloatJournalCodec.DecodeCommand(b);Need(d.GetType()==c.GetType()&&d.Fingerprint==c.Fingerprint&&b.Length<65536&&FloatJournalCodec.CommandSchema(b)==2,"Command encoding changed.");if(c is BuyFloatSupply||c is EquipFloatNet||c is UseFloatFood||c is UpgradeFloatProfile)Throws<InvalidDataException>(()=>FloatJournalCodec.EncodeCommand(c,1));}
        });
        Test("all truncated schema2 genesis and commerce command prefixes reject",()=>{
            var s=New();byte[] genesis=FloatJournalCodec.EncodeGenesis(s);for(int n=0;n<genesis.Length;n++){var b=Prefix(genesis,n);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeGenesis(b));}
            foreach(var c in new FloatCommand[]{Buy(s,FloatSupplyProduct.Food),Equip(s,G()),Food(s),Upgrade(New(false))}){byte[] full=FloatJournalCodec.EncodeCommand(c);for(int n=0;n<full.Length;n++){var b=Prefix(full,n);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(b));}}
        });
        Test("malformed schema2 versions supply enums session metadata and trailing bytes reject",()=>{
            var s=New();byte[] b=FloatJournalCodec.EncodeCommand(Buy(s,FloatSupplyProduct.Food));byte[] bad=(byte[])b.Clone();bad[4]=4;Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bad));bad=(byte[])b.Clone();bad[72]=1;Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bad));
            var extra=new byte[b.Length+1];Array.Copy(b,extra,b.Length);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(extra));
            bad=(byte[])b.Clone();bad[160]=99;Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bad));
        });
        Test("real schema2 purchase equip food and fishing journal replays exact confirmed state",()=>{
            string path=PathFor("commerce");string expected;
            using(var store=FloatGameStore.CreateNew(path,New(stamina:70))){Commit(store.Execute(Buy(store.State,FloatSupplyProduct.Net)));Commit(store.Execute(Equip(store.State,store.State.OwnedNet.Id)));Commit(store.Execute(Buy(store.State,FloatSupplyProduct.Food,3)));Commit(store.Execute(Food(store.State)));Commit(store.Execute(Buy(store.State,FloatSupplyProduct.Bait)));Commit(store.Execute(Cast(store.State)));Commit(store.Execute(Fish(store.State)));Commit(store.Execute(Sell(store.State)));expected=View(store.State);}
            using(var store=FloatGameStore.OpenExisting(path))Need(View(store.State)==expected,"Commerce replay diverged.");
        });
        Test("migration appends bounded upgrade preserving byte-exact legacy prefix and history",()=>{
            string path=PathFor("migration");byte[] prefix;string expected;UpgradeFloatProfile upgrade;
            using(var store=FloatGameStore.CreateNew(path,New(false))){Commit(store.Execute(Cast(store.State)));Commit(store.Execute(Fish(store.State)));prefix=Bytes(path);upgrade=BoundUpgrade(store,path);Commit(store.AppendUpgrade(upgrade));Need(Same(prefix,Prefix(Bytes(path),prefix.Length)),"Legacy prefix rewritten.");Need(store.State.Receipts.Count==3&&store.State.UpgradeOrigin.SourceSha256==Hash(prefix),"Source identity missing.");Commit(store.Execute(Buy(store.State,FloatSupplyProduct.Net)));expected=View(store.State);}
            using(var store=FloatGameStore.OpenExisting(path)){Need(View(store.State)==expected,"Upgrade failed replay.");var r=store.AppendUpgrade(upgrade);Need(!r.Committed&&r.Code=="branch-mismatch","Old branch upgrade retried into new state.");}
        });
        Test("upgrade prefix metadata rejects wrong whole hash tip length with no writes",()=>{
            string path=PathFor("proof");using(var store=FloatGameStore.CreateNew(path,New(false))){byte[] b=Bytes(path);foreach(var c in new[]{Upgrade(store.State,new string('0',64),Tip(b),b.Length),Upgrade(store.State,Hash(b),new string('0',64),b.Length),Upgrade(store.State,Hash(b),Tip(b),b.Length+1)}){var r=store.Execute(c);Need(!r.Committed&&r.Code=="upgrade-source-mismatch"&&Same(b,Bytes(path))&&store.State.SchemaVersion==1,"Unproven upgrade wrote data.");}}
        });
        Test("replay rejects tampered source proof even with a valid outer frame hash",()=>{
            foreach(int which in new[]{0,1,2}){string path=PathFor("false-proof");FloatGameState s;byte[] b;using(var store=FloatGameStore.CreateNew(path,New(false))){s=store.State;b=Bytes(path);}var c=Upgrade(s,which==0?new string('0',64):Hash(b),which==1?new string('0',64):Tip(b),b.Length+(which==2?1:0));AppendRaw(path,1,FloatJournalCodec.EncodeCommand(c));BadJournal(path);}
        });
        Test("schema2 old commands before upgrade and schema1 commands after upgrade reject",()=>{
            string a=PathFor("unannounced");FloatGameState s;using(var store=FloatGameStore.CreateNew(a,New(false)))s=store.State;AppendRaw(a,1,FloatJournalCodec.EncodeCommand(Cast(s),2));BadJournal(a);
            string b=PathFor("downgrade");using(var store=FloatGameStore.CreateNew(b,New(false))){Commit(store.AppendUpgrade(BoundUpgrade(store,b)));s=store.State;}AppendRaw(b,2,FloatJournalCodec.EncodeCommand(Cast(s),1));BadJournal(b);
        });
        Test("duplicate upgrade and duplicate purchase frames cannot add another receipt",()=>{
            string a=PathFor("twice-upgrade");UpgradeFloatProfile c;using(var store=FloatGameStore.CreateNew(a,New(false))){c=BoundUpgrade(store,a);Commit(store.AppendUpgrade(c));}AppendRaw(a,2,FloatJournalCodec.EncodeCommand(c));BadJournal(a);
            string b=PathFor("twice-purchase");BuyFloatSupply buy;using(var store=FloatGameStore.CreateNew(b,New())){buy=Buy(store.State,FloatSupplyProduct.Food);Commit(store.Execute(buy));}AppendRaw(b,2,FloatJournalCodec.EncodeCommand(buy));BadJournal(b);
        });
        Test("migration keeps active and pending sessions through real close and replay",()=>{
            foreach(bool pending in new[]{false,true}){string path=PathFor("unfinished");Guid session,fish=Guid.Empty;string expected;
                using(var store=FloatGameStore.CreateNew(path,New(false,capacity:1))){if(pending){Commit(store.Execute(Cast(store.State)));Commit(store.Execute(Fish(store.State)));}Commit(store.Execute(Cast(store.State)));if(pending){Commit(store.Execute(Fish(store.State)));fish=store.State.Session.PendingFish.Id;}session=store.State.Session.Id;Commit(store.AppendUpgrade(BoundUpgrade(store,path)));expected=View(store.State);}
                using(var store=FloatGameStore.OpenExisting(path)){Need(View(store.State)==expected&&store.State.Session.Id==session&&(!pending||store.State.Session.PendingFish.Id==fish),"Migration auto settled or discarded session.");Commit(store.Execute(pending?(FloatCommand)Release(store.State):Recall(store.State)));}
            }
        });
        Test("known prewrite purchase failure leaves file and state available for same-command retry",()=>{
            string path=PathFor("beforewrite");bool armed=false;using(var store=FloatGameStore.CreateNew(path,New(),p=>{if(armed&&p==FloatSaveFaultPoint.BeforeWrite)throw new IOException("fixture");})){byte[] before=Bytes(path);var c=Buy(store.State,FloatSupplyProduct.Net);armed=true;Throws<IOException>(()=>store.Execute(c));Need(!store.IsFaulted&&store.State.OwnedNet==null&&Same(before,Bytes(path)),"Known failure changed state.");armed=false;Commit(store.Execute(c));}
        });
        Test("uncertain completed purchase disables writer and reopen replays exactly once",()=>{
            string path=PathFor("unknownpurchase");bool armed=false;BuyFloatSupply c;
            using(var store=FloatGameStore.CreateNew(path,New(),p=>{if(armed&&p==FloatSaveFaultPoint.AfterFlush)throw new IOException("fixture");})){c=Buy(store.State,FloatSupplyProduct.Food,3);armed=true;Throws<FloatSaveUncertainException>(()=>store.Execute(c));Need(store.IsFaulted,"Uncertain writer remains active.");Throws<InvalidOperationException>(()=>store.Execute(c));}
            using(var store=FloatGameStore.OpenExisting(path)){byte[] before=Bytes(path);var r=store.Execute(c);Need(r.IsReplay&&!r.Written&&store.State.FoodCount==3&&store.State.WalletMinor==11940&&Same(before,Bytes(path)),"Uncertain purchase duplicated.");}
        });
        Test("uncertain completed upgrade reopens to one new branch without gifts or rewrite",()=>{
            string path=PathFor("unknownupgrade");bool armed=false;Guid target;byte[] prefix;
            using(var store=FloatGameStore.CreateNew(path,New(false,0,0,0),p=>{if(armed&&p==FloatSaveFaultPoint.AfterFlush)throw new IOException("fixture");})){var c=BoundUpgrade(store,path);target=c.NewBranchId;prefix=Bytes(path);armed=true;Throws<FloatSaveUncertainException>(()=>store.AppendUpgrade(c));}
            using(var store=FloatGameStore.OpenExisting(path))Need(store.State.BranchId==target&&store.State.Sequence==1&&store.State.WalletMinor==0&&store.State.FoodCount==0&&store.State.OwnedNet==null&&Same(prefix,Prefix(Bytes(path),prefix.Length)),"Upgrade retry rewrote or gifted.");
        });
        Test("partial upgrade remains rejected and retained instead of repairing legacy history",()=>{
            string path=PathFor("partial-upgrade");bool armed=false;byte[] prefix;
            using(var store=FloatGameStore.CreateNew(path,New(false),p=>{if(armed&&p==FloatSaveFaultPoint.AfterPrefix)throw new IOException("fixture");})){prefix=Bytes(path);var c=BoundUpgrade(store,path);armed=true;Throws<FloatSaveUncertainException>(()=>store.AppendUpgrade(c));}
            Need(Bytes(path).Length>prefix.Length&&Same(prefix,Prefix(Bytes(path),prefix.Length)),"Partial candidate deleted prefix.");BadJournal(path);
        });
        Test("legacy genesis and all six commands retain the published schema1 byte fingerprint",()=>{
            // Captured by compiling the unmodified core from commit 3b78190.
            Need(Hash(LegacyGoldenBytes())=="113d05cce559bfa6b6a89255e49f6cb3f0f396d68a89a3a0bba9f09f9033348c","Published schema1 encoding changed.");
        });
        Test("upgrade at simulated byte budget boundary rejects without prefix changes",()=>{
            string path=PathFor("capacity-bytes");using(var store=FloatGameStore.CreateNew(path,New(false))){byte[] before=Bytes(path);var c=BoundUpgrade(store,path);var field=typeof(FloatGameStore).GetField("_length",BindingFlags.Instance|BindingFlags.NonPublic);field.SetValue(store,FloatGameStore.MaximumFileBytes);var r=store.AppendUpgrade(c);Need(r.Code=="save-capacity"&&!r.Written&&!r.Committed&&store.State.SchemaVersion==1&&Same(before,Bytes(path)),"Capacity refusal changed candidate.");}
        });
        Test("upgrade at simulated transaction budget rejects without resetting history",()=>{
            string path=PathFor("capacity-transactions");using(var store=FloatGameStore.CreateNew(path,New(false,0,0,0))){byte[] before=Bytes(path);var original=store.State;var history=new List<FloatReceipt>();
                for(int i=0;i<FloatGameStore.MaximumTransactions;i+=2){Guid session=G();var cast=new CastFloat(original.ProfileId,original.BranchId,G(),G(),session,i,1,1);var recall=new RecallFloat(original.ProfileId,original.BranchId,G(),G(),session,i+1,1,1);history.Add(new FloatReceipt(cast,i+1,1,FloatOutcome.CastCommitted,Guid.Empty,0,0,2,0,1,0,new Guid[0]));history.Add(new FloatReceipt(recall,i+2,1,FloatOutcome.Recalled,Guid.Empty,0,0,0,0,0,0,new Guid[0]));}
                var full=new FloatGameState(original.ProfileId,original.BranchId,history.Count,0,0,original.BaitStackId,"worm",0,original.LegacyRig,null,new FloatFish[0],history,original.Rules);typeof(FloatGameStore).GetField("_state",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(store,full);
                var r=store.AppendUpgrade(Upgrade(full,Hash(before),Tip(before),before.Length));Need(r.Code=="save-capacity"&&!r.Written&&!r.Committed&&store.State.Sequence==10000&&store.State.Receipts.Count==10000&&Same(before,Bytes(path)),"Transaction cap reset old history.");
            }
        });
        Console.WriteLine("RESULT "+passed+" passed "+failed+" failed");return failed==0?0:1;
    }
}
