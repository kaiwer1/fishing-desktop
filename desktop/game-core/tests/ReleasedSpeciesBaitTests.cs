using System;
using System.IO;
using System.Linq;
using FishingDesktop.GameCore;

#if RELEASED_SEVEN || RELEASED_NINE
sealed partial class DualWindowContext
{
    internal static FloatGameOwner CreateReleasedPlayer(string path)
    {
        var player=CreateGameFixture(atlas:true);
        return FloatGameOwner.CreateNewWithEquipmentOffers(path,player,CreateGameSparesCatalog(player));
    }
}
#endif

static partial class ReleasedSpeciesBaitTests
{
    static readonly Guid FishId=new Guid("8fd1e5a9-a67a-4854-998d-70c523080a42");
    const long FishMassMg=1488601, FishValueMinor=260;
    static Guid Id(){return Guid.NewGuid();}
    static void Need(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);}
    static void Written(FloatCommitResult r){Need(r.Written&&r.Committed&&!r.IsReplay,"Expected new transaction: "+r.Code);}
    static void Repeat(FloatCommitResult r){Need(!r.Written&&r.Committed&&r.IsReplay,"Expected previous transaction: "+r.Code);}

    sealed class Controls
    {
        readonly FloatGameOwner owner;
        readonly FloatViewTicket view;
        readonly FloatDocumentTicket document;
        readonly FloatControlLease lease;
        internal Controls(FloatGameOwner owner)
        {
            this.owner=owner;view=owner.OpenView(FloatViewRole.Interaction);document=owner.AttachDocument(view);
            owner.SetHostPaused(false);lease=owner.GrantControl(view,document);
        }
        internal FloatCommitResult Send(FloatCommand c){return owner.ExecuteUser(view,document,lease,c);}
        internal void Cast()
        {
            var input=new FloatCastPreparation(owner,new FloatCastPreparationRules(1000,100),target=>true);
            var water=new FloatCastTarget(0,0,12000,owner.Snapshot.State.RulesVersion,15000);
            var hold=input.Begin(view,document,lease,water);
            Need(hold.Accepted&&input.Release(hold.Gesture).Accepted&&input.CommitPrepared(hold.Gesture).ShouldStartFlight,"Could not cast through release/commit.");
            Need(!input.CommitPrepared(hold.Gesture).ShouldStartFlight,"A repeated release launched another cast.");
        }
    }

    static void OldFish(FloatGameState state)
    {
        var fish=state.Creel.Single();
        Need(fish.Id==FishId&&fish.SpeciesKey=="black-seabream"&&fish.MassMg==FishMassMg&&fish.SaleValueMinor==FishValueMinor&&fish.RulesVersion==2,"The original fish or its stored price changed.");
    }
    static void OldSave(FloatGameState state)
    {
        Need(state.SchemaVersion==3&&state.Rules.Species.Count==7&&state.Sequence==3&&state.WalletMinor==12000&&state.Stamina==98&&state.BaitCount==11&&state.FoodCount==0&&state.Session==null,"Wrong seven-species starting save.");
        OldFish(state);
    }

#if RELEASED_SEVEN
    public static void Main(string[] args)
    {
        if(args[0]=="create"){
            using(var old=DualWindowContext.CreateReleasedPlayer(args[1])){
                var input=new Controls(old);input.Cast();
                var s=old.Snapshot.State;
                Written(old.ExecuteHost(new HostFloatFish(s.ProfileId,s.BranchId,Id(),Id(),s.Session.Id,FishId,Id(),"black-seabream",FishMassMg,s.Sequence,s.Rig.Revision,s.RulesVersion)));
                OldSave(old.Snapshot.State);
            }
        }
        using(var old=FloatGameStore.OpenExisting(args[1]))OldSave(old.State);
        Console.WriteLine("PASS released 0.4.0 "+args[0]+" seven-species journal, unsold black seabream 1488601 mg / 260 minor.");
    }
#elif !RELEASED_NINE && !NINE_SKILL
    static void Unconfirmed(Action action)
    {
        try{action();}catch(FloatSaveUncertainException){return;}
        throw new Exception("Expected an unacknowledged, flushed frame.");
    }
    static ManageFloatBait Bait(FloatGameState state,FloatBaitAction action)
    {return new ManageFloatBait(state.ProfileId,state.BranchId,Id(),Id(),action,state.Sequence,state.Rig.Revision,state.RulesVersion);}

    public static void Main(string[] args)
    {
        if(args[0]=="nine"){VerifyNine(args[1]);return;}
        string path=args[0];
        byte[] sevenBytes=File.ReadAllBytes(path);
        var inspection=FloatJournalArchive.Inspect(path);OldSave(inspection.State);
        string tip=FloatJournalArchive.BackupBeforeSpeciesExpansion(inspection);
        Need(tip==inspection.TipSha256,"Missing verified backup before upgrade.");
        var old=inspection.State;
        var expand=new ExpandDomesticSpecies(old.ProfileId,old.BranchId,Id(),Id(),old.Sequence,old.Rig.Revision,old.RulesVersion);
        var eleven=new ExpandToElevenSpecies(old.ProfileId,old.BranchId,Id(),Id(),old.Sequence,old.Rig.Revision,old.RulesVersion);
        Need(FloatJournalCodec.EncodeCommand(expand)[7]==30&&FloatJournalCodec.EncodeCommand(Bait(old,FloatBaitAction.PrepareBread))[7]==31&&
            FloatJournalCodec.EncodeCommand(eleven)[7]==33,"Conflicting or incompatible journal opcode.");

        // The upgrade reaches disk, then the writer loses its acknowledgement.
        Action<FloatSaveFaultPoint> afterFlush=p=>{if(p==FloatSaveFaultPoint.AfterFlush)throw new IOException("Test: acknowledgement lost after flush.");};
        Unconfirmed(()=>{using(var unexpected=FloatGameOwner.OpenExisting(path,afterFlush,3,null,tip)){}});
        var upgraded=FloatJournalArchive.Inspect(path).State;
        Need(upgraded.Sequence==4&&upgraded.Rules.Species.Count==11&&upgraded.WalletMinor==12000&&upgraded.BreadBaitCount==0&&!upgraded.BreadBaitSelected,"Missing or repeated species expansion.");
        OldFish(upgraded);
        Need(File.ReadAllBytes(path).Take(sevenBytes.Length).SequenceEqual(sevenBytes),"Upgrade rewrote old frames.");
        Console.WriteLine("PASS species expansion 33 survived a lost acknowledgement, preserved the old fish and appended one frame.");

        bool faultOnCraft=false;
        Action<FloatSaveFaultPoint> craftFault=p=>{if(faultOnCraft)afterFlush(p);};
        ManageFloatBait prepare;
        using(var owner=FloatGameOwner.OpenExisting(path,craftFault,3,null,tip)){
            var controls=new Controls(owner);owner.SetMenuOpen(true);var s=owner.Snapshot.State;
            Need(s.Sequence==4,"Reopening with the old tip repeated migration.");
            var bill=new BuyFloatSupply(s.ProfileId,s.BranchId,Id(),Id(),FloatPurchaseQuote.Create(s,FloatSupplyProduct.Food,1),Guid.Empty,s.Sequence,s.Rig.Revision,s.RulesVersion);
            Written(controls.Send(bill));Repeat(controls.Send(bill));s=owner.Snapshot.State;
            Need(s.Sequence==5&&s.WalletMinor==11980&&s.FoodCount==1,"Incorrect bread purchase.");
            prepare=Bait(s,FloatBaitAction.PrepareBread);faultOnCraft=true;
            Unconfirmed(()=>controls.Send(prepare));
        }
        using(var owner=FloatGameOwner.OpenExisting(path)){
            var controls=new Controls(owner);owner.SetMenuOpen(true);
            Repeat(controls.Send(prepare));
            var s=owner.Snapshot.State;OldFish(s);
            Need(s.Sequence==6&&s.WalletMinor==11980&&s.FoodCount==0&&s.BreadBaitCount==3&&!s.BreadBaitSelected,"Craft retry lost or doubled bait.");
            Written(controls.Send(Bait(s,FloatBaitAction.SelectBread)));
            owner.SetMenuOpen(false);controls.Cast();s=owner.Snapshot.State;
            Need(s.Sequence==8&&s.BreadBaitSelected&&s.BreadBaitCount==2&&s.BaitCount==11&&s.Stamina==96&&s.Session!=null,"Wrong bait or cost on real cast release.");
            // Dispose without shutdown settlement, as after an interruption.
        }
        Console.WriteLine("PASS food purchase and recipe 31 replay once, float release debits one prepared portion.");

        SellFloatFish sale;
        using(var owner=FloatGameOwner.OpenExisting(path)){
            var controls=new Controls(owner);owner.SetMenuOpen(true);var s=owner.Snapshot.State;OldFish(s);
            Need(s.Sequence==9&&s.Session==null&&s.Receipts.Last().RecoveryReason==FloatRecoveryReason.StartupInterrupted&&s.BreadBaitSelected&&s.BreadBaitCount==2&&s.BaitCount==11&&s.Stamina==96,"Interrupted cast settled twice or returned bait.");
            sale=new SellFloatFish(s.ProfileId,s.BranchId,Id(),Id(),new[]{FishId},s.Sequence,s.Rig.Revision,s.RulesVersion);
            Written(controls.Send(sale));Repeat(controls.Send(sale));s=owner.Snapshot.State;
            Need(s.Sequence==10&&s.Creel.Count==0&&s.WalletMinor==12240&&s.Receipts.Last().WalletAfter-s.Receipts.Last().WalletBefore==FishValueMinor,"Sale changed the original price or paid twice.");
        }
        using(var owner=FloatGameOwner.OpenExisting(path)){
            var controls=new Controls(owner);owner.SetMenuOpen(true);Repeat(controls.Send(sale));
            var s=owner.Snapshot.State;
            Need(s.Sequence==10&&s.ProfileId==old.ProfileId&&s.BranchId==old.BranchId&&s.Rules.Species.Count==11&&s.Creel.Count==0&&s.WalletMinor==12240&&s.Stamina==96&&s.BaitCount==11&&s.BreadBaitSelected&&s.BreadBaitCount==2&&s.FoodCount==0,"Final reopen mismatch.");
        }
        var backup=Directory.GetFiles(Path.GetDirectoryName(path),"player.before-11-species.*.fltj").Single();
        Need(File.ReadAllBytes(backup).SequenceEqual(sevenBytes),"Rollback copy changed.");
        Console.WriteLine("PASS startup recovered sequence 9 without refund; old fish sold once at sequence 10 for 2.60 silver, wallet 122.40.");
    }
#endif
}
