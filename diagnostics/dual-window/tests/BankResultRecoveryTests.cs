using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishingDesktop.GameCore;

// This constructor is compiled only in the isolated test executable. It
// installs the reopened owner without starting Windows, WebView or a timer.
sealed partial class DualWindowContext
{
    DualWindowContext(FloatGameOwner reopened)
    {
        // No window/message loop owns shutdown in this fixture. The outer
        // using block closes the real journal owner and verifies the file.
        stopping=true;finalized=true;
        game=reopened;gameWater=FloatStillWaterGeometry.CreateDevelopment();
        RestoreBankResultHand();
    }

    static void BankRecoveryNeed(bool valid,string reason)
    {if(!valid)throw new InvalidOperationException(reason);}
    static BottomOwnedKit RecoveryKit()
    {return new BottomOwnedKit(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());}
    static CastBankBottomRod RecoveryCast(FloatGameStore store,Guid rig)
    {
        var cmd=CastBankBottomRod.Create(store.State,rig,15000);
        BankRecoveryNeed(store.Execute(cmd).Written,"cast failed");return cmd;
    }
    static Guid RecoveryLand(FloatGameStore store,CastBankBottomRod cast,FloatOutcome expected)
    {
        var fish=Guid.NewGuid();
        var result=store.Execute(SettleBankBottomRod.Land(store.State,cast.KitRigId,cast.SessionId,fish,Guid.NewGuid(),"carp",450000));
        BankRecoveryNeed(result.Written&&result.Receipt.Outcome==expected,"landing failed");return fish;
    }
    internal static void CheckBankResultRecovery(string directory)
    {
        string path=Path.Combine(directory,"private-recovery.fltj");
        var original=CreateGameFixture(22000,100,12,true);
        var one=RecoveryKit();var two=RecoveryKit();
        Guid pending,session;Guid[] kept=new Guid[2];
        using(var store=FloatGameStore.CreateNew(path,original)){
            BankRecoveryNeed(store.Execute(new BuyBottomKit(original.ProfileId,original.BranchId,Guid.NewGuid(),Guid.NewGuid(),one,0,1,2)).Written,"buy first");
            BankRecoveryNeed(store.Execute(new BuyAdditionalBottomKit(original.ProfileId,original.BranchId,Guid.NewGuid(),Guid.NewGuid(),2,two,1,1,2)).Written,"buy second");
            BankRecoveryNeed(store.Execute(new BuyBankRodStand(original.ProfileId,original.BranchId,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),2,1,2)).Written,"buy stand");
            for(int i=0;i<2;i++)kept[i]=RecoveryLand(store,RecoveryCast(store,one.Equipment.RigId),FloatOutcome.Landed);
            RecoveryCast(store,one.Equipment.RigId);
            var cast=RecoveryCast(store,two.Equipment.RigId);session=cast.SessionId;
            pending=RecoveryLand(store,cast,FloatOutcome.CapacityRejected);
        }
        using(var owner=FloatGameOwner.OpenExisting(path))
        using(var host=new DualWindowContext(owner)){
            var before=owner.Snapshot.State;
            BankRecoveryNeed(before.BankSessions.Count==1&&before.Creel.Select(f=>f.Id).SequenceEqual(kept)&&before.HasPendingBankResult,"startup recovery");
            BankRecoveryNeed(host.GameHeldBankSession.Id==session&&host.gameBankStand.Held==two.Equipment.RigId&&
                host.BankBottomFor(two.Equipment.RigId)==null&&host.BankLiveFacts(two.Equipment.RigId)==null,"missing restored hand or fabricated simulation");
            var b=new GameBinding{View=owner.OpenView(FloatViewRole.Interaction)};
            b.Document=owner.AttachDocument(b.View);owner.SetHostPaused(false);
            b.Lease=owner.GrantControl(b.View,b.Document);host.BankLease(b.Lease);
            var projected=FloatPageProjection.Create(owner.ReadView(b.View,b.Document),0,50,host.GameHeldBankSession);
            BankRecoveryNeed(!projected.CanSimulate&&projected.PendingResult&&projected.PendingFish.Id==pending.ToString("D"),"pending fish not visible");
            var wire=new Dictionary<string,object>{{"action","release-current"},{"requestId",Guid.NewGuid().ToString("D")},{"eventId",Guid.NewGuid().ToString("D")},
                {"sessionId",projected.SessionId},{"expectedSequence",projected.Sequence},{"rigRevision",projected.RigRevision},{"rulesVersion",projected.RulesVersion}};
            FloatCommand parsed;BankRecoveryNeed(FloatPageCommands.TryParse(wire,before,out parsed),"invalid release");
            var picked=host.GameHeldBankSession;
            FloatCommitResult reply;
            host.BankLease(null);
            BankRecoveryNeed(host.SettleSelectedBankRod(b,picked,parsed,out reply)=="stand-hand-stale"&&reply==null&&owner.Snapshot.State==before,"lost lease still released");
            host.BankLease(b.Lease);
            host.SettleSelectedBankRod(b,picked,parsed,out reply);
            BankRecoveryNeed(reply!=null&&reply.Written&&reply.Receipt.Outcome==FloatOutcome.Released&&!reply.IsReplay,"manual release not written");
            var after=owner.Snapshot.State;
            BankRecoveryNeed(!after.HasPendingBankResult&&after.BankSessions.Count==0&&after.Sequence==before.Sequence+1&&after.BaitCount==before.BaitCount&&
                after.Stamina==before.Stamina&&after.WalletMinor==before.WalletMinor&&after.Creel.Select(f=>f.Id).SequenceEqual(kept),"unexpected resource change");
            BankRecoveryNeed(host.SettleSelectedBankRod(b,picked,parsed,out reply)=="stand-hand-stale"&&reply==null&&owner.Snapshot.State==after,"duplicate release");
            BankRecoveryNeed(owner.Shutdown().Confirmed,"shutdown failed");
        }
        using(var store=FloatGameStore.OpenExisting(path))
            BankRecoveryNeed(store.State.BankSessions.Count==0&&store.State.Receipts.Last().Outcome==FloatOutcome.Released&&store.State.Creel.Select(f=>f.Id).SequenceEqual(kept),"closed file replay");
    }
}

static class BankResultRecoveryTests
{
    [STAThread]
    public static int Main(string[] args)
    {
        try{
            DualWindowContext.CheckBankResultRecovery(args[0]);
            Console.WriteLine("PASS reopened full-creel bank rod can be manually released once through the restored host hand");
            return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
