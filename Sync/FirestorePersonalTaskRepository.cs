using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using TaskMate.Models;
using TaskMate.Data.Repositories;
using TaskMate.Sync;

namespace TaskMate.Sync {
	public class FirestorePersonalTaskRepository : IPersonalTaskRepo {
		private static CollectionReference Col(FirestoreDb db, string userId) =>
			db.Collection("users").Document(userId).Collection("tasks");

		public async Task<IList<TaskItem>> LoadAllAsync(string userId) {
			var db = FirestoreClient.GetDb();
			var snap = await Col(db, userId).GetSnapshotAsync();
			return snap.Documents.Select(MapFromDoc).ToList();
		}

		public async Task UpsertAsync(TaskItem item, string userId) {
			var db = FirestoreClient.GetDb();
			var doc = Col(db, userId).Document(item.Id.ToString());

			var data = MapToDict(item);
			data["UpdatedAt"] = Timestamp.GetCurrentTimestamp();

			await doc.SetAsync(data, SetOptions.MergeAll);
		}

		public async Task DeleteAsync(Guid id, string userId) {
			var db = FirestoreClient.GetDb();
			await Col(db, userId).Document(id.ToString()).DeleteAsync();
		}

		public IDisposable Listen(string userId, Action<IList<TaskItem>> onSnapshot) {
			var db = FirestoreClient.GetDb();
			var inner = Col(db, userId).Listen(snap => {
				var items = snap.Documents.Select(MapFromDoc).ToList();
				onSnapshot(items);
			});
			return new FirestoreListenerHandle(inner);
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
				// Personal list doesn’t need Accepted/AssignedToUserId to function,
				// but keeping them harmlessly allows a uniform TaskItem.
				["Accepted"] = item.Accepted,
				["AssignedTo"] = item.AssignedTo,
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

			return new TaskItem {
				Id = Guid.TryParse(S(d.GetValueOrDefault("Id")), out var gid) ? gid : Guid.Parse(doc.Id),
				Title = S(d.GetValueOrDefault("Title")),
				Description = S(d.GetValueOrDefault("Description")),
				Category = S(d.GetValueOrDefault("Category")),
				DueDate = ToDate(d.GetValueOrDefault("DueDate")),
				IsCompleted = B(d.GetValueOrDefault("IsCompleted")),
				CreatedBy = S(d.GetValueOrDefault("CreatedBy")) ?? "",
				Accepted = B(d.GetValueOrDefault("Accepted"), true),
				AssignedTo = S(d.GetValueOrDefault("AssignedTo")),
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