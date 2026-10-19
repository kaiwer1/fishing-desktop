using System;
using System.Threading;

// Shared by new candidate builds in the current Windows session.
// Does not coordinate Lively, old binaries, game saves or remote sessions.
sealed class HostInstanceGuard : IDisposable
{
    readonly Mutex mutex;
    bool disposed;
    public bool IsOwner { get; private set; }
    public bool WasAbandoned { get; private set; }

    public HostInstanceGuard()
    {
        mutex = new Mutex(false, @"Local\FishingDesktop.InputProbe.v1");
        try { IsOwner = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsOwner = true; WasAbandoned = true; }
    }
    public void Dispose()
    {
        if(disposed)return;
        disposed=true;
        try { if(IsOwner)mutex.ReleaseMutex(); }
        finally { IsOwner=false; mutex.Dispose(); }
    }
}
