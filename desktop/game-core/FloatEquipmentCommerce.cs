using System;
using System.Security.Cryptography;

namespace FishingDesktop.GameCore
{
    // Context-free journal decoding freezes only these bytes. The reducer must
    // decode FEOS against the actual current equipment catalogue and wallet cap.
    internal sealed class ConfigureFloatEquipmentOffers : FloatEquipmentCommand
    {
        // 132 bytes are reserved for the existing journal command envelope.
        internal const int MaximumPayloadBytes=65536-132;
        readonly byte[] payload;
        internal readonly string PayloadSha256;
        internal ConfigureFloatEquipmentOffers(Guid profile,Guid branch,Guid request,Guid businessEvent,byte[] offerPayload,long expectedSequence,long expectedRigRevision,long expectedRulesVersion,long expectedInventoryRevision,long expectedEquipmentVersion)
            :base("configure-equipment-offers",profile,branch,request,businessEvent,Guid.Empty,expectedSequence,expectedRigRevision,expectedRulesVersion,expectedInventoryRevision,expectedEquipmentVersion)
        {
            if(offerPayload==null||offerPayload.Length<6||offerPayload.Length>MaximumPayloadBytes)throw new ArgumentException("Invalid bounded offer payload.","offerPayload");
            payload=(byte[])offerPayload.Clone();using(var sha=SHA256.Create())PayloadSha256=BitConverter.ToString(sha.ComputeHash(payload)).Replace("-","").ToLowerInvariant();
        }
        internal byte[] CopyPayload(){return (byte[])payload.Clone();}
        internal override string Detail{get{return EquipmentDetail+"|"+PayloadSha256;}}
    }

    internal sealed class FloatEquipmentPurchaseQuote
    {
        internal readonly Guid ProfileId,BranchId;
        internal readonly long StateSequence,RigRevision,RulesVersion,InventoryRevision,EquipmentVersion,CatalogVersion,PriceMinor,LineLengthMm;
        internal readonly string CatalogSignature,Sku,DefinitionKey;
        internal readonly FloatEquipmentKind Kind;
        internal int Units{get{return 1;}}
        internal FloatEquipmentPurchaseQuote(Guid profile,Guid branch,long stateSequence,long rigRevision,long rulesVersion,long inventoryRevision,long equipmentVersion,long catalogVersion,string catalogSignature,string sku,string definitionKey,FloatEquipmentKind kind,long priceMinor,long lineLengthMm)
        {
            FloatGuard.Id(profile,"profile");FloatGuard.Id(branch,"branch");
            if(stateSequence<0||rigRevision<1||rulesVersion<1||inventoryRevision<1||equipmentVersion<1||catalogVersion<1)throw new ArgumentOutOfRangeException("quote versions");
            // Share the offer's kind/price/length validation, but do not create an
            // offer policy: only the reducer can compare this claim to policy.
            var item=new FloatEquipmentOffer(sku,definitionKey,kind,priceMinor,lineLengthMm);
            ProfileId=profile;BranchId=branch;StateSequence=stateSequence;RigRevision=rigRevision;RulesVersion=rulesVersion;InventoryRevision=inventoryRevision;EquipmentVersion=equipmentVersion;CatalogVersion=catalogVersion;CatalogSignature=FloatGuard.Sha256(catalogSignature);
            Sku=item.Sku;DefinitionKey=item.DefinitionKey;Kind=item.Kind;PriceMinor=item.PriceMinor;LineLengthMm=item.LineLengthMm;
        }
        internal static FloatEquipmentPurchaseQuote Create(FloatGameState state,string sku)
        {
            if(state==null||state.SchemaVersion!=3||state.EquipmentOffers==null)throw new ArgumentException("An explicitly configured equipment offer catalogue is required.");
            var catalog=state.EquipmentOffers;var offer=catalog.Find(FloatEquipmentGuard.Key(sku));if(offer==null)throw new ArgumentException("Unknown equipment SKU.","sku");
            return new FloatEquipmentPurchaseQuote(state.ProfileId,state.BranchId,state.Sequence,state.Rig.Revision,state.RulesVersion,state.Equipment.InventoryRevision,state.Equipment.RulesVersion,catalog.Version,catalog.Signature,offer.Sku,offer.DefinitionKey,offer.Kind,offer.PriceMinor,offer.LineLengthMm);
        }
        internal string Fingerprint{get{return ProfileId.ToString("N")+"|"+BranchId.ToString("N")+"|"+FloatGuard.Number(StateSequence)+"|"+FloatGuard.Number(RigRevision)+"|"+FloatGuard.Number(RulesVersion)+"|"+FloatGuard.Number(InventoryRevision)+"|"+FloatGuard.Number(EquipmentVersion)+"|"+FloatGuard.Number(CatalogVersion)+"|"+CatalogSignature+"|"+Sku+"|"+DefinitionKey+"|"+FloatGuard.Number((int)Kind)+"|"+FloatGuard.Number(PriceMinor)+"|"+FloatGuard.Number(LineLengthMm);}}
    }

    internal sealed class BuyFloatEquipment : FloatEquipmentCommand
    {
        internal readonly FloatEquipmentPurchaseQuote Quote;
        internal readonly Guid NewItemId;
        static FloatEquipmentPurchaseQuote Required(FloatEquipmentPurchaseQuote quote){if(quote==null)throw new ArgumentNullException("quote");return quote;}
        internal BuyFloatEquipment(Guid profile,Guid branch,Guid request,Guid businessEvent,FloatEquipmentPurchaseQuote quote,Guid newItemId)
            :base("buy-equipment",profile,branch,request,businessEvent,Guid.Empty,Required(quote).StateSequence,quote.RigRevision,quote.RulesVersion,quote.InventoryRevision,quote.EquipmentVersion)
        {FloatGuard.Id(newItemId,"newItem");Quote=quote;NewItemId=newItemId;}
        internal override string Detail{get{return EquipmentDetail+"|"+Quote.Fingerprint+"|"+NewItemId.ToString("N");}}
    }
}
