using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TaskMate.Models;
using TaskMate.Data;
using TaskMate.Data.Repositories;
using TaskMate.Sync;

namespace TaskMate.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<TaskItem> Tasks { get; set; } = new ObservableCollection<TaskItem>();



        private UserSettings _userSettings;
        private readonly ITaskRepository _cloudRepo = new FirestoreTaskRepository();
        public string UserId => _userSettings.UserId;


        private string GroupId => string.Join("-",
    new[] { UserId ?? "", PartnerId ?? "" }.OrderBy(s => s));
        public string PartnerId
        {
            get => _userSettings.PartnerId;
            set
            {
                _userSettings.PartnerId = value;
                UserSettings.Save(_userSettings);
                OnPropertyChanged();
            }
        }

        private string _newTaskTitle;
        public string NewTaskTitle
        {
            get => _newTaskTitle;
            set
            {
                _newTaskTitle = value;
                OnPropertyChanged();
            }
        }

        private string _newTaskDescription;
        public string NewTaskDescription
        {
            get => _newTaskDescription;
            set
            {
                _newTaskDescription = value;
                OnPropertyChanged();
            }
        }

        private DateTime? _newTaskDueDate;
        public DateTime? NewTaskDueDate
        {
            get => _newTaskDueDate;
            set
            {
                _newTaskDueDate = value;
                OnPropertyChanged();
            }
        }
        public ObservableCollection<TaskItem> PendingTasks { get; set; } = new();
        public ObservableCollection<string> Categories { get; set; } = new ObservableCollection<string>
        {
            "General", "Chores", "Work", "Fun", "Urgent"
        };

        private string? _newTaskCategory;
        public string? NewTaskCategory
        {
            get => _newTaskCategory;
            set
            {
                _newTaskCategory = value;
                OnPropertyChanged();
            }
        }
        private string? _newTaskAssignee = "Me";
        public string? NewTaskAssignee
        {
            get => _newTaskAssignee;
            set
            {
                _newTaskAssignee = value;
                OnPropertyChanged();
            }
        }

        public ICommand AddTaskCommand { get; }
        public ICommand DeleteTaskCommand { get; }

        public ICommand AcceptTaskCommand { get; }

        public ICommand SavePartnerCommand { get; }
        public ICommand DeclineTaskCommand { get; }

        private void SavePartner()
        {
            UserSettings.Save(_userSettings);
            // Optional: refresh or sync partner tasks
        }


        public MainViewModel()
        {
            _userSettings = UserSettings.Load();
            SavePartnerCommand = new RelayCommand(_ => SavePartner());

            // Initialize collections
            Tasks = new ObservableCollection<TaskItem>();
            PendingTasks = new ObservableCollection<TaskItem>();

            // Load tasks from JSON
            var loadedTasks = TaskDataService.LoadTasks();

            foreach (var task in loadedTasks)
            {
                // Only add accepted tasks or those not assigned to me
                if (task.Accepted || task.AssignedTo != "Me")
                    Tasks.Add(task);

                // Unaccepted tasks assigned to me go into pending
                if (task.AssignedTo == "Partner" && !task.Accepted)
                    PendingTasks.Add(task);
            }

            // Initialize commands
            AddTaskCommand = new RelayCommand(_ => AddTask());
            DeleteTaskCommand = new RelayCommand<TaskItem>(DeleteTask);
            AcceptTaskCommand = new RelayCommand<TaskItem>(AcceptTask);
            DeclineTaskCommand = new RelayCommand<TaskItem>(DeclineTask);

            _ = MergeCloudIntoLocalAsync(); // fire-and-forget on UI context
        }

        private void AddTask()
        {
            if (string.IsNullOrWhiteSpace(NewTaskTitle)) return;

            // Normalize assignee first
            string assignee = NewTaskAssignee?.ToString() == "System.Windows.Controls.ComboBoxItem: Me" ? "Me" :
                            NewTaskAssignee?.ToString() == "System.Windows.Controls.ComboBoxItem: Partner" ? "Partner" :
                            NewTaskAssignee?.ToString();

            var task = new TaskItem
            {
                Title = NewTaskTitle,
                DueDate = NewTaskDueDate,
                Category = NewTaskCategory,
                AssignedTo = assignee,
                Description = NewTaskDescription,
                CreatedBy = UserId,
                Accepted = assignee != "Partner" // ✅ THIS NOW WORKS
            };

            if (task.AssignedTo == "Me" || task.Accepted)
                Tasks.Add(task);
            else
                PendingTasks.Add(task);

            // Save the task regardless so it's synced/shared
            var allTasks = TaskDataService.LoadTasks();
            allTasks.Add(task);
            TaskDataService.SaveTasks(allTasks);
            _ = _cloudRepo.UpsertAsync(GroupId, task);

            NewTaskTitle = string.Empty;
            NewTaskDueDate = null;
            NewTaskDescription = string.Empty;
        }
        private void DeleteTask(TaskItem task)
        {
            if (Tasks.Contains(task))
            {
                Tasks.Remove(task);
                SaveAll();
                _ = _cloudRepo.DeleteAsync(GroupId, task.Id);
            }
        }
        private void SaveAll()
        {
            var all = new List<TaskItem>(Tasks);
            foreach (var t in PendingTasks)
                if (!all.Contains(t)) all.Add(t);

            TaskDataService.SaveTasks(all);
            _ = SyncAllToCloudAsync();
        }
        public void SaveTasks() => SaveAll();

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void AcceptTask(TaskItem task)
        {
            if (task is null) return;

            task.Accepted = true;

            // If it was rendered from Pending, drop it there
            if (PendingTasks.Contains(task))
                PendingTasks.Remove(task);

            //Add to main task list if not already present
            if (!Tasks.Contains(task))
                Tasks.Add(task);

            SaveAll(); //Persist changes
            _ = _cloudRepo.UpsertAsync(GroupId, task);
        }

        private void RefreshPendingTasks()
        {
            PendingTasks.Clear();
            foreach (var t in Tasks)
                if (t.AssignedTo == "Partner" && !t.Accepted)
                    PendingTasks.Add(t);

            // If you also keep some items only in PendingTasks, keep them:
            foreach (var t in PendingTasks.ToList())
                if (!(t.AssignedTo == "Partner" && !t.Accepted))
                    PendingTasks.Remove(t);
        }
        private void DeclineTask(TaskItem task)
        {
            // Hard delete for now:
            if (PendingTasks.Contains(task)) PendingTasks.Remove(task);

            // Also remove from Tasks if it somehow exists there
            if (Tasks.Contains(task)) Tasks.Remove(task);

            // Persist: load all, remove by Id, save back
            var all = TaskDataService.LoadTasks();
            all.RemoveAll(t => t.Id == task.Id);
            TaskDataService.SaveTasks(all);

            RefreshPendingTasks();
        }
        private async Task SyncAllToCloudAsync()
        {
            // push both lists; it's fine for now
            foreach (var t in Tasks)
                await _cloudRepo.UpsertAsync(GroupId, t);
            foreach (var t in PendingTasks)
                await _cloudRepo.UpsertAsync(GroupId, t);
        }
private async Task MergeCloudIntoLocalAsync()
{
    try
    {
        var cloud = await _cloudRepo.LoadAllAsync(GroupId);
        var byId = Tasks.ToDictionary(t => t.Id);

        foreach (var c in cloud)
        {
            if (byId.ContainsKey(c.Id)) continue;

            if (c.AssignedTo == "Partner" && !c.Accepted)
                PendingTasks.Add(c);
            else
                Tasks.Add(c);
        }

        TaskDataService.SaveTasks(new List<TaskItem>(Tasks)); // persist locally
    }
    catch (Exception)
    {
        // Optional: log or ignore (offline, bad creds, etc.)
    }
}
    }


}