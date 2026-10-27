using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using FishingDesktop.GameCore;

static class FloatGameStoreTests
{
    static int passed,failed,nextId,nextFile;
    static string privateRoot;
    static Guid G(){return new Guid(++nextId,0,0,new byte[]{0,0,0,0,0,0,0,2});}
    static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Reject<T>(Action action) where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
    static string PathFor(string label){return Path.Combine(privateRoot,(++nextFile).ToString(CultureInfo.InvariantCulture)+"-"+label+".fgs");}
    static FloatGameState New(long wallet=100,int stamina=1000,int bait=50,int capacity=2)
    {
        var rules=new FloatGameRules(1,"fixture-float","fixture-worm",4,1000,capacity,Int64.MaxValue,new[]{new FloatSpeciesRule("carp",100000,2000000,1000),new FloatSpeciesRule("crucian",100000,1000000,1000)});
        var rig=new FixedFloatRig(G(),1,"fixture-float",G(),G(),G(),100000,G(),G(),G());
        return FloatGameState.CreateNew(G(),G(),wallet,stamina,G(),"fixture-worm",bait,rig,rules);
    }
    static CastFloat Cast(FloatGameState s){return new CastFloat(s.ProfileId,s.BranchId,G(),G(),G(),s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static RecallFloat Recall(FloatGameState s){return new RecallFloat(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static HostFloatFish Host(FloatGameState s){return new HostFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,G(),G(),"carp",500000,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static ReleaseCurrentFloatFish Release(FloatGameState s){return new ReleaseCurrentFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static SellFloatFish Sale(FloatGameState s,IEnumerable<Guid> ids){return new SellFloatFish(s.ProfileId,s.BranchId,G(),G(),ids,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static byte[] Bytes(string path)
    {
        // Allow inspection of our own file while the store holds FileShare.Read.
        using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){
            var bytes=new byte[checked((int)input.Length)];int offset=0;while(offset<bytes.Length){int n=input.Read(bytes,offset,bytes.Length-offset);if(n==0)throw new EndOfStreamException();offset+=n;}return bytes;
        }
    }
    static bool Same(byte[] a,byte[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
    static string View(FloatGameState s)
    {
        string value=s.ProfileId+"|"+s.BranchId+"|"+s.Sequence+"|"+s.WalletMinor+"|"+s.Stamina+"|"+s.BaitCount+"|"+s.BaitStackId+"|"+s.Rig.Id+"|"+s.Rig.LineLengthMm;
        if(s.Session!=null)value+="|session:"+s.Session.Id+":"+(s.Session.PendingFish==null?"none":s.Session.PendingFish.Id.ToString());
        foreach(var fish in s.Creel)value+="|fish:"+fish.Id+":"+fish.MassMg+":"+fish.SaleValueMinor;
        foreach(var receipt in s.Receipts)value+="|receipt:"+receipt.RequestId+":"+receipt.EventId+":"+receipt.Sequence+":"+receipt.Outcome;
        return value;
    }
    static void NewCommit(FloatCommitResult r){Need(r.Committed&&r.Written&&!r.IsReplay&&r.Receipt!=null&&r.State!=null,"Expected newly confirmed file transaction.");}
    static void Replay(FloatCommitResult r){Need(r.Committed&&!r.Written&&r.IsReplay&&r.Receipt!=null&&r.State!=null,"Expected confirmed historical reply without write.");}
    static void Refused(FloatCommitResult r,string code){Need(r.Code==code&&!r.Committed&&!r.Written&&!r.IsReplay&&!r.OperationSucceeded&&r.Receipt==null,"Expected determinate rejection: "+code+", got "+r.Code);}
    static void LandOne(FloatGameStore store){NewCommit(store.Execute(Cast(store.State)));NewCommit(store.Execute(Host(store.State)));}
    static void RejectBadCopy(string label,byte[] bytes)
    {
        string path=PathFor(label);File.WriteAllBytes(path,bytes);Reject<InvalidDataException>(()=>{using(var ignored=FloatGameStore.OpenExisting(path)){}});Need(Same(bytes,Bytes(path)),"Rejected journal was modified or trimmed.");
    }
    static void CompleteFrameFault(FloatSaveFaultPoint target)
    {
        string path=PathFor("fault-"+target);bool armed=false;
        Action<FloatSaveFaultPoint> hook=point=>{if(armed&&point==target)throw new IOException("Scripted response loss after own-file write.");};
        CastFloat cast;long priorLength;
        using(var store=FloatGameStore.CreateNew(path,New(),hook)){
            cast=Cast(store.State);priorLength=Bytes(path).Length;armed=true;
            Reject<FloatSaveUncertainException>(()=>store.Execute(cast));Need(store.IsFaulted,"Post-write failure was not faulted.");
            Reject<InvalidOperationException>(()=>{var ignored=store.State;});Reject<InvalidOperationException>(()=>store.Execute(cast));
        }
        Need(Bytes(path).Length>priorLength,"No frame survived the scripted complete-frame failure.");
        using(var reopened=FloatGameStore.OpenExisting(path)){
            Need(reopened.State.Sequence==1&&reopened.State.BaitCount==49&&reopened.State.Stamina==996&&reopened.State.Session.Id==cast.SessionId,"Reopen lost or repeated the complete cast frame.");
            string before=View(reopened.State);var bytes=Bytes(path);Replay(reopened.Execute(cast));Need(View(reopened.State)==before&&Same(bytes,Bytes(path)),"Retry after response loss wrote or debited again.");
        }
    }
    static int Main(string[] args)
    {
        if(args.Length!=1||!Directory.Exists(args[0])){Console.WriteLine("FAIL existing private output root required");return 2;}
        privateRoot=Path.GetFullPath(args[0]);
        Test("explicit zero-resource genesis survives a real close and reopen without gifts",()=>{
            string path=PathFor("zero");var genesis=New(wallet:0,stamina:0,bait:0);string expected=View(genesis);
            using(var store=FloatGameStore.CreateNew(path,genesis))Need(View(store.State)==expected&&!store.IsFaulted,"Creation altered explicit zero genesis.");
            using(var store=FloatGameStore.OpenExisting(path))Need(View(store.State)==expected&&store.State.Receipts.Count==0,"Reopen initialized money bait or equipment.");
        });
        Test("cast recall land and sale reconstruct the same state from real journal frames",()=>{
            string path=PathFor("flow");string expected;
            using(var store=FloatGameStore.CreateNew(path,New())){
                NewCommit(store.Execute(Cast(store.State)));NewCommit(store.Execute(Recall(store.State)));LandOne(store);
                NewCommit(store.Execute(Sale(store.State,new[]{store.State.Creel[0].Id})));expected=View(store.State);
                Need(store.State.Sequence==5&&store.State.BaitCount==48&&store.State.Stamina==992&&store.State.WalletMinor==600&&store.State.Creel.Count==0&&store.State.Session==null,"Flow conservation failed.");
            }
            using(var reopened=FloatGameStore.OpenExisting(path))Need(View(reopened.State)==expected,"Replayed journal changed confirmed flow.");
        });
        Test("old receipt replay across restart returns latest state and does not append",()=>{
            string path=PathFor("receipt");CastFloat cast;string latest;
            using(var store=FloatGameStore.CreateNew(path,New())){cast=Cast(store.State);NewCommit(store.Execute(cast));NewCommit(store.Execute(Recall(store.State)));latest=View(store.State);}
            var bytes=Bytes(path);using(var reopened=FloatGameStore.OpenExisting(path)){
                var reply=reopened.Execute(cast);Replay(reply);Need(reply.Receipt.Sequence==1&&View(reply.State)==latest&&View(reopened.State)==latest&&Same(bytes,Bytes(path)),"Historical reply replaced newer state or appended.");
            }
        });
        Test("changed payload under an old request is rejected without altering bytes",()=>{
            string path=PathFor("request-conflict");using(var store=FloatGameStore.CreateNew(path,New())){
                var cast=Cast(store.State);NewCommit(store.Execute(cast));var s=store.State;var changed=new CastFloat(s.ProfileId,s.BranchId,cast.RequestId,G(),G(),s.Sequence,s.Rig.Revision,s.RulesVersion);
                string before=View(s);var bytes=Bytes(path);Refused(store.Execute(changed),"request-conflict");Need(View(store.State)==before&&Same(bytes,Bytes(path))&&!store.IsFaulted,"Rejected payload changed persistent state.");
            }
        });
        Test("same session under a new request cannot debit again after restart",()=>{
            string path=PathFor("session");Guid session;
            using(var store=FloatGameStore.CreateNew(path,New())){var cast=Cast(store.State);session=cast.SessionId;store.Execute(cast);store.Execute(Recall(store.State));}
            using(var reopened=FloatGameStore.OpenExisting(path)){
                var s=reopened.State;var bytes=Bytes(path);Refused(reopened.Execute(new CastFloat(s.ProfileId,s.BranchId,G(),G(),session,s.Sequence,1,1)),"session-already-used");Need(s.BaitCount==49&&s.Stamina==996&&Same(bytes,Bytes(path)),"Restart lost session deduplication.");
            }
        });
        Test("a second writer is rejected while a read-only inspection can read own file",()=>{
            string path=PathFor("writer");using(var first=FloatGameStore.CreateNew(path,New())){
                Need(Bytes(path).Length>0,"Own read inspection failed.");Reject<IOException>(()=>{using(var second=FloatGameStore.OpenExisting(path)){}});Need(!first.IsFaulted&&first.State.Sequence==0,"Second writer damaged first store.");
            }
            using(var reopened=FloatGameStore.OpenExisting(path))Need(reopened.State.Sequence==0,"Writer was not released on dispose.");
        });
        Test("CreateNew never overwrites an existing valid or invalid file",()=>{
            string valid=PathFor("existing");using(var store=FloatGameStore.CreateNew(valid,New())){}var good=Bytes(valid);
            Reject<IOException>(()=>{using(var ignored=FloatGameStore.CreateNew(valid,New())){}});Need(Same(good,Bytes(valid)),"Existing valid file was replaced.");
            string bad=PathFor("existing-bad");var raw=new byte[]{1,3,5,7};File.WriteAllBytes(bad,raw);Reject<IOException>(()=>{using(var ignored=FloatGameStore.CreateNew(bad,New())){}});Need(Same(raw,Bytes(bad)),"Existing invalid file was initialized.");
        });
        Test("OpenExisting missing or invalid files never creates a replacement genesis",()=>{
            string missing=PathFor("missing");Reject<FileNotFoundException>(()=>{using(var ignored=FloatGameStore.OpenExisting(missing)){}});Need(!File.Exists(missing),"Missing path was created.");
            RejectBadCopy("empty",new byte[0]);RejectBadCopy("random",new byte[]{10,20,30,40,50});
        });
        Test("truncated genesis and transaction frames are rejected without trimming",()=>{
            string path=PathFor("truncate-source");byte[] genesis,complete;
            using(var store=FloatGameStore.CreateNew(path,New())){genesis=Bytes(path);store.Execute(Cast(store.State));complete=Bytes(path);}
            var cutGenesis=new byte[genesis.Length-1];Array.Copy(genesis,cutGenesis,cutGenesis.Length);RejectBadCopy("cut-genesis",cutGenesis);
            var cutCommand=new byte[complete.Length-1];Array.Copy(complete,cutCommand,cutCommand.Length);RejectBadCopy("cut-command",cutCommand);
            var prefixOnly=new byte[genesis.Length+1];Array.Copy(complete,prefixOnly,prefixOnly.Length);RejectBadCopy("cut-prefix",prefixOnly);
        });
        Test("hash or header mutations are rejected and original bad bytes are preserved",()=>{
            string path=PathFor("mutate-source");byte[] bytes;using(var store=FloatGameStore.CreateNew(path,New())){store.Execute(Cast(store.State));bytes=Bytes(path);}
            var hash=(byte[])bytes.Clone();hash[hash.Length-1]^=0x80;RejectBadCopy("mutated-hash",hash);
            var header=(byte[])bytes.Clone();header[0]^=0x7f;RejectBadCopy("mutated-header",header);
        });
        Test("appended junk and copied prior frames cannot be silently ignored",()=>{
            string path=PathFor("append-source");byte[] genesis,bytes;
            using(var store=FloatGameStore.CreateNew(path,New())){genesis=Bytes(path);store.Execute(Cast(store.State));bytes=Bytes(path);}
            var junk=new byte[bytes.Length+1];Array.Copy(bytes,junk,bytes.Length);junk[junk.Length-1]=42;RejectBadCopy("trailing-junk",junk);
            var duplicate=new byte[bytes.Length+(bytes.Length-genesis.Length)];Array.Copy(bytes,duplicate,bytes.Length);Array.Copy(bytes,genesis.Length,duplicate,bytes.Length,bytes.Length-genesis.Length);RejectBadCopy("copied-frame",duplicate);
        });
        Test("full creel refusal is written and reopens with the same pending fish",()=>{
            string path=PathFor("full");Guid oldFish,pendingFish;HostFloatFish blocked;
            using(var store=FloatGameStore.CreateNew(path,New(capacity:1))){
                LandOne(store);oldFish=store.State.Creel[0].Id;store.Execute(Cast(store.State));blocked=Host(store.State);pendingFish=blocked.FishId;long before=Bytes(path).Length;
                var result=store.Execute(blocked);NewCommit(result);Need(!result.OperationSucceeded&&result.Code=="creel-capacity-exceeded"&&store.State.Session.PendingFish.Id==pendingFish&&Bytes(path).Length>before,"Capacity refusal was treated as a no-write error.");
            }
            using(var reopened=FloatGameStore.OpenExisting(path)){
                Need(reopened.State.Sequence==4&&reopened.State.Creel.Count==1&&reopened.State.Creel[0].Id==oldFish&&reopened.State.Session.PendingFish.Id==pendingFish,"Pending current fish was not recovered.");
                var bytes=Bytes(path);var reply=reopened.Execute(blocked);Replay(reply);Need(!reply.OperationSucceeded&&Same(bytes,Bytes(path)),"Capacity reply repeated host settlement.");
            }
        });
        Test("pending current fish blocks old-creel sale and releases once across reopen",()=>{
            string path=PathFor("release");ReleaseCurrentFloatFish release;Guid oldFish;
            using(var store=FloatGameStore.CreateNew(path,New(capacity:1))){
                LandOne(store);oldFish=store.State.Creel[0].Id;store.Execute(Cast(store.State));store.Execute(Host(store.State));var bytes=Bytes(path);
                Refused(store.Execute(Sale(store.State,new[]{oldFish})),"session-busy");Need(Same(bytes,Bytes(path)),"Pending state allowed sale of old fish.");release=Release(store.State);NewCommit(store.Execute(release));
            }
            using(var reopened=FloatGameStore.OpenExisting(path)){
                Replay(reopened.Execute(release));Need(reopened.State.Session==null&&reopened.State.Creel.Count==1&&reopened.State.Creel[0].Id==oldFish&&reopened.State.BaitCount==48&&reopened.State.Stamina==992&&reopened.State.WalletMinor==100,"Release repeated or refunded after restart.");
            }
        });
        Test("batch sale failure does not append or remove the valid selected fish",()=>{
            string path=PathFor("batch-reject");using(var store=FloatGameStore.CreateNew(path,New())){
                LandOne(store);LandOne(store);var s=store.State;string before=View(s);var bytes=Bytes(path);Refused(store.Execute(Sale(s,new[]{s.Creel[0].Id,G()})),"fish-not-owned");
                Need(View(store.State)==before&&Same(bytes,Bytes(path))&&!store.IsFaulted,"Partially failed sale changed the file or assets.");
            }
        });
        Test("before-write hook failure is determinate and the same intent can be retried",()=>{
            string path=PathFor("before-write");bool armed=false;Action<FloatSaveFaultPoint> hook=point=>{if(armed&&point==FloatSaveFaultPoint.BeforeWrite)throw new IOException("Scripted before-write rejection.");};
            using(var store=FloatGameStore.CreateNew(path,New(),hook)){
                var cast=Cast(store.State);string before=View(store.State);var bytes=Bytes(path);armed=true;Reject<IOException>(()=>store.Execute(cast));
                Need(!store.IsFaulted&&View(store.State)==before&&Same(bytes,Bytes(path)),"Pre-write failure became uncertain or changed state.");armed=false;NewCommit(store.Execute(cast));Need(store.State.Sequence==1&&store.State.BaitCount==49,"Determinate retry did not settle once.");
            }
        });
        Test("prefix fault blocks the live instance and reopening preserves the corrupt file",()=>{
            string path=PathFor("prefix-fault");bool armed=false;Action<FloatSaveFaultPoint> hook=point=>{if(armed&&point==FloatSaveFaultPoint.AfterPrefix)throw new IOException("Scripted partial-frame failure.");};
            using(var store=FloatGameStore.CreateNew(path,New(),hook)){
                var cast=Cast(store.State);armed=true;Reject<FloatSaveUncertainException>(()=>store.Execute(cast));Need(store.IsFaulted,"Partial write was not faulted.");Reject<InvalidOperationException>(()=>{var ignored=store.State;});Reject<InvalidOperationException>(()=>store.Execute(cast));
            }
            var bad=Bytes(path);Reject<InvalidDataException>(()=>{using(var ignored=FloatGameStore.OpenExisting(path)){}});Need(Same(bad,Bytes(path)),"Reopen trimmed or replaced a partial frame.");
        });
        Test("complete-frame response loss is uncertain but reopens without double debit",()=>CompleteFrameFault(FloatSaveFaultPoint.AfterFrame));
        Test("after-flush response loss is uncertain but history replays without double debit",()=>CompleteFrameFault(FloatSaveFaultPoint.AfterFlush));
        Test("OpenExisting hook applies to later writes and does not mutate replayed history",()=>{
            string path=PathFor("open-hook");using(var created=FloatGameStore.CreateNew(path,New())){created.Execute(Cast(created.State));}
            bool armed=false;Action<FloatSaveFaultPoint> hook=point=>{if(armed&&point==FloatSaveFaultPoint.BeforeWrite)throw new IOException("Scripted open-store pre-write failure.");};
            using(var reopened=FloatGameStore.OpenExisting(path,hook)){
                Need(reopened.State.Sequence==1,"Open hook rewrote history.");var recall=Recall(reopened.State);var bytes=Bytes(path);armed=true;Reject<IOException>(()=>reopened.Execute(recall));Need(!reopened.IsFaulted&&Same(bytes,Bytes(path))&&reopened.State.Session!=null,"Open-store before-write failure mutated state.");armed=false;NewCommit(reopened.Execute(recall));
            }
        });
        Test("capacity budget refusal writes nothing and confirmed historical queries remain usable",()=>{
            string path=PathFor("capacity-counter");CastFloat cast;
            using(var store=FloatGameStore.CreateNew(path,New())){
                cast=Cast(store.State);store.Execute(cast);store.Execute(Recall(store.State));var before=View(store.State);var bytes=Bytes(path);
                // Synthetic internal byte-budget boundary, not a real full disk.
                var field=typeof(FloatGameStore).GetField("_length",BindingFlags.Instance|BindingFlags.NonPublic);Need(field!=null&&field.FieldType==typeof(long),"Byte-budget test seam changed.");field.SetValue(store,(long)FloatGameStore.MaximumFileBytes);
                Refused(store.Execute(Cast(store.State)),"save-capacity");Need(!store.IsFaulted&&View(store.State)==before&&Same(bytes,Bytes(path)),"Capacity refusal changed file or faulted the instance.");Replay(store.Execute(cast));Need(Same(bytes,Bytes(path)),"Historical query appended at capacity.");
            }
            using(var reopened=FloatGameStore.OpenExisting(path))Need(reopened.State.Sequence==2&&reopened.State.BaitCount==49,"Synthetic budget affected actual stored history.");
        });
        Test("disposed stores reject state and execution without touching saved bytes",()=>{
            string path=PathFor("disposed");var store=FloatGameStore.CreateNew(path,New());var cast=Cast(store.State);store.Dispose();var bytes=Bytes(path);
            Reject<ObjectDisposedException>(()=>{var ignored=store.State;});Reject<ObjectDisposedException>(()=>store.Execute(cast));store.Dispose();Need(Same(bytes,Bytes(path)),"Disposed store wrote data.");
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
}
