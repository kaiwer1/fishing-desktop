using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FishingDesktop.GameCore
{
    // A sale specification for exactly one inventory instance. This is not an
    // owned item, a purchase quote, a grant, or an instruction to equip it.
    internal sealed class FloatEquipmentOffer
    {
        internal readonly string Sku,DefinitionKey;
        internal readonly FloatEquipmentKind Kind;
        internal readonly long PriceMinor,LineLengthMm;
        internal int Units {get{return 1;}}
        internal FloatEquipmentOffer(string sku,string definitionKey,FloatEquipmentKind kind,long priceMinor,long lineLengthMm=0)
        {
            if(kind!=FloatEquipmentKind.Hook&&kind!=FloatEquipmentKind.Bobber&&kind!=FloatEquipmentKind.Sinker&&kind!=FloatEquipmentKind.Line)
                throw new ArgumentOutOfRangeException("kind","Only the four declared spare categories may be offered.");
            if(priceMinor<=0)throw new ArgumentOutOfRangeException("priceMinor");
            if(kind==FloatEquipmentKind.Line){if(lineLengthMm<1||lineLengthMm>FloatEquipmentGuard.MaximumLineMm)throw new ArgumentOutOfRangeException("lineLengthMm");}
            else if(lineLengthMm!=0)throw new ArgumentException("Only an explicit whole line segment has a length.","lineLengthMm");
            Sku=FloatEquipmentGuard.Key(sku);DefinitionKey=FloatEquipmentGuard.Key(definitionKey);Kind=kind;PriceMinor=priceMinor;LineLengthMm=lineLengthMm;
        }
    }

    // Prices are a separate, explicit policy. Binding checks existing
    // definitions without adding any definition or changing a legacy profile.
    // Matching catalogues may be separate objects, but their complete canonical
    // content and version must match; version equality alone is insufficient.
    internal sealed class FloatEquipmentOffers
    {
        internal const int MaximumOffers=16;
        internal readonly long Version,EquipmentCatalogVersion;
        internal readonly string EquipmentCatalogSignature,Signature;
        internal readonly ReadOnlyCollection<FloatEquipmentOffer> Offers;
        internal readonly FloatEquipmentCatalog BoundEquipmentCatalog;
        internal readonly long ValidatedMaximumWalletMinor;
        internal FloatEquipmentOffers(long version,FloatEquipmentCatalog equipmentCatalog,IEnumerable<FloatEquipmentOffer> offers,long maximumWalletMinor)
        {
            FloatEquipmentGuard.Revision(version);
            if(equipmentCatalog==null||offers==null)throw new ArgumentNullException("offers dependency");
            if(maximumWalletMinor<=0)throw new ArgumentOutOfRangeException("maximumWalletMinor");
            var copy=new List<FloatEquipmentOffer>();var skus=new HashSet<string>(StringComparer.Ordinal);
            foreach(var offer in offers){
                if(offer==null||copy.Count>=MaximumOffers||!skus.Add(offer.Sku))throw new ArgumentException("Null, duplicate or excessive spare offers.","offers");
                copy.Add(offer);
            }
            if(copy.Count==0)throw new ArgumentException("An explicit spare catalogue cannot be empty.","offers");
            copy.Sort((a,b)=>StringComparer.Ordinal.Compare(a.Sku,b.Sku));
            Version=version;EquipmentCatalogVersion=equipmentCatalog.Version;
            EquipmentCatalogSignature=FloatEquipmentOffersCodec.EquipmentSignature(equipmentCatalog);
            Offers=copy.AsReadOnly();BoundEquipmentCatalog=equipmentCatalog;ValidatedMaximumWalletMinor=maximumWalletMinor;
            ValidateAgainst(equipmentCatalog,maximumWalletMinor);
            Signature=FloatEquipmentOffersCodec.ContentSignature(this);
        }
        internal FloatEquipmentOffer Find(string sku)
        {foreach(var offer in Offers)if(String.Equals(offer.Sku,sku,StringComparison.Ordinal))return offer;return null;}
        internal void ValidateAgainst(FloatEquipmentCatalog equipmentCatalog,long maximumWalletMinor)
        {
            if(equipmentCatalog==null)throw new ArgumentNullException("equipmentCatalog");
            if(maximumWalletMinor<=0)throw new ArgumentOutOfRangeException("maximumWalletMinor");
            if(equipmentCatalog.Version!=EquipmentCatalogVersion||FloatEquipmentOffersCodec.EquipmentSignature(equipmentCatalog)!=EquipmentCatalogSignature)
                throw new ArgumentException("The complete existing equipment catalogue does not match the offer binding.","equipmentCatalog");
            foreach(var offer in Offers){
                var definition=equipmentCatalog.Find(offer.DefinitionKey);
                if(definition==null||definition.Kind!=offer.Kind)throw new ArgumentException("An offer requires an existing definition of exactly its declared kind.","equipmentCatalog");
                if(offer.PriceMinor>maximumWalletMinor)throw new ArgumentException("An offer price exceeds the caller's declared wallet maximum.","maximumWalletMinor");
            }
            // This is a catalogue binding, not a cast-readiness or owned-reel
            // check. Later assembly still applies the explicit spool rules.
        }
    }
}
