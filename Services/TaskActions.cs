using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using TaskMate.Models;
using TaskMate.Models.Enums;
using TaskMate.Data;                // for TaskCollectionHelpers
using System.Windows.Threading;

namespace TaskMate.Services {
	public interface ITaskActions {
		Task AddAsync(
			string title,
			string? description,
			DateTime? due,
			string? category,
			Assignee assignee,
			string myUserId,
			string? partnerId,
			string groupId
		);

		Task DeleteAsync(TaskItem item, string myUserId, string groupId);
		Task AcceptAsync(TaskItem item, string myUserId, string groupId);
		Task DeclineAsync(TaskItem item, string groupId);
		Task UpdateAsync(TaskItem item, string myUserId, string groupId);
	}

	public sealed class TaskActions : ITaskActions {
		private readonly IRequestService _requests;
		private readonly ITaskService _taskService;
		private readonly Dispatcher _ui;
		private readonly IActivityLogService _activity;
		private readonly ISettingsService _settings;

		private ObservableCollection<TaskItem> Tasks => _taskService.Tasks;
		private ObservableCollection<TaskItem> Pending => _taskService.PendingTasks;

		public TaskActions(IRequestService requests, ITaskService taskService, Dispatcher ui, IActivityLogService activity, ISettingsService settings) {
			_requests = requests ?? throw new ArgumentNullException(nameof(requests));
			_taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
			_ui = ui ?? throw new ArgumentNullException(nameof(ui));
			_activity = activity ?? throw new ArgumentNullException(nameof(activity));
			_settings = settings ?? throw new ArgumentNullException(nameof(settings));
		}

		public async Task AddAsync(
			string title,
			string? description,
			DateTime? due,
			string? category,
			Assignee assignee,
			string myUserId,
			string? partnerId,
			string groupId
		) {
			if(string.IsNullOrWhiteSpace(title)) return;
			if(string.IsNullOrWhiteSpace(myUserId)) return;

			var now = DateTime.UtcNow;

			var newTask = new TaskItem {
				Id = Guid.NewGuid(),
				Title = title.Trim(),
				Description = description?.Trim() ?? string.Empty,
				DueDate = due,
				Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
				AssignedTo = assignee,
				CreatedBy = myUserId,
				UpdatedAt = now,
				Accepted = assignee != Assignee.Partner,
				AssignedToUserId = assignee switch {
					Assignee.Me => myUserId,
					Assignee.Partner => partnerId ?? string.Empty,
					_ => string.Empty
				},
			};

			if(assignee == Assignee.Partner && !string.IsNullOrWhiteSpace(partnerId)) {
				await _requests.UpsertRequestAsync(newTask, groupId);

				var pendingUi = TaskCollectionHelpers.CloneForUi(newTask, Assignee.Partner, requestMode: true);
				pendingUi.CanDecide = false;
				_ui.Invoke(() => Pending.Add(pendingUi));

				await _activity.LogAsync(new ActivityEntry {
					Kind = "task.request",
					Actor = "Me",
					Message = $"Requested task “{newTask.Title}” for Partner",
					Timestamp = DateTime.UtcNow
				}, myUserId);
			}
			else {
				await _requests.UpsertPersonalAsync(newTask, myUserId);

				var ownUi = TaskCollectionHelpers.CloneForUi(newTask, Assignee.Me, requestMode: false);
				_ui.Invoke(() => Tasks.Add(ownUi));
				SoundService.PlayTaskCreated();

				await _activity.LogAsync(new ActivityEntry {
					Kind = "task.add",
					Actor = "Me",
					Message = $"{_settings.DisplayName} Created task “{newTask.Title}”",
					Timestamp = DateTime.UtcNow
				}, myUserId);
			}

			if(due.HasValue) {
				var dueLocal = due.Value;
				AppServices.Notifications.ScheduleDueToasts(newTask.Id, newTask.Title, dueLocal, TimeSpan.FromMinutes(15));
			}
		}

		public async Task DeleteAsync(TaskItem item, string myUserId, string groupId) {
			if(item is null) return;

			if(item.AssignedTo == Assignee.Partner) {
				await _requests.DeleteRequestAsync(item.Id, groupId);
				_ui.Invoke(() => Pending.Remove(item));

				await _activity.LogAsync(new ActivityEntry {
					Kind = "task.delete",
					Actor = "Me",
					Message = $"Deleted pending request “{item.Title}”",
					Timestamp = DateTime.UtcNow
				}, myUserId);
			}
			else {
				await _requests.DeletePersonalAsync(item.Id, myUserId);
				_ui.Invoke(() => Tasks.Remove(item));

				await _activity.LogAsync(new ActivityEntry {
					Kind = "task.delete",
					Actor = "Me",
					Message = $"Deleted task “{item.Title}”",
					Timestamp = DateTime.UtcNow
				}, myUserId);
			}
		}

		public async Task AcceptAsync(TaskItem item, string myUserId, string groupId) {
			if(item is null) return;

			await _requests.AcceptRequestAsync(item, groupId, myUserId);
			_ui.Invoke(() => Pending.Remove(item));

			await _activity.LogAsync(new ActivityEntry {
				Kind = "task.accept",
				Actor = "Me",
				Message = $"Accepted task “{item.Title}”",
				Timestamp = DateTime.UtcNow
			}, myUserId);
		}

		public async Task DeclineAsync(TaskItem item, string groupId) {
			if(item is null) return;

			await _requests.DeleteRequestAsync(item.Id, groupId);
			_ui.Invoke(() => Pending.Remove(item));

			// Use current signed-in id from settings/auth instead of FirestoreClient
			var myUserId = AppServices.Settings?.UserId ?? AppServices.Auth?.Uid ?? string.Empty;
			await _activity.LogAsync(new ActivityEntry {
				Kind = "task.decline",
				Actor = "Me",
				Message = $"Declined task “{item.Title}”",
				Timestamp = DateTime.UtcNow
			}, myUserId);
		}

		public async Task UpdateAsync(TaskItem item, string myUserId, string groupId) {
			if(item is null) return;

			item.UpdatedAt = DateTime.UtcNow;

			if(item.AssignedTo == Assignee.Partner && item.Accepted == false) {
				await _requests.UpsertRequestAsync(item, groupId);

				await _activity.LogAsync(new ActivityEntry {
					Kind = "task.request.update",
					Actor = "Me",
					Message = $"Updated pending request “{item.Title}”",
					Timestamp = DateTime.UtcNow
				}, myUserId);
			}
			else {
				await _requests.UpsertPersonalAsync(item, myUserId);

				_ui.Invoke(() => {
					var idx = Tasks.ToList().FindIndex(t => t.Id == item.Id);
					if(idx >= 0) {
						var wasCompleted = Tasks[idx].IsCompleted;
						var updatedUi = TaskCollectionHelpers.CloneForUi(item, item.AssignedTo, requestMode: false);
						Tasks[idx] = updatedUi;

						if(!wasCompleted && item.IsCompleted)
							SoundService.PlayTaskCompleted();
					}
				});

				if(item.IsCompleted) {
					await _activity.LogAsync(new ActivityEntry {
						Kind = "task.complete",
						Actor = "Me",
						Message = $"Completed “{item.Title}”",
						Timestamp = DateTime.UtcNow
					}, myUserId);
				}
				else {
					await _activity.LogAsync(new ActivityEntry {
						Kind = "task.update",
						Actor = "Me",
						Message = $"Updated task “{item.Title}”",
						Timestamp = DateTime.UtcNow
					}, myUserId);
				}
			}

			// keep the existing second write (preserves original behavior)
			await _requests.UpsertPersonalAsync(item, myUserId);

			// (re)apply scheduling
			if(item.IsCompleted || !item.DueDate.HasValue) {
				AppServices.Notifications.CancelDueToasts(item.Id);
			}
			else {
				AppServices.Notifications.ScheduleDueToasts(
					item.Id, item.Title, item.DueDate.Value, TimeSpan.FromMinutes(15));
			}
		}
	}
}
