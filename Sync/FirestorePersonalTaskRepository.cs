// Services/Sync/FirestorePersonalTaskRepository.cs
using System.Text.Json;
using TaskMate.Models;
using TaskMate.Models.Enums;
using TaskMate.Services;

namespace TaskMate.Sync {
	public interface IPersonalTaskRepo {
		Task<IList<TaskItem>> LoadAllAsync(string userId);
		Task UpsertAsync(TaskItem item, string userId);
		Task DeleteAsync(Guid id, string userId);
		IDisposable Listen(string userId, Action<IList<TaskItem>> onSnapshot);
	}

	/// <summary>
	/// REST-only personal tasks repo at users/{uid}/tasks.
	/// </summary>
	public sealed class FirestorePersonalTaskRepository : IPersonalTaskRepo {
		private readonly FirestoreRestClient _rest;
		public FirestorePersonalTaskRepository(FirestoreRestClient rest) {
			_rest = rest ?? throw new ArgumentNullException(nameof(rest));
		}

		private static string ColPath(string uid) => $"users/{uid}/tasks";

		// identical to before
		public async Task<IList<TaskItem>> LoadAllAsync(string userId) {
			var list = await RunListAsync(userId).ConfigureAwait(false);
			return list;
		}

		public async Task UpsertAsync(TaskItem item, string userId) {
			var path = $"{ColPath(userId)}/{item.Id}";
			var fields = MapToFields(item); // dictionary style like SDK

			// Firestore requires "fields" at the top level
			await _rest.SetDocAsync(path, fields);
		}

		public Task DeleteAsync(Guid id, string userId)
			=> _rest.DeleteDocAsync($"{ColPath(userId)}/{id}");

		public IDisposable Listen(string userId, Action<IList<TaskItem>> onSnapshot)
	=> StartPolling(userId, onSnapshot);

		// --- internal polling helper
		private IDisposable StartPolling(string userId, Action<IList<TaskItem>> cb) {
			var cts = new CancellationTokenSource();
			_ = Task.Run(async () => {
				var last = new Dictionary<string, DateTimeOffset>();
				var first = true;

				System.Diagnostics.Trace.WriteLine($"[FS] StartPolling for user '{userId}'");

				while(!cts.IsCancellationRequested) {
					try {
						var items = await RunListAsync(userId).ConfigureAwait(false);
						System.Diagnostics.Trace.WriteLine($"[FS] RunListAsync ok – {items.Count} items");

						bool changed = items.Count != last.Count ||
									   items.Any(i => !last.TryGetValue(i.Id.ToString(), out var prev) ||
													  prev != new DateTimeOffset(i.UpdatedAt ?? DateTime.MinValue, TimeSpan.Zero));

						if(first || changed) {
							first = false;
							last = items.ToDictionary(
								i => i.Id.ToString(),
								i => new DateTimeOffset(i.UpdatedAt ?? DateTime.MinValue, TimeSpan.Zero));
							cb(items);
						}
					}
					catch(Exception ex) {
						System.Diagnostics.Trace.WriteLine("[FS] Poll error: " + ex);
					}

					try { await Task.Delay(1500, cts.Token).ConfigureAwait(false); } catch { }
				}
			}, cts.Token);

			return new FirestoreListenerHandle(() => cts.Cancel());
		}

		// --- field mapping identical to old SDK MapToDict ---------------------
		private static Dictionary<string, object?> MapToFields(TaskItem item) {
			DateTime ToUtc(DateTime dt) => DateTime.SpecifyKind(dt, DateTimeKind.Utc);
			var dict = new Dictionary<string, object?> {
				["Id"] = F.Str(item.Id.ToString()),
				["Title"] = F.Str(item.Title ?? ""),
				["Description"] = F.Str(item.Description ?? ""),
				["Category"] = F.Str(item.Category ?? ""),
				["DueDate"] = item.DueDate.HasValue ? F.Ts(ToUtc(item.DueDate.Value)) : F.Null(),
				["IsCompleted"] = F.Bool(item.IsCompleted),
				["CompletedAt"] = item.CompletedAt.HasValue ? F.Ts(ToUtc(item.CompletedAt.Value)) : F.Null(),
				["CreatedBy"] = F.Str(item.CreatedBy ?? ""),
				["Accepted"] = F.Bool(item.Accepted),
				["AssignedTo"] = F.Str(item.AssignedTo.ToString()),
				["AssignedToUserId"] = F.Str(item.AssignedToUserId ?? ""),
				["IsSuggestion"] = F.Bool(item.IsSuggestion),
				["MediaPath"] = string.IsNullOrWhiteSpace(item.MediaPath) ? F.Null() : F.Str(item.MediaPath),
				["IsRecurring"] = F.Bool(item.IsRecurring),
				["Deleted"] = F.Bool(item.Deleted),
				["UpdatedAt"] = F.Ts(ToUtc(item.UpdatedAt ?? DateTime.UtcNow))
			};
			return dict;
		}

		// --- Query listing helper ---------------------------------------------
		private async Task<IList<TaskItem>> RunListAsync(string userId) {
			var docs = await _rest.ListDocsAsync(
				collectionPath: $"users/{userId}/tasks",
				orderBy: "UpdatedAt desc",
				pageSize: 200
			).ConfigureAwait(false);

			var list = new List<TaskItem>(docs.Count);
			foreach(var doc in docs) list.Add(MapFromDoc(doc));
			return list;
		}

		private static TaskItem MapFromDoc(JsonElement doc) {
			var id = doc.GetProperty("name").GetString()!.Split('/').Last();
			var f = doc.GetProperty("fields");

			static string? S(JsonElement f, string k)
				=> f.TryGetProperty(k, out var v) && v.TryGetProperty("stringValue", out var s) ? s.GetString() : null;
			static bool B(JsonElement f, string k, bool d = false)
				=> f.TryGetProperty(k, out var v) && v.TryGetProperty("booleanValue", out var b) ? b.GetBoolean() : d;
			static DateTime? T(JsonElement f, string k)
				=> f.TryGetProperty(k, out var v) && v.TryGetProperty("timestampValue", out var t)
					? DateTime.Parse(t.GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind)
					: (DateTime?)null;

			var assignee = string.Equals(S(f, "AssignedTo"), "Partner", StringComparison.OrdinalIgnoreCase)
				? Assignee.Partner : Assignee.Me;

			return new TaskItem {
				Id = Guid.TryParse(S(f, "Id"), out var gid) ? gid : Guid.Parse(id),
				Title = S(f, "Title"),
				Description = S(f, "Description"),
				Category = S(f, "Category"),
				DueDate = T(f, "DueDate"),
				IsCompleted = B(f, "IsCompleted"),
				CompletedAt = T(f, "CompletedAt"),
				CreatedBy = S(f, "CreatedBy") ?? "",
				Accepted = B(f, "Accepted"),
				AssignedTo = assignee,
				AssignedToUserId = S(f, "AssignedToUserId") ?? "",
				IsSuggestion = B(f, "IsSuggestion"),
				MediaPath = S(f, "MediaPath"),
				IsRecurring = B(f, "IsRecurring"),
				Deleted = B(f, "Deleted"),
				UpdatedAt = T(f, "UpdatedAt")
			};
		}
	}
}
