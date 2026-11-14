using System;

namespace FishingDesktop.GameCore
{
    // These are selected, already-owned parts supplied by the equipment owner.
    // The float ledger has no leader or bait-stack slots. This value neither
    // creates those assets nor proves their ownership outside that owner.
    internal sealed class BottomTerminalSelection
    {
        internal readonly Guid RigId;
        internal readonly long RigRevision,InventoryRevision,EquipmentRulesVersion;
        internal readonly string Mode;
        internal readonly BottomRigPart Leader,HookBait;
        internal BottomTerminalSelection(Guid rigId,long rigRevision,long inventoryRevision,long equipmentRulesVersion,
            string mode,BottomRigPart leader,BottomRigPart hookBait)
        {
            RigId=rigId;RigRevision=rigRevision;InventoryRevision=inventoryRevision;EquipmentRulesVersion=equipmentRulesVersion;
            Mode=mode;Leader=leader;HookBait=hookBait;
        }
    }

    internal sealed class BottomEquipmentBridge
    {
        readonly FloatEquipmentLedger source;
        internal readonly BottomSimpleAssembly Assembly;
        internal readonly FloatEquipmentInstance InstalledLine;
        internal long InventoryRevision {get{return source.InventoryRevision;}}
        internal long EquipmentRulesVersion {get{return source.RulesVersion;}}
        internal bool IsCurrent(FloatEquipmentLedger current){return Object.ReferenceEquals(source,current);}

        BottomEquipmentBridge(FloatEquipmentLedger ledger,FloatEquipmentInstance line,BottomSimpleAssembly assembly)
        {source=ledger;InstalledLine=line;Assembly=assembly;}

        static BottomRigPart Part(FloatEquipmentInstance item,BottomPartKind kind)
        {return new BottomRigPart(item.Id,kind,item.DefinitionKey);}

        static bool LedgerIdentity(FloatEquipmentLedger ledger,Guid id)
        {
            if(id==ledger.RigId || ledger.Find(id)!=null)return true;
            foreach(var item in ledger.Instances)
                if(item.Location.Place==FloatEquipmentPlace.Lost && item.Location.OwnerId==id)return true;
            return false;
        }

        // Returns a diagnostic code on refusal. The caller must recheck source
        // and accessory ownership before a future transaction; none is written here.
        internal static string Bind(FloatEquipmentLedger ledger,BottomTerminalSelection selection,out BottomEquipmentBridge binding)
        {
            binding=null;
            if(ledger==null)return "equipment-required";
            if(selection==null || selection.Mode!="bottom_simple")return "bottom-unsupported";
            if(selection.RigId!=ledger.RigId || selection.RigRevision!=ledger.RigRevision ||
                selection.InventoryRevision!=ledger.InventoryRevision || selection.EquipmentRulesVersion!=ledger.RulesVersion)return "version-conflict";
            if(ledger.Equipped(FloatEquipmentSlot.Bobber)!=null)return "rig-incompatible";
            var rod=ledger.Equipped(FloatEquipmentSlot.Rod);var reel=ledger.Equipped(FloatEquipmentSlot.Reel);
            var hook=ledger.Equipped(FloatEquipmentSlot.Hook);var sinker=ledger.Equipped(FloatEquipmentSlot.Sinker);
            var line=reel==null?null:ledger.Spool(reel.Id);
            if(rod==null || reel==null || line==null || hook==null || sinker==null || selection.Leader==null || selection.HookBait==null)return "rig-incomplete";
            if(LedgerIdentity(ledger,selection.Leader.Id) || LedgerIdentity(ledger,selection.HookBait.Id))return "part-identity-conflict";
            if(line.RemainingLineMm>BottomSimpleAssembly.MaximumLineMm)return "line-length-unsupported";
            var assembly=new BottomSimpleAssembly(ledger.RigId,ledger.RigRevision,selection.Mode,(int)line.RemainingLineMm,
                Part(rod,BottomPartKind.Rod),Part(reel,BottomPartKind.Reel),Part(line,BottomPartKind.SpoolLine),selection.Leader,
                Part(hook,BottomPartKind.Hook),Part(sinker,BottomPartKind.Sinker),selection.HookBait);
            string invalid=assembly.ValidateComplete();if(invalid!=null)return invalid;
            var check=ledger.ValidateAssembly();if(!check.Valid)return check.Issues[0].Code;
            var catalog=ledger.Catalog;
            if(catalog.BaitDefinitionKey!=selection.HookBait.DefinitionId)return "incompatible-bait";
            var rodDef=catalog.Find(rod.DefinitionKey);var lineDef=catalog.Find(line.DefinitionKey);
            if(lineDef.LineDiameterUm!=BottomSimpleAssembly.LineDiameterUm ||
                catalog.Find(sinker.DefinitionKey).MassMg!=BottomSimpleAssembly.SinkerMassMg ||
                catalog.Find(hook.DefinitionKey).MassMg!=BottomSimpleAssembly.HookMassMg)return "rig-incompatible";
            if(BottomSimpleAssembly.CastMassMg<rodDef.MinimumLoadMg || BottomSimpleAssembly.CastMassMg>rodDef.MaximumLoadMg)return "cast-mass-out-of-range";
            if(rodDef.MinimumLoadMg!=BottomSimpleAssembly.RodMinimumCastMg || rodDef.MaximumLoadMg!=BottomSimpleAssembly.RodMaximumCastMg)return "rig-incompatible";
            binding=new BottomEquipmentBridge(ledger,line,assembly);return null;
        }
    }
}
