using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using FishingDesktop.GameCore;

static class FloatEquipmentProfileUpgradeTests
{
    static int passed,failed,nextId,nextFile;
    static string root;
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool CreateHardLink(string newFileName,string existingFileName,IntPtr securityAttributes);
    static Guid G(){return new Guid(++nextId,0,0,new byte[]{0,0,0,0,0,0,0,61});}
    static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Reject<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e);}}
    static string P(string name){return Path.Combine(root,(++nextFile).ToString(CultureInfo.InvariantCulture)+"-"+name+".fltj");}
    static byte[] Bytes(string path){return File.ReadAllBytes(path);}
    static bool Same(byte[] a,byte[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
    static bool Prefix(byte[] a,byte[] b){if(a.Length>=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
    static void NoOutputs(string backup,string candidate){Need(!File.Exists(backup)&&!File.Exists(candidate),"Rejected preflight created output.");}
    static void Link(string alias,string existing){if(!CreateHardLink(alias,existing,IntPtr.Zero))throw new IOException("Private hardlink fixture failed: "+Marshal.GetLastWin32Error());}
    static FloatCommerceCatalog Commerce(){return new FloatCommerceCatalog(1,"worm","net","bread",2400,5,20,20,20,1000,1000,100);}
    static FloatEquipmentCatalog Catalog(long mass=150000)
    {
        return new FloatEquipmentCatalog(1,"worm",new[]{
            new FloatEquipmentDefinition("rod",FloatEquipmentKind.Rod,mass,0,1000,25000),
            new FloatEquipmentDefinition("reel",FloatEquipmentKind.Reel,200000),new FloatEquipmentDefinition("line",FloatEquipmentKind.Line,0,180),
            new FloatEquipmentDefinition("hook",FloatEquipmentKind.Hook,200),new FloatEquipmentDefinition("bobber",FloatEquipmentKind.Bobber,3000,0,2000,3000),
            new FloatEquipmentDefinition("sinker",FloatEquipmentKind.Sinker,2500)},
            new[]{new FloatEquipmentPairRule("rod","reel",true)},new[]{new FloatEquipmentSpoolRule("reel","line",180,50000)},
            new[]{new FloatEquipmentPairRule("hook","worm",true)},new[]{new FloatEquipmentLossRule("terminal",new[]{FloatEquipmentSlot.Hook})});
    }
    static FloatEquipmentMigrationPolicy Policy(long version=1,string source="legacy-float",FloatEquipmentCatalog catalog=null)
    {return new FloatEquipmentMigrationPolicy(version,source,catalog??Catalog(),"rod","reel","line","hook","bobber","sinker");}
    static FloatEquipmentOffers Offers(FloatEquipmentMigrationPolicy policy,long price=35)
    {return new FloatEquipmentOffers(1,policy.Catalog,new[]{new FloatEquipmentOffer("hook-one","hook",FloatEquipmentKind.Hook,price),new FloatEquipmentOffer("line-50m","line",FloatEquipmentKind.Line,400,50000)},1000000);}
    static FloatGameState New(int schema=2,long wallet=12000,int stamina=100,int bait=12,string rigKey="legacy-float")
    {
        var rules=new FloatGameRules(2,rigKey,"worm",2,100,1,1000000,new[]{new FloatSpeciesRule("carp",100000,10000000,120)});
        var rig=new FixedFloatRig(G(),7,rigKey,G(),G(),G(),17321,G(),G(),G());var profile=G();var branch=G();var stack=G();
        return schema==1?FloatGameState.CreateNew(profile,branch,wallet,stamina,stack,"worm",bait,rig,rules):
            FloatGameState.CreateNewWithCommerce(profile,branch,wallet,stamina,stack,"worm",bait,rig,rules,Commerce(),G());
    }
    static string Source(FloatGameState state=null){string path=P("source");using(var store=FloatGameStore.CreateNew(path,state??New())){}return path;}
    static FloatProfileUpgradeResult Upgrade(string source,string backup,string candidate,FloatEquipmentMigrationPolicy policy=null,FloatEquipmentOffers offers=null)
    {policy=policy??Policy();return FloatEquipmentProfileUpgrade.CreateCandidate(source,backup,candidate,policy,offers??Offers(policy));}
    static void Commit(FloatCommitResult r){Need(r.Committed&&r.Written&&!r.IsReplay,"Fixture command failed: "+r.Code);}
    static CastFloat Cast(FloatGameState s){return new CastFloat(s.ProfileId,s.BranchId,G(),G(),G(),s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static HostFloatFish Fish(FloatGameState s){return new HostFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,G(),G(),"carp",500000,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static BuyFloatSupply Supply(FloatGameState s,FloatSupplyProduct product){return new BuyFloatSupply(s.ProfileId,s.BranchId,G(),G(),FloatPurchaseQuote.Create(s,product,1),product==FloatSupplyProduct.Net?G():Guid.Empty,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static bool OriginSame(FloatUpgradeOrigin a,FloatUpgradeOrigin b)
    {if(a==null||b==null)return a==null&&b==null;return a.SourceBranchId==b.SourceBranchId&&a.SourceSha256==b.SourceSha256&&a.SourceTipSha256==b.SourceTipSha256&&a.SourceLength==b.SourceLength&&a.SourceSequence==b.SourceSequence;}
    static void Preserved(FloatGameState a,FloatGameState b)
    {
        Need(b.SchemaVersion==3&&b.Sequence==a.Sequence+1&&b.ProfileId==a.ProfileId&&b.BranchId!=a.BranchId&&b.Session==null,"Profile not migrated exactly once.");
        Need(a.WalletMinor==b.WalletMinor&&a.Stamina==b.Stamina&&a.BaitStackId==b.BaitStackId&&a.BaitCount==b.BaitCount&&a.BaitDefinitionKey==b.BaitDefinitionKey&&a.Rules.Signature==b.Rules.Signature,"Legacy resources changed.");
        Need(a.FoodStackId==b.FoodStackId&&a.FoodCount==b.FoodCount&&a.EquippedNetId==b.EquippedNetId&&a.CommerceCatalog.Signature==b.CommerceCatalog.Signature,"Commerce ownership changed.");
        Need((a.OwnedNet==null)==(b.OwnedNet==null)&&(a.OwnedNet==null||a.OwnedNet.Id==b.OwnedNet.Id),"Owned net changed.");
        var x=a.Rig;var y=b.Rig;Need(x.Id==y.Id&&x.Revision==y.Revision&&x.DefinitionKey==y.DefinitionKey&&x.RodId==y.RodId&&x.ReelId==y.ReelId&&x.LineId==y.LineId&&x.LineLengthMm==y.LineLengthMm&&x.HookId==y.HookId&&x.BobberId==y.BobberId&&x.SinkerId==y.SinkerId,"Rig assets changed.");
        Need(b.Equipment.Instances.Count==6&&b.Equipment.InitialLineMm==17321&&b.Equipment.RemainingLineMm==17321&&b.Equipment.LostLineMm==0,"Mapping refilled or granted assets.");
        Need(a.Creel.Count==b.Creel.Count&&a.Receipts.Count+1==b.Receipts.Count,"Historical counts changed.");
        for(int i=0;i<a.Creel.Count;i++)Need(a.Creel[i].Id==b.Creel[i].Id&&a.Creel[i].SaleValueMinor==b.Creel[i].SaleValueMinor&&a.Creel[i].MassMg==b.Creel[i].MassMg,"Historical fish changed.");
        for(int i=0;i<a.Receipts.Count;i++)Need(a.Receipts[i].Fingerprint==b.Receipts[i].Fingerprint&&a.Receipts[i].Sequence==b.Receipts[i].Sequence&&a.Receipts[i].Outcome==b.Receipts[i].Outcome,"Historical receipt changed.");
        Need(OriginSame(a.UpgradeOrigin,b.UpgradeOrigin)&&b.Receipts[b.Receipts.Count-1].Outcome==FloatOutcome.EquipmentProfileUpgraded,"Old origin or new receipt changed.");
    }
    static int Main(string[] args)
    {
        if(args.Length!=1||!Directory.Exists(args[0])){Console.WriteLine("FAIL existing private output directory required");return 2;}root=Path.GetFullPath(args[0]);
        Test("exact source and independent backup retain bytes while candidate appends one equipment upgrade",()=>{
            string source=Source(New(rigKey:"float-starter")),backup=P("backup"),candidate=P("candidate");var before=FloatJournalArchive.Inspect(source);byte[] bytes=Bytes(source);var result=Upgrade(source,backup,candidate,Policy(source:"float-starter"));var after=FloatJournalArchive.Inspect(candidate);
            Need(Same(bytes,Bytes(source))&&Same(bytes,Bytes(backup))&&Prefix(bytes,Bytes(candidate)),"Source or prefix changed.");Preserved(before.State,after.State);
            var origin=after.State.EquipmentUpgradeOrigin;Need(origin.SourceSha256==before.Sha256&&origin.SourceTipSha256==before.TipSha256&&origin.SourceLength==before.Length&&origin.SourceSequence==before.Sequence&&origin.SourceBranchId==before.BranchId,"New origin not bound to source.");
            Need(!result.ExistingCandidateVerified&&result.SourceSha256==before.Sha256&&result.CandidateSha256==after.Sha256&&result.CandidateSequence==before.Sequence+1&&result.CandidateBranchId==after.BranchId&&result.BackupPath==Path.GetFullPath(backup)&&result.CandidatePath==Path.GetFullPath(candidate),"Result metadata differs.");
        });
        Test("schema1 then schema2 then schema3 preserves both origins fish food net and old receipts",()=>{
            string old=Source(New(schema:1)),source=P("schema2");FloatProfileUpgrade.CreateCandidate(old,P("legacy-backup"),source,Commerce());
            using(var store=FloatGameStore.OpenExisting(source)){Commit(store.Execute(Supply(store.State,FloatSupplyProduct.Net)));var s=store.State;Commit(store.Execute(new EquipFloatNet(s.ProfileId,s.BranchId,G(),G(),s.OwnedNet.Id,s.Sequence,s.Rig.Revision,s.RulesVersion)));Commit(store.Execute(Supply(store.State,FloatSupplyProduct.Food)));Commit(store.Execute(Cast(store.State)));Commit(store.Execute(Fish(store.State)));}
            var before=FloatJournalArchive.Inspect(source);string candidate=P("schema3");Upgrade(source,P("backup"),candidate);var after=FloatJournalArchive.Inspect(candidate);Preserved(before.State,after.State);
            Need(before.State.UpgradeOrigin!=null&&after.State.UpgradeOrigin!=null&&after.State.EquipmentUpgradeOrigin!=null&&after.State.FoodCount==1&&after.State.Creel.Count==1,"Rich fixture or origin chain absent.");
        });
        Test("zero resources remain zero without starter gifts or refill",()=>{
            string source=Source(New(wallet:0,stamina:0,bait:0)),candidate=P("candidate");var before=FloatJournalArchive.Inspect(source);Upgrade(source,P("backup"),candidate);Preserved(before.State,FloatJournalArchive.Inspect(candidate).State);
        });
        Test("same source mapping and offers produce identical candidate bytes at independent paths",()=>{
            string source=Source(),a=P("candidate"),b=P("candidate");Upgrade(source,P("backup"),a);Upgrade(source,P("backup"),b);Need(Same(Bytes(a),Bytes(b)),"Paths or time changed candidate identity.");
        });
        Test("retry only reads complete read-only backup and candidate with unchanged write times",()=>{
            string source=Source(),backup=P("backup"),candidate=P("candidate");var first=Upgrade(source,backup,candidate);byte[] a=Bytes(backup),b=Bytes(candidate);DateTime at=File.GetLastWriteTimeUtc(backup),bt=File.GetLastWriteTimeUtc(candidate);
            File.SetAttributes(backup,FileAttributes.ReadOnly);File.SetAttributes(candidate,FileAttributes.ReadOnly);
            try{var retry=Upgrade(source,backup,candidate);Need(retry.ExistingCandidateVerified&&retry.CandidateSha256==first.CandidateSha256&&Same(a,Bytes(backup))&&Same(b,Bytes(candidate))&&File.GetLastWriteTimeUtc(backup)==at&&File.GetLastWriteTimeUtc(candidate)==bt,"Retry wrote files.");}
            finally{File.SetAttributes(backup,FileAttributes.Normal);File.SetAttributes(candidate,FileAttributes.Normal);}
        });
        Test("different mapping policy or offer price refuses previous candidate without changes",()=>{
            string source=Source(),backup=P("backup"),candidate=P("candidate");Upgrade(source,backup,candidate);byte[] before=Bytes(candidate);var changed=Policy(version:2);
            Reject<InvalidDataException>(()=>Upgrade(source,backup,candidate,changed));var policy=Policy();Reject<InvalidDataException>(()=>Upgrade(source,backup,candidate,policy,Offers(policy,36)));Need(Same(before,Bytes(candidate)),"Policy mismatch rewrote candidate.");
        });
        Test("continued candidate retains later purchase and cannot be reset by migration retry",()=>{
            string source=Source(),backup=P("backup"),candidate=P("candidate");Upgrade(source,backup,candidate);using(var store=FloatGameStore.OpenExisting(candidate)){var s=store.State;Commit(store.Execute(new BuyFloatEquipment(s.ProfileId,s.BranchId,G(),G(),FloatEquipmentPurchaseQuote.Create(s,"hook-one"),G())));}
            byte[] before=Bytes(candidate);Reject<InvalidDataException>(()=>Upgrade(source,backup,candidate));Need(Same(before,Bytes(candidate)),"Continued candidate reset.");
        });
        Test("empty truncated and copied-only candidate files are retained without automatic repair",()=>{
            string source=Source();foreach(byte[] bytes in new[]{new byte[0],new byte[]{70,68,71},Bytes(source)}){string candidate=P("partial");File.WriteAllBytes(candidate,bytes);Reject<InvalidDataException>(()=>Upgrade(source,P("backup"),candidate));Need(Same(bytes,Bytes(candidate)),"Incomplete candidate repaired.");}
        });
        Test("foreign valid candidate cannot replace this source candidate",()=>{
            string source=Source(),foreign=Source(),candidate=P("foreign");Upgrade(foreign,P("backup"),candidate);byte[] before=Bytes(candidate);Reject<InvalidDataException>(()=>Upgrade(source,P("backup"),candidate));Need(Same(before,Bytes(candidate)),"Foreign candidate changed.");
        });
        Test("empty corrupt and foreign backups refuse before candidate creation",()=>{
            string source=Source();foreach(byte[] bytes in new[]{new byte[0],new byte[]{1,2,3},Bytes(Source())}){string backup=P("bad-backup"),candidate=P("candidate");File.WriteAllBytes(backup,bytes);Reject<InvalidDataException>(()=>Upgrade(source,backup,candidate));Need(Same(bytes,Bytes(backup))&&!File.Exists(candidate),"Bad backup overwritten or candidate created.");}
        });
        Test("normalized same case-insensitive and blank paths are rejected before outputs",()=>{
            string source=Source(),other=P("other");byte[] before=Bytes(source);
            foreach(string alias in new[]{source,source.ToUpperInvariant(),Path.Combine(root,".",Path.GetFileName(source))}){Reject<ArgumentException>(()=>Upgrade(source,alias,other));Reject<ArgumentException>(()=>Upgrade(source,other,alias));}
            Reject<ArgumentException>(()=>Upgrade(source,other,other.ToUpperInvariant()));Reject<ArgumentException>(()=>Upgrade(source," ",other));Need(Same(before,Bytes(source))&&!File.Exists(other),"Alias preflight changed source.");
        });
        Test("all three native hardlink alias pairs reject and preserve existing bytes",()=>{
            for(int kind=0;kind<3;kind++){string source=Source(),backup=P("backup"),candidate=P("candidate");byte[] before=Bytes(source);
                if(kind==0)Link(backup,source);else if(kind==1)Link(candidate,source);else{File.WriteAllBytes(backup,before);Link(candidate,backup);}
                Reject<IOException>(()=>Upgrade(source,backup,candidate));Need(Same(before,Bytes(source)),"Hardlink source changed.");if(File.Exists(backup))Need(Same(before,Bytes(backup)),"Hardlink backup changed.");if(File.Exists(candidate))Need(Same(before,Bytes(candidate)),"Hardlink candidate changed.");}
        });
        Test("busy source refuses before outputs and existing writer remains usable",()=>{
            string source=Source(),backup=P("backup"),candidate=P("candidate");using(var store=FloatGameStore.OpenExisting(source)){Reject<IOException>(()=>Upgrade(source,backup,candidate));NoOutputs(backup,candidate);Commit(store.Execute(Cast(store.State)));Need(!store.IsFaulted,"Live writer faulted.");}
        });
        Test("read-only source stays read-only and byte-identical after candidate creation",()=>{
            string source=Source(),backup=P("backup"),candidate=P("candidate");byte[] before=Bytes(source);File.SetAttributes(source,FileAttributes.ReadOnly);
            try{Upgrade(source,backup,candidate);Need((File.GetAttributes(source)&FileAttributes.ReadOnly)!=0&&Same(before,Bytes(source))&&Same(before,Bytes(backup)),"Read-only source changed.");}finally{File.SetAttributes(source,FileAttributes.Normal);}
        });
        Test("active and full-creel pending legacy sessions reject before backup with no settlement",()=>{
            for(int pending=0;pending<2;pending++){string source=Source(),backup=P("backup"),candidate=P("candidate");using(var store=FloatGameStore.OpenExisting(source)){Commit(store.Execute(Cast(store.State)));if(pending==1){Commit(store.Execute(Fish(store.State)));Commit(store.Execute(Cast(store.State)));Commit(store.Execute(Fish(store.State)));}}
                var before=FloatJournalArchive.Inspect(source);Need(before.HasActiveSession&&(pending==0||before.HasPendingFish),"Fixture not active or pending.");byte[] bytes=Bytes(source);
                Reject<InvalidOperationException>(()=>Upgrade(source,backup,candidate));NoOutputs(backup,candidate);Need(Same(bytes,Bytes(source)),"Busy source settled.");}
        });
        Test("schema1 and already-upgraded schema3 source reject without outputs",()=>{
            string schema1=Source(New(schema:1)),schema2=Source(),schema3=P("schema3");Upgrade(schema2,P("backup"),schema3);
            foreach(string source in new[]{schema1,schema3}){string backup=P("backup"),candidate=P("candidate");byte[] before=Bytes(source);Reject<InvalidOperationException>(()=>Upgrade(source,backup,candidate));NoOutputs(backup,candidate);Need(Same(before,Bytes(source)),"Wrong schema changed.");}
        });
        Test("unknown rig mapping and mismatched complete offer catalogue refuse before output",()=>{
            string source=Source(),backup=P("backup"),candidate=P("candidate");byte[] before=Bytes(source);Reject<InvalidOperationException>(()=>Upgrade(source,backup,candidate,Policy(source:"different-rig")));NoOutputs(backup,candidate);
            var policy=Policy();Reject<ArgumentException>(()=>Upgrade(source,backup,candidate,policy,Offers(Policy(catalog:Catalog(150001)))));NoOutputs(backup,candidate);Need(Same(before,Bytes(source)),"Invalid configuration changed source.");
        });
        Test("null policies missing corrupt source and missing output directory create nothing",()=>{
            string source=Source(),backup=P("backup"),candidate=P("candidate");var policy=Policy();
            Reject<ArgumentNullException>(()=>FloatEquipmentProfileUpgrade.CreateCandidate(source,backup,candidate,null,Offers(policy)));Reject<ArgumentNullException>(()=>FloatEquipmentProfileUpgrade.CreateCandidate(source,backup,candidate,policy,null));NoOutputs(backup,candidate);
            Reject<FileNotFoundException>(()=>Upgrade(P("missing"),backup,candidate));NoOutputs(backup,candidate);string corrupt=P("corrupt");File.WriteAllBytes(corrupt,new byte[]{1});Reject<InvalidDataException>(()=>Upgrade(corrupt,backup,candidate));NoOutputs(backup,candidate);
            string absent=Path.Combine(root,G().ToString("N"));Reject<DirectoryNotFoundException>(()=>Upgrade(source,Path.Combine(absent,"backup.fltj"),candidate));Need(!Directory.Exists(absent)&&!File.Exists(candidate),"Missing directory created.");
        });
        Console.WriteLine("SUMMARY "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
}
