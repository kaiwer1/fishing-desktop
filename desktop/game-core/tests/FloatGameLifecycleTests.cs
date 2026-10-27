using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using FishingDesktop.GameCore;

static class FloatGameLifecycleTests
{
    static int passed,failed,nextId,nextFile;
    static string privateRoot;
    static Guid G(){return new Guid(++nextId,0,0,new byte[]{0,0,0,0,0,0,0,4});}
    static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Reject<T>(Action action) where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
    static string PathFor(string label){return Path.Combine(privateRoot,(++nextFile).ToString(CultureInfo.InvariantCulture)+"-"+label+".fgs");}
    static FloatGameState New(int capacity=2,bool zero=false)
    {
        var rules=new FloatGameRules(1,"fixture-float","fixture-worm",4,1000,capacity,Int64.MaxValue,new[]{new FloatSpeciesRule("carp",100000,2000000,1000)});
        var rig=new FixedFloatRig(G(),3,"fixture-float",G(),G(),G(),125000,G(),G(),G());
        return FloatGameState.CreateNew(G(),G(),zero?0:100,zero?0:1000,G(),"fixture-worm",zero?0:20,rig,rules);
    }
    static CastFloat Cast(FloatGameState s){return new CastFloat(s.ProfileId,s.BranchId,G(),G(),G(),s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static HostFloatFish Host(FloatGameState s){return new HostFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,G(),G(),"carp",500000,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static ReleaseCurrentFloatFish Release(FloatGameState s){return new ReleaseCurrentFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static SellFloatFish Sale(FloatGameState s,IEnumerable<Guid> fish){return new SellFloatFish(s.ProfileId,s.BranchId,G(),G(),fish,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static RecoverFloatSession Recover(FloatGameState s,FloatRecoveryReason reason){return new RecoverFloatSession(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,s.Rig.Revision,s.RulesVersion,reason);}
    static void Commit(FloatCommitResult r){Need(r.Committed&&r.Written&&!r.IsReplay&&r.OperationSucceeded&&r.Receipt!=null,"Expected newly saved successful transaction.");}
    static byte[] Bytes(string path)
    {
        using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){
            var bytes=new byte[checked((int)input.Length)];int at=0;while(at<bytes.Length){int n=input.Read(bytes,at,bytes.Length-at);if(n==0)throw new EndOfStreamException();at+=n;}return bytes;
        }
    }
    static bool Equal(byte[] a,byte[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
    static string Assets(FloatGameState s)
    {
        string value=s.ProfileId+"|"+s.BranchId+"|"+s.WalletMinor+"|"+s.Stamina+"|"+s.BaitCount+"|"+s.BaitStackId+"|"+s.Rig.Id+"|"+s.Rig.Revision+"|"+s.Rig.RodId+"|"+s.Rig.ReelId+"|"+s.Rig.LineId+"|"+s.Rig.LineLengthMm+"|"+s.Rig.HookId+"|"+s.Rig.BobberId+"|"+s.Rig.SinkerId;
        foreach(var fish in s.Creel)value+="|"+fish.Id+":"+fish.MassMg+":"+fish.SaleValueMinor;return value;
    }
    static void LandOne(FloatGameStore store){Commit(store.Execute(Cast(store.State)));Commit(store.Execute(Host(store.State)));}
    static void Idle(FloatLifecycleResult r){Need(r.Confirmed&&r.NoActiveSession&&!r.PendingResultPreserved&&r.Code=="no-active-session"&&r.Transaction==null,"Expected explicit idle result without transaction.");}
    static void Recovered(FloatLifecycleResult r,FloatRecoveryReason reason)
    {
        Need(r.Confirmed&&!r.NoActiveSession&&!r.PendingResultPreserved&&r.Code=="recovered-candidate"&&r.Transaction!=null,"Lifecycle did not confirm recovery.");
        Commit(r.Transaction);var receipt=r.Transaction.Receipt;
        Need(receipt.Outcome==FloatOutcome.Recovered&&receipt.RecoveryReason==reason&&receipt.Terminal&&receipt.FishIds.Count==0&&receipt.EncounterId==Guid.Empty,"Recovery invented fish or lost reason/terminal fact.");
    }
    static int Main(string[] args)
    {
        if(args.Length!=1||!Directory.Exists(args[0])){Console.WriteLine("FAIL existing private output root required");return 2;}
        privateRoot=Path.GetFullPath(args[0]);
        Test("explicit startup recovery survives two reopens and never refunds a cast",()=>{
            string path=PathFor("startup");string assets;long activeLength;
            using(var store=FloatGameStore.CreateNew(path,New())){Commit(store.Execute(Cast(store.State)));assets=Assets(store.State);activeLength=Bytes(path).Length;}
            Need(Bytes(path).Length==activeLength,"Dispose secretly appended recovery.");
            using(var store=FloatGameStore.OpenExisting(path)){
                Need(store.State.Session!=null&&store.State.Sequence==1,"Opening implicitly settled session.");Recovered(FloatGameLifecycle.Startup(store),FloatRecoveryReason.StartupInterrupted);
                Need(store.State.Session==null&&store.State.Sequence==2&&Assets(store.State)==assets&&store.State.Creel.Count==0,"Startup changed consumed assets or created fish.");
            }
            var bytes=Bytes(path);using(var store=FloatGameStore.OpenExisting(path)){Idle(FloatGameLifecycle.Startup(store));Idle(FloatGameLifecycle.OwnerShutdown(store));Need(store.State.Sequence==2&&Assets(store.State)==assets&&Equal(bytes,Bytes(path)),"Repeated lifecycle appended or refunded.");}
        });
        Test("idle zero-resource profile remains explicit with no lifecycle write or gifts",()=>{
            string path=PathFor("idle");using(var store=FloatGameStore.CreateNew(path,New(zero:true))){var bytes=Bytes(path);var state=store.State;Idle(FloatGameLifecycle.Startup(store));Idle(FloatGameLifecycle.OwnerShutdown(store));Need(Object.ReferenceEquals(state,store.State)&&store.State.WalletMinor==0&&store.State.Stamina==0&&store.State.BaitCount==0&&Equal(bytes,Bytes(path)),"Idle path created resources or wrote.");}
        });
        Test("normal owner shutdown saves one terminal fact with unchanged assets",()=>{
            string path=PathFor("shutdown");string assets;
            using(var store=FloatGameStore.CreateNew(path,New())){Commit(store.Execute(Cast(store.State)));assets=Assets(store.State);Recovered(FloatGameLifecycle.OwnerShutdown(store),FloatRecoveryReason.OwnerShutdown);var bytes=Bytes(path);Idle(FloatGameLifecycle.OwnerShutdown(store));Need(Assets(store.State)==assets&&Equal(bytes,Bytes(path)),"Repeated shutdown mutated assets.");}
            using(var store=FloatGameStore.OpenExisting(path))Need(store.State.Sequence==2&&store.State.Session==null&&Assets(store.State)==assets&&store.State.Receipts[1].RecoveryReason==FloatRecoveryReason.OwnerShutdown,"Shutdown fact did not replay.");
        });
        Test("confirmed full-creel fish survives startup shutdown and later explicit release",()=>{
            string path=PathFor("pending");Guid oldFish,pendingFish,session;string assets;
            using(var store=FloatGameStore.CreateNew(path,New(capacity:1))){LandOne(store);oldFish=store.State.Creel[0].Id;Commit(store.Execute(Cast(store.State)));var reply=store.Execute(Host(store.State));Need(reply.Committed&&reply.Written&&!reply.OperationSucceeded,"Pending outcome was not saved.");pendingFish=store.State.Session.PendingFish.Id;session=store.State.Session.Id;assets=Assets(store.State);}
            using(var store=FloatGameStore.OpenExisting(path)){
                var bytes=Bytes(path);foreach(var r in new[]{FloatGameLifecycle.Startup(store),FloatGameLifecycle.OwnerShutdown(store)})Need(r.Confirmed&&!r.NoActiveSession&&r.PendingResultPreserved&&r.Code=="pending-result-preserved"&&r.Transaction==null,"Saved pending fish was treated as unfinished simulation.");
                Need(store.State.Sequence==4&&store.State.Session.Id==session&&store.State.Session.PendingFish.Id==pendingFish&&Assets(store.State)==assets&&Equal(bytes,Bytes(path)),"Lifecycle lost saved pending or old fish.");
                Commit(store.Execute(Release(store.State)));Need(store.State.Session==null&&store.State.Creel.Count==1&&store.State.Creel[0].Id==oldFish&&Assets(store.State)==assets,"Explicit release altered old creel or refunded resources.");
            }
            using(var store=FloatGameStore.OpenExisting(path)){Idle(FloatGameLifecycle.Startup(store));Need(store.State.Sequence==5&&store.State.Creel[0].Id==oldFish,"Release was not terminal on replay.");}
        });
        Test("direct recovery cannot bypass explicit release of a confirmed pending fish",()=>{
            string path=PathFor("pending-guard");using(var store=FloatGameStore.CreateNew(path,New(capacity:1))){LandOne(store);Commit(store.Execute(Cast(store.State)));store.Execute(Host(store.State));var state=store.State;var bytes=Bytes(path);var result=store.Execute(Recover(state,FloatRecoveryReason.StartupInterrupted));Need(!result.Committed&&!result.Written&&result.Code=="pending-result-requires-release"&&Object.ReferenceEquals(state,store.State)&&Equal(bytes,Bytes(path)),"Recover discarded a known result.");}
        });
        Test("landed and sold sessions need no startup or shutdown transaction",()=>{
            string path=PathFor("settled");using(var store=FloatGameStore.CreateNew(path,New())){LandOne(store);var bytes=Bytes(path);Idle(FloatGameLifecycle.Startup(store));Idle(FloatGameLifecycle.OwnerShutdown(store));Need(store.State.Creel.Count==1&&Equal(bytes,Bytes(path)),"Landed fish was altered.");Commit(store.Execute(Sale(store.State,new[]{store.State.Creel[0].Id})));bytes=Bytes(path);Idle(FloatGameLifecycle.Startup(store));Need(store.State.WalletMinor==600&&store.State.Sequence==3&&Equal(bytes,Bytes(path)),"Sold session was recovered or paid twice.");}
        });
        Test("recovery for another session rejects without writing or replacing current state",()=>{
            string path=PathFor("wrong-session");using(var store=FloatGameStore.CreateNew(path,New())){Commit(store.Execute(Cast(store.State)));var s=store.State;var bytes=Bytes(path);var command=new RecoverFloatSession(s.ProfileId,s.BranchId,G(),G(),G(),s.Sequence,s.Rig.Revision,s.RulesVersion,FloatRecoveryReason.StartupInterrupted);var result=store.Execute(command);Need(result.Code=="session-not-current"&&!result.Committed&&!result.Written&&Object.ReferenceEquals(s,store.State)&&Equal(bytes,Bytes(path)),"Wrong session was recovered.");}
        });
        Test("recovery requires the exact sequence rig revision and rules version",()=>{
            string path=PathFor("versions");using(var store=FloatGameStore.CreateNew(path,New())){Commit(store.Execute(Cast(store.State)));var s=store.State;var bytes=Bytes(path);for(int i=0;i<3;i++){var command=new RecoverFloatSession(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence+(i==0?1:0),s.Rig.Revision+(i==1?1:0),s.RulesVersion+(i==2?1:0),FloatRecoveryReason.OwnerShutdown);var result=store.Execute(command);Need(result.Code=="version-conflict"&&!result.Committed&&!result.Written&&Object.ReferenceEquals(s,store.State)&&Equal(bytes,Bytes(path)),"Stale recovery changed current state.");}}
        });
        Test("undefined reasons reject at command and lifecycle boundaries without a write",()=>{
            string path=PathFor("reason");using(var store=FloatGameStore.CreateNew(path,New())){Commit(store.Execute(Cast(store.State)));var s=store.State;var bytes=Bytes(path);foreach(var reason in new[]{(FloatRecoveryReason)0,(FloatRecoveryReason)3,(FloatRecoveryReason)256}){Reject<ArgumentOutOfRangeException>(()=>Recover(s,reason));Reject<ArgumentOutOfRangeException>(()=>FloatGameLifecycle.ResolveCurrent(store,reason));}Need(Object.ReferenceEquals(s,store.State)&&Equal(bytes,Bytes(path)),"Invalid reason changed active state.");}
        });
        Test("old recovery receipt replays across restart without terminating a newer cast",()=>{
            string path=PathFor("receipt");RecoverFloatSession recovery;Guid newer;
            using(var store=FloatGameStore.CreateNew(path,New())){Commit(store.Execute(Cast(store.State)));recovery=Recover(store.State,FloatRecoveryReason.StartupInterrupted);Commit(store.Execute(recovery));var cast=Cast(store.State);newer=cast.SessionId;Commit(store.Execute(cast));}
            using(var store=FloatGameStore.OpenExisting(path)){var bytes=Bytes(path);var reply=store.Execute(recovery);Need(reply.Committed&&!reply.Written&&reply.IsReplay&&reply.Receipt.Sequence==2&&reply.Receipt.Outcome==FloatOutcome.Recovered&&reply.State.Sequence==3&&reply.State.Session.Id==newer&&Equal(bytes,Bytes(path)),"Old recovery replay settled a newer session or appended.");}
        });
        Test("reason belongs to the request fingerprint and cannot be changed on replay",()=>{
            string path=PathFor("reason-fingerprint");using(var store=FloatGameStore.CreateNew(path,New())){Commit(store.Execute(Cast(store.State)));var original=Recover(store.State,FloatRecoveryReason.StartupInterrupted);Commit(store.Execute(original));var changed=new RecoverFloatSession(original.ProfileId,original.BranchId,original.RequestId,original.EventId,original.SessionId,original.ExpectedSequence,original.ExpectedRigRevision,original.ExpectedRulesVersion,FloatRecoveryReason.OwnerShutdown);var bytes=Bytes(path);var reply=store.Execute(changed);Need(!reply.Committed&&!reply.Written&&reply.Code=="request-conflict"&&Equal(bytes,Bytes(path)),"Changed recovery reason reused a receipt.");}
        });
        Test("schema one type six round-trips both reasons and rejects malformed reason payloads",()=>{
            var s=New();s=FloatGameReducer.Reduce(s,Cast(s),s.Rules).Candidate;
            foreach(var reason in new[]{FloatRecoveryReason.StartupInterrupted,FloatRecoveryReason.OwnerShutdown}){var command=Recover(s,reason);var bytes=FloatJournalCodec.EncodeCommand(command);var decoded=FloatJournalCodec.DecodeCommand(bytes) as RecoverFloatSession;Need(bytes.Length==113&&bytes[7]==6&&decoded!=null&&decoded.Reason==reason&&decoded.Fingerprint==command.Fingerprint,"Typed recovery facts changed in codec.");var result=FloatGameReducer.Reduce(s,decoded,s.Rules);Need(result.HasCandidate&&result.Receipt.RecoveryReason==reason&&result.Candidate.Session==null,"Decoded recovery did not settle once.");foreach(byte invalid in new byte[]{0,3,255}){var bad=(byte[])bytes.Clone();bad[112]=invalid;Reject<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bad));}var cut=new byte[112];Array.Copy(bytes,cut,cut.Length);Reject<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(cut));var extra=new byte[114];Array.Copy(bytes,extra,bytes.Length);Reject<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(extra));}
        });
        Test("before-write lifecycle failure preserves active state and permits explicit retry",()=>{
            string path=PathFor("before-write");bool armed=false;Action<FloatSaveFaultPoint> hook=point=>{if(armed&&point==FloatSaveFaultPoint.BeforeWrite)throw new IOException("Scripted own-file pre-write rejection.");};
            using(var store=FloatGameStore.CreateNew(path,New(),hook)){Commit(store.Execute(Cast(store.State)));var s=store.State;var bytes=Bytes(path);armed=true;Reject<IOException>(()=>FloatGameLifecycle.Startup(store));Need(!store.IsFaulted&&Object.ReferenceEquals(s,store.State)&&Equal(bytes,Bytes(path)),"Determinate failure changed or faulted store.");armed=false;Recovered(FloatGameLifecycle.Startup(store),FloatRecoveryReason.StartupInterrupted);Need(store.State.Sequence==2&&Assets(store.State)==Assets(s),"Explicit retry refunded or settled twice.");}
        });
        Test("after-flush response loss escapes and reopen observes one recovery without retry",()=>{
            string path=PathFor("after-flush");bool armed=false;string assets;Action<FloatSaveFaultPoint> hook=point=>{if(armed&&point==FloatSaveFaultPoint.AfterFlush)throw new IOException("Scripted response loss after own-file durable flush.");};
            using(var store=FloatGameStore.CreateNew(path,New(),hook)){Commit(store.Execute(Cast(store.State)));assets=Assets(store.State);armed=true;Reject<FloatSaveUncertainException>(()=>FloatGameLifecycle.Startup(store));Need(store.IsFaulted,"Uncertain recovery did not disable writer.");Reject<InvalidOperationException>(()=>FloatGameLifecycle.Startup(store));}
            var bytes=Bytes(path);using(var store=FloatGameStore.OpenExisting(path)){Need(store.State.Sequence==2&&store.State.Session==null&&Assets(store.State)==assets&&store.State.Receipts[1].Outcome==FloatOutcome.Recovered,"Flushed recovery lost or duplicated.");Idle(FloatGameLifecycle.Startup(store));Idle(FloatGameLifecycle.OwnerShutdown(store));Need(Equal(bytes,Bytes(path)),"Lifecycle retried flushed fact with another identity.");}
        });
        Test("definite save-capacity refusal is not reported as confirmed lifecycle recovery",()=>{
            string path=PathFor("capacity");using(var store=FloatGameStore.CreateNew(path,New())){Commit(store.Execute(Cast(store.State)));var s=store.State;var bytes=Bytes(path);var field=typeof(FloatGameStore).GetField("_length",BindingFlags.Instance|BindingFlags.NonPublic);Need(field!=null,"Missing private byte-budget seam.");field.SetValue(store,FloatGameStore.MaximumFileBytes);var result=FloatGameLifecycle.Startup(store);Need(!result.Confirmed&&!result.NoActiveSession&&!result.PendingResultPreserved&&result.Code=="save-capacity"&&result.Transaction!=null&&!result.Transaction.Committed&&!result.Transaction.Written&&!store.IsFaulted&&Object.ReferenceEquals(s,store.State)&&Equal(bytes,Bytes(path)),"Capacity refusal looked confirmed or changed journal.");}
            using(var store=FloatGameStore.OpenExisting(path))Need(store.State.Sequence==1&&store.State.Session!=null,"Synthetic budget boundary changed real saved history.");
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
}
