using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows;
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
        private readonly IRequestService _requestService = new RequestService();
        private readonly IPartnerService _partner;

        private IDisposable? _myHandle, _partnerHandle, _requestsHandle;

        private bool _isPartnerVerified;
        public bool ShowPartnerList => IsPartnerVerified;
        public bool NeedsProfileSetup => _partner.NeedsProfileSetup;
        public string UserId => _partner.UserId;
        private string GroupId => _partner.GroupId;
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
        public event PropertyChangedEventHandler? PropertyChanged;

        /* MOVE THIS WHEN FEATURE IS WORKING */
        private string? _newDisplayName;
        public string? NewDisplayName {
            get => _newDisplayName;
            set {
                if(_newDisplayName == value) return;
                _newDisplayName = value;
                OnPropertyChanged(nameof(NewDisplayName));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public ICommand SaveDisplayNameCommand { get; }
        public ICommand AddTaskCommand { get; }
        public ICommand DeleteTaskCommand { get; }
        public ICommand AcceptTaskCommand { get; }
        public ICommand DeclineTaskCommand { get; }

        public MainViewModel() : this(new TaskService(), new PartnerService()) { }

        public MainViewModel(ITaskService taskService, IPartnerService partnerService) {

            _taskService = taskService;
            _partner = partnerService;
            OnPropertyChanged(nameof(PartnerId));
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
            SaveDisplayNameCommand = new RelayCommand(
                _ => SaveDisplayName(),
                _ => !string.IsNullOrWhiteSpace(NewDisplayName)
        );
            _partner.PartnerChanged += async () => {
                // This mirrors your current flow: recompute, save, and reattach listeners
                await ReloadForNewPartnerAsync();
                OnPropertyChanged(nameof(PartnerId));
                OnPropertyChanged(nameof(NeedsProfileSetup));
            };

            if(NeedsProfileSetup) NewDisplayName = string.Empty;
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
                await _requestService.UpsertRequestAsync(task, GroupId);
                // Optimistic UI: show in Pending immediately (optional)
                PendingTasks.Add(TaskCollectionHelpers.CloneForUi(task, "Partner", requestMode: true));
            }
            else {
                // Write to personal list
                await _requestService.UpsertPersonalAsync(task, myId);
                // Optimistic UI: add to Tasks as "Me" so filters pick it up immediately
                Tasks.Add(TaskCollectionHelpers.CloneForUi(task, "Me", requestMode: false));
            }

            // Persist local cache (your TaskService also does saving; we can double-save safely)
            TaskDataService.SaveTasks(Tasks.ToList());

            // Clear inputs
            NewTaskTitle = string.Empty;
            NewTaskDueDate = null;
            NewTaskDescription = string.Empty;
        }
        public void SaveTasks() => _taskService.SaveAll(GroupId);

        private void SaveDisplayName() {
            if(!string.IsNullOrWhiteSpace(NewDisplayName)) {
                _partner.SaveDisplayName(NewDisplayName!);
                OnPropertyChanged(nameof(NeedsProfileSetup));
            }
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
            _myHandle = _requestService.ListenPersonal(myId, cloud => {
                Application.Current.Dispatcher.Invoke(() => {
                    TaskCollectionHelpers.UpsertInto(Tasks, cloud, assignedTo: "Me", ownerUserId: myId);
                    TaskDataService.SaveTasks(Tasks.ToList());
                    OnPropertyChanged(nameof(Tasks));
                    MyTasksView.Refresh();
                    PartnerTasksView.Refresh();
                });
            });

            _partnerHandle = _requestService.ListenPersonal(partnerId, cloud => {
                Application.Current.Dispatcher.Invoke(() => {

                    IsPartnerVerified = !string.IsNullOrWhiteSpace(partnerId);
                    TaskCollectionHelpers.UpsertInto(Tasks, cloud, assignedTo: "Partner", ownerUserId: partnerId);
                    OnPropertyChanged(nameof(Tasks));
                    PartnerTasksView.Refresh();
                });
            });

            _requestsHandle = _requestService.ListenPersonal(groupId, cloud => {
                Application.Current.Dispatcher.Invoke(() => {
                    TaskCollectionHelpers.ReplaceAll(PendingTasks, cloud, assignedTo: "Partner", requestMode: true);
                    OnPropertyChanged(nameof(PendingTasks));
                    // If any view is bound to PendingTasks, refresh it here
                });
            });
        }
        private async Task ReloadForNewPartnerAsync() {
            try {
                // Stop old listeners
                _myHandle?.Dispose();
                _partnerHandle?.Dispose();
                _requestsHandle?.Dispose();
                _myHandle = _partnerHandle = _requestsHandle = null;

                // Clear partner-sourced UI
                var mine = Tasks.Where(t => t.AssignedTo == "Me").ToList();
                Tasks.Clear();
                foreach(var t in mine) Tasks.Add(t);
                PendingTasks.Clear();

                // Re-attach listeners for the new PartnerId/GroupId
                await InitLiveAsync();

                // Refresh the views
                Application.Current.Dispatcher.Invoke(() => {
                    OnPropertyChanged(nameof(Tasks));
                    OnPropertyChanged(nameof(PendingTasks));
                    MyTasksView.Refresh();
                    PartnerTasksView.Refresh();
                });
            }
            catch(Exception ex) {
                Console.WriteLine($"ReloadForNewPartnerAsync error: {ex}");
            }
        }



        // Accept task that lives in Requests (PendingTasks)
        private async void AcceptTaskInternal(TaskItem t) {
            if(t is null) return;
            await _requestService.AcceptRequestAsync(t, GroupId, UserId);
            // UI will update via listeners; remove from Pending optimistically:
            PendingTasks.Remove(t);
        }

        // Decline (delete from requests)
        private async void DeclineTaskInternal(TaskItem t) {
            if(t is null) return;
            await _requestService.DeleteRequestAsync(t.Id, GroupId);
            PendingTasks.Remove(t);
        }

        // Delete from my personal list
        private async void DeleteTaskInternal(TaskItem t) {
            if(t is null) return;

            if(t.AssignedTo == "Partner") {
                // If this is a pending request shown in PendingTasks, delete there:
                await _requestService.DeleteRequestAsync(t.Id, GroupId);
                PendingTasks.Remove(t);
            }
            else {
                await _requestService.DeletePersonalAsync(t.Id, UserId);
                Tasks.Remove(t);
            }
            TaskDataService.SaveTasks(Tasks.ToList());
        }



        public void Dispose() {
            _myHandle?.Dispose();
            _partnerHandle?.Dispose();
            _requestsHandle?.Dispose();
        }

        public string? PartnerId {
            get => _partner.PartnerId;
            set {
                if(_partner.PartnerId == value?.Trim()) return;
                _partner.PartnerId = value;           // service saves + recomputes + raises PartnerChanged
                OnPropertyChanged(nameof(PartnerId)); // update bindings
                                                      // no RecomputeGroupId(), no ReloadForNewPartnerAsync() here
            }
        }
        public bool IsPartnerVerified {
            get => _isPartnerVerified;
            private set {
                if(_isPartnerVerified == value) return;
                _isPartnerVerified = value;
                OnPropertyChanged(nameof(IsPartnerVerified));
                OnPropertyChanged(nameof(ShowPartnerList));
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