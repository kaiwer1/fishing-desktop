using System;
using System.IO;
using FishingDesktop.GameCore;

static class FloatUnhookTransactionTests
{
    static int passed,failed,nextId,nextFile;static string root;
    static Guid G(){return new Guid(++nextId,0,0,new byte[]{0,0,0,0,0,0,0,9});}
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    static void Throws<T>(Action action) where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e);}}
    static string PathFor(string label){return Path.Combine(root,(++nextFile)+"-"+label+".fltj");}
    static FloatGameState New(bool commerce=true,int capacity=2,int baits=12,int stamina=100)
    {
        var rules=new FloatGameRules(1,"float-rig","worm",2,100,capacity,1000000,new[]{new FloatSpeciesRule("carp",100000,2000000,1000)});
        var rig=new FixedFloatRig(G(),1,"float-rig",G(),G(),G(),50000,G(),G(),G());
        var catalog=new FloatCommerceCatalog(1,"worm","landing-net","bread",2400,5,20,20,20,1000,1000,100);
        return commerce?FloatGameState.CreateNewWithCommerce(G(),G(),12000,stamina,G(),"worm",baits,rig,rules,catalog,G()):FloatGameState.CreateNew(G(),G(),12000,stamina,G(),"worm",baits,rig,rules);
    }
    static CastFloat Cast(FloatGameState s){return new CastFloat(s.ProfileId,s.BranchId,G(),G(),G(),s.Sequence,1,1);}
    static RecallFloat Recall(FloatGameState s){return new RecallFloat(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,1,1);}
    static RecoverFloatSession Recovery(FloatGameState s){return new RecoverFloatSession(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,1,1,FloatRecoveryReason.StartupInterrupted);}
    static HostFloatFish Fish(FloatGameState s,Guid encounter=default(Guid)){return new HostFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,G(),encounter==Guid.Empty?G():encounter,"carp",500000,s.Sequence,1,1);}
    static HostFloatUnhooked Loss(FloatGameState s,Guid encounter=default(Guid)){return new HostFloatUnhooked(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,encounter==Guid.Empty?G():encounter,s.Sequence,1,1,2);}
    static HostFloatUnhooked Copy(HostFloatUnhooked c,Guid? request=null,Guid? businessEvent=null,Guid? session=null,Guid? encounter=null,long? sequence=null)
    {return new HostFloatUnhooked(c.ProfileId,c.BranchId,request??c.RequestId,businessEvent??c.EventId,session??c.SessionId,encounter??c.EncounterId,sequence??c.ExpectedSequence,c.ExpectedRigRevision,c.ExpectedRulesVersion,c.FightModelVersion);}
    static FloatGameState Adopt(FloatGameState s,FloatCommand c){var r=FloatGameReducer.Reduce(s,c,s.Rules);Need(r.HasCandidate&&!r.IsReplay,"No new candidate: "+r.Code);return r.Candidate;}
    static void Reject(FloatGameState s,FloatCommand c,string code){var r=FloatGameReducer.Reduce(s,c,s.Rules);Need(!r.HasCandidate&&!r.IsReplay&&r.Receipt==null&&r.Code==code,"Expected "+code+", got "+r.Code);}
    static void Commit(FloatCommitResult r){Need(r.Committed&&r.Written&&!r.IsReplay&&r.Receipt!=null,"Expected commit: "+r.Code);}
    static void Denied(FloatCommitResult r,string code){Need(r.Code==code&&!r.Committed&&!r.Written&&r.Receipt==null,"Expected denial: "+code+", got "+r.Code);}
    static byte[] Bytes(string path){using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){var b=new byte[(int)f.Length];int at=0;while(at<b.Length){int n=f.Read(b,at,b.Length-at);if(n==0)throw new EndOfStreamException();at+=n;}return b;}}
    static bool Same(byte[] a,byte[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
    static void Assets(FloatGameState a,FloatGameState b)
    {
        Need(a.ProfileId==b.ProfileId&&a.BranchId==b.BranchId&&a.SchemaVersion==b.SchemaVersion&&a.Rules.Signature==b.Rules.Signature,"Profile or rules changed.");
        Need(a.WalletMinor==b.WalletMinor&&a.Stamina==b.Stamina&&a.BaitStackId==b.BaitStackId&&a.BaitCount==b.BaitCount&&a.BaitDefinitionKey==b.BaitDefinitionKey,"Loss charged or refunded resources.");
        Need(a.FoodStackId==b.FoodStackId&&a.FoodCount==b.FoodCount&&a.EquippedNetId==b.EquippedNetId&&(a.OwnedNet==null?Guid.Empty:a.OwnedNet.Id)==(b.OwnedNet==null?Guid.Empty:b.OwnedNet.Id),"Loss modified supplies.");
        Need(a.Rig.Id==b.Rig.Id&&a.Rig.Revision==b.Rig.Revision&&a.Rig.RodId==b.Rig.RodId&&a.Rig.ReelId==b.Rig.ReelId&&a.Rig.LineId==b.Rig.LineId&&a.Rig.LineLengthMm==b.Rig.LineLengthMm&&a.Rig.HookId==b.Rig.HookId&&a.Rig.BobberId==b.Rig.BobberId&&a.Rig.SinkerId==b.Rig.SinkerId,"Loss damaged or replaced rig.");
        Need(a.Creel.Count==b.Creel.Count,"Loss changed fish count.");for(int i=0;i<a.Creel.Count;i++)Need(a.Creel[i].Id==b.Creel[i].Id&&a.Creel[i].MassMg==b.Creel[i].MassMg&&a.Creel[i].SaleValueMinor==b.Creel[i].SaleValueMinor,"Loss changed fish assets.");
    }
    static FloatGameState Stocked()
    {
        var s=New();foreach(var product in new[]{FloatSupplyProduct.Net,FloatSupplyProduct.Food})s=Adopt(s,new BuyFloatSupply(s.ProfileId,s.BranchId,G(),G(),FloatPurchaseQuote.Create(s,product,1),product==FloatSupplyProduct.Net?G():Guid.Empty,s.Sequence,1,1));
        s=Adopt(s,new EquipFloatNet(s.ProfileId,s.BranchId,G(),G(),s.OwnedNet.Id,s.Sequence,1,1));s=Adopt(s,Cast(s));s=Adopt(s,Fish(s));return Adopt(s,Cast(s));
    }
    sealed class Control
    {
        internal readonly FloatGameOwner Owner;internal readonly FloatViewTicket View;internal readonly FloatDocumentTicket Document;internal FloatControlLease Lease;
        internal Control(FloatGameOwner owner){Owner=owner;View=owner.OpenView(FloatViewRole.Interaction);Document=owner.AttachDocument(View);owner.SetHostPaused(false);Lease=owner.GrantControl(View,Document);}
        internal FloatGameState State{get{return Owner.Snapshot.State;}}
        internal FloatCommitResult Send(FloatCommand command){return Owner.ExecuteUser(View,Document,Lease,command);}
    }
    static int Main(string[] args)
    {
        if(args.Length!=1||!Directory.Exists(args[0]))return 2;root=Path.GetFullPath(args[0]);
        Test("host loss is internal and requires an explicit supported model version",()=>{
            Need(!typeof(HostFloatUnhooked).IsPublic,"Loss is a public user factory.");var ctors=typeof(HostFloatUnhooked).GetConstructors(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);Need(ctors.Length==1&&!ctors[0].GetParameters()[9].IsOptional,"Model version has an implicit default.");
        });
        Test("empty session encounter and unsupported models reject before execution",()=>{
            var s=New();foreach(int model in new[]{Int32.MinValue,-1,0,1,3,Int32.MaxValue})Throws<ArgumentOutOfRangeException>(()=>new HostFloatUnhooked(s.ProfileId,s.BranchId,G(),G(),G(),G(),0,1,1,model));
            Throws<ArgumentException>(()=>new HostFloatUnhooked(s.ProfileId,s.BranchId,G(),G(),Guid.Empty,G(),0,1,1,2));Throws<ArgumentException>(()=>new HostFloatUnhooked(s.ProfileId,s.BranchId,G(),G(),G(),Guid.Empty,0,1,1,2));
        });
        Test("unhook clears only the current session while conserving all owned assets",()=>{
            var s=Stocked();var c=Loss(s);var after=Adopt(s,c);Assets(s,after);Need(after.Session==null&&after.Sequence==s.Sequence+1&&after.Creel.Count==1&&after.OwnedNet!=null&&after.FoodCount==1,"Incorrect loss terminal.");
        });
        Test("loss receipt is terminal with encounter identity and no fish reward",()=>{
            var s=Stocked();var c=Loss(s);var r=FloatGameReducer.Reduce(s,c,s.Rules);var receipt=r.Receipt;
            Need(r.Code=="unhooked-candidate"&&receipt.Outcome==FloatOutcome.Unhooked&&receipt.Terminal&&receipt.SessionId==c.SessionId&&receipt.EncounterId==c.EncounterId&&receipt.FishIds.Count==0&&receipt.RecoveryReason==null,"Loss receipt is not exact.");
            Need(receipt.WalletBefore==receipt.WalletAfter&&receipt.StaminaBefore==receipt.StaminaAfter&&receipt.BaitBefore==receipt.BaitAfter&&receipt.FoodBefore==receipt.FoodAfter&&receipt.OwnedNetBefore==receipt.OwnedNetAfter&&receipt.EquippedNetBefore==receipt.EquippedNetAfter,"Receipt changed resource facts.");
            Need(c.Kind=="host-unhooked"&&c.Detail==c.EncounterId.ToString("N")+"|2"&&receipt.Fingerprint==c.Fingerprint,"Model or encounter missing from durable identity.");
        });
        Test("pure loss candidate never publishes or mutates its source state",()=>{
            var s=New();s=Adopt(s,Cast(s));var before=s.Session;var r=FloatGameReducer.Reduce(s,Loss(s),s.Rules);Need(r.RequiresConfirmation&&s.Session==before&&s.Receipts.Count==1&&s.BaitCount==11&&s.Stamina==98&&r.Candidate.Session==null,"Unconfirmed candidate changed source.");
        });
        Test("same request replays one receipt while preserving a later active session",()=>{
            var s=New();s=Adopt(s,Cast(s));var c=Loss(s);s=Adopt(s,c);s=Adopt(s,Cast(s));var r=FloatGameReducer.Reduce(s,c,s.Rules);Need(r.IsReplay&&!r.HasCandidate&&r.Receipt.Sequence==2&&r.Receipt.Outcome==FloatOutcome.Unhooked&&s.Sequence==3&&s.Session!=null,"Replay manufactured loss or restored old state.");
        });
        Test("same request with another encounter or session conflicts",()=>{
            var s=New();s=Adopt(s,Cast(s));var c=Loss(s);s=Adopt(s,c);Reject(s,Copy(c,encounter:G()),"request-conflict");Reject(s,Copy(c,session:G()),"request-conflict");
        });
        Test("new request cannot reuse the terminal event",()=>{
            var s=New();s=Adopt(s,Cast(s));var c=Loss(s);s=Adopt(s,c);Reject(s,Copy(c,request:G(),sequence:s.Sequence),"event-already-used");
        });
        Test("fresh identities cannot terminate an old or absent session again",()=>{
            var s=New();s=Adopt(s,Cast(s));var c=Loss(s);s=Adopt(s,c);Reject(s,Copy(c,request:G(),businessEvent:G(),sequence:s.Sequence),"session-not-current");s=Adopt(s,Cast(s));Reject(s,Copy(c,request:G(),businessEvent:G(),sequence:s.Sequence),"session-not-current");
        });
        Test("escaped encounter cannot become another loss or landed fish",()=>{
            var s=New();s=Adopt(s,Cast(s));var c=Loss(s);s=Adopt(s,c);s=Adopt(s,Cast(s));Reject(s,Loss(s,c.EncounterId),"encounter-identity-used");Reject(s,Fish(s,c.EncounterId),"encounter-identity-used");
        });
        Test("previously landed encounter cannot be reused for loss",()=>{
            var s=New();s=Adopt(s,Cast(s));var f=Fish(s);s=Adopt(s,f);s=Adopt(s,Cast(s));Reject(s,Loss(s,f.EncounterId),"encounter-identity-used");
        });
        Test("foreign profile branch and stale sequence rig rules reject",()=>{
            var s=New();s=Adopt(s,Cast(s));Reject(s,new HostFloatUnhooked(G(),s.BranchId,G(),G(),s.Session.Id,G(),s.Sequence,1,1,2),"branch-mismatch");Reject(s,new HostFloatUnhooked(s.ProfileId,G(),G(),G(),s.Session.Id,G(),s.Sequence,1,1,2),"branch-mismatch");
            Reject(s,new HostFloatUnhooked(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,G(),0,1,1,2),"version-conflict");Reject(s,new HostFloatUnhooked(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,G(),s.Sequence,2,1,2),"version-conflict");Reject(s,new HostFloatUnhooked(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,G(),s.Sequence,1,2,2),"version-conflict");
        });
        Test("capacity pending fish cannot be erased by an unhook fact",()=>{
            var s=New(capacity:1);s=Adopt(s,Cast(s));s=Adopt(s,Fish(s));s=Adopt(s,Cast(s));s=Adopt(s,Fish(s));var pending=s.Session.PendingFish;Reject(s,Loss(s),"pending-fish-already-set");Need(s.Session.PendingFish==pending&&s.Creel.Count==1,"Pending catch lost.");
        });
        Test("schema1 store rejects loss without changing the legacy file",()=>{
            string path=PathFor("legacy");using(var store=FloatGameStore.CreateNew(path,New(false))){Commit(store.Execute(Cast(store.State)));byte[] before=Bytes(path);Denied(store.Execute(Loss(store.State)),"unhook-upgrade-required");Need(Same(before,Bytes(path))&&store.State.Session!=null,"Legacy loss wrote or ended session.");}
        });
        Test("same step failure wins before stale fish recall and recovery proposals",()=>{
            var s=New();s=Adopt(s,Cast(s));var loss=Loss(s);var fish=Fish(s,loss.EncounterId);var recall=Recall(s);var recovery=Recovery(s);s=Adopt(s,loss);foreach(var c in new FloatCommand[]{fish,recall,recovery})Reject(s,c,"version-conflict");Need(s.Session==null&&s.Creel.Count==0&&s.Receipts[1].Outcome==FloatOutcome.Unhooked,"Another terminal replaced failure.");
        });
        Test("same step landed fish wins before a stale unhook proposal",()=>{
            var s=New();s=Adopt(s,Cast(s));var loss=Loss(s);s=Adopt(s,Fish(s,loss.EncounterId));Reject(s,loss,"version-conflict");Need(s.Creel.Count==1&&s.Receipts[1].Outcome==FloatOutcome.Landed,"Stale loss undid landing.");
        });
        Test("type11 schema2 codec retains exact identity model and canonical bytes",()=>{
            var s=New();s=Adopt(s,Cast(s));var c=Loss(s);var bytes=FloatJournalCodec.EncodeCommand(c);var d=(HostFloatUnhooked)FloatJournalCodec.DecodeCommand(bytes);
            Need(bytes.Length==132&&bytes[4]==2&&bytes[5]==0&&bytes[6]==2&&bytes[7]==11&&d.Fingerprint==c.Fingerprint&&d.RequestId==c.RequestId&&d.EncounterId==c.EncounterId&&d.FightModelVersion==2&&Same(bytes,FloatJournalCodec.EncodeCommand(d)),"Loss codec changed fixed event.");
        });
        Test("every truncated loss payload and any trailing byte reject",()=>{
            var s=New();s=Adopt(s,Cast(s));var bytes=FloatJournalCodec.EncodeCommand(Loss(s));for(int n=0;n<bytes.Length;n++){var shortBytes=new byte[n];Array.Copy(bytes,shortBytes,n);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(shortBytes));}
            var extra=new byte[bytes.Length+1];Array.Copy(bytes,extra,bytes.Length);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(extra));
        });
        Test("schema1 type11 unknown model and empty codec identities reject",()=>{
            var s=New();s=Adopt(s,Cast(s));var c=Loss(s);Throws<InvalidDataException>(()=>FloatJournalCodec.EncodeCommand(c,1));var bytes=FloatJournalCodec.EncodeCommand(c);var bad=(byte[])bytes.Clone();bad[4]=1;Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bad));
            foreach(int model in new[]{-1,0,1,3}){bad=(byte[])bytes.Clone();Array.Copy(BitConverter.GetBytes(model),0,bad,128,4);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bad));}
            foreach(int offset in new[]{72,112}){bad=(byte[])bytes.Clone();Array.Clear(bad,offset,16);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bad));}
        });
        Test("user and observer channels cannot submit or replay host loss facts",()=>{
            string path=PathFor("authority");using(var owner=FloatGameOwner.CreateNew(path,New())){var c=new Control(owner);Commit(c.Send(Cast(c.State)));var loss=Loss(c.State);var bytes=Bytes(path);Denied(c.Send(loss),"user-command-not-allowed");var view=owner.OpenView(FloatViewRole.Observer);var doc=owner.AttachDocument(view);Denied(owner.ExecuteUser(view,doc,c.Lease,loss),"control-required");Need(Same(bytes,Bytes(path)),"Player submitted a loss.");Commit(owner.ExecuteHost(loss));Denied(c.Send(loss),"user-command-not-allowed");}
        });
        Test("host pause and menu gate new loss but permit exact recorded receipt replay",()=>{
            using(var owner=FloatGameOwner.CreateNew(PathFor("pause"),New())){var c=new Control(owner);Commit(c.Send(Cast(c.State)));var loss=Loss(c.State);owner.SetHostPaused(true);Denied(owner.ExecuteHost(loss),"simulation-paused");owner.SetHostPaused(false);owner.SetMenuOpen(true);Denied(owner.ExecuteHost(loss),"simulation-paused");owner.SetMenuOpen(false);Commit(owner.ExecuteHost(loss));owner.SetHostPaused(true);var r=owner.ExecuteHost(loss);Need(r.IsReplay&&!r.Written&&r.State.Sequence==2,"Paused historical lookup wrote.");}
        });
        Test("capacity pause remains unchanged and ordinary ended eligibility is not redefined",()=>{
            using(var owner=FloatGameOwner.CreateNew(PathFor("eligibility"),New(capacity:1))){var c=new Control(owner);Commit(c.Send(Cast(c.State)));Commit(owner.ExecuteHost(Fish(c.State)));Commit(c.Send(Cast(c.State)));Commit(owner.ExecuteHost(Fish(c.State)));Need(owner.Snapshot.PendingResult&&!owner.Snapshot.CanSimulate,"Capacity pause lost.");Denied(owner.ExecuteHost(Loss(c.State)),"simulation-paused");}
            using(var owner=FloatGameOwner.CreateNew(PathFor("ended-eligibility"),New())){var c=new Control(owner);Commit(c.Send(Cast(c.State)));Commit(owner.ExecuteHost(Loss(c.State)));Need(owner.Snapshot.CanSimulate&&!owner.Snapshot.PendingResult,"Loss changed owner-level eligibility semantics.");}
        });
        Test("confirmed file reopen preserves loss and startup adds no recovery transaction",()=>{
            string path=PathFor("restart");HostFloatUnhooked loss;FloatGameState ended;byte[] bytes;
            using(var owner=FloatGameOwner.CreateNew(path,New())){var c=new Control(owner);Commit(c.Send(Cast(c.State)));loss=Loss(c.State);Commit(owner.ExecuteHost(loss));ended=c.State;bytes=Bytes(path);}
            using(var owner=FloatGameOwner.OpenExisting(path)){Need(owner.Snapshot.State.Sequence==ended.Sequence&&owner.Snapshot.State.Session==null&&owner.Snapshot.State.Receipts[1].Outcome==FloatOutcome.Unhooked&&Same(bytes,Bytes(path)),"Startup replaced confirmed loss.");Assets(ended,owner.Snapshot.State);Need(owner.ExecuteHost(loss).IsReplay,"Reopened receipt missing.");}
        });
        Test("known prewrite failure keeps original session and exact retry writes once",()=>{
            bool armed=false;string path=PathFor("known");using(var owner=FloatGameOwner.CreateNew(path,New(),point=>{if(armed&&point==FloatSaveFaultPoint.BeforeWrite)throw new IOException("fixture");})){var c=new Control(owner);Commit(c.Send(Cast(c.State)));var loss=Loss(c.State);var paid=c.State;byte[] before=Bytes(path);armed=true;Throws<IOException>(()=>owner.ExecuteHost(loss));Need(!owner.Snapshot.Faulted&&Object.ReferenceEquals(c.State,paid)&&Same(before,Bytes(path)),"Known failure published loss.");armed=false;Commit(owner.ExecuteHost(loss));Need(owner.ExecuteHost(loss).IsReplay&&c.State.Sequence==2&&c.State.Session==null,"Exact retry wrote twice.");Assets(paid,c.State);}
        });
        Test("unknown complete-frame outcomes disable owner then reopen the same receipt",()=>{
            foreach(var point in new[]{FloatSaveFaultPoint.AfterFrame,FloatSaveFaultPoint.AfterFlush}){bool armed=false;string path=PathFor("unknown");HostFloatUnhooked loss;FloatGameState paid;
                using(var owner=FloatGameOwner.CreateNew(path,New(),at=>{if(armed&&at==point)throw new IOException("fixture");})){var c=new Control(owner);Commit(c.Send(Cast(c.State)));paid=c.State;loss=Loss(paid);armed=true;Throws<FloatSaveUncertainException>(()=>owner.ExecuteHost(loss));Need(owner.Snapshot.Faulted&&owner.Snapshot.State==null&&!owner.Snapshot.CanSimulate,"Unknown result exposed state.");Throws<InvalidOperationException>(()=>owner.ExecuteHost(loss));Denied(c.Send(Recall(paid)),"control-required");}
                byte[] saved=Bytes(path);using(var owner=FloatGameOwner.OpenExisting(path)){var r=owner.ExecuteHost(loss);Need(r.IsReplay&&!r.Written&&r.State.Sequence==2&&r.Receipt.Outcome==FloatOutcome.Unhooked&&Same(saved,Bytes(path)),"Unknown reopened as different terminal.");Assets(paid,r.State);}
            }
        });
        Test("unknown partial frame remains invalid and is never trimmed or recovered",()=>{
            bool armed=false;string path=PathFor("partial");using(var owner=FloatGameOwner.CreateNew(path,New(),point=>{if(armed&&point==FloatSaveFaultPoint.AfterPrefix)throw new IOException("fixture");})){var c=new Control(owner);Commit(c.Send(Cast(c.State)));var loss=Loss(c.State);armed=true;Throws<FloatSaveUncertainException>(()=>owner.ExecuteHost(loss));Need(owner.Snapshot.Faulted,"Partial write did not stop owner.");}
            var bytes=Bytes(path);Throws<InvalidDataException>(()=>{using(var owner=FloatGameOwner.OpenExisting(path)){} });Need(Same(bytes,Bytes(path)),"Partial journal was repaired without authority.");
        });
        Test("shutdown after loss adds no terminal and cannot refund exhausted resources",()=>{
            string path=PathFor("shutdown");using(var owner=FloatGameOwner.CreateNew(path,New(baits:1,stamina:2))){var c=new Control(owner);Commit(c.Send(Cast(c.State)));Commit(owner.ExecuteHost(Loss(c.State)));var before=Bytes(path);Need(c.State.BaitCount==0&&c.State.Stamina==0,"Exhausted cast paid incorrectly.");var r=owner.Shutdown();Need(r.Confirmed&&r.NoActiveSession&&c.State.Sequence==2&&Same(before,Bytes(path)),"Shutdown replaced loss or refunded.");}
        });
        Test("startup recovery committed first cannot later be replaced by old loss",()=>{
            string path=PathFor("recovery-first");HostFloatUnhooked loss;using(var owner=FloatGameOwner.CreateNew(path,New())){var c=new Control(owner);Commit(c.Send(Cast(c.State)));loss=Loss(c.State);}
            using(var owner=FloatGameOwner.OpenExisting(path)){var c=new Control(owner);Denied(owner.ExecuteHost(loss),"version-conflict");Need(c.State.Sequence==2&&c.State.Receipts[1].Outcome==FloatOutcome.Recovered&&c.State.Session==null,"Old loss overwrote recovery.");}
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
}
