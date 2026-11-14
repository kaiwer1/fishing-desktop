using System;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    // Explicit original development tuning, not a material-strength claim.
    // Opening with the ordinary mode never silently turns on this policy.
    const long GameLineBreakMilliN=5000;
    const int GameLineBreakSteps=5;
    static readonly string GameLineBreakCatalogSignature=FloatEquipmentOffersCodec.EquipmentSignature(CreateGameFixture().Equipment.Catalog);
    static void ValidateGameLineBreakCatalog(FloatGameState state)
    {
        if(state==null||state.SchemaVersion!=3||state.Equipment==null||state.EquipmentOffers==null||
            FloatEquipmentOffersCodec.EquipmentSignature(state.Equipment.Catalog)!=GameLineBreakCatalogSignature)
            throw new InvalidOperationException("Explicit line-break mode requires the complete starter equipment catalogue and spare-purchase recovery route.");
        state.EquipmentOffers.ValidateAgainst(state.Equipment.Catalog,state.Rules.MaximumWalletMinor);
        if(state.EquipmentOffers.Signature!=CreateGameSparesCatalog(state).Signature)
            throw new InvalidOperationException("Explicit line-break mode requires the complete starter replacement catalogue and prices.");
    }
    FloatLineBreakRules CreateGameLineBreakRules(FloatGameState state)
    {
        if(!GameLineBreakMode)return null;
        ValidateGameLineBreakCatalog(state);
        var line=state.Rig.LineId.HasValue?state.Equipment.Find(state.Rig.LineId.Value):null;
        if(line==null||line.Kind!=FloatEquipmentKind.Line||line.DefinitionKey!="nylon-018"||line.RemainingLineMm!=GameInstalledLineMm(state))
            throw new InvalidOperationException("The physical policy requires the actual installed starter line.");
        return new FloatLineBreakRules(line.Id,line.Revision,1,"mainline-terminal",GameLineBreakMilliN,GameLineBreakSteps);
    }
    void ValidateGameLineBreakEvidence(FloatGameState state,FloatFightResult pending)
    {
        var evidence=pending.FailureTicket.LineBreakEvidence;var rules=gameLineBreakRules;
        ValidateGameLineBreakCatalog(state);
        var line=state.Equipment.Find(evidence.LineId);
        var current=gameFight.Snapshot;
        long expectedOut=current.OutLineM==evidence.TotalLineMm/1000.0?evidence.TotalLineMm:checked((long)Math.Floor(current.OutLineM*1000.0));
        if(!GameLineBreakMode||rules==null||pending.FailureTicket.Reason!="line-overloaded"||pending.Snapshot.Phase!=FloatFightPhase.FailurePending||
            evidence.PolicyVersion!=rules.PolicyVersion||evidence.LineId!=rules.LineId||evidence.LineRevision!=rules.LineRevision||
            evidence.LossRuleKey!=rules.LossRuleKey||evidence.BreakMilliN!=rules.BreakMilliN||evidence.ExposureSteps!=rules.ExposureSteps||
            pending.Snapshot.LineBreakExposureSteps!=rules.ExposureSteps||evidence.StepIndex!=pending.Snapshot.StepIndex||
            pending.FailureTicket.SessionId!=current.SessionId||pending.FailureTicket.EncounterId!=current.EncounterId||
            pending.Snapshot.SessionId!=current.SessionId||pending.Snapshot.EncounterId!=current.EncounterId||
            pending.Snapshot.StepIndex!=current.StepIndex||pending.Snapshot.TensionN!=current.TensionN||evidence.OutLineMm!=expectedOut||
            Double.IsNaN(evidence.TensionN)||Double.IsInfinity(evidence.TensionN)||evidence.TensionN<=rules.BreakMilliN/1000.0||
            evidence.TensionN!=pending.Snapshot.TensionN||line==null||line.Revision!=rules.LineRevision||state.Rig.LineId!=line.Id||
            line.RemainingLineMm!=evidence.TotalLineMm||evidence.OutLineMm<1||evidence.OutLineMm>line.RemainingLineMm||evidence.CutDistanceFromReelMm!=0||
            state.Session==null||state.Session.Id!=pending.Snapshot.SessionId||state.Rig.Revision!=pending.Snapshot.RigRevision)
            throw new InvalidOperationException("Line-break evidence no longer matches the current physical session and owned equipment.");
    }
}
