using System;
using System.IO;
using System.Linq;
using FishingDesktop.GameCore;

static class BottomOwnedKitTests
{
    static int passed;
    static string folder;
    static Guid G(){return Guid.NewGuid();}
    static string FileName(){return Path.Combine(folder,G().ToString("N")+".fltj");}
    static void Test(string label,Action body){
        try{body();passed++;Console.WriteLine("PASS "+label);}
        catch(Exception error){Console.WriteLine("FAIL "+label+" ("+error.GetType().Name+"): "+error.Message);Environment.Exit(1);}
    }
    static void Need(bool condition){if(!condition)throw new Exception("Bottom kit assertion failed.");}
    static void Throws<T>(Action body)where T:Exception{try{body();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    static BottomOwnedKit Kit(Guid? rig=null,Guid? leader=null){return new BottomOwnedKit(rig??G(),G(),G(),G(),G(),G(),leader??G());}
    static BuyBottomKit Buy(FloatGameState s,BottomOwnedKit kit=null)
    {return new BuyBottomKit(s.ProfileId,s.BranchId,G(),G(),kit??Kit(),s.Sequence,s.Rig.Revision,s.RulesVersion);}
    internal static FloatGameState New(int schema=3,int version=2,int balance=12000)
    {
        var rig=new FixedFloatRig(G(),1,"starter",G(),G(),G(),50000,G(),G(),G());
        var rules=new FloatGameRules(version,"starter","worm",2,100,1,1000000,new[]{new FloatSpeciesRule("carp",100000,3000000,120)});
        var shop=new FloatCommerceCatalog(1,"worm","net","bread",2400,5,20,20,20,1000,1000,100);
        if(schema==1)return FloatGameState.CreateNew(G(),G(),balance,100,G(),"worm",12,rig,rules);
        var legacy=FloatGameState.CreateNewWithCommerce(G(),G(),balance,100,G(),"worm",12,rig,rules,shop,G());
        if(schema==2)return legacy;
        var equipment=new FloatEquipmentCatalog(1,"worm",new[]{
            new FloatEquipmentDefinition("rod",FloatEquipmentKind.Rod,150000,0,1000,25000),new FloatEquipmentDefinition("reel",FloatEquipmentKind.Reel,200000),
            new FloatEquipmentDefinition("line",FloatEquipmentKind.Line,0,180),new FloatEquipmentDefinition("hook",FloatEquipmentKind.Hook,200),
            new FloatEquipmentDefinition("bobber",FloatEquipmentKind.Bobber,3000,0,2000,3000),new FloatEquipmentDefinition("sinker",FloatEquipmentKind.Sinker,2500)},
            new[]{new FloatEquipmentPairRule("rod","reel",true)},new[]{new FloatEquipmentSpoolRule("reel","line",180,50000)},
            new[]{new FloatEquipmentPairRule("hook","worm",true)},new FloatEquipmentLossRule[0]);
        var mapping=new FloatEquipmentMigrationPolicy(1,"starter",equipment,"rod","reel","line","hook","bobber","sinker");
        return FloatGameState.CreateNewWithEquipment(legacy.ProfileId,legacy.BranchId,balance,100,legacy.BaitStackId,"worm",12,mapping.Map(legacy),rules,shop,legacy.FoodStackId);
    }
    static void Refused(FloatCommitResult r,string code){if(r.Code!=code || r.Committed || r.Written || r.IsReplay)throw new Exception("Expected "+code+", received "+r.Code);}
    static CastFloatWithEquipment Cast(FloatGameState s,Guid? session=null)
    {return new CastFloatWithEquipment(s.ProfileId,s.BranchId,G(),G(),session??G(),15000,s.Sequence,s.Rig.Revision,s.RulesVersion,s.Equipment.InventoryRevision,s.Equipment.RulesVersion);}
    static BuyFloatSupply Net(FloatGameState s,Guid id)
    {return new BuyFloatSupply(s.ProfileId,s.BranchId,G(),G(),FloatPurchaseQuote.Create(s,FloatSupplyProduct.Net,1),id,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static void Land(FloatGameStore store)
    {var s=store.State;Need(store.Execute(new HostFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,G(),G(),"carp",500000,s.Sequence,s.Rig.Revision,s.RulesVersion)).Written);}
    static CastBottomKitWithEquipment Bound(FloatGameState s,Guid? rig=null,Guid? line=null,Guid? leader=null,long revision=1,long span=25000)
    {
        var kit=s.OwnedBottomKit;
        return new CastBottomKitWithEquipment(s.ProfileId,s.BranchId,G(),G(),G(),rig??kit.Equipment.RigId,line??kit.Ids[3],leader??kit.LeaderId,
            revision,1,span,s.Sequence,s.Rig.Revision,s.RulesVersion);
    }
    public static int Main(string[] args)
    {
        folder=args[0];
        Test("old profiles keep no kit and cannot purchase it",()=>{
            foreach(var s in new[]{New(1),New(2),New(3,1),New(3,3)}){
                var r=FloatGameReducer.Reduce(s,Buy(s),s.Rules);Need(s.OwnedBottomKit==null && !r.HasCandidate && r.Code=="bottom-kit-unavailable");
            }
        });
        Test("one purchase debits 64 silver without changing the original float rig or its consumables",()=>{
            string path=FileName();var genesis=New();var prior=FloatEquipmentCodec.Encode(genesis.Equipment);
            using(var store=FloatGameStore.CreateNew(path,genesis)){
                var r=store.Execute(Buy(genesis));Need(r.Written && r.OperationSucceeded && r.Receipt.Outcome==FloatOutcome.BottomKitPurchased &&
                    r.Receipt.WalletBefore-r.Receipt.WalletAfter==6400 && store.State.WalletMinor==5600 && store.State.Sequence==1);
                Need(store.State.BaitCount==12 && store.State.Stamina==100 && store.State.Equipment==genesis.Equipment &&
                    prior.SequenceEqual(FloatEquipmentCodec.Encode(store.State.Equipment)) && store.State.Equipment.Instances.Count==6);
                Need(store.Execute(Net(store.State,G())).Written && store.State.WalletMinor==3200);
            }
        });
        Test("genesis bytes remain unchanged and reloaded kit binds the same parts and original bait stack",()=>{
            string path=FileName();var initial=New();var kit=Kit();var cmd=Buy(initial,kit);byte[] prefix;
            using(var store=FloatGameStore.CreateNew(path,initial)){}
            prefix=File.ReadAllBytes(path);
            using(var store=FloatGameStore.OpenExisting(path)){Need(store.Execute(cmd).Written);}
            Need(File.ReadAllBytes(path).Take(prefix.Length).SequenceEqual(prefix));
            using(var store=FloatGameStore.OpenExisting(path)){
                BottomEquipmentBridge bridge;Need(BottomOwnedEquipment.Bind(store.State,out bridge)==null);
                Need(kit.Ids.SequenceEqual(store.State.OwnedBottomKit.Ids) && bridge.Assembly.ItemId(BottomPartKind.HookBait)==initial.BaitStackId &&
                    bridge.Assembly.ItemId(BottomPartKind.Leader)==kit.LeaderId && bridge.Assembly.LineRemainingMm==50000 &&
                    bridge.IsCurrent(store.State.OwnedBottomKit.Equipment) && !bridge.IsCurrent(store.State.Equipment));
                Need(store.Execute(cmd).IsReplay && store.State.Sequence==1 && store.State.WalletMinor==5600);
            }
        });
        Test("another purchase or a changed duplicate request cannot grant another set",()=>{
            using(var store=FloatGameStore.CreateNew(FileName(),New())){
                var c=Buy(store.State);Need(store.Execute(c).Written);Refused(store.Execute(Buy(store.State)),"bottom-kit-already-owned");
                var mismatch=new BuyBottomKit(c.ProfileId,c.BranchId,c.RequestId,c.EventId,Kit(),c.ExpectedSequence,c.ExpectedRigRevision,c.ExpectedRulesVersion);
                Refused(store.Execute(mismatch),"request-conflict");
            }
        });
        Test("insufficient balance and an active or pending rod cannot charge",()=>{
            using(var s=FloatGameStore.CreateNew(FileName(),New(balance:6399))){Refused(s.Execute(Buy(s.State)),"wallet-insufficient");Need(s.State.Sequence==0 && s.State.OwnedBottomKit==null);}
            using(var s=FloatGameStore.CreateNew(FileName(),New())){
                Need(s.Execute(Cast(s.State)).Written);Refused(s.Execute(Buy(s.State)),"session-busy");
                var v=s.State;Need(s.Execute(new HostFloatFish(v.ProfileId,v.BranchId,G(),G(),v.Session.Id,G(),G(),"carp",500000,v.Sequence,v.Rig.Revision,v.RulesVersion)).Written);
                Need(s.Execute(Cast(s.State)).Written);v=s.State;Need(s.Execute(new HostFloatFish(v.ProfileId,v.BranchId,G(),G(),v.Session.Id,G(),G(),"carp",500000,v.Sequence,v.Rig.Revision,v.RulesVersion)).Written);
                Need(s.State.Session.CapacityBlocked);Refused(s.Execute(Buy(s.State)),"session-busy");
            }
        });
        Test("duplicate, historical and current identities cannot be minted or repurposed",()=>{
            var duplicate=G();Throws<ArgumentException>(()=>Kit(duplicate,duplicate));
            using(var store=FloatGameStore.CreateNew(FileName(),New())){
                Refused(store.Execute(Buy(store.State,Kit(leader:store.State.BaitStackId))),"asset-identity-used");
                var coat=new ChangeFloatClothing(store.State.ProfileId,store.State.BranchId,G(),G(),FloatClothingAction.Buy,"moss-jacket",0,1,2);
                Need(store.Execute(coat).Written);Refused(store.Execute(Buy(store.State,Kit(coat.EventId))),"asset-identity-used");
                var c=Buy(store.State);Need(store.Execute(c).Written);var s=store.State;var id=c.Kit.LeaderId;
                Refused(store.Execute(Net(s,id)),"asset-identity-used");Refused(store.Execute(Cast(s,id)),"session-identity-used");
                var reuseRequest=new ChangeFloatClothing(s.ProfileId,s.BranchId,id,G(),FloatClothingAction.Wear,"moss-jacket",s.Sequence,1,2);
                Refused(store.Execute(reuseRequest),"request-identity-used");
                Need(store.Execute(Cast(s)).Written);s=store.State;
                Refused(store.Execute(new HostFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,id,G(),"carp",500000,s.Sequence,1,2)),"fish-identity-used");
                Refused(store.Execute(new HostFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,G(),id,"carp",500000,s.Sequence,1,2)),"encounter-identity-used");
            }
        });
        Test("type 22 rejects old schemas, duplicate IDs, truncation and trailing bytes",()=>{
            var c=Buy(New());var bytes=FloatJournalCodec.EncodeCommand(c);Need(bytes[7]==22 && FloatJournalCodec.DecodeCommand(bytes).Fingerprint==c.Fingerprint);
            foreach(int schema in new[]{1,2})Throws<InvalidDataException>(()=>FloatJournalCodec.EncodeCommand(c,schema));
            for(int i=0;i<bytes.Length;i++)Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bytes.Take(i).ToArray()));
            Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bytes.Concat(new byte[]{1}).ToArray()));
            Array.Copy(bytes,bytes.Length-32,bytes,bytes.Length-16,16);Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bytes));
        });
        Test("owner requires a current interaction grant and open menu even on replay",()=>{
            using(var o=FloatGameOwner.CreateNew(FileName(),New())){
                var v=o.OpenView(FloatViewRole.Interaction);var d=o.AttachDocument(v);o.SetHostPaused(false);var lease=o.GrantControl(v,d);var cmd=Buy(o.Snapshot.State);
                Refused(o.ExecuteUser(v,d,lease,cmd),"menu-required");o.SetMenuOpen(true);Refused(o.ExecuteHost(cmd),"host-command-not-allowed");
                Need(o.ExecuteUser(v,d,lease,cmd).Written);o.RevokeControl(lease);Refused(o.ExecuteUser(v,d,lease,cmd),"control-required");
            }
        });
        Test("known failure retries the same IDs and uncertain flush reopens exactly one kit",()=>{
            foreach(var point in new[]{FloatSaveFaultPoint.BeforeWrite,FloatSaveFaultPoint.AfterFlush}){
                string path=FileName();bool fail=false;BuyBottomKit c;
                using(var s=FloatGameStore.CreateNew(path,New(),p=>{if(fail&&p==point)throw new IOException("private");})){
                    c=Buy(s.State);fail=true;
                    if(point==FloatSaveFaultPoint.BeforeWrite){Throws<IOException>(()=>s.Execute(c));Need(s.State.OwnedBottomKit==null);fail=false;Need(s.Execute(c).Written);}
                    else Throws<FloatSaveUncertainException>(()=>s.Execute(c));
                }
                using(var s=FloatGameStore.OpenExisting(path))Need(s.Execute(c).IsReplay && s.State.OwnedBottomKit.Ids.SequenceEqual(c.Kit.Ids) && s.State.WalletMinor==5600);
            }
        });
        Test("type 23 freezes session, kit rig, line and leader plus revisions and reserves its true frame length",()=>{
            using(var store=FloatGameStore.CreateNew(FileName(),New())){
                Need(store.Execute(Buy(store.State)).Written);var cast=Bound(store.State);var bytes=FloatJournalCodec.EncodeCommand(cast);
                var again=(CastBottomKitWithEquipment)FloatJournalCodec.DecodeCommand(bytes);
                Need(bytes[7]==23 && again.Fingerprint==cast.Fingerprint && bytes.Length+76==FloatSaveBudget.BottomCastFrameBytes);
                for(int i=0;i<bytes.Length;i++)Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bytes.Take(i).ToArray()));
                Throws<InvalidDataException>(()=>FloatJournalCodec.EncodeCommand(cast,2));
                int need=FloatSaveBudget.BottomCastFrameBytes+FloatSaveBudget.MaximumEndingBytes;
                var min=new FloatSaveBudget(3,9997,FloatGameStore.MaximumFileBytes-need);
                var shortByOne=new FloatSaveBudget(3,9997,FloatGameStore.MaximumFileBytes-need+1);
                Need(min.CanStartBottomCast && !shortByOne.CanStartBottomCast && shortByOne.CanStartCast);
            }
        });
        Test("incorrect kit binding or line budget leaves the private journal unchanged",()=>{
            using(var store=FloatGameStore.CreateNew(FileName(),New())){
                Need(store.Execute(Buy(store.State)).Written);var s=store.State;
                foreach(var bad in new[]{Bound(s,rig:s.Rig.Id),Bound(s,line:s.Rig.LineId),Bound(s,leader:G()),Bound(s,revision:2)})
                    Refused(store.Execute(bad),"bottom-kit-binding-stale");
                Refused(store.Execute(Bound(s,span:50001)),"line-insufficient");
                Need(store.State==s && s.Sequence==1 && s.BaitCount==12 && s.Stamina==100);
            }
        });
        Test("the shared owner preparation explicitly selects bottom after the float reel was removed",()=>{
            using(var o=FloatGameOwner.CreateNew(FileName(),New())){
                var v=o.OpenView(FloatViewRole.Interaction);var d=o.AttachDocument(v);o.SetHostPaused(false);var grant=o.GrantControl(v,d);
                var target=new FloatCastTarget(0,0,18000,2,18500);var rules=new FloatCastPreparationRules(1600,150);
                var bottom=new FloatCastPreparation(o,rules,t=>t==target,true);var standard=new FloatCastPreparation(o,rules,t=>t==target);
                Need(bottom.Begin(v,d,grant,target).Code=="bottom-kit-not-owned");
                o.SetMenuOpen(true);Need(o.ExecuteUser(v,d,grant,Buy(o.Snapshot.State)).Written);var s=o.Snapshot.State;
                Need(o.ExecuteUser(v,d,grant,new ReplaceFloatSlot(s.ProfileId,s.BranchId,G(),G(),FloatEquipmentSlot.Reel,null,s.Sequence,s.Rig.Revision,
                    s.RulesVersion,s.Equipment.InventoryRevision,s.Equipment.RulesVersion)).Written);o.SetMenuOpen(false);
                Need(!standard.Begin(v,d,grant,target).Accepted && o.Snapshot.State.Rig.LineId==null);
                var first=bottom.Begin(v,d,grant,target);Need(first.Accepted && bottom.Cancel(first.Gesture).Accepted && o.Snapshot.State.BaitCount==12);
                var charging=bottom.Begin(v,d,grant,target);Need(charging.Accepted && bottom.Release(charging.Gesture).Accepted);
                Need(bottom.CommitPrepared(charging.Gesture).ShouldStartFlight);s=o.Snapshot.State;
                Need(s.Session.IsBottom && s.Session.BottomKit==s.OwnedBottomKit && s.Session.RigRevision==1 && s.Rig.Revision==2 &&
                    s.BaitCount==11 && s.Stamina==98 && s.Session.RequiredLineMm==18500);
                Need(o.Shutdown().Confirmed);
            }
        });
        Test("a bottom session refuses float line loss, blocks second rods and supports ordinary recall",()=>{
            using(var store=FloatGameStore.CreateNew(FileName(),New())){
                Need(store.Execute(Buy(store.State)).Written);var cast=Bound(store.State);Need(store.Execute(cast).Written && store.Execute(cast).IsReplay);
                var s=store.State;Need(s.Session.IsBottom);Refused(store.Execute(Cast(s)),"session-busy");Refused(store.Execute(Bound(s)),"session-busy");
                var line=s.Equipment.Spool(s.Equipment.Equipped(FloatEquipmentSlot.Reel).Id);
                Refused(store.Execute(new HostFloatTerminalLoss(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,G(),line.Id,line.Revision,"terminal",15000,0,
                    s.Sequence,s.Rig.Revision,s.RulesVersion,s.Equipment.InventoryRevision,s.Equipment.RulesVersion,1)),"bottom-loss-unsupported");
                Need(store.Execute(new RecallFloat(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,s.Rig.Revision,s.RulesVersion)).Written);
                s=store.State;Need(s.Session==null && s.Creel.Count==0 && s.BaitCount==11 && s.Equipment.RemainingLineMm==50000 &&
                    s.OwnedBottomKit.Equipment.RemainingLineMm==50000);
            }
        });
        Test("an active bottom cast reopens and startup recovery keeps the prior expenditure exactly once",()=>{
            string path=FileName();Guid session;
            using(var store=FloatGameStore.CreateNew(path,New())){
                Need(store.Execute(Buy(store.State)).Written);var c=Bound(store.State);session=c.SessionId;Need(store.Execute(c).Written);
            }
            using(var store=FloatGameStore.OpenExisting(path)){
                Need(store.State.Session.Id==session && store.State.Session.IsBottom && store.State.Session.BottomKit==store.State.OwnedBottomKit);
                Need(FloatGameLifecycle.Startup(store).Confirmed && FloatGameLifecycle.Startup(store).NoActiveSession && store.State.Sequence==3 &&
                    store.State.BaitCount==11 && store.State.Stamina==98 && store.State.Creel.Count==0);
            }
        });
        Test("bottom catch and full creel retain the same fish and mode until an explicit release after reopening",()=>{
            string path=FileName();Guid pending;
            using(var store=FloatGameStore.CreateNew(path,New())){
                Need(store.Execute(Buy(store.State)).Written);Need(store.Execute(Bound(store.State)).Written);Land(store);
                Need(store.State.Session==null && store.State.Creel.Count==1);Need(store.Execute(Bound(store.State)).Written);Land(store);
                Need(store.State.Session.CapacityBlocked && store.State.Session.IsBottom);pending=store.State.Session.PendingFish.Id;
            }
            using(var store=FloatGameStore.OpenExisting(path)){
                var s=store.State;Need(FloatGameLifecycle.Startup(store).PendingResultPreserved && s.Session.IsBottom && s.Session.PendingFish.Id==pending);
                Need(store.Execute(new ReleaseCurrentFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,s.Rig.Revision,s.RulesVersion)).Written);
                Need(store.State.Session==null && store.State.BaitCount==10 && store.State.Stamina==96 && store.State.Creel.Count==1);
            }
        });
        Console.WriteLine("RESULT passed="+passed);return 0;
    }
}
