// ActivityLogService.cs (REST-only, no Google.Cloud.Firestore)
// Requires: AppServices.FirestoreRest (TaskMate.Sync), SettingsService for DisplayName
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.VisualBasic;
using TaskMate.Models;
using TaskMate.Sync;

namespace TaskMate.Services {
	public sealed class ActivityLogService : IActivityLogService, IDisposable {
		private readonly Dispatcher _ui;
		private readonly ISettingsService _settings;
		private readonly FirestoreRestClient _rest;

		private CancellationTokenSource? _mineCts;
		private CancellationTokenSource? _partnerCts;

		private const string LocalFeedPath = "activity_feed.json";

		public ObservableCollection<ActivityEntry> Feed { get; } = new();

		public ActivityLogService(Dispatcher ui, ISettingsService settings) {
			_ui = ui ?? throw new ArgumentNullException(nameof(ui));
			_settings = settings ?? throw new ArgumentNullException(nameof(settings));
			_rest = AppServices.FirestoreRest
				?? throw new InvalidOperationException("FirestoreRest is not initialized.");
			LoadLocal();
		}

		// ---------------------------------------------------------------------
		// Write (REST add with auto-id)
		// ---------------------------------------------------------------------
		public async Task LogAsync(ActivityEntry e, string myUserId) {
			if(e is null || string.IsNullOrWhiteSpace(myUserId)) return;

			var utc = DateTime.SpecifyKind(e.Timestamp, DateTimeKind.Utc);
			var myActor = _settings.DisplayName ?? "Me";

			// Add to users/{uid}/activity (auto-id). NOTE: top-level fields, no F.Map wrapper.
			var docName = await _rest.AddAsync(
				$"users/{myUserId}/activity",
				new {
					Timestamp = F.Ts(utc),
					Actor = F.Str(myActor),
					Kind = F.Str(e.Kind ?? ""),
					Message = F.Str(e.Message ?? ""),
					Reactions = new { mapValue = new { fields = new { } } }
				});
			var id = docName.Split('/').Last();

			// Optimistic echo (local time so it matches your header clock)
			try {
				var localTs = utc.ToLocalTime();
				var echo = new ActivityEntry {
					Id = id, // auto-id will be picked up by poller
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

		// ---------------------------------------------------------------------
		// “Listeners” via light polling (no Google SDK)
		// ---------------------------------------------------------------------
		public Task StartMineAsync(string myUserId) {
			_mineCts?.Cancel();
			_mineCts = string.IsNullOrWhiteSpace(myUserId) ? null : new CancellationTokenSource();
			if(_mineCts is null) return Task.CompletedTask;

			var ct = _mineCts.Token;
			_ = Task.Run(() => PollUserActivityAsync(myUserId, isMine: true, ct), ct);
			return Task.CompletedTask;
		}

		public Task StartPartnerSinceAsync(string partnerUserId, DateTime sinceUtc) {
			_partnerCts?.Cancel();
			_partnerCts = string.IsNullOrWhiteSpace(partnerUserId) ? null : new CancellationTokenSource();
			if(_partnerCts is null) return Task.CompletedTask;

			var ct = _partnerCts.Token;
			// small skew so we don’t miss writes just before connect
			_ = Task.Run(() => PollUserActivityAsync(partnerUserId, isMine: false, ct, sinceUtc.AddSeconds(-10)), ct);
			return Task.CompletedTask;
		}

		public Task StopPartnerAsync() {
			_partnerCts?.Cancel();
			_partnerCts = null;
			return Task.CompletedTask;
		}

		public void Dispose() {
			_mineCts?.Cancel();
			_partnerCts?.Cancel();
			_mineCts = _partnerCts = null;
		}

		// ---------------------------------------------------------------------
		// Reactions (read-modify-write using REST)
		// ---------------------------------------------------------------------
		public async Task ReactAsync(string ownerUserId, string entryId, string reactorUserId, string emoji) {
			if(string.IsNullOrWhiteSpace(ownerUserId) ||
				string.IsNullOrWhiteSpace(entryId) ||
				string.IsNullOrWhiteSpace(reactorUserId)) return;

			var docPath = $"users/{ownerUserId}/activity/{entryId}";

			// 1) Read existing doc
			string json;
			try { json = await _rest.GetDocRawAsync(docPath).ConfigureAwait(false); }
			catch { return; } // not found or unauthorized

			// 2) Parse fields, update Reactions map in-memory
			var root = JsonDocument.Parse(json).RootElement;
			if(!root.TryGetProperty("fields", out var fields)) return;

			// Build mutable dictionary from fields for easier patch
			var fieldDict = new Dictionary<string, object>();
			foreach(var prop in fields.EnumerateObject())
				fieldDict[prop.Name] = prop.Value;

			// Ensure a map for Reactions exists, then set reactorUserId = emoji
			var reactionsFields = new Dictionary<string, object>();
			if(fields.TryGetProperty("Reactions", out var reactNode) &&
				reactNode.TryGetProperty("mapValue", out var mv) &&
				mv.TryGetProperty("fields", out var rfields)) {
				foreach(var r in rfields.EnumerateObject())
					reactionsFields[r.Name] = r.Value;
			}
			reactionsFields[reactorUserId] = F.Str(emoji);

			fieldDict["Reactions"] = new { mapValue = new { fields = reactionsFields } };

			// 3) Write the full fields object back (no merge mask needed)
			await _rest.SetDocAsync(docPath, fieldDict).ConfigureAwait(false);

			// 4) Optimistic local update
			_ = _ui.BeginInvoke(() => {
	var old = Feed.FirstOrDefault(a => a.Id == entryId && a.OwnerUserId == ownerUserId);
	if(old == null) return;

	// clone reactions and apply this user’s emoji
	var newReactions = new Dictionary<string, string>(old.Reactions ?? new());
	newReactions[reactorUserId] = emoji;

	// NEW: create a fresh ActivityEntry so DataContext actually changes
	var updated = new ActivityEntry {
		Id          = old.Id,
		OwnerUserId = old.OwnerUserId,
		Timestamp   = old.Timestamp,
		Actor       = old.Actor,
		Kind        = old.Kind,
		Message     = old.Message,
		IsMine      = old.IsMine,
		Reactions   = newReactions
	};

	var idx = Feed.IndexOf(old);
	if(idx >= 0)
		Feed[idx] = updated;   // CollectionChanged(Replace with new instance)

	SaveLocal();
});
		}

		public Task PostMessageAsync(string text, string myUserId)
			=> LogAsync(new ActivityEntry {
				Kind = "chat.message",
				Message = text,
				Timestamp = DateTime.UtcNow
			}, myUserId);

		// ---------------------------------------------------------------------
		// Polling workers
		// ---------------------------------------------------------------------
		private async Task PollUserActivityAsync(
			string userId,
			bool isMine,
			CancellationToken ct,
			DateTime? sinceUtc = null) {
			string? lastSig = null;
			DateTime? lastSeenUtc = sinceUtc;   // only used for partner

			while(!ct.IsCancellationRequested) {
				try {
					var myDisplay = _settings.DisplayName ?? "Me";

					// 👉 For my own activity, always fetch the full latest page (no cutoff)
					// 👉 For partner, use lastSeenUtc as a cutoff for “only new since…”
					var effectiveCutoff = isMine ? (DateTime?)null : lastSeenUtc;

					var items = await QueryUserActivityAsync(
						userId,
						isMine,
						isMine ? myDisplay : null,
						effectiveCutoff).ConfigureAwait(false);

					// Only advance lastSeenUtc for partner
					if(!isMine && items.Count > 0) {
						var newestLocal = items.Max(e => e.Timestamp);
						lastSeenUtc = newestLocal.ToUniversalTime();
					}

					// ⚠️ For my own feed: if nothing came back, don't blow away what I already have
					if(isMine && items.Count == 0) {
						goto DelayOnly;
					}

					// Signature still used to avoid unnecessary UI updates
					// Signature used to avoid unnecessary UI updates
					string MakeSig(ActivityEntry e) {
						// Deterministic representation of reaction map
						var reactionsPart = string.Join(",",
							(e.Reactions ?? new Dictionary<string, string>())
								.OrderBy(kv => kv.Key)
								.Select(kv => $"{kv.Key}={kv.Value}"));

						return $"{e.Id ?? ""}#{e.Timestamp:O}#{reactionsPart}";
					}

					var sig = string.Join("|", items.Select(MakeSig));

					if(!string.Equals(sig, lastSig, StringComparison.Ordinal)) {
						lastSig = sig;
						if(isMine)
							_ui.BeginInvoke(() => ReplaceMine(items, myDisplay));
						else
							_ui.BeginInvoke(() => UpsertIntoFeed(items));
					}
				}
				catch {
					// keep polling even if a cycle fails
				}

			DelayOnly:
				try { await Task.Delay(8000, ct).ConfigureAwait(false); } catch { }
			}
		}

		private async Task<IList<ActivityEntry>> QueryUserActivityAsync(
			string userId,
			bool isMine,
			string? myDisplayForMine,
			DateTime? cutoffUtc) {
			var list = new List<ActivityEntry>();

			// Use the same pattern as personal tasks: ListDocs on the subcollection
			var docs = await _rest.ListDocsAsync(
				collectionPath: $"users/{userId}/activity",
				orderBy: "Timestamp desc",
				pageSize: 200
			).ConfigureAwait(false);

			foreach(var doc in docs) {
				// Map every doc
				var entry = MapFromDoc(doc, userId, isMine, myDisplayForMine);

				// Optional: apply cutoff in-memory if provided (for partner "since" logic)
				if(cutoffUtc.HasValue) {
					var tsUtc = entry.Timestamp.ToUniversalTime();
					if(tsUtc <= cutoffUtc.Value.ToUniversalTime())
						continue;
				}

				list.Add(entry);
			}

			// Already ordered by Timestamp desc from the query, but just to be safe:
			var final = list
				.GroupBy(e => $"{e.OwnerUserId}/{e.Id}")
				.Select(g => g.OrderByDescending(x => x.Timestamp).First())
				.OrderByDescending(x => x.Timestamp)
				.ToList();

			return final;
		}

		// ---------------------------------------------------------------------
		// Mapping helpers (REST doc -> ActivityEntry)
		// ---------------------------------------------------------------------
		private static ActivityEntry MapFromDoc(
			JsonElement doc,
			string ownerUserId,
			bool isMine,
			string? myDisplayForMine) {

			var id = doc.GetProperty("name").GetString()!.Split('/').Last();
			var f = doc.GetProperty("fields");

			static string? S(JsonElement f, string k)
				=> f.TryGetProperty(k, out var v) && v.TryGetProperty("stringValue", out var s) ? s.GetString() : null;

			static DateTime? T(JsonElement f, string k)
				=> f.TryGetProperty(k, out var v) && v.TryGetProperty("timestampValue", out var t)
					? DateTime.Parse(t.GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind)
					: (DateTime?)null;

			var reactions = new Dictionary<string, string>();
			if(f.TryGetProperty("Reactions", out var react) &&
				react.TryGetProperty("mapValue", out var mv) &&
				mv.TryGetProperty("fields", out var rf)) {
				foreach(var kv in rf.EnumerateObject())
					if(kv.Value.TryGetProperty("stringValue", out var sv))
						reactions[kv.Name] = sv.GetString() ?? "";
			}

			var tsUtc = T(f, "Timestamp") ?? DateTime.UtcNow;
			var actor = S(f, "Actor") ?? "";
			if(isMine && string.IsNullOrWhiteSpace(actor))
				actor = myDisplayForMine ?? "Me";

			return new ActivityEntry {
				Id = id,
				OwnerUserId = ownerUserId,
				Timestamp = tsUtc.ToLocalTime(),
				Actor = actor,                     // set in initializer (init-only safe)
				Kind = S(f, "Kind") ?? "",
				Message = S(f, "Message") ?? "",
				Reactions = reactions,
				IsMine = isMine                    // set in initializer (init-only safe)
			};
		}

		// ---------------------------------------------------------------------
		// Local feed merge & persistence
		// ---------------------------------------------------------------------
		private void ReplaceMine(IList<ActivityEntry> mine, string myDisplay) {
			// Figure out which userId is "me" on this device
			var myUserId = AppServices.Auth?.Uid
						   ?? _settings.UserId
						   ?? string.Empty;

			// Remove only entries that are actually mine
			for(int i = Feed.Count - 1; i >= 0; i--) {
				var row = Feed[i];

				// Old logic (problematic):
				// var a = row.Actor;
				// if (string.Equals(a, myDisplay, StringComparison.OrdinalIgnoreCase)
				//     || string.Equals(a, "Me", StringComparison.OrdinalIgnoreCase)) {
				//     Feed.RemoveAt(i);
				// }

				// New logic: use OwnerUserId / IsMine instead of Actor text
				if(row.IsMine &&
					!string.IsNullOrWhiteSpace(row.OwnerUserId) &&
					string.Equals(row.OwnerUserId, myUserId, StringComparison.OrdinalIgnoreCase)) {
					Feed.RemoveAt(i);
				}
			}

			// Add the fresh "mine" entries we just fetched
			foreach(var e in mine.OrderByDescending(x => x.Timestamp))
				Feed.Add(e);

			ResortFeedNewestFirst();
			SaveLocal();
		}

		private void UpsertIntoFeed(IEnumerable<ActivityEntry> incoming) {
			foreach(var e in incoming) {
				ActivityEntry? existing = null;

				if(!string.IsNullOrWhiteSpace(e.Id)) {
					existing = Feed.FirstOrDefault(x => x.Id == e.Id && x.OwnerUserId == e.OwnerUserId);
				}
				if(existing is null) {
					// legacy coarse signature
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

		private void LoadLocal() {
			try {
				if(!File.Exists(LocalFeedPath)) return;

				var json = File.ReadAllText(LocalFeedPath);
				var saved = JsonSerializer.Deserialize<List<ActivityEntry>>(json) ?? new();
				var myDisplay = _settings.DisplayName ?? "Me";

				foreach(var s in saved.OrderByDescending(x => x.Timestamp)) {
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

		private void SaveLocal() {
			try {
				var snapshot = Feed.OrderByDescending(x => x.Timestamp).ToList();
				var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
				File.WriteAllText(LocalFeedPath, json);
			}
			catch { /* ignore local write failures */ }
		}
	}
}
