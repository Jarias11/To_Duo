using System;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.Firestore;

namespace TaskMate.Sync
{
    /// <summary>
    /// Wraps FirestoreChangeListener with an IDisposable so callers can dispose safely.
    /// </summary>
    public sealed class FirestoreListenerHandle : IDisposable
    {
        private readonly FirestoreChangeListener _inner;
        public FirestoreListenerHandle(FirestoreChangeListener inner) => _inner = inner;

        // Dispose synchronously by stopping the async listener
        public void Dispose() => _inner.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
    }
}