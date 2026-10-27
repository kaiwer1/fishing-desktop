using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Web.Script.Serialization;
using FishingDesktop.GameCore;

static class FloatPageProjectionTests
{
    static int passed, failed, nextId;
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    static Guid G() { return new Guid(++nextId, 0, 0, new byte[] { 0, 0, 0, 0, 0, 0, 0, 7 }); }
    static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static void Reject<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    static void Test(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); } }
    static FloatGameState New(int capacity = 60, long wallet = 0, long version = 1)
    {
        var rules = new FloatGameRules(version, "fixture-float", "fixture-worm", 1, 1000, capacity, Int64.MaxValue, new[] { new FloatSpeciesRule("carp", 1, 1000000, 100) });
        var rig = new FixedFloatRig(G(), version, "fixture-float", G(), G(), G(), 100000, G(), G(), G());
        return FloatGameState.CreateNew(G(), G(), wallet, 1000, G(), "fixture-worm", 1000, rig, rules);
    }
    static FloatOwnerSnapshot Snapshot(FloatGameState state, bool paused = false, bool menu = false, bool shutdown = false)
    { return new FloatOwnerSnapshot(state, false, paused, menu, shutdown); }
    // These tests explicitly adopt pure candidates to construct display inputs;
    // they do not claim file confirmation or execute native windows.
    static FloatGameState Adopt(FloatGameState state, FloatCommand command)
    {
        var result = FloatGameReducer.Reduce(state, command, state.Rules);
        Need(result.HasCandidate, "Fixture candidate missing: " + result.Code); return result.Candidate;
    }
    static FloatGameState Cast(FloatGameState state)
    { return Adopt(state, new CastFloat(state.ProfileId, state.BranchId, G(), G(), G(), state.Sequence, state.Rig.Revision, state.RulesVersion)); }
    static FloatGameState Fish(FloatGameState state)
    { return Adopt(state, new HostFloatFish(state.ProfileId, state.BranchId, G(), G(), state.Session.Id, G(), G(), "carp", 500000, state.Sequence, state.Rig.Revision, state.RulesVersion)); }
    static FloatGameState Land(FloatGameState state) { return Fish(Cast(state)); }
    static FloatGameState Sale(FloatGameState state)
    {
        var ids = new List<Guid>(); foreach (var fish in state.Creel) ids.Add(fish.Id);
        return Adopt(state, new SellFloatFish(state.ProfileId, state.BranchId, G(), G(), ids, state.Sequence, state.Rig.Revision, state.RulesVersion));
    }
    static Dictionary<string, object> ObjectJson(object value)
    { return Json.Deserialize<Dictionary<string, object>>(Json.Serialize(value)); }

    static int Main()
    {
        Test("idle state exposes explicit zero money without invented assets", () => {
            var page = FloatPageProjection.Create(Snapshot(New()), 0, 50);
            Need(page.HasConfirmedState && !page.Faulted && page.WalletMinor == "0" && page.Sequence == "0", "Missing confirmed zero.");
            Need(page.SessionStatus == "idle" && page.SessionId == null && page.PendingFish == null && !page.PendingResult, "Invented active result.");
            Need(page.CreelCount == 0 && page.Creel.Count == 0 && page.CreelCapacity == 60 && page.RigDefinitionKey == "fixture-float" && page.BaitDefinitionKey == "fixture-worm", "Wrong inventory display.");
        });
        Test("active projection keeps paid resources and session identity", () => {
            var state = Cast(New()); var page = FloatPageProjection.Create(Snapshot(state), 0, 1);
            Need(page.SessionStatus == "active" && page.SessionId == state.Session.Id.ToString("D") && !page.PendingResult && page.BaitCount == 999 && page.Stamina == 999, "Active cast displayed incorrectly.");
        });
        Test("capacity pending is distinct from creel and disables simulation", () => {
            var state = Fish(Cast(Land(New(1)))); var page = FloatPageProjection.Create(Snapshot(state), 0, 50);
            Need(page.SessionStatus == "pending" && page.PendingResult && !page.CanSimulate && page.PendingFish != null, "Pending result lost.");
            Need(page.PendingFish.Id == state.Session.PendingFish.Id.ToString("D") && page.CreelCount == 1 && page.Creel.Count == 1 && page.PendingFish.Id != page.Creel[0].Id, "Pending fish silently moved into creel.");
        });
        Test("creel pagination caps rows and keeps total and order", () => {
            var state = New(); for (int i = 0; i < 53; i++) state = Land(state);
            var first = FloatPageProjection.Create(Snapshot(state), 0, 50); var tail = FloatPageProjection.Create(Snapshot(state), 50, 50);
            Need(first.Creel.Count == 50 && tail.Creel.Count == 3 && first.CreelCount == 53 && tail.CreelOffset == 50, "Unbounded or wrong page.");
            Need(first.Creel[49].Id == state.Creel[49].Id.ToString("D") && tail.Creel[0].Id == state.Creel[50].Id.ToString("D"), "Paging changed selection identity.");
            Need(Json.Serialize(first).Length < 16384, "Bounded page exceeded expected host response budget.");
        });
        Test("offset equal to total is a valid empty terminal page", () => {
            var state = Land(New()); var page = FloatPageProjection.Create(Snapshot(state), 1, 50);
            Need(page.CreelOffset == 1 && page.CreelCount == 1 && page.Creel.Count == 0, "End page wrong.");
        });
        Test("invalid and extreme pagination fails before indexing", () => {
            var snapshot = Snapshot(New());
            Reject<ArgumentNullException>(() => FloatPageProjection.Create(null, 0, 1));
            Reject<ArgumentOutOfRangeException>(() => FloatPageProjection.Create(snapshot, -1, 1));
            Reject<ArgumentOutOfRangeException>(() => FloatPageProjection.Create(snapshot, 1, 1));
            Reject<ArgumentOutOfRangeException>(() => FloatPageProjection.Create(snapshot, Int32.MaxValue, 50));
            Reject<ArgumentOutOfRangeException>(() => FloatPageProjection.Create(snapshot, 0, 0));
            Reject<ArgumentOutOfRangeException>(() => FloatPageProjection.Create(snapshot, 0, 51));
            Reject<ArgumentOutOfRangeException>(() => FloatPageProjection.Create(snapshot, 0, Int32.MaxValue));
        });
        Test("Int64 wallet and versions remain exact invariant JSON strings", () => {
            var previous = Thread.CurrentThread.CurrentCulture;
            try {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");
                var page = FloatPageProjection.Create(Snapshot(New(wallet: 9007199254740993L, version: Int64.MaxValue)), 0, 1);
                var values = ObjectJson(page);
                Need((string)values["WalletMinor"] == "9007199254740993" && (string)values["RulesVersion"] == "9223372036854775807" && (string)values["RigRevision"] == "9223372036854775807", "Precision or locale was lost.");
                Need(values["Sequence"] is string && (string)values["Sequence"] == "0", "Sequence became JSON number.");
            } finally { Thread.CurrentThread.CurrentCulture = previous; }
        });
        Test("fish scalar conversion preserves full Int64 without gameplay assumptions", () => {
            // Explicit scalar-boundary fixture, not a catch allowed by current rules.
            var fish = new FloatFish(G(), G(), G(), "carp", Int64.MaxValue, 9007199254740993L, 1);
            var values = ObjectJson(new FloatPageFish(fish));
            Need((string)values["MassMg"] == "9223372036854775807" && (string)values["SaleValueMinor"] == "9007199254740993", "Fish numeric precision was lost.");
        });
        Test("serialized shape excludes history internal identities and rule table", () => {
            var state = Land(New()); var page = FloatPageProjection.Create(Snapshot(state), 0, 1); var wire = Json.Serialize(page); var values = ObjectJson(page);
            var allowed = new HashSet<string>(new[] { "HasConfirmedState", "Faulted", "HostPaused", "ShutdownConfirmed", "MenuOpen", "PendingResult", "CanSimulate", "Sequence", "RulesVersion", "RigRevision", "WalletMinor", "RigDefinitionKey", "BaitDefinitionKey", "SessionId", "SessionStatus", "Stamina", "BaitCount", "CreelCount", "CreelCapacity", "CreelOffset", "Creel", "PendingFish" });
            Need(values.Count == allowed.Count, "Unexpected page field count."); foreach (var key in values.Keys) Need(allowed.Contains(key), "Unexpected page field: " + key);
            var fish = ObjectJson(page.Creel[0]); Need(fish.Count == 4 && fish.ContainsKey("Id") && fish.ContainsKey("SpeciesKey") && fish.ContainsKey("MassMg") && fish.ContainsKey("SaleValueMinor"), "Fish leaked internal metadata.");
            foreach (var id in new[] { state.ProfileId, state.BranchId, state.BaitStackId, state.Rig.Id, state.Rig.RodId, state.Rig.ReelId, state.Creel[0].EncounterId, state.Receipts[0].RequestId, state.Receipts[0].EventId })
                Need(!wire.Contains(id.ToString("D")) && !wire.Contains(id.ToString("N")), "Private identity leaked.");
        });
        Test("faulted snapshots hide even supplied stale confirmed business data", () => {
            var state = Land(New()); var snapshot = new FloatOwnerSnapshot(state, true, false, false, false); var page = FloatPageProjection.Create(snapshot, 0, 50);
            Need(page.Faulted && !page.HasConfirmedState && page.HostPaused && !page.CanSimulate && page.SessionStatus == "unknown", "Fault looked playable.");
            Need(page.Sequence == null && page.WalletMinor == null && page.RulesVersion == null && page.RigRevision == null && page.BaitDefinitionKey == null && page.RigDefinitionKey == null && page.SessionId == null && page.PendingFish == null && page.Creel.Count == 0 && page.CreelCount == 0 && page.Stamina == 0 && page.BaitCount == 0, "Stale confirmed data exposed after fault.");
            Reject<ArgumentOutOfRangeException>(() => FloatPageProjection.Create(snapshot, 1, 50));
        });
        Test("missing state is explicit unknown without fabricated confirmation", () => {
            var page = FloatPageProjection.Create(new FloatOwnerSnapshot(null, false, false, false, false), 0, 1);
            Need(!page.HasConfirmedState && !page.Faulted && !page.CanSimulate && page.HostPaused && page.SessionStatus == "unknown" && page.WalletMinor == null, "Missing state became a new profile.");
        });
        Test("published projection stays immutable after later sale", () => {
            var old = Land(New()); var page = FloatPageProjection.Create(Snapshot(old), 0, 50); var sold = Sale(old); var current = FloatPageProjection.Create(Snapshot(sold), 0, 50);
            Need(page.Creel.Count == 1 && page.WalletMinor == "0" && current.Creel.Count == 0 && current.WalletMinor == "50" && page.Sequence == "2" && current.Sequence == "3", "Old view mutated or new state omitted.");
            Reject<NotSupportedException>(() => ((IList<FloatPageFish>)page.Creel).Clear());
            Need(!Object.ReferenceEquals(page.Creel[0], old.Creel[0]), "Projection returned domain object.");
        });
        Test("pause menu and shutdown eligibility are reported independently", () => {
            var state = New(); var paused = FloatPageProjection.Create(Snapshot(state, paused: true), 0, 1); var menu = FloatPageProjection.Create(Snapshot(state, menu: true), 0, 1); var ended = FloatPageProjection.Create(Snapshot(state, shutdown: true), 0, 1);
            Need(paused.HasConfirmedState && paused.HostPaused && !paused.CanSimulate && !paused.MenuOpen, "Host pause lost.");
            Need(menu.MenuOpen && !menu.HostPaused && !menu.CanSimulate && menu.HasConfirmedState, "Menu conflated with host pause.");
            Need(ended.ShutdownConfirmed && !ended.CanSimulate && ended.HasConfirmedState, "Shutdown lost confirmation.");
        });
        Console.WriteLine("SUMMARY passed=" + passed + " failed=" + failed); return failed == 0 ? 0 : 1;
    }
}
