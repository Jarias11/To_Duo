using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using TaskMate.Models;
using TaskMate.Data.Repositories;

namespace TaskMate.Sync {
    public class FirestoreTaskRepository : ITaskRepository {
        private static CollectionReference Col(FirestoreDb db, string groupId) =>
            db.Collection("groups").Document(groupId).Collection("tasks");

        public async Task<IList<TaskItem>> LoadAllAsync(string groupId) {
            var db = FirestoreClient.GetDb();
            var snap = await Col(db, groupId).GetSnapshotAsync();
            return snap.Documents.Select(MapFromDoc).ToList();
        }

        public async Task UpsertAsync(string groupId, TaskItem item) {
            var db = FirestoreClient.GetDb();
            var doc = Col(db, groupId).Document(item.Id.ToString());

            var data = new Dictionary<string, object?> {
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
                ["AssignedTo"] = item.AssignedTo,
                ["AssignedToUserId"] = item.AssignedToUserId, // keep for future
                ["IsSuggestion"] = item.IsSuggestion,
                ["MediaPath"] = item.MediaPath,
                ["IsRecurring"] = item.IsRecurring,
                ["Deleted"] = item.Deleted,
                ["UpdatedAt"] = Timestamp.GetCurrentTimestamp()
            };

            await doc.SetAsync(data, SetOptions.MergeAll);
        }

        public async Task DeleteAsync(string groupId, Guid id) {
            var db = FirestoreClient.GetDb();
            await Col(db, groupId).Document(id.ToString()).DeleteAsync();
        }

        // ✅ Returns IDisposable (FirestoreChangeListener)
        public IDisposable ListenAll(string groupId, Action<IList<TaskItem>> onSnapshot) {
            var db = FirestoreClient.GetDb();
            var inner = Col(db, groupId).Listen(snap => {
                var items = snap.Documents.Select(MapFromDoc).ToList();
                onSnapshot(items);
            });
            return new FirestoreListenerHandle(inner);   // ← wrap it
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