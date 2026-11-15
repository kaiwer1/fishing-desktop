using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace FishingDesktop.GameCore
{
    internal static class FloatGuard
    {
        internal static void Id(Guid id,string name){if(id==Guid.Empty)throw new ArgumentException("An explicit nonempty identity is required.",name);}
        internal static string Key(string value)
        {
            if(String.IsNullOrEmpty(value)||value.Length>64)throw new ArgumentException("Invalid definition key.");
            foreach(char c in value)if(!((c>='a'&&c<='z')||(c>='0'&&c<='9')||c=='-'||c=='_'))throw new ArgumentException("Invalid definition key.");
            return value;
        }
        internal static string Number(long value){return value.ToString(CultureInfo.InvariantCulture);}
        internal static string Sha256(string value)
        {
            if(value==null||value.Length!=64)throw new ArgumentException("A lowercase SHA256 digest is required.");
            foreach(char c in value)if(!((c>='0'&&c<='9')||(c>='a'&&c<='f')))throw new ArgumentException("A lowercase SHA256 digest is required.");
            return value;
        }
        internal static void Unique(HashSet<Guid> ids,Guid id){Id(id,"id");if(!ids.Add(id))throw new ArgumentException("An asset has more than one owner or slot.");}
    }
    // Fixed complete fixture, NOT a general inventory/assembly editor.
    public sealed class FixedFloatRig
    {
        public readonly Guid Id,RodId,ReelId,LineId,HookId,BobberId,SinkerId;
        public readonly long Revision,LineLengthMm;
        public readonly string DefinitionKey;
        public FixedFloatRig(Guid id,long revision,string definitionKey,Guid rod,Guid reel,Guid line,long lineLengthMm,Guid hook,Guid bobber,Guid sinker)
        {
            if(revision<=0||lineLengthMm<=0)throw new ArgumentOutOfRangeException("revision");
            var ids=new HashSet<Guid>();foreach(var item in new[]{id,rod,reel,line,hook,bobber,sinker})FloatGuard.Unique(ids,item);
            Id=id;Revision=revision;DefinitionKey=FloatGuard.Key(definitionKey);RodId=rod;ReelId=reel;LineId=line;LineLengthMm=lineLengthMm;HookId=hook;BobberId=bobber;SinkerId=sinker;
        }
        internal void AddAssets(HashSet<Guid> ids)
        {foreach(var item in new[]{Id,RodId,ReelId,LineId,HookId,BobberId,SinkerId})FloatGuard.Unique(ids,item);}
    }
    // A derived description, never a second inventory or a journal input.
    public sealed class FloatRigDescriptor
    {
        public readonly Guid Id;
        public readonly long Revision;
        public readonly string DefinitionKey;
        public readonly Guid? RodId,ReelId,LineId,HookId,BobberId,SinkerId;
        public readonly long? LineLengthMm;
        internal FloatRigDescriptor(FixedFloatRig rig)
        {Id=rig.Id;Revision=rig.Revision;DefinitionKey=rig.DefinitionKey;RodId=rig.RodId;ReelId=rig.ReelId;LineId=rig.LineId;HookId=rig.HookId;BobberId=rig.BobberId;SinkerId=rig.SinkerId;LineLengthMm=rig.LineLengthMm;}
        internal FloatRigDescriptor(FloatEquipmentLedger equipment,string definition)
        {
            Id=equipment.RigId;Revision=equipment.RigRevision;DefinitionKey=definition;
            RodId=Item(equipment,FloatEquipmentSlot.Rod);ReelId=Item(equipment,FloatEquipmentSlot.Reel);
            HookId=Item(equipment,FloatEquipmentSlot.Hook);BobberId=Item(equipment,FloatEquipmentSlot.Bobber);SinkerId=Item(equipment,FloatEquipmentSlot.Sinker);
            var line=ReelId.HasValue?equipment.Spool(ReelId.Value):null;
            LineId=line==null?(Guid?)null:line.Id;LineLengthMm=line==null?(long?)null:line.RemainingLineMm;
        }
        static Guid? Item(FloatEquipmentLedger equipment,FloatEquipmentSlot slot)
        {var item=equipment.Equipped(slot);return item==null?(Guid?)null:item.Id;}
    }
    public sealed class FloatSpeciesRule
    {
        public readonly string SpeciesKey;
        public readonly long MinimumMassMg,MaximumMassMg,MinorPerKg;
        public FloatSpeciesRule(string speciesKey,long minimumMassMg,long maximumMassMg,long minorPerKg)
        {
            if(minimumMassMg<=0||maximumMassMg<minimumMassMg||minorPerKg<=0)throw new ArgumentOutOfRangeException("minimumMassMg");
            SpeciesKey=FloatGuard.Key(speciesKey);MinimumMassMg=minimumMassMg;MaximumMassMg=maximumMassMg;MinorPerKg=minorPerKg;
        }
        internal long Value(long massMg){return checked(massMg*MinorPerKg)/1000000;}
    }
    public sealed class FloatGameRules
    {
        public readonly long Version,MaximumWalletMinor;
        public readonly int CastStaminaCost,MaximumStamina,CreelCapacity;
        public readonly string RigDefinitionKey,BaitDefinitionKey;
        public readonly ReadOnlyCollection<FloatSpeciesRule> Species;
        internal readonly string Signature;
        public FloatGameRules(long version,string rigDefinitionKey,string baitDefinitionKey,int castStaminaCost,int maximumStamina,int creelCapacity,long maximumWalletMinor,IEnumerable<FloatSpeciesRule> species)
        {
            if(version<=0||castStaminaCost<=0||maximumStamina<=0||castStaminaCost>maximumStamina||creelCapacity<=0||maximumWalletMinor<=0)throw new ArgumentOutOfRangeException("version");
            if(species==null)throw new ArgumentNullException("species");
            Version=version;RigDefinitionKey=FloatGuard.Key(rigDefinitionKey);BaitDefinitionKey=FloatGuard.Key(baitDefinitionKey);CastStaminaCost=castStaminaCost;MaximumStamina=maximumStamina;CreelCapacity=creelCapacity;MaximumWalletMinor=maximumWalletMinor;
            var rows=new List<FloatSpeciesRule>();var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var row in species){if(row==null||!keys.Add(row.SpeciesKey))throw new ArgumentException("Null or duplicate species rule.");rows.Add(row);}
            if(rows.Count==0)throw new ArgumentException("At least one explicit species rule is required.","species");
            rows.Sort((a,b)=>StringComparer.Ordinal.Compare(a.SpeciesKey,b.SpeciesKey));Species=rows.AsReadOnly();
            string signature=FloatGuard.Number(version)+"|"+RigDefinitionKey+"|"+BaitDefinitionKey+"|"+FloatGuard.Number(castStaminaCost)+"|"+FloatGuard.Number(maximumStamina)+"|"+FloatGuard.Number(creelCapacity)+"|"+FloatGuard.Number(maximumWalletMinor);
            foreach(var row in rows)signature+="|"+row.SpeciesKey+":"+FloatGuard.Number(row.MinimumMassMg)+":"+FloatGuard.Number(row.MaximumMassMg)+":"+FloatGuard.Number(row.MinorPerKg);
            Signature=signature;
        }
        internal FloatSpeciesRule Find(string key){foreach(var row in Species)if(row.SpeciesKey==key)return row;return null;}
    }
    public sealed class FloatFish
    {
        public readonly Guid Id,SessionId,EncounterId;
        public readonly string SpeciesKey;
        public readonly long MassMg,SaleValueMinor,RulesVersion;
        internal FloatFish(Guid id,Guid session,Guid encounter,string species,long massMg,long valueMinor,long rulesVersion)
        {
            FloatGuard.Id(id,"id");FloatGuard.Id(session,"session");FloatGuard.Id(encounter,"encounter");
            if(massMg<=0||valueMinor<0||rulesVersion<=0)throw new ArgumentOutOfRangeException("massMg");
            Id=id;SessionId=session;EncounterId=encounter;SpeciesKey=FloatGuard.Key(species);MassMg=massMg;SaleValueMinor=valueMinor;RulesVersion=rulesVersion;
        }
    }
    public sealed class FloatSession
    {
        public readonly Guid Id,ReleaseEventId;
        public readonly long RigRevision,RulesVersion;
        public readonly long EquipmentInventoryRevision,EquipmentRulesVersion,RequiredLineMm;
        public readonly FloatFish PendingFish;
        internal readonly BottomOwnedKit BottomKit;
        internal readonly OwnedLureKit LureKit;
        internal readonly Guid BankRigId;
        public bool IsBottom {get{return BottomKit!=null;}}
        public bool IsLure {get{return LureKit!=null;}}
        public bool CapacityBlocked{get{return PendingFish!=null;}}
        internal FloatSession(Guid id,Guid releaseEvent,long rigRevision,long rulesVersion,FloatFish pendingFish=null,long equipmentInventoryRevision=0,long equipmentRulesVersion=0,long requiredLineMm=0,BottomOwnedKit bottomKit=null,Guid bankRig=default(Guid),OwnedLureKit lureKit=null)
        {
            FloatGuard.Id(id,"id");FloatGuard.Id(releaseEvent,"releaseEvent");if(rigRevision<=0||rulesVersion<=0)throw new ArgumentOutOfRangeException("rigRevision");
            if(pendingFish!=null&&pendingFish.SessionId!=id)throw new ArgumentException("Pending fish belongs to another session.");
            if(equipmentInventoryRevision<0||equipmentRulesVersion<0||requiredLineMm<0||
                ((equipmentInventoryRevision==0||equipmentRulesVersion==0||requiredLineMm==0)&&!(equipmentInventoryRevision==0&&equipmentRulesVersion==0&&requiredLineMm==0)))throw new ArgumentException("Incomplete equipment session binding.");
            Id=id;ReleaseEventId=releaseEvent;RigRevision=rigRevision;RulesVersion=rulesVersion;PendingFish=pendingFish;
            if(bankRig!=Guid.Empty&&(bottomKit==null||bottomKit.Equipment.RigId!=bankRig))throw new ArgumentException("Invalid bank rig binding.");
            if(lureKit!=null&&(bottomKit!=null||bankRig!=Guid.Empty))throw new ArgumentException("Conflicting fishing method bindings.");
            LureKit=lureKit;
            EquipmentInventoryRevision=equipmentInventoryRevision;EquipmentRulesVersion=equipmentRulesVersion;RequiredLineMm=requiredLineMm;BottomKit=bottomKit;BankRigId=bankRig;
        }
    }
    // Independent commerce policy: changing it cannot reprice existing fish or
    // change the original fishing RulesVersion. No implicit default catalogue.
    public sealed class FloatCommerceCatalog
    {
        public readonly long Version,NetPriceMinor,BaitPackPriceMinor,FoodPriceMinor;
        public readonly int BaitPackUnits,FoodStaminaRestore,MaximumBaitCount,MaximumFoodCount,MaximumPurchasePacks;
        public readonly string BaitDefinitionKey,NetDefinitionKey,FoodDefinitionKey;
        internal readonly string Signature;
        public FloatCommerceCatalog(long version,string baitKey,string netKey,string foodKey,long netPriceMinor,int baitPackUnits,long baitPackPriceMinor,long foodPriceMinor,int foodStaminaRestore,int maximumBaitCount,int maximumFoodCount,int maximumPurchasePacks)
        {
            if(version<=0||netPriceMinor<=0||baitPackUnits<=0||baitPackPriceMinor<=0||foodPriceMinor<=0||foodStaminaRestore<=0||maximumBaitCount<1||maximumFoodCount<1||maximumPurchasePacks<1||maximumPurchasePacks>100)throw new ArgumentOutOfRangeException("catalogue values");
            Version=version;BaitDefinitionKey=FloatGuard.Key(baitKey);NetDefinitionKey=FloatGuard.Key(netKey);FoodDefinitionKey=FloatGuard.Key(foodKey);
            if(BaitDefinitionKey==NetDefinitionKey||BaitDefinitionKey==FoodDefinitionKey||NetDefinitionKey==FoodDefinitionKey)throw new ArgumentException("Supply definitions must be distinct.");
            NetPriceMinor=netPriceMinor;BaitPackUnits=baitPackUnits;BaitPackPriceMinor=baitPackPriceMinor;FoodPriceMinor=foodPriceMinor;FoodStaminaRestore=foodStaminaRestore;MaximumBaitCount=maximumBaitCount;MaximumFoodCount=maximumFoodCount;MaximumPurchasePacks=maximumPurchasePacks;
            Signature=FloatGuard.Number(version)+"|"+BaitDefinitionKey+"|"+NetDefinitionKey+"|"+FoodDefinitionKey+"|"+FloatGuard.Number(netPriceMinor)+"|"+FloatGuard.Number(baitPackUnits)+"|"+FloatGuard.Number(baitPackPriceMinor)+"|"+FloatGuard.Number(foodPriceMinor)+"|"+FloatGuard.Number(foodStaminaRestore)+"|"+FloatGuard.Number(maximumBaitCount)+"|"+FloatGuard.Number(maximumFoodCount)+"|"+FloatGuard.Number(maximumPurchasePacks);
        }
    }
    public sealed class FloatOwnedNet
    {
        public readonly Guid Id;public readonly string DefinitionKey;
        internal FloatOwnedNet(Guid id,string definition){FloatGuard.Id(id,"net");Id=id;DefinitionKey=FloatGuard.Key(definition);}
    }
    public sealed class FloatUpgradeOrigin
    {
        public readonly Guid SourceBranchId;
        public readonly string SourceSha256,SourceTipSha256;
        public readonly long SourceLength,SourceSequence;
        internal FloatUpgradeOrigin(Guid branch,string sha256,string tip,long length,long sequence)
        {
            FloatGuard.Id(branch,"sourceBranch");if(length<=0||length>16*1024*1024||sequence<0)throw new ArgumentOutOfRangeException("source length or sequence");
            SourceBranchId=branch;SourceSha256=FloatGuard.Sha256(sha256);SourceTipSha256=FloatGuard.Sha256(tip);SourceLength=length;SourceSequence=sequence;
        }
    }
    public enum FloatRecoveryReason { StartupInterrupted=1,OwnerShutdown=2 }
    internal sealed class FloatClothingItem
    {
        internal readonly string Id,Name;
        internal readonly long PriceMinor;
        internal FloatClothingItem(string id,string name,long price){Id=id;Name=name;PriceMinor=price;}
    }
    internal static class FloatClothingCatalog
    {
        // Type 21 journal commands bind these two original fixed prices.
        internal static readonly ReadOnlyCollection<FloatClothingItem> Items=Array.AsReadOnly(new[]{
            new FloatClothingItem("moss-jacket","苔绿钓鱼服",1800),new FloatClothingItem("coast-jacket","海蓝防风衣",2600)});
        internal static FloatClothingItem Find(string id){foreach(var item in Items)if(item.Id==id)return item;return null;}
        internal static bool Enabled(FloatGameState state)
        {return state!=null && state.SchemaVersion==3 && state.RulesVersion==2 && state.EquipmentUpgradeOrigin==null;}
    }
    // Fixed type-22 v1 kit. The purchase frame records every new identity;
    // its canonical catalogue and installation are independent of the float rod.
    internal sealed class BottomOwnedKit
    {
        internal const long PriceMinor=6400;
        internal const long AdditionalPriceMinor=4800;
        internal readonly FloatEquipmentLedger Equipment;
        internal readonly Guid LeaderId;
        internal readonly ReadOnlyCollection<Guid> Ids;
        static readonly FloatEquipmentCatalog Definitions=new FloatEquipmentCatalog(1,"worm",new[]{
            new FloatEquipmentDefinition("bottom-bank-rod",FloatEquipmentKind.Rod,260000,0,10000,35000),
            new FloatEquipmentDefinition("bottom-bank-reel",FloatEquipmentKind.Reel,230000),
            new FloatEquipmentDefinition("bottom-022-line",FloatEquipmentKind.Line,0,220),
            new FloatEquipmentDefinition("bottom-single-hook",FloatEquipmentKind.Hook,200),
            new FloatEquipmentDefinition("bottom-18g-sinker",FloatEquipmentKind.Sinker,18000)},
            new[]{new FloatEquipmentPairRule("bottom-bank-rod","bottom-bank-reel",true)},
            new[]{new FloatEquipmentSpoolRule("bottom-bank-reel","bottom-022-line",220,50000)},
            new[]{new FloatEquipmentPairRule("bottom-single-hook","worm",true)},new FloatEquipmentLossRule[0]);
        internal BottomOwnedKit(Guid rigId,Guid rodId,Guid reelId,Guid lineId,Guid hookId,Guid sinkerId,Guid leaderId)
        {
            var ids=new[]{rigId,rodId,reelId,lineId,hookId,sinkerId,leaderId};
            var unique=new HashSet<Guid>();foreach(var id in ids)FloatGuard.Unique(unique,id);
            Ids=Array.AsReadOnly(ids);LeaderId=leaderId;
            Equipment=new FloatEquipmentLedger(rigId,1,1,Definitions,new[]{
                new FloatEquipmentInstance(rodId,"bottom-bank-rod",FloatEquipmentKind.Rod,1,FloatEquipmentLocation.RigSlot(rigId,FloatEquipmentSlot.Rod)),
                new FloatEquipmentInstance(reelId,"bottom-bank-reel",FloatEquipmentKind.Reel,1,FloatEquipmentLocation.RigSlot(rigId,FloatEquipmentSlot.Reel)),
                new FloatEquipmentInstance(lineId,"bottom-022-line",FloatEquipmentKind.Line,1,FloatEquipmentLocation.ReelSpool(reelId),50000,50000),
                new FloatEquipmentInstance(hookId,"bottom-single-hook",FloatEquipmentKind.Hook,1,FloatEquipmentLocation.RigSlot(rigId,FloatEquipmentSlot.Hook)),
                new FloatEquipmentInstance(sinkerId,"bottom-18g-sinker",FloatEquipmentKind.Sinker,1,FloatEquipmentLocation.RigSlot(rigId,FloatEquipmentSlot.Sinker))});
        }
        internal static bool Enabled(FloatGameState state)
        {return state.SchemaVersion==3 && state.RulesVersion==2 && state.EquipmentUpgradeOrigin==null && state.BaitDefinitionKey=="worm";}
    }
    // Purchased capacity only. Site placement and occupied slots require their own transactions.
    internal sealed class OwnedBankRodStand
    {
        internal const long PriceMinor=1600;
        internal readonly Guid Id;
        internal int SlotCount {get{return 3;}}
        internal OwnedBankRodStand(Guid id){FloatGuard.Id(id,"stand");Id=id;}
    }
    // The fixed direct-tie kit is paid once. Its hook belongs to the spoon and
    // cannot be equipped on a second rig or detached as a spare hook.
    internal sealed class OwnedLureKit
    {
        internal const long PriceMinor=5600;
        internal const int CastStaminaCost=2;
        internal readonly Guid RigId,RodId,ReelId,LineId,LureId,HookId;
        internal readonly ReadOnlyCollection<Guid> Ids;
        internal int LineLengthMm {get{return 50000;}}
        internal long RigRevision {get{return 1;}}
        internal long LineRevision {get{return 1;}}
        internal long LureRevision {get{return 1;}}
        internal OwnedLureKit(Guid rig,Guid rod,Guid reel,Guid line,Guid lure,Guid hook)
        {
            var ids=new[]{rig,rod,reel,line,lure,hook};var unique=new HashSet<Guid>();
            foreach(var id in ids)FloatGuard.Unique(unique,id);
            RigId=rig;RodId=rod;ReelId=reel;LineId=line;LureId=lure;HookId=hook;Ids=Array.AsReadOnly(ids);
        }
        internal static bool Enabled(FloatGameState state)
        {return state!=null && state.SchemaVersion==3 && state.RulesVersion==2 && state.EquipmentUpgradeOrigin==null;}
    }
    public enum FloatOutcome { CastCommitted,Recalled,Landed,CapacityRejected,Released,Sold,Recovered,ProfileUpgraded,SuppliesPurchased,NetEquipped,FoodUsed,Unhooked,SlotReplaced,LineMoved,TerminalLost,EquipmentOffersConfigured,EquipmentPurchased,EquipmentProfileUpgraded,EquipmentArchived,CampWorkRewarded,ClothingPurchased,ClothingWorn,ClothingRemoved,BottomKitPurchased,AdditionalBottomKitPurchased,BankRodStandPurchased,LureKitPurchased }
    public sealed class FloatEquipmentLossSummary
    {
        public readonly Guid LineId;
        public readonly long RemainingLineBeforeMm,RemainingLineAfterMm,LostLineMm;
        public readonly ReadOnlyCollection<Guid> LostItemIds;
        internal FloatEquipmentLossSummary(Guid lineId,long before,long after,long lost,IEnumerable<Guid> lostItems)
        {
            FloatGuard.Id(lineId,"line");if(before<1||before>FloatEquipmentGuard.MaximumLineMm||after<0||after>before||lost!=before-after)throw new ArgumentOutOfRangeException("line loss summary");
            if(lostItems==null)throw new ArgumentNullException("lostItems");var copy=new List<Guid>(lostItems);if(copy.Count<1||copy.Count>4)throw new ArgumentException("Invalid lost item count.");
            var unique=new HashSet<Guid>();foreach(var id in copy)FloatGuard.Unique(unique,id);copy.Sort();
            if(unique.Contains(lineId)!=(after==0))throw new ArgumentException("Line tombstone mismatch.");
            LineId=lineId;RemainingLineBeforeMm=before;RemainingLineAfterMm=after;LostLineMm=lost;LostItemIds=copy.AsReadOnly();
        }
    }
    public sealed class FloatReceipt
    {
        public readonly Guid RequestId,EventId,SessionId,EncounterId;
        public readonly long Sequence,RulesVersion,WalletBefore,WalletAfter;
        public readonly int StaminaBefore,StaminaAfter,BaitBefore,BaitAfter;
        public readonly int FoodBefore,FoodAfter;
        public readonly Guid OwnedNetBefore,OwnedNetAfter,EquippedNetBefore,EquippedNetAfter;
        public readonly FloatOutcome Outcome;
        public readonly FloatRecoveryReason? RecoveryReason;
        public readonly ReadOnlyCollection<Guid> FishIds;
        public readonly FloatEquipmentLossSummary EquipmentLoss;
        public readonly FloatEquipmentArchiveSummary EquipmentArchive;
        public readonly string ClothingItemId;
        internal readonly BottomOwnedKit BottomKitPurchase;
        internal readonly int BottomKitNumber;
        internal readonly OwnedBankRodStand StandPurchase;
        internal readonly OwnedLureKit LurePurchase;
        internal readonly Guid LureRigId;
        internal readonly Guid BankRigId;
        // Transient facts derived again from the validated journal command on
        // replay. They introduce no new payload or writable experience field.
        internal readonly string CastMethod,LandedSpecies;
        internal readonly long LandedMassMg;
        internal readonly string Fingerprint;
        internal bool Terminal{get{return Outcome==FloatOutcome.Recalled||Outcome==FloatOutcome.Landed||Outcome==FloatOutcome.Released||Outcome==FloatOutcome.Recovered||Outcome==FloatOutcome.Unhooked||Outcome==FloatOutcome.TerminalLost;}}
        internal FloatReceipt(FloatCommand command,long sequence,long rulesVersion,FloatOutcome outcome,Guid encounterId,long walletBefore,long walletAfter,int staminaBefore,int staminaAfter,int baitBefore,int baitAfter,IEnumerable<Guid> fishIds,int foodBefore=0,int foodAfter=0,Guid ownedNetBefore=default(Guid),Guid ownedNetAfter=default(Guid),Guid equippedNetBefore=default(Guid),Guid equippedNetAfter=default(Guid),FloatEquipmentLossSummary equipmentLoss=null,FloatEquipmentArchiveSummary equipmentArchive=null)
        {
            RequestId=command.RequestId;EventId=command.EventId;SessionId=command.SessionId;EncounterId=encounterId;Sequence=sequence;RulesVersion=rulesVersion;Outcome=outcome;Fingerprint=command.Fingerprint;
            var recovery=command as RecoverFloatSession;RecoveryReason=recovery==null?(FloatRecoveryReason?)null:recovery.Reason;
            var bankCast=command as CastBankBottomRod;var bankEnd=command as SettleBankBottomRod;
            if(outcome==FloatOutcome.CastCommitted){
                if(command is CastLureWithEquipment)CastMethod="lure";
                else if(command is CastBottomKitWithEquipment)CastMethod="bottom";
                else if(command is CastFloat||command is CastFloatWithEquipment)CastMethod="float";
                else throw new ArgumentException("Missing cast method on a confirmed release.");
            }
            if(outcome==FloatOutcome.Landed){
                var landed=command as HostFloatFish;
                if(landed!=null){LandedSpecies=landed.SpeciesKey;LandedMassMg=landed.MassMg;}
                else if(bankEnd!=null&&bankEnd.Ending==BankRodEnding.Land){LandedSpecies=bankEnd.Species;LandedMassMg=bankEnd.MassMg;}
                else throw new ArgumentException("Missing fish on a confirmed landing.");
            }
            if(bankCast!=null)BankRigId=bankCast.KitRigId;
            if(bankEnd!=null){
                BankRigId=bankEnd.RigId;
                if(bankEnd.Ending==BankRodEnding.RecoverStartup)RecoveryReason=FloatRecoveryReason.StartupInterrupted;
                if(bankEnd.Ending==BankRodEnding.RecoverShutdown)RecoveryReason=FloatRecoveryReason.OwnerShutdown;
            }
            WalletBefore=walletBefore;WalletAfter=walletAfter;StaminaBefore=staminaBefore;StaminaAfter=staminaAfter;BaitBefore=baitBefore;BaitAfter=baitAfter;
            FoodBefore=foodBefore;FoodAfter=foodAfter;OwnedNetBefore=ownedNetBefore;OwnedNetAfter=ownedNetAfter;EquippedNetBefore=equippedNetBefore;EquippedNetAfter=equippedNetAfter;
            var clothing=command as ChangeFloatClothing;
            ClothingItemId=clothing==null?null:clothing.ItemId;
            var first=command as BuyBottomKit;
            var lurePurchase=command as BuyLureKit;var lureCast=command as CastLureWithEquipment;
            if(lureCast!=null)LureRigId=lureCast.RigId;
            if(lurePurchase!=null)LurePurchase=lurePurchase.Kit;
            if((outcome==FloatOutcome.LureKitPurchased)!=(LurePurchase!=null))throw new ArgumentException("Invalid lure purchase receipt.");
            var additional=command as BuyAdditionalBottomKit;
            var stand=command as BuyBankRodStand;
            if(first!=null){BottomKitPurchase=first.Kit;BottomKitNumber=1;}
            if(additional!=null){BottomKitPurchase=additional.Kit;BottomKitNumber=additional.KitNumber;}
            if(stand!=null)StandPurchase=stand.Stand;
            if((outcome==FloatOutcome.BottomKitPurchased)!=(first!=null) ||
                (outcome==FloatOutcome.AdditionalBottomKitPurchased)!=(additional!=null) ||
                (outcome==FloatOutcome.BankRodStandPurchased)!=(stand!=null))throw new ArgumentException("Invalid bank gear purchase receipt.");
            var copy=new List<Guid>(fishIds);var unique=new HashSet<Guid>();foreach(var id in copy)FloatGuard.Unique(unique,id);FishIds=copy.AsReadOnly();
            if((outcome==FloatOutcome.TerminalLost)!=(equipmentLoss!=null)||(equipmentLoss!=null&&copy.Count!=0))throw new ArgumentException("Invalid equipment loss receipt.");EquipmentLoss=equipmentLoss;
            if((outcome==FloatOutcome.EquipmentArchived)!=(equipmentArchive!=null)||(equipmentArchive!=null&&(copy.Count!=0||command.SessionId!=Guid.Empty||encounterId!=Guid.Empty)))throw new ArgumentException("Invalid equipment archive receipt.");EquipmentArchive=equipmentArchive;
        }
    }
    public sealed class FloatGameState
    {
        public readonly Guid ProfileId,BranchId,BaitStackId;
        public readonly long Sequence,RulesVersion,WalletMinor;
        public readonly int Stamina,BaitCount;
        public readonly ReadOnlyCollection<string> OwnedClothingIds;
        public readonly string WornClothingId;
        public readonly string BaitDefinitionKey;
        public readonly FloatRigDescriptor Rig;
        internal readonly FixedFloatRig LegacyRig;
        internal readonly FloatEquipmentLedger Equipment;
        internal readonly ReadOnlyCollection<BottomOwnedKit> OwnedBottomKits;
        internal BottomOwnedKit OwnedBottomKit {get{return OwnedBottomKits.Count==0?null:OwnedBottomKits[0];}}
        internal readonly OwnedBankRodStand OwnedBankRodStand;
        internal readonly OwnedLureKit OwnedLureKit;
        readonly HashSet<Guid> bankGearIdentities=new HashSet<Guid>();
        readonly HashSet<Guid> bankJournalIdentities=new HashSet<Guid>();
        internal readonly ReadOnlyCollection<FloatSession> BankSessions;
        internal readonly FloatEquipmentOffers EquipmentOffers;
        internal readonly ReadOnlyCollection<Guid> ArchivedEquipmentIds,ArchivedLossOwnerIds;
        internal readonly long ArchivedEquipmentLineMm;
        readonly HashSet<Guid> archivedIdentities;
        public readonly FloatSession Session;
        public readonly ReadOnlyCollection<FloatFish> Creel;
        public readonly ReadOnlyCollection<FloatReceipt> Receipts;
        public readonly int SchemaVersion,FoodCount;
        public readonly Guid FoodStackId,EquippedNetId;
        public readonly FloatOwnedNet OwnedNet;
        public readonly FloatCommerceCatalog CommerceCatalog;
        public readonly FloatUpgradeOrigin UpgradeOrigin;
        internal readonly FloatUpgradeOrigin EquipmentUpgradeOrigin;
        internal readonly FloatGameRules Rules;
        internal FloatGameState(Guid profile,Guid branch,long sequence,long wallet,int stamina,Guid baitStack,string baitDefinition,int baitCount,FixedFloatRig rig,FloatSession session,IEnumerable<FloatFish> creel,IEnumerable<FloatReceipt> receipts,FloatGameRules rules,int schemaVersion=1,FloatCommerceCatalog catalog=null,Guid foodStack=default(Guid),int foodCount=0,FloatOwnedNet ownedNet=null,Guid equippedNet=default(Guid),FloatUpgradeOrigin origin=null,FloatEquipmentLedger equipment=null,FloatEquipmentOffers equipmentOffers=null,FloatUpgradeOrigin equipmentOrigin=null,IEnumerable<FloatSession> bankSessions=null)
        {
            FloatGuard.Id(profile,"profile");FloatGuard.Id(branch,"branch");FloatGuard.Id(baitStack,"baitStack");
            if(rules==null||creel==null||receipts==null)throw new ArgumentNullException("state component");
            if(schemaVersion==3){
                if(rig!=null||equipment==null||(origin!=null&&equipmentOrigin==null))throw new ArgumentException("Schema3 requires one equipment owner and an explicit upgrade origin for legacy history.");
                if(equipmentOrigin!=null&&(equipmentOrigin.SourceBranchId==branch||equipmentOrigin.SourceSequence>=sequence))throw new ArgumentException("Invalid equipment upgrade origin.");
                if(equipment.Catalog.BaitDefinitionKey!=baitDefinition||!equipment.ValidateAssembly().Valid)throw new ArgumentException("Invalid equipment assembly or bait configuration.");
                if(equipmentOffers!=null)equipmentOffers.ValidateAgainst(equipment.Catalog,rules.MaximumWalletMinor);
            }else if((schemaVersion!=1&&schemaVersion!=2)||rig==null||equipment!=null)throw new ArgumentException("Legacy profiles require only their fixed rig.");
            if(schemaVersion!=3&&equipmentOffers!=null)throw new ArgumentException("Legacy profiles cannot contain equipment offers.");
            if(schemaVersion!=3&&equipmentOrigin!=null)throw new ArgumentException("Legacy profiles cannot contain equipment upgrade origin.");
            var description=schemaVersion==3?new FloatRigDescriptor(equipment,rules.RigDefinitionKey):new FloatRigDescriptor(rig);
            if(sequence<0||wallet<0||wallet>rules.MaximumWalletMinor||stamina<0||stamina>rules.MaximumStamina||baitCount<0)throw new ArgumentOutOfRangeException("state values");
            if(description.DefinitionKey!=rules.RigDefinitionKey||FloatGuard.Key(baitDefinition)!=rules.BaitDefinitionKey)throw new ArgumentException("Unsupported assembly or bait.");
            var fish=new List<FloatFish>(creel);var history=new List<FloatReceipt>(receipts);if(fish.Count>rules.CreelCapacity||sequence!=(long)history.Count)throw new ArgumentException("Capacity or sequence mismatch.");
            var assets=new HashSet<Guid>();if(rig!=null)rig.AddAssets(assets);else{FloatGuard.Unique(assets,equipment.RigId);foreach(var item in equipment.Instances)FloatGuard.Unique(assets,item.Id);}FloatGuard.Unique(assets,baitStack);
            if(schemaVersion==1){if(catalog!=null||foodStack!=Guid.Empty||foodCount!=0||ownedNet!=null||equippedNet!=Guid.Empty||origin!=null)throw new ArgumentException("Legacy profiles have no commerce state.");}
            else if(schemaVersion==2||schemaVersion==3){
                if(catalog==null||catalog.BaitDefinitionKey!=baitDefinition||baitCount>catalog.MaximumBaitCount||foodCount<0||foodCount>catalog.MaximumFoodCount)throw new ArgumentException("Invalid commerce state.");
                FloatGuard.Unique(assets,foodStack);
                if(ownedNet!=null){if(ownedNet.DefinitionKey!=catalog.NetDefinitionKey)throw new ArgumentException("Unsupported owned net.");FloatGuard.Unique(assets,ownedNet.Id);}
                if(equippedNet!=Guid.Empty&&(ownedNet==null||ownedNet.Id!=equippedNet))throw new ArgumentException("Equipped net must be owned.");
                if(origin!=null&&(origin.SourceBranchId==branch||origin.SourceSequence>=sequence))throw new ArgumentException("Invalid upgrade origin.");
            }else throw new ArgumentException("Unsupported profile schema.");
            foreach(var row in fish){if(row==null||row.RulesVersion!=rules.Version)throw new ArgumentException("Invalid fish.");FloatGuard.Unique(assets,row.Id);}
            var requests=new HashSet<Guid>();var events=new HashSet<Guid>();var casts=new HashSet<Guid>();var terminals=new HashSet<Guid>();
            var archivedItems=new HashSet<Guid>();var archivedOwners=new HashSet<Guid>();var reservedArchiveIds=new HashSet<Guid>();long archivedLine=0;
            var clothingIds=new List<string>();string wearing=null;
            var kits=new List<BottomOwnedKit>();
            var bankCasts=new Dictionary<Guid,Guid>();
            var lureCasts=new HashSet<Guid>();
            for(int i=0;i<history.Count;i++){
                var row=history[i];if(row==null||row.Sequence!=(long)i+1||row.RulesVersion!=rules.Version||!requests.Add(row.RequestId)||!events.Add(row.EventId))throw new ArgumentException("Invalid receipt history.");
                if(row.BottomKitPurchase!=null){
                    long price=row.BottomKitNumber==1?BottomOwnedKit.PriceMinor:BottomOwnedKit.AdditionalPriceMinor;
                    if(row.BottomKitNumber!=kits.Count+1 || kits.Count>=3 || schemaVersion!=3 || rules.Version!=2 ||
                        equipmentOrigin!=null || baitDefinition!="worm" || row.WalletBefore-row.WalletAfter!=price)
                        throw new ArgumentException("Invalid bottom kit purchase.");
                    kits.Add(row.BottomKitPurchase);
                    foreach(var id in row.BottomKitPurchase.Ids){FloatGuard.Unique(assets,id);FloatGuard.Unique(bankGearIdentities,id);}
                }
                if(row.LurePurchase!=null){
                    if(OwnedLureKit!=null||schemaVersion!=3||rules.Version!=2||equipmentOrigin!=null||
                        row.WalletBefore-row.WalletAfter!=OwnedLureKit.PriceMinor)throw new ArgumentException("Invalid lure kit purchase.");
                    OwnedLureKit=row.LurePurchase;
                    foreach(var id in OwnedLureKit.Ids){FloatGuard.Unique(assets,id);FloatGuard.Unique(bankGearIdentities,id);}
                    FloatGuard.Unique(bankGearIdentities,row.RequestId);FloatGuard.Unique(bankGearIdentities,row.EventId);
                }
                if(row.LureRigId!=Guid.Empty){
                    if(OwnedLureKit==null||OwnedLureKit.RigId!=row.LureRigId||row.Outcome!=FloatOutcome.CastCommitted||
                        row.BaitBefore!=row.BaitAfter||row.StaminaBefore-row.StaminaAfter!=OwnedLureKit.CastStaminaCost||
                        !lureCasts.Add(row.SessionId))throw new ArgumentException("Invalid paid lure cast.");
                    bankJournalIdentities.Add(row.RequestId);bankJournalIdentities.Add(row.EventId);bankJournalIdentities.Add(row.SessionId);
                }
                if(row.StandPurchase!=null){
                    if(OwnedBankRodStand!=null || schemaVersion!=3 || rules.Version!=2 || equipmentOrigin!=null || baitDefinition!="worm" ||
                        row.WalletBefore-row.WalletAfter!=OwnedBankRodStand.PriceMinor)throw new ArgumentException("Invalid rod stand purchase.");
                    OwnedBankRodStand=row.StandPurchase;
                    FloatGuard.Unique(assets,OwnedBankRodStand.Id);FloatGuard.Unique(bankGearIdentities,OwnedBankRodStand.Id);
                }
                if(row.BottomKitNumber>1 || row.StandPurchase!=null){
                    FloatGuard.Unique(bankGearIdentities,row.RequestId);FloatGuard.Unique(bankGearIdentities,row.EventId);
                }
                if(row.ClothingItemId!=null){
                    var item=FloatClothingCatalog.Find(row.ClothingItemId);
                    if(schemaVersion!=3||rules.Version!=2||equipmentOrigin!=null||item==null)throw new ArgumentException("Unsupported wardrobe receipt.");
                    switch(row.Outcome){
                    case FloatOutcome.ClothingPurchased:
                        if(clothingIds.Contains(item.Id)||row.WalletBefore-row.WalletAfter!=item.PriceMinor)throw new ArgumentException("Invalid jacket purchase.");
                        clothingIds.Add(item.Id);break;
                    case FloatOutcome.ClothingWorn:
                        if(!clothingIds.Contains(item.Id)||wearing==item.Id||row.WalletBefore!=row.WalletAfter)throw new ArgumentException("Invalid worn jacket.");
                        wearing=item.Id;break;
                    case FloatOutcome.ClothingRemoved:
                        if(wearing!=item.Id||row.WalletBefore!=row.WalletAfter)throw new ArgumentException("Invalid removed jacket.");
                        wearing=null;break;
                    default:throw new ArgumentException("Unrecognized wardrobe receipt.");
                    }
                }
                // Reserve only identities whose archive was already confirmed.
                // Earlier receipt namespaces remain replay-compatible.
                if(reservedArchiveIds.Contains(row.RequestId)||reservedArchiveIds.Contains(row.EventId)||reservedArchiveIds.Contains(row.SessionId)||reservedArchiveIds.Contains(row.EncounterId))throw new ArgumentException("A later receipt reused an archived identity.");
                foreach(var id in row.FishIds)if(reservedArchiveIds.Contains(id))throw new ArgumentException("A later fish reused an archived identity.");
                if(row.EquipmentArchive!=null){
                    if(schemaVersion!=3)throw new ArgumentException("Only schema3 can archive equipment.");
                    var archived=row.EquipmentArchive;
                    foreach(var id in archived.ItemIds){if(!archivedItems.Add(id)||archivedOwners.Contains(id))throw new ArgumentException("An equipment identity was archived twice or reused.");reservedArchiveIds.Add(id);}
                    foreach(var id in archived.LossOwnerIds){if(archivedItems.Contains(id))throw new ArgumentException("An archived loss owner reused an item identity.");archivedOwners.Add(id);reservedArchiveIds.Add(id);}
                    if(reservedArchiveIds.Contains(row.RequestId)||reservedArchiveIds.Contains(row.EventId))throw new ArgumentException("The archive transaction reused its archived identity.");
                    archivedLine=checked(archivedLine+archived.LineMm);
                }
                if(row.Outcome==FloatOutcome.CastCommitted&&!casts.Add(row.SessionId))throw new ArgumentException("Repeated cast session.");
                if(row.Terminal&&!terminals.Add(row.SessionId))throw new ArgumentException("Repeated terminal session.");
                if(row.BankRigId!=Guid.Empty){
                    if(row.Outcome==FloatOutcome.CastCommitted)bankCasts.Add(row.SessionId,row.BankRigId);
                    Guid original;if(!bankCasts.TryGetValue(row.SessionId,out original)||original!=row.BankRigId)throw new ArgumentException("Bank receipt rig mismatch.");
                    bankJournalIdentities.Add(row.RequestId);bankJournalIdentities.Add(row.EventId);bankJournalIdentities.Add(row.SessionId);
                    if(row.EncounterId!=Guid.Empty)bankJournalIdentities.Add(row.EncounterId);
                    foreach(var fishId in row.FishIds)bankJournalIdentities.Add(fishId);
                }
            }
            OwnedBottomKits=kits.AsReadOnly();
            var activeBank=new List<FloatSession>(bankSessions??new FloatSession[0]);
            var activeIds=new HashSet<Guid>();var bankRigs=new HashSet<Guid>();
            if(activeBank.Count>3 || (activeBank.Count>0 && (OwnedBankRodStand==null||session!=null)))throw new ArgumentException("Invalid bank session mode.");
            foreach(var active in activeBank){
                if(active==null || active.BankRigId==Guid.Empty || !bankRigs.Add(active.BankRigId) || !activeIds.Add(active.Id) ||
                    BottomKitFor(active.BankRigId)!=active.BottomKit || active.RigRevision!=active.BottomKit.Equipment.RigRevision ||
                    active.RulesVersion!=rules.Version || active.EquipmentRulesVersion!=active.BottomKit.Equipment.RulesVersion ||
                    active.EquipmentInventoryRevision!=active.BottomKit.Equipment.InventoryRevision || active.RequiredLineMm<1 ||
                    active.RequiredLineMm>active.BottomKit.Equipment.RemainingLineMm || assets.Contains(active.Id) || terminals.Contains(active.Id) ||
                    !bankCasts.ContainsKey(active.Id) || bankCasts[active.Id]!=active.BankRigId)throw new ArgumentException("Invalid active bank rod.");
                if(active.PendingFish!=null){
                    if(active.PendingFish.RulesVersion!=rules.Version)throw new ArgumentException("Invalid bank pending fish.");
                    FloatGuard.Unique(assets,active.PendingFish.Id);
                }
            }
            activeBank.Sort((a,b)=>kits.IndexOf(a.BottomKit).CompareTo(kits.IndexOf(b.BottomKit)));
            BankSessions=activeBank.AsReadOnly();
            foreach(var id in casts)if(!terminals.Contains(id)&&!activeIds.Contains(id)&&(session==null||session.Id!=id))throw new ArgumentException("Unaccounted active cast.");
            foreach(var id in terminals)if(!casts.Contains(id))throw new ArgumentException("Terminal without cast.");
            if(session!=null){
                long rigRevision=session.IsLure?session.LureKit.RigRevision:session.IsBottom?session.BottomKit.Equipment.RigRevision:description.Revision;
                if(!casts.Contains(session.Id)||terminals.Contains(session.Id)||bankCasts.ContainsKey(session.Id)||session.BankRigId!=Guid.Empty||
                    session.IsLure!=lureCasts.Contains(session.Id)||session.RigRevision!=rigRevision||session.RulesVersion!=rules.Version)throw new ArgumentException("Invalid current session.");
                if(session.IsLure){
                    if(schemaVersion!=3||session.LureKit!=OwnedLureKit||session.EquipmentInventoryRevision!=1||session.EquipmentRulesVersion!=1||
                        session.RequiredLineMm<1||session.RequiredLineMm>OwnedLureKit.LineLengthMm||assets.Contains(session.Id))
                        throw new ArgumentException("Invalid paid lure session binding.");
                }else if(session.IsBottom){
                    if(schemaVersion!=3 || OwnedBottomKit!=session.BottomKit || session.EquipmentInventoryRevision!=OwnedBottomKit.Equipment.InventoryRevision ||
                        session.EquipmentRulesVersion!=OwnedBottomKit.Equipment.RulesVersion || session.RequiredLineMm<1 ||
                        session.RequiredLineMm>OwnedBottomKit.Equipment.Spool(OwnedBottomKit.Equipment.Equipped(FloatEquipmentSlot.Reel).Id).RemainingLineMm || assets.Contains(session.Id))
                        throw new ArgumentException("Invalid owned bottom rig session binding.");
                }else if(schemaVersion==3){
                    if(session.EquipmentInventoryRevision!=equipment.InventoryRevision||session.EquipmentRulesVersion!=equipment.RulesVersion||session.RequiredLineMm<1||
                        !equipment.EvaluateCastReadiness(FloatEquipmentGate.Idle,session.RequiredLineMm,1,rules.CastStaminaCost,rules.CastStaminaCost).Valid||assets.Contains(session.Id))throw new ArgumentException("Invalid equipment session binding.");
                }else if(session.EquipmentInventoryRevision!=0||session.EquipmentRulesVersion!=0||session.RequiredLineMm!=0)throw new ArgumentException("Legacy session contains equipment data.");
                if(session.PendingFish!=null){if(session.PendingFish.RulesVersion!=rules.Version)throw new ArgumentException("Invalid pending rules.");FloatGuard.Unique(assets,session.PendingFish.Id);}
            }
            foreach(var id in assets)if(reservedArchiveIds.Contains(id))throw new ArgumentException("Current ownership reused an archived identity.");
            if(reservedArchiveIds.Contains(profile)||reservedArchiveIds.Contains(branch))throw new ArgumentException("Current profile or branch reused an archived identity.");
            if(session!=null&&reservedArchiveIds.Contains(session.Id))throw new ArgumentException("Current session reused an archived identity.");
            var archiveIds=new List<Guid>(archivedItems);archiveIds.Sort();ArchivedEquipmentIds=archiveIds.AsReadOnly();
            var lossOwners=new List<Guid>(archivedOwners);lossOwners.Sort();ArchivedLossOwnerIds=lossOwners.AsReadOnly();
            ArchivedEquipmentLineMm=archivedLine;archivedIdentities=reservedArchiveIds;
            clothingIds.Sort(StringComparer.Ordinal);OwnedClothingIds=clothingIds.AsReadOnly();WornClothingId=wearing;
            ProfileId=profile;BranchId=branch;Sequence=sequence;RulesVersion=rules.Version;WalletMinor=wallet;Stamina=stamina;BaitStackId=baitStack;BaitDefinitionKey=baitDefinition;BaitCount=baitCount;Rig=description;LegacyRig=rig;Equipment=equipment;Session=session;Creel=fish.AsReadOnly();Receipts=history.AsReadOnly();Rules=rules;
            SchemaVersion=schemaVersion;CommerceCatalog=catalog;FoodStackId=foodStack;FoodCount=foodCount;OwnedNet=ownedNet;EquippedNetId=equippedNet;UpgradeOrigin=origin;
            EquipmentOffers=equipmentOffers;
            EquipmentUpgradeOrigin=equipmentOrigin;
        }
        // No import/restore fallback and no default money, bait or equipment.
        public static FloatGameState CreateNew(Guid profile,Guid branch,long walletMinor,int stamina,Guid baitStack,string baitDefinition,int baitCount,FixedFloatRig rig,FloatGameRules rules)
        {return new FloatGameState(profile,branch,0,walletMinor,stamina,baitStack,baitDefinition,baitCount,rig,null,new FloatFish[0],new FloatReceipt[0],rules);}
        public static FloatGameState CreateNewWithCommerce(Guid profile,Guid branch,long walletMinor,int stamina,Guid baitStack,string baitDefinition,int baitCount,FixedFloatRig rig,FloatGameRules rules,FloatCommerceCatalog catalog,Guid foodStackId)
        {return new FloatGameState(profile,branch,0,walletMinor,stamina,baitStack,baitDefinition,baitCount,rig,null,new FloatFish[0],new FloatReceipt[0],rules,2,catalog,foodStackId);}
        internal static FloatGameState CreateNewWithEquipment(Guid profile,Guid branch,long walletMinor,int stamina,Guid baitStack,string baitDefinition,int baitCount,FloatEquipmentLedger equipment,FloatGameRules rules,FloatCommerceCatalog catalog,Guid foodStackId)
        {return new FloatGameState(profile,branch,0,walletMinor,stamina,baitStack,baitDefinition,baitCount,null,null,new FloatFish[0],new FloatReceipt[0],rules,3,catalog,foodStackId,0,null,Guid.Empty,null,equipment);}
        internal void AddEquipmentAssets(HashSet<Guid> ids)
        {if(LegacyRig!=null)LegacyRig.AddAssets(ids);else{FloatGuard.Unique(ids,Equipment.RigId);foreach(var item in Equipment.Instances)FloatGuard.Unique(ids,item.Id);}
            foreach(var kit in OwnedBottomKits)foreach(var id in kit.Ids)FloatGuard.Unique(ids,id);
            if(OwnedLureKit!=null)foreach(var id in OwnedLureKit.Ids)FloatGuard.Unique(ids,id);
            if(OwnedBankRodStand!=null)FloatGuard.Unique(ids,OwnedBankRodStand.Id);}
        internal bool BankGearIdentityUsed(Guid id){return bankGearIdentities.Contains(id);}
        internal bool BankJournalIdentityUsed(Guid id){return bankJournalIdentities.Contains(id);}
        internal BottomOwnedKit BottomKitFor(Guid rigId)
        {foreach(var kit in OwnedBottomKits)if(kit.Equipment.RigId==rigId)return kit;return null;}
        internal FloatSession BankSessionFor(Guid rigId)
        {foreach(var session in BankSessions)if(session.BankRigId==rigId)return session;return null;}
        internal bool HasPendingBankResult
        {get{foreach(var session in BankSessions)if(session.CapacityBlocked)return true;return false;}}
        internal bool ArchivedEquipmentIdentityWasUsed(Guid id){return archivedIdentities.Contains(id);}
    }
}
