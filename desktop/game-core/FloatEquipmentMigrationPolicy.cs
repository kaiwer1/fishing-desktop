using System;
using System.Security.Cryptography;
using System.Text;

namespace FishingDesktop.GameCore
{
    // Pure, explicit interpretation of the old complete six-item rig. This
    // creates no new identities, branch, journal, money, supplies or receipts.
    // A file upgrade must separately prove its source prefix and confirmation.
    internal sealed class FloatEquipmentMigrationPolicy
    {
        internal readonly long Version;
        internal readonly string SourceRigDefinitionKey, RodKey, ReelKey, LineKey, HookKey, BobberKey, SinkerKey;
        internal readonly string EquipmentCatalogSignature, Signature;
        internal readonly FloatEquipmentCatalog Catalog;

        internal FloatEquipmentMigrationPolicy(long version,string sourceRigDefinitionKey,FloatEquipmentCatalog catalog,
            string rodKey,string reelKey,string lineKey,string hookKey,string bobberKey,string sinkerKey)
        {
            FloatEquipmentGuard.Revision(version);
            if(catalog==null)throw new ArgumentNullException("catalog");
            Version=version;SourceRigDefinitionKey=FloatEquipmentGuard.Key(sourceRigDefinitionKey);Catalog=catalog;
            RodKey=Definition(rodKey,FloatEquipmentKind.Rod);ReelKey=Definition(reelKey,FloatEquipmentKind.Reel);
            LineKey=Definition(lineKey,FloatEquipmentKind.Line);HookKey=Definition(hookKey,FloatEquipmentKind.Hook);
            BobberKey=Definition(bobberKey,FloatEquipmentKind.Bobber);SinkerKey=Definition(sinkerKey,FloatEquipmentKind.Sinker);
            EquipmentCatalogSignature=FloatEquipmentOffersCodec.EquipmentSignature(catalog);
            // Key alphabet excludes '|'. Domain/version and all explicit
            // mapping fields bind the policy, including unused catalogue rows.
            string material="fishing-equipment-migration-policy-v1|"+FloatGuard.Number(version)+"|"+SourceRigDefinitionKey+"|"+
                EquipmentCatalogSignature+"|"+RodKey+"|"+ReelKey+"|"+LineKey+"|"+HookKey+"|"+BobberKey+"|"+SinkerKey;
            using(var hash=SHA256.Create())Signature=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(material))).Replace("-","").ToLowerInvariant();
        }

        string Definition(string key,FloatEquipmentKind kind)
        {
            key=FloatEquipmentGuard.Key(key);var item=Catalog.Find(key);
            if(item==null||item.Kind!=kind)throw new ArgumentException("An explicit existing definition of the required kind is needed.");
            return key;
        }

        internal FloatEquipmentLedger Map(FloatGameState source)
        {
            if(source==null)throw new ArgumentNullException("source");
            if(source.SchemaVersion!=2||source.LegacyRig==null||source.Equipment!=null)
                throw new InvalidOperationException("Equipment migration accepts only a schema2 legacy rig.");
            var rig=source.LegacyRig;
            if(rig.DefinitionKey!=SourceRigDefinitionKey||source.Rules.RigDefinitionKey!=SourceRigDefinitionKey||source.BaitDefinitionKey!=Catalog.BaitDefinitionKey)
                throw new InvalidOperationException("The source rig or bait has no mapping in this policy.");
            // Legacy format records one positive available length and has no
            // terminal equipment-loss operation. Preserve that exact length;
            // never use spool capacity or a starter length as replacement.
            var equipment=new FloatEquipmentLedger(rig.Id,rig.Revision,1,Catalog,new[]{
                Slot(rig.RodId,RodKey,FloatEquipmentKind.Rod,rig.Id,FloatEquipmentSlot.Rod),
                Slot(rig.ReelId,ReelKey,FloatEquipmentKind.Reel,rig.Id,FloatEquipmentSlot.Reel),
                new FloatEquipmentInstance(rig.LineId,LineKey,FloatEquipmentKind.Line,1,FloatEquipmentLocation.ReelSpool(rig.ReelId),rig.LineLengthMm,rig.LineLengthMm),
                Slot(rig.HookId,HookKey,FloatEquipmentKind.Hook,rig.Id,FloatEquipmentSlot.Hook),
                Slot(rig.BobberId,BobberKey,FloatEquipmentKind.Bobber,rig.Id,FloatEquipmentSlot.Bobber),
                Slot(rig.SinkerId,SinkerKey,FloatEquipmentKind.Sinker,rig.Id,FloatEquipmentSlot.Sinker)});
            var check=equipment.ValidateAssembly();
            if(!check.Valid)throw new InvalidOperationException("The mapped equipment is incompatible: "+check.Issues[0].Code);
            return equipment;
        }

        static FloatEquipmentInstance Slot(Guid id,string key,FloatEquipmentKind kind,Guid rig,FloatEquipmentSlot slot)
        {return new FloatEquipmentInstance(id,key,kind,1,FloatEquipmentLocation.RigSlot(rig,slot));}
    }
}
