using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

static class DurableCounterStoreTests
{
    static readonly List<string> passed = new List<string>();
    static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
    static string root;
    static string NewId() { return Guid.NewGuid().ToString("D"); }
    static string Journal() { return Path.Combine(root, NewId() + ".jsonl"); }
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    static void Test(string name, Action action) { action(); passed.Add(name); Console.WriteLine("PASS " + name); }

    static int Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "--hold-probe") return HoldProbe(args[1], args[2], args[3]);
        if (args.Length != 1 || !Directory.Exists(args[0])) return 3;
        root = Path.GetFullPath(args[0]);
        try
        {
            Run();
            File.WriteAllText(Path.Combine(root, "store-tests.json"), new JavaScriptSerializer().Serialize(
                new { passed = true, count = passed.Count, checks = passed.ToArray(), gameLoaded = false }), Utf8);
            Console.WriteLine(passed.Count + " durable store checks passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    static int HoldProbe(string journal, string ready, string release)
    {
        try
        {
            using (var store = new DurableCounterStore(journal))
            {
                var receipt = store.Commit("11111111-1111-4111-8111-111111111111", 7);
                File.WriteAllText(ready, new JavaScriptSerializer().Serialize(new {
                    owner = true, pid = Process.GetCurrentProcess().Id, value = store.Value,
                    sequence = store.Sequence, duplicate = receipt.Duplicate }), Utf8);
                var elapsed = Stopwatch.StartNew();
                while (!File.Exists(release) && elapsed.Elapsed.TotalSeconds < 20) Thread.Sleep(25);
                return 0;
            }
        }
        catch (IOException)
        {
            File.WriteAllText(ready, new JavaScriptSerializer().Serialize(new {
                owner = false, pid = Process.GetCurrentProcess().Id }), Utf8);
            return 2;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 3; }
    }

    static void Run()
    {
        Test("commit, duplicate and copied receipts", delegate {
            string path = Journal(), id = NewId();
            using (var store = new DurableCounterStore(path))
            {
                var first = store.Commit(id, 4);
                long length = new FileInfo(path).Length;
                Assert(first.Sequence == 1 && first.Value == 4 && !first.Duplicate, "First receipt incorrect.");
                first.Value = 987;
                var second = store.Commit(id, 4);
                Assert(second.Value == 4 && second.Sequence == 1 && second.Duplicate, "Duplicate receipt incorrect.");
                CounterReceipt query;
                Assert(store.TryGetReceipt(id, out query) && query.Duplicate && query.Value == 4, "Lookup failed.");
                query.Delta = 99;
                Assert(store.Commit(id, 4).Delta == 4, "Lookup leaked mutable internal receipt.");
                Assert(!store.TryGetReceipt(NewId(), out query) && query == null, "Missing lookup wrong.");
                Assert(store.Value == 4 && store.Sequence == 1 && new FileInfo(path).Length == length, "Duplicate appended.");
            }
        });
        Test("conflicting request is rejected without poisoning valid writes", delegate {
            using (var store = new DurableCounterStore(Journal()))
            {
                string id = NewId(); store.Commit(id, 2);
                Throws<InvalidOperationException>(delegate { store.Commit(id, 3); });
                Assert(store.Commit(NewId(), 1).Value == 3, "Conflict changed state.");
            }
        });
        Test("invalid ID and delta do not touch journal", delegate {
            string path = Journal();
            using (var store = new DurableCounterStore(path))
            {
                Throws<ArgumentException>(delegate { store.Commit("not-a-guid", 1); });
                Throws<ArgumentException>(delegate { store.Commit("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA", 1); });
                Throws<ArgumentException>(delegate { store.Commit(Guid.NewGuid().ToString("N"), 1); });
                Throws<ArgumentOutOfRangeException>(delegate { store.Commit(NewId(), 0); });
                Throws<ArgumentOutOfRangeException>(delegate { store.Commit(NewId(), 1001); });
                Assert(store.Sequence == 0 && new FileInfo(path).Length == 0, "Invalid requests wrote data.");
            }
        });
        Test("reopen restores chain and old original receipt", delegate {
            string path = Journal(), id = NewId();
            using (var store = new DurableCounterStore(path)) { store.Commit(id, 3); store.Commit(NewId(), 5); }
            using (var store = new DurableCounterStore(path))
            {
                Assert(store.Value == 8 && store.Sequence == 2, "Restore mismatch.");
                Assert(store.Commit(id, 3).Value == 3, "Original receipt not retained.");
                Assert(store.Commit(NewId(), 4).Value == 12, "Append after restore failed.");
            }
            using (var store = new DurableCounterStore(path)) Assert(store.Value == 12 && store.Sequence == 3, "Second restore mismatch.");
        });
        Test("concurrent open rejects another writer and releases on dispose", delegate {
            string path = Journal();
            using (var first = new DurableCounterStore(path))
                Throws<IOException>(delegate { using (var second = new DurableCounterStore(path)) { } });
            using (var next = new DurableCounterStore(path)) Assert(next.Sequence == 0, "Release failed.");
        });
        Test("incomplete canonical tail is backed up before truncation", delegate {
            string path = Journal();
            using (var store = new DurableCounterStore(path)) store.Commit(NewId(), 5);
            long length = new FileInfo(path).Length;
            byte[] tail = Utf8.GetBytes("{\"schema\":1,\"requestId\":\"" + NewId() + "\",\"delta\":");
            using (var writer = new FileStream(path, FileMode.Append)) writer.Write(tail, 0, tail.Length);
            using (var store = new DurableCounterStore(path))
            {
                Assert(store.Value == 5 && new FileInfo(path).Length == length, "Tail was not repaired.");
                string[] backups = Directory.GetFiles(root, Path.GetFileName(path) + ".incomplete-tail-*.bin");
                Assert(backups.Length == 1 && Convert.ToBase64String(File.ReadAllBytes(backups[0])) == Convert.ToBase64String(tail), "Exact tail backup missing.");
                store.Commit(NewId(), 2);
            }
            using (var store = new DurableCounterStore(path)) Assert(store.Value == 7 && store.Sequence == 2, "Tail repair broke next append.");
        });
        Test("missing LF preserves an acknowledged complete record and retry receipt", delegate {
            string path = Journal(), secondId = NewId();
            using (var store = new DurableCounterStore(path)) { store.Commit(NewId(), 5); store.Commit(secondId, 2); }
            byte[] original = File.ReadAllBytes(path), shortened = new byte[original.Length - 1];
            Array.Copy(original, shortened, shortened.Length); File.WriteAllBytes(path, shortened);
            using (var store = new DurableCounterStore(path))
            {
                Assert(store.Value == 7 && store.Sequence == 2, "Acknowledged tail rolled back.");
                Assert(store.Commit(secondId, 2).Duplicate, "Acknowledged request deduplication was lost.");
            }
            Assert(Convert.ToBase64String(File.ReadAllBytes(path)) == Convert.ToBase64String(original), "Missing framing marker not repaired exactly.");
            Assert(Directory.GetFiles(root, Path.GetFileName(path) + ".incomplete-tail-*.bin").Length == 0, "Complete tail was treated as discarded bytes.");
        });
        Test("canonical append prefixes at each field boundary recover with exact backups", delegate {
            string checksum;
            string complete = BuildRecord(NewId(), 1000, 1, 1000, new string('0', 64), out checksum);
            int[] cuts = { 1, 8, complete.IndexOf("requestId", StringComparison.Ordinal) + 14,
                complete.IndexOf("delta", StringComparison.Ordinal) + 8,
                complete.IndexOf("sequence", StringComparison.Ordinal) + 11,
                complete.IndexOf("value", StringComparison.Ordinal) + 8,
                complete.IndexOf("previousChecksum", StringComparison.Ordinal) + 23,
                complete.IndexOf("\"checksum\"", StringComparison.Ordinal) + 17, complete.Length - 1 };
            foreach (int cut in cuts)
            {
                string path = Journal(), prefix = complete.Substring(0, cut);
                File.WriteAllText(path, prefix, Utf8);
                using (var store = new DurableCounterStore(path)) Assert(store.Value == 0 && new FileInfo(path).Length == 0, "Canonical prefix not recovered.");
                string[] backups = Directory.GetFiles(root, Path.GetFileName(path) + ".incomplete-tail-*.bin");
                Assert(backups.Length == 1 && File.ReadAllText(backups[0]) == prefix, "Field-boundary backup mismatch.");
            }
        });
        Test("complete damaged tails reject without changing or backing up bytes", delegate {
            string originalPath = Journal();
            using (var store = new DurableCounterStore(originalPath)) store.Commit(NewId(), 5);
            string valid = File.ReadAllText(originalPath).TrimEnd('\n');
            string wrongChecksum = valid.Substring(0, valid.Length - 3) + (valid[valid.Length - 3] == '0' ? '1' : '0') + "\"}";
            string unusedChecksum;
            string wrongChain = valid + "\n" + BuildRecord(NewId(), 2, 2, 7, new string('0', 64), out unusedChecksum);
            foreach (string damaged in new[] { valid.Replace("\"value\":5", "\"value\":6"), valid.Replace("\"schema\":1", "\"schema\":2"), wrongChecksum, wrongChain, valid + "x", "{\"schema\":1}" })
            {
                string path = Journal(); File.WriteAllText(path, damaged, Utf8);
                Throws<InvalidDataException>(delegate { using (var store = new DurableCounterStore(path)) { } });
                Assert(File.ReadAllText(path) == damaged, "Damaged complete tail was altered.");
                Assert(Directory.GetFiles(root, Path.GetFileName(path) + ".incomplete-tail-*.bin").Length == 0, "Damaged complete tail was classified as recoverable.");
            }
        });
        Test("noncanonical and invalid UTF-8 tails reject without truncation", delegate {
            foreach (byte[] tail in new[] { new byte[] { 123, 34, 226, 130 }, Utf8.GetBytes("{\"schema\":9"), Utf8.GetBytes("garbage") })
            {
                string path = Journal();
                using (var store = new DurableCounterStore(path)) store.Commit(NewId(), 5);
                using (var writer = new FileStream(path, FileMode.Append)) writer.Write(tail, 0, tail.Length);
                byte[] original = File.ReadAllBytes(path);
                Throws<InvalidDataException>(delegate { using (var store = new DurableCounterStore(path)) { } });
                Assert(Convert.ToBase64String(File.ReadAllBytes(path)) == Convert.ToBase64String(original), "Unrecognized tail was altered.");
            }
        });
        Test("corrupt complete line preserves all bytes including tail", delegate {
            string path = Journal();
            using (var store = new DurableCounterStore(path)) store.Commit(NewId(), 5);
            string text = File.ReadAllText(path).Replace("\"value\":5", "\"value\":6") + "{partial";
            File.WriteAllText(path, text, Utf8);
            Throws<InvalidDataException>(delegate { using (var store = new DurableCounterStore(path)) { } });
            Assert(File.ReadAllText(path) == text, "Invalid journal was altered.");
            // Constructor failure must also release the write handle.
            using (var access = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        });
        Test("malformed JSON, schema, blank line and duplicate fields rejected", delegate {
            foreach (string variant in new[] { "{oops}\n", "\n", "{\"schema\":2}\n", "{\"schema\":1,\"schema\":1}\n" })
            {
                string path = Journal(); File.WriteAllText(path, variant, Utf8);
                Throws<InvalidDataException>(delegate { using (var store = new DurableCounterStore(path)) { } });
                Assert(File.ReadAllText(path) == variant, "Bad schema was reset.");
            }
        });
        Test("invalid UTF-8 complete record rejected", delegate {
            string path = Journal(); byte[] bytes = { 255, 10 }; File.WriteAllBytes(path, bytes);
            Throws<InvalidDataException>(delegate { using (var store = new DurableCounterStore(path)) { } });
            Assert(File.ReadAllBytes(path).Length == 2, "Bad UTF-8 was changed.");
        });
        Test("reordered and duplicated complete records rejected", delegate {
            string path = Journal();
            using (var store = new DurableCounterStore(path)) { store.Commit(NewId(), 5); store.Commit(NewId(), 2); }
            string[] lines = File.ReadAllLines(path);
            foreach (string text in new[] { lines[1] + "\n" + lines[0] + "\n", lines[0] + "\n" + lines[0] + "\n" })
            {
                File.WriteAllText(path, text, Utf8);
                Throws<InvalidDataException>(delegate { using (var store = new DurableCounterStore(path)) { } });
            }
        });
        Test("valid checksum still cannot admit wrong value or broken chain", delegate {
            foreach (bool wrongValue in new[] { false, true })
            {
                string path = Journal(), checksum;
                string first = BuildRecord(NewId(), 3, 1, 3, new string('0', 64), out checksum);
                string second = BuildRecord(NewId(), 2, 2, wrongValue ? 6 : 5,
                    wrongValue ? checksum : new string('0', 64), out checksum);
                File.WriteAllText(path, first + "\n" + second + "\n", Utf8);
                Throws<InvalidDataException>(delegate { using (var store = new DurableCounterStore(path)) { } });
            }
        });
        foreach (string stage in new[] { "before-write", "after-write", "after-flush" })
        {
            string faultStage = stage;
            Test("failure at " + stage + " closes writes until reopen", delegate {
                string path = Journal(), id = NewId();
                using (var store = new DurableCounterStore(path, delegate(string current) {
                    if (current == faultStage) throw new IOException("Injected checkpoint failure."); }))
                {
                    Throws<IOException>(delegate { store.Commit(id, 5); });
                    Throws<InvalidOperationException>(delegate { store.Commit(NewId(), 1); });
                    CounterReceipt ignored;
                    Throws<InvalidOperationException>(delegate { store.TryGetReceipt(id, out ignored); });
                }
                using (var store = new DurableCounterStore(path))
                {
                    var receipt = store.Commit(id, 5);
                    Assert(store.Value == 5 && store.Sequence == 1, "Retry double counted or lost the command.");
                    if (faultStage == "after-flush") Assert(receipt.Duplicate, "Flushed unacknowledged command was not deduplicated.");
                    if (faultStage == "before-write") Assert(!receipt.Duplicate, "Before-write failure wrote data.");
                }
            });
        }
        Test("real disposed file causes write failure and fail-closed state", delegate {
            DurableCounterStore store = null;
            store = new DurableCounterStore(Journal(), delegate(string stage) {
                if (stage == "before-write")
                {
                    // Fault injection into the real file object, not a mocked Commit.
                    var field = typeof(DurableCounterStore).GetField("stream", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    ((FileStream)field.GetValue(store)).Dispose();
                }
            });
            using (store)
            {
                Throws<ObjectDisposedException>(delegate { store.Commit(NewId(), 1); });
                Throws<InvalidOperationException>(delegate { store.Commit(NewId(), 1); });
            }
        });
        Test("real disposed file causes flush failure and retry recovers once", delegate {
            string path = Journal(), id = NewId(); DurableCounterStore store = null;
            store = new DurableCounterStore(path, delegate(string stage) {
                if (stage == "after-write")
                {
                    var field = typeof(DurableCounterStore).GetField("stream", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    ((FileStream)field.GetValue(store)).Dispose();
                }
            });
            using (store)
            {
                Throws<ObjectDisposedException>(delegate { store.Commit(id, 9); });
                Throws<InvalidOperationException>(delegate { store.Commit(NewId(), 1); });
            }
            using (var recovered = new DurableCounterStore(path)) Assert(recovered.Commit(id, 9).Value == 9 && recovered.Sequence == 1, "Flush fault retry duplicated.");
        });
        Test("10000-record bound permits duplicates and rejects new requests", delegate {
            string path = Journal(), checksum = new string('0', 64), firstId = null;
            using (var writer = new StreamWriter(path, false, Utf8))
                for (int i = 1; i <= 10000; i++)
                {
                    string id = NewId(); if (i == 1) firstId = id;
                    writer.Write(BuildRecord(id, 1, i, i, checksum, out checksum) + "\n");
                }
            using (var store = new DurableCounterStore(path))
            {
                Assert(store.Sequence == 10000 && store.Value == 10000, "Bound fixture restore failed.");
                Assert(store.Commit(firstId, 1).Duplicate, "Bound rejected retry.");
                Throws<InvalidOperationException>(delegate { store.Commit(NewId(), 1); });
            }
        });
        Test("disposed stores reject operations", delegate {
            var store = new DurableCounterStore(Journal()); store.Dispose(); store.Dispose();
            Throws<ObjectDisposedException>(delegate { store.Commit(NewId(), 1); });
            CounterReceipt ignored;
            Throws<ObjectDisposedException>(delegate { store.TryGetReceipt(NewId(), out ignored); });
        });
    }

    static string BuildRecord(string id, int delta, int sequence, int value, string previous, out string checksum)
    {
        string payload = String.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{{\"schema\":1,\"requestId\":\"{0}\",\"delta\":{1},\"sequence\":{2},\"value\":{3},\"previousChecksum\":\"{4}\"}}",
            id, delta, sequence, value, previous);
        using (var hash = SHA256.Create()) checksum = BitConverter.ToString(hash.ComputeHash(Utf8.GetBytes(payload))).Replace("-", "").ToLowerInvariant();
        return payload.Substring(0, payload.Length - 1) + ",\"checksum\":\"" + checksum + "\"}";
    }
}
