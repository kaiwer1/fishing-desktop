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
        {foreach(var row in state.Receipts)if((row.Outcome==FloatOutcome.Landed||row.Outcome==FloatOutcome.CapacityRejected)&&row.EncounterId==id)return true;return false;}
        static bool AssetExists(FloatGameState state,Guid id)
        {var ids=new HashSet<Guid>();state.Rig.AddAssets(ids);ids.Add(state.BaitStackId);foreach(var fish in state.Creel)ids.Add(fish.Id);return ids.Contains(id);}
        static string Code(FloatOutcome outcome)
        {switch(outcome){case FloatOutcome.CastCommitted:return "cast-candidate";case FloatOutcome.Recalled:return "recall-candidate";case FloatOutcome.Landed:return "landed-candidate";case FloatOutcome.CapacityRejected:return "creel-capacity-exceeded";case FloatOutcome.Released:return "released-candidate";case FloatOutcome.Recovered:return "recovered-candidate";default:return "sale-candidate";}}
        public static FloatReduction Reduce(FloatGameState state,FloatCommand command,FloatGameRules rules)
        {
            if(state==null)throw new ArgumentNullException("state");if(command==null)throw new ArgumentNullException("command");
            if(command.ProfileId!=state.ProfileId||command.BranchId!=state.BranchId)return Reject("branch-mismatch");
            string fingerprint=command.Fingerprint;
            foreach(var row in state.Receipts)if(row.RequestId==command.RequestId)
                return row.Fingerprint==fingerprint?new FloatReduction(Code(row.Outcome),receipt:row,replay:true):Reject("request-conflict");
            foreach(var row in state.Receipts)if(row.EventId==command.EventId)return Reject("event-already-used");
            if(command is CastFloat&&UsedSession(state,command.SessionId))return Reject("session-already-used");
            if(command.ExpectedSequence!=state.Sequence||command.ExpectedRigRevision!=state.Rig.Revision||command.ExpectedRulesVersion!=state.RulesVersion)return Reject("version-conflict");
            if(rules==null)return Reject("configuration-missing");
            if(rules.Signature!=state.Rules.Signature)return Reject("configuration-mismatch");
            long wallet=state.WalletMinor;int stamina=state.Stamina,baits=state.BaitCount;var fish=new List<FloatFish>(state.Creel);var session=state.Session;var affected=new List<Guid>();Guid encounterId=Guid.Empty;FloatOutcome outcome;
            try{
                if(command is CastFloat){
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
                }else if(command is HostFloatFish){
                    var host=(HostFloatFish)command;
                    if(session==null||session.Id!=command.SessionId)return Reject("session-not-current");if(session.CapacityBlocked)return Reject("pending-fish-already-set");
                    var rule=rules.Find(host.SpeciesKey);if(rule==null)return Reject("configuration-missing");
                    if(host.MassMg<rule.MinimumMassMg||host.MassMg>rule.MaximumMassMg)return Reject("fish-mass-invalid");
                    if(AssetExists(state,host.FishId)||FishWasCreated(state,host.FishId))return Reject("fish-identity-used");
                    if(EncounterWasUsed(state,host.EncounterId))return Reject("encounter-identity-used");
                    var caught=new FloatFish(host.FishId,session.Id,host.EncounterId,host.SpeciesKey,host.MassMg,rule.Value(host.MassMg),rules.Version);affected.Add(caught.Id);encounterId=host.EncounterId;
                    if(fish.Count>=rules.CreelCapacity){session=new FloatSession(session.Id,session.ReleaseEventId,session.RigRevision,session.RulesVersion,caught);outcome=FloatOutcome.CapacityRejected;}
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
                var receipt=new FloatReceipt(command,sequence,rules.Version,outcome,encounterId,state.WalletMinor,wallet,state.Stamina,stamina,state.BaitCount,baits,affected);
                var history=new List<FloatReceipt>(state.Receipts);history.Add(receipt);
                var candidate=new FloatGameState(state.ProfileId,state.BranchId,sequence,wallet,stamina,state.BaitStackId,state.BaitDefinitionKey,baits,state.Rig,session,fish,history,rules);
                return new FloatReduction(Code(outcome),candidate,receipt);
            }catch(OverflowException){return Reject("arithmetic-overflow");}
        }
    }
}
