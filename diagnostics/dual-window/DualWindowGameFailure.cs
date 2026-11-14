using System;
using System.IO;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    FloatCommand gameFailureCommand;
    FloatFailureTicket gameFailureTicket;
    bool gameFailureRetry;
    int gameUnhookedConfirmations;
    int gameLineBreakConfirmations;
    bool gameFailureFixture,gameFailurePrewriteArmed,gameFailureCommitInProgress;
    bool GameFailureUnconfirmed {get{return gameFight!=null&&gameFight.Snapshot.Phase==FloatFightPhase.FailurePending;}}

    // Initial emission is a host physical fact. A known failed write can only
    // be retried by an authorized explicit retry-escape for this same ticket.
    // No page chooses species, mass, failure reason, cost or durable identities.
    void CommitGameFailure(FloatFightResult pending,ProbeWindow pane=null,string transport=null)
    {
        if(!pending.ShouldCommitFailure||pending.FailureTicket==null||gameFight==null||
            !gameFight.IsCurrentFailureSubmission(pending.FailureTicket,pending.FailureAttempt))
            throw new InvalidOperationException("A current physical failure ticket is required.");
        if(gameFailureCommand==null){
            var state=game.Snapshot.State;
            gameFailureTicket=pending.FailureTicket;
            var loss=pending.FailureTicket.LineBreakEvidence;
            if(loss!=null){
                ValidateGameLineBreakEvidence(state,pending);
                gameFailureCommand=new HostFloatTerminalLoss(state.ProfileId,state.BranchId,Guid.NewGuid(),pending.FailureTicket.FailureId,
                    pending.Snapshot.SessionId,pending.Snapshot.EncounterId,loss.LineId,loss.LineRevision,loss.LossRuleKey,loss.OutLineMm,loss.CutDistanceFromReelMm,
                    state.Sequence,state.Rig.Revision,state.RulesVersion,state.Equipment.InventoryRevision,state.Equipment.RulesVersion,1);
            }else{
                if(pending.FailureTicket.Reason!="slack-unhooked")throw new InvalidOperationException("Unknown physical failure reason.");
                gameFailureCommand=new HostFloatUnhooked(state.ProfileId,state.BranchId,Guid.NewGuid(),pending.FailureTicket.FailureId,
                    pending.Snapshot.SessionId,pending.Snapshot.EncounterId,state.Sequence,state.Rig.Revision,state.RulesVersion,FloatFightSimulation.ModelVersion);
            }
        }
        if(!Object.ReferenceEquals(gameFailureTicket,pending.FailureTicket))throw new InvalidOperationException("A failure cannot replace an unresolved physical result.");
        ClearGameFightInput();gameFailureRetry=false;
        gameFailureCommitInProgress=true;
        try{
            var result=game.ExecuteHost(gameFailureCommand);
            var expected=gameFailureCommand is HostFloatTerminalLoss?FloatOutcome.TerminalLost:FloatOutcome.Unhooked;
            if(result.Committed&&result.Receipt!=null&&result.Receipt.Outcome==expected){
                gameFight.ConfirmFailure(pending.FailureTicket,pending.FailureAttempt,FloatFailureOutcome.Confirmed);
                if(result.Written&&!result.IsReplay){
                    if(expected==FloatOutcome.TerminalLost)gameLineBreakConfirmations=checked(gameLineBreakConfirmations+1);
                    else gameUnhookedConfirmations=checked(gameUnhookedConfirmations+1);
                }
            }else{
                gameFight.ConfirmFailure(pending.FailureTicket,pending.FailureAttempt,FloatFailureOutcome.KnownNotWritten);gameFailureRetry=true;
            }
            if(pane!=null)GameReply(pane,transport,result.Code,result);
        }catch(FloatSaveUncertainException){
            gameFight.ConfirmFailure(pending.FailureTicket,pending.FailureAttempt,FloatFailureOutcome.Unknown);ExitCode=4;
            if(pane!=null)GameReply(pane,transport,"save-outcome-unknown");
        }catch(IOException){
            gameFight.ConfirmFailure(pending.FailureTicket,pending.FailureAttempt,FloatFailureOutcome.KnownNotWritten);gameFailureRetry=true;
            if(pane!=null)GameReply(pane,transport,"failure-not-written");
        }finally{gameFailureCommitInProgress=false;}
        Broadcast();
    }
}
