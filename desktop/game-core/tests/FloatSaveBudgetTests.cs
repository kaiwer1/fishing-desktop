using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FishingDesktop.GameCore;

static class FloatSaveBudgetTests
{
    static int passed,failed,nextId,nextFile;static string root;
    static readonly Dictionary<int,string> historical=new Dictionary<int,string>();
    static Guid G(){return new Guid(++nextId,0,0,new byte[]{0,0,0,0,0,0,0,92});}
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    static void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error);}}
    static string P(string label){return Path.Combine(root,(++nextFile)+"-"+label+".fltj");}
    static byte[] Bytes(string path){using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){var bytes=new byte[(int)file.Length];int at=0;while(at<bytes.Length){int count=file.Read(bytes,at,bytes.Length-at);if(count==0)throw new EndOfStreamException();at+=count;}return bytes;}}
    static string Sha(byte[] bytes){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    static bool Same(byte[] a,byte[] b){return a.Length==b.Length&&Sha(a)==Sha(b);}
    static FloatCommerceCatalog Commerce(){return new FloatCommerceCatalog(1,"worm","net","bread",2400,5,20,20,20,10000,1000,100);}
    static FloatEquipmentCatalog Catalog(){return new FloatEquipmentCatalog(1,"worm",new[]{new FloatEquipmentDefinition("rod",FloatEquipmentKind.Rod,150000,0,1000,25000),new FloatEquipmentDefinition("reel",FloatEquipmentKind.Reel,200000),new FloatEquipmentDefinition("line",FloatEquipmentKind.Line,0,180),new FloatEquipmentDefinition("hook",FloatEquipmentKind.Hook,200),new FloatEquipmentDefinition("bobber",FloatEquipmentKind.Bobber,3000,0,2000,3000),new FloatEquipmentDefinition("sinker",FloatEquipmentKind.Sinker,2500)},new[]{new FloatEquipmentPairRule("rod","reel",true)},new[]{new FloatEquipmentSpoolRule("reel","line",180,50000)},new[]{new FloatEquipmentPairRule("hook","worm",true)},new[]{new FloatEquipmentLossRule(new string('r',96),new[]{FloatEquipmentSlot.Hook})});}
    static FloatGameState New(int schema)
    {
        var rig=new FixedFloatRig(G(),1,"float-rig",G(),G(),G(),50000,G(),G(),G());
        var rules=new FloatGameRules(1,"float-rig","worm",2,20000,1,1000000,new[]{new FloatSpeciesRule(new string('f',64),100000,2000000,120)});
        var s=FloatGameState.CreateNewWithCommerce(G(),G(),12000,20000,G(),"worm",6000,rig,rules,Commerce(),G());
        if(schema==1)return FloatGameState.CreateNew(s.ProfileId,s.BranchId,s.WalletMinor,s.Stamina,s.BaitStackId,"worm",s.BaitCount,rig,rules);
        if(schema==2)return s;
        var policy=new FloatEquipmentMigrationPolicy(1,"float-rig",Catalog(),"rod","reel","line","hook","bobber","sinker");
        return FloatGameState.CreateNewWithEquipment(s.ProfileId,s.BranchId,s.WalletMinor,s.Stamina,s.BaitStackId,"worm",s.BaitCount,policy.Map(s),rules,s.CommerceCatalog,s.FoodStackId);
    }
    static FloatCommand Cast(FloatGameState s,long sequence,Guid session)
    {return s.SchemaVersion==3?(FloatCommand)new CastFloatWithEquipment(s.ProfileId,s.BranchId,G(),G(),session,15000,sequence,s.Rig.Revision,s.RulesVersion,s.Equipment.InventoryRevision,s.Equipment.RulesVersion):new CastFloat(s.ProfileId,s.BranchId,G(),G(),session,sequence,s.Rig.Revision,s.RulesVersion);}
    static FloatCommand Cast(FloatGameState s){return Cast(s,s.Sequence,G());}
    static HostFloatFish Fish(FloatGameState s,long sequence,Guid session,Guid fish){return new HostFloatFish(s.ProfileId,s.BranchId,G(),G(),session,fish,G(),new string('f',64),500000,sequence,s.Rig.Revision,s.RulesVersion);}
    static HostFloatFish Fish(FloatGameState s){return Fish(s,s.Sequence,s.Session.Id,G());}
    static RecallFloat Recall(FloatGameState s){return new RecallFloat(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static RecoverFloatSession Recovery(FloatGameState s){return new RecoverFloatSession(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,s.Rig.Revision,s.RulesVersion,FloatRecoveryReason.StartupInterrupted);}
    static ReleaseCurrentFloatFish Release(FloatGameState s){return new ReleaseCurrentFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static SellFloatFish Sale(FloatGameState s){return new SellFloatFish(s.ProfileId,s.BranchId,G(),G(),new[]{s.Creel[0].Id},s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static void Commit(FloatCommitResult r){Need(r.Committed&&r.Written&&!r.IsReplay,"Expected commit: "+r.Code);}
    static void Refused(FloatCommitResult r,string code){Need(!r.Committed&&!r.Written&&r.Code==code,"Expected "+code+" got "+r.Code);}
    static void WriteFrame(BinaryWriter output,long sequence,byte[] payload,ref byte[] previous)
    {
        byte[] body;using(var memory=new MemoryStream()){using(var writer=new BinaryWriter(memory,Encoding.UTF8,true)){writer.Write(payload.Length);writer.Write(sequence);writer.Write(previous);writer.Write(payload);}body=memory.ToArray();}
        using(var hash=SHA256.Create())previous=hash.ComputeHash(body);output.Write(body);output.Write(previous);
    }
    static string Historical(int schema)
    {
        string existing;if(historical.TryGetValue(schema,out existing))return existing;
        string path=P("historical-schema"+schema);var s=New(schema);Guid session=G(),firstFish=G(),keptFish=G();
        // A real hash-chained old-format fixture; no reflection, direct state
        // mutation or use of the new append gate while constructing history.
        using(var output=new BinaryWriter(new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None),Encoding.UTF8)){
            output.Write(Encoding.ASCII.GetBytes("FDGJ0001"));byte[] previous=new byte[32];WriteFrame(output,0,FloatJournalCodec.EncodeGenesis(s),ref previous);
            WriteFrame(output,1,FloatJournalCodec.EncodeCommand(Cast(s,0,session),schema),ref previous);
            WriteFrame(output,2,FloatJournalCodec.EncodeCommand(Fish(s,1,session,firstFish),schema),ref previous);
            WriteFrame(output,3,FloatJournalCodec.EncodeCommand(new SellFloatFish(s.ProfileId,s.BranchId,G(),G(),new[]{firstFish},2,1,1),schema),ref previous);
            session=G();WriteFrame(output,4,FloatJournalCodec.EncodeCommand(Cast(s,3,session),schema),ref previous);
            WriteFrame(output,5,FloatJournalCodec.EncodeCommand(Fish(s,4,session,keptFish),schema),ref previous);
            for(int i=6;i<=9997;i++){
                FloatCommand command;if((i&1)==0){session=G();command=Cast(s,i-1,session);}else command=new RecallFloat(s.ProfileId,s.BranchId,G(),G(),session,i-1,1,1);
                WriteFrame(output,i,FloatJournalCodec.EncodeCommand(command,schema),ref previous);
            }
        }
        var inspection=FloatJournalArchive.Inspect(path);Need(inspection.Sequence==9997&&inspection.State.Session==null&&inspection.State.Creel.Count==1,"Historical fixture not valid full-creel idle state.");historical.Add(schema,path);return path;
    }
    static string Copy(string source){string path=P("copy");File.Copy(source,path);return path;}
    static void HistoricalAppend(string path,FloatCommand command,int schema)
    {
        byte[] bytes=Bytes(path),previous=new byte[32];Array.Copy(bytes,bytes.Length-32,previous,0,32);
        using(var output=new BinaryWriter(new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.None),Encoding.UTF8))WriteFrame(output,command.ExpectedSequence+1,FloatJournalCodec.EncodeCommand(command,schema),ref previous);
    }
    static int Main(string[] args)
    {
        if(args.Length!=1||!Directory.Exists(args[0]))return 2;root=Path.GetFullPath(args[0]);
        Test("storage budget accepts exactly three transactions and rejects two for all schemas",()=>{
            foreach(int schema in new[]{1,2,3}){var yes=new FloatSaveBudget(schema,9997,8);var no=new FloatSaveBudget(schema,9998,8);Need(yes.CanStartCast&&yes.Code==null&&yes.RequiredCastTransactions==3&&yes.RemainingTransactions==3&&!no.CanStartCast&&no.Code=="save-settlement-reserve"&&no.RemainingTransactions==2,"Transaction bound mismatch.");}
        });
        Test("byte admission is exact at 670 legacy and 694 owned bytes with one byte less rejected",()=>{
            foreach(int schema in new[]{1,2,3}){int bytes=schema==3?694:670;var yes=new FloatSaveBudget(schema,0,FloatGameStore.MaximumFileBytes-bytes);var no=new FloatSaveBudget(schema,0,FloatGameStore.MaximumFileBytes-bytes+1);Need(yes.CanStartCast&&yes.RequiredCastBytes==bytes&&yes.RemainingBytes==bytes&&!no.CanStartCast,"Byte budget mismatch.");}
        });
        Test("real typed codecs prove worst ending bounds including full-creel release and longest loss key",()=>{
            foreach(int schema in new[]{1,2,3}){var s=New(schema);var c=Cast(s);Need(FloatJournalCodec.EncodeCommand(c,schema).Length+76==(schema==3?212:188),"Cast frame bound drifted.");var fish=Fish(s,0,G(),G());var release=new ReleaseCurrentFloatFish(s.ProfileId,s.BranchId,G(),G(),G(),0,1,1);Need(FloatJournalCodec.EncodeCommand(fish,schema).Length+76==294&&FloatJournalCodec.EncodeCommand(release,schema).Length+76==188,"Largest fish and release lengths drifted.");var recovery=new RecoverFloatSession(s.ProfileId,s.BranchId,G(),G(),G(),0,1,1,FloatRecoveryReason.OwnerShutdown);Need(FloatJournalCodec.EncodeCommand(recovery,schema).Length+76==189,"Recovery bound drifted.");}
            var e=New(3);var loss=new HostFloatTerminalLoss(e.ProfileId,e.BranchId,G(),G(),G(),G(),e.Rig.LineId.Value,1,new string('r',96),20000,1000,0,1,1,1,1,1);Need(FloatJournalCodec.EncodeCommand(loss,3).Length+76==362&&362<482,"Loss bound exceeds reserved ending.");
            var unhook=new HostFloatUnhooked(e.ProfileId,e.BranchId,G(),G(),G(),G(),0,1,1,2);Need(FloatJournalCodec.EncodeCommand(unhook,3).Length+76==208,"Unhook bound drifted.");
        });
        Test("pure candidate gate retains two endings for active and one release for pending",()=>{
            var s=New(2);var active=FloatGameReducer.Reduce(s,Cast(s),s.Rules).Candidate;var landed=FloatGameReducer.Reduce(active,Fish(active),s.Rules).Candidate;active=FloatGameReducer.Reduce(landed,Cast(landed),s.Rules).Candidate;var pending=FloatGameReducer.Reduce(active,Fish(active),s.Rules).Candidate;
            Need(FloatSaveBudget.PreservesEnding(9997,8,188,active.Session)&&!FloatSaveBudget.PreservesEnding(9998,8,188,active.Session),"Active reserve count wrong.");
            Need(FloatSaveBudget.PreservesEnding(9998,8,294,pending.Session)&&!FloatSaveBudget.PreservesEnding(9999,8,294,pending.Session),"Pending reserve count wrong.");
            Need(FloatSaveBudget.PreservesEnding(9999,FloatGameStore.MaximumFileBytes-188,188,null),"Final release blocked.");
            Need(FloatSaveBudget.PreservesEnding(0,FloatGameStore.MaximumFileBytes-670,188,active.Session)&&!FloatSaveBudget.PreservesEnding(0,FloatGameStore.MaximumFileBytes-669,188,active.Session),"Active byte reserve wrong.");
            Need(FloatSaveBudget.PreservesEnding(0,FloatGameStore.MaximumFileBytes-482,294,pending.Session)&&!FloatSaveBudget.PreservesEnding(0,FloatGameStore.MaximumFileBytes-481,294,pending.Session),"Pending byte reserve wrong.");
        });
        Test("actual 9997 transaction journals complete cast full-creel result and release at 10000 for all schemas",()=>{
            foreach(int schema in new[]{1,2,3}){string path=Copy(Historical(schema));using(var store=FloatGameStore.OpenExisting(path)){Need(store.SaveBudget.CanStartCast&&store.SaveBudget.RemainingTransactions==3,"Fresh opening budget mismatch.");var before=store.State;var cast=Cast(before);Commit(store.Execute(cast));Commit(store.Execute(Fish(store.State)));Need(store.State.Session!=null&&store.State.Session.CapacityBlocked&&store.State.Sequence==9999,"Full-creel result failed.");Commit(store.Execute(Release(store.State)));Need(store.State.Sequence==10000&&store.State.Session==null&&store.State.BaitCount==before.BaitCount-1&&store.State.Stamina==before.Stamina-2&&store.State.Creel[0].Id==before.Creel[0].Id,"Final release failed or replaced old fish.");byte[] bytes=Bytes(path);var replay=store.Execute(cast);Need(replay.IsReplay&&!replay.Written&&Same(bytes,Bytes(path)),"Capacity blocked receipt replay or wrote again.");Need(!store.SaveBudget.CanStartCast&&store.SaveBudget.RemainingTransactions==0,"Completed budget incorrect.");}Need(FloatJournalArchive.Inspect(path).Sequence==10000,"Complete final frame did not reopen.");}
        });
        Test("actual idle 9998 journals reject new casts without spending resources or appending bytes",()=>{
            foreach(int schema in new[]{1,2,3}){string path=Copy(Historical(schema));using(var store=FloatGameStore.OpenExisting(path)){Commit(store.Execute(Sale(store.State)));var before=store.State;byte[] bytes=Bytes(path);Refused(store.Execute(Cast(before)),"save-settlement-reserve");Need(Object.ReferenceEquals(before,store.State)&&Same(bytes,Bytes(path))&&store.SaveBudget.RemainingTransactions==2,"Rejected cast changed state.");}}
        });
        Test("preexisting 9999 active legacy journals still allow recall or startup recovery at final slot",()=>{
            foreach(bool recover in new[]{false,true}){string path=Copy(Historical(2));using(var store=FloatGameStore.OpenExisting(path))Commit(store.Execute(Sale(store.State)));var before=FloatJournalArchive.Inspect(path);HistoricalAppend(path,Cast(before.State),2);using(var store=FloatGameStore.OpenExisting(path)){Need(store.State.Sequence==9999&&store.State.Session!=null&&!store.SaveBudget.CanStartCast,"Old active history not accepted.");Commit(store.Execute(recover?(FloatCommand)Recovery(store.State):Recall(store.State)));Need(store.State.Sequence==10000&&store.State.Session==null,"Current ending was blocked by new-cast reservation.");}}
        });
        Test("preexisting 9999 pending result journal allows its explicit final release",()=>{
            string path=Copy(Historical(3));var before=FloatJournalArchive.Inspect(path);HistoricalAppend(path,Cast(before.State),3);var active=FloatJournalArchive.Inspect(path);HistoricalAppend(path,Fish(active.State),3);using(var store=FloatGameStore.OpenExisting(path)){Need(store.State.Sequence==9999&&store.State.Session.CapacityBlocked,"Old pending history invalid.");Commit(store.Execute(Release(store.State)));Need(store.State.Sequence==10000&&store.State.Session==null,"Pending result could not release.");}
        });
        Test("active schema1 upgrade cannot spend the final reserved transaction",()=>{
            string path=Copy(Historical(1));using(var store=FloatGameStore.OpenExisting(path))Commit(store.Execute(Sale(store.State)));var idle=FloatJournalArchive.Inspect(path);HistoricalAppend(path,Cast(idle.State),1);var source=FloatJournalArchive.Inspect(path);var s=source.State;var upgrade=new UpgradeFloatProfile(s.ProfileId,s.BranchId,G(),G(),G(),G(),source.Sha256,source.TipSha256,source.Length,s.Sequence,1,1,Commerce());
            using(var store=FloatGameStore.OpenExisting(path)){byte[] bytes=Bytes(path);Refused(store.AppendUpgrade(upgrade),"save-settlement-reserve");Need(Same(bytes,Bytes(path))&&store.State.SchemaVersion==1&&store.State.Session!=null,"Upgrade consumed ending reservation.");Commit(store.Execute(Recall(store.State)));}
        });
        Test("save budget tracks actual journal bytes and advances only after confirmed append",()=>{
            string path=P("ordinary");bool fail=false;using(var store=FloatGameStore.CreateNew(path,New(3),point=>{if(fail&&point==FloatSaveFaultPoint.BeforeWrite)throw new IOException("private prewrite fault");})){var before=store.SaveBudget;Need(before.RemainingBytes==FloatGameStore.MaximumFileBytes-Bytes(path).Length,"Budget ignored actual length.");var cast=Cast(store.State);fail=true;Throws<IOException>(()=>store.Execute(cast));Need(store.SaveBudget.RemainingBytes==before.RemainingBytes&&store.SaveBudget.RemainingTransactions==before.RemainingTransactions,"Known rejection consumed budget.");fail=false;Commit(store.Execute(cast));Need(store.SaveBudget.RemainingBytes==before.RemainingBytes-212&&store.SaveBudget.RemainingTransactions==before.RemainingTransactions-1,"Confirmed cast budget wrong.");}
        });
        Test("faulted and disposed writers do not expose an apparently usable save budget",()=>{
            string path=P("unknown");var store=FloatGameStore.CreateNew(path,New(2));store.Dispose();Throws<ObjectDisposedException>(()=>{var ignored=store.SaveBudget;});
            using(var opened=FloatGameStore.OpenExisting(path,point=>{if(point==FloatSaveFaultPoint.AfterFlush)throw new IOException("private unknown fault");})){Throws<FloatSaveUncertainException>(()=>opened.Execute(Cast(opened.State)));Throws<InvalidOperationException>(()=>{var ignored=opened.SaveBudget;});}
        });
        Test("new gate does not rewrite or repair historical full-capacity active files",()=>{
            string path=Copy(Historical(2));using(var store=FloatGameStore.OpenExisting(path)){Commit(store.Execute(Sale(store.State)));var s=store.State;Commit(store.Execute(new BuyFloatSupply(s.ProfileId,s.BranchId,G(),G(),FloatPurchaseQuote.Create(s,FloatSupplyProduct.Food,1),Guid.Empty,s.Sequence,s.Rig.Revision,s.RulesVersion)));}var before=FloatJournalArchive.Inspect(path);HistoricalAppend(path,Cast(before.State),2);
            // Already-full history remains readable. There is no implicit
            // truncation, compaction, refund or extra transaction allowance.
            byte[] bytes=Bytes(path);using(var store=FloatGameStore.OpenExisting(path)){Need(store.State.Sequence==10000&&store.State.Session!=null,"Historical full active fixture invalid.");Refused(store.Execute(Recall(store.State)),"save-capacity");Need(!store.SaveBudget.CanStartCast&&Same(bytes,Bytes(path)),"Full journal was rewritten.");}
        });
        return failed==0?0:1;
    }
}
