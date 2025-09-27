// ActivityLogService.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Google.Cloud.Firestore;
using TaskMate.Models;
using TaskMate.Sync;
using System.IO;
using System.Text.Json;


namespace TaskMate.Services {
	public sealed class ActivityLogService : IActivityLogService {
		private readonly Dispatcher _ui;
		private readonly ISettingsService _settings;
		private IDisposable? _mineHandle;
		private IDisposable? _partnerHandle;
		private const string LocalFeedPath = "activity_feed.json";

		public ObservableCollection<ActivityEntry> Feed { get; } = new();

		public ActivityLogService(Dispatcher ui, ISettingsService settings) {
			_ui = ui ?? throw new ArgumentNullException(nameof(ui));
			_settings = settings ?? throw new ArgumentNullException(nameof(settings));
			LoadLocal();
		}

		// Collection: users/{userId}/activity
		private static CollectionReference Col(FirestoreDb db, string userId) =>
			db.Collection("users").Document(userId).Collection("activity");

		// ---- Public API ------------------------------------------------------

		public async Task LogAsync(ActivityEntry e, string myUserId) {
			if(e is null || string.IsNullOrWhiteSpace(myUserId)) return;

			// Normalize to UTC for storage
			var utc = DateTime.SpecifyKind(e.Timestamp, DateTimeKind.Utc);

			var db = FirestoreClient.GetDb();
			var doc = Col(db, myUserId).Document(); // auto-id

			// IMPORTANT: write *display name* for Actor (fallback to "Me")
			var myActor = _settings.DisplayName ?? "Me";

			var payload = new Dictionary<string, object?> {
				["Timestamp"] = Timestamp.FromDateTime(utc),
				["Actor"] = myActor,
				["Kind"] = e.Kind ?? "",
				["Message"] = e.Message ?? "",
				["Reactions"] = new Dictionary<string, object?>()
			};

			await doc.SetAsync(payload, SetOptions.MergeAll);
			try {
				var localTs = utc.ToLocalTime(); // MapFrom uses local time for UI
				var echo = new ActivityEntry {
					Id = doc.Id,                     // NEW
					OwnerUserId = myUserId,
					Timestamp = localTs,
					Actor = myActor,
					Kind = e.Kind ?? "",
					Message = e.Message ?? "",
					IsMine = true,
					Reactions = new Dictionary<string, string>()
				};
				_ui.BeginInvoke(() => UpsertIntoFeed(new[] { echo }));
			}
			catch { /* non-fatal */ }
		}

		public Task StartMineAsync(string myUserId) {
			_mineHandle?.Dispose();
			if(string.IsNullOrWhiteSpace(myUserId)) return Task.CompletedTask;

			var db = FirestoreClient.GetDb();
			var inner = Col(db, myUserId)
				.OrderByDescending("Timestamp")
				.Limit(200)
				.Listen(snap => {
					var myDisplay = _settings.DisplayName ?? "Me";

					var mine = snap.Documents
						.Select(d => MapFrom(d, myUserId))
						// Ensure our rows show with *our* current display name for consistency
						.Select(e => new ActivityEntry {
							Id = e.Id,
							OwnerUserId = myUserId,
							Timestamp = e.Timestamp,
							Kind = e.Kind,
							Message = e.Message,
							Actor = string.IsNullOrWhiteSpace(e.Actor) ? myDisplay : e.Actor,
							IsMine = true,
							Reactions = e.Reactions
						})
						.ToList();

					_ui.BeginInvoke(() => ReplaceMine(mine, myDisplay));
				});

			_mineHandle = new FirestoreListenerHandle(inner);
			return Task.CompletedTask;
		}

		// keep only partner items, then append sorted “mine”
		private void ReplaceMine(IList<ActivityEntry> mine, string myDisplay) {
			// Remove current rows whose Actor matches *my* display name (case-insensitive)
			for(int i = Feed.Count - 1; i >= 0; i--) {
				var a = Feed[i].Actor;
				if(string.Equals(a, myDisplay, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(a, "Me", StringComparison.OrdinalIgnoreCase)) // <— add this
				{
					Feed.RemoveAt(i);
				}
			}

			// Add the refreshed "mine" rows
			foreach(var e in mine.OrderByDescending(x => x.Timestamp))
				Feed.Add(e);

			ResortFeedNewestFirst();
			SaveLocal();
		}

		public Task StartPartnerSinceAsync(string partnerUserId, DateTime sinceUtc) {
			_partnerHandle?.Dispose();
			if(string.IsNullOrWhiteSpace(partnerUserId)) return Task.CompletedTask;

			var db = FirestoreClient.GetDb();

			// Allow 10s skew so we don't miss “just before connect” writes
			var cutoffUtc = DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc).AddSeconds(-10);

			var inner = Col(db, partnerUserId)
				.OrderByDescending("Timestamp")
				.Limit(200)
				.Listen(snap => {
					var entries = snap.Documents
						.Select(d => MapFrom(d, partnerUserId)) // uses the Actor stored by the partner (their display name)
						.Where(e => e.Timestamp.ToUniversalTime() >= cutoffUtc)
						.Select(e => { e.IsMine = false; return e; })
						.ToList();

					_ui.BeginInvoke(() => UpsertIntoFeed(entries));  // merge; do not replace
				});

			_partnerHandle = new FirestoreListenerHandle(inner);
			return Task.CompletedTask;
		}

		public Task StopPartnerAsync() {
			_partnerHandle?.Dispose();
			_partnerHandle = null;
			return Task.CompletedTask;
		}

		public void Dispose() {
			_mineHandle?.Dispose();
			_partnerHandle?.Dispose();
			_mineHandle = _partnerHandle = null;
		}

		// ---- Mapping & merge helpers ----------------------------------------

		private static ActivityEntry MapFrom(DocumentSnapshot d, string ownerUserId) {
			var dict = d.ToDictionary();
			return MapFrom(dict, ownerUserId, d.Id);
		}

		private static ActivityEntry MapFrom(IDictionary<string, object?> dict, string ownerUserId, string id) {
			DateTime ts = DateTime.UtcNow;
			if(dict.TryGetValue("Timestamp", out var v) && v is Timestamp t) {
				// Firestore Timestamp -> DateTime, then normalize to local so UI matches header clock
				ts = DateTime.SpecifyKind(t.ToDateTime(), DateTimeKind.Utc).ToLocalTime();
			}
			static string S(IDictionary<string, object?> d, string k)
				=> d.TryGetValue(k, out var v) ? v?.ToString() ?? "" : "";

			var reactions = new Dictionary<string, string>();
			if(dict.TryGetValue("Reactions", out var r) && r is IDictionary<string, object?> map) {
				foreach(var kv in map) reactions[kv.Key] = kv.Value?.ToString() ?? "";
			}
			return new ActivityEntry {
				Id = id,
				OwnerUserId = ownerUserId,
				Timestamp = ts,
				Actor = S(dict, "Actor"),
				Kind = S(dict, "Kind"),
				Message = S(dict, "Message"),
				Reactions = reactions
			};
		}

		private void UpsertIntoFeed(IEnumerable<ActivityEntry> incoming) {
			foreach(var e in incoming) {
				ActivityEntry? existing = null;

				if(!string.IsNullOrWhiteSpace(e.Id)) {
					existing = Feed.FirstOrDefault(x => x.Id == e.Id && x.OwnerUserId == e.OwnerUserId);
				}
				if(existing is null) {
					// fallback to coarse signature for legacy rows
					existing = Feed.FirstOrDefault(x =>
						x.Timestamp == e.Timestamp &&
						string.Equals(x.Kind, e.Kind, StringComparison.Ordinal) &&
						string.Equals(x.Message, e.Message, StringComparison.Ordinal) &&
						string.Equals(x.Actor, e.Actor, StringComparison.Ordinal));
				}

				if(existing is null) Feed.Add(e);
				else {
					var i = Feed.IndexOf(existing);
					if(i >= 0) Feed[i] = e;
				}
			}
			ResortFeedNewestFirst();
			SaveLocal();
		}

		private void ResortFeedNewestFirst() {
			var ordered = Feed.OrderByDescending(x => x.Timestamp).ToList();
			if(ordered.Count != Feed.Count || !ordered.SequenceEqual(Feed)) {
				Feed.Clear();
				foreach(var e in ordered) Feed.Add(e);
			}
		}

		// Kept for completeness; not used now but harmless to keep
		private void ReplaceAll(IList<ActivityEntry> entries) {
			Feed.Clear();
			foreach(var e in entries.OrderByDescending(x => x.Timestamp))
				Feed.Add(e);
		}
		public async Task ReactAsync(string ownerUserId, string entryId, string reactorUserId, string emoji) {
			if(string.IsNullOrWhiteSpace(ownerUserId) || string.IsNullOrWhiteSpace(entryId) || string.IsNullOrWhiteSpace(reactorUserId))
				return;

			var db = FirestoreClient.GetDb();
			var doc = Col(db, ownerUserId).Document(entryId);

			// write to Reactions.{reactorUserId} = emoji (merge)
			await doc.SetAsync(new Dictionary<string, object?> {
				["Reactions"] = new Dictionary<string, object?> { [reactorUserId] = emoji }
			}, SetOptions.MergeAll);

			// optimistic local update
			_ui.BeginInvoke(() => {
				var row = Feed.FirstOrDefault(a => a.Id == entryId && a.OwnerUserId == ownerUserId);
				if(row != null) {
					row.Reactions[reactorUserId] = emoji;
					// replace to trigger bindings if needed
					var idx = Feed.IndexOf(row);
					if(idx >= 0) Feed[idx] = row;
					SaveLocal();
				}
			});
		}

		private void LoadLocal() {
			try {
				if(!File.Exists(LocalFeedPath)) return;

				var json = File.ReadAllText(LocalFeedPath);
				var saved = JsonSerializer.Deserialize<List<ActivityEntry>>(json) ?? new();
				var myDisplay = _settings.DisplayName ?? "Me";

				foreach(var s in saved.OrderByDescending(x => x.Timestamp)) {
					// normalize Actor fallback only; keep the rest intact
					var actor = string.IsNullOrWhiteSpace(s.Actor) ? myDisplay : s.Actor;

					Feed.Add(new ActivityEntry {
						Id = s.Id,
						OwnerUserId = s.OwnerUserId,
						Timestamp = s.Timestamp,
						Kind = s.Kind,
						Message = s.Message,
						Actor = actor,
						IsMine = s.IsMine,
						Reactions = new Dictionary<string, string>(s.Reactions ?? new())
					});
				}
			}
			catch { /* ignore */ }
		}
		public Task PostMessageAsync(string text, string myUserId)
	=> LogAsync(new ActivityEntry {
		Kind = "chat.message",
		Message = text,
		Timestamp = DateTime.UtcNow
	}, myUserId);

		private void SaveLocal() {
			try {
				// Newest-first for nicer diffs
				var snapshot = Feed.OrderByDescending(x => x.Timestamp).ToList();
				var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
				File.WriteAllText(LocalFeedPath, json);
			}
			catch { /* ignore local write failures */ }
		}
	}
}