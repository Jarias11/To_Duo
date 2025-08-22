using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Data;
using TaskMate.Models;
using TaskMate.Data;
using TaskMate.Data.Repositories;
using TaskMate.Sync;
using TaskMate.Services;

namespace TaskMate.ViewModels {
    public class MainViewModel : INotifyPropertyChanged {

        private readonly ITaskService _taskService;
        private UserSettings _userSettings;
        private string GroupId => string.Join("-", new[] { UserId ?? "", PartnerId ?? "" }.OrderBy(s => s));
        private string _newTaskTitle = string.Empty;
        private string _newTaskDescription = string.Empty;
        private DateTime? _newTaskDueDate;
        private string? _newTaskCategory;
        private string? _newTaskAssignee = "Me";
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public ICollectionView MyTasksView { get; }
        public ICollectionView PartnerTasksView { get; }
        public ObservableCollection<TaskItem> Tasks => _taskService.Tasks;
        public ObservableCollection<TaskItem> PendingTasks => _taskService.PendingTasks;
        public ObservableCollection<string> Categories { get; set; } = [
            "General", "Chores", "Work", "Fun", "Urgent"
        ];
        public string UserId => _userSettings.UserId;
        public event PropertyChangedEventHandler? PropertyChanged;

        public ICommand AddTaskCommand { get; }
        public ICommand DeleteTaskCommand { get; }
        public ICommand AcceptTaskCommand { get; }
        public ICommand SavePartnerCommand { get; }
        public ICommand DeclineTaskCommand { get; }

        public MainViewModel() : this(new TaskService()) { }

        public MainViewModel(ITaskService taskService) {

            _taskService = taskService;
            _userSettings = UserSettings.Load();
            MyTasksView = CollectionViewSource.GetDefaultView(Tasks);
            MyTasksView.Filter = o => {
                var t = o as TaskItem;
                return t != null && t.AssignedTo == "Me"; // your current field
            };
            PartnerTasksView = new CollectionViewSource { Source = Tasks }.View;
            PartnerTasksView.Filter = o => {
                var t = o as TaskItem;
                return t != null && t.AssignedTo == "Partner";
            };

            // Initialize commands
            AddTaskCommand = new RelayCommand(_ => AddTask());
            DeleteTaskCommand = new RelayCommand<TaskItem>(t => _taskService.DeleteTask(t!, GroupId));
            AcceptTaskCommand = new RelayCommand<TaskItem>(t => _taskService.AcceptTask(t!, GroupId));
            DeclineTaskCommand = new RelayCommand<TaskItem>(t => _taskService.DeclineTask(t!, GroupId));
            SavePartnerCommand = new RelayCommand(_ => SavePartner());
            // Load tasks from JSON
            var loadedTasks = TaskDataService.LoadTasks();


            _ = _taskService.InitializeAsync(GroupId); // fire-and-forget on UI context
        }
        private void AddTask() {
            if(string.IsNullOrWhiteSpace(NewTaskTitle)) return;
            // Normalize assignee first
            string? assignee =
                NewTaskAssignee?.ToString() == "System.Windows.Controls.ComboBoxItem: Me" ? "Me" :
                NewTaskAssignee?.ToString() == "System.Windows.Controls.ComboBoxItem: Partner" ? "Partner" :
                NewTaskAssignee?.ToString();
            var task = new TaskItem {
                Title = NewTaskTitle,
                DueDate = NewTaskDueDate,
                Category = NewTaskCategory,
                AssignedTo = assignee,
                Description = NewTaskDescription,
                CreatedBy = UserId,
                Accepted = assignee != "Partner",
                UpdatedAt = DateTime.UtcNow,
                AssignedToUserId = (assignee == "Me") ? UserId : PartnerId
            };

            _taskService.AddTask(task, GroupId);

            NewTaskTitle = string.Empty;
            NewTaskDueDate = null;
            NewTaskDescription = string.Empty;
        }

        public void SaveTasks() => _taskService.SaveAll(GroupId);

        private void SavePartner() {
            UserSettings.Save(_userSettings);
            // Optional: refresh or sync partner tasks
            // If partner changed, re-init to merge that group
            _ = _taskService.InitializeAsync(GroupId);
        }
        public string PartnerId {
            get => _userSettings.PartnerId;
            set {
                _userSettings.PartnerId = value;
                UserSettings.Save(_userSettings);
                OnPropertyChanged();
            }
        }
        public string NewTaskTitle {
            get => _newTaskTitle;
            set {
                _newTaskTitle = value;
                OnPropertyChanged();
            }
        }
        public string NewTaskDescription {
            get => _newTaskDescription;
            set {
                _newTaskDescription = value;
                OnPropertyChanged();
            }
        }
        public DateTime? NewTaskDueDate {
            get => _newTaskDueDate;
            set {
                _newTaskDueDate = value;
                OnPropertyChanged();
            }
        }
        public string? NewTaskCategory {
            get => _newTaskCategory;
            set {
                _newTaskCategory = value;
                OnPropertyChanged();
            }
        }
        public string? NewTaskAssignee {
            get => _newTaskAssignee;
            set {
                _newTaskAssignee = value;
                OnPropertyChanged();
            }
        }

    }
}