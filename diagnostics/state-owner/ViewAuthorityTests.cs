using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

// Pure authority contract checks: no browser, input, desktop or game code is loaded.
static class ViewAuthorityTests
{
    static string output;
    static int passed;
    static int failed;

    sealed class Fixture : IDisposable
    {
        public readonly string Path;
        public readonly DurableCounterStore Store;
        public readonly ViewAuthority Owner;
        public readonly string Document = Id();
        public readonly string ObserverDocument = Id();
        public string Lease;
        public Fixture(Action<string> checkpoint = null)
        {
            Path = Journal();
            Store = new DurableCounterStore(Path, checkpoint);
            Owner = new ViewAuthority(Store);
            Owner.Attach("controller", Document, true);
            Owner.Attach("observer", ObserverDocument, false);
            Lease = Owner.Activate("controller");
        }
        public OwnerReply Execute(string request, int delta, long expected)
        {
            return Owner.Execute("controller", Document, Lease, request, delta, expected);
        }
        public void Dispose() { Store.Dispose(); }
    }

    static string Id() { return Guid.NewGuid().ToString("D"); }
    static string Journal() { return System.IO.Path.Combine(output, Id() + ".jsonl"); }
    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    static void Rejected(OwnerReply reply, string error)
    {
        Assert(!reply.Ok && reply.Error == error && reply.Receipt == null, "Expected rejection: " + error);
    }
    static void Run(string name, Action action)
    {
        try { action(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.GetType().Name + " " + error.Message); }
    }

    static void SharedAuthorityAndReadOnlyView()
    {
        using (var f = new Fixture())
        {
            var initial = f.Owner.Snapshot();
            Assert(initial.Sequence == 0 && initial.Value == 0 && !initial.Faulted, "Initial snapshot is invalid.");
            Assert(f.Owner.Activate("observer") == null, "Observer received a control lease.");
            Rejected(f.Owner.Execute("observer", f.ObserverDocument, f.Lease, Id(), 9, 0), "read-only-view");
            Assert(f.Execute(Id(), 7, 0).Ok, "Controller mutation failed.");
            var observerSnapshot = f.Owner.Snapshot();
            Assert(observerSnapshot.OwnerId == initial.OwnerId && observerSnapshot.Sequence == 1 && observerSnapshot.Value == 7,
                "Both views did not read the same committed owner state.");
            initial.Value = 999; observerSnapshot.Sequence = 999;
            Assert(f.Owner.Snapshot().Value == 7 && f.Owner.Snapshot().Sequence == 1, "A returned snapshot mutated authoritative state.");
        }
    }

    static void UniqueLeaseAndHandoff()
    {
        using (var f = new Fixture())
        {
            string secondDocument = Id();
            f.Owner.Attach("second-controller", secondDocument, true);
            string secondLease = f.Owner.Activate("second-controller");
            Assert(secondLease != null && secondLease != f.Lease, "Handoff reused the old lease.");
            Rejected(f.Execute(Id(), 2, 0), "inactive-lease");
            Assert(f.Owner.Execute("second-controller", secondDocument, secondLease, Id(), 3, 0).Ok, "New controller could not commit.");
            string replacement = f.Owner.Activate("second-controller");
            Rejected(f.Owner.Execute("second-controller", secondDocument, secondLease, Id(), 3, 1), "inactive-lease");
            Assert(f.Owner.Execute("second-controller", secondDocument, replacement, Id(), 4, 1).Ok, "Explicit reactivation failed.");
            Assert(f.Owner.Snapshot().Value == 7, "Handoff duplicated a mutation.");
        }
    }

    static void ReloadRejectsOldDocumentAndOldLease()
    {
        using (var f = new Fixture())
        {
            string newDocument = Id();
            f.Owner.Attach("controller", newDocument, true);
            Rejected(f.Execute(Id(), 5, 0), "stale-document");
            Rejected(f.Owner.Execute("controller", newDocument, f.Lease, Id(), 5, 0), "inactive-lease");
            string newLease = f.Owner.Activate("controller");
            Rejected(f.Owner.Execute("controller", f.Document, newLease, Id(), 5, 0), "stale-document");
            Assert(f.Owner.Execute("controller", newDocument, newLease, Id(), 5, 0).Ok, "New document could not explicitly regain control.");
        }
    }

    static void RevocationAndDetachDoNotResurrect()
    {
        using (var f = new Fixture())
        {
            f.Owner.Revoke("observer");
            Assert(f.Execute(Id(), 2, 0).Ok, "Inactive observer revocation cancelled the active controller.");
            f.Owner.Revoke("controller");
            Rejected(f.Execute(Id(), 2, 1), "inactive-lease");
            f.Lease = f.Owner.Activate("controller");
            f.Owner.Detach("controller");
            Rejected(f.Execute(Id(), 2, 1), "stale-document");
            Assert(f.Owner.Activate("controller") == null, "Detached view regained a lease.");
            Assert(f.Owner.Snapshot().Sequence == 1, "Revoked commands changed committed state.");
        }
    }

    static void ConditionalRevocationMatchesViewAndLease()
    {
        using (var f = new Fixture())
        {
            Assert(!f.Owner.Revoke("controller", null), "An absent lease revoked active control.");
            Assert(!f.Owner.Revoke("controller", ""), "An empty lease revoked active control.");
            Assert(!f.Owner.Revoke("controller", Id()), "An unrelated lease revoked active control.");
            Assert(!f.Owner.Revoke("observer", f.Lease), "Another view revoked the controller using its token.");
            Assert(f.Execute(Id(), 1, 0).Ok, "Mismatched cancellation damaged the active lease.");
            Assert(f.Owner.Revoke("controller", f.Lease), "Current view and lease could not be revoked.");
            Assert(!f.Owner.Revoke("controller", f.Lease), "Repeated cancellation reported an active lease.");
            Rejected(f.Execute(Id(), 1, 1), "inactive-lease");
        }
    }

    static void DelayedRevocationPreservesNewActivation()
    {
        using (var f = new Fixture())
        {
            string oldLease = f.Lease;
            f.Lease = f.Owner.Activate("controller");
            Assert(!f.Owner.Revoke("controller", oldLease), "Old blur revoked the new activation.");
            Assert(f.Execute(Id(), 3, 0).Ok, "New activation failed after an old cancellation arrived.");
            string nextDocument = Id();
            f.Owner.Attach("second-controller", nextDocument, true);
            string nextLease = f.Owner.Activate("second-controller");
            Assert(!f.Owner.Revoke("controller", f.Lease), "Old view revoked the handoff recipient.");
            Assert(f.Owner.Execute("second-controller", nextDocument, nextLease, Id(), 4, 1).Ok,
                "New view failed after previous view's cancellation arrived.");
            // Host deactivation intentionally revokes whichever lease is current.
            f.Owner.Revoke("second-controller");
            Rejected(f.Owner.Execute("second-controller", nextDocument, nextLease, Id(), 1, 2), "inactive-lease");
        }
    }

    static void DuplicateBeforeVersionCheck()
    {
        using (var f = new Fixture())
        {
            string request = Id();
            var original = f.Execute(request, 6, 0);
            Assert(original.Ok && !original.Receipt.Duplicate, "Initial request was not committed once.");
            Assert(f.Execute(Id(), 10, 1).Ok, "Intervening request failed.");
            var duplicate = f.Execute(request, 6, 0);
            Assert(duplicate.Ok && duplicate.Receipt.Duplicate && duplicate.Receipt.Sequence == 1 && duplicate.Receipt.Value == 6,
                "Duplicate did not return its original receipt before stale-version rejection.");
            Assert(duplicate.Snapshot.Sequence == 2 && duplicate.Snapshot.Value == 16, "Retry changed current state or returned an old snapshot.");
        }
    }

    static void RequestConflictRejected()
    {
        using (var f = new Fixture())
        {
            string request = Id();
            Assert(f.Execute(request, 4, 0).Ok, "Initial request failed.");
            Rejected(f.Execute(request, 5, 1), "request-conflict");
            Assert(f.Owner.Snapshot().Sequence == 1 && f.Owner.Snapshot().Value == 4, "Conflicting ID changed the state.");
        }
    }

    static void QueuedCommandsWithSameVersion()
    {
        using (var f = new Fixture())
        {
            // Both requests were prepared from revision zero; the owner executes its queue serially.
            long sharedVersion = f.Owner.Snapshot().Sequence;
            var first = f.Execute(Id(), 3, sharedVersion);
            var second = f.Execute(Id(), 11, sharedVersion);
            Assert(first.Ok, "First queued request failed.");
            Rejected(second, "stale-state");
            Assert(f.Owner.Snapshot().Sequence == 1 && f.Owner.Snapshot().Value == 3, "Two requests settled from the same stale revision.");
        }
    }

    static void DeduplicationSurvivesOwnerRestart()
    {
        string path = Journal(), request = Id(), previousOwner;
        using (var store = new DurableCounterStore(path))
        {
            var owner = new ViewAuthority(store);
            string document = Id(); owner.Attach("controller", document, true);
            Assert(owner.Execute("controller", document, owner.Activate("controller"), request, 8, 0).Ok, "Initial owner did not commit.");
            previousOwner = owner.Snapshot().OwnerId;
        }
        using (var store = new DurableCounterStore(path))
        {
            var owner = new ViewAuthority(store);
            string document = Id(); owner.Attach("controller", document, true);
            var reply = owner.Execute("controller", document, owner.Activate("controller"), request, 8, 0);
            Assert(reply.Ok && reply.Receipt.Duplicate && reply.Receipt.Sequence == 1 && reply.Receipt.Value == 8,
                "Explicitly reconnected client did not recover the durable receipt.");
            Assert(owner.Snapshot().OwnerId != previousOwner && owner.Snapshot().Sequence == 1, "Owner restart reused identity or duplicated the record.");
        }
    }

    static void NoRetryThroughRevokedSession()
    {
        using (var f = new Fixture())
        {
            string request = Id(); Assert(f.Execute(request, 3, 0).Ok, "Initial request failed.");
            f.Owner.Revoke("controller");
            Rejected(f.Execute(request, 3, 0), "inactive-lease");
            f.Owner.Attach("controller", Id(), true);
            Rejected(f.Execute(request, 3, 0), "stale-document");
            Assert(f.Owner.Snapshot().Sequence == 1, "A stale session replay changed the journal.");
        }
    }

    static void CrossThreadCallsRejected()
    {
        using (var f = new Fixture())
        {
            var operations = new List<Action> {
                delegate { f.Owner.Snapshot(); },
                delegate { f.Owner.Attach("foreign", Id(), true); },
                delegate { f.Owner.Detach("controller"); },
                delegate { f.Owner.Activate("controller"); },
                delegate { f.Owner.Revoke("controller"); },
                delegate { f.Owner.Revoke("controller", f.Lease); },
                delegate { f.Execute(Id(), 1, 0); }
            };
            int rejected = 0; Exception unexpected = null;
            var thread = new Thread(delegate() {
                foreach (var operation in operations)
                {
                    try { operation(); }
                    catch (InvalidOperationException) { rejected++; }
                    catch (Exception error) { unexpected = error; }
                }
            });
            thread.IsBackground = true; thread.Start();
            Assert(thread.Join(5000), "Foreign-thread test did not finish.");
            Assert(unexpected == null && rejected == operations.Count, "Authority accepted a foreign-thread operation.");
            Assert(f.Execute(Id(), 1, 0).Ok, "Foreign-thread rejection damaged the owner thread.");
        }
    }

    static void UncertainDurableWriteRequiresRestart()
    {
        string path, request = Id();
        using (var f = new Fixture(delegate(string stage) { if (stage == "after-flush") throw new IOException("Injected post-flush fault."); }))
        {
            path = f.Path;
            Rejected(f.Execute(request, 12, 0), "persistence-uncertain");
            Assert(f.Owner.Snapshot().Faulted && f.Owner.Snapshot().Sequence == 0 && f.Owner.Snapshot().Value == 0,
                "Owner acknowledged or published a possibly durable transaction before its receipt.");
            Assert(f.Owner.Activate("controller") == null, "Faulted owner issued a new lease.");
            Rejected(f.Execute(request, 12, 0), "owner-faulted");
            Rejected(f.Execute(Id(), 1, 0), "owner-faulted");
            f.Owner.Attach("second-controller", Id(), true);
            Assert(f.Owner.Activate("second-controller") == null, "New view bypassed the faulted owner.");
        }
        using (var store = new DurableCounterStore(path))
        {
            var owner = new ViewAuthority(store); string document = Id(); owner.Attach("controller", document, true);
            string lease = owner.Activate("controller");
            var recovered = owner.Execute("controller", document, lease, request, 12, 0);
            Assert(recovered.Ok && recovered.Receipt.Duplicate && recovered.Snapshot.Sequence == 1 && recovered.Snapshot.Value == 12,
                "Restart did not reconcile the committed-but-unacknowledged record.");
            Assert(owner.Execute("controller", document, lease, Id(), 1, 1).Ok && owner.Snapshot().Value == 13,
                "Explicit restart could not accept the next valid mutation.");
        }
    }

    static void InvalidCommandsDoNotPoisonOwner()
    {
        using (var f = new Fixture())
        {
            foreach (string request in new[] { null, "", "not-a-guid", "A1234567-1234-1234-1234-123456789ABC" })
                Rejected(f.Execute(request, 1, 0), "invalid-command");
            Rejected(f.Execute(Id(), 0, 0), "invalid-command");
            Rejected(f.Execute(Id(), 1001, 0), "invalid-command");
            Rejected(f.Execute(Id(), 1, -1), "invalid-command");
            Assert(!f.Owner.Snapshot().Faulted && f.Owner.Snapshot().Sequence == 0, "Malformed command changed or poisoned state.");
            Assert(f.Execute(Id(), 1, 0).Ok, "Valid command failed after malformed inputs.");
        }
    }

    static string WriteFullJournalFixture(string path)
    {
        // Prebuild a bounded, valid fixture. This is not 10000 durable Commit
        // calls and does not constitute long-running flush performance evidence.
        var utf8 = new UTF8Encoding(false);
        string firstRequest = null, previous = new string('0', 64);
        using (var writer = new StreamWriter(path, false, utf8))
        using (var hash = SHA256.Create())
        {
            for (int i = 1; i <= 10000; i++)
            {
                string request = Id(); if (i == 1) firstRequest = request;
                string payload = String.Format(CultureInfo.InvariantCulture,
                    "{{\"schema\":1,\"requestId\":\"{0}\",\"delta\":1,\"sequence\":{1},\"value\":{1},\"previousChecksum\":\"{2}\"}}",
                    request, i, previous);
                string checksum = BitConverter.ToString(hash.ComputeHash(utf8.GetBytes(payload))).Replace("-", "").ToLowerInvariant();
                writer.Write(payload.Substring(0, payload.Length - 1) + ",\"checksum\":\"" + checksum + "\"}\n");
                previous = checksum;
            }
        }
        return firstRequest;
    }

    static void JournalCapacityDoesNotPoisonOwnerOrRevokeLease()
    {
        string path = Journal(), firstRequest = WriteFullJournalFixture(path);
        byte[] original = File.ReadAllBytes(path);
        using (var store = new DurableCounterStore(path))
        {
            var owner = new ViewAuthority(store);
            string document = Id(); owner.Attach("controller", document, true);
            string lease = owner.Activate("controller");
            for (int i = 0; i < 2; i++)
            {
                var rejected = owner.Execute("controller", document, lease, Id(), 1, 10000);
                Rejected(rejected, "journal-full");
                Assert(!rejected.Snapshot.Faulted && rejected.Snapshot.Sequence == 10000 && rejected.Snapshot.Value == 10000,
                    "Known capacity rejection faulted or changed the owner.");
                // Reuse the same lease and the old request's stale expectedSequence.
                var retry = owner.Execute("controller", document, lease, firstRequest, 1, 0);
                Assert(retry.Ok && retry.Receipt.Duplicate && retry.Receipt.Sequence == 1 && retry.Receipt.Value == 1,
                    "Capacity rejection revoked the lease or blocked the original receipt.");
                Assert(!retry.Snapshot.Faulted && retry.Snapshot.Sequence == 10000 && retry.Snapshot.Value == 10000,
                    "Full journal retry changed the current snapshot.");
            }
            Rejected(owner.Execute("controller", document, lease, firstRequest, 2, 0), "request-conflict");
            Assert(!owner.Snapshot().Faulted, "Conflicting retry at capacity faulted the owner.");
        }
        Assert(Convert.ToBase64String(File.ReadAllBytes(path)) == Convert.ToBase64String(original),
            "Capacity checks or retries modified the full journal.");
    }

    static void ReturnedReceiptCannotMutateDedupe()
    {
        using (var f = new Fixture())
        {
            string request = Id(); var reply = f.Execute(request, 2, 0);
            reply.Receipt.Value = 900; reply.Receipt.Delta = 900; reply.Receipt.RequestId = Id(); reply.Receipt.Sequence = 900;
            var duplicate = f.Execute(request, 2, 0);
            Assert(duplicate.Ok && duplicate.Receipt.Value == 2 && duplicate.Receipt.Delta == 2 && duplicate.Receipt.Sequence == 1,
                "A client-visible receipt modified the stored deduplication result.");
        }
    }

    static int Main(string[] args)
    {
        if (args.Length != 1 || !Directory.Exists(args[0])) return 2;
        output = System.IO.Path.GetFullPath(args[0]);
        Run("observer reads shared committed state but cannot mutate", SharedAuthorityAndReadOnlyView);
        Run("one control lease survives handoff and explicit reactivation", UniqueLeaseAndHandoff);
        Run("reload rejects both the old document and its lease", ReloadRejectsOldDocumentAndOldLease);
        Run("focus revocation and detach require a new explicit lease", RevocationAndDetachDoNotResurrect);
        Run("page cancellation must match both current view and current lease", ConditionalRevocationMatchesViewAndLease);
        Run("late old-lease cancellation cannot revoke a new activation or handoff", DelayedRevocationPreservesNewActivation);
        Run("duplicate request returns original receipt despite stale expected sequence", DuplicateBeforeVersionCheck);
        Run("same request ID with different payload is rejected", RequestConflictRejected);
        Run("two queued requests from one snapshot settle only once", QueuedCommandsWithSameVersion);
        Run("durable request deduplication survives a new owner", DeduplicationSurvivesOwnerRestart);
        Run("revoked sessions cannot use duplicate replay to bypass authorization", NoRetryThroughRevokedSession);
        Run("every authority operation rejects a foreign thread", CrossThreadCallsRejected);
        Run("post-flush failure blocks writes until durable replay on restart", UncertainDurableWriteRequiresRestart);
        Run("malformed commands are rejected without poisoning the owner", InvalidCommandsDoNotPoisonOwner);
        Run("journal capacity preserves current lease and committed request retries", JournalCapacityDoesNotPoisonOwnerOrRevokeLease);
        Run("returned receipts cannot alter stored deduplication", ReturnedReceiptCannotMutateDedupe);
        Console.WriteLine("RESULT passed=" + passed + " failed=" + failed);
        return failed == 0 ? 0 : 1;
    }
}
