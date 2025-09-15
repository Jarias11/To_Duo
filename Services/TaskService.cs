namespace TaskMate.Services {
	using System.Collections.ObjectModel;
	using TaskMate.Models;
	using TaskMate.Data;
	using TaskMate.Data.Repositories;   // if you have FirestoreTaskRepository here
	using TaskMate.Sync;                // if your repo types live here
	using System.Linq;
	using System;
	using TaskMate.Models.Enums;

	public class TaskService : ITaskService {
		private readonly ITaskRepository _cloudRepo;
		public ObservableCollection<TaskItem> Tasks { get; } = new();
		public ObservableCollection<TaskItem> PendingTasks { get; } = new();
		private IDisposable? _listener;
		private readonly object _saveLock = new();
		private CancellationTokenSource? _saveCts;

		public TaskService(ITaskRepository? cloudRepo = null) {
			// Allow injecting a mock for tests; default to Firestore implementation
			
		}

		public async Task InitializeAsync(string groupId) {
			// Load local
			var local = TaskDataService.LoadTasks();
			Tasks.Clear();
			PendingTasks.Clear();


			foreach(var t in local) {
				if(t.AssignedTo == Assignee.Me && !t.Accepted)
					PendingTasks.Add(t);          // tasks I must accept
				else
					Tasks.Add(t);
			}



			_listener?.Dispose();
			_listener = _cloudRepo.ListenAll(groupId, cloudItems => {
				// simple last-write-wins merge
				var byId = Tasks.Concat(PendingTasks).ToDictionary(t => t.Id);

				foreach(var c in cloudItems) {
					if(byId.TryGetValue(c.Id, out var existing)) {
						// newer?
						var oldTs = existing.UpdatedAt ?? DateTime.MinValue;
						var newTs = c.UpdatedAt ?? DateTime.MinValue;
						if(newTs <= oldTs) continue;

						// move between collections if necessary
						if(PendingTasks.Contains(existing)) PendingTasks.Remove(existing);
						if(Tasks.Contains(existing)) Tasks.Remove(existing);
					}

					if(c.AssignedTo == Assignee.Me && !c.Accepted) PendingTasks.Add(c);
					else Tasks.Add(c);
				}
				SaveAll(groupId);
			});



		}

		public TaskItem AddTask(TaskItem task, string groupId) {
			if(task is null) return null!;

			if(task.AssignedTo == Assignee.Me && !task.Accepted) {
				// Incoming-to-me style pending (e.g., if you later support “suggest to self then accept”)
				PendingTasks.Add(task);
			}
			else if(task.AssignedTo == Assignee.Me || task.Accepted) {
				// My own tasks (accepted or not) belong in my main list; unaccepted ones will also be in Pending
				Tasks.Add(task);
			}
			else {
				// Assigned to Partner & not accepted (outgoing request):
				// do NOT show locally in Tasks or Pending; it will appear on the partner’s device.
				// We still persist locally & push to cloud below.
			}

			// Save locally and push to cloud
			var all = TaskDataService.LoadTasks();
			all.Add(task);
			TaskDataService.SaveTasks(all);

			return task;
		}

		public void DeleteTask(TaskItem task, string groupId) {
			if(task is null) return;

			// Remove from UI collections by Id (not by reference)
			var inTasks = Tasks.FirstOrDefault(t => t.Id == task.Id);
			if(inTasks != null) Tasks.Remove(inTasks);

			var inPending = PendingTasks.FirstOrDefault(t => t.Id == task.Id);
			if(inPending != null) PendingTasks.Remove(inPending);

			// Persist locally by Id (you already do this)
			var all = TaskDataService.LoadTasks();
			all.RemoveAll(t => t.Id == task.Id);
			TaskDataService.SaveTasks(all);

		}

		public void AcceptTask(TaskItem task, string groupId) {
			if(task is null) return;

			task.Accepted = true;
			task.UpdatedAt = DateTime.UtcNow;
			if(PendingTasks.Contains(task))
				PendingTasks.Remove(task);

			if(!Tasks.Contains(task))
				Tasks.Add(task);

		}

		public void DeclineTask(TaskItem task, string groupId) {
			if(task is null) return;

			if(PendingTasks.Contains(task)) PendingTasks.Remove(task);
			if(Tasks.Contains(task)) Tasks.Remove(task);

			var all = TaskDataService.LoadTasks();
			all.RemoveAll(t => t.Id == task.Id);
			TaskDataService.SaveTasks(all);
		}

		public void SaveAll(string groupId) {
			var snapshot = Tasks.Concat(PendingTasks).ToList();
			_ = Task.Run(async () => {
				TaskDataService.SaveTasks(snapshot);      // file I/O off UI thread
				await SyncAllToCloudAsync(groupId).ConfigureAwait(false);
			});
		}

		private async Task SyncAllToCloudAsync(string groupId) {
			foreach(var t in Tasks)
				await _cloudRepo.UpsertAsync(groupId, t);
			foreach(var t in PendingTasks)
				await _cloudRepo.UpsertAsync(groupId, t);
		}

	}
}