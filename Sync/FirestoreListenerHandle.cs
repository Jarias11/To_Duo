// Services/Sync/FirestoreListenerHandle.cs  (same name, REST-friendly)
// Minimal IDisposable used by repositories to stop polling loops.
using System;

namespace TaskMate.Sync {
    /// <summary>
    /// Lightweight handle that calls a provided stop action on Dispose.
    /// Used as a drop-in replacement for the SDK change listener handle.
    /// </summary>
    public sealed class FirestoreListenerHandle : IDisposable {
        private readonly Action? _stop;
        private bool _disposed;

        public FirestoreListenerHandle(Action stop) => _stop = stop;

        public void Dispose() {
            if(_disposed) return;
            _disposed = true;
            try { _stop?.Invoke(); } catch { /* swallow on shutdown */ }
        }
    }
}
