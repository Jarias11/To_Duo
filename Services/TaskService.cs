namespace TaskMate.Services {
	using System.Collections.ObjectModel;
	using TaskMate.Models;
	using TaskMate.Data;
	using System.Linq;
	using TaskMate.Models.Enums;

	public class TaskService : ITaskService {
		public ObservableCollection<TaskItem> Tasks { get; } = new();
		public ObservableCollection<TaskItem> PendingTasks { get; } = new();
		public ObservableCollection<TaskItem> SentPendingTasks { get; } = new();



		public TaskService() {
			// Allow injecting a mock for tests; default to Firestore implementation

		}

		public Task InitializeAsync(string groupId) {
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
			return Task.CompletedTask;



		}

		public TaskItem AddTask(TaskItem task, string groupId) {
			if(task is null) return null!;

			return task;
		}

		public void DeleteTask(TaskItem task, string groupId) {
			if(task is null) return;

		}

		public void AcceptTask(TaskItem task, string groupId) {
			if(task is null) return;

		}

		public void DeclineTask(TaskItem task, string groupId) {
			if(task is null) return;

		}

		public void SaveAll(string groupId) {
			var snapshot = Tasks.Concat(PendingTasks).ToList();
			_ = Task.Run(() => TaskDataService.SaveTasks(snapshot));
		}


	}
}