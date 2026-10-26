using System;

// UI-thread-only lifecycle gate. No simulation or save state lives here.
sealed class InputHostLifecycle
{
    public int Generation { get; private set; }
    public int Revision { get; private set; }
    public int CancellationEpoch { get; private set; }
    public string DocumentId { get; private set; }
    public bool Ready { get { return DocumentId != null; } }
    public bool Closed { get; private set; }
    public int Invalidate()
    {
        DocumentId = null;
        CancellationEpoch = 0;
        return ++Generation;
    }
    public bool Bind(int generation, string documentId)
    {
        Guid parsed;
        if (Closed || generation != Generation || !Guid.TryParseExact(documentId, "D", out parsed)) return false;
        if (Ready) return DocumentId == documentId; // repeated completion must not reset cancellation
        DocumentId = documentId;
        CancellationEpoch = 0;
        return true;
    }
    public bool Observe(string documentId, int cancellationEpoch)
    {
        if (Closed || !Ready || DocumentId != documentId || cancellationEpoch < CancellationEpoch) return false;
        CancellationEpoch = cancellationEpoch;
        return true;
    }
    public object Command(bool enabled, string reason, bool foreground,string activationRequestId="")
    {
        if (Closed || !Ready || (enabled && !foreground)) return null;
        return new { kind = "mode", documentId = DocumentId, revision = ++Revision,
            cancellationEpoch = CancellationEpoch, enabled = enabled, reason = reason,activationRequestId=activationRequestId };
    }
    public void Close() { Closed = true; Invalidate(); }
}
