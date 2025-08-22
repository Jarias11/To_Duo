using Google.Cloud.Firestore;
using System.Threading.Tasks;

namespace TaskMate.Sync {
    public class FirestoreClient {
        private static FirestoreDb? _db;
        private const string ProjectId = "taskmate-4777f";



        // Sync getter to avoid 'await' in non-async methods
        public static FirestoreDb GetDb() {
            return _db ??= FirestoreDb.Create(ProjectId);
        }

        // keep your existing async if you want, but the repo will use GetDb()
        public static Task<FirestoreDb> GetDbAsync()
            => Task.FromResult(GetDb());
    

        public static async Task WriteHealthCheckAsync() {
            var db = await GetDbAsync();
            var doc = db.Collection("health").Document("desktop");
            await doc.SetAsync(new { ok = true, updatedAt = Timestamp.GetCurrentTimestamp() });
        }
        public static async Task<bool> ReadHealthCheckAsync() {
            var db = await GetDbAsync();
            var snap = await db.Collection("health").Document("desktop").GetSnapshotAsync();
            return snap.Exists && snap.TryGetValue<bool>("ok", out var ok) && ok;
        }
    }
}