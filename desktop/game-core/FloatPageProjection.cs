using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace FishingDesktop.GameCore
{
    // Only presentation/selection data. No encounter, request, receipt, asset
    // ownership or authority objects cross this boundary.
    public sealed class FloatPageFish
    {
        public readonly string Id, SpeciesKey, MassMg, SaleValueMinor;
        internal FloatPageFish(FloatFish fish)
        {
            Id = fish.Id.ToString("D"); SpeciesKey = fish.SpeciesKey;
            MassMg = fish.MassMg.ToString(CultureInfo.InvariantCulture);
            SaleValueMinor = fish.SaleValueMinor.ToString(CultureInfo.InvariantCulture);
        }
    }

    public sealed class FloatPageProjection
    {
        public readonly bool HasConfirmedState, Faulted, HostPaused, ShutdownConfirmed, MenuOpen, PendingResult, CanSimulate;
        // Decimal strings retain exact Int64 values across JavaScript JSON.
        public readonly string Sequence, RulesVersion, RigRevision, WalletMinor;
        public readonly string RigDefinitionKey, BaitDefinitionKey, SessionId, SessionStatus;
        public readonly int Stamina, BaitCount, CreelCount, CreelCapacity, CreelOffset;
        public readonly ReadOnlyCollection<FloatPageFish> Creel;
        public readonly FloatPageFish PendingFish;

        private FloatPageProjection(FloatOwnerSnapshot snapshot, int offset, int pageSize)
        {
            HasConfirmedState = !snapshot.Faulted && snapshot.State != null;
            Faulted = snapshot.Faulted; HostPaused = snapshot.HostPaused || !HasConfirmedState;
            ShutdownConfirmed = snapshot.ShutdownConfirmed; MenuOpen = snapshot.MenuOpen;
            CanSimulate = HasConfirmedState && snapshot.CanSimulate;
            SessionStatus = "unknown";
            var rows = new List<FloatPageFish>();
            if (HasConfirmedState)
            {
                var state = snapshot.State;
                Sequence = state.Sequence.ToString(CultureInfo.InvariantCulture);
                RulesVersion = state.RulesVersion.ToString(CultureInfo.InvariantCulture);
                RigRevision = state.Rig.Revision.ToString(CultureInfo.InvariantCulture);
                WalletMinor = state.WalletMinor.ToString(CultureInfo.InvariantCulture);
                RigDefinitionKey = state.Rig.DefinitionKey; BaitDefinitionKey = state.BaitDefinitionKey;
                Stamina = state.Stamina; BaitCount = state.BaitCount;
                CreelCount = state.Creel.Count; CreelCapacity = state.Rules.CreelCapacity; CreelOffset = offset;
                SessionId = state.Session == null ? null : state.Session.Id.ToString("D");
                PendingResult = state.Session != null && state.Session.CapacityBlocked;
                SessionStatus = state.Session == null ? "idle" : PendingResult ? "pending" : "active";
                if (PendingResult) PendingFish = new FloatPageFish(state.Session.PendingFish);
                int count = Math.Min(pageSize, CreelCount - offset);
                for (int index = 0; index < count; index++) rows.Add(new FloatPageFish(state.Creel[offset + index]));
            }
            Creel = rows.AsReadOnly();
        }

        // The host supplies a current snapshot and an explicit bounded page.
        // Clamp a remembered UI offset after mutations before calling this;
        // accepting an invalid offset silently would conceal adapter mistakes.
        public static FloatPageProjection Create(FloatOwnerSnapshot snapshot, int offset, int pageSize)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            if (pageSize < 1 || pageSize > 50) throw new ArgumentOutOfRangeException("pageSize");
            int count = !snapshot.Faulted && snapshot.State != null ? snapshot.State.Creel.Count : 0;
            if (offset < 0 || offset > count) throw new ArgumentOutOfRangeException("offset");
            return new FloatPageProjection(snapshot, offset, pageSize);
        }
    }
}
