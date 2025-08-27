using Google.Cloud.Firestore;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TaskMate.Models;

namespace TaskMate.Sync {
    public class FirestoreClient {
        private static FirestoreDb? _db;
        private static readonly object _dbLock = new();
        private const string ProjectId = "taskmate-4777f";
        private static volatile bool _initialized;
        private static readonly SemaphoreSlim _initGate = new(1, 1);

        public static string CurrentUserId { get; private set; } = string.Empty;
        public static string PartnerId { get; private set; } = string.Empty;
        public static string GroupId { get; private set; } = string.Empty;




        // Sync getter to avoid 'await' in non-async methods
        public static FirestoreDb GetDb() {
            if(_db != null) return _db;
            lock(_dbLock) {
                _db ??= FirestoreDb.Create(ProjectId);
            }

            return _db;
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

        public static async Task InitializeAsync() {
            if(_initialized) return;
            await _initGate.WaitAsync().ConfigureAwait(false);
            try {
                if(_initialized) return;
                _ = GetDb();

                var settings = UserSettings.Load();

                if(string.IsNullOrWhiteSpace(settings.UserId)) {
                    settings.UserId = TaskMate.Models.SnowflakeId.New();
                    settings.GroupId ??= string.Empty;
                    settings.PartnerId ??= string.Empty; // just to satisfy nullable
                    UserSettings.Save(settings);
                }
                CurrentUserId = settings.UserId ?? string.Empty;
                PartnerId = settings.PartnerId ?? string.Empty;
                GroupId = settings.GroupId ?? string.Empty;

                _initialized = true;
            }
            finally {
                _initGate.Release();
            }

        }
        /// <summary>
        /// If the user connects/disconnects a partner or group at runtime,
        /// call this to refresh the in-memory ids from UserSettings.
        /// </summary>
        public static void ReloadSettings() {
            var settings = UserSettings.Load(); // adjust namespace if needed
            // Do not mutate CurrentUserId unless the stored value is missing (shouldn't happen)
            if(string.IsNullOrWhiteSpace(CurrentUserId) && !string.IsNullOrWhiteSpace(settings.UserId))
                CurrentUserId = settings.UserId;

            PartnerId = settings.PartnerId ?? string.Empty;
            GroupId = settings.GroupId ?? string.Empty;
        }

        /// <summary>
        /// Optional helper if you want to set partner/group and persist in one call.
        /// </summary>
        public static void SavePartnerAndGroup(string? partnerId, string? groupId) {
            var settings = UserSettings.Load(); // adjust namespace if needed
            settings.PartnerId = partnerId ?? string.Empty;
            settings.GroupId = groupId ?? string.Empty;
            UserSettings.Save(settings);

            PartnerId = settings.PartnerId;
            GroupId = settings.GroupId;
        }
    }
}