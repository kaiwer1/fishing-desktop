using System;
using System.IO;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    HostFloatUnhooked gameFailureCommand;
    FloatFailureTicket gameFailureTicket;
    bool gameFailureRetry;
    int gameUnhookedConfirmations;
    bool gameFailureFixture,gameFailurePrewriteArmed,gameFailureCommitInProgress;
    bool GameFailureUnconfirmed {get{return gameFight!=null&&gameFight.Snapshot.Phase==FloatFightPhase.FailurePending;}}

    // Initial emission is a host physical fact. A known failed write can only
    // be retried by an authorized explicit retry-escape for this same ticket.
    // No page chooses species, mass, failure reason, cost or durable identities.
    void CommitGameFailure(FloatFightResult pending,ProbeWindow pane=null,string transport=null)
    {
        if(!pending.ShouldCommitFailure||pending.FailureTicket==null)throw new InvalidOperationException("A current physical failure ticket is required.");
        if(gameFailureCommand==null){
            var state=game.Snapshot.State;
            gameFailureTicket=pending.FailureTicket;
            gameFailureCommand=new HostFloatUnhooked(state.ProfileId,state.BranchId,Guid.NewGuid(),pending.FailureTicket.FailureId,
                pending.Snapshot.SessionId,pending.Snapshot.EncounterId,state.Sequence,state.Rig.Revision,state.RulesVersion,FloatFightSimulation.ModelVersion);
        }
        if(!Object.ReferenceEquals(gameFailureTicket,pending.FailureTicket))throw new InvalidOperationException("A failure cannot replace an unresolved physical result.");
        ClearGameFightInput();gameFailureRetry=false;
        gameFailureCommitInProgress=true;
        try{
            var result=game.ExecuteHost(gameFailureCommand);
            if(result.Committed&&result.Receipt!=null&&result.Receipt.Outcome==FloatOutcome.Unhooked){
                gameFight.ConfirmFailure(pending.FailureTicket,pending.FailureAttempt,FloatFailureOutcome.Confirmed);
                if(result.Written&&!result.IsReplay)gameUnhookedConfirmations=checked(gameUnhookedConfirmations+1);
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
