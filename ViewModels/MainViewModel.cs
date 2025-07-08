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

        public MainViewModel()
        {
            // Load tasks from JSON at startup
            var loadedTasks = TaskDataService.LoadTasks();
            foreach (var task in loadedTasks)
                Tasks.Add(task);    
            Tasks = new ObservableCollection<TaskItem>(TaskDataService.LoadTasks());
            AddTaskCommand = new RelayCommand(_ => AddTask());
            DeleteTaskCommand = new RelayCommand<TaskItem>(DeleteTask);
        }

        private void AddTask()
        {
            if (string.IsNullOrWhiteSpace(NewTaskTitle)) return;

            var task = new TaskItem
            {
                Title = NewTaskTitle,
                DueDate = NewTaskDueDate,
                Category = NewTaskCategory,
                AssignedTo = NewTaskAssignee?.ToString() == "System.Windows.Controls.ComboBoxItem: Me" ? "Me" :
             NewTaskAssignee?.ToString() == "System.Windows.Controls.ComboBoxItem: Partner" ? "Partner" :
             NewTaskAssignee?.ToString()  // You can change this logic later
            };

            Tasks.Add(task);
            TaskDataService.SaveTasks(Tasks);
            NewTaskTitle = string.Empty;
            NewTaskDueDate = null;
              

            NewTaskTitle = string.Empty;
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
    }
}