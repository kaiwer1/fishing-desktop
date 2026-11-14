using System;
using System.IO;
using System.Linq;
using FishingDesktop.GameCore;

static class BankFishingJournalTests
{
    static string root;
    static int failures;
    static Guid G(){return Guid.NewGuid();}
    static string PathFor(){return Path.Combine(root,G().ToString("N")+".fltj");}
    static void Need(bool pass,string message){if(!pass)throw new Exception(message);}
    static void Test(string label,Action action){
        try{action();Console.WriteLine("PASS "+label);}
        catch(Exception e){failures++;Console.WriteLine("FAIL "+label+": "+e);}
    }
    static void Written(FloatCommitResult r){Need(r.Written&&r.Committed&&!r.IsReplay,"expected write: "+r.Code);}
    static void Reject(FloatCommitResult r,string code){Need(!r.Committed&&!r.IsReplay&&!r.Written&&r.Code==code,"expected "+code+" got "+r.Code);}
    static void Throws<T>(Action a)where T:Exception{try{a();}catch(T){return;}throw new Exception("expected "+typeof(T).Name);}
    static BottomOwnedKit Kit(){return new BottomOwnedKit(G(),G(),G(),G(),G(),G(),G());}
    static FloatGameStore Fresh(string path,int baits=12,int stamina=100,bool stand=true){
        var template=BottomOwnedKitTests.New(balance:22000);
        var start=FloatGameState.CreateNewWithEquipment(template.ProfileId,template.BranchId,template.WalletMinor,stamina,
            template.BaitStackId,"worm",baits,template.Equipment,template.Rules,template.CommerceCatalog,template.FoodStackId);
        var store=FloatGameStore.CreateNew(path,start);
        try{
            Written(store.Execute(new BuyBottomKit(start.ProfileId,start.BranchId,G(),G(),Kit(),0,1,2)));
            for(int n=2;n<=3;n++)Written(store.Execute(new BuyAdditionalBottomKit(start.ProfileId,start.BranchId,G(),G(),n,Kit(),store.State.Sequence,1,2)));
            if(stand)Written(store.Execute(new BuyBankRodStand(start.ProfileId,start.BranchId,G(),G(),G(),store.State.Sequence,1,2)));
            return store;
        }catch{store.Dispose();throw;}
    }
    static Guid Rig(FloatGameState state,int index){return state.OwnedBottomKits[index].Equipment.RigId;}
    static CastBankBottomRod Cast(FloatGameState s,int index){return CastBankBottomRod.Create(s,Rig(s,index),15000+index);}
    static CastBankBottomRod Start(FloatGameStore store,int index){var cast=Cast(store.State,index);Written(store.Execute(cast));return cast;}
    static SettleBankBottomRod End(FloatGameState s,int index,BankRodEnding ending){
        var rig=Rig(s,index);return SettleBankBottomRod.Create(s,rig,s.BankSessionFor(rig).Id,ending);
    }
    static SettleBankBottomRod Fish(FloatGameState s,int index){
        var rig=Rig(s,index);return SettleBankBottomRod.Land(s,rig,s.BankSessionFor(rig).Id,G(),G(),"carp",500000,G());
    }
    static CastFloatWithEquipment FloatCast(FloatGameState s){
        return new CastFloatWithEquipment(s.ProfileId,s.BranchId,G(),G(),G(),15000,s.Sequence,1,2,s.Equipment.InventoryRevision,s.Equipment.RulesVersion);
    }
    static void Roundtrip(FloatCommand command,int type){
        var bytes=FloatJournalCodec.EncodeCommand(command);
        Need(bytes[7]==type&&FloatJournalCodec.DecodeCommand(bytes).Fingerprint==command.Fingerprint,"typed roundtrip");
        Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bytes.Take(bytes.Length-1).ToArray()));
        Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bytes.Concat(new byte[]{0}).ToArray()));
        foreach(int schema in new[]{1,2})Throws<InvalidDataException>(()=>FloatJournalCodec.EncodeCommand(command,schema));
    }
    public static int Main(string[] args){
        root=args[0];
        Test("three paid independent sessions share one supply stack and reopen in stable purchase order",()=>{
            string path=PathFor();var ids=new Guid[3];byte[] original;
            using(var store=Fresh(path)){
                original=FloatEquipmentCodec.Encode(store.State.Equipment);
                foreach(int index in new[]{2,0,1})ids[index]=Start(store,index).SessionId;
                var s=store.State;Need(s.Session==null&&s.Sequence==7&&s.BaitCount==9&&s.Stamina==94&&s.WalletMinor==4400,"one set of balances");
                Need(ids.SequenceEqual(s.BankSessions.Select(r=>r.Id)),"stable session order");
            }
            using(var store=FloatGameStore.OpenExisting(path)){
                var s=store.State;
                for(int i=0;i<3;i++){
                    var rod=s.BankSessionFor(Rig(s,i));Need(rod.Id==ids[i]&&rod.BottomKit==s.OwnedBottomKits[i]&&rod.RequiredLineMm==15000+i,"lost bank binding");
                    BottomEquipmentBridge bridge;Need(BottomOwnedEquipment.Bind(s,rod.BankRigId,out bridge)==null&&bridge.Assembly.LineRemainingMm==50000,"wrong spool binding");
                }
                Need(FloatEquipmentCodec.Encode(s.Equipment).SequenceEqual(original),"float equipment changed");
            }
        });
        Test("only an owned stand and distinct owned idle rod may cast, within that reel's line budget",()=>{
            using(var st=Fresh(PathFor(),stand:false))Reject(st.Execute(Cast(st.State,0)),"bank-stand-required");
            using(var st=Fresh(PathFor())){
                var s=st.State;var c=Cast(s,1);
                var wrong=new CastBankBottomRod(s.ProfileId,s.BranchId,G(),G(),G(),c.KitRigId,s.OwnedBottomKits[0].Ids[3],c.LeaderId,1,1,100,s.Sequence,1,2);
                Reject(st.Execute(wrong),"bottom-kit-binding-stale");
                Reject(st.Execute(CastBankBottomRod.Create(s,c.KitRigId,50001)),"line-insufficient");
                Start(st,1);Reject(st.Execute(Cast(st.State,1)),"rig-session-busy");
                Need(st.State.BaitCount==11&&st.State.Stamina==98,"rejected cast debited resources");
            }
        });
        Test("two frontends targeting the last bait or stamina can fund at most one rod",()=>{
            foreach(bool scarceBait in new[]{true,false})using(var st=Fresh(PathFor(),scarceBait?1:12,scarceBait?100:2)){
                var first=Cast(st.State,0);var stale=Cast(st.State,1);
                Written(st.Execute(first));Reject(st.Execute(stale),"version-conflict");
                Reject(st.Execute(Cast(st.State,1)),scarceBait?"bait-empty":"stamina-insufficient");
                Need(st.Execute(first).IsReplay&&st.State.BankSessions.Count==1,"duplicate consumed last supply");
            }
        });
        Test("legacy float and first-kit casts and ordinary menu writes are exclusive with bank sessions",()=>{
            using(var st=Fresh(PathFor())){
                Written(st.Execute(FloatCast(st.State)));Reject(st.Execute(Cast(st.State,0)),"session-busy");
                var s=st.State;Written(st.Execute(new RecallFloat(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,1,2)));
                Start(st,1);s=st.State;Reject(st.Execute(FloatCast(s)),"session-busy");
                Reject(st.Execute(CastBottomKitWithEquipment.Create(s,15000)),"session-busy");
                Reject(st.Execute(new ChangeFloatClothing(s.ProfileId,s.BranchId,G(),G(),FloatClothingAction.Buy,"moss-jacket",s.Sequence,1,2)),"session-busy");
                Reject(st.Execute(new RecallFloat(s.ProfileId,s.BranchId,G(),G(),s.BankSessions[0].Id,s.Sequence,1,2)),"session-busy");
                Written(st.Execute(End(st.State,1,BankRodEnding.Recall)));Written(st.Execute(CastBottomKitWithEquipment.Create(st.State,15000)));
                Need(st.State.Session.IsBottom&&st.State.BankSessions.Count==0,"old single rod did not resume");
            }
        });
        Test("wrong rod-session pairing and stale endings cannot terminate or reward a different rod",()=>{
            using(var st=Fresh(PathFor())){
                var first=Start(st,0);var next=Start(st,1);var s=st.State;
                Reject(st.Execute(SettleBankBottomRod.Create(s,next.KitRigId,first.SessionId,BankRodEnding.Recall)),"session-not-current");
                Reject(st.Execute(End(s,1,BankRodEnding.Release)),"pending-fish-not-current");
                var stale=End(s,1,BankRodEnding.Recall);var current=End(s,0,BankRodEnding.Recall);
                Written(st.Execute(current));Reject(st.Execute(stale),"version-conflict");Need(st.Execute(current).IsReplay,"lost ending receipt");
                Reject(st.Execute(SettleBankBottomRod.Create(st.State,first.KitRigId,first.SessionId,BankRodEnding.Recall)),"session-not-current");
                Need(st.State.BankSessions.Single().Id==next.SessionId&&st.State.Creel.Count==0,"other rod affected");
            }
        });
        Test("two fish compete for the last creel slot, pending survives reopen and sale uses the original wallet",()=>{
            string path=PathFor();Guid pending,kept;long value;
            using(var st=Fresh(path)){
                Start(st,0);Start(st,1);Start(st,2);
                var first=Fish(st.State,0);var stale=Fish(st.State,1);kept=first.FishId;
                Written(st.Execute(first));Reject(st.Execute(stale),"version-conflict");
                var second=Fish(st.State,1);pending=second.FishId;var r=st.Execute(second);Written(r);
                Need(!r.OperationSucceeded&&r.Receipt.Outcome==FloatOutcome.CapacityRejected&&st.Execute(second).IsReplay,"capacity must have one receipt");
                Need(st.State.Creel.Single().Id==kept&&st.State.BankSessionFor(second.RigId).PendingFish.Id==pending,"wrong fish owner");
                value=st.State.Creel[0].SaleValueMinor;
                Reject(st.Execute(End(st.State,1,BankRodEnding.Recall)),"release-current-required");
                Reject(st.Execute(Fish(st.State,2)),"bank-result-pending");
            }
            using(var owner=FloatGameOwner.OpenExisting(path)){
                var s=owner.Snapshot.State;Need(s.BankSessions.Count==1&&s.BankSessions[0].PendingFish.Id==pending&&owner.Snapshot.PendingResult,"startup lost pending");
                Need(s.Receipts.Last().RecoveryReason==FloatRecoveryReason.StartupInterrupted,"other rod not recovered");
                var v=owner.OpenView(FloatViewRole.Interaction);var d=owner.AttachDocument(v);owner.SetHostPaused(false);var lease=owner.GrantControl(v,d);
                var release=End(s,1,BankRodEnding.Release);Written(owner.ExecuteUser(v,d,lease,release));Need(owner.ExecuteUser(v,d,lease,release).IsReplay,"release repeat");
                s=owner.Snapshot.State;Written(owner.ExecuteUser(v,d,lease,new SellFloatFish(s.ProfileId,s.BranchId,G(),G(),new[]{kept},s.Sequence,1,2)));
                Need(owner.Snapshot.State.WalletMinor==4400+value&&owner.Snapshot.State.Creel.Count==0&&owner.Snapshot.State.BankSessions.Count==0,"wallet/creel after sale");
            }
        });
        Test("orderly shutdown records each outstanding rig once and clean startup does not append",()=>{
            string path=PathFor();
            using(var st=Fresh(path)){Start(st,2);Start(st,1);Start(st,0);
                var resolved=FloatGameLifecycle.OwnerShutdown(st);
                Need(resolved.Confirmed&&resolved.Transactions.Count==3&&resolved.Transactions.All(r=>r.Written&&r.Receipt.RecoveryReason==FloatRecoveryReason.OwnerShutdown),"incomplete orderly recovery");
                Need(resolved.Transactions.Select(r=>r.Receipt.BankRigId).SequenceEqual(st.State.OwnedBottomKits.Select(k=>k.Equipment.RigId)),"unstable recovery order");
            }
            var before=File.ReadAllBytes(path);using(var o=FloatGameOwner.OpenExisting(path))Need(o.Snapshot.State.Sequence==10&&o.Snapshot.State.BankSessions.Count==0,"unexpected startup");
            Need(before.SequenceEqual(File.ReadAllBytes(path)),"clean restart wrote again");
        });
        Test("interrupted multi-recovery keeps its confirmed prefix, then settles only the remaining rods",()=>{
            string path=PathFor();using(var st=Fresh(path)){Start(st,0);Start(st,1);Start(st,2);}
            int writes=0;
            using(var st=FloatGameStore.OpenExisting(path,p=>{if(p==FloatSaveFaultPoint.AfterFlush&&++writes==2)throw new IOException("lost ack");}))
                Throws<FloatSaveUncertainException>(()=>FloatGameLifecycle.Startup(st));
            using(var st=FloatGameStore.OpenExisting(path)){Need(st.State.BankSessions.Count==1&&st.State.Sequence==9,"uncertain prefix");
                var p=FloatGameLifecycle.Startup(st);Need(p.Transactions.Count==1&&st.State.BankSessions.Count==0&&st.State.Sequence==10,"recovery repeated confirmed rod");
                Need(st.State.Creel.Count==0&&st.State.BaitCount==9&&st.State.Stamina==94,"recovery refunded or rewarded");
            }
        });
        Test("definite cast save failure retries same identities and flushed fish acknowledgement replays once",()=>{
            string path=PathFor();using(var st=Fresh(path)){}
            bool fail=true;SettleBankBottomRod fish;
            using(var st=FloatGameStore.OpenExisting(path,p=>{if(fail&&p==FloatSaveFaultPoint.BeforeWrite)throw new IOException("before write");})){
                var cast=Cast(st.State,2);Throws<IOException>(()=>st.Execute(cast));Need(st.State.BankSessions.Count==0&&st.State.BaitCount==12,"premature publication");
                fail=false;Written(st.Execute(cast));Need(st.Execute(cast).IsReplay,"retry changed identities");fish=Fish(st.State,2);
            }
            using(var st=FloatGameStore.OpenExisting(path,p=>{if(p==FloatSaveFaultPoint.AfterFlush)throw new IOException("after flush");}))
                Throws<FloatSaveUncertainException>(()=>st.Execute(fish));
            using(var st=FloatGameStore.OpenExisting(path)){
                Need(st.Execute(fish).IsReplay&&st.State.Creel.Single().Id==fish.FishId&&st.State.BankSessions.Count==0,"confirmed catch duplicated");
            }
        });
        Test("user and host privileges separate casts, recalls, catches, unhooks and lifecycle recovery",()=>{
            string path=PathFor();using(var st=Fresh(path)){}
            using(var o=FloatGameOwner.OpenExisting(path)){
                var v=o.OpenView(FloatViewRole.Interaction);var d=o.AttachDocument(v);o.SetHostPaused(false);var lease=o.GrantControl(v,d);
                var c=Cast(o.Snapshot.State,2);o.SetMenuOpen(true);Reject(o.ExecuteUser(v,d,lease,c),"domain-paused");o.SetMenuOpen(false);
                Reject(o.ExecuteHost(c),"host-command-not-allowed");Written(o.ExecuteUser(v,d,lease,c));
                var s=o.Snapshot.State;Reject(o.ExecuteUser(v,d,lease,Fish(s,2)),"user-command-not-allowed");
                Reject(o.ExecuteHost(End(s,2,BankRodEnding.Recall)),"host-command-not-allowed");
                Reject(o.ExecuteUser(v,d,lease,End(s,2,BankRodEnding.RecoverStartup)),"user-command-not-allowed");
                var id=G();var loss=SettleBankBottomRod.Unhooked(s,c.KitRigId,c.SessionId,G(),2,id);
                o.SetMenuOpen(true);Reject(o.ExecuteHost(loss),"simulation-paused");o.SetMenuOpen(false);
                Written(o.ExecuteHost(loss));Need(o.ExecuteHost(loss).IsReplay&&o.Snapshot.State.Receipts.Last().EventId==id&&o.Snapshot.State.Creel.Count==0,"unhook duplicated or paid");
            }
        });
        Test("codec roundtrips each ending, rejects malformed payloads, and saves the actual landing event",()=>{
            using(var st=Fresh(PathFor())){
                var cast=Start(st,0);var s=st.State;Roundtrip(cast,26);
                foreach(var ending in new[]{BankRodEnding.Recall,BankRodEnding.Release,BankRodEnding.RecoverStartup,BankRodEnding.RecoverShutdown})Roundtrip(End(s,0,ending),27);
                var land=Fish(s,0);Roundtrip(land,27);Roundtrip(SettleBankBottomRod.Unhooked(s,cast.KitRigId,cast.SessionId,G(),2,G()),27);
                var invalid=FloatJournalCodec.EncodeCommand(End(s,0,BankRodEnding.Recall));invalid[invalid.Length-1]=77;
                Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(invalid));Written(st.Execute(land));
                Need(st.State.Receipts.Last().EventId==land.EventId&&st.State.Receipts.Last().BankRigId==cast.KitRigId,"landing event dropped");
            }
        });
        Test("bank session, encounter, fish, equipment and request identities cannot be reissued cross-domain",()=>{
            using(var st=Fresh(PathFor())){
                var first=Start(st,0);var next=Start(st,1);var s=st.State;
                foreach(var bad in new[]{first.SessionId,first.KitRigId,first.EventId,s.OwnedBankRodStand.Id,next.RequestId}){
                    Reject(st.Execute(SettleBankBottomRod.Land(s,next.KitRigId,next.SessionId,bad,G(),"carp",500000)),"fish-identity-used");
                    Reject(st.Execute(SettleBankBottomRod.Land(s,next.KitRigId,next.SessionId,G(),bad,"carp",500000)),"encounter-identity-used");
                }
                var fish=Fish(s,0);Written(st.Execute(fish));s=st.State;
                Reject(st.Execute(SettleBankBottomRod.Land(s,next.KitRigId,next.SessionId,fish.EncounterId,G(),"carp",500000)),"fish-identity-used");
                Written(st.Execute(End(s,1,BankRodEnding.Recall)));s=st.State;
                var legacy=new CastFloatWithEquipment(s.ProfileId,s.BranchId,G(),G(),fish.EventId,15000,s.Sequence,1,2,1,1);
                Reject(st.Execute(legacy),"session-identity-used");
            }
        });
        Test("exact per-rod byte and transaction reserve includes full creel then explicit release",()=>{
            using(var st=Fresh(PathFor())){
                Start(st,0);Start(st,1);var s=st.State;
                var cast=Cast(s,2);var retained=s.BankSessions[0];
                var land=SettleBankBottomRod.Land(s,retained.BankRigId,retained.Id,G(),G(),new string('a',64),500000);
                int castFrame=FloatJournalCodec.EncodeCommand(cast).Length+76;
                int endFrame=FloatJournalCodec.EncodeCommand(land).Length+76;
                int releaseFrame=FloatJournalCodec.EncodeCommand(End(s,0,BankRodEnding.Release)).Length+76;
                int needed=castFrame+3*(endFrame+releaseFrame);
                Need(castFrame==260&&releaseFrame==205&&endFrame+releaseFrame==516,"codec budget mismatch");
                long limit=FloatGameStore.MaximumFileBytes,tx=FloatGameStore.MaximumTransactions;
                Need(new FloatSaveBudget(3,tx-7,limit-needed,s.BankSessions).CanStartBankCast,"exact budget rejected");
                Need(!new FloatSaveBudget(3,tx-6,limit-needed,s.BankSessions).CanStartBankCast&&
                    !new FloatSaveBudget(3,tx-7,limit-needed+1,s.BankSessions).CanStartBankCast,"short budget accepted");
                Start(st,2);Need(FloatSaveBudget.PreservesEnding(tx-7,limit-needed,castFrame,null,st.State.BankSessions)&&
                    !FloatSaveBudget.PreservesEnding(tx-7,limit-needed+1,castFrame,null,st.State.BankSessions),"writer reserve mismatch");
            }
        });
        return failures==0?0:1;
    }
}
