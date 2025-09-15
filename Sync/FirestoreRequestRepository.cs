using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using TaskMate.Models;
using TaskMate.Data.Repositories;
using TaskMate.Sync;
using TaskMate.Models.Enums;

namespace TaskMate.Sync {
	public class FirestoreRequestRepository : IRequestRepo {
		private static CollectionReference Col(FirestoreDb db, string groupId) =>
			db.Collection("groups").Document(groupId).Collection("requests");

		public async Task<IList<TaskItem>> LoadAllAsync(string groupId) {
			var db = FirestoreClient.GetDb();
			var snap = await Col(db, groupId).GetSnapshotAsync();
			return snap.Documents.Select(MapFromDoc).ToList();
		}

		public async Task UpsertAsync(TaskItem item, string groupId) {
			var db = FirestoreClient.GetDb();
			var doc = Col(db, groupId).Document(item.Id.ToString());

			var data = MapToDict(item);
			data["UpdatedAt"] = Timestamp.GetCurrentTimestamp();

			await doc.SetAsync(data, SetOptions.MergeAll);
		}

		public async Task DeleteAsync(Guid id, string groupId) {
			var db = FirestoreClient.GetDb();
			await Col(db, groupId).Document(id.ToString()).DeleteAsync();
		}

		public IDisposable Listen(string groupId, Action<IList<TaskItem>> onSnapshot) {
			var db = FirestoreClient.GetDb();
			var inner = Col(db, groupId).Listen(snap => {
				var items = snap.Documents.Select(MapFromDoc).ToList();
				onSnapshot(items);
			});
			return new FirestoreListenerHandle(inner);
		}

		public async Task AcceptAsync(TaskItem item, string groupId, string assigneeUserId) {
			var db = FirestoreClient.GetDb();

			var personalRef = db.Collection("users").Document(assigneeUserId)
								.Collection("tasks").Document(item.Id.ToString());
			var requestRef = Col(db, groupId).Document(item.Id.ToString());

			// Copy to personal with Accepted=true and AssignedToUserId=assignee
			var toUpsert = MapToDict(item);
			toUpsert["Accepted"] = true;
			toUpsert["AssignedToUserId"] = assigneeUserId;
			toUpsert["UpdatedAt"] = Timestamp.GetCurrentTimestamp();

			var batch = db.StartBatch();
			batch.Set(personalRef, toUpsert, SetOptions.MergeAll);
			batch.Delete(requestRef);
			await batch.CommitAsync();
		}

		private static Dictionary<string, object?> MapToDict(TaskItem item) {
			return new Dictionary<string, object?> {
				["Id"] = item.Id.ToString(),
				["Title"] = item.Title,
				["Description"] = item.Description,
				["Category"] = item.Category,
				["DueDate"] = item.DueDate is DateTime dt
					? Timestamp.FromDateTime(DateTime.SpecifyKind(dt, DateTimeKind.Utc))
					: null,
				["IsCompleted"] = item.IsCompleted,
				["CreatedBy"] = item.CreatedBy,
				["Accepted"] = item.Accepted,
				["AssignedTo"] = item.AssignedTo.ToString(),
				["AssignedToUserId"] = item.AssignedToUserId,
				["IsSuggestion"] = item.IsSuggestion,
				["MediaPath"] = item.MediaPath,
				["IsRecurring"] = item.IsRecurring,
				["Deleted"] = item.Deleted,
				["UpdatedAt"] = item.UpdatedAt
			};
		}

		private static TaskItem MapFromDoc(DocumentSnapshot doc) {
			var d = doc.ToDictionary();

			DateTime? ToDate(object? v) =>
				v is Timestamp ts ? ts.ToDateTime() :
				v is DateTime dt ? dt : (DateTime?)null;

			bool B(object? v, bool def = false) => v is bool b ? b : def;
			string? S(object? v) => v?.ToString();

			static Assignee ParseAssignee(object? v) {
				var s = v?.ToString();
				return string.Equals(s, "Partner", StringComparison.OrdinalIgnoreCase)
					? Assignee.Partner
					: Assignee.Me; // default/fallback for old/empty docs
			}

			return new TaskItem {
				Id = Guid.TryParse(S(d.GetValueOrDefault("Id")), out var gid) ? gid : Guid.Parse(doc.Id),
				Title = S(d.GetValueOrDefault("Title")),
				Description = S(d.GetValueOrDefault("Description")),
				Category = S(d.GetValueOrDefault("Category")),
				DueDate = ToDate(d.GetValueOrDefault("DueDate")),
				IsCompleted = B(d.GetValueOrDefault("IsCompleted")),
				CreatedBy = S(d.GetValueOrDefault("CreatedBy")) ?? "",
				Accepted = B(d.GetValueOrDefault("Accepted"), false),
				AssignedTo = ParseAssignee(d.GetValueOrDefault("AssignedTo")),
				AssignedToUserId = S(d.GetValueOrDefault("AssignedToUserId")) ?? "",
				IsSuggestion = B(d.GetValueOrDefault("IsSuggestion")),
				MediaPath = S(d.GetValueOrDefault("MediaPath")),
				IsRecurring = B(d.GetValueOrDefault("IsRecurring")),
				Deleted = B(d.GetValueOrDefault("Deleted")),
				UpdatedAt = ToDate(d.GetValueOrDefault("UpdatedAt"))
			};
		}
	}
}