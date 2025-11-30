// Services/Sync/FirestoreRequestRepository.cs
using System.Text.Json;
using TaskMate.Models;
using TaskMate.Models.Enums;
using TaskMate.Services;

namespace TaskMate.Sync {
	public interface IRequestRepo {
		Task<IList<TaskItem>> LoadAllAsync(string groupId);
		Task UpsertAsync(TaskItem item, string groupId);
		Task DeleteAsync(Guid id, string groupId);
		IDisposable Listen(string groupId, Action<IList<TaskItem>> onSnapshot);
		Task AcceptAsync(TaskItem item, string groupId, string assigneeUserId);
	}

	/// <summary>
	/// REST-only repo for group requests at groups/{groupId}/requests.
	/// </summary>
	public sealed class FirestoreRequestRepository : IRequestRepo {
		private readonly FirestoreRestClient _rest;
		private readonly IPersonalTaskRepo _personal;
		public FirestoreRequestRepository(FirestoreRestClient rest, IPersonalTaskRepo personal) {
			_rest = rest ?? throw new ArgumentNullException(nameof(rest));
			_personal = personal ?? throw new ArgumentNullException(nameof(personal));
		}

		private static string ColPath(string gid) => $"groups/{gid}/requests";

		public async Task<IList<TaskItem>> LoadAllAsync(string groupId) {
			// Match Personal repo style for consistency and least privilege.
			var docs = await _rest.ListDocsAsync(
				collectionPath: ColPath(groupId),
				orderBy: "UpdatedAt desc",
				pageSize: 200
			).ConfigureAwait(false);

			var list = new List<TaskItem>(docs.Count);
			foreach(var doc in docs)
				list.Add(MapFromDoc(doc));
			return list;
		}

		public async Task UpsertAsync(TaskItem item, string groupId) {
			var docPath = $"{ColPath(groupId)}/{item.Id}";

			var updatedUtc = (item.UpdatedAt ?? DateTime.UtcNow).ToUniversalTime();
			var dueUtc = item.DueDate?.ToUniversalTime();

			await _rest.SetDocAsync(docPath, new {
				Id = F.Str(item.Id.ToString()),
				Title = F.Str(item.Title ?? string.Empty),
				Description = F.Str(item.Description ?? string.Empty),
				Category = string.IsNullOrWhiteSpace(item.Category) ? F.Null() : F.Str(item.Category),
				AssignedTo = F.Str(item.AssignedTo.ToString()),
				AssignedToUserId = F.Str(item.AssignedToUserId ?? string.Empty),
				CreatedBy = F.Str(item.CreatedBy ?? string.Empty),

				Accepted = F.Bool(false),
				IsCompleted = F.Bool(item.IsCompleted),
				IsSuggestion = F.Bool(item.IsSuggestion),
				IsRecurring = F.Bool(item.IsRecurring),
				Deleted = F.Bool(item.Deleted),

				UpdatedAt = F.Ts(updatedUtc),
				DueDate = dueUtc.HasValue ? F.Ts(dueUtc.Value) : F.Null(),

				GroupId = F.Str(groupId)
			}).ConfigureAwait(false);
		}

		public Task DeleteAsync(Guid id, string groupId)
			=> _rest.DeleteDocAsync($"{ColPath(groupId)}/{id}");

		public IDisposable Listen(string groupId, Action<IList<TaskItem>> onSnapshot) {
			var cts = new CancellationTokenSource();
			_ = Task.Run(async () => {
				var cache = new Dictionary<string, DateTimeOffset>();
				DateTimeOffset? lastNewest = null;
				var first = true;

				while(!cts.IsCancellationRequested) {
					try {
						// Cheap head check: only newest request
						var headDocs = await _rest.ListDocsAsync(
							collectionPath: ColPath(groupId),
							orderBy: "UpdatedAt desc",
							pageSize: 1
						).ConfigureAwait(false);

						DateTimeOffset? newest = null;
						if(headDocs.Count > 0) {
							var headItem = MapFromDoc(headDocs[0]);
							newest = new DateTimeOffset(headItem.UpdatedAt ?? DateTime.MinValue, TimeSpan.Zero);
						}

						if(!first && newest.HasValue && lastNewest.HasValue && newest.Value == lastNewest.Value) {
							goto DelayOnly;
						}

						lastNewest = newest;
						first = false;

						// Full list only when changed
						var items = await LoadAllAsync(groupId).ConfigureAwait(false);

						bool changed = items.Count != cache.Count ||
									   items.Any(i =>
										   !cache.TryGetValue(i.Id.ToString(), out var prev) ||
										   prev != new DateTimeOffset(i.UpdatedAt ?? DateTime.MinValue, TimeSpan.Zero));

						if(changed) {
							cache = items.ToDictionary(
								i => i.Id.ToString(),
								i => new DateTimeOffset(i.UpdatedAt ?? DateTime.MinValue, TimeSpan.Zero));
							onSnapshot(items);
						}
					}
					catch {
						// keep polling
					}

				DelayOnly:
					try { await Task.Delay(8000, cts.Token).ConfigureAwait(false); } catch { }
				}
			}, cts.Token);

			return new FirestoreListenerHandle(() => cts.Cancel());
		}

		public async Task AcceptAsync(TaskItem item, string groupId, string assigneeUserId) {
			await _personal.UpsertAsync(new TaskItem {
				Id = item.Id,
				Title = item.Title,
				Description = item.Description,
				Category = item.Category,
				DueDate = item.DueDate,
				IsCompleted = item.IsCompleted,
				CompletedAt = item.CompletedAt,
				CreatedBy = item.CreatedBy,
				Accepted = true,
				AssignedTo = item.AssignedTo,
				AssignedToUserId = assigneeUserId,
				IsSuggestion = item.IsSuggestion,
				MediaPath = item.MediaPath,
				IsRecurring = item.IsRecurring,
				Deleted = item.Deleted,
				UpdatedAt = DateTime.UtcNow
			}, assigneeUserId).ConfigureAwait(false);

			await DeleteAsync(item.Id, groupId).ConfigureAwait(false);
		}

		// --- Mapping ----------------------------------------------------------

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
				Accepted = B(f, "Accepted", false),
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
