using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FishingDesktop.GameCore;

static class FloatProfileUpgradeTests
{
    static int passed,failed,nextId,nextFile;
    static string root;
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool CreateHardLink(string newFileName,string existingFileName,IntPtr securityAttributes);
    static Guid G(){return new Guid(++nextId,0,0,new byte[]{0,0,0,0,0,0,0,31});}
    static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Reject<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    static void Test(string name,Action body){try{body();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error);}}
    static string P(string label){return Path.Combine(root,(++nextFile).ToString(CultureInfo.InvariantCulture)+"-"+label+".fltj");}
    static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    static bool Equal(byte[] a,byte[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
    static bool Prefix(byte[] prefix,byte[] value){if(value.Length<=prefix.Length)return false;for(int i=0;i<prefix.Length;i++)if(prefix[i]!=value[i])return false;return true;}
    static FloatCommerceCatalog Catalog(int maximumBait=20000,string baitKey="worm",long baitPrice=20)
    {return new FloatCommerceCatalog(1,baitKey,"landing-net","bread",2400,5,baitPrice,20,20,maximumBait,1000,100);}
    static FloatGameState New(bool commerce=false,long wallet=12000,int stamina=100,int bait=12,int capacity=2,int maximumStamina=100)
    {
        var rules=new FloatGameRules(1,"float-rig","worm",2,maximumStamina,capacity,Int64.MaxValue,new[]{new FloatSpeciesRule("carp",100000,2000000,1000)});
        var rig=new FixedFloatRig(G(),1,"float-rig",G(),G(),G(),80000,G(),G(),G());Guid profile=G(),branch=G(),stack=G();
        return commerce?FloatGameState.CreateNewWithCommerce(profile,branch,wallet,stamina,stack,"worm",bait,rig,rules,Catalog(),G()):FloatGameState.CreateNew(profile,branch,wallet,stamina,stack,"worm",bait,rig,rules);
    }
    static string Source(FloatGameState state=null){string path=P("source");using(var store=FloatGameStore.CreateNew(path,state??New())){}return path;}
    static CastFloat Cast(FloatGameState s){return new CastFloat(s.ProfileId,s.BranchId,G(),G(),G(),s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static RecallFloat Recall(FloatGameState s){return new RecallFloat(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static HostFloatFish Fish(FloatGameState s){return new HostFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,G(),G(),"carp",500000,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static void Commit(FloatCommitResult r){Need(r.Committed&&r.Written&&!r.IsReplay,"Fixture transaction failed: "+r.Code);}
    static string FishView(FloatFish f){return f==null?"none":f.Id+"|"+f.SessionId+"|"+f.EncounterId+"|"+f.SpeciesKey+"|"+f.MassMg+"|"+f.SaleValueMinor+"|"+f.RulesVersion;}
    static string ReceiptView(FloatReceipt r)
    {
        string result=r.RequestId+"|"+r.EventId+"|"+r.SessionId+"|"+r.EncounterId+"|"+r.Sequence+"|"+r.RulesVersion+"|"+r.WalletBefore+"|"+r.WalletAfter+"|"+r.StaminaBefore+"|"+r.StaminaAfter+"|"+r.BaitBefore+"|"+r.BaitAfter+"|"+r.Outcome+"|"+r.RecoveryReason+"|"+r.Fingerprint;
        foreach(var id in r.FishIds)result+="|"+id;return result;
    }
    static void LegacyPreserved(FloatGameState before,FloatGameState after)
    {
        Need(after.ProfileId==before.ProfileId&&after.RulesVersion==before.RulesVersion&&after.Rules.Signature==before.Rules.Signature&&after.WalletMinor==before.WalletMinor&&after.Stamina==before.Stamina&&after.BaitCount==before.BaitCount&&after.BaitStackId==before.BaitStackId&&after.BaitDefinitionKey==before.BaitDefinitionKey,"Migration changed legacy resources or rules.");
        var a=before.Rig;var b=after.Rig;Need(a.Id==b.Id&&a.Revision==b.Revision&&a.DefinitionKey==b.DefinitionKey&&a.RodId==b.RodId&&a.ReelId==b.ReelId&&a.LineId==b.LineId&&a.LineLengthMm==b.LineLengthMm&&a.HookId==b.HookId&&a.BobberId==b.BobberId&&a.SinkerId==b.SinkerId,"Migration changed rig assets.");
        Need(after.Creel.Count==before.Creel.Count&&after.Receipts.Count==before.Receipts.Count+1,"Migration changed old fish or receipt count.");
        for(int i=0;i<before.Creel.Count;i++)Need(FishView(before.Creel[i])==FishView(after.Creel[i]),"Migration changed an old fish.");
        for(int i=0;i<before.Receipts.Count;i++)Need(ReceiptView(before.Receipts[i])==ReceiptView(after.Receipts[i]),"Migration changed a historical receipt.");
        if(before.Session==null)Need(after.Session==null,"Migration opened a session.");
        else Need(after.Session!=null&&after.Session.Id==before.Session.Id&&after.Session.ReleaseEventId==before.Session.ReleaseEventId&&after.Session.RigRevision==before.Session.RigRevision&&after.Session.RulesVersion==before.Session.RulesVersion&&FishView(after.Session.PendingFish)==FishView(before.Session.PendingFish),"Migration settled or replaced the active/pending session.");
        Need(after.SchemaVersion==2&&after.BranchId!=before.BranchId&&after.FoodStackId!=Guid.Empty&&after.FoodCount==0&&after.OwnedNet==null&&after.EquippedNetId==Guid.Empty,"Migration gifted supplies or failed to branch.");
        Need(after.Receipts[after.Receipts.Count-1].Outcome==FloatOutcome.ProfileUpgraded,"Upgrade receipt missing.");
    }
    static void NoOutputs(string backup,string candidate){Need(!File.Exists(backup)&&!File.Exists(candidate),"Rejected preflight created upgrade output.");}
    static void Link(string alias,string existing){if(!CreateHardLink(alias,existing,IntPtr.Zero))throw new IOException("Private hardlink fixture creation failed: "+Marshal.GetLastWin32Error());}
    static string FullJournal(int transactions)
    {
        // Build complete, valid schema-1 transactions in memory without doing
        // a disk flush for every fixture frame. Production replay validates all.
        var s=New(stamina:10000,bait:5000,maximumStamina:10000);string path=P("transaction-cap");
        using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var writer=new BinaryWriter(output,Encoding.UTF8)){
            writer.Write(Encoding.ASCII.GetBytes("FDGJ0001"));byte[] previous=new byte[32];Guid session=Guid.Empty;
            for(int index=0;index<=transactions;index++){
                byte[] payload;
                if(index==0)payload=FloatJournalCodec.EncodeGenesis(s);
                else if((index&1)==1){session=G();payload=FloatJournalCodec.EncodeCommand(new CastFloat(s.ProfileId,s.BranchId,G(),G(),session,index-1,1,1),1);}
                else payload=FloatJournalCodec.EncodeCommand(new RecallFloat(s.ProfileId,s.BranchId,G(),G(),session,index-1,1,1),1);
                byte[] body;using(var memory=new MemoryStream())using(var frame=new BinaryWriter(memory,Encoding.UTF8)){
                    frame.Write(payload.Length);frame.Write((long)index);frame.Write(previous);frame.Write(payload);frame.Flush();body=memory.ToArray();}
                using(var sha=SHA256.Create())previous=sha.ComputeHash(body);writer.Write(body);writer.Write(previous);
            }
        }return path;
    }
    static int Main(string[] args)
    {
        if(args.Length!=1||!Directory.Exists(args[0])){Console.WriteLine("FAIL existing private output directory required");return 2;}root=Path.GetFullPath(args[0]);
        Test("source stays byte-identical while backup is exact and candidate appends one upgrade",()=>{
            string source=Source(),backup=P("backup"),candidate=P("candidate");byte[] before=File.ReadAllBytes(source);var old=FloatJournalArchive.Inspect(source);
            var result=FloatProfileUpgrade.CreateCandidate(source,backup,candidate,Catalog());var current=FloatJournalArchive.Inspect(candidate);
            Need(Equal(before,File.ReadAllBytes(source))&&Equal(before,File.ReadAllBytes(backup))&&Prefix(before,File.ReadAllBytes(candidate)),"Upgrade overwrote source or history.");
            Need(!result.ExistingCandidateVerified&&result.SourceSha256==Hash(before)&&result.CandidateSha256==current.Sha256&&result.SourceSequence==old.Sequence&&result.CandidateSequence==old.Sequence+1&&result.SourceBranchId==old.BranchId&&result.CandidateBranchId==current.BranchId&&result.ProfileId==old.ProfileId,"Result metadata is not tied to validated bytes.");
            Need(result.BackupPath==Path.GetFullPath(backup)&&result.CandidatePath==Path.GetFullPath(candidate),"Result paths are not explicit.");LegacyPreserved(old.State,current.State);
        });
        Test("zero resources remain zero with no automatic net food or stamina",()=>{
            string source=Source(New(wallet:0,stamina:0,bait:0)),backup=P("backup"),candidate=P("candidate");var before=FloatJournalArchive.Inspect(source);
            FloatProfileUpgrade.CreateCandidate(source,backup,candidate,Catalog());LegacyPreserved(before.State,FloatJournalArchive.Inspect(candidate).State);
        });
        Test("existing fish value and historical sale receipts survive without repricing",()=>{
            string source=Source();using(var store=FloatGameStore.OpenExisting(source)){
                Commit(store.Execute(Cast(store.State)));Commit(store.Execute(Fish(store.State)));var s=store.State;
                Commit(store.Execute(new SellFloatFish(s.ProfileId,s.BranchId,G(),G(),new[]{s.Creel[0].Id},s.Sequence,1,1)));Commit(store.Execute(Cast(store.State)));Commit(store.Execute(Fish(store.State)));}
            var before=FloatJournalArchive.Inspect(source);string candidate=P("candidate");FloatProfileUpgrade.CreateCandidate(source,P("backup"),candidate,Catalog(baitPrice:999));LegacyPreserved(before.State,FloatJournalArchive.Inspect(candidate).State);
        });
        Test("active cast and confirmed consumption survive without startup recovery",()=>{
            string source=Source();using(var store=FloatGameStore.OpenExisting(source))Commit(store.Execute(Cast(store.State)));
            var before=FloatJournalArchive.Inspect(source);string candidate=P("candidate");FloatProfileUpgrade.CreateCandidate(source,P("backup"),candidate,Catalog());var after=FloatJournalArchive.Inspect(candidate);
            LegacyPreserved(before.State,after.State);Need(after.HasActiveSession&&!after.HasPendingFish&&after.Sequence==2,"Active cast was silently settled.");
        });
        Test("full-creel pending fish retains identity and original confirmed value",()=>{
            string source=Source(New(capacity:1));using(var store=FloatGameStore.OpenExisting(source)){
                Commit(store.Execute(Cast(store.State)));Commit(store.Execute(Fish(store.State)));Commit(store.Execute(Cast(store.State)));Commit(store.Execute(Fish(store.State)));}
            var before=FloatJournalArchive.Inspect(source);string candidate=P("candidate");FloatProfileUpgrade.CreateCandidate(source,P("backup"),candidate,Catalog());var after=FloatJournalArchive.Inspect(candidate);
            LegacyPreserved(before.State,after.State);Need(after.HasPendingFish&&after.Sequence==5,"Pending result was lost or rewarded again.");
        });
        Test("upgrade origin binds exact source bytes tip branch length and sequence",()=>{
            string source=Source(),candidate=P("candidate");var original=FloatJournalArchive.Inspect(source);FloatProfileUpgrade.CreateCandidate(source,P("backup"),candidate,Catalog());var origin=FloatJournalArchive.Inspect(candidate).State.UpgradeOrigin;
            Need(origin.SourceSha256==original.Sha256&&origin.SourceTipSha256==original.TipSha256&&origin.SourceBranchId==original.BranchId&&origin.SourceLength==original.Length&&origin.SourceSequence==original.Sequence,"Source evidence changed.");
        });
        Test("retry of complete read-only candidate and backup only verifies bytes",()=>{
            string source=Source(),backup=P("backup"),candidate=P("candidate");var first=FloatProfileUpgrade.CreateCandidate(source,backup,candidate,Catalog());byte[] before=File.ReadAllBytes(candidate);DateTime time=File.GetLastWriteTimeUtc(candidate);
            File.SetAttributes(candidate,FileAttributes.ReadOnly);File.SetAttributes(backup,FileAttributes.ReadOnly);
            try{var second=FloatProfileUpgrade.CreateCandidate(source,backup,candidate,Catalog());Need(second.ExistingCandidateVerified&&second.CandidateSha256==first.CandidateSha256&&second.CandidateBranchId==first.CandidateBranchId&&Equal(before,File.ReadAllBytes(candidate))&&File.GetLastWriteTimeUtc(candidate)==time,"Retry wrote or selected another branch.");}
            finally{File.SetAttributes(candidate,FileAttributes.Normal);File.SetAttributes(backup,FileAttributes.Normal);}
        });
        Test("same source and policy produce exact deterministic candidate bytes at new paths",()=>{
            string source=Source(),first=P("candidate"),second=P("candidate");FloatProfileUpgrade.CreateCandidate(source,P("backup"),first,Catalog());FloatProfileUpgrade.CreateCandidate(source,P("backup"),second,Catalog());Need(Equal(File.ReadAllBytes(first),File.ReadAllBytes(second)),"Path or time changed migration identities.");
        });
        Test("valid existing independent backup is accepted without rewriting it",()=>{
            string source=Source(),backup=P("backup"),candidate=P("candidate");File.WriteAllBytes(backup,File.ReadAllBytes(source));DateTime time=File.GetLastWriteTimeUtc(backup);
            FloatProfileUpgrade.CreateCandidate(source,backup,candidate,Catalog());Need(File.GetLastWriteTimeUtc(backup)==time&&Equal(File.ReadAllBytes(source),File.ReadAllBytes(backup)),"Valid backup was rewritten.");
        });
        Test("partial candidate is retained exactly and never repaired on retry",()=>{
            foreach(byte[] partial in new[]{new byte[0],new byte[]{70,68,71}}){string source=Source(),backup=P("backup"),candidate=P("partial");File.WriteAllBytes(candidate,partial);
                Reject<InvalidDataException>(()=>FloatProfileUpgrade.CreateCandidate(source,backup,candidate,Catalog()));Need(Equal(partial,File.ReadAllBytes(candidate)),"Partial candidate was replaced.");}
        });
        Test("unupgraded copied source at candidate path is not silently appended or repaired",()=>{
            string source=Source(),backup=P("backup"),candidate=P("candidate");byte[] before=File.ReadAllBytes(source);File.WriteAllBytes(candidate,before);
            Reject<InvalidDataException>(()=>FloatProfileUpgrade.CreateCandidate(source,backup,candidate,Catalog()));Need(Equal(before,File.ReadAllBytes(candidate))&&Equal(before,File.ReadAllBytes(source)),"Copied-only candidate was resumed implicitly.");
        });
        Test("continued candidate is refused and its new gameplay is retained",()=>{
            string source=Source(),backup=P("backup"),candidate=P("candidate");FloatProfileUpgrade.CreateCandidate(source,backup,candidate,Catalog());using(var store=FloatGameStore.OpenExisting(candidate))Commit(store.Execute(Cast(store.State)));
            byte[] before=File.ReadAllBytes(candidate);Reject<InvalidDataException>(()=>FloatProfileUpgrade.CreateCandidate(source,backup,candidate,Catalog()));Need(Equal(before,File.ReadAllBytes(candidate)),"Continued candidate was reset.");
        });
        Test("foreign upgraded candidate cannot substitute for this source",()=>{
            string source=Source(),foreign=Source(),candidate=P("candidate");FloatProfileUpgrade.CreateCandidate(foreign,P("backup"),candidate,Catalog());byte[] before=File.ReadAllBytes(candidate);
            Reject<InvalidDataException>(()=>FloatProfileUpgrade.CreateCandidate(source,P("backup"),candidate,Catalog()));Need(Equal(before,File.ReadAllBytes(candidate)),"Foreign candidate changed.");
        });
        Test("different commerce policy cannot reuse the old complete candidate",()=>{
            string source=Source(),backup=P("backup"),candidate=P("candidate");FloatProfileUpgrade.CreateCandidate(source,backup,candidate,Catalog());byte[] before=File.ReadAllBytes(candidate);
            Reject<InvalidDataException>(()=>FloatProfileUpgrade.CreateCandidate(source,backup,candidate,Catalog(baitPrice:30)));Need(Equal(before,File.ReadAllBytes(candidate)),"Policy mismatch rewrote candidate.");
        });
        Test("matching migration identities with a substituted food stack are refused unchanged",()=>{
            string source=Source(),backup=P("backup"),legitimate=P("legitimate"),forged=P("substituted-food");var catalog=Catalog();
            FloatProfileUpgrade.CreateCandidate(source,backup,legitimate,catalog);var expected=FloatJournalArchive.Inspect(legitimate).State;var receipt=expected.Receipts[expected.Receipts.Count-1];var origin=expected.UpgradeOrigin;
            File.WriteAllBytes(forged,File.ReadAllBytes(source));Guid substituted=G();
            using(var store=FloatGameStore.OpenExisting(forged))Commit(store.AppendUpgrade(new UpgradeFloatProfile(expected.ProfileId,origin.SourceBranchId,receipt.RequestId,receipt.EventId,
                expected.BranchId,substituted,origin.SourceSha256,origin.SourceTipSha256,origin.SourceLength,origin.SourceSequence,expected.Rig.Revision,expected.RulesVersion,catalog)));
            var valid=FloatJournalArchive.Inspect(forged);Need(valid.State.FoodStackId==substituted&&substituted!=expected.FoodStackId&&valid.BranchId==expected.BranchId,"Forged fixture was not a valid alternate food identity.");
            byte[] before=File.ReadAllBytes(forged);Reject<InvalidDataException>(()=>FloatProfileUpgrade.CreateCandidate(source,backup,forged,catalog));
            Need(Equal(before,File.ReadAllBytes(forged)),"Substituted candidate was overwritten or repaired.");
        });
        Test("changed source refuses its previous backup and does not touch old candidate",()=>{
            string source=Source(),backup=P("backup"),candidate=P("candidate");FloatProfileUpgrade.CreateCandidate(source,backup,candidate,Catalog());byte[] originalBackup=File.ReadAllBytes(backup),originalCandidate=File.ReadAllBytes(candidate);
            using(var store=FloatGameStore.OpenExisting(source))Commit(store.Execute(Cast(store.State)));byte[] current=File.ReadAllBytes(source);
            Reject<InvalidDataException>(()=>FloatProfileUpgrade.CreateCandidate(source,backup,candidate,Catalog()));Need(Equal(current,File.ReadAllBytes(source))&&Equal(originalBackup,File.ReadAllBytes(backup))&&Equal(originalCandidate,File.ReadAllBytes(candidate)),"Changed source retry rewrote prior files.");
        });
        Test("corrupt and foreign backups refuse before creating a candidate",()=>{
            string source=Source();foreach(byte[] contents in new[]{new byte[]{1,2,3},File.ReadAllBytes(Source())}){string backup=P("wrong-backup"),candidate=P("candidate");File.WriteAllBytes(backup,contents);
                Reject<InvalidDataException>(()=>FloatProfileUpgrade.CreateCandidate(source,backup,candidate,Catalog()));Need(Equal(contents,File.ReadAllBytes(backup))&&!File.Exists(candidate),"Bad backup was replaced or candidate created.");}
        });
        Test("same normalized or case-insensitive paths are rejected before writing",()=>{
            string source=Source();byte[] before=File.ReadAllBytes(source);string other=P("other");
            foreach(string alias in new[]{source,source.ToUpperInvariant(),Path.Combine(root,".",Path.GetFileName(source))}){
                Reject<ArgumentException>(()=>FloatProfileUpgrade.CreateCandidate(source,alias,other,Catalog()));Reject<ArgumentException>(()=>FloatProfileUpgrade.CreateCandidate(source,other,alias,Catalog()));}
            Reject<ArgumentException>(()=>FloatProfileUpgrade.CreateCandidate(source,other,other.ToUpperInvariant(),Catalog()));Need(Equal(before,File.ReadAllBytes(source))&&!File.Exists(other),"Same-path rejection modified files.");
        });
        Test("source-backup hardlink alias is rejected instead of claiming independent backup",()=>{
            string source=Source(),backup=P("source-alias"),candidate=P("candidate");Link(backup,source);byte[] before=File.ReadAllBytes(source);
            Reject<IOException>(()=>FloatProfileUpgrade.CreateCandidate(source,backup,candidate,Catalog()));Need(Equal(before,File.ReadAllBytes(source))&&!File.Exists(candidate),"Aliased backup was accepted or source modified.");
        });
        Test("source-candidate hardlink alias cannot write an upgrade into original",()=>{
            string source=Source(),backup=P("backup"),candidate=P("source-alias");Link(candidate,source);byte[] before=File.ReadAllBytes(source);
            Reject<IOException>(()=>FloatProfileUpgrade.CreateCandidate(source,backup,candidate,Catalog()));Need(Equal(before,File.ReadAllBytes(source))&&Equal(before,File.ReadAllBytes(candidate)),"Aliased candidate touched the original.");
            if(File.Exists(backup))Need(Equal(before,File.ReadAllBytes(backup)),"The earlier verified backup was altered on alias refusal.");
        });
        Test("backup-candidate hardlink alias is refused and never converted in place",()=>{
            string source=Source(),backup=P("backup"),candidate=P("backup-alias");byte[] before=File.ReadAllBytes(source);File.WriteAllBytes(backup,before);Link(candidate,backup);
            Reject<IOException>(()=>FloatProfileUpgrade.CreateCandidate(source,backup,candidate,Catalog()));Need(Equal(before,File.ReadAllBytes(backup))&&Equal(before,File.ReadAllBytes(source)),"Aliased backup was upgraded in place.");
        });
        Test("busy source refuses before outputs and its original writer continues",()=>{
            string source=Source(),backup=P("backup"),candidate=P("candidate");using(var store=FloatGameStore.OpenExisting(source)){
                Reject<IOException>(()=>FloatProfileUpgrade.CreateCandidate(source,backup,candidate,Catalog()));NoOutputs(backup,candidate);Commit(store.Execute(Cast(store.State)));Need(!store.IsFaulted,"Upgrade damaged live writer.");}
        });
        Test("read-only schema1 source upgrades without removing read-only attribute",()=>{
            string source=Source(),backup=P("backup"),candidate=P("candidate");byte[] before=File.ReadAllBytes(source);File.SetAttributes(source,FileAttributes.ReadOnly);
            try{FloatProfileUpgrade.CreateCandidate(source,backup,candidate,Catalog());Need((File.GetAttributes(source)&FileAttributes.ReadOnly)!=0&&Equal(before,File.ReadAllBytes(source))&&Equal(before,File.ReadAllBytes(backup)),"Read-only source changed.");}
            finally{File.SetAttributes(source,FileAttributes.Normal);}
        });
        Test("schema2 source is refused without a second upgrade or outputs",()=>{
            string source=Source(New(commerce:true)),backup=P("backup"),candidate=P("candidate");byte[] before=File.ReadAllBytes(source);
            Reject<InvalidOperationException>(()=>FloatProfileUpgrade.CreateCandidate(source,backup,candidate,Catalog()));NoOutputs(backup,candidate);Need(Equal(before,File.ReadAllBytes(source)),"Schema2 source changed.");
        });
        Test("catalogue bait identity and stock preflight refuse before backup creation",()=>{
            foreach(var catalog in new[]{Catalog(baitKey:"corn"),Catalog(maximumBait:1)}){string source=Source(),backup=P("backup"),candidate=P("candidate");byte[] before=File.ReadAllBytes(source);
                Reject<InvalidOperationException>(()=>FloatProfileUpgrade.CreateCandidate(source,backup,candidate,catalog));NoOutputs(backup,candidate);Need(Equal(before,File.ReadAllBytes(source)),"Catalog preflight modified source.");}
        });
        Test("missing corrupt and oversized sources are refused without outputs",()=>{
            string missing=P("missing"),backup=P("backup"),candidate=P("candidate");Reject<FileNotFoundException>(()=>FloatProfileUpgrade.CreateCandidate(missing,backup,candidate,Catalog()));NoOutputs(backup,candidate);
            string corrupt=P("corrupt");File.WriteAllBytes(corrupt,new byte[]{1,2,3});Reject<InvalidDataException>(()=>FloatProfileUpgrade.CreateCandidate(corrupt,backup,candidate,Catalog()));NoOutputs(backup,candidate);
            string oversized=P("oversized");using(var f=new FileStream(oversized,FileMode.CreateNew,FileAccess.Write))f.SetLength(FloatGameStore.MaximumFileBytes+1);Reject<InvalidDataException>(()=>FloatProfileUpgrade.CreateCandidate(oversized,backup,candidate,Catalog()));NoOutputs(backup,candidate);
        });
        Test("invalid arguments and missing backup directory create nothing",()=>{
            string source=Source(),backup=P("backup"),candidate=P("candidate");Reject<ArgumentNullException>(()=>FloatProfileUpgrade.CreateCandidate(source,backup,candidate,null));Reject<ArgumentException>(()=>FloatProfileUpgrade.CreateCandidate(source," ",candidate,Catalog()));NoOutputs(backup,candidate);
            string absent=Path.Combine(root,G().ToString("N"));Reject<DirectoryNotFoundException>(()=>FloatProfileUpgrade.CreateCandidate(source,Path.Combine(absent,"backup.fltj"),candidate,Catalog()));Need(!Directory.Exists(absent)&&!File.Exists(candidate),"Missing directory was implicitly created.");
        });
        Test("real maximum-transaction schema1 journal is rejected before any output",()=>{
            string source=FullJournal(FloatGameStore.MaximumTransactions),backup=P("backup"),candidate=P("candidate");byte[] before=File.ReadAllBytes(source);
            Reject<InvalidOperationException>(()=>FloatProfileUpgrade.CreateCandidate(source,backup,candidate,Catalog()));NoOutputs(backup,candidate);Need(Equal(before,File.ReadAllBytes(source)),"Transaction cap was compacted reset or upgraded.");
        });
        Console.WriteLine("SUMMARY "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
}
