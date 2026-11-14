namespace FishingDesktop.GameCore
{
    internal static class BottomOwnedEquipment
    {
        // The receipt and the saved bait stack establish ownership. The main
        // float equipment remains a separate, unchanged rig in this profile.
        internal static string Bind(FloatGameState state,out BottomEquipmentBridge binding)
        {return Bind(state,state==null||state.OwnedBottomKit==null?System.Guid.Empty:state.OwnedBottomKit.Equipment.RigId,out binding);}
        internal static string Bind(FloatGameState state,System.Guid rigId,out BottomEquipmentBridge binding)
        {
            binding=null;
            if(state==null || !BottomOwnedKit.Enabled(state))return "bottom-kit-unavailable";
            var kit=state.BottomKitFor(rigId);if(kit==null)return "bottom-kit-not-owned";
            var equipment=kit.Equipment;
            var selected=new BottomTerminalSelection(equipment.RigId,equipment.RigRevision,equipment.InventoryRevision,equipment.RulesVersion,
                "bottom_simple",new BottomRigPart(kit.LeaderId,BottomPartKind.Leader,"bottom-016-leader"),
                new BottomRigPart(state.BaitStackId,BottomPartKind.HookBait,state.BaitDefinitionKey));
            return BottomEquipmentBridge.Bind(equipment,selected,out binding);
        }
    }
}
