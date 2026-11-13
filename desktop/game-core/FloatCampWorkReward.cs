using System;

namespace FishingDesktop.GameCore
{
    // Original recovery rule for this project. These values are not RF4
    // mechanics, and a valid command is not proof of elapsed real-world time.
    internal static class FloatCampWorkRules
    {
        internal const int DurationMilliseconds=60000;
        internal const long PolicyVersion=1,RewardMinor=800,EligibleWalletBelow=3200;
        internal static string Check(FloatGameState state)
        {
            if(state==null)return "camp-state-unavailable";
            if(state.SchemaVersion!=3)return "camp-schema-required";
            if(state.Session!=null)return "session-busy";
            if(state.Creel.Count!=0)return "camp-creel-not-empty";
            if(state.WalletMinor>=EligibleWalletBelow)return "camp-wallet-ineligible";
            // Subtraction avoids overflow and preserves the configured cap.
            if(state.Rules.MaximumWalletMinor<RewardMinor||state.WalletMinor>state.Rules.MaximumWalletMinor-RewardMinor)return "wallet-capacity-exceeded";
            return null;
        }
    }

    // Host-only completion fact. The timer adapter must establish current
    // authority and count eligible active time; pages cannot choose a reward.
    // Exact duration/policy are validated by reduction, including replay.
    internal sealed class HostFloatCampReward : FloatCommand
    {
        internal readonly int ActiveMilliseconds;
        internal readonly long PolicyVersion;
        internal HostFloatCampReward(Guid profile,Guid branch,Guid request,Guid businessEvent,int activeMilliseconds,long policyVersion,
            long expectedSequence,long expectedRigRevision,long expectedRulesVersion)
            :base("host-camp-reward",profile,branch,request,businessEvent,Guid.Empty,expectedSequence,expectedRigRevision,expectedRulesVersion)
        {ActiveMilliseconds=activeMilliseconds;PolicyVersion=policyVersion;}
        internal override string Detail{get{return FloatGuard.Number(ActiveMilliseconds)+"|"+FloatGuard.Number(PolicyVersion);}}
    }
}
