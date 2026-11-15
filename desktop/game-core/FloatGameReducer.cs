using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

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
    internal enum FloatBaitAction : byte { PrepareBread=1, SelectBread=2, SelectWorm=3 }
    internal sealed class ManageFloatBait : FloatCommand
    {
        internal const int PortionsPerBread=3;
        internal readonly FloatBaitAction Action;
        internal ManageFloatBait(Guid profile,Guid branch,Guid request,Guid businessEvent,FloatBaitAction action,long sequence,long rig,long rules)
            :base("manage-float-bait-v1",profile,branch,request,businessEvent,Guid.Empty,sequence,rig,rules)
        {
            if(action!=FloatBaitAction.PrepareBread&&action!=FloatBaitAction.SelectBread&&action!=FloatBaitAction.SelectWorm)
                throw new ArgumentOutOfRangeException("action");
            Action=action;
        }
        internal override string Detail {get{return FloatGuard.Number((int)Action);}}
    }
    internal sealed class BuyLureKit : FloatCommand
    {
        internal readonly OwnedLureKit Kit;
        internal BuyLureKit(Guid profile,Guid branch,Guid request,Guid businessEvent,OwnedLureKit kit,long sequence,long rigRevision,long rulesVersion)
            :base("buy-lure-kit-v1",profile,branch,request,businessEvent,Guid.Empty,sequence,rigRevision,rulesVersion)
        {if(kit==null)throw new ArgumentNullException("kit");Kit=kit;}
        internal override string Detail {get{return String.Join("|",Kit.Ids);}}
    }
    internal sealed class CastLureWithEquipment : FloatCommand
    {
        internal readonly Guid RigId,LineId,LureId,HookId;
        internal readonly long KitRigRevision,LineRevision,LureRevision,RequiredLineMm;
        internal CastLureWithEquipment(Guid profile,Guid branch,Guid request,Guid businessEvent,Guid session,
            Guid rig,Guid line,Guid lure,Guid hook,long kitRigRevision,long lineRevision,long lureRevision,long requiredLineMm,
            long sequence,long expectedRigRevision,long rulesVersion)
            :base("cast-lure-kit-v1",profile,branch,request,businessEvent,session,sequence,expectedRigRevision,rulesVersion)
        {
            var ids=new HashSet<Guid>();foreach(var id in new[]{session,rig,line,lure,hook})FloatGuard.Unique(ids,id);
            if(kitRigRevision<1||lineRevision<1||lureRevision<1||requiredLineMm<1||requiredLineMm>FloatEquipmentGuard.MaximumLineMm)
                throw new ArgumentOutOfRangeException("lure cast binding");
            RigId=rig;LineId=line;LureId=lure;HookId=hook;KitRigRevision=kitRigRevision;LineRevision=lineRevision;LureRevision=lureRevision;RequiredLineMm=requiredLineMm;
        }
        internal static CastLureWithEquipment Create(FloatGameState state,long requiredLineMm)
        {
            var k=state.OwnedLureKit;if(k==null)throw new InvalidOperationException("Lure kit not owned.");
            return new CastLureWithEquipment(state.ProfileId,state.BranchId,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
                k.RigId,k.LineId,k.LureId,k.HookId,k.RigRevision,k.LineRevision,k.LureRevision,requiredLineMm,
                state.Sequence,state.Rig.Revision,state.RulesVersion);
        }
        internal override string Detail {get{return String.Join("|",new[]{RigId,LineId,LureId,HookId})+"|"+FloatGuard.Number(KitRigRevision)+"|"+
            FloatGuard.Number(LineRevision)+"|"+FloatGuard.Number(LureRevision)+"|"+FloatGuard.Number(RequiredLineMm);}}
    }
    internal sealed class BuyBottomKit : FloatCommand
    {
        internal readonly BottomOwnedKit Kit;
        internal BuyBottomKit(Guid profile,Guid branch,Guid request,Guid businessEvent,BottomOwnedKit kit,long expectedSequence,long expectedRigRevision,long expectedRulesVersion)
            :base("buy-bottom-kit-v1",profile,branch,request,businessEvent,Guid.Empty,expectedSequence,expectedRigRevision,expectedRulesVersion)
        {if(kit==null)throw new ArgumentNullException("kit");Kit=kit;}
        internal override string Detail{get{return String.Join("|",Kit.Ids);}}
    }
    internal sealed class BuyAdditionalBottomKit : FloatCommand
    {
        internal readonly int KitNumber;
        internal readonly BottomOwnedKit Kit;
        internal BuyAdditionalBottomKit(Guid profile,Guid branch,Guid request,Guid businessEvent,int kitNumber,BottomOwnedKit kit,
            long expectedSequence,long expectedRigRevision,long expectedRulesVersion)
            :base("buy-additional-bottom-kit-v1",profile,branch,request,businessEvent,Guid.Empty,expectedSequence,expectedRigRevision,expectedRulesVersion)
        {
            if(kitNumber<2||kitNumber>3)throw new ArgumentOutOfRangeException("kitNumber");
            if(kit==null)throw new ArgumentNullException("kit");
            KitNumber=kitNumber;Kit=kit;
        }
        internal override string Detail{get{return FloatGuard.Number(KitNumber)+"|"+String.Join("|",Kit.Ids);}}
    }
    internal sealed class BuyBankRodStand : FloatCommand
    {
        internal readonly OwnedBankRodStand Stand;
        internal BuyBankRodStand(Guid profile,Guid branch,Guid request,Guid businessEvent,Guid standId,
            long expectedSequence,long expectedRigRevision,long expectedRulesVersion)
            :base("buy-bank-rod-stand-v1",profile,branch,request,businessEvent,Guid.Empty,expectedSequence,expectedRigRevision,expectedRulesVersion)
        {Stand=new OwnedBankRodStand(standId);}
        internal override string Detail{get{return Stand.Id.ToString("N");}}
    }
    internal class CastBottomKitWithEquipment : FloatCommand
    {
        internal readonly Guid KitRigId,LineId,LeaderId;
        internal readonly long KitRigRevision,LineRevision,RequiredLineMm;
        internal CastBottomKitWithEquipment(Guid profile,Guid branch,Guid request,Guid businessEvent,Guid session,Guid kitRig,Guid line,Guid leader,
            long kitRigRevision,long lineRevision,long requiredLineMm,long expectedSequence,long expectedRigRevision,long expectedRulesVersion)
            :this("cast-bottom-kit-v1",profile,branch,request,businessEvent,session,kitRig,line,leader,kitRigRevision,lineRevision,
                requiredLineMm,expectedSequence,expectedRigRevision,expectedRulesVersion){}
        protected CastBottomKitWithEquipment(string kind,Guid profile,Guid branch,Guid request,Guid businessEvent,Guid session,Guid kitRig,Guid line,Guid leader,
            long kitRigRevision,long lineRevision,long requiredLineMm,long expectedSequence,long expectedRigRevision,long expectedRulesVersion)
            :base(kind,profile,branch,request,businessEvent,session,expectedSequence,expectedRigRevision,expectedRulesVersion)
        {
            FloatGuard.Id(session,"session");FloatGuard.Id(kitRig,"kitRig");FloatGuard.Id(line,"line");FloatGuard.Id(leader,"leader");
            if(kitRigRevision<1 || lineRevision<1 || requiredLineMm<1 || requiredLineMm>FloatEquipmentGuard.MaximumLineMm)throw new ArgumentOutOfRangeException("bottom rig binding");
            KitRigId=kitRig;LineId=line;LeaderId=leader;KitRigRevision=kitRigRevision;LineRevision=lineRevision;RequiredLineMm=requiredLineMm;
        }
        internal static CastBottomKitWithEquipment Create(FloatGameState state,long requiredLineMm)
        {
            var kit=state.OwnedBottomKit;if(kit==null)throw new InvalidOperationException("No purchased bottom kit.");
            var equipment=kit.Equipment;var line=equipment.Spool(equipment.Equipped(FloatEquipmentSlot.Reel).Id);
            return new CastBottomKitWithEquipment(state.ProfileId,state.BranchId,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),equipment.RigId,line.Id,kit.LeaderId,
                equipment.RigRevision,line.Revision,requiredLineMm,state.Sequence,state.Rig.Revision,state.RulesVersion);
        }
        internal override string Detail{get{return KitRigId.ToString("N")+"|"+LineId.ToString("N")+"|"+LeaderId.ToString("N")+"|"+
            FloatGuard.Number(KitRigRevision)+"|"+FloatGuard.Number(LineRevision)+"|"+FloatGuard.Number(RequiredLineMm);}}
    }
    internal sealed class CastBankBottomRod : CastBottomKitWithEquipment
    {
        internal CastBankBottomRod(Guid profile,Guid branch,Guid request,Guid businessEvent,Guid session,Guid kitRig,Guid line,Guid leader,
            long kitRigRevision,long lineRevision,long requiredLineMm,long sequence,long rigRevision,long rulesVersion)
            :base("cast-bank-bottom-rod-v1",profile,branch,request,businessEvent,session,kitRig,line,leader,
                kitRigRevision,lineRevision,requiredLineMm,sequence,rigRevision,rulesVersion){}
        internal static CastBankBottomRod Create(FloatGameState state,Guid rigId,long requiredLineMm)
        {
            var kit=state.BottomKitFor(rigId);if(kit==null)throw new InvalidOperationException("The bank rod must be owned.");
            var equipment=kit.Equipment;var line=equipment.Spool(equipment.Equipped(FloatEquipmentSlot.Reel).Id);
            return new CastBankBottomRod(state.ProfileId,state.BranchId,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),rigId,line.Id,kit.LeaderId,
                equipment.RigRevision,line.Revision,requiredLineMm,state.Sequence,state.Rig.Revision,state.RulesVersion);
        }
    }
    internal enum BankRodEnding : byte { Recall=1,Land=2,Release=3,RecoverStartup=4,RecoverShutdown=5,Unhooked=6 }
    internal sealed class SettleBankBottomRod : FloatCommand
    {
        internal readonly Guid RigId,FishId,EncounterId;
        internal readonly BankRodEnding Ending;
        internal readonly string Species;
        internal readonly long MassMg;
        internal readonly int FightModelVersion;
        internal SettleBankBottomRod(Guid profile,Guid branch,Guid request,Guid businessEvent,Guid rigId,Guid session,
            BankRodEnding ending,Guid fishId,Guid encounterId,string species,long massMg,long sequence,long rigRevision,long rulesVersion,int fightModelVersion=0)
            :base("settle-bank-bottom-rod-v1",profile,branch,request,businessEvent,session,sequence,rigRevision,rulesVersion)
        {
            FloatGuard.Id(rigId,"rig");FloatGuard.Id(session,"session");
            if(!Enum.IsDefined(typeof(BankRodEnding),ending))throw new ArgumentOutOfRangeException("ending");
            if(ending==BankRodEnding.Land){
                FloatGuard.Id(fishId,"fish");FloatGuard.Id(encounterId,"encounter");species=FloatGuard.Key(species);
                if(massMg<=0)throw new ArgumentOutOfRangeException("massMg");
            }else if(ending==BankRodEnding.Unhooked){
                FloatGuard.Id(encounterId,"encounter");
                if(fishId!=Guid.Empty||species!=null||massMg!=0||fightModelVersion!=2)throw new ArgumentException("Invalid bank unhook facts.");
            }else if(fishId!=Guid.Empty||encounterId!=Guid.Empty||species!=null||massMg!=0)throw new ArgumentException("Unexpected fish data.");
            if(ending!=BankRodEnding.Unhooked&&fightModelVersion!=0)throw new ArgumentException("Unexpected fight model.");
            RigId=rigId;Ending=ending;FishId=fishId;EncounterId=encounterId;Species=species;MassMg=massMg;FightModelVersion=fightModelVersion;
        }
        internal static SettleBankBottomRod Create(FloatGameState state,Guid rigId,Guid session,BankRodEnding ending)
        {return new SettleBankBottomRod(state.ProfileId,state.BranchId,Guid.NewGuid(),Guid.NewGuid(),rigId,session,ending,Guid.Empty,Guid.Empty,null,0,state.Sequence,state.Rig.Revision,state.RulesVersion);}
        internal static SettleBankBottomRod Land(FloatGameState state,Guid rigId,Guid session,Guid fishId,Guid encounterId,string species,long massMg,Guid? eventId=null)
        {return new SettleBankBottomRod(state.ProfileId,state.BranchId,Guid.NewGuid(),eventId??Guid.NewGuid(),rigId,session,BankRodEnding.Land,fishId,encounterId,species,massMg,state.Sequence,state.Rig.Revision,state.RulesVersion);}
        internal static SettleBankBottomRod Unhooked(FloatGameState state,Guid rigId,Guid session,Guid encounterId,int fightModelVersion,Guid eventId)
        {return new SettleBankBottomRod(state.ProfileId,state.BranchId,Guid.NewGuid(),eventId,rigId,session,BankRodEnding.Unhooked,Guid.Empty,encounterId,null,0,state.Sequence,state.Rig.Revision,state.RulesVersion,fightModelVersion);}
        internal override string Detail{get{return RigId.ToString("N")+"|"+FloatGuard.Number((byte)Ending)+"|"+FishId.ToString("N")+"|"+
            EncounterId.ToString("N")+"|"+Species+"|"+FloatGuard.Number(MassMg)+"|"+FloatGuard.Number(FightModelVersion);}}
    }
    internal enum FloatClothingAction : byte { Buy=1,Wear=2,Remove=3 }
    internal sealed class ChangeFloatClothing : FloatCommand
    {
        internal readonly string ItemId;
        internal readonly FloatClothingAction Action;
        internal ChangeFloatClothing(Guid profile,Guid branch,Guid request,Guid businessEvent,FloatClothingAction action,string itemId,
            long expectedSequence,long expectedRigRevision,long expectedRulesVersion)
            :base("clothing",profile,branch,request,businessEvent,Guid.Empty,expectedSequence,expectedRigRevision,expectedRulesVersion)
        {
            if(action!=FloatClothingAction.Buy && action!=FloatClothingAction.Wear && action!=FloatClothingAction.Remove)
                throw new ArgumentOutOfRangeException("action");
            ItemId=FloatGuard.Key(itemId);Action=action;
        }
        internal override string Detail{get{return FloatGuard.Number((byte)Action)+"|"+ItemId;}}
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
    // Host evidence only. This transaction does not decide when a physical break
    // occurred; it atomically applies one explicit rule to the locked assembly.
    internal sealed class HostFloatTerminalLoss : FloatEquipmentCommand
    {
        internal readonly Guid EncounterId,LineId;
        internal readonly long ExpectedLineRevision,OutLineMm,CutDistanceFromReelMm;
        internal readonly string LossRuleKey;
        internal readonly int LossModelVersion;
        internal HostFloatTerminalLoss(Guid profile,Guid branch,Guid request,Guid businessEvent,Guid session,Guid encounter,Guid lineId,long expectedLineRevision,string lossRuleKey,long outLineMm,long cutDistanceFromReelMm,long expectedSequence,long expectedRigRevision,long expectedRulesVersion,long expectedInventoryRevision,long expectedEquipmentVersion,int lossModelVersion)
            :base("host-terminal-loss",profile,branch,request,businessEvent,session,expectedSequence,expectedRigRevision,expectedRulesVersion,expectedInventoryRevision,expectedEquipmentVersion)
        {
            FloatGuard.Id(session,"session");FloatGuard.Id(encounter,"encounter");FloatGuard.Id(lineId,"line");
            if(expectedLineRevision<1||outLineMm<1||outLineMm>FloatEquipmentGuard.MaximumLineMm||cutDistanceFromReelMm<0||cutDistanceFromReelMm>outLineMm||lossModelVersion!=1)throw new ArgumentOutOfRangeException("terminal loss values");
            EncounterId=encounter;LineId=lineId;ExpectedLineRevision=expectedLineRevision;LossRuleKey=FloatEquipmentGuard.Key(lossRuleKey);OutLineMm=outLineMm;CutDistanceFromReelMm=cutDistanceFromReelMm;LossModelVersion=lossModelVersion;
        }
        internal override string Detail{get{return EquipmentDetail+"|"+EncounterId.ToString("N")+"|"+LineId.ToString("N")+"|"+FloatGuard.Number(ExpectedLineRevision)+"|"+LossRuleKey+"|"+FloatGuard.Number(OutLineMm)+"|"+FloatGuard.Number(CutDistanceFromReelMm)+"|"+FloatGuard.Number(LossModelVersion);}}
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
        {if(state.BankGearIdentityUsed(id)||state.BankJournalIdentityUsed(id)||state.ArchivedEquipmentIdentityWasUsed(id))return true;foreach(var row in state.Receipts)if(((row.Outcome==FloatOutcome.Landed||row.Outcome==FloatOutcome.CapacityRejected||row.Outcome==FloatOutcome.Unhooked||row.Outcome==FloatOutcome.TerminalLost)&&row.EncounterId==id)||((row.Outcome==FloatOutcome.TerminalLost||row.Outcome==FloatOutcome.CampWorkRewarded)&&row.EventId==id))return true;return false;}
        // Loss and paid camp-work identities are durable reservations. Only
        // histories containing these outcomes acquire their respective rules;
        // earlier journals keep their original identity behavior.
        static bool TerminalIdentityWasUsed(FloatGameState state,Guid id)
        {foreach(var row in state.Receipts)if((row.Outcome==FloatOutcome.TerminalLost&&(row.EventId==id||row.EncounterId==id))||(row.Outcome==FloatOutcome.CampWorkRewarded&&row.EventId==id))return true;return false;}
        static bool EventWasUsed(FloatGameState state,Guid id)
        {foreach(var row in state.Receipts)if(row.EventId==id)return true;return false;}
        static bool RequestWasUsed(FloatGameState state,Guid id)
        {foreach(var row in state.Receipts)if(row.RequestId==id)return true;return false;}
        static bool AssetExists(FloatGameState state,Guid id)
        {var ids=new HashSet<Guid>();state.AddEquipmentAssets(ids);ids.Add(state.BaitStackId);if(state.FoodStackId!=Guid.Empty)ids.Add(state.FoodStackId);if(state.OwnedNet!=null)ids.Add(state.OwnedNet.Id);foreach(var fish in state.Creel)ids.Add(fish.Id);if(state.Session!=null&&state.Session.PendingFish!=null)ids.Add(state.Session.PendingFish.Id);return ids.Contains(id)||state.BankGearIdentityUsed(id)||state.BankJournalIdentityUsed(id)||TerminalIdentityWasUsed(state,id)||state.ArchivedEquipmentIdentityWasUsed(id);}
        static bool EquipmentPurchaseIdentityUsed(FloatGameState state,FloatCommand command,Guid id)
        {
            return id==command.RequestId||id==command.EventId||KnownPurchaseIdentity(state,id);
        }
        static bool KnownPurchaseIdentity(FloatGameState state,Guid id)
        {
            if(id==state.ProfileId||id==state.BranchId||AssetExists(state,id)||FishWasCreated(state,id)||UsedSession(state,id)||EncounterWasUsed(state,id)||EventWasUsed(state,id)||RequestWasUsed(state,id))return true;
            foreach(var item in state.Equipment.Instances)if(item.Location.Place==FloatEquipmentPlace.Lost&&item.Location.OwnerId==id)return true;
            return false;
        }
        static bool CampWorkIdentityUsed(FloatGameState state,FloatCommand command)
        {
            Guid id=command.EventId;
            if(id==command.RequestId||id==state.ProfileId||id==state.BranchId||AssetExists(state,id)||FishWasCreated(state,id)||UsedSession(state,id)||EncounterWasUsed(state,id)||EventWasUsed(state,id)||RequestWasUsed(state,id))return true;
            if((state.UpgradeOrigin!=null&&state.UpgradeOrigin.SourceBranchId==id)||(state.EquipmentUpgradeOrigin!=null&&state.EquipmentUpgradeOrigin.SourceBranchId==id))return true;
            foreach(var item in state.Equipment.Instances)if(item.Location.Place==FloatEquipmentPlace.Lost&&item.Location.OwnerId==id)return true;
            return false;
        }
        static string Code(FloatOutcome outcome)
        {switch(outcome){
            case FloatOutcome.BreadBaitPrepared:return "bread-bait-prepared";
            case FloatOutcome.FloatBaitSelected:return "float-bait-selected";
            case FloatOutcome.BottomKitPurchased:return "bottom-kit-purchase-candidate";
            case FloatOutcome.LureKitPurchased:return "lure-kit-purchase-candidate";
            case FloatOutcome.AdditionalBottomKitPurchased:return "additional-bottom-kit-purchase-candidate";
            case FloatOutcome.BankRodStandPurchased:return "bank-rod-stand-purchase-candidate";
            case FloatOutcome.ClothingPurchased:return "clothing-purchase-candidate";
            case FloatOutcome.ClothingWorn:return "clothing-wear-candidate";
            case FloatOutcome.ClothingRemoved:return "clothing-remove-candidate";
            case FloatOutcome.CastCommitted:return "cast-candidate";case FloatOutcome.Recalled:return "recall-candidate";case FloatOutcome.Landed:return "landed-candidate";case FloatOutcome.CapacityRejected:return "creel-capacity-exceeded";case FloatOutcome.Released:return "released-candidate";case FloatOutcome.Recovered:return "recovered-candidate";case FloatOutcome.ProfileUpgraded:return "upgrade-candidate";case FloatOutcome.SuppliesPurchased:return "purchase-candidate";case FloatOutcome.NetEquipped:return "equip-candidate";case FloatOutcome.FoodUsed:return "food-candidate";case FloatOutcome.Unhooked:return "unhooked-candidate";case FloatOutcome.SlotReplaced:return "assembly-candidate";case FloatOutcome.LineMoved:return "line-move-candidate";case FloatOutcome.TerminalLost:return "terminal-loss-candidate";case FloatOutcome.EquipmentOffersConfigured:return "equipment-offers-candidate";case FloatOutcome.EquipmentPurchased:return "equipment-purchase-candidate";case FloatOutcome.EquipmentProfileUpgraded:return "equipment-upgrade-candidate";case FloatOutcome.EquipmentArchived:return "equipment-archive-candidate";case FloatOutcome.CampWorkRewarded:return "camp-work-candidate";default:return "sale-candidate";}}
        public static FloatReduction Reduce(FloatGameState state,FloatCommand command,FloatGameRules rules)
        {
            if(state==null)throw new ArgumentNullException("state");if(command==null)throw new ArgumentNullException("command");
            if(command.ProfileId!=state.ProfileId||command.BranchId!=state.BranchId)return Reject("branch-mismatch");
            string fingerprint=command.Fingerprint;
            foreach(var row in state.Receipts)if(row.RequestId==command.RequestId)
                return row.Fingerprint==fingerprint?new FloatReduction(Code(row.Outcome),receipt:row,replay:true):Reject("request-conflict");
            if(state.BankGearIdentityUsed(command.RequestId)||state.BankJournalIdentityUsed(command.RequestId)||state.ArchivedEquipmentIdentityWasUsed(command.RequestId))return Reject("request-identity-used");
            if(state.BankGearIdentityUsed(command.EventId)||state.BankJournalIdentityUsed(command.EventId)||state.ArchivedEquipmentIdentityWasUsed(command.EventId))return Reject("event-identity-used");
            if(state.BankGearIdentityUsed(command.SessionId)||state.ArchivedEquipmentIdentityWasUsed(command.SessionId))return Reject("session-identity-used");
            if(TerminalIdentityWasUsed(state,command.RequestId))return Reject("request-identity-used");
            foreach(var row in state.Receipts)if(row.EventId==command.EventId)return Reject("event-already-used");
            if(TerminalIdentityWasUsed(state,command.EventId))return Reject("event-identity-used");
            if((command is CastFloat||command is CastFloatWithEquipment||command is CastBottomKitWithEquipment||command is CastLureWithEquipment)&&UsedSession(state,command.SessionId))return Reject("session-already-used");
            if(command.ExpectedSequence!=state.Sequence||command.ExpectedRigRevision!=state.Rig.Revision||command.ExpectedRulesVersion!=state.RulesVersion)return Reject("version-conflict");
            if(rules==null)return Reject("configuration-missing");
            if(rules.Signature!=state.Rules.Signature)return Reject("configuration-mismatch");
            var bankCast=command as CastBankBottomRod;
            var bankEnd=command as SettleBankBottomRod;
            bool bankOperation=bankCast!=null||bankEnd!=null;
            if(state.BankSessions.Count>0&&!bankOperation)return Reject("session-busy");
            if(bankOperation){
                if(!BottomOwnedKit.Enabled(state)||state.OwnedBankRodStand==null)return Reject("bank-stand-required");
                if(state.Session!=null)return Reject("session-busy");
                if(state.HasPendingBankResult&&(bankCast!=null||bankEnd.Ending==BankRodEnding.Land||bankEnd.Ending==BankRodEnding.Unhooked))return Reject("bank-result-pending");
                if(command.RequestId==command.EventId||KnownPurchaseIdentity(state,command.RequestId)||KnownPurchaseIdentity(state,command.EventId))return Reject("bank-identity-invalid");
            }
            var equipmentCommand=command as FloatEquipmentCommand;
            if(equipmentCommand!=null){
                if(state.SchemaVersion!=3)return Reject("equipment-schema-required");
                if(equipmentCommand.ExpectedInventoryRevision!=state.Equipment.InventoryRevision||equipmentCommand.ExpectedEquipmentVersion!=state.Equipment.RulesVersion)return Reject("equipment-version-conflict");
            }
            var archiveCommand=command as ArchiveLostFloatEquipment;
            if(archiveCommand!=null){
                if(state.SchemaVersion!=3)return Reject("equipment-schema-required");
                if(archiveCommand.ExpectedInventoryRevision!=state.Equipment.InventoryRevision||archiveCommand.ExpectedEquipmentVersion!=state.Equipment.RulesVersion)return Reject("equipment-version-conflict");
            }
            long wallet=state.WalletMinor;int stamina=state.Stamina,baits=state.BaitCount;var fish=new List<FloatFish>(state.Creel);var session=state.Session;var affected=new List<Guid>();Guid encounterId=Guid.Empty;FloatOutcome outcome;
            int schema=state.SchemaVersion,foods=state.FoodCount;Guid branch=state.BranchId,foodStack=state.FoodStackId,equipped=state.EquippedNetId;var net=state.OwnedNet;var catalog=state.CommerceCatalog;var origin=state.UpgradeOrigin;
            var equipment=state.Equipment;var equipmentOffers=state.EquipmentOffers;var equipmentOrigin=state.EquipmentUpgradeOrigin;FloatEquipmentLossSummary equipmentLoss=null;FloatEquipmentArchiveSummary equipmentArchive=null;
            var bankSessions=new List<FloatSession>(state.BankSessions);
            int breadBaits=state.BreadBaitCount;
            bool breadSelected=state.BreadBaitSelected;
            try{
                if(command is ManageFloatBait){
                    if(!state.BreadRecipeAvailable)return Reject("bread-recipe-unavailable");
                    if(session!=null)return Reject("session-busy");
                    var action=((ManageFloatBait)command).Action;
                    if(action==FloatBaitAction.PrepareBread){
                        if(foods<1)return Reject("food-empty");
                        if(breadBaits>catalog.MaximumBaitCount-ManageFloatBait.PortionsPerBread)return Reject("bait-limit-exceeded");
                        foods--;breadBaits+=ManageFloatBait.PortionsPerBread;outcome=FloatOutcome.BreadBaitPrepared;
                    }else{
                        bool desired=action==FloatBaitAction.SelectBread;
                        if(desired==breadSelected)return Reject("float-bait-unchanged");
                        if((desired?breadBaits:baits)<1)return Reject("bait-empty");
                        breadSelected=desired;outcome=FloatOutcome.FloatBaitSelected;
                    }
                }else if(command is BuyLureKit){
                    if(!OwnedLureKit.Enabled(state))return Reject("lure-kit-unavailable");
                    if(session!=null)return Reject("session-busy");
                    if(state.OwnedLureKit!=null)return Reject("lure-kit-already-owned");
                    if(wallet<OwnedLureKit.PriceMinor)return Reject("wallet-insufficient");
                    if(command.RequestId==command.EventId||KnownPurchaseIdentity(state,command.RequestId)||KnownPurchaseIdentity(state,command.EventId))
                        return Reject("purchase-identity-invalid");
                    foreach(var id in ((BuyLureKit)command).Kit.Ids)if(EquipmentPurchaseIdentityUsed(state,command,id))return Reject("asset-identity-used");
                    wallet-=OwnedLureKit.PriceMinor;outcome=FloatOutcome.LureKitPurchased;
                }else if(command is BuyBottomKit){
                    if(!BottomOwnedKit.Enabled(state))return Reject("bottom-kit-unavailable");
                    if(session!=null)return Reject("session-busy");
                    if(state.OwnedBottomKit!=null)return Reject("bottom-kit-already-owned");
                    if(wallet<BottomOwnedKit.PriceMinor)return Reject("wallet-insufficient");
                    var kit=((BuyBottomKit)command).Kit;
                    foreach(var id in kit.Ids)if(EquipmentPurchaseIdentityUsed(state,command,id))return Reject("asset-identity-used");
                    wallet-=BottomOwnedKit.PriceMinor;outcome=FloatOutcome.BottomKitPurchased;
                }else if(command is BuyAdditionalBottomKit || command is BuyBankRodStand){
                    if(!BottomOwnedKit.Enabled(state))return Reject("bank-gear-unavailable");
                    if(session!=null)return Reject("session-busy");
                    if(command.RequestId==command.EventId || KnownPurchaseIdentity(state,command.RequestId) ||
                        KnownPurchaseIdentity(state,command.EventId))return Reject("purchase-identity-invalid");
                    var additional=command as BuyAdditionalBottomKit;
                    long price;
                    if(additional!=null){
                        if(state.OwnedBottomKits.Count>=additional.KitNumber)return Reject("bottom-kit-already-owned");
                        if(state.OwnedBottomKits.Count+1!=additional.KitNumber)return Reject("bottom-kit-previous-required");
                        foreach(var id in additional.Kit.Ids)if(EquipmentPurchaseIdentityUsed(state,command,id))return Reject("asset-identity-used");
                        price=BottomOwnedKit.AdditionalPriceMinor;outcome=FloatOutcome.AdditionalBottomKitPurchased;
                    }else{
                        if(state.OwnedBankRodStand!=null)return Reject("bank-rod-stand-already-owned");
                        if(EquipmentPurchaseIdentityUsed(state,command,((BuyBankRodStand)command).Stand.Id))return Reject("asset-identity-used");
                        price=OwnedBankRodStand.PriceMinor;outcome=FloatOutcome.BankRodStandPurchased;
                    }
                    if(wallet<price)return Reject("wallet-insufficient");
                    wallet-=price;
                }else if(command is HostFloatCampReward){
                    var reward=(HostFloatCampReward)command;
                    if(reward.PolicyVersion!=FloatCampWorkRules.PolicyVersion)return Reject("camp-policy-unsupported");
                    if(reward.ActiveMilliseconds!=FloatCampWorkRules.DurationMilliseconds)return Reject("camp-duration-invalid");
                    string refused=FloatCampWorkRules.Check(state);if(refused!=null)return Reject(refused);
                    if(CampWorkIdentityUsed(state,command))return Reject("camp-identity-invalid");
                    wallet=checked(wallet+FloatCampWorkRules.RewardMinor);outcome=FloatOutcome.CampWorkRewarded;
                }else if(command is ChangeFloatClothing){
                    var change=(ChangeFloatClothing)command;
                    if(!FloatClothingCatalog.Enabled(state))return Reject("clothing-unavailable");
                    if(session!=null)return Reject("session-busy");
                    var item=FloatClothingCatalog.Find(change.ItemId);
                    if(item==null)return Reject("clothing-item-unknown");
                    bool owned=state.OwnedClothingIds.Contains(item.Id);
                    switch(change.Action){
                    case FloatClothingAction.Buy:
                        if(owned)return Reject("clothing-already-owned");
                        if(wallet<item.PriceMinor)return Reject("wallet-insufficient");
                        wallet-=item.PriceMinor;outcome=FloatOutcome.ClothingPurchased;break;
                    case FloatClothingAction.Wear:
                        if(!owned)return Reject("clothing-not-owned");
                        if(state.WornClothingId==item.Id)return Reject("clothing-unchanged");
                        outcome=FloatOutcome.ClothingWorn;break;
                    default:
                        if(state.WornClothingId!=item.Id)return Reject("clothing-not-worn");
                        outcome=FloatOutcome.ClothingRemoved;break;
                    }
                }else if(archiveCommand!=null){
                    if(session!=null)return Reject("session-busy");
                    var selected=new HashSet<Guid>(archiveCommand.ItemIds);var owners=new HashSet<Guid>();long lineMm=0;
                    foreach(var id in archiveCommand.ItemIds){
                        var item=equipment.Find(id);if(item==null)return Reject("item-not-found");
                        if(item.Location.Place!=FloatEquipmentPlace.Lost||item.RemainingLineMm!=0)return Reject("item-not-lost");
                        owners.Add(item.Location.OwnerId);lineMm=checked(lineMm+item.InitialLineMm);
                    }
                    // Old genesis tombstones can lack a loss receipt. Their
                    // owner identities must survive removal too, but cannot
                    // become aliases of retained ownership or this transaction.
                    var retainedAssets=new HashSet<Guid>();state.AddEquipmentAssets(retainedAssets);retainedAssets.ExceptWith(selected);
                    retainedAssets.Add(state.ProfileId);retainedAssets.Add(state.BranchId);retainedAssets.Add(state.BaitStackId);retainedAssets.Add(state.FoodStackId);
                    if(state.OwnedNet!=null)retainedAssets.Add(state.OwnedNet.Id);foreach(var caught in state.Creel)retainedAssets.Add(caught.Id);
                    foreach(var id in selected)if(id==command.RequestId||id==command.EventId||retainedAssets.Contains(id))return Reject("archive-identity-invalid");
                    foreach(var id in owners)if(id==command.RequestId||id==command.EventId||selected.Contains(id)||retainedAssets.Contains(id))return Reject("archive-identity-invalid");
                    var items=new List<FloatEquipmentInstance>();foreach(var item in equipment.Instances)if(!selected.Contains(item.Id))items.Add(item);
                    var nextEquipment=new FloatEquipmentLedger(equipment.RigId,equipment.RigRevision,checked(equipment.InventoryRevision+1),equipment.Catalog,items);
                    if(equipment.InitialLineMm-nextEquipment.InitialLineMm!=lineMm||equipment.RemainingLineMm!=nextEquipment.RemainingLineMm||equipment.LostLineMm-nextEquipment.LostLineMm!=lineMm)
                        throw new InvalidOperationException("Equipment archive violated line conservation.");
                    equipmentArchive=new FloatEquipmentArchiveSummary(archiveCommand.ItemIds,owners,lineMm);equipment=nextEquipment;outcome=FloatOutcome.EquipmentArchived;
                }else if(command is UpgradeFloatEquipment){
                    if(schema!=2)return Reject("equipment-upgrade-source-required");if(session!=null)return Reject("session-busy");
                    var upgrade=(UpgradeFloatEquipment)command;
                    if(AssetExists(state,upgrade.NewBranchId)||FishWasCreated(state,upgrade.NewBranchId)||UsedSession(state,upgrade.NewBranchId)||EncounterWasUsed(state,upgrade.NewBranchId)||EventWasUsed(state,upgrade.NewBranchId)||RequestWasUsed(state,upgrade.NewBranchId)||
                        (origin!=null&&origin.SourceBranchId==upgrade.NewBranchId))return Reject("branch-identity-used");
                    try{
                        equipment=upgrade.ValidateMapping(state);
                        equipmentOffers=FloatEquipmentOffersCodec.Decode(upgrade.CopyOffersPayload(),equipment.Catalog,rules.MaximumWalletMinor);
                    }catch(ArgumentException){return Reject("equipment-upgrade-invalid");}
                    catch(InvalidDataException){return Reject("equipment-upgrade-invalid");}
                    catch(InvalidOperationException){return Reject("equipment-upgrade-invalid");}
                    equipmentOrigin=upgrade.Origin;schema=3;branch=upgrade.NewBranchId;outcome=FloatOutcome.EquipmentProfileUpgraded;
                }else if(command is ConfigureFloatEquipmentOffers){
                    if(session!=null)return Reject("session-busy");if(equipmentOffers!=null)return Reject("equipment-offers-already-configured");
                    try{equipmentOffers=FloatEquipmentOffersCodec.Decode(((ConfigureFloatEquipmentOffers)command).CopyPayload(),equipment.Catalog,rules.MaximumWalletMinor);}
                    catch(InvalidDataException){return Reject("equipment-offers-invalid");}
                    outcome=FloatOutcome.EquipmentOffersConfigured;
                }else if(command is BuyFloatEquipment){
                    if(session!=null)return Reject("session-busy");if(equipmentOffers==null)return Reject("equipment-offers-missing");
                    var buy=(BuyFloatEquipment)command;var quote=buy.Quote;
                    if(quote.ProfileId!=state.ProfileId||quote.BranchId!=state.BranchId||quote.StateSequence!=state.Sequence)return Reject("equipment-quote-stale");
                    var offer=equipmentOffers.Find(quote.Sku);
                    if(quote.CatalogVersion!=equipmentOffers.Version||quote.CatalogSignature!=equipmentOffers.Signature||offer==null||quote.DefinitionKey!=offer.DefinitionKey||quote.Kind!=offer.Kind||quote.PriceMinor!=offer.PriceMinor||quote.LineLengthMm!=offer.LineLengthMm)return Reject("equipment-quote-invalid");
                    if(EquipmentPurchaseIdentityUsed(state,command,buy.NewItemId))return Reject("asset-identity-used");
                    if(equipment.Instances.Count>=FloatEquipmentGuard.MaximumInstances)return Reject("equipment-capacity-exceeded");
                    if(wallet<offer.PriceMinor)return Reject("wallet-insufficient");
                    var items=new List<FloatEquipmentInstance>(equipment.Instances);items.Add(new FloatEquipmentInstance(buy.NewItemId,offer.DefinitionKey,offer.Kind,1,FloatEquipmentLocation.Inventory(),offer.LineLengthMm,offer.LineLengthMm));
                    var nextEquipment=new FloatEquipmentLedger(equipment.RigId,equipment.RigRevision,checked(equipment.InventoryRevision+1),equipment.Catalog,items);
                    if(nextEquipment.InitialLineMm!=checked(equipment.InitialLineMm+offer.LineLengthMm)||nextEquipment.RemainingLineMm!=checked(equipment.RemainingLineMm+offer.LineLengthMm)||nextEquipment.LostLineMm!=equipment.LostLineMm)throw new InvalidOperationException("Equipment purchase violated line conservation.");
                    equipment=nextEquipment;wallet=checked(wallet-offer.PriceMinor);outcome=FloatOutcome.EquipmentPurchased;
                }else if(command is UpgradeFloatProfile){
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
                }else if(bankEnd!=null){
                    var rod=state.BankSessionFor(bankEnd.RigId);
                    if(rod==null||rod.Id!=bankEnd.SessionId)return Reject("session-not-current");
                    if(rod.CapacityBlocked&&bankEnd.Ending!=BankRodEnding.Release)return Reject("release-current-required");
                    bankSessions.Remove(rod);
                    switch(bankEnd.Ending){
                    case BankRodEnding.Land:
                        if(bankEnd.FishId==bankEnd.EncounterId || EquipmentPurchaseIdentityUsed(state,command,bankEnd.FishId))return Reject("fish-identity-used");
                        if(EquipmentPurchaseIdentityUsed(state,command,bankEnd.EncounterId))return Reject("encounter-identity-used");
                        var rule=rules.Find(bankEnd.Species);if(rule==null)return Reject("configuration-missing");
                        if(bankEnd.MassMg<rule.MinimumMassMg||bankEnd.MassMg>rule.MaximumMassMg)return Reject("fish-mass-invalid");
                        var caught=new FloatFish(bankEnd.FishId,rod.Id,bankEnd.EncounterId,bankEnd.Species,bankEnd.MassMg,rule.Value(bankEnd.MassMg),rules.Version);
                        affected.Add(caught.Id);encounterId=caught.EncounterId;
                        if(fish.Count>=rules.CreelCapacity){
                            bankSessions.Add(new FloatSession(rod.Id,rod.ReleaseEventId,rod.RigRevision,rod.RulesVersion,caught,
                                rod.EquipmentInventoryRevision,rod.EquipmentRulesVersion,rod.RequiredLineMm,rod.BottomKit,rod.BankRigId));
                            outcome=FloatOutcome.CapacityRejected;
                        }else{fish.Add(caught);outcome=FloatOutcome.Landed;}
                        break;
                    case BankRodEnding.Release:
                        if(!rod.CapacityBlocked)return Reject("pending-fish-not-current");
                        affected.Add(rod.PendingFish.Id);encounterId=rod.PendingFish.EncounterId;outcome=FloatOutcome.Released;break;
                    case BankRodEnding.Recall:outcome=FloatOutcome.Recalled;break;
                    case BankRodEnding.Unhooked:
                        if(EquipmentPurchaseIdentityUsed(state,command,bankEnd.EncounterId))return Reject("encounter-identity-used");
                        encounterId=bankEnd.EncounterId;outcome=FloatOutcome.Unhooked;break;
                    default:outcome=FloatOutcome.Recovered;break;
                    }
                }else if(command is CastLureWithEquipment){
                    var c=(CastLureWithEquipment)command;var k=state.OwnedLureKit;
                    if(!OwnedLureKit.Enabled(state)||k==null)return Reject("lure-kit-not-owned");
                    if(session!=null)return Reject("session-busy");
                    if(c.RequestId==c.EventId||KnownPurchaseIdentity(state,c.RequestId)||KnownPurchaseIdentity(state,c.EventId))return Reject("cast-identity-invalid");
                    if(EquipmentPurchaseIdentityUsed(state,c,c.SessionId))return Reject("session-identity-used");
                    if(c.RigId!=k.RigId||c.LineId!=k.LineId||c.LureId!=k.LureId||c.HookId!=k.HookId||c.KitRigRevision!=k.RigRevision||
                        c.LineRevision!=k.LineRevision||c.LureRevision!=k.LureRevision)return Reject("lure-kit-binding-stale");
                    if(c.RequiredLineMm>k.LineLengthMm)return Reject("line-insufficient");
                    if(stamina<OwnedLureKit.CastStaminaCost)return Reject("stamina-insufficient");
                    stamina-=OwnedLureKit.CastStaminaCost;
                    session=new FloatSession(c.SessionId,c.EventId,k.RigRevision,rules.Version,null,1,1,c.RequiredLineMm,lureKit:k);outcome=FloatOutcome.CastCommitted;
                }else if(command is CastBottomKitWithEquipment){
                    var cast=(CastBottomKitWithEquipment)command;var kit=bankCast==null?state.OwnedBottomKit:state.BottomKitFor(cast.KitRigId);
                    if(!BottomOwnedKit.Enabled(state) || kit==null)return Reject("bottom-kit-not-owned");
                    if(session!=null)return Reject("session-busy");
                    if(bankCast!=null&&state.BankSessionFor(cast.KitRigId)!=null)return Reject("rig-session-busy");
                    if(EquipmentPurchaseIdentityUsed(state,command,cast.SessionId))return Reject("session-identity-used");
                    var parts=kit.Equipment;var line=parts.Spool(parts.Equipped(FloatEquipmentSlot.Reel).Id);
                    if(parts.RigId!=cast.KitRigId || parts.RigRevision!=cast.KitRigRevision || line.Id!=cast.LineId ||
                        line.Revision!=cast.LineRevision || kit.LeaderId!=cast.LeaderId)return Reject("bottom-kit-binding-stale");
                    if(line.RemainingLineMm<cast.RequiredLineMm)return Reject("line-insufficient");
                    if(baits<1)return Reject("bait-empty");if(stamina<rules.CastStaminaCost)return Reject("stamina-insufficient");
                    baits-=1;stamina-=rules.CastStaminaCost;
                    var started=new FloatSession(cast.SessionId,cast.EventId,parts.RigRevision,rules.Version,null,parts.InventoryRevision,parts.RulesVersion,
                        cast.RequiredLineMm,kit,bankCast==null?Guid.Empty:cast.KitRigId);
                    if(bankCast==null)session=started;else bankSessions.Add(started);
                    outcome=FloatOutcome.CastCommitted;
                }else if(command is CastFloatWithEquipment){
                    if(session!=null)return Reject("session-busy");var cast=(CastFloatWithEquipment)command;
                    if(AssetExists(state,cast.SessionId))return Reject("session-identity-used");
                    var readiness=equipment.EvaluateCastReadiness(FloatEquipmentGate.Idle,cast.RequiredLineMm,state.SelectedFloatBaitCount,stamina,rules.CastStaminaCost);
                    if(!readiness.Valid)return Reject(readiness.Issues[0].Code);
                    if(breadSelected)breadBaits--;else baits--;
                    stamina=checked(stamina-rules.CastStaminaCost);
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
                }else if(command is HostFloatTerminalLoss){
                    var loss=(HostFloatTerminalLoss)command;
                    if(session==null||session.Id!=command.SessionId)return Reject("session-not-current");
                    if(session.IsBottom)return Reject("bottom-loss-unsupported");
                    if(session.IsLure)return Reject("lure-loss-unsupported");
                    if(session.CapacityBlocked)return Reject("pending-fish-already-set");
                    if(command.EventId==command.RequestId||command.EventId==state.ProfileId||command.EventId==state.BranchId||command.EventId==loss.EncounterId||RequestWasUsed(state,command.EventId)||UsedSession(state,command.EventId)||EncounterWasUsed(state,command.EventId)||AssetExists(state,command.EventId)||FishWasCreated(state,command.EventId))return Reject("loss-identity-invalid");
                    if(loss.EncounterId==command.RequestId||loss.EncounterId==state.ProfileId||loss.EncounterId==state.BranchId||RequestWasUsed(state,loss.EncounterId)||UsedSession(state,loss.EncounterId)||EventWasUsed(state,loss.EncounterId)||AssetExists(state,loss.EncounterId)||FishWasCreated(state,loss.EncounterId)||EncounterWasUsed(state,loss.EncounterId))return Reject("encounter-identity-used");
                    var reel=equipment.Equipped(FloatEquipmentSlot.Reel);var line=reel==null?null:equipment.Spool(reel.Id);
                    if(line==null||line.Id!=loss.LineId||line.Revision!=loss.ExpectedLineRevision)return Reject("session-line-not-current");
                    var locked=equipment.LockForSession(session.Id);
                    var change=equipment.PlanTerminalLoss(FloatEquipmentGate.ActiveSession,locked,command.EventId,loss.LossRuleKey,loss.OutLineMm,loss.CutDistanceFromReelMm);
                    if(!change.Accepted)return Reject(change.Code);
                    equipmentLoss=new FloatEquipmentLossSummary(line.Id,line.RemainingLineMm,change.Candidate.Find(line.Id).RemainingLineMm,change.LostLineMm,change.LostItemIds);
                    equipment=change.Candidate;encounterId=loss.EncounterId;session=null;outcome=FloatOutcome.TerminalLost;
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
                    if(fish.Count>=rules.CreelCapacity){session=new FloatSession(session.Id,session.ReleaseEventId,session.RigRevision,session.RulesVersion,caught,session.EquipmentInventoryRevision,session.EquipmentRulesVersion,session.RequiredLineMm,session.BottomKit,lureKit:session.LureKit);outcome=FloatOutcome.CapacityRejected;}
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
                var receipt=new FloatReceipt(command,sequence,rules.Version,outcome,encounterId,state.WalletMinor,wallet,state.Stamina,stamina,state.BaitCount,baits,affected,state.FoodCount,foods,state.OwnedNet==null?Guid.Empty:state.OwnedNet.Id,net==null?Guid.Empty:net.Id,state.EquippedNetId,equipped,equipmentLoss,equipmentArchive);
                var history=new List<FloatReceipt>(state.Receipts);history.Add(receipt);
                var candidate=new FloatGameState(state.ProfileId,branch,sequence,wallet,stamina,state.BaitStackId,state.BaitDefinitionKey,baits,schema==3?null:state.LegacyRig,session,fish,history,rules,schema,catalog,foodStack,foods,net,equipped,origin,equipment,equipmentOffers,equipmentOrigin,bankSessions,breadBaits,breadSelected);
                return new FloatReduction(Code(outcome),candidate,receipt);
            }catch(OverflowException){return Reject("arithmetic-overflow");}
        }
    }
}
