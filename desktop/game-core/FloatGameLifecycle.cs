using System;

namespace FishingDesktop.GameCore
{
    public sealed class FloatLifecycleResult
    {
        public readonly bool Confirmed, NoActiveSession, PendingResultPreserved;
        public readonly string Code;
        public readonly FloatCommitResult Transaction;
        internal FloatLifecycleResult(bool noActiveSession, bool pendingResult, FloatCommitResult transaction)
        {
            NoActiveSession = noActiveSession; PendingResultPreserved = pendingResult; Transaction = transaction;
            Confirmed = noActiveSession || pendingResult || (transaction != null && transaction.Committed);
            Code = noActiveSession ? "no-active-session" : pendingResult ? "pending-result-preserved" : transaction.Code;
        }
    }

    // Explicit host startup/shutdown entry, not a page route or Dispose hook.
    internal static class FloatGameLifecycle
    {
        internal static FloatLifecycleResult Startup(FloatGameStore store)
        { return ResolveCurrent(store, FloatRecoveryReason.StartupInterrupted); }

        internal static FloatLifecycleResult OwnerShutdown(FloatGameStore store)
        { return ResolveCurrent(store, FloatRecoveryReason.OwnerShutdown); }

        internal static FloatLifecycleResult ResolveCurrent(FloatGameStore store, FloatRecoveryReason reason)
        {
            if (store == null) throw new ArgumentNullException("store");
            if (reason != FloatRecoveryReason.StartupInterrupted && reason != FloatRecoveryReason.OwnerShutdown)
                throw new ArgumentOutOfRangeException("reason");
            var state = store.State;
            if (state.Session == null) return new FloatLifecycleResult(true, false, null);
            // A known, saved capacity refusal is a result, not an unfinished
            // simulation. Preserve it until an authorized explicit release.
            if (state.Session.CapacityBlocked) return new FloatLifecycleResult(false, true, null);
            var command = new RecoverFloatSession(state.ProfileId, state.BranchId,
                Guid.NewGuid(), Guid.NewGuid(), state.Session.Id, state.Sequence,
                state.Rig.Revision, state.RulesVersion, reason);
            // One attempt only. An uncertain append escapes to the host and
            // disables this store; no alternate ID or refund is attempted.
            return new FloatLifecycleResult(false, false, store.Execute(command));
        }
    }
}
