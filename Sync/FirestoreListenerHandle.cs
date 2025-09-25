using System;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.Firestore;

namespace TaskMate.Sync {
    /// <summary>
    /// Wraps FirestoreChangeListener with an IDisposable so callers can dispose safely.
    /// </summary>
    public sealed class FirestoreListenerHandle : IDisposable {
        private readonly FirestoreChangeListener _inner;
        public FirestoreListenerHandle(FirestoreChangeListener inner) => _inner = inner;

        public void Dispose() {
            try {
                var stop = _inner.StopAsync(CancellationToken.None);
                // If we’re on the UI thread, don’t block it—let it finish in the background.
                if(System.Windows.Application.Current?.Dispatcher?.CheckAccess() == true) {
                    _ = stop; // fire-and-forget
                }
                else {
                    stop.GetAwaiter().GetResult();
                }
            }
            catch {
                // swallow on shutdown; nothing else to do
            }
        }
    }
}