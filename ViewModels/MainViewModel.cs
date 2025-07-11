using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TaskMate.Models;
using TaskMate.Data;

namespace TaskMate.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<TaskItem> Tasks { get; set; } = new ObservableCollection<TaskItem>();



        private UserSettings _userSettings;
        public string UserId => _userSettings.UserId;
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
                Accepted = assignee == "Partner" ? false : true // ✅ THIS NOW WORKS
            };

            if (task.AssignedTo == "Me" || task.Accepted)
                Tasks.Add(task);
            else
                PendingTasks.Add(task);

            // Save the task regardless so it's synced/shared
            var allTasks = TaskDataService.LoadTasks();
            allTasks.Add(task);
            TaskDataService.SaveTasks(allTasks);

            NewTaskTitle = string.Empty;
            NewTaskDueDate = null;
            NewTaskDescription = string.Empty;
        }
        private void DeleteTask(TaskItem task)
        {
            if (Tasks.Contains(task))
            {
                Tasks.Remove(task);
                TaskDataService.SaveTasks(new List<TaskItem>(Tasks));
            }
        }
        public void SaveTasks()
        {
            TaskDataService.SaveTasks(new List<TaskItem>(Tasks));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void AcceptTask(TaskItem task)
        {
            task.Accepted = true;

            // 🔼 Add to main task list if not already present
            if (!Tasks.Contains(task))
                Tasks.Add(task);

            RefreshPendingTasks(); // ✅ Removes it from PendingTasks
            TaskDataService.SaveTasks(Tasks); // ✅ Persist changes
        }

        private void RefreshPendingTasks()
        {
            PendingTasks.Clear();
            foreach (var task in Tasks)
            {
                if (task.AssignedTo == "Me" && !task.Accepted)
                    PendingTasks.Add(task);
            }
        }


    }


}