using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows;                 // <-- gives you Application.Current
using System.Windows.Threading;
using System.Windows.Data;
using TaskMate.Models;
using TaskMate.Data;
using TaskMate.Data.Repositories;
using TaskMate.Sync;
using TaskMate.Services;

namespace TaskMate.ViewModels {
    public class MainViewModel : INotifyPropertyChanged, IDisposable {

        private readonly ITaskService _taskService;
        private readonly IPersonalTaskRepo _personalRepo = new FirestorePersonalTaskRepository();
        private readonly IRequestRepo _requestRepo = new FirestoreRequestRepository();
        private UserSettings _userSettings;
        private IDisposable? _myHandle, _partnerHandle, _requestsHandle;
        private string GroupId => string.IsNullOrWhiteSpace(PartnerId)
            ? ""
            : string.Join("-", new[] { UserId ?? "", PartnerId ?? "" }.OrderBy(s => s));
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
            DeleteTaskCommand = new RelayCommand<TaskItem>(t => DeleteTaskInternal(t!));
            AcceptTaskCommand = new RelayCommand<TaskItem>(t => AcceptTaskInternal(t!));
            DeclineTaskCommand = new RelayCommand<TaskItem>(t => DeclineTaskInternal(t!));
            SavePartnerCommand = new RelayCommand(_ => SavePartner());
            // Load tasks from JSON
            var loadedTasks = TaskDataService.LoadTasks();


            //_ = _taskService.InitializeAsync(GroupId); // fire-and-forget on UI context
            _ = InitLiveAsync(); // fire-and-forget
        }
        private async void AddTask() {
            if(string.IsNullOrWhiteSpace(NewTaskTitle)) return;

            // Normalize assignee first
            string? assignee =
                NewTaskAssignee?.ToString() == "System.Windows.Controls.ComboBoxItem: Me" ? "Me" :
                NewTaskAssignee?.ToString() == "System.Windows.Controls.ComboBoxItem: Partner" ? "Partner" :
                NewTaskAssignee?.ToString();

            var myId = UserId;
            var now = DateTime.UtcNow;
            var task = new TaskItem {
                Id = Guid.NewGuid(),
                Title = NewTaskTitle,
                DueDate = NewTaskDueDate,
                Category = NewTaskCategory,
                AssignedTo = assignee,
                Description = NewTaskDescription,
                CreatedBy = myId,
                UpdatedAt = now,
                Accepted = assignee != "Partner",
                AssignedToUserId = (assignee == "Me") ? myId : PartnerId
            };

            if(assignee == "Partner" && !string.IsNullOrWhiteSpace(PartnerId)) {
                // Write to requests (group path)
                await _requestRepo.UpsertAsync(task, GroupId);
                // Optimistic UI: show in Pending immediately (optional)
                PendingTasks.Add(CloneForUi(task, "Partner", requestMode: true));
            }
            else {
                // Write to personal list
                await _personalRepo.UpsertAsync(task, myId);
                // Optimistic UI: add to Tasks as "Me" so filters pick it up immediately
                Tasks.Add(CloneForUi(task, "Me", requestMode: false));
            }

            // Persist local cache (your TaskService also does saving; we can double-save safely)
            TaskDataService.SaveTasks(Tasks.ToList());

            // Clear inputs
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

        private async Task InitLiveAsync() {
            // 1) Ensure we have IDs + Firestore ready
            await FirestoreClient.InitializeAsync();
            var myId = FirestoreClient.CurrentUserId;
            var partnerId = PartnerId;           // from your settings-backed property
            var groupId = GroupId;               // you compute GroupId already

            // 2) Show local cache instantly (you already load JSON in TaskService; we’ll leave it)
            //    If you want to reload into UI immediately, it's already available via _taskService.Tasks

            // 3) Start live listener for *my* personal list
            _myHandle = _personalRepo.Listen(myId, cloud => {
                Application.Current.Dispatcher.Invoke(() => {
                    UpsertInto(Tasks, cloud, assignedTo: "Me", ownerUserId: myId);
                    TaskDataService.SaveTasks(Tasks.ToList());
                    OnPropertyChanged(nameof(Tasks));
                    MyTasksView.Refresh();
                    PartnerTasksView.Refresh();
                });
            });

            _partnerHandle = _personalRepo.Listen(partnerId, cloud => {
                Application.Current.Dispatcher.Invoke(() => {
                    // however you update the partner view
                    UpsertInto(Tasks, cloud, assignedTo: "Partner", ownerUserId: partnerId);
                    OnPropertyChanged(nameof(Tasks));
                    PartnerTasksView.Refresh();
                });
            });

            _requestsHandle = _requestRepo.Listen(groupId, cloud => {
                Application.Current.Dispatcher.Invoke(() => {
                    ReplaceAll(PendingTasks, cloud, assignedTo: "Partner", requestMode: true);
                    OnPropertyChanged(nameof(PendingTasks));
                    // If any view is bound to PendingTasks, refresh it here
                });
            });
        }

        private static void ReplaceAll(ObservableCollection<TaskItem> target, IList<TaskItem> incoming, string? assignedTo = null, bool requestMode = false) {
            target.Clear();
            foreach(var inc in incoming) {
                var copy = CloneForUi(inc, assignedTo, requestMode);
                target.Add(copy);
            }
        }

        private static void UpsertInto(ObservableCollection<TaskItem> target, IList<TaskItem> incoming, string assignedTo, string ownerUserId) {
            // Build index for faster upsert
            var index = target.ToDictionary(t => t.Id);

            foreach(var inc in incoming) {
                var mapped = CloneForUi(inc, assignedTo, requestMode: false);
                if(index.TryGetValue(mapped.Id, out var existing)) {
                    var oldTime = existing.UpdatedAt ?? DateTime.MinValue;
                    var newTime = mapped.UpdatedAt ?? DateTime.MinValue;
                    if(newTime > oldTime) {
                        var pos = target.IndexOf(existing);
                        target[pos] = mapped;
                    }
                }
                else {
                    target.Add(mapped);
                }
            }
        }
        private static TaskItem CloneForUi(TaskItem inc, string? assignedTo, bool requestMode) {
            // We preserve your current UI fields: AssignedTo ("Me"/"Partner") and Accepted flag.
            // Requests show Accepted=false until moved.
            return new TaskItem {
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
                Accepted = requestMode ? false : true, // Personal list items are implicitly accepted
                IsSuggestion = inc.IsSuggestion,
                MediaPath = inc.MediaPath,
                IsRecurring = inc.IsRecurring,
                Deleted = inc.Deleted
            };
        }



        // Accept task that lives in Requests (PendingTasks)
        private async void AcceptTaskInternal(TaskItem t) {
            if(t is null) return;
            await _requestRepo.AcceptAsync(t, GroupId, UserId);
            // UI will update via listeners; remove from Pending optimistically:
            PendingTasks.Remove(t);
        }

        // Decline (delete from requests)
        private async void DeclineTaskInternal(TaskItem t) {
            if(t is null) return;
            await _requestRepo.DeleteAsync(t.Id, GroupId);
            PendingTasks.Remove(t);
        }

        // Delete from my personal list
        private async void DeleteTaskInternal(TaskItem t) {
            if(t is null) return;

            if(t.AssignedTo == "Partner") {
                // If this is a pending request shown in PendingTasks, delete there:
                await _requestRepo.DeleteAsync(t.Id, GroupId);
                PendingTasks.Remove(t);
            }
            else {
                await _personalRepo.DeleteAsync(t.Id, UserId);
                Tasks.Remove(t);
            }
            TaskDataService.SaveTasks(Tasks.ToList());
        }



        public void Dispose() {
            _myHandle?.Dispose();
            _partnerHandle?.Dispose();
            _requestsHandle?.Dispose();
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