using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using TaskMate.Models;
using TaskMate.Data.Repositories;

namespace TaskMate.Sync {
    public class FirestoreTaskRepository : ITaskRepository {
        private static CollectionReference TasksCol(FirestoreDb db, string groupId) =>
            db.Collection("groups").Document(groupId).Collection("tasks");

        public async Task<IList<TaskItem>> LoadAllAsync(string groupId) {
            var db = await FirestoreClient.GetDbAsync();
            var snap = await TasksCol(db, groupId).GetSnapshotAsync();

            var list = new List<TaskItem>();
            foreach (var doc in snap.Documents) {
                var dict = doc.ToDictionary();

                // Helper to read value safely
                object Get(string key) => dict.TryGetValue(key, out var v) ? v : null;

                var item = new TaskItem {
                    Id = Guid.TryParse(Get("Id")?.ToString(), out var gid) ? gid : Guid.NewGuid(),
                    Title = Get("Title")?.ToString(),
                    Description = Get("Description")?.ToString(),
                    Category = Get("Category")?.ToString(),
                    DueDate = Get("DueDate") is Timestamp ts ? ts.ToDateTime() : null,
                    IsCompleted = Get("IsCompleted") as bool? ?? false,
                    CreatedBy = Get("CreatedBy")?.ToString(),
                    Accepted = Get("Accepted") as bool? ?? true,
                    AssignedTo = Get("AssignedTo")?.ToString(),
                    IsSuggestion = Get("IsSuggestion") as bool? ?? false,
                    MediaPath = Get("MediaPath")?.ToString(),
                    IsRecurring = Get("IsRecurring") as bool? ?? false
                };

                list.Add(item);
            }
            return list;
        }

        public async Task UpsertAsync(string groupId, TaskItem item) {
            var db = await FirestoreClient.GetDbAsync();
            var doc = TasksCol(db, groupId).Document(item.Id.ToString());

            var data = new Dictionary<string, object?> {
                ["Id"] = item.Id.ToString(),
                ["Title"] = item.Title,
                ["Description"] = item.Description,
                ["Category"] = item.Category,
                ["DueDate"] = item.DueDate.HasValue
                                    ? Timestamp.FromDateTime(DateTime.SpecifyKind(item.DueDate.Value, DateTimeKind.Utc))
                                    : null,
                ["IsCompleted"] = item.IsCompleted,
                ["CreatedBy"] = item.CreatedBy,
                ["Accepted"] = item.Accepted,
                ["AssignedTo"] = item.AssignedTo,
                ["IsSuggestion"] = item.IsSuggestion,
                ["MediaPath"] = item.MediaPath,
                ["IsRecurring"] = item.IsRecurring
            };

            await doc.SetAsync(data, SetOptions.MergeAll);
        }
        public async Task DeleteAsync(string groupId, Guid id) {
            var db = await FirestoreClient.GetDbAsync();
            await db.Collection("groups").Document(groupId)
                    .Collection("tasks").Document(id.ToString())
                    .DeleteAsync();
        }
    }
}