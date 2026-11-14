using System;
using System.IO;
using System.Linq;
using FishingDesktop.GameCore;

static class FloatClothingTests
{
    const string Moss="moss-jacket",Coast="coast-jacket";
    static int passed,failed;
    static string directory;
    static Guid G(){return Guid.NewGuid();}
    static string PathFor(){return Path.Combine(directory,G().ToString("N")+".fltj");}
    static void Need(bool value,string why){if(!value)throw new Exception(why);}
    static void Test(string name,Action body)
    {
        try{body();passed++;Console.WriteLine("PASS "+name);}
        catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error);}
    }
    static void Throws<T>(Action action)where T:Exception
    {try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    static FloatGameState New(int schema=3,long version=2,long money=12000)
    {
        var rig=new FixedFloatRig(G(),1,"starter",G(),G(),G(),50000,G(),G(),G());
        var rules=new FloatGameRules(version,"starter","worm",2,100,1,1000000,new[]{new FloatSpeciesRule("carp",100000,2000000,120)});
        var shop=new FloatCommerceCatalog(1,"worm","net","bread",2400,5,20,20,20,1000,1000,100);
        if(schema==1)return FloatGameState.CreateNew(G(),G(),money,100,G(),"worm",12,rig,rules);
        var state=FloatGameState.CreateNewWithCommerce(G(),G(),money,100,G(),"worm",12,rig,rules,shop,G());if(schema==2)return state;
        var equipment=new FloatEquipmentCatalog(1,"worm",new[]{
            new FloatEquipmentDefinition("rod",FloatEquipmentKind.Rod,150000,0,1000,25000),new FloatEquipmentDefinition("reel",FloatEquipmentKind.Reel,200000),
            new FloatEquipmentDefinition("line",FloatEquipmentKind.Line,0,180),new FloatEquipmentDefinition("hook",FloatEquipmentKind.Hook,200),
            new FloatEquipmentDefinition("bobber",FloatEquipmentKind.Bobber,3000,0,2000,3000),new FloatEquipmentDefinition("sinker",FloatEquipmentKind.Sinker,2500)},
            new[]{new FloatEquipmentPairRule("rod","reel",true)},new[]{new FloatEquipmentSpoolRule("reel","line",180,50000)},
            new[]{new FloatEquipmentPairRule("hook","worm",true)},new[]{new FloatEquipmentLossRule("terminal",new[]{FloatEquipmentSlot.Hook})});
        var map=new FloatEquipmentMigrationPolicy(1,"starter",equipment,"rod","reel","line","hook","bobber","sinker");
        return FloatGameState.CreateNewWithEquipment(state.ProfileId,state.BranchId,money,100,state.BaitStackId,"worm",12,map.Map(state),rules,shop,state.FoodStackId);
    }
    static ChangeFloatClothing Change(FloatGameState state,FloatClothingAction action,string item)
    {return new ChangeFloatClothing(state.ProfileId,state.BranchId,G(),G(),action,item,state.Sequence,state.Rig.Revision,state.RulesVersion);}
    static void Reject(FloatCommitResult result,string code)
    {Need(result.Code==code && !result.Committed && !result.Written && result.Receipt==null,"Expected "+code+", received "+result.Code);}
    static FloatCommitResult Apply(FloatGameStore store,FloatCommand command)
    {var result=store.Execute(command);Need(result.Written && result.Committed && !result.IsReplay,"Write refused: "+result.Code);return result;}
    static void Cast(FloatGameStore store)
    {
        var s=store.State;Apply(store,new CastFloatWithEquipment(s.ProfileId,s.BranchId,G(),G(),G(),15000,s.Sequence,s.Rig.Revision,s.RulesVersion,s.Equipment.InventoryRevision,s.Equipment.RulesVersion));
    }
    static void Land(FloatGameStore store)
    {
        var s=store.State;Apply(store,new HostFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,G(),G(),"carp",500000,s.Sequence,s.Rig.Revision,s.RulesVersion));
    }
    static void Wardrobe(FloatGameState s,long money,string worn,params string[] bought)
    {
        Need(s.WalletMinor==money && s.WornClothingId==worn && s.OwnedClothingIds.SequenceEqual(bought.OrderBy(x=>x,StringComparer.Ordinal)),
            "Money, bought jackets or selected jacket disagree.");
    }

    static int Main(string[] args)
    {
        directory=args[0];
        Test("new wardrobe starts empty with two fixed prices and old rule or schema versions remain disabled",()=>{
            var fresh=New();Wardrobe(fresh,12000,null);Need(FloatClothingCatalog.Enabled(fresh) && FloatClothingCatalog.Items.Count==2 &&
                FloatClothingCatalog.Find(Moss).PriceMinor==1800 && FloatClothingCatalog.Find(Coast).PriceMinor==2600,"Catalogue mismatch.");
            foreach(var old in new[]{New(1,2),New(2,2),New(3,1),New(3,3)}){
                Need(!FloatClothingCatalog.Enabled(old),"Old or unknown rules enabled a wardrobe.");
                var result=FloatGameReducer.Reduce(old,Change(old,FloatClothingAction.Buy,Moss),old.Rules);
                Need(!result.HasCandidate && result.Code=="clothing-unavailable","Unsupported profile gained clothing.");
            }
        });
        Test("actual journal buys debit the exact stored price once and preserve all equipment, bait and stamina",()=>{
            using(var s=FloatGameStore.CreateNew(PathFor(),New())){
                var cmd=Change(s.State,FloatClothingAction.Buy,Moss);var result=Apply(s,cmd);
                Need(result.Code=="clothing-purchase-candidate" && result.Receipt.ClothingItemId==Moss && result.Receipt.Outcome==FloatOutcome.ClothingPurchased &&
                    result.Receipt.WalletBefore-result.Receipt.WalletAfter==1800,"Wrong receipt.");
                Wardrobe(s.State,10200,null,Moss);
                var replay=s.Execute(cmd);Need(replay.IsReplay && !replay.Written && s.State.Sequence==1,"Duplicate buy charged money.");
                Reject(s.Execute(Change(s.State,FloatClothingAction.Buy,Moss)),"clothing-already-owned");
                Need(s.State.Equipment.Instances.Count==6 && s.State.Equipment.RemainingLineMm==50000 && s.State.BaitCount==12 && s.State.Stamina==100,"Fishing resources changed.");
            }
        });
        Test("unaffordable or unknown jackets leave file length unchanged and exact balance is spendable",()=>{
            string path=PathFor();using(var s=FloatGameStore.CreateNew(path,New(money:1800))){
                long n=new FileInfo(path).Length;
                Reject(s.Execute(Change(s.State,FloatClothingAction.Buy,Coast)),"wallet-insufficient");
                Reject(s.Execute(Change(s.State,FloatClothingAction.Buy,"missing")),"clothing-item-unknown");
                Need(new FileInfo(path).Length==n,"Failed purchase appended bytes.");
                Apply(s,Change(s.State,FloatClothingAction.Buy,Moss));Wardrobe(s.State,0,null,Moss);
            }
        });
        Test("wearing requires ownership and removing requires the jacket actually being worn",()=>{
            using(var s=FloatGameStore.CreateNew(PathFor(),New())){
                Reject(s.Execute(Change(s.State,FloatClothingAction.Wear,Moss)),"clothing-not-owned");
                Apply(s,Change(s.State,FloatClothingAction.Buy,Moss));Apply(s,Change(s.State,FloatClothingAction.Wear,Moss));
                Wardrobe(s.State,10200,Moss,Moss);
                Reject(s.Execute(Change(s.State,FloatClothingAction.Wear,Moss)),"clothing-unchanged");
                Reject(s.Execute(Change(s.State,FloatClothingAction.Remove,Coast)),"clothing-not-worn");
                var remove=Apply(s,Change(s.State,FloatClothingAction.Remove,Moss));Need(remove.Code=="clothing-remove-candidate","Wrong removal.");
                Wardrobe(s.State,10200,null,Moss);
                Reject(s.Execute(Change(s.State,FloatClothingAction.Remove,Moss)),"clothing-not-worn");
            }
        });
        Test("switching directly between two purchased jackets replays on reopening the same journal",()=>{
            string path=PathFor();ChangeFloatClothing original;byte[] prefix;
            using(var s=FloatGameStore.CreateNew(path,New())){
                prefix=FloatJournalCodec.EncodeGenesis(s.State);
                Apply(s,Change(s.State,FloatClothingAction.Buy,Moss));Apply(s,Change(s.State,FloatClothingAction.Wear,Moss));
                Apply(s,Change(s.State,FloatClothingAction.Buy,Coast));Wardrobe(s.State,7600,Moss,Moss,Coast);
                original=Change(s.State,FloatClothingAction.Wear,Coast);Apply(s,original);
            }
            byte[] bytes=File.ReadAllBytes(path);
            Need(bytes.Skip(52).Take(prefix.Length).SequenceEqual(prefix),"Original genesis payload changed.");
            using(var s=FloatGameStore.OpenExisting(path)){
                Wardrobe(s.State,7600,Coast,Moss,Coast);
                var repeat=s.Execute(original);Need(repeat.Code=="clothing-wear-candidate" && repeat.IsReplay && !repeat.Written && s.State.Sequence==4,"Wear replay appended.");
                Apply(s,Change(s.State,FloatClothingAction.Remove,Coast));
            }
            using(var s=FloatGameStore.OpenExisting(path))Wardrobe(s.State,7600,null,Moss,Coast);
        });
        Test("stale purchases and a reused request with a different operation never alter balance",()=>{
            using(var s=FloatGameStore.CreateNew(PathFor(),New())){
                var first=Change(s.State,FloatClothingAction.Buy,Moss);var stale=Change(s.State,FloatClothingAction.Buy,Coast);
                Apply(s,first);Reject(s.Execute(stale),"version-conflict");
                var altered=new ChangeFloatClothing(first.ProfileId,first.BranchId,first.RequestId,first.EventId,FloatClothingAction.Wear,Moss,first.ExpectedSequence,first.ExpectedRigRevision,first.ExpectedRulesVersion);
                Reject(s.Execute(altered),"request-conflict");Wardrobe(s.State,10200,null,Moss);
            }
        });
        Test("active and pending full-creel sessions reject every wardrobe action",()=>{
            using(var s=FloatGameStore.CreateNew(PathFor(),New())){
                Cast(s);foreach(FloatClothingAction action in Enum.GetValues(typeof(FloatClothingAction)))Reject(s.Execute(Change(s.State,action,Moss)),"session-busy");
                Land(s);Cast(s);Land(s);Need(s.State.Session.PendingFish!=null,"Missing full-creel fixture.");
                foreach(FloatClothingAction action in Enum.GetValues(typeof(FloatClothingAction)))Reject(s.Execute(Change(s.State,action,Moss)),"session-busy");
                Wardrobe(s.State,12000,null);
            }
        });
        Test("the owner requires current interactive authority and an open menu, including duplicate requests",()=>{
            using(var o=FloatGameOwner.CreateNew(PathFor(),New())){
                var v=o.OpenView(FloatViewRole.Interaction);var d=o.AttachDocument(v);o.SetHostPaused(false);var grant=o.GrantControl(v,d);
                var read=o.OpenView(FloatViewRole.Observer);var rd=o.AttachDocument(read);
                var cmd=Change(o.Snapshot.State,FloatClothingAction.Buy,Moss);
                Reject(o.ExecuteUser(v,d,grant,cmd),"menu-required");o.SetMenuOpen(true);
                Reject(o.ExecuteUser(read,rd,grant,cmd),"control-required");Reject(o.ExecuteHost(cmd),"host-command-not-allowed");
                Need(o.ExecuteUser(v,d,grant,cmd).Written,"Authorized buy failed.");
                o.SetMenuOpen(false);Reject(o.ExecuteUser(v,d,grant,cmd),"menu-required");
                o.SetMenuOpen(true);o.RevokeControl(grant);Reject(o.ExecuteUser(v,d,grant,cmd),"control-required");
                Wardrobe(o.Snapshot.State,10200,null,Moss);
            }
        });
        Test("all three type 21 actions round-trip with strict schemas, operation range and complete payload",()=>{
            var s=New();
            foreach(FloatClothingAction action in Enum.GetValues(typeof(FloatClothingAction))){
                var c=Change(s,action,Moss);byte[] bytes=FloatJournalCodec.EncodeCommand(c);
                var decoded=(ChangeFloatClothing)FloatJournalCodec.DecodeCommand(bytes);
                Need(bytes[7]==21 && decoded.Fingerprint==c.Fingerprint && decoded.RequestId==c.RequestId,"Codec changed command identity.");
                foreach(int version in new[]{1,2})Throws<InvalidDataException>(()=>FloatJournalCodec.EncodeCommand(c,version));
                for(int i=0;i<bytes.Length;i++)Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bytes.Take(i).ToArray()));
                Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bytes.Concat(new byte[]{0}).ToArray()));
                bytes[112]=5;Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bytes));
            }
        });
        Test("known pre-write failure is retryable with exactly the same jacket request",()=>{
            bool fail=false;string path=PathFor();
            using(var s=FloatGameStore.CreateNew(path,New(),p=>{if(fail&&p==FloatSaveFaultPoint.BeforeWrite)throw new IOException("private write fault");})){
                var buy=Change(s.State,FloatClothingAction.Buy,Moss);long n=new FileInfo(path).Length;fail=true;
                Throws<IOException>(()=>s.Execute(buy));Wardrobe(s.State,12000,null);Need(new FileInfo(path).Length==n,"Known failure wrote bytes.");
                fail=false;Apply(s,buy);Wardrobe(s.State,10200,null,Moss);
            }
        });
        Test("uncertain post-flush purchase reopens as one debit with one owned jacket",()=>{
            bool fail=false;string path=PathFor();ChangeFloatClothing buy;
            using(var s=FloatGameStore.CreateNew(path,New(),p=>{if(fail&&p==FloatSaveFaultPoint.AfterFlush)throw new IOException("private post-flush fault");})){
                buy=Change(s.State,FloatClothingAction.Buy,Coast);fail=true;Throws<FloatSaveUncertainException>(()=>s.Execute(buy));
            }
            using(var s=FloatGameStore.OpenExisting(path)){
                Wardrobe(s.State,9400,null,Coast);var same=s.Execute(buy);Need(same.IsReplay && !same.Written && s.State.Sequence==1,"Reopening duplicated an uncertain purchase.");
            }
        });
        Test("a partial wardrobe transaction is preserved for inspection and never silently repaired",()=>{
            bool fail=false;string path=PathFor();
            using(var s=FloatGameStore.CreateNew(path,New(),p=>{if(fail&&p==FloatSaveFaultPoint.AfterPrefix)throw new IOException("private partial save");})){
                fail=true;Throws<FloatSaveUncertainException>(()=>s.Execute(Change(s.State,FloatClothingAction.Buy,Moss)));
            }
            byte[] partial=File.ReadAllBytes(path);Throws<InvalidDataException>(()=>{using(var s=FloatGameStore.OpenExisting(path)){}});
            Need(partial.SequenceEqual(File.ReadAllBytes(path)),"Partial journal changed.");
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
}
