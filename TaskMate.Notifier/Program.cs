// TaskMate.Notifier/Program.cs
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Toolkit.Uwp.Notifications;
using TaskMate.Services;
using TaskMate.Services.Notifications;
using TaskMate.Sync;
using TaskMate.Models;


internal static class Program {
    private sealed class NotifierState {
        public DateTime LastSeenUtc { get; set; }
    }

    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TaskMate", "notifier-state.json");

    private static async Task<int> Main(string[] args) {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);

            // 1) Register toast compat (AUMID/COM) so we can fire toasts
            var notify = new NotificationService();
            notify.EnsureRegistered();

            // 2) Init Firestore and identify ourselves
            await FirestoreClient.InitializeAsync();
            var myUserId = FirestoreClient.CurrentUserId;
            if(string.IsNullOrWhiteSpace(myUserId)) return 0;

            // 3) Load last-seen marker
            var state = LoadState();

            // 4) Take a one-shot snapshot of incoming partner requests
            var svc = new PartnerRequestService();
            var tcs = new TaskCompletionSource<System.Collections.Generic.IList<TaskMate.Models.PartnerRequest>>();

            IDisposable? sub = null;
            sub = svc.ListenIncoming(myUserId, list => {
                try { tcs.TrySetResult(list); }
                catch { }
                finally { sub?.Dispose(); }
            });

            var list = await Task.WhenAny(tcs.Task, Task.Delay(4000)) == tcs.Task ? tcs.Task.Result
                      : Array.Empty<TaskMate.Models.PartnerRequest>();

            // 5) Filter > last seen & still pending
            var fresh = list
                .Where(r => string.Equals(r.Status, "pending", StringComparison.OrdinalIgnoreCase))
                .Where(r => (r.UpdatedAt ?? DateTime.MinValue) > state.LastSeenUtc)
                .ToList();

            // 6) Toast each new one
            foreach(var r in fresh) {
                var caption = "New partner task";
                var fromName = string.IsNullOrWhiteSpace(r.FromDisplayName) ? r.FromUserId : r.FromDisplayName;

                // r.Id is a string; derive a stable Guid for toast actions
                notify.ShowPartnerRequestToast(r.Id, fromName ?? r.FromUserId, caption);
            }

            // 7) Advance last-seen (to newest UpdatedAt we saw)
            var newest = list.Select(r => r.UpdatedAt ?? DateTime.MinValue).DefaultIfEmpty(state.LastSeenUtc).Max();
            if(newest > state.LastSeenUtc) {
                state.LastSeenUtc = newest;
                SaveState(state);
            }

            return 0;
        }
        catch {
            return 1;
        }
    }

    private static NotifierState LoadState() {
        try {
            if(File.Exists(StatePath))
                return JsonSerializer.Deserialize<NotifierState>(File.ReadAllText(StatePath)) ?? new NotifierState();
        }
        catch { }
        return new NotifierState { LastSeenUtc = DateTime.MinValue };
    }
    private static void SaveState(NotifierState s) {
        try {
            File.WriteAllText(StatePath, JsonSerializer.Serialize(s));
        }
        catch { }
    }

    // RFC4122 namespace-based guid helper (if you need to derive a stable Guid from string ids)
    private static class GuidUtility {
        public static readonly Guid UrlNamespace = new Guid("6ba7b811-9dad-11d1-80b4-00c04fd430c8");
        public static Guid Create(Guid namespaceId, string name) {
            // SHA1 name-based GUID
            var ns = namespaceId.ToByteArray();
            SwapByteOrder(ns);
            var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);

            var data = new byte[ns.Length + nameBytes.Length];
            Buffer.BlockCopy(ns, 0, data, 0, ns.Length);
            Buffer.BlockCopy(nameBytes, 0, data, ns.Length, nameBytes.Length);

            using var sha1 = System.Security.Cryptography.SHA1.Create();
            var hash = sha1.ComputeHash(data);
            var newGuid = new byte[16];
            Array.Copy(hash, 0, newGuid, 0, 16);

            newGuid[6] = (byte)((newGuid[6] & 0x0F) | (5 << 4));
            newGuid[8] = (byte)((newGuid[8] & 0x3F) | 0x80);

            SwapByteOrder(newGuid);
            return new Guid(newGuid);
        }
        private static void SwapByteOrder(byte[] guid) {
            void Swap(int a, int b) { var t = guid[a]; guid[a] = guid[b]; guid[b] = t; }
            Swap(0, 3); Swap(1, 2); Swap(4, 5); Swap(6, 7);
        }
    }
}