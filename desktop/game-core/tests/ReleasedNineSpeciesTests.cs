#if !RELEASED_SEVEN
using System;
using System.IO;
using System.Linq;
using FishingDesktop.GameCore;

static partial class ReleasedSpeciesBaitTests
{
    static BuyFloatSupply Buy(FloatGameState s,FloatSupplyProduct product)
    {
        return new BuyFloatSupply(s.ProfileId,s.BranchId,Id(),Id(),FloatPurchaseQuote.Create(s,product,1),
            Guid.Empty,s.Sequence,s.Rig.Revision,s.RulesVersion);
    }
    static void PreparedNine(FloatGameState s)
    {
        Need(s.Rules.Species.Count==9 && s.Session==null && s.WalletMinor==11960 && s.Stamina==98 &&
            s.BaitCount==16 && s.FoodCount==0 && s.BreadBaitCount==3 && s.BreadBaitSelected,"Nine-species bait inventory changed.");
        OldFish(s);
    }
#if RELEASED_NINE
    public static void Main(string[] args)
    {
        string mode=args[0],path=args[1];
        if(mode=="verify"){PreparedNine(FloatJournalArchive.Inspect(path).State);return;}
        var owner=mode=="create"?DualWindowContext.CreateReleasedPlayer(path):
            FloatGameOwner.OpenExisting(path,null,3,null,FloatJournalArchive.BackupBeforeSpeciesExpansion(FloatJournalArchive.Inspect(path)));
        using(owner){
            var controls=new Controls(owner);
            if(mode=="create"){
                controls.Cast();var s=owner.Snapshot.State;
                Written(owner.ExecuteHost(new HostFloatFish(s.ProfileId,s.BranchId,Id(),Id(),s.Session.Id,FishId,Id(),"black-seabream",
                    FishMassMg,s.Sequence,s.Rig.Revision,s.RulesVersion)));
            }else{
                Need(mode=="upgrade-seven" && owner.Snapshot.State.Receipts.Last().Outcome==FloatOutcome.SpeciesCatalogExpanded,
                    "The released nine-species player did not replay its seven-to-nine event.");
            }
            owner.SetMenuOpen(true);
            Written(controls.Send(Buy(owner.Snapshot.State,FloatSupplyProduct.Bait)));
            Written(controls.Send(Buy(owner.Snapshot.State,FloatSupplyProduct.Food)));
            foreach(var action in new[]{FloatBaitAction.PrepareBread,FloatBaitAction.SelectBread}){
                var s=owner.Snapshot.State;
                Written(controls.Send(new ManageFloatBait(s.ProfileId,s.BranchId,Id(),Id(),action,s.Sequence,s.Rig.Revision,s.RulesVersion)));
            }
            PreparedNine(owner.Snapshot.State);
        }
        Console.WriteLine("PASS released 0.5.0 writer "+mode+" with unsold black seabream and selected prepared bread.");
    }
#elif NINE_SKILL
    public static void Main(string[] args)
    {
        if(args[0]=="learn"){
            using(var owner=FloatGameOwner.OpenExisting(args[1])){
                var controls=new Controls(owner);owner.SetMenuOpen(true);var s=owner.Snapshot.State;PreparedNine(s);
                var learn=new LearnSteadyStrike(s.ProfileId,s.BranchId,Id(),Id(),s.Sequence,s.Rig.Revision,s.RulesVersion);
                Written(controls.Send(learn));Repeat(controls.Send(learn));
            }
        }
        var old=FloatJournalArchive.Inspect(args[1]).State;PreparedNine(old);
        Need(old.FloatXp==19&&old.SteadyStrikeLearned,"Old nine-species writer lost its confirmed catch XP or skill.");
        Console.WriteLine("PASS pre-eleven nine-species reader verifies 19 float XP and learnt strike skill.");
    }
#else
    static void CheckUnavailableBackup(string original,FloatJournalInspection originalInspection)
    {
        string blocked=Path.Combine(Path.GetDirectoryName(original),"blocked.fltj");
        File.Copy(original,blocked);
        var inspection=FloatJournalArchive.Inspect(blocked);
        string collision=Path.Combine(Path.GetDirectoryName(original),"blocked.before-11-species."+inspection.Sha256.Substring(0,24)+".fltj");
        byte[] other={1,2,3};File.WriteAllBytes(collision,other);
        string tip=FloatJournalArchive.BackupBeforeSpeciesExpansion(inspection);Need(tip==null,"Invalid backup accepted.");
        using(var owner=FloatGameOwner.OpenExisting(blocked,null,3,null,tip)){
            var s=owner.Snapshot.State;PreparedNine(s);var controls=new Controls(owner);
            Need(s.Sequence==originalInspection.Sequence,"Blocked upgrade spent a transaction.");
            controls.Cast();s=owner.Snapshot.State;
            Need(s.Session!=null&&s.BreadBaitCount==2&&s.BaitCount==16,"Existing nine-species player could not use its selected bait.");
        }
        Need(File.ReadAllBytes(collision).SequenceEqual(other),"Backup collision overwritten.");
    }
    static void VerifyNine(string path)
    {
        var inspection=FloatJournalArchive.Inspect(path);var old=inspection.State;PreparedNine(old);
        long prior=old.Sequence;bool known=old.SteadyStrikeLearned;
        var sevenToNine=old.Receipts.Count(r=>r.Outcome==FloatOutcome.SpeciesCatalogExpanded);
        Need(sevenToNine==(known?1:0),"Missing released genesis or event-30 history.");
        var historic=new ExpandDomesticSpecies(old.ProfileId,old.BranchId,Id(),Id(),old.Sequence,old.Rig.Revision,old.RulesVersion);
        Need(FloatGameReducer.Reduce(old,historic,old.Rules).Code=="species-catalog-ineligible","Historical opcode 30 accepted a nine-species source.");
        if(!known)CheckUnavailableBackup(path,inspection);
        var previousBytes=File.ReadAllBytes(path);
        string tip=FloatJournalArchive.BackupBeforeSpeciesExpansion(inspection);
        Need(tip==inspection.TipSha256,"Missing verified nine-species backup.");
        using(var owner=FloatGameOwner.OpenExisting(path,null,3,null,tip)){
            var s=owner.Snapshot.State;
            Need(s.Sequence==prior+1 && s.Rules.Signature==FloatDomesticSpeciesCatalog.Eleven.Signature &&
                s.ProfileId==old.ProfileId && s.BranchId==old.BranchId && s.Rig.Id==old.Rig.Id &&
                s.FloatXp==19 && s.SteadyStrikeLearned==known && s.Receipts.Count(r=>r.Outcome==FloatOutcome.SpeciesCatalogExpanded)==sevenToNine+1,
                "Upgrade changed old identity, skill or XP, or failed to append exactly one event.");
            foreach(var row in old.Rules.Species){
                var next=s.Rules.Find(row.SpeciesKey);
                Need(next!=null&&next.MinimumMassMg==row.MinimumMassMg&&next.MaximumMassMg==row.MaximumMassMg&&next.MinorPerKg==row.MinorPerKg,
                    "Existing species rules changed: "+row.SpeciesKey);
            }
            Need(FloatEquipmentCodec.Encode(s.Equipment).SequenceEqual(FloatEquipmentCodec.Encode(old.Equipment))&&
                s.WalletMinor==old.WalletMinor&&s.BaitCount==old.BaitCount&&s.BreadBaitCount==3&&s.BreadBaitSelected,"Equipment or inventory changed.");
            OldFish(s);
            var controls=new Controls(owner);owner.SetMenuOpen(true);
            var bill=Buy(s,FloatSupplyProduct.Bait);Written(controls.Send(bill));Repeat(controls.Send(bill));
            s=owner.Snapshot.State;Need(s.BaitCount==21&&s.WalletMinor==11940,"Post-upgrade purchase changed units or price.");
            var sale=new SellFloatFish(s.ProfileId,s.BranchId,Id(),Id(),new[]{FishId},s.Sequence,s.Rig.Revision,s.RulesVersion);
            Written(controls.Send(sale));Repeat(controls.Send(sale));
        }
        var finished=File.ReadAllBytes(path);var after=FloatJournalArchive.Inspect(path);
        Need(finished.Take(previousBytes.Length).SequenceEqual(previousBytes),"History was rewritten.");
        Need(FloatJournalArchive.BackupBeforeSpeciesExpansion(after)==null,"Already upgraded file requested another backup.");
        using(var owner=FloatGameOwner.OpenExisting(path,null,3,null,tip)){
            var s=owner.Snapshot.State;var log=FloatAnglerLog.Build(s);
            Need(s.Sequence==prior+3 && s.WalletMinor==12200 && s.Creel.Count==0 && s.BreadBaitCount==3 && s.BreadBaitSelected &&
                s.SteadyStrikeLearned==known && s.FloatXp==19 && log.TotalCaught==1 && log.TotalSold==1 && log.SaleMinor==260,
                "Reopen changed historical catch, sale, inventory or skill.");
        }
        Need(File.ReadAllBytes(path).SequenceEqual(finished),"Reopen wrote an extra frame.");
        string backup=Directory.GetFiles(Path.GetDirectoryName(path),Path.GetFileNameWithoutExtension(path)+".before-11-species.*.fltj").Single();
        Need(previousBytes.SequenceEqual(File.ReadAllBytes(backup)),"Nine-species rollback differs from original.");
        Console.WriteLine("PASS nine to eleven, preserved old opcode 30 count "+sevenToNine+", skill="+known+", 19 XP, old fish sale 260 minor, wallet 12200.");
    }
#endif
}
#endif
