using Google.Cloud.Firestore;
using System.Threading.Tasks;

namespace TaskMate.Sync
{
    public class FirestoreClient
    {
        private static FirestoreDb? _db;

        public static async Task<FirestoreDb> GetDbAsync()
        {
            if (_db != null) return _db;
            string projectId = "taskmate-4777f";
            _db = await FirestoreDb.CreateAsync(projectId);
            return _db;
        }
        public static async Task WriteHealthCheckAsync()
        {
            var db = await GetDbAsync();
            var doc = db.Collection("health").Document("desktop");
            await doc.SetAsync(new { ok = true, updatedAt = Timestamp.GetCurrentTimestamp() });
        }
        public static async Task<bool> ReadHealthCheckAsync()
        {
            var db = await GetDbAsync();
            var snap = await db.Collection("health").Document("desktop").GetSnapshotAsync();
            return snap.Exists && snap.TryGetValue<bool>("ok", out var ok) && ok;
        }
    }
}