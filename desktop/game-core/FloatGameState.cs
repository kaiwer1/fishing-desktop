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
        public readonly FloatFish PendingFish;
        public bool CapacityBlocked{get{return PendingFish!=null;}}
        internal FloatSession(Guid id,Guid releaseEvent,long rigRevision,long rulesVersion,FloatFish pendingFish=null)
        {
            FloatGuard.Id(id,"id");FloatGuard.Id(releaseEvent,"releaseEvent");if(rigRevision<=0||rulesVersion<=0)throw new ArgumentOutOfRangeException("rigRevision");
            if(pendingFish!=null&&pendingFish.SessionId!=id)throw new ArgumentException("Pending fish belongs to another session.");
            Id=id;ReleaseEventId=releaseEvent;RigRevision=rigRevision;RulesVersion=rulesVersion;PendingFish=pendingFish;
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
    public enum FloatOutcome { CastCommitted,Recalled,Landed,CapacityRejected,Released,Sold,Recovered,ProfileUpgraded,SuppliesPurchased,NetEquipped,FoodUsed,Unhooked }
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
        internal readonly string Fingerprint;
        internal bool Terminal{get{return Outcome==FloatOutcome.Recalled||Outcome==FloatOutcome.Landed||Outcome==FloatOutcome.Released||Outcome==FloatOutcome.Recovered||Outcome==FloatOutcome.Unhooked;}}
        internal FloatReceipt(FloatCommand command,long sequence,long rulesVersion,FloatOutcome outcome,Guid encounterId,long walletBefore,long walletAfter,int staminaBefore,int staminaAfter,int baitBefore,int baitAfter,IEnumerable<Guid> fishIds,int foodBefore=0,int foodAfter=0,Guid ownedNetBefore=default(Guid),Guid ownedNetAfter=default(Guid),Guid equippedNetBefore=default(Guid),Guid equippedNetAfter=default(Guid))
        {
            RequestId=command.RequestId;EventId=command.EventId;SessionId=command.SessionId;EncounterId=encounterId;Sequence=sequence;RulesVersion=rulesVersion;Outcome=outcome;Fingerprint=command.Fingerprint;
            var recovery=command as RecoverFloatSession;RecoveryReason=recovery==null?(FloatRecoveryReason?)null:recovery.Reason;
            WalletBefore=walletBefore;WalletAfter=walletAfter;StaminaBefore=staminaBefore;StaminaAfter=staminaAfter;BaitBefore=baitBefore;BaitAfter=baitAfter;
            FoodBefore=foodBefore;FoodAfter=foodAfter;OwnedNetBefore=ownedNetBefore;OwnedNetAfter=ownedNetAfter;EquippedNetBefore=equippedNetBefore;EquippedNetAfter=equippedNetAfter;
            var copy=new List<Guid>(fishIds);var unique=new HashSet<Guid>();foreach(var id in copy)FloatGuard.Unique(unique,id);FishIds=copy.AsReadOnly();
        }
    }
    public sealed class FloatGameState
    {
        public readonly Guid ProfileId,BranchId,BaitStackId;
        public readonly long Sequence,RulesVersion,WalletMinor;
        public readonly int Stamina,BaitCount;
        public readonly string BaitDefinitionKey;
        public readonly FixedFloatRig Rig;
        public readonly FloatSession Session;
        public readonly ReadOnlyCollection<FloatFish> Creel;
        public readonly ReadOnlyCollection<FloatReceipt> Receipts;
        public readonly int SchemaVersion,FoodCount;
        public readonly Guid FoodStackId,EquippedNetId;
        public readonly FloatOwnedNet OwnedNet;
        public readonly FloatCommerceCatalog CommerceCatalog;
        public readonly FloatUpgradeOrigin UpgradeOrigin;
        internal readonly FloatGameRules Rules;
        internal FloatGameState(Guid profile,Guid branch,long sequence,long wallet,int stamina,Guid baitStack,string baitDefinition,int baitCount,FixedFloatRig rig,FloatSession session,IEnumerable<FloatFish> creel,IEnumerable<FloatReceipt> receipts,FloatGameRules rules,int schemaVersion=1,FloatCommerceCatalog catalog=null,Guid foodStack=default(Guid),int foodCount=0,FloatOwnedNet ownedNet=null,Guid equippedNet=default(Guid),FloatUpgradeOrigin origin=null)
        {
            FloatGuard.Id(profile,"profile");FloatGuard.Id(branch,"branch");FloatGuard.Id(baitStack,"baitStack");
            if(rules==null||rig==null||creel==null||receipts==null)throw new ArgumentNullException("state component");
            if(sequence<0||wallet<0||wallet>rules.MaximumWalletMinor||stamina<0||stamina>rules.MaximumStamina||baitCount<0)throw new ArgumentOutOfRangeException("state values");
            if(rig.DefinitionKey!=rules.RigDefinitionKey||FloatGuard.Key(baitDefinition)!=rules.BaitDefinitionKey)throw new ArgumentException("Unsupported fixed assembly or bait.");
            var fish=new List<FloatFish>(creel);var history=new List<FloatReceipt>(receipts);if(fish.Count>rules.CreelCapacity||sequence!=(long)history.Count)throw new ArgumentException("Capacity or sequence mismatch.");
            var assets=new HashSet<Guid>();rig.AddAssets(assets);FloatGuard.Unique(assets,baitStack);
            if(schemaVersion==1){if(catalog!=null||foodStack!=Guid.Empty||foodCount!=0||ownedNet!=null||equippedNet!=Guid.Empty||origin!=null)throw new ArgumentException("Legacy profiles have no commerce state.");}
            else if(schemaVersion==2){
                if(catalog==null||catalog.BaitDefinitionKey!=baitDefinition||baitCount>catalog.MaximumBaitCount||foodCount<0||foodCount>catalog.MaximumFoodCount)throw new ArgumentException("Invalid commerce state.");
                FloatGuard.Unique(assets,foodStack);
                if(ownedNet!=null){if(ownedNet.DefinitionKey!=catalog.NetDefinitionKey)throw new ArgumentException("Unsupported owned net.");FloatGuard.Unique(assets,ownedNet.Id);}
                if(equippedNet!=Guid.Empty&&(ownedNet==null||ownedNet.Id!=equippedNet))throw new ArgumentException("Equipped net must be owned.");
                if(origin!=null&&(origin.SourceBranchId==branch||origin.SourceSequence>=sequence))throw new ArgumentException("Invalid upgrade origin.");
            }else throw new ArgumentException("Unsupported profile schema.");
            foreach(var row in fish){if(row==null||row.RulesVersion!=rules.Version)throw new ArgumentException("Invalid fish.");FloatGuard.Unique(assets,row.Id);}
            var requests=new HashSet<Guid>();var events=new HashSet<Guid>();var casts=new HashSet<Guid>();var terminals=new HashSet<Guid>();
            for(int i=0;i<history.Count;i++){
                var row=history[i];if(row==null||row.Sequence!=(long)i+1||row.RulesVersion!=rules.Version||!requests.Add(row.RequestId)||!events.Add(row.EventId))throw new ArgumentException("Invalid receipt history.");
                if(row.Outcome==FloatOutcome.CastCommitted&&!casts.Add(row.SessionId))throw new ArgumentException("Repeated cast session.");
                if(row.Terminal&&!terminals.Add(row.SessionId))throw new ArgumentException("Repeated terminal session.");
            }
            foreach(var id in casts)if(!terminals.Contains(id)&&(session==null||session.Id!=id))throw new ArgumentException("Unaccounted active cast.");
            foreach(var id in terminals)if(!casts.Contains(id))throw new ArgumentException("Terminal without cast.");
            if(session!=null){
                if(!casts.Contains(session.Id)||terminals.Contains(session.Id)||session.RigRevision!=rig.Revision||session.RulesVersion!=rules.Version)throw new ArgumentException("Invalid current session.");
                if(session.PendingFish!=null){if(session.PendingFish.RulesVersion!=rules.Version)throw new ArgumentException("Invalid pending rules.");FloatGuard.Unique(assets,session.PendingFish.Id);}
            }
            ProfileId=profile;BranchId=branch;Sequence=sequence;RulesVersion=rules.Version;WalletMinor=wallet;Stamina=stamina;BaitStackId=baitStack;BaitDefinitionKey=baitDefinition;BaitCount=baitCount;Rig=rig;Session=session;Creel=fish.AsReadOnly();Receipts=history.AsReadOnly();Rules=rules;
            SchemaVersion=schemaVersion;CommerceCatalog=catalog;FoodStackId=foodStack;FoodCount=foodCount;OwnedNet=ownedNet;EquippedNetId=equippedNet;UpgradeOrigin=origin;
        }
        // No import/restore fallback and no default money, bait or equipment.
        public static FloatGameState CreateNew(Guid profile,Guid branch,long walletMinor,int stamina,Guid baitStack,string baitDefinition,int baitCount,FixedFloatRig rig,FloatGameRules rules)
        {return new FloatGameState(profile,branch,0,walletMinor,stamina,baitStack,baitDefinition,baitCount,rig,null,new FloatFish[0],new FloatReceipt[0],rules);}
        public static FloatGameState CreateNewWithCommerce(Guid profile,Guid branch,long walletMinor,int stamina,Guid baitStack,string baitDefinition,int baitCount,FixedFloatRig rig,FloatGameRules rules,FloatCommerceCatalog catalog,Guid foodStackId)
        {return new FloatGameState(profile,branch,0,walletMinor,stamina,baitStack,baitDefinition,baitCount,rig,null,new FloatFish[0],new FloatReceipt[0],rules,2,catalog,foodStackId);}
    }
}
