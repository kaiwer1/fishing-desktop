using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FishingDesktop.GameCore;

static class FloatJournalArchiveTests
{
    static int passed,failed,nextId,nextFile;
    static string privateRoot;
    static Guid G(){return new Guid(++nextId,0,0,new byte[]{0,0,0,0,0,0,0,13});}
    static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Reject<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    static void Test(string name,Action body){try{body();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
    static string PathFor(string label){return Path.Combine(privateRoot,(++nextFile).ToString(CultureInfo.InvariantCulture)+"-"+label+".fltj");}
    static FloatGameState New(long wallet=100,int stamina=1000,int bait=50,int capacity=2)
    {
        var rules=new FloatGameRules(1,"fixture-float","fixture-worm",4,1000,capacity,Int64.MaxValue,new[]{new FloatSpeciesRule("carp",100000,2000000,1000)});
        var rig=new FixedFloatRig(G(),1,"fixture-float",G(),G(),G(),100000,G(),G(),G());
        return FloatGameState.CreateNew(G(),G(),wallet,stamina,G(),"fixture-worm",bait,rig,rules);
    }
    static CastFloat Cast(FloatGameState s){return new CastFloat(s.ProfileId,s.BranchId,G(),G(),G(),s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static RecallFloat Recall(FloatGameState s){return new RecallFloat(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static HostFloatFish Fish(FloatGameState s){return new HostFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,G(),G(),"carp",500000,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static string WriteNew(FloatGameState genesis=null)
    {string path=PathFor("source");using(var store=FloatGameStore.CreateNew(path,genesis??New())){}return path;}
    static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    static bool Equal(byte[] a,byte[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
    static string View(FloatGameState s)
    {
        string text=s.ProfileId+"|"+s.BranchId+"|"+s.Sequence+"|"+s.WalletMinor+"|"+s.Stamina+"|"+s.BaitCount+"|"+s.Rig.Id;
        if(s.Session!=null)text+="|session:"+s.Session.Id+":"+(s.Session.PendingFish==null?"none":s.Session.PendingFish.Id+":"+s.Session.PendingFish.EncounterId);
        foreach(var fish in s.Creel)text+="|fish:"+fish.Id+":"+fish.MassMg+":"+fish.SaleValueMinor;
        foreach(var receipt in s.Receipts)text+="|receipt:"+receipt.RequestId+":"+receipt.EventId+":"+receipt.Outcome;
        return text;
    }
    static void Metadata(FloatJournalInspection inspection,byte[] bytes,FloatGameState expected)
    {
        Need(inspection.Length==bytes.Length&&inspection.Sha256==Hash(bytes)&&inspection.Sequence==expected.Sequence&&inspection.ProfileId==expected.ProfileId&&inspection.BranchId==expected.BranchId,"Inspection metadata differs.");
        var tail=new byte[32];Array.Copy(bytes,bytes.Length-32,tail,0,32);Need(inspection.TipSha256==BitConverter.ToString(tail).Replace("-","").ToLowerInvariant(),"Tip hash differs.");
        Need(View(inspection.State)==View(expected),"Replayed inspection state differs.");
    }
    static byte[] ForgedJournal(FloatGameState genesis,params FloatCommand[] commands)
    {
        // Constructs intentional test data, not a second production parser.
        using(var output=new MemoryStream())using(var writer=new BinaryWriter(output,Encoding.UTF8)){
            writer.Write(Encoding.ASCII.GetBytes("FDGJ0001"));byte[] previous=new byte[32];long sequence=0;
            var payloads=new List<byte[]>{FloatJournalCodec.EncodeGenesis(genesis)};foreach(var command in commands)payloads.Add(FloatJournalCodec.EncodeCommand(command));
            foreach(byte[] payload in payloads){byte[] body;using(var memory=new MemoryStream())using(var frame=new BinaryWriter(memory,Encoding.UTF8)){
                    frame.Write(payload.Length);frame.Write(sequence++);frame.Write(previous);frame.Write(payload);frame.Flush();body=memory.ToArray();}
                using(var sha=SHA256.Create())previous=sha.ComputeHash(body);writer.Write(body);writer.Write(previous);}
            writer.Flush();return output.ToArray();
        }
    }
    static void RejectInvalid(string label,byte[] bytes)
    {string path=PathFor(label);File.WriteAllBytes(path,bytes);Reject<InvalidDataException>(()=>FloatJournalArchive.Inspect(path));Need(Equal(bytes,File.ReadAllBytes(path)),"Rejected source was rewritten or trimmed.");}
    static int Main(string[] args)
    {
        if(args.Length!=1||!Directory.Exists(args[0])){Console.WriteLine("FAIL existing private output root required");return 2;}
        privateRoot=Path.GetFullPath(args[0]);
        Test("closed explicit zero-resource journal is inspected without initialization or writes",()=>{
            var original=New(wallet:0,stamina:0,bait:0);string path=WriteNew(original);byte[] before=File.ReadAllBytes(path);var inspection=FloatJournalArchive.Inspect(path);
            Metadata(inspection,before,original);Need(!inspection.HasActiveSession&&!inspection.HasPendingFish&&inspection.State.Receipts.Count==0&&Equal(before,File.ReadAllBytes(path)),"Inspection gave resources or wrote a transaction.");
        });
        Test("ordinary active session is retained without owner startup recovery",()=>{
            string path=WriteNew();FloatGameState expected;using(var store=FloatGameStore.OpenExisting(path)){store.Execute(Cast(store.State));expected=store.State;}
            byte[] before=File.ReadAllBytes(path);var inspection=FloatJournalArchive.Inspect(path);Metadata(inspection,before,expected);
            Need(inspection.HasActiveSession&&!inspection.HasPendingFish&&inspection.State.Session.Id==expected.Session.Id&&inspection.Sequence==1&&Equal(before,File.ReadAllBytes(path)),"Inspection settled or refunded the open cast.");
        });
        Test("confirmed full-creel pending fish remains the same pending result",()=>{
            string path=WriteNew(New(capacity:1));FloatGameState expected;using(var store=FloatGameStore.OpenExisting(path)){
                store.Execute(Cast(store.State));store.Execute(Fish(store.State));store.Execute(Cast(store.State));store.Execute(Fish(store.State));expected=store.State;}
            byte[] before=File.ReadAllBytes(path);var inspection=FloatJournalArchive.Inspect(path);Metadata(inspection,before,expected);
            Need(inspection.HasActiveSession&&inspection.HasPendingFish&&inspection.State.Session.PendingFish.Id==expected.Session.PendingFish.Id&&Equal(before,File.ReadAllBytes(path)),"Inspection discarded or awarded pending fish.");
        });
        Test("source is opened read-only and remains locked against writers and deletion through validation",()=>{
            string path=WriteNew();int observations=0;FloatJournalArchive.Inspect(path,(point,source)=>{
                if(point!=FloatArchiveFaultPoint.SourceOpened&&point!=FloatArchiveFaultPoint.SourceCopied)return;observations++;
                Need(source.CanRead&&!source.CanWrite,"Inspection opened a writable source.");Reject<NotSupportedException>(()=>source.WriteByte(1));
                Reject<IOException>(()=>{using(var writer=new FileStream(path,FileMode.Open,FileAccess.Write,FileShare.ReadWrite)) {}});
                Reject<IOException>(()=>File.Delete(path));using(var reader=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))Need(reader.Length==source.Length,"Read sharing failed.");
            });Need(observations==2,"Capture lock was not observed across both stages.");
        });
        Test("an already active game writer blocks archive inspection and stays healthy",()=>{
            string path=WriteNew();using(var store=FloatGameStore.OpenExisting(path)){
                Reject<IOException>(()=>FloatJournalArchive.Inspect(path));Need(!store.IsFaulted&&store.State.Sequence==0,"Inspection damaged current writer.");
                Need(store.Execute(Cast(store.State)).Committed,"Writer could not continue after refused inspection.");}
            Need(FloatJournalArchive.Inspect(path).Sequence==1,"Source did not become inspectable after close.");
        });
        Test("read-only file attribute is supported without changing it",()=>{
            string path=WriteNew();byte[] before=File.ReadAllBytes(path);File.SetAttributes(path,File.GetAttributes(path)|FileAttributes.ReadOnly);
            try{Need(FloatJournalArchive.Inspect(path).Sha256==Hash(before)&&(File.GetAttributes(path)&FileAttributes.ReadOnly)!=0,"Read-only source was altered.");}
            finally{File.SetAttributes(path,FileAttributes.Normal);}
        });
        Test("source mutation after inspection cannot change captured state bytes or backup",()=>{
            string path=WriteNew();using(var store=FloatGameStore.OpenExisting(path))store.Execute(Cast(store.State));var frozen=FloatJournalArchive.Inspect(path);byte[] original=File.ReadAllBytes(path);
            using(var store=FloatGameStore.OpenExisting(path))store.Execute(Recall(store.State));Need(Hash(File.ReadAllBytes(path))!=frozen.Sha256,"Fixture source did not change.");
            string destination=PathFor("frozen-backup");var result=FloatJournalArchive.Backup(frozen,destination);var check=FloatJournalArchive.Inspect(destination);
            Need(result.Sha256==frozen.Sha256&&check.HasActiveSession&&check.Sequence==1&&Equal(original,File.ReadAllBytes(destination)),"Backup reopened or reread changed source.");
        });
        Test("snapshot keeps a private copy and does not expose raw bytes or state publicly",()=>{
            string path=WriteNew();var snapshot=FloatJournalArchive.Inspect(path);var copy=snapshot.CopyBytes();copy[0]^=255;
            Need(snapshot.Sha256==Hash(snapshot.CopyBytes()),"Mutable exported copy changed frozen bytes.");
            foreach(var field in typeof(FloatJournalInspection).GetFields(BindingFlags.Public|BindingFlags.Instance))Need(field.FieldType!=typeof(byte[])&&field.FieldType!=typeof(FloatGameState),"Public inspection leaked mutable buffer or full state.");
            Need(typeof(FloatJournalInspection).GetConstructors().Length==0,"Caller can manufacture an unvalidated public inspection.");
        });
        Test("missing source and directory input are rejected without creating a file",()=>{
            string missing=PathFor("missing");Reject<FileNotFoundException>(()=>FloatJournalArchive.Inspect(missing));Need(!File.Exists(missing),"Missing source initialized.");
            Reject<UnauthorizedAccessException>(()=>FloatJournalArchive.Inspect(privateRoot));
        });
        Test("oversized input is refused before copying and remains unchanged",()=>{
            string path=PathFor("oversized");using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write))output.SetLength(FloatGameStore.MaximumFileBytes+1);
            Reject<InvalidDataException>(()=>FloatJournalArchive.Inspect(path));Need(new FileInfo(path).Length==FloatGameStore.MaximumFileBytes+1,"Oversized source changed.");
        });
        Test("empty unknown-header and truncated files are never repaired",()=>{
            byte[] good=File.ReadAllBytes(WriteNew());RejectInvalid("empty",new byte[0]);var header=(byte[])good.Clone();header[0]^=1;RejectInvalid("header",header);
            foreach(int length in new[]{1,7,good.Length-1}){var truncated=new byte[length];Array.Copy(good,truncated,length);RejectInvalid("truncated",truncated);}
        });
        Test("tampered payload and trailing garbage remain rejected by the store's exact shared parser",()=>{
            byte[] good=File.ReadAllBytes(WriteNew());var changed=(byte[])good.Clone();changed[good.Length-33]^=1;RejectInvalid("tamper",changed);
            var extra=new byte[good.Length+1];Array.Copy(good,extra,good.Length);extra[extra.Length-1]=7;RejectInvalid("garbage",extra);
        });
        Test("valid hashes do not admit duplicate or semantically rejected transaction frames",()=>{
            var genesis=New();var cast=Cast(genesis);RejectInvalid("duplicate-command",ForgedJournal(genesis,cast,cast));
            var invalid=new RecallFloat(genesis.ProfileId,genesis.BranchId,G(),G(),G(),0,1,1);RejectInvalid("unowned-session",ForgedJournal(genesis,invalid));
        });
        Test("shared replay keeps frame count and canonical sequence validation",()=>{
            var bytes=File.ReadAllBytes(WriteNew());bytes[12]=1;RejectInvalid("wrong-genesis-sequence",bytes);
            string path=WriteNew();using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){input.Position=1;Reject<ArgumentException>(()=>FloatGameStore.ReadValidatedJournal(input));}
        });
        Test("successful backup returns only after exact bytes hash and replay match",()=>{
            string source=WriteNew();var inspection=FloatJournalArchive.Inspect(source);byte[] before=File.ReadAllBytes(source);string destination=PathFor("verified-backup");var backup=FloatJournalArchive.Backup(inspection,destination);
            Need(backup.DestinationPath==Path.GetFullPath(destination)&&backup.Length==inspection.Length&&backup.Sha256==inspection.Sha256&&backup.TipSha256==inspection.TipSha256&&backup.Sequence==inspection.Sequence&&backup.ProfileId==inspection.ProfileId&&backup.BranchId==inspection.BranchId,"Backup metadata not confirmed.");
            Need(Equal(before,File.ReadAllBytes(destination))&&Equal(before,File.ReadAllBytes(source)),"Backup changed source or bytes.");
            using(var exclusive=new FileStream(destination,FileMode.Open,FileAccess.ReadWrite,FileShare.None))Need(exclusive.Length==backup.Length,"Completed backup handle leaked.");
        });
        Test("identical existing backup is refused instead of overwritten or silently accepted",()=>{
            string source=WriteNew();var inspection=FloatJournalArchive.Inspect(source);string destination=PathFor("exists-same");File.WriteAllBytes(destination,inspection.CopyBytes());byte[] before=File.ReadAllBytes(destination);
            Reject<IOException>(()=>FloatJournalArchive.Backup(inspection,destination));Need(Equal(before,File.ReadAllBytes(destination)),"Same-content destination overwritten.");
        });
        Test("malformed or empty existing backup is refused and retained exactly",()=>{
            var inspection=FloatJournalArchive.Inspect(WriteNew());foreach(byte[] contents in new[]{new byte[0],new byte[]{1,2,3}}){string destination=PathFor("exists-invalid");File.WriteAllBytes(destination,contents);
                Reject<IOException>(()=>FloatJournalArchive.Backup(inspection,destination));Need(Equal(contents,File.ReadAllBytes(destination)),"Invalid destination was replaced.");}
        });
        Test("same-source normalized and case-insensitive destinations are rejected without touching source",()=>{
            string source=WriteNew();var snapshot=FloatJournalArchive.Inspect(source);byte[] before=File.ReadAllBytes(source);
            foreach(string destination in new[]{source,source.ToUpperInvariant(),Path.Combine(privateRoot,".",Path.GetFileName(source))})Reject<ArgumentException>(()=>FloatJournalArchive.Backup(snapshot,destination));
            Need(Equal(before,File.ReadAllBytes(source)),"Same-source backup wrote source.");
        });
        Test("backup does not require or restore the source after frozen inspection",()=>{
            string source=WriteNew();var snapshot=FloatJournalArchive.Inspect(source);File.Move(source,source+".retained");string destination=PathFor("source-moved");FloatJournalArchive.Backup(snapshot,destination);
            Need(!File.Exists(source)&&File.Exists(source+".retained")&&FloatJournalArchive.Inspect(destination).Sha256==snapshot.Sha256,"Backup recreated or depended on moved source.");
            Reject<ArgumentException>(()=>FloatJournalArchive.Backup(snapshot,source));Need(!File.Exists(source),"Missing original path was restored implicitly.");
        });
        Test("explicit missing destination directory is not created automatically",()=>{
            var snapshot=FloatJournalArchive.Inspect(WriteNew());string directory=Path.Combine(privateRoot,G().ToString("N"));
            Reject<DirectoryNotFoundException>(()=>FloatJournalArchive.Backup(snapshot,Path.Combine(directory,"backup.fltj")));Need(!Directory.Exists(directory),"Backup created a destination tree.");
        });
        Test("invalid API inputs cannot create a destination or accept raw bytes",()=>{
            string destination=PathFor("invalid-call");Reject<ArgumentNullException>(()=>FloatJournalArchive.Backup(null,destination));Need(!File.Exists(destination),"Null inspection created a destination.");
            Reject<ArgumentException>(()=>FloatJournalArchive.Inspect(" "));var snapshot=FloatJournalArchive.Inspect(WriteNew());Reject<ArgumentException>(()=>FloatJournalArchive.Backup(snapshot," "));
        });
        Test("destination remains exclusive through write flush and verification",()=>{
            string source=WriteNew();string destination=PathFor("exclusive");int observations=0;var snapshot=FloatJournalArchive.Inspect(source);
            FloatJournalArchive.Backup(snapshot,destination,(point,output)=>{
                observations++;Reject<IOException>(()=>{using(var reader=new FileStream(destination,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)) {}});
                Reject<IOException>(()=>File.Delete(destination));
                if(point==FloatArchiveFaultPoint.AfterBackupVerified)Need(output==null,"Post-verification fault seam exposed a mutation handle.");
                else Need(output.CanRead&&output.CanWrite,"Verification does not share the owned output handle.");
            });Need(observations==6,"Write and verification boundaries not covered.");
        });
        Test("injected before-write and prefix-write failures retain unconfirmed new files without altering source",()=>{
            string source=WriteNew();byte[] before=File.ReadAllBytes(source);var snapshot=FloatJournalArchive.Inspect(source);
            foreach(var target in new[]{FloatArchiveFaultPoint.BeforeBackupWrite,FloatArchiveFaultPoint.AfterBackupPrefix}){
                string destination=PathFor("partial");FloatJournalBackupUnconfirmedException caught=null;
                try{FloatJournalArchive.Backup(snapshot,destination,(point,output)=>{if(point==target)throw new IOException("Injected private-fixture write failure.");});}
                catch(FloatJournalBackupUnconfirmedException error){caught=error;}
                Need(caught!=null&&caught.DestinationPath==Path.GetFullPath(destination)&&File.Exists(destination),"Failure hid or deleted unconfirmed destination.");
                long length=new FileInfo(destination).Length;Need(length==(target==FloatArchiveFaultPoint.BeforeBackupWrite?0:64),"Partial file unexpectedly repaired.");
                byte[] partial=File.ReadAllBytes(destination);Reject<IOException>(()=>FloatJournalArchive.Backup(snapshot,destination));Need(Equal(partial,File.ReadAllBytes(destination)),"Retry overwrote unconfirmed partial file.");
            }Need(Equal(before,File.ReadAllBytes(source)),"Write failure modified source.");
        });
        Test("injected complete-write flush-boundary and post-verify failures never report confirmed backup",()=>{
            var snapshot=FloatJournalArchive.Inspect(WriteNew());foreach(var target in new[]{FloatArchiveFaultPoint.AfterBackupWrite,FloatArchiveFaultPoint.AfterBackupFlush,FloatArchiveFaultPoint.BeforeBackupVerify,FloatArchiveFaultPoint.AfterBackupVerified}){
                string destination=PathFor("complete-unconfirmed");Reject<FloatJournalBackupUnconfirmedException>(()=>FloatJournalArchive.Backup(snapshot,destination,(point,output)=>{if(point==target)throw new IOException("Injected private-fixture confirmation failure.");}));
                Need(File.Exists(destination)&&new FileInfo(destination).Length==snapshot.Length,"Unconfirmed complete file was removed.");Need(FloatJournalArchive.Inspect(destination).Sha256==snapshot.Sha256,"Explicit later inspection could not establish complete bytes.");
            }
        });
        Test("verification rejects a truncated destination and retains it for inspection",()=>{
            var snapshot=FloatJournalArchive.Inspect(WriteNew());string destination=PathFor("verify-truncate");
            Reject<FloatJournalBackupUnconfirmedException>(()=>FloatJournalArchive.Backup(snapshot,destination,(point,output)=>{if(point==FloatArchiveFaultPoint.BeforeBackupVerify)output.SetLength(output.Length-1);}));
            Need(new FileInfo(destination).Length==snapshot.Length-1,"Truncated destination was cleaned up or rewritten.");Reject<InvalidDataException>(()=>FloatJournalArchive.Inspect(destination));
        });
        Test("verification rejects corruption even though writing and flushing succeeded",()=>{
            var snapshot=FloatJournalArchive.Inspect(WriteNew());string destination=PathFor("verify-corrupt");
            Reject<FloatJournalBackupUnconfirmedException>(()=>FloatJournalArchive.Backup(snapshot,destination,(point,output)=>{if(point==FloatArchiveFaultPoint.BeforeBackupVerify){output.Position=0;output.WriteByte(0);output.Flush();}}));
            Need(File.ReadAllBytes(destination)[0]==0,"Corrupt destination was silently repaired.");
        });
        Test("a different valid journal cannot pass exact backup identity verification",()=>{
            var snapshot=FloatJournalArchive.Inspect(WriteNew());byte[] other=File.ReadAllBytes(WriteNew(New(wallet:7)));Need(other.Length==snapshot.Length,"Fixture needs equal lengths.");string destination=PathFor("verify-other-valid");
            Reject<FloatJournalBackupUnconfirmedException>(()=>FloatJournalArchive.Backup(snapshot,destination,(point,output)=>{if(point==FloatArchiveFaultPoint.BeforeBackupVerify){output.Position=0;output.Write(other,0,other.Length);output.Flush();}}));
            Need(FloatJournalArchive.Inspect(destination).Sha256==Hash(other)&&Hash(other)!=snapshot.Sha256,"Different valid journal was mistaken for the inspected snapshot.");
        });
        Test("inspection failure releases its read-only handle without modifying source",()=>{
            string source=WriteNew();byte[] before=File.ReadAllBytes(source);Reject<IOException>(()=>FloatJournalArchive.Inspect(source,(point,input)=>{if(point==FloatArchiveFaultPoint.SourceCopied)throw new IOException("Injected private-fixture capture failure.");}));
            using(var reopened=FloatGameStore.OpenExisting(source))Need(reopened.State.Sequence==0,"Inspection left an unrecoverable lock.");Need(Equal(before,File.ReadAllBytes(source)),"Failed capture modified source.");
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
}
