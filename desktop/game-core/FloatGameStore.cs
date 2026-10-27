using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FishingDesktop.GameCore
{
    public sealed class FloatSaveUncertainException : IOException
    {
        internal FloatSaveUncertainException(Exception cause)
            : base("The save may have been written. Close and verify the original journal before issuing another command.", cause) { }
    }

    public sealed class FloatCommitResult
    {
        public readonly string Code;
        public readonly FloatGameState State;
        public readonly FloatReceipt Receipt;
        public readonly bool Committed, Written, IsReplay;
        public bool OperationSucceeded { get { return Committed && Receipt != null && Receipt.Outcome != FloatOutcome.CapacityRejected; } }

        internal FloatCommitResult(string code, FloatGameState state, FloatReceipt receipt, bool committed, bool written, bool replay)
        {
            Code = code; State = state; Receipt = receipt;
            Committed = committed; Written = written; IsReplay = replay;
        }
    }

    // Internal fault injection for real private-file tests; never a page API.
    internal enum FloatSaveFaultPoint { BeforeWrite, AfterPrefix, AfterFrame, AfterFlush }

    // Bounded, append-only schema-1 candidate. There is no import, automatic
    // repair, tail trimming, compaction, checkpoint fallback or reset-to-new.
    public sealed class FloatGameStore : IDisposable
    {
        public const long MaximumFileBytes = 16 * 1024 * 1024;
        public const int MaximumTransactions = 10000;
        const int FrameOverhead = 4 + 8 + 32 + 32;
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("FDGJ0001");
        readonly object _gate = new object();
        readonly FileStream _file;
        readonly Action<FloatSaveFaultPoint> _faultHook;
        FloatGameState _state;
        byte[] _lastHash = new byte[32];
        long _length;
        bool _faulted, _disposed, _executing;

        FloatGameStore(FileStream file, Action<FloatSaveFaultPoint> hook)
        {
            _file = file; _faultHook = hook;
        }

        public bool IsFaulted { get { lock (_gate) return _faulted; } }
        public FloatGameState State
        {
            get { lock (_gate) { CheckAvailable(); return _state; } }
        }

        public static FloatGameStore CreateNew(string path, FloatGameState genesis)
        {
            return CreateNew(path, genesis, null);
        }

        internal static FloatGameStore CreateNew(string path, FloatGameState genesis, Action<FloatSaveFaultPoint> hook)
        {
            // Validate/encode before creating anything. Existing paths never
            // get overwritten even when they contain an invalid save.
            byte[] payload = FloatJournalCodec.EncodeGenesis(genesis);
            var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.Read, 4096, FileOptions.WriteThrough);
            var store = new FloatGameStore(file, hook);
            try
            {
                try { file.Write(Magic, 0, Magic.Length); }
                catch (Exception error) { store._faulted = true; throw new FloatSaveUncertainException(error); }
                store._length = Magic.Length;
                store.AppendFrame(0, payload);
                store._state = genesis;
                return store;
            }
            catch { file.Dispose(); throw; }
        }

        public static FloatGameStore OpenExisting(string path)
        {
            return OpenExisting(path, null);
        }

        internal static FloatGameStore OpenExisting(string path, Action<FloatSaveFaultPoint> hook)
        {
            // Open is deliberately separate from CreateNew. Missing, old or
            // damaged files must never silently receive starter resources.
            var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite,
                FileShare.Read, 4096, FileOptions.WriteThrough);
            var store = new FloatGameStore(file, hook);
            try { store.ReadJournal(); return store; }
            catch { file.Dispose(); throw; }
        }

        void CheckAvailable()
        {
            if (_disposed) throw new ObjectDisposedException("FloatGameStore");
            if (_faulted) throw new InvalidOperationException("Save outcome is uncertain; this writer is disabled.");
        }

        public FloatCommitResult Execute(FloatCommand command)
        {
            lock (_gate)
            {
                CheckAvailable();
                if (_executing) throw new InvalidOperationException("Reentrant commands are not allowed.");
                _executing = true;
                try
                {
                    var result = FloatGameReducer.Reduce(_state, command, _state.Rules);
                    if (!result.HasCandidate)
                        return new FloatCommitResult(result.Code, _state, result.Receipt, result.IsReplay, false, result.IsReplay);

                    byte[] payload = FloatJournalCodec.EncodeCommand(command);
                    long frameSize = FrameOverhead + (long)payload.Length;
                    // No write has started: this is a definite rejection.
                    // Old receipts remain queryable at the capacity boundary.
                    if (_state.Sequence >= MaximumTransactions || _length > MaximumFileBytes - frameSize)
                        return new FloatCommitResult("save-capacity", _state, null, false, false, false);

                    AppendFrame(result.Candidate.Sequence, payload);
                    // Publish only after the full frame and Flush(true) succeed.
                    // CapacityRejected also reaches this point and is saved.
                    _state = result.Candidate;
                    return new FloatCommitResult(result.Code, _state, result.Receipt, true, true, false);
                }
                finally { _executing = false; }
            }
        }

        static byte[] FrameBody(long sequence, byte[] previousHash, byte[] payload)
        {
            using (var buffer = new MemoryStream())
            {
                using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true))
                {
                    writer.Write(payload.Length); writer.Write(sequence);
                    writer.Write(previousHash); writer.Write(payload);
                }
                return buffer.ToArray();
            }
        }

        static byte[] Digest(byte[] body)
        {
            using (var sha = SHA256.Create()) return sha.ComputeHash(body);
        }

        static bool Equal(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            int difference = 0;
            for (int i = 0; i < left.Length; i++) difference |= left[i] ^ right[i];
            return difference == 0;
        }

        void Fault(FloatSaveFaultPoint point)
        {
            if (_faultHook != null) _faultHook(point);
        }

        void AppendFrame(long sequence, byte[] payload)
        {
            byte[] body = FrameBody(sequence, _lastHash, payload);
            byte[] hash = Digest(body);
            // Test-only pre-write failure is known not to have written a frame.
            Fault(FloatSaveFaultPoint.BeforeWrite);
            try
            {
                if (_file.Length != _length || _file.Position != _length)
                    throw new IOException("The journal length or cursor changed unexpectedly.");
                _file.Write(body, 0, 12);
                Fault(FloatSaveFaultPoint.AfterPrefix);
                _file.Write(body, 12, body.Length - 12);
                _file.Write(hash, 0, hash.Length);
                Fault(FloatSaveFaultPoint.AfterFrame);
                _file.Flush(true);
                Fault(FloatSaveFaultPoint.AfterFlush);
                _length += body.Length + hash.Length;
                _lastHash = hash;
            }
            catch (Exception error)
            {
                _faulted = true;
                throw new FloatSaveUncertainException(error);
            }
        }

        static byte[] Exact(BinaryReader reader, int count)
        {
            byte[] result = reader.ReadBytes(count);
            if (result.Length != count) throw new InvalidDataException("Truncated save frame.");
            return result;
        }

        void ReadJournal()
        {
            long size = _file.Length;
            if (size > MaximumFileBytes || size < Magic.Length + FrameOverhead + 1)
                throw new InvalidDataException("Save length is invalid or exceeds the schema-1 limit.");
            using (var reader = new BinaryReader(_file, Encoding.UTF8, true))
            {
                if (!Equal(Exact(reader, Magic.Length), Magic))
                    throw new InvalidDataException("Unsupported save header or version.");
                long expectedSequence = 0;
                while (_file.Position < size)
                {
                    if (size - _file.Position < FrameOverhead + 1 || expectedSequence > MaximumTransactions)
                        throw new InvalidDataException("Truncated or excessive save frames.");
                    int length = reader.ReadInt32();
                    long sequence = reader.ReadInt64();
                    if (length <= 0 || length > FloatJournalCodec.MaximumPayloadBytes ||
                        size - _file.Position < 64L + length || sequence != expectedSequence)
                        throw new InvalidDataException("Invalid save frame length or sequence.");
                    byte[] previous = Exact(reader, 32);
                    byte[] payload = Exact(reader, length);
                    byte[] hash = Exact(reader, 32);
                    if (!Equal(previous, _lastHash) || !Equal(hash, Digest(FrameBody(sequence, previous, payload))))
                        throw new InvalidDataException("Save frame integrity check failed.");

                    if (sequence == 0) _state = FloatJournalCodec.DecodeGenesis(payload);
                    else
                    {
                        var command = FloatJournalCodec.DecodeCommand(payload);
                        var reduction = FloatGameReducer.Reduce(_state, command, _state.Rules);
                        // Every stored command must add exactly one new state.
                        // An extra duplicate/rejected command is not a commit.
                        if (!reduction.HasCandidate || reduction.IsReplay || reduction.Candidate.Sequence != sequence)
                            throw new InvalidDataException("Save command is inconsistent with its preceding state.");
                        _state = reduction.Candidate;
                    }
                    _lastHash = hash;
                    expectedSequence++;
                }
            }
            if (_state == null || _file.Position != size)
                throw new InvalidDataException("The save has no complete initial state.");
            _length = size;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                if (_executing) throw new InvalidOperationException("Cannot dispose during a transaction.");
                _disposed = true;
                _file.Dispose();
            }
        }
    }
}
