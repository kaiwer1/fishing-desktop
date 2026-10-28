using System;
using System.Globalization;
using System.IO;
using FishingDesktop.GameCore;

static class FloatCastPreparationTests
{
    static int passed, failed, nextId, nextFile;
    static string privateRoot;
    static Guid G() { return new Guid(++nextId, 0, 0, new byte[] { 0, 0, 0, 0, 0, 0, 0, 9 }); }
    static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static void Reject<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    static void Test(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); } }
    static string PathFor() { return Path.Combine(privateRoot, (++nextFile).ToString(CultureInfo.InvariantCulture) + ".fltj"); }
    static FloatGameState New(int bait = 50, int stamina = 1000)
    {
        var rules = new FloatGameRules(1, "fixture-float", "fixture-worm", 4, 1000, 2, Int64.MaxValue, new[] { new FloatSpeciesRule("carp", 100000, 2000000, 1000) });
        var rig = new FixedFloatRig(G(), 1, "fixture-float", G(), G(), G(), 50000, G(), G(), G());
        return FloatGameState.CreateNew(G(), G(), 100, stamina, G(), "fixture-worm", bait, rig, rules);
    }
    static CastFloat Cast(FloatGameState s) { return new CastFloat(s.ProfileId, s.BranchId, G(), G(), G(), s.Sequence, s.Rig.Revision, s.RulesVersion); }
    static RecallFloat Recall(FloatGameState s) { return new RecallFloat(s.ProfileId, s.BranchId, G(), G(), s.Session.Id, s.Sequence, s.Rig.Revision, s.RulesVersion); }
    sealed class Fixture : IDisposable
    {
        internal readonly string Path;
        internal readonly FloatGameOwner Owner;
        internal readonly FloatCastPreparation Preparation;
        internal readonly FloatCastTarget Target = new FloatCastTarget(0, 0, 10000, 1, 15000);
        internal readonly FloatViewTicket View;
        internal FloatDocumentTicket Document;
        internal FloatControlLease Lease;
        internal bool TargetValid = true;
        internal Fixture(int bait = 50, int stamina = 1000, Action<FloatSaveFaultPoint> fault = null)
        {
            Path = PathFor(); Owner = FloatGameOwner.CreateNew(Path, New(bait, stamina), fault);
            View = Owner.OpenView(FloatViewRole.Interaction); Document = Owner.AttachDocument(View);
            Owner.SetHostPaused(false); Lease = Owner.GrantControl(View, Document);
            Preparation = new FloatCastPreparation(Owner, new FloatCastPreparationRules(1000, 100), target => TargetValid);
        }
        internal FloatGameState State { get { return Owner.Snapshot.State; } }
        internal FloatCastResult Begin() { return Preparation.Begin(View, Document, Lease, Target); }
        internal FloatCommitResult Send(FloatCommand command) { return Owner.ExecuteUser(View, Document, Lease, command); }
        internal long Length { get { return new FileInfo(Path).Length; } }
        public void Dispose() { Owner.Dispose(); }
    }
    static void Unchanged(Fixture f, FloatGameState state, long length)
    { Need(Object.ReferenceEquals(f.State, state) && f.Length == length, "Unconfirmed gesture changed confirmed state or file."); }
    static FloatCastGesture Prepared(Fixture f)
    {
        var begin = f.Begin(); Need(begin.Accepted && begin.Gesture != null, "Begin rejected: " + begin.Code);
        var release = f.Preparation.Release(begin.Gesture);
        Need(release.Accepted && release.Snapshot.Phase == FloatCastPhase.Prepared && !release.ShouldStartFlight, "Release did not stop at prepared.");
        return begin.Gesture;
    }
    static void CannotCast(Fixture f, FloatCastTarget target)
    {
        var state = f.State; long bytes = f.Length;
        var start = f.Preparation.Begin(f.View, f.Document, f.Lease, target);
        if (start.Accepted) {
            var release = f.Preparation.Release(start.Gesture);
            if (release.Accepted) {
                var commit = f.Preparation.CommitPrepared(start.Gesture);
                Need(!commit.Accepted && !commit.ShouldStartFlight, "Invalid cast was committed.");
            }
        }
        Unchanged(f, state, bytes);
    }

    static int Main(string[] args)
    {
        if (args.Length != 1 || !Directory.Exists(args[0])) { Console.WriteLine("FAIL existing private directory required"); return 2; }
        privateRoot = System.IO.Path.GetFullPath(args[0]);
        Test("configuration rejects invalid charge range and missing collaborators", () => {
            Reject<ArgumentException>(() => new FloatCastPreparationRules(0, 100));
            Reject<ArgumentException>(() => new FloatCastPreparationRules(1000, -1));
            Reject<ArgumentException>(() => new FloatCastPreparationRules(1000, 1001));
            using (var f = new Fixture()) {
                Reject<ArgumentException>(() => new FloatCastPreparation(null, new FloatCastPreparationRules(1000, 100), target => true));
                Reject<ArgumentException>(() => new FloatCastPreparation(f.Owner, null, target => true));
                Reject<ArgumentException>(() => new FloatCastPreparation(f.Owner, new FloatCastPreparationRules(1000, 100), null));
            }
        });
        Test("begin prepares a zero cost gesture without saving", () => {
            using (var f = new Fixture()) {
                var state = f.State; long length = f.Length; var begin = f.Begin();
                Need(begin.Accepted && begin.Gesture != null && begin.Snapshot.Phase == FloatCastPhase.Charging && begin.Snapshot.ElapsedMilliseconds == 0 && begin.Snapshot.PowerPermille == 100 && !begin.ShouldStartFlight, "Unsafe or wrong initial gesture.");
                Need(Object.ReferenceEquals(begin.Snapshot.Target, f.Target), "Target not frozen."); Unchanged(f, state, length);
            }
        });
        Test("repeated down retains the same elapsed gesture and original target", () => {
            using (var f = new Fixture()) {
                var first = f.Begin(); f.Preparation.Advance(first.Gesture, 200);
                var duplicate = f.Preparation.Begin(f.View, f.Document, f.Lease, new FloatCastTarget(3000, 0, 20000, 1, 25000));
                Need(Object.ReferenceEquals(first.Gesture, duplicate.Gesture) && duplicate.Snapshot.ElapsedMilliseconds == 200 && Object.ReferenceEquals(duplicate.Snapshot.Target, f.Target), "Repeated down reset time, identity or target.");
                Need(f.State.Sequence == 0 && f.State.BaitCount == 50, "Repeated down spent bait.");
            }
        });
        Test("equal bounded elapsed time produces equal charge regardless of subdivision", () => {
            using (var a = new Fixture()) using (var b = new Fixture()) {
                var ga = a.Begin().Gesture; var gb = b.Begin().Gesture;
                FloatCastResult ra = null, rb = null;
                for (int i = 0; i < 8; i++) ra = a.Preparation.Advance(ga, 100);
                for (int i = 0; i < 4; i++) rb = b.Preparation.Advance(gb, 200);
                Need(ra.Snapshot.ElapsedMilliseconds == 800 && rb.Snapshot.ElapsedMilliseconds == 800 && ra.Snapshot.PowerPermille == 820 && rb.Snapshot.PowerPermille == 820, "Charge depends on draw/tick subdivision.");
                var zero = a.Preparation.Advance(ga, 0); Need(zero.Snapshot.ElapsedMilliseconds == 800 && zero.Snapshot.PowerPermille == 820, "Zero step advanced.");
            }
        });
        Test("charge saturates and oversized or negative steps reject", () => {
            using (var f = new Fixture()) {
                var g = f.Begin().Gesture; FloatCastResult last = null;
                for (int i = 0; i < 8; i++) last = f.Preparation.Advance(g, 250);
                Need(last.Snapshot.ElapsedMilliseconds == 1000 && last.Snapshot.PowerPermille == 1000, "Charge exceeded cap.");
                Reject<ArgumentException>(() => f.Preparation.Advance(g, -1));
                Reject<ArgumentException>(() => f.Preparation.Advance(g, 251));
                Need(f.State.Sequence == 0, "Elapsed updates saved a transaction.");
            }
        });
        Test("release freezes prepared power without charging resources or flight", () => {
            using (var f = new Fixture()) {
                var state = f.State; long length = f.Length; var g = f.Begin().Gesture; f.Preparation.Advance(g, 250);
                var release = f.Preparation.Release(g);
                Need(release.Accepted && release.Snapshot.Phase == FloatCastPhase.Prepared && release.Snapshot.ElapsedMilliseconds == 250 && release.Snapshot.PowerPermille == 325 && !release.ShouldStartFlight, "Release committed or recomputed charge.");
                var late = f.Preparation.Advance(g, 250); Need(!late.Accepted && late.Snapshot.ElapsedMilliseconds == 250 && late.Snapshot.PowerPermille == 325, "Prepared charge changed.");
                Unchanged(f, state, length);
            }
        });
        Test("cancelled charging and stale callbacks cannot affect the next gesture", () => {
            using (var f = new Fixture()) {
                var state = f.State; long length = f.Length; var old = f.Begin().Gesture;
                var cancelled = f.Preparation.Cancel(old); Need(cancelled.Snapshot.Phase == FloatCastPhase.Cancelled, "Cancel did not close charging.");
                var next = f.Begin(); Need(next.Accepted && !Object.ReferenceEquals(old, next.Gesture), "New gesture not created.");
                foreach (var r in new[] { f.Preparation.Advance(old, 250), f.Preparation.Release(old), f.Preparation.CommitPrepared(old), f.Preparation.Cancel(old) })
                    Need(!r.Accepted && !r.ShouldStartFlight && r.Snapshot.Phase == FloatCastPhase.Charging && Object.ReferenceEquals(r.Gesture, next.Gesture), "Stale callback touched new gesture.");
                Unchanged(f, state, length);
            }
        });
        Test("prepared cancellation prevents committing and preserves resources", () => {
            using (var f = new Fixture()) {
                var state = f.State; long length = f.Length; var g = Prepared(f);
                Need(f.Preparation.Cancel(g).Snapshot.Phase == FloatCastPhase.Cancelled, "Prepared cancellation missing.");
                var late = f.Preparation.CommitPrepared(g); Need(!late.Accepted && !late.ShouldStartFlight, "Cancelled prepared cast committed.");
                Need(f.Begin().Accepted, "Cancellation stranded preparation."); Unchanged(f, state, length);
            }
        });
        Test("commit cannot bypass release and null callbacks are harmless", () => {
            using (var f = new Fixture()) {
                var state = f.State; long length = f.Length; var g = f.Begin().Gesture;
                Need(!f.Preparation.CommitPrepared(g).Accepted, "Charging committed without release.");
                Need(!f.Preparation.Release(null).Accepted && !f.Preparation.CommitPrepared(null).Accepted && !f.Preparation.Cancel(null).Accepted, "Null callback accepted.");
                Unchanged(f, state, length);
            }
        });
        Test("revoked control cancels charging rather than accruing time", () => {
            using (var f = new Fixture()) {
                var state = f.State; long length = f.Length; var g = f.Begin().Gesture; f.Preparation.Advance(g, 100); f.Owner.RevokeControl(f.Lease);
                var result = f.Preparation.Advance(g, 250);
                Need(!result.Accepted && result.Snapshot.Phase == FloatCastPhase.Cancelled && !result.ShouldStartFlight, "Lost control continued charging.");
                Unchanged(f, state, length);
            }
        });
        Test("document replacement makes old release ineligible", () => {
            using (var f = new Fixture()) {
                var state = f.State; long length = f.Length; var g = f.Begin().Gesture;
                f.Document = f.Owner.AttachDocument(f.View); f.Lease = f.Owner.GrantControl(f.View, f.Document);
                var release = f.Preparation.Release(g);
                Need(!release.Accepted && release.Snapshot.Phase == FloatCastPhase.Cancelled && !release.ShouldStartFlight, "Replacement authority revived old input.");
                Unchanged(f, state, length);
            }
        });
        Test("pause and resume cannot restore a prepared cast control lease", () => {
            using (var f = new Fixture()) {
                var state = f.State; long length = f.Length; var g = Prepared(f);
                f.Owner.SetHostPaused(true); f.Owner.SetHostPaused(false); f.Lease = f.Owner.GrantControl(f.View, f.Document);
                var commit = f.Preparation.CommitPrepared(g);
                Need(!commit.Accepted && commit.Snapshot.Phase == FloatCastPhase.Cancelled && !commit.ShouldStartFlight, "Old preparation crossed pause grant boundary.");
                Unchanged(f, state, length);
            }
        });
        Test("menu blocks prepared commit while retaining ability for a fresh later gesture", () => {
            using (var f = new Fixture()) {
                var state = f.State; long length = f.Length; var g = Prepared(f); f.Owner.SetMenuOpen(true);
                var commit = f.Preparation.CommitPrepared(g);
                Need(!commit.Accepted && commit.Snapshot.Phase == FloatCastPhase.Cancelled && !commit.ShouldStartFlight, "Menu allowed release side effects.");
                f.Owner.SetMenuOpen(false); Need(f.Begin().Accepted, "Closed menu prevented fresh authorized preparation."); Unchanged(f, state, length);
            }
        });
        Test("target validation rechecks owner changes and blocks preparation reentry", () => {
            using (var f = new Fixture()) {
                var state = f.State; long length = f.Length; f.TargetValid = false; Need(!f.Begin().Accepted, "Invalid initial target accepted.");
                f.TargetValid = true; var g = Prepared(f); f.TargetValid = false; var commit = f.Preparation.CommitPrepared(g);
                Need(!commit.Accepted && !commit.ShouldStartFlight, "Stale target still committed."); Unchanged(f, state, length);
            }
            using (var f = new Fixture()) {
                bool armed = false;
                var preparation = new FloatCastPreparation(f.Owner, new FloatCastPreparationRules(1000, 100), target => {
                    if (armed) f.Owner.SetMenuOpen(true); return true;
                });
                var state = f.State; long length = f.Length;
                var g = preparation.Begin(f.View, f.Document, f.Lease, f.Target).Gesture;
                Need(preparation.Release(g).Accepted, "Owner-mutation fixture did not prepare."); armed = true;
                var commit = preparation.CommitPrepared(g);
                Need(!commit.Accepted && !commit.ShouldStartFlight && commit.Snapshot.Phase == FloatCastPhase.Cancelled && f.Owner.Snapshot.MenuOpen, "Validator owner mutation bypassed post-callback qualification.");
                Unchanged(f, state, length);
            }
            using (var f = new Fixture()) {
                bool armed = false; int blocked = 0;
                FloatCastPreparation preparation = null; FloatCastGesture gesture = null;
                preparation = new FloatCastPreparation(f.Owner, new FloatCastPreparationRules(1000, 100), target => {
                    if (armed) {
                        Reject<InvalidOperationException>(() => preparation.Cancel(gesture)); blocked++;
                        Need(preparation.Snapshot.Phase == FloatCastPhase.Prepared, "Reentrant cancel changed prepared phase.");
                    }
                    return true;
                });
                gesture = preparation.Begin(f.View, f.Document, f.Lease, f.Target).Gesture;
                Need(preparation.Release(gesture).Accepted, "Reentry fixture did not prepare."); armed = true;
                var commit = preparation.CommitPrepared(gesture);
                Need(commit.Accepted && commit.ShouldStartFlight && blocked == 1 && commit.Snapshot.Phase == FloatCastPhase.Committed && f.State.Sequence == 1 && f.State.BaitCount == 49, "Blocked validator reentry corrupted single commit.");
                var state = f.State; long length = f.Length;
                Need(!preparation.CommitPrepared(gesture).ShouldStartFlight && blocked == 1, "Repeated commit re-entered validator or flight.");
                Unchanged(f, state, length);
            }
        });
        Test("wrong target rules and insufficient frozen line cannot cast", () => {
            using (var f = new Fixture()) CannotCast(f, new FloatCastTarget(0, 0, 10000, 2, 15000));
            using (var f = new Fixture()) CannotCast(f, new FloatCastTarget(0, 0, 10000, 1, 50001));
        });
        Test("empty bait and insufficient stamina do not produce a debit or flight", () => {
            using (var f = new Fixture(bait: 0)) CannotCast(f, f.Target);
            using (var f = new Fixture(stamina: 3)) CannotCast(f, f.Target);
        });
        Test("changed confirmed sequence rejects a prepared cast without another debit", () => {
            using (var f = new Fixture()) {
                var g = Prepared(f); Need(f.Send(Cast(f.State)).Committed && f.Send(Recall(f.State)).Committed, "Fixture intervening commands failed.");
                var state = f.State; long length = f.Length; var commit = f.Preparation.CommitPrepared(g);
                Need(!commit.Accepted && !commit.ShouldStartFlight && state.BaitCount == 49 && state.Sequence == 2, "Stale prepared cast committed against new resources.");
                Unchanged(f, state, length);
            }
        });
        Test("confirmed commit starts flight once and only after one saved debit", () => {
            using (var f = new Fixture()) {
                var g = Prepared(f); long before = f.Length; var commit = f.Preparation.CommitPrepared(g);
                Need(commit.Accepted && commit.ShouldStartFlight && commit.Snapshot.Phase == FloatCastPhase.Committed && f.State.Sequence == 1 && f.State.BaitCount == 49 && f.State.Stamina == 996 && f.Length > before, "Commit was not saved before flight.");
                Need(commit.Snapshot.SessionId == f.State.Session.Id, "Flight session differs from confirmed cast.");
                long after = f.Length; var state = f.State;
                foreach (var late in new[] { f.Preparation.CommitPrepared(g), f.Preparation.Release(g), f.Preparation.Advance(g, 200), f.Preparation.Cancel(g) })
                    Need(!late.ShouldStartFlight && late.Snapshot.Phase == FloatCastPhase.Committed, "Late callback replays flight or rewrites committed phase.");
                Unchanged(f, state, after);
            }
        });
        Test("uncertain saves lock unknown while determinate exceptions leave rejection", () => {
            bool armed = false; string path;
            using (var f = new Fixture(fault: point => { if (armed && point == FloatSaveFaultPoint.AfterFlush) throw new IOException("fixture response loss"); })) {
                path = f.Path; var g = Prepared(f); armed = true; var commit = f.Preparation.CommitPrepared(g);
                Need(!commit.Accepted && !commit.ShouldStartFlight && commit.Snapshot.Phase == FloatCastPhase.Unknown && f.Owner.Snapshot.Faulted, "Unknown write presented as playable or launchable.");
                var again = f.Begin(); Need(!again.Accepted && again.Snapshot.Phase == FloatCastPhase.Unknown && Object.ReferenceEquals(again.Gesture, g), "Unknown save allowed a fresh cast.");
                Need(!f.Preparation.Cancel(g).ShouldStartFlight && !f.Preparation.CommitPrepared(g).ShouldStartFlight, "Unknown write emitted flight later.");
            }
            using (var store = FloatGameStore.OpenExisting(path)) Need(store.State.Sequence == 1 && store.State.BaitCount == 49 && store.State.Stamina == 996 && store.State.Session != null, "Original confirmed write was duplicated or refunded.");
            bool unexpectedArmed = false; int attempts = 0;
            var originalError = new InvalidOperationException("fixture unexpected prewrite failure");
            using (var f = new Fixture(fault: point => {
                if (unexpectedArmed && point == FloatSaveFaultPoint.BeforeWrite) { attempts++; throw originalError; }
            })) {
                var g = Prepared(f); var state = f.State; long length = f.Length; unexpectedArmed = true;
                Exception caught = null;
                try { f.Preparation.CommitPrepared(g); } catch (Exception error) { caught = error; }
                Need(Object.ReferenceEquals(caught, originalError) && f.Preparation.Snapshot.Phase == FloatCastPhase.Rejected && !f.Owner.Snapshot.Faulted && attempts == 1, "Unexpected prewrite failure was hidden or left retryable preparation.");
                Unchanged(f, state, length);
                var retry = f.Preparation.CommitPrepared(g);
                Need(!retry.Accepted && !retry.ShouldStartFlight && retry.Snapshot.Phase == FloatCastPhase.Rejected && attempts == 1, "Rejected preparation retried after unexpected exception.");
                Unchanged(f, state, length);
            }
        });
        Test("committed revoke never refunds and recall permits a new gesture", () => {
            using (var f = new Fixture()) {
                var old = Prepared(f); Need(f.Preparation.CommitPrepared(old).ShouldStartFlight, "Fixture commit failed.");
                f.Owner.RevokeControl(f.Lease); var paid = f.State; long length = f.Length;
                f.Preparation.Cancel(old); Unchanged(f, paid, length);
                f.Lease = f.Owner.GrantControl(f.View, f.Document); Need(f.Send(Recall(f.State)).Committed, "Recall failed.");
                var next = f.Begin(); Need(next.Accepted && !Object.ReferenceEquals(next.Gesture, old) && f.State.Sequence == 2 && f.State.BaitCount == 49 && f.State.Stamina == 996, "Recall refunded or stranded next gesture.");
                Need(!f.Preparation.Release(old).ShouldStartFlight && f.Preparation.Release(next.Gesture).Snapshot.Phase == FloatCastPhase.Prepared, "Late old callback affected next cast.");
            }
        });
        Test("gesture from another preparation cannot advance release cancel or commit", () => {
            using (var a = new Fixture()) using (var b = new Fixture()) {
                var ga = a.Begin().Gesture; var gb = b.Begin().Gesture; var state = b.State; long length = b.Length;
                foreach (var foreign in new[] { b.Preparation.Advance(ga, 250), b.Preparation.Release(ga), b.Preparation.Cancel(ga), b.Preparation.CommitPrepared(ga) })
                    Need(!foreign.Accepted && !foreign.ShouldStartFlight && Object.ReferenceEquals(foreign.Gesture, gb) && foreign.Snapshot.Phase == FloatCastPhase.Charging && foreign.Snapshot.ElapsedMilliseconds == 0, "Foreign gesture affected current input.");
                Unchanged(b, state, length);
            }
        });
        Console.WriteLine("SUMMARY passed=" + passed + " failed=" + failed); return failed == 0 ? 0 : 1;
    }
}
