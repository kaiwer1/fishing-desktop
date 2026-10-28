using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FishingDesktop.GameCore;

static class FloatTerminalLossTransactionTests
{
    static int passed,failed,nextId,nextFile;static string root;
    static Guid G(){return new Guid(++nextId,0,0,new byte[]{0,0,0,0,0,0,0,73});}
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
    static HostFloatTerminalLoss Loss(FloatGameState s,long outLine=30000,long cut=10000,Guid? encounter=null,Guid? businessEvent=null,string rule="terminal")
    {
        var line=s.Equipment.Find(s.Rig.LineId.Value);
        return new HostFloatTerminalLoss(s.ProfileId,s.BranchId,G(),businessEvent??G(),s.Session==null?G():s.Session.Id,encounter??G(),line.Id,line.Revision,rule,outLine,cut,s.Sequence,s.Rig.Revision,s.RulesVersion,s.Equipment.InventoryRevision,s.Equipment.RulesVersion,1);
    }
    static HostFloatTerminalLoss Copy(HostFloatTerminalLoss c,Guid? profile=null,Guid? branch=null,Guid? request=null,Guid? businessEvent=null,Guid? session=null,Guid? encounter=null,Guid? line=null,long? lineRevision=null,string rule=null,long? outLine=null,long? cut=null,long? sequence=null,long? rig=null,long? rules=null,long? inventory=null,long? equipment=null,int model=1)
    {return new HostFloatTerminalLoss(profile??c.ProfileId,branch??c.BranchId,request??c.RequestId,businessEvent??c.EventId,session??c.SessionId,encounter??c.EncounterId,line??c.LineId,lineRevision??c.ExpectedLineRevision,rule??c.LossRuleKey,outLine??c.OutLineMm,cut??c.CutDistanceFromReelMm,sequence??c.ExpectedSequence,rig??c.ExpectedRigRevision,rules??c.ExpectedRulesVersion,inventory??c.ExpectedInventoryRevision,equipment??c.ExpectedEquipmentVersion,model);}
    static void Reject(FloatGameState s,FloatCommand command,string code)
    {var result=FloatGameReducer.Reduce(s,command,s.Rules);Need(!result.HasCandidate&&!result.IsReplay&&result.Receipt==null&&result.Code==code,"Expected "+code+" got "+result.Code);}
    static RecoverFloatSession Recovery(FloatGameState s){return new RecoverFloatSession(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,s.Rig.Revision,s.RulesVersion,FloatRecoveryReason.StartupInterrupted);}
    static HostFloatUnhooked Unhook(FloatGameState s,Guid? encounter=null){return new HostFloatUnhooked(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,encounter??G(),s.Sequence,s.Rig.Revision,s.RulesVersion,2);}
    static void Resources(FloatGameState before,FloatGameState after)
    {
        Need(before.WalletMinor==after.WalletMinor&&before.Stamina==after.Stamina&&before.BaitCount==after.BaitCount&&before.BaitStackId==after.BaitStackId&&before.FoodCount==after.FoodCount&&before.FoodStackId==after.FoodStackId&&before.EquippedNetId==after.EquippedNetId&&(before.OwnedNet==null?Guid.Empty:before.OwnedNet.Id)==(after.OwnedNet==null?Guid.Empty:after.OwnedNet.Id),"Loss changed wallet, bait, stamina, food or net.");
        Need(before.Creel.Count==after.Creel.Count,"Loss changed creel.");for(int i=0;i<before.Creel.Count;i++)Need(before.Creel[i].Id==after.Creel[i].Id&&before.Creel[i].MassMg==after.Creel[i].MassMg,"Loss changed a fish.");
        Need(before.Equipment.InitialLineMm==after.Equipment.InitialLineMm&&before.Equipment.Instances.Count==after.Equipment.Instances.Count,"Loss removed identities or original line budget.");
        foreach(var item in before.Equipment.Instances)Need(after.Equipment.Find(item.Id)!=null,"Loss deleted an identity.");
    }
    static FloatGameState Stocked(Fixture f)
    {
        var s=f.State();s=Adopt(s,Buy(s,FloatSupplyProduct.Net));s=Adopt(s,new EquipFloatNet(s.ProfileId,s.BranchId,G(),G(),s.OwnedNet.Id,s.Sequence,s.Rig.Revision,s.RulesVersion));s=Adopt(s,Buy(s,FloatSupplyProduct.Food));s=Adopt(s,Cast(s));s=Adopt(s,Fish(s));return Adopt(s,Cast(s));
    }
    static void Summary(FloatReceipt row,HostFloatTerminalLoss command,long before,long after,int lostCount)
    {
        var loss=row.EquipmentLoss;Need(row.Outcome==FloatOutcome.TerminalLost&&row.Terminal&&row.SessionId==command.SessionId&&row.EncounterId==command.EncounterId&&row.EventId==command.EventId&&row.FishIds.Count==0&&row.RecoveryReason==null,"Loss receipt terminal identity mismatch.");
        Need(loss!=null&&loss.LineId==command.LineId&&loss.RemainingLineBeforeMm==before&&loss.RemainingLineAfterMm==after&&loss.LostLineMm==before-after&&loss.LostItemIds.Count==lostCount,"Loss summary mismatch.");
        Need(row.WalletBefore==row.WalletAfter&&row.StaminaBefore==row.StaminaAfter&&row.BaitBefore==row.BaitAfter&&row.FoodBefore==row.FoodAfter&&row.OwnedNetBefore==row.OwnedNetAfter&&row.EquippedNetBefore==row.EquippedNetAfter,"Loss receipt contains unrelated debit.");
    }
    static int Main(string[] args)
    {
        if(args.Length!=1||!Directory.Exists(args[0]))return 2;root=Path.GetFullPath(args[0]);
        Test("partial loss candidate is immutable and preserves all non-equipment resources",()=>{
            var f=new Fixture();var s=Stocked(f);var command=Loss(s);byte[] original=FloatEquipmentCodec.Encode(s.Equipment);var result=FloatGameReducer.Reduce(s,command,s.Rules);Need(result.RequiresConfirmation&&result.Code=="terminal-loss-candidate"&&result.Candidate.Session==null&&s.Session.Id==command.SessionId&&Same(original,FloatEquipmentCodec.Encode(s.Equipment)),"Unconfirmed loss mutated source.");
            var next=result.Candidate;Resources(s,next);Summary(result.Receipt,command,50000,30000,1);Need(next.Equipment.Find(f.Hook).Location.Place==FloatEquipmentPlace.Lost&&next.Equipment.Find(f.Hook).Location.OwnerId==command.EventId&&next.Equipment.Find(f.Line).Location.OwnerId==f.Reel&&next.Equipment.Find(f.Line).Revision==2&&next.Equipment.Find(f.Reel).Revision==2&&next.Rig.HookId==null&&next.Rig.Revision==2&&next.Equipment.InventoryRevision==2,"Partial loss ownership or revision wrong.");
            Need(next.Equipment.RemainingLineMm==50000&&next.Equipment.LostLineMm==20000&&next.Rig.LineLengthMm==30000,"Partial loss did not conserve millimetres.");
        });
        Test("full remaining line loss retains tombstone and empty spool across file reopen",()=>{
            var f=new Fixture();string path=PathFor("full");HostFloatTerminalLoss command;using(var store=FloatGameStore.CreateNew(path,f.State())){Commit(store.Execute(Cast(store.State)));command=Loss(store.State,50000,0);var paid=store.State;var result=store.Execute(command);Commit(result);Summary(result.Receipt,command,50000,0,2);Resources(paid,store.State);Need(store.State.Rig.LineId==null&&store.State.Equipment.Spool(f.Reel)==null&&store.State.Equipment.Find(f.Line).Location.Place==FloatEquipmentPlace.Lost&&store.State.Equipment.Find(f.Line).Location.OwnerId==command.EventId&&store.State.Equipment.Find(f.Line).RemainingLineMm==0,"Full loss discarded line identity or left spool.");}
            using(var store=FloatGameStore.OpenExisting(path)){Summary(store.State.Receipts[1],command,50000,0,2);Need(store.State.Equipment.InitialLineMm==70000&&store.State.Equipment.RemainingLineMm==20000&&store.State.Equipment.LostLineMm==50000,"Reopen changed line conservation.");Refused(store.Execute(Move(store.State,f.Line,f.Reel)),"item-unavailable");}
        });
        Test("zero millimetre line loss still loses the configured hook once",()=>{
            var f=new Fixture();var s=f.State();s=Adopt(s,Cast(s));var command=Loss(s,30000,30000);var next=Adopt(s,command);Summary(next.Receipts[1],command,50000,50000,1);Resources(s,next);Need(next.Rig.HookId==null&&next.Equipment.Find(f.Hook).Location.Place==FloatEquipmentPlace.Lost&&next.Rig.LineLengthMm==50000&&next.Equipment.Find(f.Line).Revision==1&&next.Equipment.Find(f.Reel).Revision==1&&next.Equipment.LostLineMm==0&&next.Equipment.InventoryRevision==2&&next.Rig.Revision==2,"Zero line loss changed line or failed to consume hook.");
        });
        Test("multi-component loss follows explicit catalogue rule only",()=>{
            var f=new Fixture();var c=Catalog();var catalog=new FloatEquipmentCatalog(c.Version,c.BaitDefinitionKey,c.Definitions,c.RodReelRules,c.SpoolRules,c.HookBaitRules,new[]{new FloatEquipmentLossRule("terminal",new[]{FloatEquipmentSlot.Hook,FloatEquipmentSlot.Bobber,FloatEquipmentSlot.Sinker})});
            var s=f.State(ledger:new FloatEquipmentLedger(f.Rig,1,1,catalog,f.Items));s=Adopt(s,Cast(s));var command=Loss(s,50000,0);var next=Adopt(s,command);Summary(next.Receipts[1],command,50000,0,4);Need(next.Rig.RodId==f.Rod&&next.Rig.ReelId==f.Reel&&next.Rig.HookId==null&&next.Rig.BobberId==null&&next.Rig.SinkerId==null&&next.Rig.LineId==null,"Rule altered wrong components.");Resources(s,next);
        });
        Test("out line can exceed original cast distance but never installed remaining line",()=>{
            var f=new Fixture();var s=f.State();s=Adopt(s,Cast(s,6000));var command=Loss(s,42000,7000);var next=Adopt(s,command);Need(s.Session.RequiredLineMm==6000&&next.Rig.LineLengthMm==15000,"Loss incorrectly capped paid out line at original cast distance.");Reject(s,Loss(s,50001,0),"line-loss-out-of-range");
        });
        Test("invalid identities ranges rule keys and model versions reject at construction",()=>{
            var f=new Fixture();var s=f.State();s=Adopt(s,Cast(s));var c=Loss(s);foreach(var id in new[]{Guid.Empty}){Throws<ArgumentException>(()=>Copy(c,session:id));Throws<ArgumentException>(()=>Copy(c,encounter:id));Throws<ArgumentException>(()=>Copy(c,line:id));}
            foreach(int model in new[]{-1,0,2,Int32.MaxValue})Throws<ArgumentOutOfRangeException>(()=>Copy(c,model:model));Throws<ArgumentOutOfRangeException>(()=>Copy(c,lineRevision:0));Throws<ArgumentOutOfRangeException>(()=>Copy(c,outLine:0));Throws<ArgumentOutOfRangeException>(()=>Copy(c,outLine:100000001));Throws<ArgumentOutOfRangeException>(()=>Copy(c,cut:-1));Throws<ArgumentOutOfRangeException>(()=>Copy(c,cut:30001));Throws<ArgumentException>(()=>Copy(c,rule:"bad|key"));
        });
        Test("wrong branch sequence and every equipment binding version reject without writes",()=>{
            var f=new Fixture();using(var store=FloatGameStore.CreateNew(PathFor("versions"),f.State())){Commit(store.Execute(Cast(store.State)));var c=Loss(store.State);var original=FloatEquipmentCodec.Encode(store.State.Equipment);
                Refused(store.Execute(Copy(c,profile:G())),"branch-mismatch");Refused(store.Execute(Copy(c,branch:G())),"branch-mismatch");foreach(var wrong in new[]{Copy(c,sequence:0),Copy(c,rig:2),Copy(c,rules:2)})Refused(store.Execute(wrong),"version-conflict");foreach(var wrong in new[]{Copy(c,inventory:2),Copy(c,equipment:8)})Refused(store.Execute(wrong),"equipment-version-conflict");Refused(store.Execute(Copy(c,lineRevision:2)),"session-line-not-current");Need(store.State.Sequence==1&&Same(original,FloatEquipmentCodec.Encode(store.State.Equipment)),"Stale binding changed ledger.");}
        });
        Test("spare line on another reel or in inventory cannot be lost by current session",()=>{
            foreach(bool onReel in new[]{false,true}){var f=new Fixture();var s=f.State();if(onReel)s=Adopt(s,Move(s,f.SpareLine,f.SpareReel));s=Adopt(s,Cast(s));var c=Loss(s);Reject(s,Copy(c,line:f.SpareLine,lineRevision:s.Equipment.Find(f.SpareLine).Revision),"session-line-not-current");Reject(s,Copy(c,line:f.Reel),"session-line-not-current");}
        });
        Test("ordinary current session is required and pending fish cannot be erased",()=>{
            var f=new Fixture();var s=f.State();Reject(s,Loss(s),"session-not-current");s=Adopt(s,Cast(s));Reject(s,Copy(Loss(s),session:G()),"session-not-current");s=Adopt(s,Fish(s));s=Adopt(s,Cast(s));s=Adopt(s,Fish(s));var pending=s.Session.PendingFish;Reject(s,Loss(s),"pending-fish-already-set");Need(s.Session.PendingFish==pending&&s.Creel.Count==1,"Loss removed pending catch.");
        });
        Test("missing loss configuration rejects instead of inventing terminal components",()=>{
            var f=new Fixture();var s=f.State();s=Adopt(s,Cast(s));Reject(s,Loss(s,rule:"missing"),"configuration-missing");var command=Loss(s);Need(FloatGameReducer.Reduce(s,command,null).Code=="configuration-missing","Missing rules accepted.");
        });
        Test("receipt summary is derived immutable and absent on unrelated outcomes",()=>{
            var f=new Fixture();var s=f.State();s=Adopt(s,Cast(s));Need(s.Receipts[0].EquipmentLoss==null,"Cast got loss summary.");var c=Loss(s);var next=Adopt(s,c);var summary=next.Receipts[1].EquipmentLoss;Throws<NotSupportedException>(()=>((IList<Guid>)summary.LostItemIds).Add(G()));Need(summary.LostItemIds[0]==f.Hook,"Summary used fish or wrong item.");
            var supplied=new List<Guid>{f.Hook};var copy=new FloatEquipmentLossSummary(f.Line,50000,30000,20000,supplied);supplied.Clear();Need(copy.LostItemIds.Count==1,"Mutable summary input escaped.");Throws<ArgumentException>(()=>new FloatEquipmentLossSummary(f.Line,50000,0,50000,new[]{f.Hook}));
        });
        Test("every accepted command field is bound to the original request fingerprint",()=>{
            var f=new Fixture();var s=f.State();s=Adopt(s,Cast(s));var c=Loss(s);s=Adopt(s,c);var variants=new[]{Copy(c,businessEvent:G()),Copy(c,session:G()),Copy(c,encounter:G()),Copy(c,line:G()),Copy(c,lineRevision:2),Copy(c,rule:"other"),Copy(c,outLine:30001),Copy(c,cut:9999),Copy(c,sequence:2),Copy(c,rig:2),Copy(c,rules:2),Copy(c,inventory:2),Copy(c,equipment:8)};
            foreach(var changed in variants){Need(c.Fingerprint!=changed.Fingerprint,"Field missing from fingerprint.");Reject(s,changed,"request-conflict");}Reject(s,Copy(c,profile:G()),"branch-mismatch");Reject(s,Copy(c,branch:G()),"branch-mismatch");Need(c.Detail.EndsWith("|1")&&c.Kind=="host-terminal-loss","Model version missing from fingerprint.");
        });
        Test("same request replays once and fresh request cannot reuse terminal event",()=>{
            var f=new Fixture();using(var store=FloatGameStore.CreateNew(PathFor("duplicate"),f.State())){Commit(store.Execute(Cast(store.State)));var c=Loss(store.State);Commit(store.Execute(c));var original=FloatEquipmentCodec.Encode(store.State.Equipment);Need(store.Execute(c).IsReplay&&store.State.Sequence==2,"Duplicate wrote twice.");Refused(store.Execute(Copy(c,request:G(),sequence:2,rig:2,inventory:2)),"event-already-used");Need(Same(original,FloatEquipmentCodec.Encode(store.State.Equipment)),"Duplicate changed ledger.");}
        });
        Test("event and encounter cannot collide with current global assets or each other",()=>{
            var f=new Fixture();var s=Stocked(f);var c=Loss(s);var ids=new List<Guid>{s.ProfileId,s.BranchId,s.Rig.Id,s.BaitStackId,s.FoodStackId,s.OwnedNet.Id,s.Creel[0].Id,s.Session.Id};foreach(var item in s.Equipment.Instances)ids.Add(item.Id);
            foreach(var id in ids){Reject(s,Copy(c,businessEvent:id),"loss-identity-invalid");Reject(s,Copy(c,encounter:id),"encounter-identity-used");}Reject(s,Copy(c,businessEvent:c.EncounterId),"loss-identity-invalid");Reject(s,Copy(c,encounter:c.EventId),"loss-identity-invalid");
        });
        Test("historical sold fish session encounter and event identities cannot be reassigned",()=>{
            var f=new Fixture();var s=f.State();s=Adopt(s,Cast(s));var fish=Fish(s);s=Adopt(s,fish);s=Adopt(s,new SellFloatFish(s.ProfileId,s.BranchId,G(),G(),new[]{fish.FishId},s.Sequence,s.Rig.Revision,s.RulesVersion));s=Adopt(s,Cast(s));var c=Loss(s);
            foreach(var id in new[]{fish.FishId,fish.SessionId,fish.EncounterId}){Reject(s,Copy(c,businessEvent:id),"loss-identity-invalid");Reject(s,Copy(c,encounter:id),"encounter-identity-used");}Reject(s,Copy(c,encounter:fish.EventId),"encounter-identity-used");
        });
        Test("loss event and encounter reject all historical requests and their own request",()=>{
            var f=new Fixture();string path=PathFor("request-identities");using(var store=FloatGameStore.CreateNew(path,f.State())){Commit(store.Execute(Buy(store.State,FloatSupplyProduct.Food)));Commit(store.Execute(Cast(store.State)));Commit(store.Execute(Recall(store.State)));Commit(store.Execute(Cast(store.State)));var c=Loss(store.State);byte[] before=Bytes(path);
                foreach(var receipt in store.State.Receipts){Refused(store.Execute(Copy(c,businessEvent:receipt.RequestId)),"loss-identity-invalid");Refused(store.Execute(Copy(c,encounter:receipt.RequestId)),"encounter-identity-used");}
                Refused(store.Execute(Copy(c,businessEvent:c.RequestId)),"loss-identity-invalid");Refused(store.Execute(Copy(c,encounter:c.RequestId)),"encounter-identity-used");Need(Same(before,Bytes(path))&&store.State.Session.Id==c.SessionId&&store.State.Rig.HookId==f.Hook,"Request identity collision changed active session or ledger.");Commit(store.Execute(c));Need(store.Execute(c).IsReplay,"Valid distinct identities lost exact replay.");
            }
        });
        Test("old unhooked encounter cannot be claimed by terminal loss",()=>{
            var f=new Fixture();var s=f.State();s=Adopt(s,Cast(s));var unhook=Unhook(s);s=Adopt(s,unhook);s=Adopt(s,Cast(s));Reject(s,Loss(s,encounter:unhook.EncounterId),"encounter-identity-used");
        });
        Test("loss encounter stays consumed after repairing gear and starting another session",()=>{
            var f=new Fixture();var s=f.State();s=Adopt(s,Cast(s));var c=Loss(s);s=Adopt(s,c);s=Adopt(s,Replace(s,FloatEquipmentSlot.Hook,f.SpareHook));s=Adopt(s,Cast(s));var active=s.Session;var replay=FloatGameReducer.Reduce(s,c,s.Rules);Need(replay.IsReplay&&!replay.HasCandidate&&replay.Receipt.Sequence==2&&s.Session==active,"Old receipt changed current session.");Reject(s,Loss(s,encounter:c.EncounterId),"encounter-identity-used");Reject(s,Unhook(s,c.EncounterId),"encounter-identity-used");var fish=new HostFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,G(),c.EncounterId,"carp",500000,s.Sequence,s.Rig.Revision,s.RulesVersion);Reject(s,fish,"encounter-identity-used");
        });
        Test("loss event and encounter stay reserved against every later identity allocation",()=>{
            var f=new Fixture();string path=PathFor("reserved");HostFloatTerminalLoss loss;
            using(var store=FloatGameStore.CreateNew(path,f.State())){Commit(store.Execute(Cast(store.State)));loss=Loss(store.State);Commit(store.Execute(loss));Commit(store.Execute(Replace(store.State,FloatEquipmentSlot.Hook,f.SpareHook)));}
            using(var owner=FloatGameOwner.OpenExisting(path,null,3)){var c=new Control(owner);byte[] before=Bytes(path);foreach(var id in new[]{loss.EventId,loss.EncounterId}){
                    Refused(c.Send(Buy(c.State,FloatSupplyProduct.Net,id)),"asset-identity-used");Refused(c.Send(Cast(c.State,15000,id)),"session-identity-used");
                    var s=c.State;Refused(c.Send(new BuyFloatSupply(s.ProfileId,s.BranchId,id,G(),FloatPurchaseQuote.Create(s,FloatSupplyProduct.Bait,1),Guid.Empty,s.Sequence,s.Rig.Revision,s.RulesVersion)),"request-identity-used");
                    Refused(c.Send(new BuyFloatSupply(s.ProfileId,s.BranchId,G(),id,FloatPurchaseQuote.Create(s,FloatSupplyProduct.Bait,1),Guid.Empty,s.Sequence,s.Rig.Revision,s.RulesVersion)),id==loss.EventId?"event-already-used":"event-identity-used");
                }Need(Same(before,Bytes(path)),"Rejected reserved identity wrote a transaction.");Commit(c.Send(Cast(c.State)));before=Bytes(path);
                foreach(var id in new[]{loss.EventId,loss.EncounterId}){Refused(owner.ExecuteHost(Fish(c.State,id)),"fish-identity-used");Refused(owner.ExecuteHost(Unhook(c.State,id)),"encounter-identity-used");Refused(owner.ExecuteHost(Loss(c.State,encounter:id)),"encounter-identity-used");var s=c.State;Refused(owner.ExecuteHost(new HostFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,G(),id,"carp",500000,s.Sequence,s.Rig.Revision,s.RulesVersion)),"encounter-identity-used");}
                Need(Same(before,Bytes(path))&&owner.ExecuteHost(loss).IsReplay,"Reserved identity check damaged original replay or active session.");
            }
        });
        Test("loss committed first defeats competing recall recovery landing and unhook",()=>{
            var f=new Fixture();var s=f.State();s=Adopt(s,Cast(s));var c=Loss(s);var alternatives=new FloatCommand[]{Recall(s),Recovery(s),Fish(s),Unhook(s)};s=Adopt(s,c);foreach(var command in alternatives)Reject(s,command,"version-conflict");Need(s.Creel.Count==0&&s.Session==null&&s.Receipts.Count==2,"Competing result replaced loss.");
        });
        Test("other terminal committed first defeats stale loss without equipment changes",()=>{
            foreach(int winner in new[]{0,1,2,3}){var f=new Fixture();var s=f.State();s=Adopt(s,Cast(s));var c=Loss(s);var before=FloatEquipmentCodec.Encode(s.Equipment);s=Adopt(s,winner==0?(FloatCommand)Recall(s):winner==1?Recovery(s):winner==2?(FloatCommand)Fish(s):Unhook(s));Reject(s,c,"version-conflict");Need(Same(before,FloatEquipmentCodec.Encode(s.Equipment)),"Stale loss damaged equipment.");}
        });
        Test("type15 schema3 codec is exact and rejects all truncated prefixes and tail bytes",()=>{
            var f=new Fixture();var s=f.State();s=Adopt(s,Cast(s));var c=Loss(s);var bytes=FloatJournalCodec.EncodeCommand(c);var decoded=(HostFloatTerminalLoss)FloatJournalCodec.DecodeCommand(bytes);Need(bytes[4]==3&&bytes[7]==15&&decoded.Fingerprint==c.Fingerprint&&decoded.RequestId==c.RequestId&&Same(bytes,FloatJournalCodec.EncodeCommand(decoded)),"Codec changed typed command.");for(int n=0;n<bytes.Length;n++){var prefix=new byte[n];Array.Copy(bytes,prefix,n);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(prefix));}var tail=new byte[bytes.Length+1];Array.Copy(bytes,tail,bytes.Length);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(tail));
        });
        Test("legacy schema unknown model empty identities and invalid encoded values reject",()=>{
            var f=new Fixture();var s=f.State();s=Adopt(s,Cast(s));var c=Loss(s);var bytes=FloatJournalCodec.EncodeCommand(c);foreach(int schema in new[]{1,2}){Throws<InvalidDataException>(()=>FloatJournalCodec.EncodeCommand(c,schema));var bad=(byte[])bytes.Clone();bad[4]=(byte)schema;Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bad));}
            foreach(int offset in new[]{72,128,144}){var bad=(byte[])bytes.Clone();Array.Clear(bad,offset,16);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bad));}foreach(int model in new[]{0,2}){var bad=(byte[])bytes.Clone();Array.Copy(BitConverter.GetBytes(model),0,bad,bad.Length-4,4);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bad));}
            var wrong=(byte[])bytes.Clone();wrong[7]=16;Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(wrong));wrong=(byte[])bytes.Clone();Array.Copy(BitConverter.GetBytes(-1L),0,wrong,160,8);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(wrong));wrong=(byte[])bytes.Clone();wrong[170]=0xff;Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(wrong));
        });
        Test("user observer revoked and reloaded document cannot submit host loss",()=>{
            using(var owner=FloatGameOwner.CreateNew(PathFor("authority"),new Fixture().State())){var c=new Control(owner);Commit(c.Send(Cast(c.State)));var loss=Loss(c.State);Refused(c.Send(loss),"user-command-not-allowed");var observer=owner.OpenView(FloatViewRole.Observer);var doc=owner.AttachDocument(observer);Refused(owner.ExecuteUser(observer,doc,c.Lease,loss),"control-required");var old=c.Document;var lease=c.Lease;c.Document=owner.AttachDocument(c.View);c.Lease=owner.GrantControl(c.View,c.Document);Refused(owner.ExecuteUser(c.View,old,lease,loss),"control-required");Commit(owner.ExecuteHost(loss));Refused(c.Send(loss),"user-command-not-allowed");owner.RevokeControl(c.Lease);Refused(c.Send(loss),"control-required");}
        });
        Test("paused or menu host cannot create loss but exact receipt lookup remains read only",()=>{
            using(var owner=FloatGameOwner.CreateNew(PathFor("pause"),new Fixture().State())){var c=new Control(owner);Commit(c.Send(Cast(c.State)));var loss=Loss(c.State);owner.SetMenuOpen(true);Refused(owner.ExecuteHost(loss),"simulation-paused");owner.SetMenuOpen(false);owner.SetHostPaused(true);Refused(owner.ExecuteHost(loss),"simulation-paused");owner.SetHostPaused(false);Commit(owner.ExecuteHost(loss));owner.SetHostPaused(true);var result=owner.ExecuteHost(loss);Need(result.IsReplay&&!result.Written&&result.State.Sequence==2,"Paused receipt query wrote.");}
        });
        Test("confirmed reopen performs no recovery and missing hook cast has zero additional cost",()=>{
            var f=new Fixture();string path=PathFor("restart");HostFloatTerminalLoss command;byte[] before;using(var owner=FloatGameOwner.CreateNew(path,f.State())){var c=new Control(owner);Commit(c.Send(Cast(c.State)));command=Loss(c.State);Commit(owner.ExecuteHost(command));before=Bytes(path);}
            using(var owner=FloatGameOwner.OpenExisting(path,null,3)){var c=new Control(owner);Need(c.State.Sequence==2&&c.State.Session==null&&Same(before,Bytes(path)),"Startup replaced loss with recovery.");Summary(c.State.Receipts[1],command,50000,30000,1);Refused(c.Send(Cast(c.State)),"missing-component");Need(c.State.BaitCount==11&&c.State.Stamina==98&&Same(before,Bytes(path))&&owner.ExecuteHost(command).IsReplay,"Missing part charged or old loss repeated.");}
        });
        Test("known prewrite failure leaves exact source state then explicit same command retries once",()=>{
            bool armed=false;string path=PathFor("known");using(var owner=FloatGameOwner.CreateNew(path,new Fixture().State(),at=>{if(armed&&at==FloatSaveFaultPoint.BeforeWrite)throw new IOException("private loss fault");})){var c=new Control(owner);Commit(c.Send(Cast(c.State)));var command=Loss(c.State);var paid=c.State;var before=Bytes(path);armed=true;Throws<IOException>(()=>owner.ExecuteHost(command));Need(!owner.Snapshot.Faulted&&Object.ReferenceEquals(c.State,paid)&&Same(before,Bytes(path)),"Known unwritten loss was published.");armed=false;Commit(owner.ExecuteHost(command));Need(owner.ExecuteHost(command).IsReplay&&c.State.Sequence==2,"Known retry deducted twice.");Resources(paid,c.State);}
        });
        Test("known unwritten loss is not recoverable after process close and ordinary recovery wins",()=>{
            bool armed=false;string path=PathFor("unwritten-close");HostFloatTerminalLoss command;byte[] equipment;using(var owner=FloatGameOwner.CreateNew(path,new Fixture().State(),at=>{if(armed&&at==FloatSaveFaultPoint.BeforeWrite)throw new IOException("private unwritten loss");})){var c=new Control(owner);Commit(c.Send(Cast(c.State)));command=Loss(c.State);equipment=FloatEquipmentCodec.Encode(c.State.Equipment);armed=true;Throws<IOException>(()=>owner.ExecuteHost(command));}
            using(var owner=FloatGameOwner.OpenExisting(path,null,3)){var c=new Control(owner);Need(c.State.Sequence==2&&c.State.Receipts[1].Outcome==FloatOutcome.Recovered&&c.State.Receipts[1].EquipmentLoss==null&&Same(equipment,FloatEquipmentCodec.Encode(c.State.Equipment))&&c.State.Session==null&&c.State.BaitCount==11&&c.State.Stamina==98,"Undurable loss was invented or paid cast refunded.");Refused(owner.ExecuteHost(command),"version-conflict");}
        });
        Test("uncertain complete frame hides state then reopens exactly one loss without recovery",()=>{
            foreach(var point in new[]{FloatSaveFaultPoint.AfterFrame,FloatSaveFaultPoint.AfterFlush}){bool armed=false;string path=PathFor("uncertain");HostFloatTerminalLoss command;FloatGameState paid;
                using(var owner=FloatGameOwner.CreateNew(path,new Fixture().State(),at=>{if(armed&&at==point)throw new IOException("private uncertain loss");})){var c=new Control(owner);Commit(c.Send(Cast(c.State)));paid=c.State;command=Loss(paid);armed=true;Throws<FloatSaveUncertainException>(()=>owner.ExecuteHost(command));Need(owner.Snapshot.Faulted&&owner.Snapshot.State==null&&!owner.Snapshot.CanSimulate,"Unknown write exposed obsolete rig.");Throws<InvalidOperationException>(()=>owner.ExecuteHost(command));Refused(c.Send(Recall(paid)),"control-required");}
                var before=Bytes(path);using(var owner=FloatGameOwner.OpenExisting(path,null,3)){var result=owner.ExecuteHost(command);Need(result.IsReplay&&!result.Written&&result.State.Sequence==2&&result.State.Session==null&&Same(before,Bytes(path)),"Reopen repeated loss or added recovery.");Summary(result.Receipt,command,50000,30000,1);Resources(paid,result.State);}
            }
        });
        Test("uncertain partial frame refuses reopen without trimming or automatic repair",()=>{
            bool armed=false;string path=PathFor("partial");using(var owner=FloatGameOwner.CreateNew(path,new Fixture().State(),at=>{if(armed&&at==FloatSaveFaultPoint.AfterPrefix)throw new IOException("private partial loss");})){var c=new Control(owner);Commit(c.Send(Cast(c.State)));armed=true;Throws<FloatSaveUncertainException>(()=>owner.ExecuteHost(Loss(c.State)));}
            var before=Bytes(path);Throws<InvalidDataException>(()=>{using(var owner=FloatGameOwner.OpenExisting(path,null,3)){}});Need(Same(before,Bytes(path)),"Invalid tail was silently repaired.");
        });
        Test("schema1 and schema2 ordinary sessions reject loss without altering legacy bytes",()=>{
            foreach(int schema in new[]{1,2}){var rig=new FixedFloatRig(G(),1,"float-rig",G(),G(),G(),50000,G(),G(),G());var s=schema==1?FloatGameState.CreateNew(G(),G(),12000,100,G(),"worm",12,rig,Rules()):FloatGameState.CreateNewWithCommerce(G(),G(),12000,100,G(),"worm",12,rig,Rules(),Commerce(),G());string path=PathFor("legacy");using(var store=FloatGameStore.CreateNew(path,s)){s=store.State;Commit(store.Execute(new CastFloat(s.ProfileId,s.BranchId,G(),G(),G(),0,1,1)));s=store.State;var command=new HostFloatTerminalLoss(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,G(),s.Rig.LineId.Value,1,"terminal",30000,10000,s.Sequence,1,1,1,7,1);var before=Bytes(path);Refused(store.Execute(command),"equipment-schema-required");Need(Same(before,Bytes(path))&&store.State.Session!=null,"Legacy loss changed state.");}}
        });
        Test("correctly hashed forged loss with stale line revision is rejected by journal replay",()=>{
            string path=PathFor("forged");HostFloatTerminalLoss command;using(var store=FloatGameStore.CreateNew(path,new Fixture().State())){Commit(store.Execute(Cast(store.State)));command=Copy(Loss(store.State),lineRevision:2);}AppendFixtureFrame(path,2,FloatJournalCodec.EncodeCommand(command));var before=Bytes(path);Throws<InvalidDataException>(()=>{using(var store=FloatGameStore.OpenExisting(path)){}});Need(Same(before,Bytes(path)),"Replay accepted stale line or rewrote source.");
        });
        Test("lost identities stay unavailable while explicit spare repair enables another cast",()=>{
            var f=new Fixture();using(var store=FloatGameStore.CreateNew(PathFor("repair"),f.State())){Commit(store.Execute(Cast(store.State)));var command=Loss(store.State);Commit(store.Execute(command));Refused(store.Execute(Replace(store.State,FloatEquipmentSlot.Hook,f.Hook)),"item-unavailable");Refused(store.Execute(Buy(store.State,FloatSupplyProduct.Net,f.Hook)),"asset-identity-used");Commit(store.Execute(Replace(store.State,FloatEquipmentSlot.Hook,f.SpareHook)));Commit(store.Execute(Cast(store.State)));Refused(store.Execute(Fish(store.State,f.Hook)),"fish-identity-used");Refused(store.Execute(Loss(store.State,businessEvent:f.Hook)),"loss-identity-invalid");Need(store.State.Equipment.Find(f.Hook).Location.OwnerId==command.EventId&&store.State.Rig.LineLengthMm==30000&&store.State.BaitCount==10,"Repair revived lost gear or altered line.");}
        });
        Test("shutdown after terminal loss does not append a second terminal or refund resources",()=>{
            string path=PathFor("shutdown");using(var owner=FloatGameOwner.CreateNew(path,new Fixture().State(1,2,0))){var c=new Control(owner);Commit(c.Send(Cast(c.State)));Commit(owner.ExecuteHost(Loss(c.State)));var before=Bytes(path);Need(c.State.BaitCount==0&&c.State.Stamina==0,"Paid cast cost wrong.");var result=owner.Shutdown();Need(result.Confirmed&&result.NoActiveSession&&c.State.Sequence==2&&Same(before,Bytes(path)),"Shutdown altered confirmed loss.");}
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
}
