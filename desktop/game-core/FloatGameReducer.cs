using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FishingDesktop.GameCore
{
    public abstract class FloatCommand
    {
        public readonly Guid ProfileId,BranchId,RequestId,EventId,SessionId;
        public readonly long ExpectedSequence,ExpectedRigRevision,ExpectedRulesVersion;
        internal readonly string Kind;
        internal FloatCommand(string kind,Guid profile,Guid branch,Guid request,Guid businessEvent,Guid session,long expectedSequence,long expectedRigRevision,long expectedRulesVersion)
        {
            foreach(var id in new[]{profile,branch,request,businessEvent})FloatGuard.Id(id,"command id");
            if(expectedSequence<0||expectedRigRevision<=0||expectedRulesVersion<=0)throw new ArgumentOutOfRangeException("expectedSequence");
            Kind=kind;ProfileId=profile;BranchId=branch;RequestId=request;EventId=businessEvent;SessionId=session;ExpectedSequence=expectedSequence;ExpectedRigRevision=expectedRigRevision;ExpectedRulesVersion=expectedRulesVersion;
        }
        internal virtual string Detail{get{return "";}}
        internal string Fingerprint{get{return Kind+"|"+ProfileId.ToString("N")+"|"+BranchId.ToString("N")+"|"+EventId.ToString("N")+"|"+SessionId.ToString("N")+"|"+FloatGuard.Number(ExpectedSequence)+"|"+FloatGuard.Number(ExpectedRigRevision)+"|"+FloatGuard.Number(ExpectedRulesVersion)+"|"+Detail;}}
    }
    public sealed class CastFloat : FloatCommand
    {
        public CastFloat(Guid profile,Guid branch,Guid request,Guid releaseEvent,Guid session,long expectedSequence,long expectedRigRevision,long expectedRulesVersion)
            :base("cast",profile,branch,request,releaseEvent,session,expectedSequence,expectedRigRevision,expectedRulesVersion){FloatGuard.Id(session,"session");}
    }
    public sealed class RecallFloat : FloatCommand
    {
        public RecallFloat(Guid profile,Guid branch,Guid request,Guid terminalEvent,Guid session,long expectedSequence,long expectedRigRevision,long expectedRulesVersion)
            :base("recall",profile,branch,request,terminalEvent,session,expectedSequence,expectedRigRevision,expectedRulesVersion){FloatGuard.Id(session,"session");}
    }
    internal abstract class FloatEquipmentCommand : FloatCommand
    {
        internal readonly long ExpectedInventoryRevision,ExpectedEquipmentVersion;
        internal FloatEquipmentCommand(string kind,Guid profile,Guid branch,Guid request,Guid businessEvent,Guid session,long sequence,long rig,long rules,long inventory,long equipment)
            :base(kind,profile,branch,request,businessEvent,session,sequence,rig,rules)
        {if(inventory<=0||equipment<=0)throw new ArgumentOutOfRangeException("equipment versions");ExpectedInventoryRevision=inventory;ExpectedEquipmentVersion=equipment;}
        internal string EquipmentDetail{get{return FloatGuard.Number(ExpectedInventoryRevision)+"|"+FloatGuard.Number(ExpectedEquipmentVersion);}}
    }
    internal sealed class ReplaceFloatSlot : FloatEquipmentCommand
    {
        internal readonly FloatEquipmentSlot Slot;internal readonly Guid? ItemId;
        internal ReplaceFloatSlot(Guid profile,Guid branch,Guid request,Guid businessEvent,FloatEquipmentSlot slot,Guid? itemId,long expectedSequence,long expectedRigRevision,long expectedRulesVersion,long expectedInventoryRevision,long expectedEquipmentVersion)
            :base("replace-slot",profile,branch,request,businessEvent,Guid.Empty,expectedSequence,expectedRigRevision,expectedRulesVersion,expectedInventoryRevision,expectedEquipmentVersion)
        {if(!FloatEquipmentGuard.Slot(slot)||itemId==Guid.Empty)throw new ArgumentException("Invalid slot intent.");Slot=slot;ItemId=itemId;}
        internal override string Detail{get{return EquipmentDetail+"|"+FloatGuard.Number((int)Slot)+"|"+(ItemId.HasValue?ItemId.Value.ToString("N"):"none");}}
    }
    internal sealed class MoveFloatLine : FloatEquipmentCommand
    {
        internal readonly Guid LineId;internal readonly Guid? ReelId;
        internal MoveFloatLine(Guid profile,Guid branch,Guid request,Guid businessEvent,Guid lineId,Guid? reelId,long expectedSequence,long expectedRigRevision,long expectedRulesVersion,long expectedInventoryRevision,long expectedEquipmentVersion)
            :base("move-line",profile,branch,request,businessEvent,Guid.Empty,expectedSequence,expectedRigRevision,expectedRulesVersion,expectedInventoryRevision,expectedEquipmentVersion)
        {FloatGuard.Id(lineId,"line");if(reelId==Guid.Empty)throw new ArgumentException("Invalid reel identity.");LineId=lineId;ReelId=reelId;}
        internal override string Detail{get{return EquipmentDetail+"|"+LineId.ToString("N")+"|"+(ReelId.HasValue?ReelId.Value.ToString("N"):"none");}}
    }
    internal sealed class CastFloatWithEquipment : FloatEquipmentCommand
    {
        internal readonly long RequiredLineMm;
        internal CastFloatWithEquipment(Guid profile,Guid branch,Guid request,Guid releaseEvent,Guid session,long requiredLineMm,long expectedSequence,long expectedRigRevision,long expectedRulesVersion,long expectedInventoryRevision,long expectedEquipmentVersion)
            :base("cast-equipment",profile,branch,request,releaseEvent,session,expectedSequence,expectedRigRevision,expectedRulesVersion,expectedInventoryRevision,expectedEquipmentVersion)
        {FloatGuard.Id(session,"session");if(requiredLineMm<1||requiredLineMm>FloatEquipmentGuard.MaximumLineMm)throw new ArgumentOutOfRangeException("requiredLineMm");RequiredLineMm=requiredLineMm;}
        internal override string Detail{get{return EquipmentDetail+"|"+FloatGuard.Number(RequiredLineMm);}}
    }
    public sealed class ReleaseCurrentFloatFish : FloatCommand
    {
        public ReleaseCurrentFloatFish(Guid profile,Guid branch,Guid request,Guid terminalEvent,Guid session,long expectedSequence,long expectedRigRevision,long expectedRulesVersion)
            :base("release-current",profile,branch,request,terminalEvent,session,expectedSequence,expectedRigRevision,expectedRulesVersion){FloatGuard.Id(session,"session");}
    }
    public sealed class SellFloatFish : FloatCommand
    {
        public readonly ReadOnlyCollection<Guid> FishIds;
        public SellFloatFish(Guid profile,Guid branch,Guid request,Guid saleIntent,IEnumerable<Guid> fishIds,long expectedSequence,long expectedRigRevision,long expectedRulesVersion)
            :base("sell",profile,branch,request,saleIntent,Guid.Empty,expectedSequence,expectedRigRevision,expectedRulesVersion)
        {
            if(fishIds==null)throw new ArgumentNullException("fishIds");var copy=new List<Guid>(fishIds);copy.Sort();FishIds=copy.AsReadOnly();
        }
        internal override string Detail{get{var ids=new List<string>();foreach(var id in FishIds)ids.Add(id.ToString("N"));return String.Join(",",ids.ToArray());}}
    }
    // Assembly-internal simulation seam. It is deliberately not a public user
    // command/factory or a page-provided species/weight/value payload.
    internal sealed class HostFloatFish : FloatCommand
    {
        internal readonly Guid FishId,EncounterId;
        internal readonly string SpeciesKey;
        internal readonly long MassMg;
        internal HostFloatFish(Guid profile,Guid branch,Guid request,Guid terminalEvent,Guid session,Guid fish,Guid encounter,string species,long massMg,long expectedSequence,long expectedRigRevision,long expectedRulesVersion)
            :base("host-fish",profile,branch,request,terminalEvent,session,expectedSequence,expectedRigRevision,expectedRulesVersion)
        {FloatGuard.Id(session,"session");FloatGuard.Id(fish,"fish");FloatGuard.Id(encounter,"encounter");FishId=fish;EncounterId=encounter;SpeciesKey=FloatGuard.Key(species);MassMg=massMg;}
        internal override string Detail{get{return FishId.ToString("N")+"|"+EncounterId.ToString("N")+"|"+SpeciesKey+"|"+FloatGuard.Number(MassMg);}}
    }
    // Fixed host-observed slack escape, not a player-selected loss or a fish
    // reward. The model version is explicit and part of durable identity.
    internal sealed class HostFloatUnhooked : FloatCommand
    {
        internal readonly Guid EncounterId;
        internal readonly int FightModelVersion;
        internal HostFloatUnhooked(Guid profile,Guid branch,Guid request,Guid terminalEvent,Guid session,Guid encounter,long expectedSequence,long expectedRigRevision,long expectedRulesVersion,int fightModelVersion)
            :base("host-unhooked",profile,branch,request,terminalEvent,session,expectedSequence,expectedRigRevision,expectedRulesVersion)
        {
            FloatGuard.Id(session,"session");FloatGuard.Id(encounter,"encounter");
            if(fightModelVersion!=2)throw new ArgumentOutOfRangeException("fightModelVersion");
            EncounterId=encounter;FightModelVersion=fightModelVersion;
        }
        internal override string Detail{get{return EncounterId.ToString("N")+"|"+FloatGuard.Number(FightModelVersion);}}
    }
    // Host lifecycle only; never accepted through the player command channel.
    internal sealed class RecoverFloatSession : FloatCommand
    {
        internal readonly FloatRecoveryReason Reason;
        internal RecoverFloatSession(Guid profile,Guid branch,Guid request,Guid terminalEvent,Guid session,long expectedSequence,long expectedRigRevision,long expectedRulesVersion,FloatRecoveryReason reason)
            :base("recover-session",profile,branch,request,terminalEvent,session,expectedSequence,expectedRigRevision,expectedRulesVersion)
        {
            FloatGuard.Id(session,"session");
            if(reason!=FloatRecoveryReason.StartupInterrupted&&reason!=FloatRecoveryReason.OwnerShutdown)throw new ArgumentOutOfRangeException("reason");
            Reason=reason;
        }
        internal override string Detail{get{return FloatGuard.Number((int)Reason);}}
    }
    public enum FloatSupplyProduct { Net=1,Bait=2,Food=3 }
    // The host freezes this object. Pages later receive an opaque quote ID,
    // never authority to set prices, catalogue policy or resulting asset IDs.
    public sealed class FloatPurchaseQuote
    {
        public readonly Guid ProfileId,BranchId;
        public readonly long StateSequence,CatalogVersion,TotalPriceMinor;
        public readonly FloatSupplyProduct Product;
        public readonly int PackCount,Units;
        internal readonly string CatalogSignature;
        internal FloatPurchaseQuote(Guid profile,Guid branch,long sequence,long catalogVersion,FloatSupplyProduct product,int packs,int units,long total,string signature)
        {
            FloatGuard.Id(profile,"profile");FloatGuard.Id(branch,"branch");
            if(sequence<0||catalogVersion<=0||packs<=0||packs>100||units<=0||total<=0||String.IsNullOrEmpty(signature)||signature.Length>1024)throw new ArgumentException("Invalid frozen quote.");
            if(product!=FloatSupplyProduct.Net&&product!=FloatSupplyProduct.Bait&&product!=FloatSupplyProduct.Food)throw new ArgumentOutOfRangeException("product");
            ProfileId=profile;BranchId=branch;StateSequence=sequence;CatalogVersion=catalogVersion;Product=product;PackCount=packs;Units=units;TotalPriceMinor=total;CatalogSignature=signature;
        }
        internal static FloatPurchaseQuote Create(FloatGameState state,FloatSupplyProduct product,int packCount)
        {
            if(state==null||(state.SchemaVersion!=2&&state.SchemaVersion!=3)||state.CommerceCatalog==null)throw new ArgumentException("A commerce profile is required.");
            var catalog=state.CommerceCatalog;if(packCount<=0||packCount>catalog.MaximumPurchasePacks)throw new ArgumentOutOfRangeException("packCount");
            if(product!=FloatSupplyProduct.Net&&product!=FloatSupplyProduct.Bait&&product!=FloatSupplyProduct.Food)throw new ArgumentOutOfRangeException("product");
            if(product==FloatSupplyProduct.Net&&packCount!=1)throw new ArgumentException("Only one net can be purchased.");
            int units=checked(packCount*(product==FloatSupplyProduct.Bait?catalog.BaitPackUnits:1));
            long total=checked(packCount*(product==FloatSupplyProduct.Net?catalog.NetPriceMinor:product==FloatSupplyProduct.Bait?catalog.BaitPackPriceMinor:catalog.FoodPriceMinor));
            return new FloatPurchaseQuote(state.ProfileId,state.BranchId,state.Sequence,catalog.Version,product,packCount,units,total,catalog.Signature);
        }
        internal string Fingerprint{get{return ProfileId.ToString("N")+"|"+BranchId.ToString("N")+"|"+FloatGuard.Number(StateSequence)+"|"+FloatGuard.Number(CatalogVersion)+"|"+FloatGuard.Number((int)Product)+"|"+FloatGuard.Number(PackCount)+"|"+FloatGuard.Number(Units)+"|"+FloatGuard.Number(TotalPriceMinor)+"|"+CatalogSignature;}}
    }
    internal sealed class UpgradeFloatProfile : FloatCommand
    {
        internal readonly Guid NewBranchId,FoodStackId;
        internal readonly FloatUpgradeOrigin Origin;
        internal readonly FloatCommerceCatalog Catalog;
        internal UpgradeFloatProfile(Guid profile,Guid oldBranch,Guid request,Guid businessEvent,Guid newBranch,Guid foodStack,string sourceSha256,string sourceTipSha256,long sourceLength,long expectedSequence,long expectedRigRevision,long expectedRulesVersion,FloatCommerceCatalog catalog)
            :base("upgrade-profile",profile,oldBranch,request,businessEvent,Guid.Empty,expectedSequence,expectedRigRevision,expectedRulesVersion)
        {
            FloatGuard.Id(newBranch,"newBranch");FloatGuard.Id(foodStack,"foodStack");if(newBranch==oldBranch||catalog==null)throw new ArgumentException("A new branch and explicit catalogue are required.");
            NewBranchId=newBranch;FoodStackId=foodStack;Catalog=catalog;Origin=new FloatUpgradeOrigin(oldBranch,sourceSha256,sourceTipSha256,sourceLength,expectedSequence);
        }
        internal override string Detail{get{return NewBranchId.ToString("N")+"|"+FoodStackId.ToString("N")+"|"+Origin.SourceSha256+"|"+Origin.SourceTipSha256+"|"+FloatGuard.Number(Origin.SourceLength)+"|"+Catalog.Signature;}}
    }
    public sealed class BuyFloatSupply : FloatCommand
    {
        public readonly FloatPurchaseQuote Quote;public readonly Guid NewNetId;
        public BuyFloatSupply(Guid profile,Guid branch,Guid request,Guid businessEvent,FloatPurchaseQuote quote,Guid newNetId,long expectedSequence,long expectedRigRevision,long expectedRulesVersion)
            :base("buy-supply",profile,branch,request,businessEvent,Guid.Empty,expectedSequence,expectedRigRevision,expectedRulesVersion)
        {if(quote==null)throw new ArgumentNullException("quote");Quote=quote;NewNetId=newNetId;}
        internal override string Detail{get{return Quote.Fingerprint+"|"+NewNetId.ToString("N");}}
    }
    public sealed class EquipFloatNet : FloatCommand
    {
        public readonly Guid NetId;
        public EquipFloatNet(Guid profile,Guid branch,Guid request,Guid businessEvent,Guid netId,long expectedSequence,long expectedRigRevision,long expectedRulesVersion)
            :base("equip-net",profile,branch,request,businessEvent,Guid.Empty,expectedSequence,expectedRigRevision,expectedRulesVersion){NetId=netId;}
        internal override string Detail{get{return NetId.ToString("N");}}
    }
    public sealed class UseFloatFood : FloatCommand
    {
        public readonly Guid FoodStackId;
        public UseFloatFood(Guid profile,Guid branch,Guid request,Guid businessEvent,Guid foodStackId,long expectedSequence,long expectedRigRevision,long expectedRulesVersion)
            :base("use-food",profile,branch,request,businessEvent,Guid.Empty,expectedSequence,expectedRigRevision,expectedRulesVersion){FloatGuard.Id(foodStackId,"foodStackId");FoodStackId=foodStackId;}
        internal override string Detail{get{return FoodStackId.ToString("N");}}
    }
    public sealed class FloatReduction
    {
        public readonly string Code;
        public readonly FloatGameState Candidate;
        public readonly FloatReceipt Receipt;
        public readonly bool IsReplay;
        public bool HasCandidate{get{return Candidate!=null;}}
        public bool RequiresConfirmation{get{return HasCandidate;}}
        public bool OperationSucceeded{get{return Receipt!=null&&Receipt.Outcome!=FloatOutcome.CapacityRejected;}}
        internal FloatReduction(string code,FloatGameState candidate=null,FloatReceipt receipt=null,bool replay=false){Code=code;Candidate=candidate;Receipt=receipt;IsReplay=replay;}
    }
    public static class FloatGameReducer
    {
        static FloatReduction Reject(string code){return new FloatReduction(code);}
        static bool UsedSession(FloatGameState state,Guid id){foreach(var row in state.Receipts)if(row.Outcome==FloatOutcome.CastCommitted&&row.SessionId==id)return true;return false;}
        static bool FishWasCreated(FloatGameState state,Guid id)
        {foreach(var row in state.Receipts)if(row.Outcome==FloatOutcome.Landed||row.Outcome==FloatOutcome.CapacityRejected)foreach(var fish in row.FishIds)if(fish==id)return true;return false;}
        static bool EncounterWasUsed(FloatGameState state,Guid id)
        {foreach(var row in state.Receipts)if((row.Outcome==FloatOutcome.Landed||row.Outcome==FloatOutcome.CapacityRejected||row.Outcome==FloatOutcome.Unhooked)&&row.EncounterId==id)return true;return false;}
        static bool AssetExists(FloatGameState state,Guid id)
        {var ids=new HashSet<Guid>();state.AddEquipmentAssets(ids);ids.Add(state.BaitStackId);if(state.FoodStackId!=Guid.Empty)ids.Add(state.FoodStackId);if(state.OwnedNet!=null)ids.Add(state.OwnedNet.Id);foreach(var fish in state.Creel)ids.Add(fish.Id);if(state.Session!=null&&state.Session.PendingFish!=null)ids.Add(state.Session.PendingFish.Id);return ids.Contains(id);}
        static string Code(FloatOutcome outcome)
        {switch(outcome){case FloatOutcome.CastCommitted:return "cast-candidate";case FloatOutcome.Recalled:return "recall-candidate";case FloatOutcome.Landed:return "landed-candidate";case FloatOutcome.CapacityRejected:return "creel-capacity-exceeded";case FloatOutcome.Released:return "released-candidate";case FloatOutcome.Recovered:return "recovered-candidate";case FloatOutcome.ProfileUpgraded:return "upgrade-candidate";case FloatOutcome.SuppliesPurchased:return "purchase-candidate";case FloatOutcome.NetEquipped:return "equip-candidate";case FloatOutcome.FoodUsed:return "food-candidate";case FloatOutcome.Unhooked:return "unhooked-candidate";case FloatOutcome.SlotReplaced:return "assembly-candidate";case FloatOutcome.LineMoved:return "line-move-candidate";default:return "sale-candidate";}}
        public static FloatReduction Reduce(FloatGameState state,FloatCommand command,FloatGameRules rules)
        {
            if(state==null)throw new ArgumentNullException("state");if(command==null)throw new ArgumentNullException("command");
            if(command.ProfileId!=state.ProfileId||command.BranchId!=state.BranchId)return Reject("branch-mismatch");
            string fingerprint=command.Fingerprint;
            foreach(var row in state.Receipts)if(row.RequestId==command.RequestId)
                return row.Fingerprint==fingerprint?new FloatReduction(Code(row.Outcome),receipt:row,replay:true):Reject("request-conflict");
            foreach(var row in state.Receipts)if(row.EventId==command.EventId)return Reject("event-already-used");
            if((command is CastFloat||command is CastFloatWithEquipment)&&UsedSession(state,command.SessionId))return Reject("session-already-used");
            if(command.ExpectedSequence!=state.Sequence||command.ExpectedRigRevision!=state.Rig.Revision||command.ExpectedRulesVersion!=state.RulesVersion)return Reject("version-conflict");
            if(rules==null)return Reject("configuration-missing");
            if(rules.Signature!=state.Rules.Signature)return Reject("configuration-mismatch");
            var equipmentCommand=command as FloatEquipmentCommand;
            if(equipmentCommand!=null){
                if(state.SchemaVersion!=3)return Reject("equipment-schema-required");
                if(equipmentCommand.ExpectedInventoryRevision!=state.Equipment.InventoryRevision||equipmentCommand.ExpectedEquipmentVersion!=state.Equipment.RulesVersion)return Reject("equipment-version-conflict");
            }
            long wallet=state.WalletMinor;int stamina=state.Stamina,baits=state.BaitCount;var fish=new List<FloatFish>(state.Creel);var session=state.Session;var affected=new List<Guid>();Guid encounterId=Guid.Empty;FloatOutcome outcome;
            int schema=state.SchemaVersion,foods=state.FoodCount;Guid branch=state.BranchId,foodStack=state.FoodStackId,equipped=state.EquippedNetId;var net=state.OwnedNet;var catalog=state.CommerceCatalog;var origin=state.UpgradeOrigin;
            var equipment=state.Equipment;
            try{
                if(command is UpgradeFloatProfile){
                    var upgrade=(UpgradeFloatProfile)command;
                    if(schema!=1)return Reject("profile-already-upgraded");
                    if(upgrade.Catalog.BaitDefinitionKey!=state.BaitDefinitionKey||baits>upgrade.Catalog.MaximumBaitCount)return Reject("commerce-configuration-mismatch");
                    if(AssetExists(state,upgrade.FoodStackId)||FishWasCreated(state,upgrade.FoodStackId))return Reject("asset-identity-used");
                    schema=2;catalog=upgrade.Catalog;origin=upgrade.Origin;branch=upgrade.NewBranchId;foodStack=upgrade.FoodStackId;outcome=FloatOutcome.ProfileUpgraded;
                }else if(command is ReplaceFloatSlot||command is MoveFloatLine){
                    if(session!=null)return Reject("session-busy");FloatEquipmentChange change;
                    var replace=command as ReplaceFloatSlot;
                    if(replace!=null)change=equipment.ReplaceSlot(FloatEquipmentGate.Idle,command.ExpectedRigRevision,equipmentCommand.ExpectedInventoryRevision,replace.Slot,replace.ItemId);
                    else{var move=(MoveFloatLine)command;change=equipment.MoveLine(FloatEquipmentGate.Idle,command.ExpectedRigRevision,equipmentCommand.ExpectedInventoryRevision,move.LineId,move.ReelId);}
                    if(!change.Accepted)return Reject(change.Code);equipment=change.Candidate;outcome=replace!=null?FloatOutcome.SlotReplaced:FloatOutcome.LineMoved;
                }else if(command is BuyFloatSupply||command is EquipFloatNet||command is UseFloatFood){
                    if((schema!=2&&schema!=3)||catalog==null)return Reject("commerce-upgrade-required");if(session!=null)return Reject("session-busy");
                    if(command is BuyFloatSupply){
                        var buy=(BuyFloatSupply)command;var quote=buy.Quote;
                        if(quote.ProfileId!=state.ProfileId||quote.BranchId!=state.BranchId||quote.StateSequence!=state.Sequence)return Reject("quote-stale");
                        if(quote.CatalogVersion!=catalog.Version||quote.CatalogSignature!=catalog.Signature||quote.PackCount>catalog.MaximumPurchasePacks)return Reject("quote-invalid");
                        if(quote.Product==FloatSupplyProduct.Net&&quote.PackCount!=1)return Reject("quote-invalid");
                        int units=checked(quote.PackCount*(quote.Product==FloatSupplyProduct.Bait?catalog.BaitPackUnits:1));
                        long total=checked(quote.PackCount*(quote.Product==FloatSupplyProduct.Net?catalog.NetPriceMinor:quote.Product==FloatSupplyProduct.Bait?catalog.BaitPackPriceMinor:catalog.FoodPriceMinor));
                        if(quote.Units!=units||quote.TotalPriceMinor!=total)return Reject("quote-invalid");
                        if(quote.Product==FloatSupplyProduct.Net){
                            if(net!=null)return Reject("net-already-owned");if(buy.NewNetId==Guid.Empty||AssetExists(state,buy.NewNetId)||FishWasCreated(state,buy.NewNetId))return Reject("asset-identity-used");
                            net=new FloatOwnedNet(buy.NewNetId,catalog.NetDefinitionKey);
                        }else{
                            if(buy.NewNetId!=Guid.Empty)return Reject("purchase-asset-invalid");
                            if(quote.Product==FloatSupplyProduct.Bait){baits=checked(baits+units);if(baits>catalog.MaximumBaitCount)return Reject("bait-limit-exceeded");}
                            else{foods=checked(foods+units);if(foods>catalog.MaximumFoodCount)return Reject("food-limit-exceeded");}
                        }
                        if(wallet<total)return Reject("wallet-insufficient");wallet=checked(wallet-total);outcome=FloatOutcome.SuppliesPurchased;
                    }else if(command is EquipFloatNet){
                        Guid desired=((EquipFloatNet)command).NetId;
                        if(desired!=Guid.Empty&&(net==null||net.Id!=desired))return Reject("net-not-owned");
                        if(equipped==desired)return Reject("net-equipment-unchanged");equipped=desired;outcome=FloatOutcome.NetEquipped;
                    }else{
                        if(((UseFloatFood)command).FoodStackId!=foodStack)return Reject("food-not-owned");if(foods<=0)return Reject("food-empty");if(stamina>=rules.MaximumStamina)return Reject("stamina-full");
                        stamina=(int)Math.Min((long)rules.MaximumStamina,(long)stamina+catalog.FoodStaminaRestore);foods=checked(foods-1);outcome=FloatOutcome.FoodUsed;
                    }
                }else if(command is CastFloatWithEquipment){
                    if(session!=null)return Reject("session-busy");var cast=(CastFloatWithEquipment)command;
                    if(AssetExists(state,cast.SessionId))return Reject("session-identity-used");
                    var readiness=equipment.EvaluateCastReadiness(FloatEquipmentGate.Idle,cast.RequiredLineMm,baits,stamina,rules.CastStaminaCost);
                    if(!readiness.Valid)return Reject(readiness.Issues[0].Code);
                    baits=checked(baits-1);stamina=checked(stamina-rules.CastStaminaCost);
                    session=new FloatSession(command.SessionId,command.EventId,equipment.RigRevision,rules.Version,null,equipment.InventoryRevision,equipment.RulesVersion,cast.RequiredLineMm);outcome=FloatOutcome.CastCommitted;
                }else if(command is CastFloat){
                    if(schema==3)return Reject("equipment-cast-required");
                    if(session!=null)return Reject("session-busy");if(baits<1)return Reject("bait-empty");if(stamina<rules.CastStaminaCost)return Reject("stamina-insufficient");
                    baits=checked(baits-1);stamina=checked(stamina-rules.CastStaminaCost);session=new FloatSession(command.SessionId,command.EventId,state.Rig.Revision,rules.Version);outcome=FloatOutcome.CastCommitted;
                }else if(command is RecallFloat){
                    if(session==null||session.Id!=command.SessionId)return Reject("session-not-current");if(session.CapacityBlocked)return Reject("release-current-required");
                    session=null;outcome=FloatOutcome.Recalled;
                }else if(command is ReleaseCurrentFloatFish){
                    if(session==null||session.Id!=command.SessionId||!session.CapacityBlocked)return Reject("pending-fish-not-current");
                    affected.Add(session.PendingFish.Id);encounterId=session.PendingFish.EncounterId;session=null;outcome=FloatOutcome.Released;
                }else if(command is RecoverFloatSession){
                    if(session==null||session.Id!=command.SessionId)return Reject("session-not-current");
                    if(session.CapacityBlocked)return Reject("pending-result-requires-release");
                    session=null;outcome=FloatOutcome.Recovered;
                }else if(command is HostFloatUnhooked){
                    var unhooked=(HostFloatUnhooked)command;
                    if(schema!=2&&schema!=3)return Reject("unhook-upgrade-required");
                    if(session==null||session.Id!=command.SessionId)return Reject("session-not-current");
                    if(session.CapacityBlocked)return Reject("pending-fish-already-set");
                    if(EncounterWasUsed(state,unhooked.EncounterId))return Reject("encounter-identity-used");
                    encounterId=unhooked.EncounterId;session=null;outcome=FloatOutcome.Unhooked;
                }else if(command is HostFloatFish){
                    var host=(HostFloatFish)command;
                    if(session==null||session.Id!=command.SessionId)return Reject("session-not-current");if(session.CapacityBlocked)return Reject("pending-fish-already-set");
                    var rule=rules.Find(host.SpeciesKey);if(rule==null)return Reject("configuration-missing");
                    if(host.MassMg<rule.MinimumMassMg||host.MassMg>rule.MaximumMassMg)return Reject("fish-mass-invalid");
                    if(AssetExists(state,host.FishId)||FishWasCreated(state,host.FishId))return Reject("fish-identity-used");
                    if(EncounterWasUsed(state,host.EncounterId))return Reject("encounter-identity-used");
                    var caught=new FloatFish(host.FishId,session.Id,host.EncounterId,host.SpeciesKey,host.MassMg,rule.Value(host.MassMg),rules.Version);affected.Add(caught.Id);encounterId=host.EncounterId;
                    if(fish.Count>=rules.CreelCapacity){session=new FloatSession(session.Id,session.ReleaseEventId,session.RigRevision,session.RulesVersion,caught,session.EquipmentInventoryRevision,session.EquipmentRulesVersion,session.RequiredLineMm);outcome=FloatOutcome.CapacityRejected;}
                    else{fish.Add(caught);session=null;outcome=FloatOutcome.Landed;}
                }else if(command is SellFloatFish){
                    if(session!=null)return Reject("session-busy");var sale=(SellFloatFish)command;if(sale.FishIds.Count==0)return Reject("fish-selection-invalid");
                    var selected=new HashSet<Guid>();long sum=0;
                    foreach(var id in sale.FishIds){
                        if(id==Guid.Empty||!selected.Add(id))return Reject("fish-selection-invalid");FloatFish found=null;
                        foreach(var row in fish)if(row.Id==id){found=row;break;}if(found==null)return Reject("fish-not-owned");
                        sum=checked(sum+found.SaleValueMinor);affected.Add(id);
                    }
                    wallet=checked(wallet+sum);if(wallet>rules.MaximumWalletMinor)return Reject("wallet-limit-exceeded");
                    fish.RemoveAll(row=>selected.Contains(row.Id));outcome=FloatOutcome.Sold;
                }else return Reject("command-unsupported");
                long sequence=checked(state.Sequence+1);
                var receipt=new FloatReceipt(command,sequence,rules.Version,outcome,encounterId,state.WalletMinor,wallet,state.Stamina,stamina,state.BaitCount,baits,affected,state.FoodCount,foods,state.OwnedNet==null?Guid.Empty:state.OwnedNet.Id,net==null?Guid.Empty:net.Id,state.EquippedNetId,equipped);
                var history=new List<FloatReceipt>(state.Receipts);history.Add(receipt);
                var candidate=new FloatGameState(state.ProfileId,branch,sequence,wallet,stamina,state.BaitStackId,state.BaitDefinitionKey,baits,state.LegacyRig,session,fish,history,rules,schema,catalog,foodStack,foods,net,equipped,origin,equipment);
                return new FloatReduction(Code(outcome),candidate,receipt);
            }catch(OverflowException){return Reject("arithmetic-overflow");}
        }
    }
}
