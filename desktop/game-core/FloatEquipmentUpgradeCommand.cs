using System;
using System.IO;
using System.Security.Cryptography;

namespace FishingDesktop.GameCore
{
    internal sealed class UpgradeFloatEquipment : FloatCommand
    {
        internal const int MaximumCombinedPayloadBytes=65536-512;
        internal readonly Guid NewBranchId;
        internal readonly FloatUpgradeOrigin Origin;
        internal readonly long PolicyVersion;
        internal readonly string SourceRigDefinitionKey,EquipmentSha256,OffersSha256;
        readonly byte[] equipmentPayload,offersPayload;
        internal UpgradeFloatEquipment(Guid profile,Guid oldBranch,Guid request,Guid businessEvent,Guid newBranch,
            string sourceSha256,string sourceTipSha256,long sourceLength,long expectedSequence,long expectedRigRevision,long expectedRulesVersion,
            long policyVersion,string sourceRigKey,byte[] equipment,byte[] offers)
            :base("upgrade-equipment",profile,oldBranch,request,businessEvent,Guid.Empty,expectedSequence,expectedRigRevision,expectedRulesVersion)
        {
            FloatGuard.Id(newBranch,"newBranch");FloatEquipmentGuard.Revision(policyVersion);
            if(newBranch==oldBranch||newBranch==profile||newBranch==request||newBranch==businessEvent)throw new ArgumentException("A distinct new branch is required.");
            if(equipment==null||offers==null||equipment.Length<6||offers.Length<6||
                (long)equipment.Length+offers.Length>MaximumCombinedPayloadBytes)throw new ArgumentException("Invalid bounded upgrade payloads.");
            NewBranchId=newBranch;PolicyVersion=policyVersion;SourceRigDefinitionKey=FloatEquipmentGuard.Key(sourceRigKey);
            Origin=new FloatUpgradeOrigin(oldBranch,sourceSha256,sourceTipSha256,sourceLength,expectedSequence);
            equipmentPayload=(byte[])equipment.Clone();offersPayload=(byte[])offers.Clone();EquipmentSha256=Digest(equipmentPayload);OffersSha256=Digest(offersPayload);
        }
        static string Digest(byte[] bytes){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        internal byte[] CopyEquipmentPayload(){return (byte[])equipmentPayload.Clone();}
        internal byte[] CopyOffersPayload(){return (byte[])offersPayload.Clone();}
        internal override string Detail{get{return NewBranchId.ToString("N")+"|"+Origin.SourceSha256+"|"+Origin.SourceTipSha256+"|"+
            FloatGuard.Number(Origin.SourceLength)+"|"+FloatGuard.Number(PolicyVersion)+"|"+SourceRigDefinitionKey+"|"+EquipmentSha256+"|"+OffersSha256;}}
        static string Key(FloatEquipmentInstance item)
        {if(item==null)throw new InvalidDataException("Mapped equipment is incomplete.");return item.DefinitionKey;}
        internal FloatEquipmentLedger ValidateMapping(FloatGameState source)
        {
            var claimed=FloatEquipmentCodec.Decode(equipmentPayload);var reel=claimed.Equipped(FloatEquipmentSlot.Reel);
            if(reel==null)throw new InvalidDataException("Mapped reel is missing.");
            var policy=new FloatEquipmentMigrationPolicy(PolicyVersion,SourceRigDefinitionKey,claimed.Catalog,
                Key(claimed.Equipped(FloatEquipmentSlot.Rod)),Key(reel),Key(claimed.Spool(reel.Id)),Key(claimed.Equipped(FloatEquipmentSlot.Hook)),
                Key(claimed.Equipped(FloatEquipmentSlot.Bobber)),Key(claimed.Equipped(FloatEquipmentSlot.Sinker)));
            var expected=policy.Map(source);byte[] canonical=FloatEquipmentCodec.Encode(expected);
            if(canonical.Length!=equipmentPayload.Length)throw new InvalidDataException("Equipment upgrade changes source ownership.");
            for(int i=0;i<canonical.Length;i++)if(canonical[i]!=equipmentPayload[i])throw new InvalidDataException("Equipment upgrade changes source ownership or line length.");
            return expected;
        }
    }
}
