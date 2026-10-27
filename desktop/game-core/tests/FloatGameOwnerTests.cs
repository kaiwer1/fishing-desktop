using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using FishingDesktop.GameCore;

static class FloatGameOwnerTests
{
    static int passed, failed, nextId, nextFile;
    static string privateRoot;
    static Guid G() { return new Guid(++nextId, 0, 0, new byte[] { 0, 0, 0, 0, 0, 0, 0, 5 }); }
    static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static void Reject<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    static void Test(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); } }
    static string PathFor(string label) { return Path.Combine(privateRoot, (++nextFile).ToString(CultureInfo.InvariantCulture) + "-" + label + ".fgs"); }
    static FloatGameState New(int capacity = 2)
    {
        var rules = new FloatGameRules(1, "fixture-float", "fixture-worm", 4, 1000, capacity, Int64.MaxValue, new[] { new FloatSpeciesRule("carp", 100000, 2000000, 1000) });
        var rig = new FixedFloatRig(G(), 1, "fixture-float", G(), G(), G(), 100000, G(), G(), G());
        return FloatGameState.CreateNew(G(), G(), 100, 1000, G(), "fixture-worm", 50, rig, rules);
    }
    static CastFloat Cast(FloatGameState s) { return new CastFloat(s.ProfileId, s.BranchId, G(), G(), G(), s.Sequence, s.Rig.Revision, s.RulesVersion); }
    static RecallFloat Recall(FloatGameState s) { return new RecallFloat(s.ProfileId, s.BranchId, G(), G(), s.Session.Id, s.Sequence, s.Rig.Revision, s.RulesVersion); }
    static HostFloatFish Host(FloatGameState s) { return new HostFloatFish(s.ProfileId, s.BranchId, G(), G(), s.Session.Id, G(), G(), "carp", 500000, s.Sequence, s.Rig.Revision, s.RulesVersion); }
    static RecoverFloatSession Recovery(FloatGameState s) { return new RecoverFloatSession(s.ProfileId, s.BranchId, G(), G(), s.Session.Id, s.Sequence, s.Rig.Revision, s.RulesVersion, FloatRecoveryReason.StartupInterrupted); }
    static ReleaseCurrentFloatFish Release(FloatGameState s) { return new ReleaseCurrentFloatFish(s.ProfileId, s.BranchId, G(), G(), s.Session.Id, s.Sequence, s.Rig.Revision, s.RulesVersion); }
    static SellFloatFish Sale(FloatGameState s) { var ids = new List<Guid>(); foreach (var fish in s.Creel) ids.Add(fish.Id); return new SellFloatFish(s.ProfileId, s.BranchId, G(), G(), ids, s.Sequence, s.Rig.Revision, s.RulesVersion); }
    static void Committed(FloatCommitResult result) { Need(result.Committed && result.Written && !result.IsReplay && result.State != null && result.Receipt != null, "Expected new confirmed transaction."); }
    static void Denied(FloatCommitResult result, string code) { Need(result.Code == code && !result.Committed && !result.Written && result.Receipt == null && result.State == null, "Expected owner denial without profile or receipt: " + code); }
    static void Replayed(FloatCommitResult result, long latest) { Need(result.Committed && !result.Written && result.IsReplay && result.State.Sequence == latest, "Historical reply lost latest state or wrote again."); }
    static byte[] Bytes(string path)
    {
        using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var result = new byte[checked((int)input.Length)]; int offset = 0;
            while (offset < result.Length) { int count = input.Read(result, offset, result.Length - offset); if (count == 0) throw new EndOfStreamException(); offset += count; }
            return result;
        }
    }
    static bool Same(byte[] a, byte[] b) { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
    sealed class Control
    {
        internal readonly FloatGameOwner Owner;
        internal FloatViewTicket View;
        internal FloatDocumentTicket Document;
        internal FloatControlLease Lease;
        internal Control(FloatGameOwner owner)
        {
            Owner = owner; View = owner.OpenView(FloatViewRole.Interaction); Document = owner.AttachDocument(View);
            owner.SetHostPaused(false); Lease = owner.GrantControl(View, Document);
        }
        internal FloatCommitResult Send(FloatCommand command) { return Owner.ExecuteUser(View, Document, Lease, command); }
        internal FloatGameState State { get { return Owner.Snapshot.State; } }
        internal void Land() { Committed(Send(Cast(State))); Committed(Owner.ExecuteHost(Host(State))); }
    }
    sealed class UnknownCommand : FloatCommand
    {
        internal UnknownCommand(FloatGameState s) : base("unknown", s.ProfileId, s.BranchId, G(), G(), G(), s.Sequence, 1, 1) { }
    }

    static int Main(string[] args)
    {
        if (args.Length != 1 || !Directory.Exists(args[0])) { Console.WriteLine("FAIL existing private output root required"); return 2; }
        privateRoot = Path.GetFullPath(args[0]);
        Test("new owner starts paused and view observation grants no control", () => {
            using (var owner = FloatGameOwner.CreateNew(PathFor("paused"), New())) {
                var snap = owner.Snapshot; Need(snap.HostPaused && !snap.CanSimulate && !snap.Faulted && snap.State.Sequence == 0, "Unsafe initial eligibility.");
                var view = owner.OpenView(FloatViewRole.Observer); var doc = owner.AttachDocument(view);
                Need(owner.ReadView(view, doc).State.Sequence == 0, "Observer cannot read confirmed state.");
                Reject<InvalidOperationException>(() => owner.GrantControl(view, doc));
                Denied(owner.ExecuteUser(view, doc, null, Cast(snap.State)), "control-required");
            }
        });
        Test("grant requires attached current interaction document and an explicit host resume", () => {
            using (var owner = FloatGameOwner.CreateNew(PathFor("grant"), New())) {
                var view = owner.OpenView(FloatViewRole.Interaction); Reject<InvalidOperationException>(() => owner.GrantControl(view, null));
                var doc = owner.AttachDocument(view); Reject<InvalidOperationException>(() => owner.GrantControl(view, doc));
                owner.SetHostPaused(false); Denied(owner.ExecuteUser(view, doc, null, Cast(owner.Snapshot.State)), "control-required");
                var lease = owner.GrantControl(view, doc); Committed(owner.ExecuteUser(view, doc, lease, Cast(owner.Snapshot.State)));
            }
        });
        Test("observer slot replacement invalidates old documents without touching interaction authority", () => {
            using (var owner = FloatGameOwner.CreateNew(PathFor("observer"), New())) {
                var control = new Control(owner); var old = owner.OpenView(FloatViewRole.Observer); var oldDoc = owner.AttachDocument(old);
                var current = owner.OpenView(FloatViewRole.Observer); var doc = owner.AttachDocument(current);
                Reject<ArgumentException>(() => owner.ReadView(old, oldDoc)); Need(!owner.CloseView(old), "Stale observer closed current slot.");
                Need(owner.ReadView(current, doc).State.Sequence == 0, "Current observer lost state."); Committed(control.Send(Cast(control.State)));
            }
        });
        Test("interaction document reload revokes old lease and preserves paid session", () => {
            string path = PathFor("document"); using (var owner = FloatGameOwner.CreateNew(path, New())) {
                var control = new Control(owner); Committed(control.Send(Cast(control.State))); var paid = control.State;
                var oldDoc = control.Document; var oldLease = control.Lease; control.Document = owner.AttachDocument(control.View);
                Denied(owner.ExecuteUser(control.View, oldDoc, oldLease, Recall(paid)), "control-required");
                Denied(control.Send(Recall(paid)), "control-required"); Reject<ArgumentException>(() => owner.ReadView(control.View, oldDoc));
                Need(Object.ReferenceEquals(control.State, paid) && paid.BaitCount == 49 && owner.Snapshot.CanSimulate, "Reload settled, refunded or stopped host eligibility.");
                Reject<IOException>(() => { using (var other = FloatGameStore.OpenExisting(path)) { } });
                control.Lease = owner.GrantControl(control.View, control.Document); Committed(control.Send(Recall(paid)));
            }
        });
        Test("closing and rebuilding views keeps store alive and host simulation independent", () => {
            using (var owner = FloatGameOwner.CreateNew(PathFor("close-view"), New())) {
                var control = new Control(owner); Committed(control.Send(Cast(control.State))); var paid = control.State;
                Need(owner.CloseView(control.View), "Current view failed to close."); Need(owner.Snapshot.CanSimulate && Object.ReferenceEquals(owner.Snapshot.State, paid), "Close changed gameplay.");
                Denied(control.Send(Recall(paid)), "control-required"); Committed(owner.ExecuteHost(Host(paid)));
                var replacement = new Control(owner); Need(replacement.State.Creel.Count == 1 && replacement.State.BaitCount == 49, "Rebuilt view replaced profile.");
            }
        });
        Test("old view close and old lease cancellation cannot revoke a replacement grant", () => {
            using (var owner = FloatGameOwner.CreateNew(PathFor("stale-cancel"), New())) {
                var old = new Control(owner); var current = new Control(owner);
                Need(!owner.CloseView(old.View) && !owner.RevokeControl(old.Lease), "Old authority mutated new slot.");
                Denied(old.Send(Cast(old.State)), "control-required"); Committed(current.Send(Cast(current.State)));
            }
        });
        Test("new grant replaces old grant and stale focus cancellation is harmless", () => {
            using (var owner = FloatGameOwner.CreateNew(PathFor("replace-lease"), New())) {
                var control = new Control(owner); var old = control.Lease; control.Lease = owner.GrantControl(control.View, control.Document);
                Need(!owner.RevokeControl(old), "Old lease revoked a new grant."); Denied(owner.ExecuteUser(control.View, control.Document, old, Cast(control.State)), "control-required");
                Committed(control.Send(Cast(control.State))); Need(owner.RevokeControl(control.Lease), "Current focus revoke failed.");
                Denied(control.Send(Recall(control.State)), "control-required"); control.Lease = owner.GrantControl(control.View, control.Document); Committed(control.Send(Recall(control.State)));
            }
        });
        Test("host pause revokes authority and resume never resurrects its lease or receipt access", () => {
            using (var owner = FloatGameOwner.CreateNew(PathFor("pause"), New())) {
                var control = new Control(owner); var cast = Cast(control.State); Committed(control.Send(cast)); owner.SetHostPaused(true);
                Denied(control.Send(cast), "control-required"); owner.SetHostPaused(false); Denied(control.Send(cast), "control-required");
                control.Lease = owner.GrantControl(control.View, control.Document); Replayed(control.Send(cast), 1); Need(control.State.BaitCount == 49, "Pause retry paid twice.");
            }
        });
        Test("foreign and forged ticket identities cannot read grant write or cancel this owner", () => {
            using (var first = FloatGameOwner.CreateNew(PathFor("first"), New())) using (var second = FloatGameOwner.CreateNew(PathFor("second"), New())) {
                var a = new Control(first); var b = new Control(second);
                Reject<ArgumentException>(() => first.ReadView(b.View, b.Document)); Reject<ArgumentException>(() => first.AttachDocument(b.View));
                Reject<InvalidOperationException>(() => first.GrantControl(a.View, b.Document));
                Denied(first.ExecuteUser(a.View, a.Document, b.Lease, Cast(a.State)), "control-required");
                Need(!first.RevokeControl(b.Lease) && !first.CloseView(b.View), "Foreign cancellation changed this owner.");
                Denied(first.ExecuteUser(new FloatViewTicket(), new FloatDocumentTicket(), new FloatControlLease(), Cast(a.State)), "control-required");
                Committed(a.Send(Cast(a.State)));
            }
        });
        Test("wrong thread rejects observation mutation execution and disposal without touching file", () => {
            string path = PathFor("thread"); using (var owner = FloatGameOwner.CreateNew(path, New())) {
                var control = new Control(owner); var cast = Cast(control.State); var bytes = Bytes(path); int rejected = 0; Exception unexpected = null;
                var worker = new Thread(() => {
                    Action[] actions = { () => { var ignored = owner.Snapshot; }, () => owner.OpenView(FloatViewRole.Observer), () => owner.SetMenuOpen(true), () => control.Send(cast), () => owner.Dispose() };
                    foreach (var action in actions) try { action(); } catch (InvalidOperationException) { rejected++; } catch (Exception error) { unexpected = error; }
                }); worker.Start(); worker.Join();
                Need(rejected == 5 && unexpected == null && Same(bytes, Bytes(path)), "Wrong thread escaped owner boundary."); Committed(control.Send(cast));
            }
        });
        Test("page channel rejects host facts unknown types and null before any store write", () => {
            string path = PathFor("user-types"); using (var owner = FloatGameOwner.CreateNew(path, New())) {
                var control = new Control(owner); Committed(control.Send(Cast(control.State))); var fact = Host(control.State); var bytes = Bytes(path);
                Denied(control.Send(fact), "user-command-not-allowed"); Denied(control.Send(Recovery(control.State)), "user-command-not-allowed"); Denied(control.Send(new UnknownCommand(control.State)), "user-command-not-allowed"); Denied(control.Send(null), "user-command-not-allowed");
                Need(Same(bytes, Bytes(path)), "Page submitted a host fact."); Committed(owner.ExecuteHost(fact)); Denied(control.Send(fact), "user-command-not-allowed");
            }
        });
        Test("internal host channel rejects user commands and new facts while host or menu paused", () => {
            using (var owner = FloatGameOwner.CreateNew(PathFor("host-gate"), New())) {
                var control = new Control(owner); Denied(owner.ExecuteHost(Cast(control.State)), "host-command-not-allowed"); Denied(owner.ExecuteHost(null), "host-command-not-allowed");
                Committed(control.Send(Cast(control.State))); var fact = Host(control.State); var recovery = Recovery(control.State); owner.SetHostPaused(true); Denied(owner.ExecuteHost(fact), "simulation-paused"); Denied(owner.ExecuteHost(recovery), "simulation-paused");
                owner.SetHostPaused(false); owner.SetMenuOpen(true); Denied(owner.ExecuteHost(fact), "simulation-paused"); owner.SetMenuOpen(false); Committed(owner.ExecuteHost(fact));
                owner.SetHostPaused(true); Replayed(owner.ExecuteHost(fact), 2);
            }
        });
        Test("menu freezes simulation and new casts but authorized old receipts keep latest state", () => {
            using (var owner = FloatGameOwner.CreateNew(PathFor("menu-cast"), New())) {
                var control = new Control(owner); var cast = Cast(control.State); Committed(control.Send(cast)); Committed(control.Send(Recall(control.State)));
                owner.SetMenuOpen(true); Need(!owner.Snapshot.CanSimulate && owner.Snapshot.MenuOpen && !owner.Snapshot.HostPaused, "Domain and host pauses were conflated.");
                Denied(control.Send(Cast(control.State)), "domain-paused"); Replayed(control.Send(cast), 2); Need(control.State.BaitCount == 49 && control.State.Session == null, "Old cast restored its old session.");
                owner.RevokeControl(control.Lease); Denied(control.Send(cast), "control-required");
            }
        });
        Test("menu sale is a valid confirmed transaction while simulation remains frozen", () => {
            using (var owner = FloatGameOwner.CreateNew(PathFor("menu-sale"), New())) {
                var control = new Control(owner); control.Land(); owner.SetMenuOpen(true); Committed(control.Send(Sale(control.State)));
                Need(control.State.WalletMinor == 600 && control.State.Creel.Count == 0 && !owner.Snapshot.CanSimulate, "Menu incorrectly blocked sale or resumed simulation.");
            }
        });
        Test("full creel pauses simulation but allows explicit current fish release in an open menu", () => {
            using (var owner = FloatGameOwner.CreateNew(PathFor("pending"), New(1))) {
                var control = new Control(owner); control.Land(); Guid old = control.State.Creel[0].Id; Committed(control.Send(Cast(control.State))); var oldCast = control.State.Receipts[2].RequestId;
                var fact = Host(control.State); var blocked = owner.ExecuteHost(fact); Committed(blocked); Need(!blocked.OperationSucceeded && owner.Snapshot.PendingResult && !owner.Snapshot.CanSimulate, "Full creel was not a persisted domain pause.");
                var current = control.State.Session.PendingFish.Id; owner.SetMenuOpen(true); Denied(control.Send(Cast(control.State)), "domain-paused");
                Committed(control.Send(Release(control.State))); Need(control.State.Session == null && control.State.Creel.Count == 1 && control.State.Creel[0].Id == old && control.State.Creel[0].Id != current && !owner.Snapshot.PendingResult && !owner.Snapshot.CanSimulate && oldCast != Guid.Empty, "Release changed old creel or ignored menu pause.");
                owner.SetMenuOpen(false); Need(owner.Snapshot.CanSimulate, "Cleared domain pause stayed latched.");
            }
        });
        Test("immutable snapshot stays at its confirmed sequence after later transactions", () => {
            using (var owner = FloatGameOwner.CreateNew(PathFor("snapshot"), New())) {
                var control = new Control(owner); var before = owner.Snapshot; Committed(control.Send(Cast(control.State)));
                Need(before.State.Sequence == 0 && before.State.BaitCount == 50 && before.State.Session == null && control.State.Sequence == 1, "Old snapshot mutated in place.");
                Reject<NotSupportedException>(() => ((IList<FloatFish>)before.State.Creel).Clear());
            }
        });
        Test("Dispose writes no hidden terminal event and reopening owner explicitly recovers active session", () => {
            string path = PathFor("recovery"); long sequence; byte[] before;
            using (var owner = FloatGameOwner.CreateNew(path, New())) { var control = new Control(owner); Committed(control.Send(Cast(control.State))); sequence = control.State.Sequence; before = Bytes(path); }
            Need(Same(before, Bytes(path)), "Dispose silently settled paid cast.");
            using (var raw = FloatGameStore.OpenExisting(path)) Need(raw.State.Sequence == sequence && raw.State.Session != null, "Disposal wrote a recovery transaction.");
            using (var recovered = FloatGameOwner.OpenExisting(path)) Need(recovered.Snapshot.HostPaused && recovered.Snapshot.State.Session == null && recovered.Snapshot.State.Sequence == sequence + 1 && recovered.Snapshot.State.BaitCount == 49 && recovered.Snapshot.State.Stamina == 996, "Startup failed to settle without refund.");
            var stable = Bytes(path); using (var recovered = FloatGameOwner.OpenExisting(path)) Need(recovered.Snapshot.State.Sequence == sequence + 1, "Second startup repeated terminal settlement.");
            Need(Same(stable, Bytes(path)), "Idle startup appended a gratuitous transaction.");
        });
        Test("explicit Shutdown settles despite host pause and confirms exit before releasing the file", () => {
            string path = PathFor("shutdown"); using (var owner = FloatGameOwner.CreateNew(path, New())) {
                var control = new Control(owner); Committed(control.Send(Cast(control.State))); owner.SetHostPaused(true);
                var result = owner.Shutdown(); Need(result.Confirmed && owner.Snapshot.ShutdownConfirmed && owner.Snapshot.HostPaused && !owner.Snapshot.CanSimulate && owner.Snapshot.State.Session == null && owner.Snapshot.State.Sequence == 2, "Paused shutdown was not confirmed.");
                Denied(control.Send(Cast(owner.Snapshot.State)), "control-required"); Reject<InvalidOperationException>(() => owner.SetHostPaused(false)); Reject<InvalidOperationException>(() => owner.Shutdown());
                Reject<IOException>(() => { using (var other = FloatGameStore.OpenExisting(path)) { } });
            }
            using (var reopened = FloatGameOwner.OpenExisting(path)) Need(reopened.Snapshot.State.Sequence == 2 && reopened.Snapshot.State.BaitCount == 49, "Confirmed shutdown did not survive reopen.");
        });
        Test("pending result survives owner restart and shutdown as the same unresolved fish", () => {
            string path = PathFor("pending-restart"); Guid pending, old; long sequence;
            using (var owner = FloatGameOwner.CreateNew(path, New(1))) { var control = new Control(owner); control.Land(); old = control.State.Creel[0].Id; Committed(control.Send(Cast(control.State))); Committed(owner.ExecuteHost(Host(control.State))); pending = control.State.Session.PendingFish.Id; sequence = control.State.Sequence; }
            using (var owner = FloatGameOwner.OpenExisting(path)) {
                Need(owner.Snapshot.State.Sequence == sequence && owner.Snapshot.PendingResult && owner.Snapshot.State.Session.PendingFish.Id == pending, "Startup invented a new pending fish or auto-released it.");
                var result = owner.Shutdown(); Need(result.Confirmed && owner.Snapshot.State.Sequence == sequence && owner.Snapshot.State.Creel[0].Id == old && owner.Snapshot.State.Session.PendingFish.Id == pending, "Shutdown lost a confirmed result.");
            }
            using (var owner = FloatGameOwner.OpenExisting(path)) { var control = new Control(owner); Committed(control.Send(Release(control.State))); Need(control.State.Sequence == sequence + 1 && control.State.Creel[0].Id == old, "Explicit result release failed after restart."); }
        });
        Test("known prewrite failure leaves owner healthy and same authorized intent retryable", () => {
            bool armed = false; string path = PathFor("prewrite"); Action<FloatSaveFaultPoint> hook = point => { if (armed && point == FloatSaveFaultPoint.BeforeWrite) throw new IOException("Scripted no-write fault."); };
            using (var owner = FloatGameOwner.CreateNew(path, New(), hook)) {
                var control = new Control(owner); var cast = Cast(control.State); var bytes = Bytes(path); armed = true; Reject<IOException>(() => control.Send(cast));
                Need(!owner.Snapshot.Faulted && owner.Snapshot.State.Sequence == 0 && Same(bytes, Bytes(path)), "No-write rejection became a published or uncertain transaction."); armed = false; Committed(control.Send(cast));
            }
        });
        Test("uncertain write revokes all control hides stale state and blocks business until reopen", () => {
            bool armed = false; string path = PathFor("uncertain"); Action<FloatSaveFaultPoint> hook = point => { if (armed && point == FloatSaveFaultPoint.AfterFlush) throw new IOException("Scripted acknowledgement loss."); };
            using (var owner = FloatGameOwner.CreateNew(path, New(), hook)) {
                var control = new Control(owner); var observer = owner.OpenView(FloatViewRole.Observer); var doc = owner.AttachDocument(observer); var cast = Cast(control.State); var stale = owner.Snapshot; armed = true;
                Reject<FloatSaveUncertainException>(() => control.Send(cast)); var fault = owner.Snapshot; Need(fault.Faulted && fault.HostPaused && !fault.CanSimulate && fault.State == null && stale.State.Sequence == 0, "Uncertainty presented stale state as confirmed.");
                Need(owner.ReadView(observer, doc).Faulted && owner.ReadView(observer, doc).State == null, "Observer received stale profile after uncertainty.");
                Denied(control.Send(cast), "control-required"); Reject<InvalidOperationException>(() => owner.GrantControl(control.View, control.Document)); Reject<InvalidOperationException>(() => owner.ExecuteHost(cast));
                Reject<InvalidOperationException>(() => owner.Shutdown()); Need(!owner.RevokeControl(control.Lease) && owner.CloseView(control.View), "Uncertain owner kept control or prevented safe view cleanup.");
            }
            using (var recovered = FloatGameOwner.OpenExisting(path)) Need(!recovered.Snapshot.Faulted && recovered.Snapshot.State.Sequence == 2 && recovered.Snapshot.State.BaitCount == 49 && recovered.Snapshot.State.Session == null, "Reopen repeated debit or skipped startup settlement.");
        });
        Test("partial frame uncertainty blocks owner and preserves rejected original file", () => {
            bool armed = false; string path = PathFor("partial"); Action<FloatSaveFaultPoint> hook = point => { if (armed && point == FloatSaveFaultPoint.AfterPrefix) throw new IOException("Scripted partial frame."); };
            using (var owner = FloatGameOwner.CreateNew(path, New(), hook)) { var control = new Control(owner); var cast = Cast(control.State); armed = true; Reject<FloatSaveUncertainException>(() => control.Send(cast)); Need(owner.Snapshot.Faulted && owner.Snapshot.State == null, "Partial frame did not fault owner."); }
            var bytes = Bytes(path); Reject<InvalidDataException>(() => { using (var owner = FloatGameOwner.OpenExisting(path)) { } }); Need(Same(bytes, Bytes(path)), "Startup trimmed or replaced bad frame.");
        });
        Test("startup settlement failure returns no owner and never resets paid resources", () => {
            string path = PathFor("startup-fail"); using (var owner = FloatGameOwner.CreateNew(path, New())) { var control = new Control(owner); Committed(control.Send(Cast(control.State))); }
            var bytes = Bytes(path); Action<FloatSaveFaultPoint> hook = point => { if (point == FloatSaveFaultPoint.BeforeWrite) throw new IOException("Scripted startup write rejection."); };
            Reject<IOException>(() => { using (var owner = FloatGameOwner.OpenExisting(path, hook)) { } }); Need(Same(bytes, Bytes(path)), "Failed startup changed original save.");
            using (var raw = FloatGameStore.OpenExisting(path)) Need(raw.State.Sequence == 1 && raw.State.Session != null && raw.State.BaitCount == 49, "Failed startup returned replacement resources or held its write lock.");
            using (var recovered = FloatGameOwner.OpenExisting(path)) Need(recovered.Snapshot.State.Sequence == 2 && recovered.Snapshot.State.Session == null, "Retry could not recover original session.");
        });
        Test("known shutdown failure does not claim exit confirmation and can retry exact paid state", () => {
            bool armed = false; string path = PathFor("shutdown-fail"); Action<FloatSaveFaultPoint> hook = point => { if (armed && point == FloatSaveFaultPoint.BeforeWrite) throw new IOException("Scripted shutdown rejection."); };
            using (var owner = FloatGameOwner.CreateNew(path, New(), hook)) {
                var control = new Control(owner); Committed(control.Send(Cast(control.State))); armed = true; Reject<IOException>(() => owner.Shutdown());
                Need(!owner.Snapshot.ShutdownConfirmed && !owner.Snapshot.Faulted && owner.Snapshot.HostPaused && owner.Snapshot.State.Sequence == 1 && owner.Snapshot.State.Session != null, "Known failed shutdown claimed success.");
                Denied(control.Send(Recall(owner.Snapshot.State)), "control-required"); armed = false; Need(owner.Shutdown().Confirmed && owner.Snapshot.State.Sequence == 2, "Shutdown retry failed.");
            }
        });
        Test("same instance authorization is required to query a receipt even across observer documents", () => {
            using (var owner = FloatGameOwner.CreateNew(PathFor("receipt-auth"), New())) {
                var control = new Control(owner); var cast = Cast(control.State); Committed(control.Send(cast)); Committed(control.Send(Recall(control.State)));
                var view = owner.OpenView(FloatViewRole.Observer); var doc = owner.AttachDocument(view);
                Denied(owner.ExecuteUser(view, doc, control.Lease, cast), "control-required"); Replayed(control.Send(cast), 2);
                var replacement = new Control(owner); Denied(control.Send(cast), "control-required"); Replayed(replacement.Send(cast), 2);
            }
        });
        Test("reentrant mutation read grant shutdown and disposal are rejected during file write", () => {
            FloatGameOwner owner = null; Control control = null; bool armed = false; int rejected = 0;
            Action<FloatSaveFaultPoint> hook = point => {
                if (!armed || point != FloatSaveFaultPoint.BeforeWrite) return;
                Action[] actions = { () => { var ignored = owner.Snapshot; }, () => owner.SetHostPaused(true), () => owner.GrantControl(control.View, control.Document), () => owner.Shutdown(), () => owner.Dispose(), () => control.Send(Cast(control.State)) };
                foreach (var action in actions) try { action(); } catch (InvalidOperationException) { rejected++; }
            };
            using (owner = FloatGameOwner.CreateNew(PathFor("reentry"), New(), hook)) { control = new Control(owner); var cast = Cast(control.State); armed = true; Committed(control.Send(cast)); Need(rejected == 6 && !owner.Snapshot.Faulted && control.State.Sequence == 1 && !owner.Snapshot.HostPaused, "Reentry changed lifecycle or control during commit."); }
        });
        Test("missing corrupt and existing paths never silently initialize or replace a profile", () => {
            string missing = PathFor("missing"); Reject<FileNotFoundException>(() => { using (var owner = FloatGameOwner.OpenExisting(missing)) { } }); Need(!File.Exists(missing), "Missing open created a file.");
            string bad = PathFor("corrupt"); var bytes = new byte[] { 1, 2, 3, 4 }; File.WriteAllBytes(bad, bytes);
            Reject<InvalidDataException>(() => { using (var owner = FloatGameOwner.OpenExisting(bad)) { } }); Reject<IOException>(() => { using (var owner = FloatGameOwner.CreateNew(bad, New())) { } }); Need(Same(bytes, Bytes(bad)), "Corrupt original was overwritten.");
        });
        Test("invalid roles and stale documents reject atomically without replacing live authority", () => {
            using (var owner = FloatGameOwner.CreateNew(PathFor("invalid-slot"), New())) {
                var control = new Control(owner); Reject<ArgumentOutOfRangeException>(() => owner.OpenView((FloatViewRole)99)); Reject<ArgumentException>(() => owner.AttachDocument(null));
                Reject<ArgumentException>(() => owner.ReadView(control.View, new FloatDocumentTicket())); Need(!owner.CloseView(null) && !owner.RevokeControl(null), "Null identity changed state."); Committed(control.Send(Cast(control.State)));
            }
        });
        Test("disposed owner rejects access without a hidden transaction and releases its lock", () => {
            string path = PathFor("disposed"); var owner = FloatGameOwner.CreateNew(path, New()); var control = new Control(owner); var cast = Cast(control.State); var bytes = Bytes(path); owner.Dispose(); owner.Dispose();
            Reject<ObjectDisposedException>(() => { var ignored = owner.Snapshot; }); Reject<ObjectDisposedException>(() => control.Send(cast)); Reject<ObjectDisposedException>(() => owner.Shutdown()); Need(Same(bytes, Bytes(path)), "Dispose wrote a command.");
            using (var raw = FloatGameStore.OpenExisting(path)) Need(raw.State.Sequence == 0, "Dispose did not release original file.");
        });
        Console.WriteLine("RESULT " + passed + " passed, " + failed + " failed"); return failed == 0 ? 0 : 1;
    }
}
