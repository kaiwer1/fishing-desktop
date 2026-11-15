using System;
using System.IO;
using System.Linq;
using FishingDesktop.GameCore;

static class LureOwnedKitTests
{
    static string root;
    static int failures;
    static Guid G(){return Guid.NewGuid();}
    static string PathFor(){return Path.Combine(root,G().ToString("N")+".fltj");}
    static OwnedLureKit Kit(Guid? hook=null){return new OwnedLureKit(G(),G(),G(),G(),G(),hook??G());}
    static FloatGameState New(int bait=0,int stamina=100,int balance=12000){
        var s=BottomOwnedKitTests.New(balance:balance);
        var rules=new FloatGameRules(2,s.Rig.DefinitionKey,s.BaitDefinitionKey,2,100,1,1000000,FloatFishingAtlas.SpeciesRules);
        return FloatGameState.CreateNewWithEquipment(s.ProfileId,s.BranchId,balance,stamina,s.BaitStackId,s.BaitDefinitionKey,
            bait,s.Equipment,rules,s.CommerceCatalog,s.FoodStackId);
    }
    static BuyLureKit Buy(FloatGameState s,OwnedLureKit k=null){
        return new BuyLureKit(s.ProfileId,s.BranchId,G(),G(),k??Kit(),s.Sequence,s.Rig.Revision,s.RulesVersion);
    }
    static CastLureWithEquipment Cast(FloatGameState s){return CastLureWithEquipment.Create(s,25000);}
    static RecallFloat Recall(FloatGameState s){return new RecallFloat(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static HostFloatFish Fish(FloatGameState s){
        return new HostFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,G(),G(),"spotted-seabass",500000,s.Sequence,s.Rig.Revision,s.RulesVersion);
    }
    static void Need(bool pass,string why){if(!pass)throw new Exception(why);}
    static void Written(FloatCommitResult r){Need(r.Written&&r.Committed&&!r.IsReplay,"expected write: "+r.Code);}
    static void Rejected(FloatCommitResult r,string code){Need(!r.Written&&!r.Committed&&!r.IsReplay&&r.Code==code,"expected "+code+" got "+r.Code);}
    static void Throws<T>(Action a)where T:Exception{try{a();}catch(T){return;}throw new Exception("expected "+typeof(T).Name);}
    static void Test(string label,Action a){try{a();Console.WriteLine("PASS "+label);}catch(Exception e){failures++;Console.WriteLine("FAIL "+label+": "+e);}}
    public static int Main(string[] args){
        root=args[0];
        Test("legacy profiles start without a lure and unsupported profiles cannot buy one",()=>{
            foreach(var state in new[]{BottomOwnedKitTests.New(1),BottomOwnedKitTests.New(2),BottomOwnedKitTests.New(3,1)}){
                var r=FloatGameReducer.Reduce(state,Buy(state),state.Rules);
                Need(state.OwnedLureKit==null&&!r.HasCandidate&&r.Code=="lure-kit-unavailable","legacy ownership or eligibility");
            }
            var initial=New();Need(initial.OwnedLureKit==null&&FloatJournalCodec.DecodeGenesis(FloatJournalCodec.EncodeGenesis(initial)).OwnedLureKit==null,"genesis granted gear");
        });
        Test("one purchase costs 56 silver and six distinct identities survive re-open without touching the float rig",()=>{
            var path=PathFor();var initial=New();var buy=Buy(initial);byte[] prefix,gear=FloatEquipmentCodec.Encode(initial.Equipment);
            using(var st=FloatGameStore.CreateNew(path,initial)){}
            prefix=File.ReadAllBytes(path);
            using(var st=FloatGameStore.OpenExisting(path)){Written(st.Execute(buy));}
            Need(File.ReadAllBytes(path).Take(prefix.Length).SequenceEqual(prefix),"old prefix changed");
            using(var st=FloatGameStore.OpenExisting(path)){
                var s=st.State;Need(s.WalletMinor==6400&&s.Sequence==1&&s.OwnedLureKit.Ids.SequenceEqual(buy.Kit.Ids)&&
                    s.OwnedLureKit.Ids.Distinct().Count()==6&&s.OwnedLureKit.LineLengthMm==50000&&s.OwnedBottomKits.Count==0,"kit replay");
                Need(st.Execute(buy).IsReplay&&FloatEquipmentCodec.Encode(s.Equipment).SequenceEqual(gear),"duplicate or float rig changed");
                Rejected(st.Execute(Buy(s)),"lure-kit-already-owned");
                Rejected(st.Execute(new BuyLureKit(s.ProfileId,s.BranchId,buy.RequestId,buy.EventId,Kit(),0,1,2)),"request-conflict");
            }
        });
        Test("insufficient cash and identity collisions never mint a kit or an extra hook",()=>{
            using(var st=FloatGameStore.CreateNew(PathFor(),New(balance:5599)))Rejected(st.Execute(Buy(st.State)),"wallet-insufficient");
            using(var st=FloatGameStore.CreateNew(PathFor(),New())){
                var s=st.State;var id=G();
                Throws<ArgumentException>(()=>new OwnedLureKit(G(),G(),G(),G(),id,id));
                Rejected(st.Execute(Buy(s,Kit(s.Rig.Id))),"asset-identity-used");
                var bill=Buy(s);Rejected(st.Execute(new BuyLureKit(s.ProfileId,s.BranchId,bill.RequestId,bill.EventId,Kit(bill.EventId),0,1,2)),"asset-identity-used");
                Written(st.Execute(bill));s=st.State;
                var cmd=new ChangeFloatClothing(s.ProfileId,s.BranchId,s.OwnedLureKit.HookId,G(),FloatClothingAction.Buy,"moss-jacket",s.Sequence,1,2);
                Rejected(st.Execute(cmd),"request-identity-used");
            }
        });
        Test("zero worms and repeated casts consume two stamina per paid release while retaining the same spoon, line and integral hook",()=>{
            using(var st=FloatGameStore.CreateNew(PathFor(),New())){
                Written(st.Execute(Buy(st.State)));var owned=st.State.OwnedLureKit;
                for(int i=0;i<3;i++){
                    var cast=Cast(st.State);Written(st.Execute(cast));var s=st.State;
                    Need(s.Session.IsLure&&!s.Session.IsBottom&&s.Session.LureKit==owned&&s.Session.Id==cast.SessionId&&
                        s.Stamina==98-2*i&&s.BaitCount==0&&s.Session.RequiredLineMm==25000&&s.Receipts.Last().BaitAfter==s.Receipts.Last().BaitBefore,"cast cost");
                    LureRetrieveRig rig;Need(LureOwnedEquipment.Bind(s,cast.SessionId,out rig)==null&&rig.LureId==owned.LureId&&rig.LineId==owned.LineId&&
                        rig.RodId==owned.RodId&&rig.ReelId==owned.ReelId&&rig.TotalLineMm==50000&&LureRetrieveRig.LureMassMg==12000,"physical kit binding");
                    Need(LureOwnedEquipment.Bind(s,G(),out rig)=="session-not-current"&&st.Execute(cast).IsReplay,"stale session bound or duplicate payment");
                    Written(st.Execute(Recall(s)));
                }
                Need(st.State.Stamina==94&&st.State.Session==null&&st.State.OwnedLureKit.Ids.SequenceEqual(owned.Ids)&&st.State.Creel.Count==0,"gear consumed on retrieve");
            }
        });
        Test("insufficient stamina, insufficient line, and a different spoon, hook or revision cannot produce a paid release",()=>{
            using(var st=FloatGameStore.CreateNew(PathFor(),New(stamina:1))){Written(st.Execute(Buy(st.State)));Rejected(st.Execute(Cast(st.State)),"stamina-insufficient");}
            using(var st=FloatGameStore.CreateNew(PathFor(),New())){
                Written(st.Execute(Buy(st.State)));var s=st.State;var k=s.OwnedLureKit;
                Rejected(st.Execute(CastLureWithEquipment.Create(s,50001)),"line-insufficient");
                for(int i=0;i<3;i++){
                    var wrong=new CastLureWithEquipment(s.ProfileId,s.BranchId,G(),G(),G(),k.RigId,k.LineId,i==0?G():k.LureId,i==1?G():k.HookId,
                        1,1,i==2?2:1,20000,s.Sequence,1,2);
                    Rejected(st.Execute(wrong),"lure-kit-binding-stale");
                }
                Need(st.State.Sequence==1&&st.State.Stamina==100,"rejected release charged");
            }
        });
        Test("old float, ordinary bottom and bank sessions remain exclusive with lure fishing",()=>{
            using(var st=FloatGameStore.CreateNew(PathFor(),New(bait:5,balance:30000))){
                var s=st.State;Written(st.Execute(Buy(s)));s=st.State;
                var kit=new BottomOwnedKit(G(),G(),G(),G(),G(),G(),G());Written(st.Execute(new BuyBottomKit(s.ProfileId,s.BranchId,G(),G(),kit,s.Sequence,1,2)));s=st.State;
                Written(st.Execute(new BuyBankRodStand(s.ProfileId,s.BranchId,G(),G(),G(),s.Sequence,1,2)));Written(st.Execute(Cast(st.State)));s=st.State;
                Rejected(st.Execute(CastBankBottomRod.Create(s,kit.Equipment.RigId,12000)),"session-busy");
                Rejected(st.Execute(CastBottomKitWithEquipment.Create(s,12000)),"session-busy");
                Rejected(st.Execute(new CastFloatWithEquipment(s.ProfileId,s.BranchId,G(),G(),G(),12000,s.Sequence,1,2,s.Equipment.InventoryRevision,s.Equipment.RulesVersion)),"session-busy");
                Written(st.Execute(Recall(s)));Written(st.Execute(CastBankBottomRod.Create(st.State,kit.Equipment.RigId,12000)));
                Rejected(st.Execute(Cast(st.State)),"session-busy");Need(st.State.OwnedLureKit.LureId!=kit.LeaderId,"shared gear id");
            }
        });
        Test("physical float line loss cannot mutate the spoon session or the separate float equipment",()=>{
            using(var st=FloatGameStore.CreateNew(PathFor(),New())){
                Written(st.Execute(Buy(st.State)));Written(st.Execute(Cast(st.State)));var s=st.State;
                var line=s.Equipment.Spool(s.Equipment.Equipped(FloatEquipmentSlot.Reel).Id);
                Rejected(st.Execute(new HostFloatTerminalLoss(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,G(),line.Id,line.Revision,"terminal",15000,0,
                    s.Sequence,1,2,s.Equipment.InventoryRevision,s.Equipment.RulesVersion,1)),"lure-loss-unsupported");
                Need(st.State==s&&line.RemainingLineMm==50000,"loss escaped method boundary");
            }
        });
        Test("catch, full-creel pending, explicit release and shared sale survive journal replay",()=>{
            string path=PathFor();Guid kept,pending,spoon;long price;
            using(var st=FloatGameStore.CreateNew(path,New())){
                Written(st.Execute(Buy(st.State)));spoon=st.State.OwnedLureKit.LureId;Written(st.Execute(Cast(st.State)));Written(st.Execute(Fish(st.State)));
                kept=st.State.Creel.Single().Id;price=st.State.Creel[0].SaleValueMinor;
                Written(st.Execute(Cast(st.State)));var land=Fish(st.State);pending=land.FishId;var r=st.Execute(land);Written(r);
                Need(!r.OperationSucceeded&&st.State.Session.IsLure&&st.State.Session.CapacityBlocked&&st.Execute(land).IsReplay,"pending mode lost");
            }
            using(var st=FloatGameStore.OpenExisting(path)){
                var s=st.State;Need(FloatGameLifecycle.Startup(st).PendingResultPreserved&&s.Session.PendingFish.Id==pending&&s.Session.LureKit.LureId==spoon,"lost pending outcome");
                Written(st.Execute(new ReleaseCurrentFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,1,2)));s=st.State;
                Written(st.Execute(new SellFloatFish(s.ProfileId,s.BranchId,G(),G(),new[]{kept},s.Sequence,1,2)));
                Need(st.State.Creel.Count==0&&st.State.WalletMinor==6400+price&&st.State.OwnedLureKit.LureId==spoon&&st.State.Stamina==96&&st.State.BaitCount==0,"wrong shared balances");
            }
        });
        Test("startup retrieves an interrupted spoon cast once with no refund or automatic fish",()=>{
            string path=PathFor();Guid session;
            using(var st=FloatGameStore.CreateNew(path,New())){Written(st.Execute(Buy(st.State)));var c=Cast(st.State);session=c.SessionId;Written(st.Execute(c));}
            using(var o=FloatGameOwner.OpenExisting(path)){
                var s=o.Snapshot.State;Need(s.Sequence==3&&s.Session==null&&s.Stamina==98&&s.BaitCount==0&&s.Creel.Count==0&&
                    s.Receipts.Last().SessionId==session&&s.Receipts.Last().RecoveryReason==FloatRecoveryReason.StartupInterrupted,"wrong startup");
            }
            using(var o=FloatGameOwner.OpenExisting(path))Need(o.Snapshot.State.Sequence==3,"second recovery");
        });
        Test("purchase and cast frames reject legacy schemas, truncation, trailing bytes and impossible identities",()=>{
            using(var st=FloatGameStore.CreateNew(PathFor(),New())){
                var buy=Buy(st.State);Written(st.Execute(buy));var cast=Cast(st.State);
                foreach(var command in new FloatCommand[]{buy,cast}){
                    var b=FloatJournalCodec.EncodeCommand(command);Need(b[7]==(command==buy?28:29)&&FloatJournalCodec.DecodeCommand(b).Fingerprint==command.Fingerprint,"typed codec");
                    Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(b.Take(b.Length-1).ToArray()));
                    Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(b.Concat(new byte[]{0}).ToArray()));
                    foreach(int schema in new[]{1,2})Throws<InvalidDataException>(()=>FloatJournalCodec.EncodeCommand(command,schema));
                }
                int frame=FloatJournalCodec.EncodeCommand(cast).Length+76;
                Need(frame==284&&new FloatSaveBudget(3,9997,FloatGameStore.MaximumFileBytes-frame-482).CanStartLureCast&&
                    !new FloatSaveBudget(3,9997,FloatGameStore.MaximumFileBytes-frame-481).CanStartLureCast,"journal byte reserve");
            }
        });
        Test("known purchase write failure and uncertain cast acknowledgement never duplicate the kit or double debit",()=>{
            string path=PathFor();using(var st=FloatGameStore.CreateNew(path,New())){}
            bool fail=true;CastLureWithEquipment cast;
            using(var st=FloatGameStore.OpenExisting(path,p=>{if(fail&&p==FloatSaveFaultPoint.BeforeWrite)throw new IOException("test before write");})){
                var b=Buy(st.State);Throws<IOException>(()=>st.Execute(b));Need(st.State.OwnedLureKit==null&&st.State.WalletMinor==12000,"unconfirmed kit published");
                fail=false;Written(st.Execute(b));cast=Cast(st.State);
            }
            using(var st=FloatGameStore.OpenExisting(path,p=>{if(p==FloatSaveFaultPoint.AfterFlush)throw new IOException("test after flush");}))
                Throws<FloatSaveUncertainException>(()=>st.Execute(cast));
            using(var st=FloatGameStore.OpenExisting(path))Need(st.Execute(cast).IsReplay&&st.State.Sequence==2&&st.State.Stamina==98&&st.State.BaitCount==0,"uncertain cast charged twice");
        });
        Test("normal cast preparation supports zero bait, cancels without cost and commits only the selected paid kit",()=>{
            using(var o=FloatGameOwner.CreateNew(PathFor(),New())){
                var view=o.OpenView(FloatViewRole.Interaction);var doc=o.AttachDocument(view);o.SetHostPaused(false);var lease=o.GrantControl(view,doc);
                var buy=Buy(o.Snapshot.State);Rejected(o.ExecuteUser(view,doc,lease,buy),"menu-required");o.SetMenuOpen(true);Written(o.ExecuteUser(view,doc,lease,buy));o.SetMenuOpen(false);
                var t=new FloatCastTarget(0,0,15000,2,16000);var prep=new FloatCastPreparation(o,new FloatCastPreparationRules(1600,150),x=>x==t,lureRig:true);
                var first=prep.Begin(view,doc,lease,t);Need(first.Accepted&&prep.Cancel(first.Gesture).Accepted&&o.Snapshot.State.Stamina==100,"begin or cancel changed resources");
                var second=prep.Begin(view,doc,lease,t);Need(second.Accepted&&prep.Release(second.Gesture).Accepted&&prep.CommitPrepared(second.Gesture).ShouldStartFlight,"release failed");
                Need(o.Snapshot.State.Session.IsLure&&o.Snapshot.State.BaitCount==0&&o.Snapshot.State.Stamina==98&&o.Shutdown().Confirmed,"normal entry selected wrong method");
            }
        });
        return failures==0?0:1;
    }
}
