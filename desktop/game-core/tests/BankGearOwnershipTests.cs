using System;
using System.IO;
using System.Linq;
using FishingDesktop.GameCore;

static class BankGearOwnershipTests
{
    static string folder;
    static int passed;
    static Guid G(){return Guid.NewGuid();}
    static string Journal(){return Path.Combine(folder,G().ToString("N")+".fltj");}
    static BottomOwnedKit Kit(Guid? leader=null){return new BottomOwnedKit(G(),G(),G(),G(),G(),G(),leader??G());}
    static BuyBottomKit First(FloatGameState s){return new BuyBottomKit(s.ProfileId,s.BranchId,G(),G(),Kit(),s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static BuyAdditionalBottomKit Additional(FloatGameState s,int n,BottomOwnedKit kit=null,Guid? request=null)
    {return new BuyAdditionalBottomKit(s.ProfileId,s.BranchId,request??G(),G(),n,kit??Kit(),s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static BuyBankRodStand Stand(FloatGameState s,Guid? stand=null)
    {return new BuyBankRodStand(s.ProfileId,s.BranchId,G(),G(),stand??G(),s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static CastBottomKitWithEquipment OldCast(FloatGameState s){var k=s.OwnedBottomKit;return new CastBottomKitWithEquipment(
        s.ProfileId,s.BranchId,G(),G(),G(),k.Equipment.RigId,k.Ids[3],k.LeaderId,1,1,22000,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static void Need(bool ok,string why){if(!ok)throw new Exception(why);}
    static void Refused(FloatCommitResult result,string code)
    {Need(!result.Committed&&!result.Written&&!result.IsReplay&&result.Code==code,"Expected "+code+", received "+result.Code);}
    static void Throws<T>(Action work)where T:Exception{try{work();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    static void Check(string label,Action work){
        try{work();passed++;Console.WriteLine("PASS "+label);}
        catch(Exception e){Console.WriteLine("FAIL "+label+" ("+e.GetType().Name+"): "+e.Message);Environment.Exit(1);}
    }
    public static int Main(string[] args){
        folder=args[0];
        Check("stand can be purchased before any rod; all three paid kits retain distinct parts on reopen",()=>{
            string path=Journal();var initial=BottomOwnedKitTests.New(balance:17600);var floatBytes=FloatEquipmentCodec.Encode(initial.Equipment);
            var rack=Stand(initial);FloatCommand[] commands;Guid[] ids;
            using(var store=FloatGameStore.CreateNew(path,initial)){
                Need(store.Execute(rack).Written && store.State.WalletMinor==16000 && store.State.OwnedBottomKits.Count==0,"Stand issued a rod.");
                var first=First(store.State);Need(store.Execute(first).Written,"First kit.");
                var two=Additional(store.State,2);Need(store.Execute(two).Written && store.State.WalletMinor==4800,"Second kit price.");
                var three=Additional(store.State,3);Need(store.Execute(three).Written && store.State.WalletMinor==0,"Third kit price.");
                commands=new FloatCommand[]{rack,first,two,three};
                ids=new[]{first.Kit,two.Kit,three.Kit}.SelectMany(k=>k.Ids).ToArray();
            }
            var bytes=File.ReadAllBytes(path);
            using(var store=FloatGameStore.OpenExisting(path)){
                var s=store.State;
                Need(s.OwnedBankRodStand.Id==rack.Stand.Id && s.OwnedBankRodStand.SlotCount==3 && s.OwnedBottomKits.Count==3,"Ownership counts.");
                Need(s.OwnedBottomKits.SelectMany(k=>k.Ids).SequenceEqual(ids) && ids.Distinct().Count()==21 && !ids.Contains(rack.Stand.Id),"Restored part identities.");
                Need(s.OwnedBottomKit==s.OwnedBottomKits[0] && s.BaitCount==12 && s.Stamina==100 && s.Sequence==4 && s.WalletMinor==0,"Unexpected shared resource.");
                Need(floatBytes.SequenceEqual(FloatEquipmentCodec.Encode(s.Equipment)),"Float gear changed.");
                foreach(var c in commands)Need(store.Execute(c).IsReplay,"Old purchase was not replayable.");
                Refused(store.Execute(Additional(s,3)),"bottom-kit-already-owned");Refused(store.Execute(Stand(s)),"bank-rod-stand-already-owned");
            }
            Need(bytes.SequenceEqual(File.ReadAllBytes(path)),"Replay or refusal appended.");
        });
        Check("additional kits cannot skip an earlier kit or spend beyond the shared wallet",()=>{
            using(var s=FloatGameStore.CreateNew(Journal(),BottomOwnedKitTests.New(balance:11199))){
                Refused(s.Execute(Additional(s.State,2)),"bottom-kit-previous-required");
                Need(s.Execute(First(s.State)).Written,"First kit missing.");
                Refused(s.Execute(Additional(s.State,3)),"bottom-kit-previous-required");
                Refused(s.Execute(Additional(s.State,2)),"wallet-insufficient");
                Need(s.State.WalletMinor==4799&&s.State.OwnedBottomKits.Count==1&&s.State.Sequence==1,"Low-wallet purchase mutated state.");
            }
            using(var s=FloatGameStore.CreateNew(Journal(),BottomOwnedKitTests.New(balance:1599)))
                Refused(s.Execute(Stand(s.State)),"wallet-insufficient");
            foreach(var old in new[]{BottomOwnedKitTests.New(1),BottomOwnedKitTests.New(2),BottomOwnedKitTests.New(version:1)}){
                using(var s=FloatGameStore.CreateNew(Journal(),old)){
                    Refused(s.Execute(Stand(s.State)),"bank-gear-unavailable");Refused(s.Execute(Additional(s.State,2)),"bank-gear-unavailable");
                }
            }
        });
        Check("new commands refuse prior kit and stand identities, stale versions and altered duplicate requests",()=>{
            using(var s=FloatGameStore.CreateNew(Journal(),BottomOwnedKitTests.New(balance:20000))){
                var stale=Stand(s.State);var first=First(s.State);Need(s.Execute(first).Written,"First kit.");
                Refused(s.Execute(stale),"version-conflict");
                Refused(s.Execute(Stand(s.State,first.Kit.Ids[3])),"asset-identity-used");
                var rack=Stand(s.State);Need(s.Execute(rack).Written,"Stand.");
                Refused(s.Execute(Additional(s.State,2,Kit(rack.Stand.Id))),"asset-identity-used");
                Refused(s.Execute(Additional(s.State,2,Kit(first.Kit.LeaderId))),"asset-identity-used");
                Refused(s.Execute(Additional(s.State,2,null,rack.Stand.Id)),"request-identity-used");
                var two=Additional(s.State,2);Need(s.Execute(two).Written,"Second kit.");
                var changed=new BuyAdditionalBottomKit(two.ProfileId,two.BranchId,two.RequestId,two.EventId,3,Kit(),two.ExpectedSequence,two.ExpectedRigRevision,two.ExpectedRulesVersion);
                Refused(s.Execute(changed),"request-conflict");
                Need(s.State.Sequence==3&&s.State.OwnedBottomKits.Count==2,"Rejected requests mutated.");
            }
        });
        Check("new types round-trip exact identities, legacy first kit and cast keep type 22 and 23",()=>{
            string path=Journal();byte[] original;CastBottomKitWithEquipment cast;
            using(var s=FloatGameStore.CreateNew(path,BottomOwnedKitTests.New(balance:20000))){
                var first=First(s.State);Need(FloatJournalCodec.EncodeCommand(first)[7]==22&&s.Execute(first).Written,"Old purchase type.");
                cast=OldCast(s.State);Need(FloatJournalCodec.EncodeCommand(cast)[7]==23&&s.Execute(cast).Written,"Old cast type.");
                Refused(s.Execute(Stand(s.State)),"session-busy");Refused(s.Execute(Additional(s.State,2)),"session-busy");
            }
            original=File.ReadAllBytes(path);
            using(var s=FloatGameStore.OpenExisting(path)){
                Need(s.State.Session.Id==cast.SessionId&&s.State.Session.BottomKit==s.State.OwnedBottomKit&&s.Execute(cast).IsReplay,"Legacy cast changed.");
                var t=s.State;Need(s.Execute(new RecallFloat(t.ProfileId,t.BranchId,G(),G(),t.Session.Id,t.Sequence,t.Rig.Revision,t.RulesVersion)).Written,"Recall.");
                foreach(var c in new FloatCommand[]{Additional(s.State,2),Stand(s.State)}){
                    byte[] bytes=FloatJournalCodec.EncodeCommand(c);
                    Need(bytes[7]==(c is BuyAdditionalBottomKit?24:25) && FloatJournalCodec.DecodeCommand(bytes).Fingerprint==c.Fingerprint,"New codec mismatch.");
                    Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bytes.Concat(new byte[]{1}).ToArray()));
                    Throws<InvalidDataException>(()=>FloatJournalCodec.DecodeCommand(bytes.Take(bytes.Length-1).ToArray()));
                }
                Need(s.Execute(Additional(s.State,2)).Written,"New kit after old cast.");
            }
            Need(File.ReadAllBytes(path).Take(original.Length).SequenceEqual(original),"New entries rewrote legacy prefix.");
        });
        Check("owner requires the active menu and current control; old float rod still casts normally",()=>{
            using(var o=FloatGameOwner.CreateNew(Journal(),BottomOwnedKitTests.New(balance:20000))){
                var v=o.OpenView(FloatViewRole.Interaction);var d=o.AttachDocument(v);o.SetHostPaused(false);var l=o.GrantControl(v,d);
                var c=Stand(o.Snapshot.State);Refused(o.ExecuteUser(v,d,l,c),"menu-required");o.SetMenuOpen(true);
                Refused(o.ExecuteUser(v,d,null,c),"control-required");Need(o.ExecuteUser(v,d,l,c).Written,"Owner denied purchased stand.");
                var s=o.Snapshot.State;Need(o.ExecuteUser(v,d,l,First(s)).Written,"Original kit.");
                s=o.Snapshot.State;Need(o.ExecuteUser(v,d,l,Additional(s,2)).Written,"Cannot buy in menu.");
                o.SetMenuOpen(false);s=o.Snapshot.State;
                var cast=new CastFloatWithEquipment(s.ProfileId,s.BranchId,G(),G(),G(),15000,s.Sequence,s.Rig.Revision,s.RulesVersion,s.Equipment.InventoryRevision,s.Equipment.RulesVersion);
                Need(o.ExecuteUser(v,d,l,cast).Written && !o.Snapshot.State.Session.IsBottom && o.Snapshot.State.BaitCount==11,"Old float cast failed.");
                Need(o.Shutdown().Confirmed,"Shutdown recovery failed.");
            }
        });
        Check("known write failure is retryable; uncertain flush is recognized without repurchasing",()=>{
            foreach(bool kit in new[]{false,true})foreach(var point in new[]{FloatSaveFaultPoint.BeforeWrite,FloatSaveFaultPoint.AfterFlush}){
                var path=Journal();var origin=BottomOwnedKitTests.New(balance:20000);bool armed=false;FloatCommand c;
                using(var s=FloatGameStore.CreateNew(path,origin,p=>{if(armed&&p==point)throw new IOException("private bank test");})){
                    Need(s.Execute(First(s.State)).Written,"First kit.");c=kit?(FloatCommand)Additional(s.State,2):Stand(s.State);armed=true;
                    if(point==FloatSaveFaultPoint.BeforeWrite){Throws<IOException>(()=>s.Execute(c));Need(!s.IsFaulted&&s.State.Sequence==1,"Known failure committed.");
                        armed=false;Need(s.Execute(c).Written,"Known failure retry.");}
                    else {Throws<FloatSaveUncertainException>(()=>s.Execute(c));Need(s.IsFaulted,"Uncertain writer remained usable.");}
                }
                var bytes=File.ReadAllBytes(path);
                using(var s=FloatGameStore.OpenExisting(path))Need(s.Execute(c).IsReplay&&s.State.WalletMinor==(kit?8800:12000)&&s.State.Sequence==2&&
                    s.State.OwnedBottomKits.Count==(kit?2:1)&&(s.State.OwnedBankRodStand!=null)==!kit,"Uncertain purchase replay changed ownership.");
                Need(bytes.SequenceEqual(File.ReadAllBytes(path)),"Reopen rewrote file.");
            }
        });
        Console.WriteLine("RESULT passed="+passed);return 0;
    }
}
