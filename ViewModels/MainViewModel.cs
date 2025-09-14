using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Data;
using TaskMate.Models;
using TaskMate.Data;
using TaskMate.Sync;
using TaskMate.Services;

namespace TaskMate.ViewModels {
    public class MainViewModel : INotifyPropertyChanged, IDisposable {
        //Readonly services
        private readonly ITaskService _taskService;
        private readonly IRequestService _requestService = new RequestService();
        private readonly IPartnerService _partner;
        private readonly IPartnerRequestService _partnerReqs = new PartnerRequestService();
        private readonly IThemeService _themeService;
        private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

        // Firestore listener handles
        private IDisposable? _myHandle, _partnerHandle, _requestsHandle, _incomingReqsHandle, _outgoingReqsHandle;

        // Backing fields + properties
        private DateTime _now = DateTime.Now;
        private UserSettings Settings { get; } = UserSettings.Load();
        private void SaveSettings() => UserSettings.Save(Settings);
        private string? _newDisplayName;
        private bool _isPartnerVerified;
        private bool _hasPendingOutgoing;
        public bool ShowPartnerList => IsPartnerVerified;
        public bool NeedsProfileSetup => _partner.NeedsProfileSetup;
        public string UserId => _partner.UserId;
        public string? PartnerId => _partner.PartnerId;
        public string? DisplayName => _partner.DisplayName;
        private string? _enteredPartnerCode;
        private string _themeButtonText = "Dark Mode"; // when in Light, offer Dark
        public string ConnectionSummary =>
            IsPartnerVerified ? $"Connected to: {PartnerId}" : "No partner connected yet";
        private string GroupId => _partner.GroupId;
        private string _newTaskTitle = string.Empty;
        private string _newTaskDescription = string.Empty;
        private DateTime? _newTaskDueDate;
        private string? _newTaskCategory;
        private string? _newTaskAssignee = "Me";
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));


        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler? PropertyChanged;
        public ICollectionView MyTasksView { get; }
        public ICollectionView PartnerTasksView { get; }
        public ObservableCollection<TaskItem> Tasks => _taskService.Tasks;
        public ObservableCollection<TaskItem> PendingTasks => _taskService.PendingTasks;
        public ObservableCollection<string> Categories { get; set; } = [
            "General", "Chores", "Work", "Fun", "Urgent"
        ];
        public ObservableCollection<PartnerRequest> IncomingPartnerRequests { get; } = new();
        public ObservableCollection<PartnerRequest> OutgoingPartnerRequests { get; } = new();

        // Commands

        public RelayCommand ToggleThemeCommand { get; }
        public ICommand SendPartnerRequestCommand { get; }
        public ICommand AcceptPartnerInviteCommand { get; }
        public ICommand DeclinePartnerInviteCommand { get; }
        public ICommand CancelPartnerRequestCommand { get; }
        public ICommand DisconnectPartnerCommand { get; }
        public ICommand SaveDisplayNameCommand { get; }
        public ICommand AddTaskCommand { get; }
        public ICommand DeleteTaskCommand { get; }
        public ICommand AcceptTaskCommand { get; }
        public ICommand DeclineTaskCommand { get; }

        public MainViewModel() : this(new TaskService(), new PartnerService(), new ThemeService()) { }

        public MainViewModel(ITaskService taskService, IPartnerService partnerService, IThemeService? themeService = null) {

            _taskService = taskService;
            _partner = partnerService;
            _themeService = themeService ?? new ThemeService();
            _clock.Tick += (_, __) => Now = DateTime.Now;
            _clock.Start();
            OnPropertyChanged(nameof(PartnerId));
            MyTasksView = CollectionViewSource.GetDefaultView(Tasks);
            IsPartnerVerified = !string.IsNullOrWhiteSpace(PartnerId);
            MyTasksView.Filter = o => {
                var t = o as TaskItem;
                return t != null && t.AssignedTo == "Me"; // your current field
            };
            PartnerTasksView = new CollectionViewSource { Source = Tasks }.View;
            PartnerTasksView.Filter = o => {
                var t = o as TaskItem;
                return t != null && t.AssignedTo == "Partner";
            };
            // Apply saved theme on startup
            var saved = (Settings?.Theme ?? "Light").Equals("Dark", StringComparison.OrdinalIgnoreCase)
                ? AppTheme.Dark : AppTheme.Light;

            _themeService.Apply(saved);
            ThemeButtonText = saved == AppTheme.Light ? "Dark Mode" : "Light Mode";

            SendPartnerRequestCommand = new RelayCommand(
            async _ => await SendPartnerRequestAsync(),
            _ => !string.IsNullOrWhiteSpace(EnteredPartnerCode) && EnteredPartnerCode != UserId
            );

            AcceptPartnerInviteCommand = new RelayCommand<PartnerRequest>(async r => await AcceptPartnerInviteAsync(r!));
            DeclinePartnerInviteCommand = new RelayCommand<PartnerRequest>(async r => await DeclinePartnerInviteAsync(r!));
            CancelPartnerRequestCommand = new RelayCommand<PartnerRequest>(async r => await CancelPartnerRequestAsync(r!));
            ToggleThemeCommand = new RelayCommand(_ => {
                var next = _themeService.Toggle();
                Settings.Theme = next == AppTheme.Dark ? "Dark" : "Light";
                SaveSettings(); // call your existing method that persists UserSettings
                ThemeButtonText = next == AppTheme.Light ? "Dark Mode" : "Light Mode";
            });
            OutgoingPartnerRequests.CollectionChanged += (_, __) => RecomputeOutgoingFlags();

            // Initialize commands
            AddTaskCommand = new RelayCommand(_ => AddTask());
            DeleteTaskCommand = new RelayCommand<TaskItem>(t => DeleteTaskInternal(t!));
            AcceptTaskCommand = new RelayCommand<TaskItem>(t => AcceptTaskInternal(t!));
            DeclineTaskCommand = new RelayCommand<TaskItem>(t => DeclineTaskInternal(t!));
            SaveDisplayNameCommand = new RelayCommand(
                _ => SaveDisplayName(),
                _ => !string.IsNullOrWhiteSpace(NewDisplayName)
        );
            DisconnectPartnerCommand = new RelayCommand(_ => DisconnectPartner());
            _partner.PartnerChanged += async () => {
                // This mirrors your current flow: recompute, save, and reattach listeners
                IsPartnerVerified = !string.IsNullOrWhiteSpace(PartnerId);
                await ReloadForNewPartnerAsync();
                OnPropertyChanged(nameof(PartnerId));
                OnPropertyChanged(nameof(NeedsProfileSetup));
                OnPropertyChanged(nameof(DisplayName));
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
                OnPropertyChanged(nameof(DisplayName));
            }
        }
        void RecomputeOutgoingFlags() {
            HasPendingOutgoing = OutgoingPartnerRequests.Any(r => r.Status == "pending");
            OnPropertyChanged(nameof(ConnectionSummary));
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

            // 4) Start partner-request listeners (new)
            _incomingReqsHandle = _partnerReqs.ListenIncoming(myId, list => {
                Application.Current.Dispatcher.Invoke(() => {
                    IncomingPartnerRequests.Clear();
                    foreach(var r in list.Where(x => x.Status == "pending"))
                        IncomingPartnerRequests.Add(r);
                    var disconnected = list.FirstOrDefault(x => x.Status == "disconnected");
                    if(disconnected != null) {
                        var other = disconnected.FromUserId; // incoming doc id is requester
                        if(!string.IsNullOrWhiteSpace(other)) {
                            _ = _partnerReqs.PurgePairAsync(myId, other);

                            _partner.PartnerId = string.Empty;
                            IsPartnerVerified = false;
                            OnPropertyChanged(nameof(ConnectionSummary));

                            OutgoingPartnerRequests.Clear();
                            IncomingPartnerRequests.Clear();
                            RecomputeOutgoingFlags();
                        }
                    }
                });
            });

            _outgoingReqsHandle = _partnerReqs.ListenOutgoing(myId, list => {
                Application.Current.Dispatcher.Invoke(() => {
                    OutgoingPartnerRequests.Clear();
                    foreach(var r in list)
                        OutgoingPartnerRequests.Add(r);

                    // If recipient accepted our outgoing request, auto-connect
                    var accepted = list.FirstOrDefault(x => x.Status == "accepted");
                    if(accepted != null) {
                        // Figure out the “other” user id
                        var other = accepted.ToUserId == myId ? accepted.FromUserId : accepted.ToUserId;
                        if(!string.IsNullOrWhiteSpace(other) && PartnerId != other) {
                            _partner.PartnerId = other;   // persists + raises PartnerChanged
                            IsPartnerVerified = true;     // turn on partner UI + enable syncing
                        }
                    }
                    var disconnected = list.FirstOrDefault(x => x.Status == "disconnected");
                    if(disconnected != null) {
                        var other = disconnected.FromUserId; // incoming doc id is requester
                        if(!string.IsNullOrWhiteSpace(other)) {
                            _ = _partnerReqs.PurgePairAsync(myId, other);

                            _partner.PartnerId = string.Empty;
                            IsPartnerVerified = false;
                            OnPropertyChanged(nameof(ConnectionSummary));

                            OutgoingPartnerRequests.Clear();
                            IncomingPartnerRequests.Clear();
                            RecomputeOutgoingFlags();
                        }
                    }
                });
            });

            // 5) Attach partner + group listeners ONLY when verified
            if(IsPartnerVerified && !string.IsNullOrWhiteSpace(partnerId)) {
                _partnerHandle = _requestService.ListenPersonal(partnerId, cloud => {
                    Application.Current.Dispatcher.Invoke(() => {
                        TaskCollectionHelpers.UpsertInto(Tasks, cloud, assignedTo: "Partner", ownerUserId: partnerId);
                        OnPropertyChanged(nameof(Tasks));
                        PartnerTasksView.Refresh();
                    });
                });

                _requestsHandle = _requestService.ListenRequests(groupId, cloud => {
                    Application.Current.Dispatcher.Invoke(() => {
                        TaskCollectionHelpers.ReplaceAll(PendingTasks, cloud, assignedTo: "Partner", requestMode: true);
                        OnPropertyChanged(nameof(PendingTasks));
                    });
                });
            }
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

        private async void DisconnectPartner() {
            var pid = PartnerId;
            if(string.IsNullOrWhiteSpace(pid)) return;

            // Tell Firestore first so BOTH sides flip right away
            await _partnerReqs.DisconnectAsync(UserId, pid);
            _ = _partnerReqs.PurgePairAsync(UserId, pid);

            // Clear local state
            _partner.PartnerId = string.Empty;      // persists + raises PartnerChanged
            IsPartnerVerified = false;
            OnPropertyChanged(nameof(ConnectionSummary));
        }
        private async Task SendPartnerRequestAsync() {
            if(string.IsNullOrWhiteSpace(EnteredPartnerCode) || EnteredPartnerCode == UserId) return;

            var myName = _partner.DisplayName ?? "Someone";
            await _partnerReqs.SendAsync(UserId, EnteredPartnerCode, myName);
            EnteredPartnerCode = string.Empty;
        }

        private async Task AcceptPartnerInviteAsync(PartnerRequest r) {
            if(r is null) return;

            await _partnerReqs.AcceptAsync(UserId, r.FromUserId);

            // Set local partner & mark verified; this triggers your PartnerChanged flow

            IsPartnerVerified = true;
            _partner.PartnerId = r.FromUserId;
        }

        private async Task DeclinePartnerInviteAsync(PartnerRequest r) {
            if(r is null) return;
            await _partnerReqs.DeclineAsync(UserId, r.FromUserId);
        }

        private async Task CancelPartnerRequestAsync(PartnerRequest r) {
            if(r is null) return;

            var target = r.ToUserId == UserId ? r.FromUserId : r.ToUserId;
            await _partnerReqs.CancelAsync(UserId, target);
        }


        public DateTime Now {
            get => _now;
            private set { _now = value; OnPropertyChanged(nameof(Now)); }
        }
        public void Dispose() {
            _clock.Stop();
            _myHandle?.Dispose();
            _partnerHandle?.Dispose();
            _requestsHandle?.Dispose();
            _incomingReqsHandle?.Dispose();
            _outgoingReqsHandle?.Dispose();
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
        public string ThemeButtonText {
            get => _themeButtonText;
            set { if(_themeButtonText == value) return; _themeButtonText = value; OnPropertyChanged(nameof(ThemeButtonText)); }
        }
        public bool HasPendingOutgoing {
            get => _hasPendingOutgoing;
            private set { if(_hasPendingOutgoing == value) return; _hasPendingOutgoing = value; OnPropertyChanged(nameof(HasPendingOutgoing)); }
        }
        public string? EnteredPartnerCode {
            get => _enteredPartnerCode;
            set { _enteredPartnerCode = value?.Trim(); OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
        }
        public string? NewDisplayName {
            get => _newDisplayName;
            set {
                if(_newDisplayName == value) return;
                _newDisplayName = value;
                OnPropertyChanged(nameof(NewDisplayName));
                CommandManager.InvalidateRequerySuggested();
            }
        }

    }
}