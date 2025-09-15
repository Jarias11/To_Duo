using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TaskMate.Models;
using TaskMate.Models.Enums;

namespace TaskMate.Services
{
    public static class TaskCollectionHelpers
    {
        /// <summary>
        /// Replace target with incoming (optionally remapping AssignedTo and request-mode Accepted flag).
        /// </summary>
        public static void ReplaceAll(
            ObservableCollection<TaskItem> target,
            IList<TaskItem> incoming,
            Assignee? assignedTo = null,
            bool requestMode = false)
        {
            target.Clear();
            foreach (var inc in incoming)
            {
                var copy = CloneForUi(inc, assignedTo, requestMode);
                target.Add(copy);
            }
        }

        /// <summary>
        /// Upsert incoming items into target by Id, preferring newer UpdatedAt.
        /// </summary>
        public static void UpsertInto(
            ObservableCollection<TaskItem> target,
            IList<TaskItem> incoming,
            Assignee assignedTo,
            string ownerUserId)
        {
            var index = target.ToDictionary(t => t.Id);
            foreach (var inc in incoming)
            {
                var mapped = CloneForUi(inc, assignedTo, requestMode: false);

                if (index.TryGetValue(mapped.Id, out var existing))
                {
                    var oldTime = existing.UpdatedAt ?? DateTime.MinValue;
                    var newTime = mapped.UpdatedAt ?? DateTime.MinValue;
                    if (newTime > oldTime)
                    {
                        var pos = target.IndexOf(existing);
                        target[pos] = mapped;
                    }
                }
                else
                {
                    target.Add(mapped);
                }
            }
        }

        /// <summary>
        /// Make a UI-facing copy while normalizing flags (AssignedTo/Accepted) for list views.
        /// </summary>
        public static TaskItem CloneForUi(TaskItem inc, Assignee? assignedTo, bool requestMode)
        {
            return new TaskItem
            {
                Id = inc.Id,
                Title = inc.Title,
                Description = inc.Description,
                Category = inc.Category,
                DueDate = inc.DueDate,
                IsCompleted = inc.IsCompleted,
                CreatedBy = inc.CreatedBy,
                UpdatedAt = inc.UpdatedAt,
                AssignedToUserId = inc.AssignedToUserId,
                AssignedTo = assignedTo ?? inc.AssignedTo,
                Accepted = requestMode ? false : true, // personal list items are implicitly accepted
                IsSuggestion = inc.IsSuggestion,
                MediaPath = inc.MediaPath,
                IsRecurring = inc.IsRecurring,
                Deleted = inc.Deleted
            };
        }
    }
}