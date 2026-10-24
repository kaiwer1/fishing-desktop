using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

// This rejection is known before an append starts; it is not an uncertain write.
sealed class JournalCapacityExceededException : InvalidOperationException
{
    public JournalCapacityExceededException() : base("Diagnostic journal record limit reached.") { }
}

sealed class CounterReceipt
{
    public string RequestId { get; internal set; }
    public int Delta { get; internal set; }
    public long Sequence { get; internal set; }
    public long Value { get; internal set; }
    public bool Duplicate { get; internal set; }

    internal CounterReceipt Copy(bool duplicate)
    {
        return new CounterReceipt { RequestId = RequestId, Delta = Delta,
            Sequence = Sequence, Value = Value, Duplicate = duplicate };
    }
}

// Isolated P0 counter only. This is not a game save format or a security boundary.
// SHA-256 detects accidental damage; it is not a keyed authenticity guarantee.
sealed class DurableCounterStore : IDisposable
{
    const int MaximumRecords = 10000;
    const int MaximumLineBytes = 1024;
    static readonly string InitialChecksum = new string('0', 64);
    static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
    readonly object sync = new object();
    readonly Dictionary<string, CounterReceipt> receipts = new Dictionary<string, CounterReceipt>(StringComparer.Ordinal);
    readonly Action<string> checkpoint;
    FileStream stream;
    bool failed;
    bool disposed;
    string previousChecksum = InitialChecksum;
    long value;
    long sequence;

    public long Value { get { lock (sync) { return value; } } }
    public long Sequence { get { lock (sync) { return sequence; } } }

    public DurableCounterStore(string journalPath, Action<string> checkpoint = null)
    {
        if (String.IsNullOrWhiteSpace(journalPath)) throw new ArgumentException("A journal path is required.", "journalPath");
        this.checkpoint = checkpoint;
        // The open handle excludes every other writer, including another process/build.
        stream = new FileStream(journalPath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
            FileShare.Read, 4096, FileOptions.WriteThrough);
        try { Load(); }
        catch { stream.Dispose(); stream = null; throw; }
    }

    public bool TryGetReceipt(string requestId, out CounterReceipt receipt)
    {
        ValidateRequestId(requestId);
        lock (sync)
        {
            EnsureAvailable();
            CounterReceipt stored;
            if (receipts.TryGetValue(requestId, out stored))
            {
                receipt = stored.Copy(true);
                return true;
            }
            receipt = null;
            return false;
        }
    }

    public CounterReceipt Commit(string requestId, int delta)
    {
        ValidateRequestId(requestId);
        if (delta < 1 || delta > 1000) throw new ArgumentOutOfRangeException("delta");
        lock (sync)
        {
            EnsureAvailable();
            CounterReceipt stored;
            if (receipts.TryGetValue(requestId, out stored))
            {
                if (stored.Delta != delta) throw new InvalidOperationException("Request ID already has a different delta.");
                return stored.Copy(true);
            }
            if (sequence >= MaximumRecords) throw new JournalCapacityExceededException();
            var receipt = new CounterReceipt { RequestId = requestId, Delta = delta,
                Sequence = checked(sequence + 1), Value = checked(value + delta), Duplicate = false };
            string payload = Payload(receipt, previousChecksum);
            string checksum = Checksum(payload);
            byte[] bytes = Utf8.GetBytes(Record(payload, checksum) + "\n");
            try
            {
                Checkpoint("before-write");
                stream.Write(bytes, 0, bytes.Length);
                Checkpoint("after-write");
                // A receipt may be returned only after the durable flush succeeds.
                stream.Flush(true);
                Checkpoint("after-flush");
                receipts.Add(requestId, receipt);
                sequence = receipt.Sequence;
                value = receipt.Value;
                previousChecksum = checksum;
                return receipt.Copy(false);
            }
            catch
            {
                // The on-disk outcome may be unknown. Never append again in this instance.
                failed = true;
                throw;
            }
        }
    }

    void Checkpoint(string stage) { if (checkpoint != null) checkpoint(stage); }
    void EnsureAvailable()
    {
        if (disposed) throw new ObjectDisposedException("DurableCounterStore");
        if (failed) throw new InvalidOperationException("Journal outcome is uncertain; close and reopen the store.");
    }

    internal static void ValidateRequestId(string requestId)
    {
        Guid parsed;
        if (requestId == null || !Guid.TryParseExact(requestId, "D", out parsed) ||
            !String.Equals(parsed.ToString("D"), requestId, StringComparison.Ordinal))
            throw new ArgumentException("Request ID must be a canonical lowercase D-format GUID.", "requestId");
    }

    void Load()
    {
        if (stream.Length > (long)(MaximumRecords + 1) * MaximumLineBytes)
            throw new InvalidDataException("Diagnostic journal exceeds its bounded size.");
        byte[] bytes = new byte[checked((int)stream.Length)];
        int read = 0;
        while (read < bytes.Length)
        {
            int count = stream.Read(bytes, read, bytes.Length - read);
            if (count == 0) throw new EndOfStreamException("Journal changed while being read.");
            read += count;
        }
        int start = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != (byte)'\n') continue;
            if (i - start < 1 || i - start > MaximumLineBytes)
                throw new InvalidDataException("Journal contains an invalid complete line.");
            try { LoadRecord(Utf8.GetString(bytes, start, i - start)); }
            catch (InvalidDataException) { throw; }
            catch (Exception error)
            {
                throw new InvalidDataException("Journal contains an invalid complete record.", error);
            }
            start = i + 1;
        }
        // A missing final LF may be framing damage to an already acknowledged
        // record. Preserve a fully valid record; do not silently roll it back.
        if (start < bytes.Length)
        {
            RecoverTail(bytes, start);
        }
        stream.Position = stream.Length;
    }

    void RecoverTail(byte[] bytes, int start)
    {
        int length = bytes.Length - start;
        if (length > MaximumLineBytes) throw new InvalidDataException("Journal tail exceeds the record size bound.");
        string tail;
        try { tail = Utf8.GetString(bytes, start, length); }
        catch (DecoderFallbackException error)
        {
            throw new InvalidDataException("Journal tail is not a valid prefix of this ASCII record format.", error);
        }

        // First distinguish a complete record from an incomplete append. A
        // canonical complete record without LF remains part of the durable state.
        if (!IsIncompleteCanonicalPrefix(tail))
        {
            try { LoadRecord(tail); }
            catch (InvalidDataException) { throw; }
            catch (Exception error) { throw new InvalidDataException("Journal has a damaged complete tail.", error); }
            stream.Position = stream.Length;
            stream.WriteByte((byte)'\n');
            stream.Flush(true);
            return;
        }

        // Save the exact discarded suffix durably before changing the journal.
        // Backups stay beside this private diagnostic journal, never in the repo.
        string backup = stream.Name + ".incomplete-tail-" + Guid.NewGuid().ToString("N") + ".bin";
        using (var copy = new FileStream(backup, FileMode.CreateNew, FileAccess.Write,
            FileShare.Read, 4096, FileOptions.WriteThrough))
        {
            copy.Write(bytes, start, length);
            copy.Flush(true);
        }
        stream.SetLength(start);
        stream.Flush(true);
    }

    bool IsIncompleteCanonicalPrefix(string tail)
    {
        // Only exact prefixes that Commit could produce qualify for automatic
        // truncation. Malformed JSON and arbitrary non-newline damage fail closed.
        if (sequence >= MaximumRecords) return false;
        int position = 0;
        if (!ConsumePrefix(tail, ref position, "{\"schema\":1,\"requestId\":\"")) return position == tail.Length;
        int idStart = position;
        for (int i = 0; i < 36; i++)
        {
            if (position == tail.Length) return true;
            char current = tail[position++];
            bool dash = i == 8 || i == 13 || i == 18 || i == 23;
            if (dash ? current != '-' : !((current >= '0' && current <= '9') || (current >= 'a' && current <= 'f')))
                return false;
        }
        string requestId = tail.Substring(idStart, 36);
        if (receipts.ContainsKey(requestId) || sequence >= MaximumRecords) return false;
        if (!ConsumePrefix(tail, ref position, "\",\"delta\":")) return position == tail.Length;
        int deltaStart = position;
        while (position < tail.Length && tail[position] >= '0' && tail[position] <= '9') position++;
        if (position == deltaStart) return position == tail.Length;
        string digits = tail.Substring(deltaStart, position - deltaStart);
        int delta;
        if (digits[0] == '0' || !Int32.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out delta) || delta > 1000)
            return false;
        if (position == tail.Length) return true;
        var receipt = new CounterReceipt { RequestId = requestId, Delta = delta, Sequence = sequence + 1, Value = value + delta };
        string payload = Payload(receipt, previousChecksum);
        string expected = Record(payload, Checksum(payload));
        return tail.Length < expected.Length && expected.StartsWith(tail, StringComparison.Ordinal);
    }

    static bool ConsumePrefix(string text, ref int position, string expected)
    {
        for (int i = 0; i < expected.Length; i++)
        {
            if (position == text.Length) return false;
            if (text[position] != expected[i]) { position = -1; return false; }
            position++;
        }
        return true;
    }

    void LoadRecord(string line)
    {
        if (sequence >= MaximumRecords) throw new InvalidDataException("Journal has too many records.");
        var serializer = new JavaScriptSerializer { MaxJsonLength = MaximumLineBytes, RecursionLimit = 8 };
        var data = serializer.DeserializeObject(line) as Dictionary<string, object>;
        if (data == null || data.Count != 7 || Number(data, "schema") != 1)
            throw new InvalidDataException("Unknown journal schema.");
        string requestId = Text(data, "requestId");
        ValidateRequestId(requestId);
        long delta = Number(data, "delta");
        var receipt = new CounterReceipt { RequestId = requestId, Delta = checked((int)delta),
            Sequence = Number(data, "sequence"), Value = Number(data, "value") };
        string previous = Text(data, "previousChecksum");
        string checksum = Text(data, "checksum");
        if (delta < 1 || delta > 1000 || receipt.Sequence != sequence + 1 ||
            receipt.Value != value + delta || previous != previousChecksum || receipts.ContainsKey(requestId))
            throw new InvalidDataException("Journal sequence, value, request ID or checksum chain is invalid.");
        string payload = Payload(receipt, previous);
        if (!String.Equals(checksum, Checksum(payload), StringComparison.Ordinal) ||
            !String.Equals(line, Record(payload, checksum), StringComparison.Ordinal))
            throw new InvalidDataException("Journal record checksum or canonical encoding is invalid.");
        receipts.Add(requestId, receipt);
        sequence = receipt.Sequence;
        value = receipt.Value;
        previousChecksum = checksum;
    }

    static string Text(Dictionary<string, object> data, string name)
    {
        object result;
        if (!data.TryGetValue(name, out result) || !(result is string))
            throw new InvalidDataException("Expected a string journal field.");
        return (string)result;
    }
    static long Number(Dictionary<string, object> data, string name)
    {
        object result;
        if (!data.TryGetValue(name, out result) || (!(result is int) && !(result is long)))
            throw new InvalidDataException("Expected an integer journal field.");
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }
    static string Payload(CounterReceipt receipt, string previous)
    {
        return "{\"schema\":1,\"requestId\":\"" + receipt.RequestId + "\",\"delta\":" + receipt.Delta.ToString(CultureInfo.InvariantCulture) +
            ",\"sequence\":" + receipt.Sequence.ToString(CultureInfo.InvariantCulture) + ",\"value\":" + receipt.Value.ToString(CultureInfo.InvariantCulture) +
            ",\"previousChecksum\":\"" + previous + "\"}";
    }
    static string Record(string payload, string checksum)
    {
        return payload.Substring(0, payload.Length - 1) + ",\"checksum\":\"" + checksum + "\"}";
    }
    static string Checksum(string text)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Utf8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            stream.Dispose();
        }
    }
}
