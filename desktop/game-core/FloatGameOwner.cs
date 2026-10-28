using System;
using System.Threading;

namespace FishingDesktop.GameCore
{
    public enum FloatViewRole { Observer, Interaction }

    // Reference identity is the authority. These contain no URL, page-supplied
    // role, process identity or serializable credentials.
    public sealed class FloatViewTicket { internal FloatViewTicket() { } }
    public sealed class FloatDocumentTicket { internal FloatDocumentTicket() { } }
    public sealed class FloatControlLease { internal FloatControlLease() { } }

    public sealed class FloatOwnerSnapshot
    {
        public readonly FloatGameState State;
        public readonly bool Faulted, HostPaused, MenuOpen, PendingResult, CanSimulate, ShutdownConfirmed;
        internal FloatOwnerSnapshot(FloatGameState state, bool faulted, bool paused, bool menu, bool shutdown)
        {
            State = state; Faulted = faulted; HostPaused = paused; MenuOpen = menu; ShutdownConfirmed = shutdown;
            PendingResult = state != null && state.Session != null && state.Session.CapacityBlocked;
            // Eligibility at this layer only. No clock, physics, offline catch,
            // render visibility or new input authority is implied by true.
            CanSimulate = !faulted && !paused && !menu && !PendingResult && !shutdown;
        }
    }

    // The host owns this instance and its file for longer than any view. Host
    // attachment/grant/setter methods are not a WebView message protocol.
    public sealed class FloatGameOwner : IDisposable
    {
        readonly FloatGameStore _store;
        readonly int _thread = Thread.CurrentThread.ManagedThreadId;
        FloatViewTicket _observer, _interaction;
        FloatDocumentTicket _observerDocument, _interactionDocument;
        FloatControlLease _lease;
        bool _hostPaused = true, _menu, _faulted, _disposed, _busy, _shutdown;

        FloatGameOwner(FloatGameStore store) { _store = store; }

        public static FloatGameOwner CreateNew(string path, FloatGameState genesis)
        { return CreateNew(path, genesis, null); }
        internal static FloatGameOwner CreateNew(string path, FloatGameState genesis, Action<FloatSaveFaultPoint> hook)
        { return new FloatGameOwner(FloatGameStore.CreateNew(path, genesis, hook)); }

        public static FloatGameOwner OpenExisting(string path)
        { return OpenExisting(path, null); }
        internal static FloatGameOwner OpenExisting(string path, Action<FloatSaveFaultPoint> hook)
        { return OpenExisting(path,hook,0); }
        // A host with a newer feature set may require a schema before startup
        // settlement. The already-held writer closes the inspection/open gap.
        internal static FloatGameOwner OpenExisting(string path,Action<FloatSaveFaultPoint> hook,int requiredSchemaVersion)
        {
            var owner = new FloatGameOwner(FloatGameStore.OpenExisting(path, hook));
            try
            {
                if(requiredSchemaVersion!=0&&owner._store.State.SchemaVersion!=requiredSchemaVersion)
                    throw new InvalidOperationException("An explicitly upgraded profile candidate is required before opening this game.");
                var recovery = owner.Run(() => FloatGameLifecycle.Startup(owner._store));
                if (!recovery.Confirmed) throw new InvalidOperationException("Startup settlement was not confirmed: " + recovery.Code);
                return owner;
            }
            catch { owner._store.Dispose(); owner._disposed = true; throw; }
        }

        void Entry()
        {
            if (Thread.CurrentThread.ManagedThreadId != _thread) throw new InvalidOperationException("Use the game owner's host thread.");
            if (_disposed) throw new ObjectDisposedException("FloatGameOwner");
            if (_busy) throw new InvalidOperationException("Reentrant owner calls are not allowed.");
        }
        void Operational()
        {
            if (_faulted) throw new InvalidOperationException("The save outcome is uncertain; close and verify the original journal.");
            if (_shutdown) throw new InvalidOperationException("Owner shutdown was already confirmed.");
        }
        T Run<T>(Func<T> operation)
        {
            _busy = true;
            try { return operation(); }
            catch
            {
                if (_store.IsFaulted) { _faulted = true; _hostPaused = true; _lease = null; }
                throw;
            }
            finally { _busy = false; }
        }
        FloatOwnerSnapshot Current()
        { return new FloatOwnerSnapshot(_faulted ? null : _store.State, _faulted, _hostPaused, _menu, _shutdown); }
        public FloatOwnerSnapshot Snapshot { get { Entry(); return Current(); } }

        bool DocumentMatches(FloatViewTicket view, FloatDocumentTicket document)
        {
            return view != null && document != null &&
                ((Object.ReferenceEquals(view, _observer) && Object.ReferenceEquals(document, _observerDocument)) ||
                 (Object.ReferenceEquals(view, _interaction) && Object.ReferenceEquals(document, _interactionDocument)));
        }
        bool Authorized(FloatViewTicket view, FloatDocumentTicket document, FloatControlLease lease)
        {
            return !_hostPaused && !_faulted && !_shutdown && lease != null && Object.ReferenceEquals(lease, _lease) &&
                Object.ReferenceEquals(view, _interaction) && DocumentMatches(view, document);
        }
        internal bool CanControl(FloatViewTicket view, FloatDocumentTicket document, FloatControlLease lease)
        { Entry(); return Authorized(view, document, lease); }
        static FloatCommitResult Refused(string code)
        {
            // A rejected authority never receives an old transaction or the
            // current profile through the command reply.
            return new FloatCommitResult(code, null, null, false, false, false);
        }
        static bool UserType(FloatCommand command)
        { return command is CastFloat || command is CastFloatWithEquipment || command is ReplaceFloatSlot || command is MoveFloatLine || command is RecallFloat || command is ReleaseCurrentFloatFish || command is SellFloatFish || command is BuyFloatSupply || command is EquipFloatNet || command is UseFloatFood; }
        bool HasRecordedRequest(FloatCommand command)
        {
            if (command == null) return false;
            foreach (var row in _store.State.Receipts) if (row.RequestId == command.RequestId) return true;
            return false;
        }

        public FloatViewTicket OpenView(FloatViewRole role)
        {
            Entry(); Operational();
            if (role != FloatViewRole.Observer && role != FloatViewRole.Interaction) throw new ArgumentOutOfRangeException("role");
            var ticket = new FloatViewTicket();
            if (role == FloatViewRole.Observer) { _observer = ticket; _observerDocument = null; }
            else { _interaction = ticket; _interactionDocument = null; _lease = null; }
            return ticket;
        }
        public FloatDocumentTicket AttachDocument(FloatViewTicket view)
        {
            Entry(); Operational();
            var document = new FloatDocumentTicket();
            if (view != null && Object.ReferenceEquals(view, _observer)) _observerDocument = document;
            else if (view != null && Object.ReferenceEquals(view, _interaction)) { _interactionDocument = document; _lease = null; }
            else throw new ArgumentException("The view ticket is not current.", "view");
            return document;
        }
        public FloatOwnerSnapshot ReadView(FloatViewTicket view, FloatDocumentTicket document)
        {
            Entry();
            if (!DocumentMatches(view, document)) throw new ArgumentException("The view document is not current.");
            return Current();
        }
        public FloatControlLease GrantControl(FloatViewTicket view, FloatDocumentTicket document)
        {
            Entry(); Operational();
            if (_hostPaused || !Object.ReferenceEquals(view, _interaction) || !DocumentMatches(view, document))
                throw new InvalidOperationException("Only the current attached interaction document may receive a host grant.");
            _lease = new FloatControlLease();
            return _lease;
        }
        public bool RevokeControl(FloatControlLease lease)
        {
            Entry();
            if (lease == null || !Object.ReferenceEquals(lease, _lease)) return false;
            _lease = null; return true;
        }
        public bool CloseView(FloatViewTicket view)
        {
            Entry();
            if (view != null && Object.ReferenceEquals(view, _observer)) { _observer = null; _observerDocument = null; return true; }
            if (view != null && Object.ReferenceEquals(view, _interaction)) { _interaction = null; _interactionDocument = null; _lease = null; return true; }
            return false;
        }
        public void SetHostPaused(bool paused)
        {
            Entry(); Operational(); _hostPaused = paused;
            if (paused) _lease = null;
        }
        public void SetMenuOpen(bool open) { Entry(); Operational(); _menu = open; }

        public FloatCommitResult ExecuteUser(FloatViewTicket view, FloatDocumentTicket document, FloatControlLease lease, FloatCommand command)
        {
            Entry();
            // Authority is checked before inspecting any old request receipt.
            if (!Authorized(view, document, lease)) return Refused("control-required");
            if (!UserType(command)) return Refused("user-command-not-allowed");
            if ((command is CastFloat || command is CastFloatWithEquipment) && (_menu || Current().PendingResult) && !HasRecordedRequest(command)) return Refused("domain-paused");
            return Run(() => _store.Execute(command));
        }
        internal FloatCommitResult ExecuteHost(FloatCommand command)
        {
            Entry(); Operational();
            if (!(command is HostFloatFish) && !(command is RecoverFloatSession) && !(command is HostFloatUnhooked)) return Refused("host-command-not-allowed");
            if (!Current().CanSimulate && !HasRecordedRequest(command)) return Refused("simulation-paused");
            return Run(() => _store.Execute(command));
        }
        public FloatLifecycleResult Shutdown()
        {
            Entry(); Operational(); _hostPaused = true; _lease = null;
            // Lifecycle settlement is deliberately outside simulation gating.
            var result = Run(() => FloatGameLifecycle.OwnerShutdown(_store));
            if (result.Confirmed) _shutdown = true;
            return result;
        }
        public void Dispose()
        {
            if (Thread.CurrentThread.ManagedThreadId != _thread) throw new InvalidOperationException("Use the game owner's host thread.");
            if (_disposed) return;
            if (_busy) throw new InvalidOperationException("Cannot dispose during an owner transaction.");
            _lease = null; _observer = null; _interaction = null; _observerDocument = null; _interactionDocument = null;
            // Abrupt/discard disposal never secretly settles a paid session.
            _disposed = true; _store.Dispose();
        }
    }
}
