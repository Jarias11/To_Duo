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

                await EnsureUserDocExistsAsync().ConfigureAwait(false);
                if(!string.IsNullOrWhiteSpace(GroupId))
                    await EnsureGroupDocExistsAsync().ConfigureAwait(false);

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
            if(!string.IsNullOrWhiteSpace(GroupId))
                _ = EnsureGroupDocExistsAsync();
        }

        // --- NEW: create/ensure a users/{uid} doc exists ---
        public static async Task EnsureUserDocExistsAsync() {
            var db = GetDb();
            var uid = CurrentUserId;
            if(string.IsNullOrWhiteSpace(uid)) return;

            var userRef = db.Collection("users").Document(uid);
            var snap = await userRef.GetSnapshotAsync().ConfigureAwait(false);

            if(!snap.Exists) {
                await userRef.SetAsync(new {
                    displayName = UserSettings.Load().DisplayName ?? "",
                    createdAt = FieldValue.ServerTimestamp,
                    updatedAt = FieldValue.ServerTimestamp
                }).ConfigureAwait(false);
            }
            else {
                // Don’t overwrite createdAt; just update mutable fields
                await userRef.SetAsync(new {
                    displayName = UserSettings.Load().DisplayName ?? "",
                    updatedAt = FieldValue.ServerTimestamp
                }, SetOptions.MergeAll).ConfigureAwait(false);
            }
        }

        // --- NEW: create/ensure a groups/{groupId} doc exists ---
        public static async Task EnsureGroupDocExistsAsync() {
            var db = GetDb();
            var gid = GroupId;
            if(string.IsNullOrWhiteSpace(gid)) return;

            // (a, b) are the two members; order doesn’t matter, GroupId is already sorted
            var a = CurrentUserId;
            var b = PartnerId;

            var grpRef = db.Collection("groups").Document(gid);
            var snap = await grpRef.GetSnapshotAsync().ConfigureAwait(false);

            if(!snap.Exists) {
                await grpRef.SetAsync(new {
                    a,
                    b,
                    createdAt = FieldValue.ServerTimestamp,
                    updatedAt = FieldValue.ServerTimestamp
                }).ConfigureAwait(false);
            }
            else {
                await grpRef.SetAsync(new {
                    a,
                    b,
                    updatedAt = FieldValue.ServerTimestamp
                }, SetOptions.MergeAll).ConfigureAwait(false);
            }
        }
    }
}