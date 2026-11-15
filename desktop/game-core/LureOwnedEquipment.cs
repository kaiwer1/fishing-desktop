using System;

namespace FishingDesktop.GameCore
{
    internal static class LureOwnedEquipment
    {
        internal static string BindForCast(FloatGameState state,out LureRetrieveRig rig)
        {
            rig=null;
            if(!OwnedLureKit.Enabled(state))return "lure-kit-unavailable";
            var kit=state.OwnedLureKit;if(kit==null)return "lure-kit-not-owned";
            rig=new LureRetrieveRig(kit.RigId,kit.RigRevision,kit.RodId,kit.ReelId,kit.LineId,kit.LureId,kit.LureRevision,kit.LineLengthMm);
            return null;
        }
        // Physics consumes only the kit of the currently paid lure session.
        // The spoon carries its integral hook. Both remain owned on retrieval.
        internal static string Bind(FloatGameState state,Guid sessionId,out LureRetrieveRig rig)
        {
            rig=null;
            var session=state==null?null:state.Session;
            if(sessionId==Guid.Empty||session==null||session.Id!=sessionId)return "session-not-current";
            var kit=state.OwnedLureKit;
            if(kit==null||!session.IsLure||kit!=session.LureKit)return "lure-kit-binding-stale";
            return BindForCast(state,out rig);
        }
    }
}
