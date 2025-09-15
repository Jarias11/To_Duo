using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using TaskMate.Models;
using TaskMate.Models.Enums;
using TaskMate.Data;                // for TaskCollectionHelpers
using TaskMate.Services;
using System.Windows.Threading;            // for IRequestService, ITaskService

namespace TaskMate.Services {
	public interface ITaskActions {
		/// <summary>
		/// Add a new task. For Partner-assigned tasks, writes as a request (group path);
		/// for Me-assigned tasks, writes to personal. Also performs optimistic UI updates.
		/// </summary>
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

		/// <summary>
		/// Delete a task from the correct store (request vs. personal) and remove from UI lists.
		/// </summary>
		Task DeleteAsync(TaskItem item, string myUserId, string groupId);

		/// <summary>
		/// Accept a pending partner task request (moves from requests into the live list on the backend).
		/// Optimistically removes from Pending UI.
		/// </summary>
		Task AcceptAsync(TaskItem item, string myUserId, string groupId);

		/// <summary>
		/// Decline a pending partner task request (delete request) and remove from Pending UI.
		/// </summary>
		Task DeclineAsync(TaskItem item, string groupId);
	}

	/// <summary>
	/// Orchestrates "what to write where" and does optimistic UI updates against the collections
	/// owned by ITaskService. Keeps Firestore boundary decisions out of the ViewModel.
	/// </summary>
	public sealed class TaskActions : ITaskActions {
		private readonly IRequestService _requests;
		private readonly ITaskService _taskService;
		private readonly Dispatcher _ui;

		// We purposefully use the collections exposed by the task service
		// so that VM filters (CollectionViewSource) see the updates immediately.
		private ObservableCollection<TaskItem> Tasks => _taskService.Tasks;
		private ObservableCollection<TaskItem> Pending => _taskService.PendingTasks;

		public TaskActions(IRequestService requests, ITaskService taskService, Dispatcher ui) {
			_requests = requests ?? throw new ArgumentNullException(nameof(requests));
			_taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
			_ui = ui ?? throw new ArgumentNullException(nameof(ui));
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
				AssignedToUserId = (assignee == Assignee.Me) ? myUserId : partnerId
			};

			if(assignee == Assignee.Partner && !string.IsNullOrWhiteSpace(partnerId)) {
				await _requests.UpsertRequestAsync(newTask, groupId);
				var pendingUi = TaskCollectionHelpers.CloneForUi(newTask, Assignee.Partner, requestMode: true);
				_ui.Invoke(() => Pending.Add(pendingUi));
			}
			else {
				await _requests.UpsertPersonalAsync(newTask, myUserId);
				var ownUi = TaskCollectionHelpers.CloneForUi(newTask, Assignee.Me, requestMode: false);
				_ui.Invoke(() => Tasks.Add(ownUi));
			}
		}

		public async Task DeleteAsync(TaskItem item, string myUserId, string groupId) {
			if(item is null) return;

			if(item.AssignedTo == Assignee.Partner) {
				await _requests.DeleteRequestAsync(item.Id, groupId);
				_ui.Invoke(() => Pending.Remove(item));
			}
			else {
				await _requests.DeletePersonalAsync(item.Id, myUserId);
				_ui.Invoke(() => Tasks.Remove(item));
			}
		}

		public async Task AcceptAsync(TaskItem item, string myUserId, string groupId) {
			if(item is null) return;
			await _requests.AcceptRequestAsync(item, groupId, myUserId);
			_ui.Invoke(() => Pending.Remove(item));
		}

		public async Task DeclineAsync(TaskItem item, string groupId) {
			if(item is null) return;
			await _requests.DeleteRequestAsync(item.Id, groupId);
			_ui.Invoke(() => Pending.Remove(item));
		}
	}
}