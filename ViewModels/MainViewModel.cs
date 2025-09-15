using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Data;
using TaskMate.Models;
using TaskMate.Services;
using TaskMate.Models.Enums;

namespace TaskMate.ViewModels {
    public class MainViewModel : INotifyPropertyChanged, IDisposable {
        //Public properties
        public Assignee[] Assignees { get; } = (Assignee[])Enum.GetValues(typeof(Assignee));
        public bool ShowPartnerList => IsPartnerVerified;
        public bool NeedsProfileSetup => _settings.NeedsProfileSetup;
        public string UserId => _partner.UserId;
        public string? PartnerId => _partner.PartnerId;
        public string? DisplayName => _settings.DisplayName;
        public string ConnectionSummary =>
            IsPartnerVerified ? $"Connected to: {PartnerId}" : "No partner connected yet";

        //Readonly services
        private readonly ILiveSyncCoordinator _live;
        private readonly ITaskService _taskService;
        private readonly ITaskActions _actions;
        private readonly IRequestService _requestService;
        private readonly IPartnerService _partner;
        private readonly IPartnerRequestService _partnerReqs;
        private readonly IThemeService _themeService;
        private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly ISettingsService _settings;

        // Backing fields + properties
        private DateTime _now = DateTime.Now;
        private string? _newDisplayName;
        private bool _isPartnerVerified;
        private bool _hasPendingOutgoing;
        private Assignee _newTaskAssignee = Assignee.Me;
        private string? _enteredPartnerCode;
        private string _themeButtonText = "Dark Mode"; // when in Light, offer Dark
        private string GroupId => _partner.GroupId;
        private string _newTaskTitle = string.Empty;
        private string _newTaskDescription = string.Empty;
        private DateTime? _newTaskDueDate;
        private string? _newTaskCategory;
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


        public MainViewModel(ITaskService taskService, IPartnerService partnerService, IThemeService themeService, ISettingsService settingsService, IRequestService requestService, IPartnerRequestService partnerReqs, ILiveSyncCoordinator live, ITaskActions actions) {

            // Dependency injection of services
            _requestService = requestService;
            _taskService = taskService;
            _actions = actions;
            _partner = partnerService;
            _partnerReqs = partnerReqs;
            _themeService = themeService;
            _settings = settingsService;
            _live = live;
            _clock.Tick += (_, __) => Now = DateTime.Now;
            _clock.Start();
            _ = _taskService.InitializeAsync(GroupId);
            OnPropertyChanged(nameof(PartnerId));
            MyTasksView = CollectionViewSource.GetDefaultView(Tasks);
            IsPartnerVerified = !string.IsNullOrWhiteSpace(PartnerId);
            MyTasksView.Filter = o => (o as TaskItem)?.AssignedTo == Assignee.Me;
            PartnerTasksView = new CollectionViewSource { Source = Tasks }.View;
            PartnerTasksView.Filter = o => (o as TaskItem)?.AssignedTo == Assignee.Partner;
            // Apply saved theme on startup
            var saved = _settings.Theme;
            _themeService.Apply(saved);
            ThemeButtonText = saved == AppTheme.Light ? "Dark Mode" : "Light Mode";
            // Live sync
            _live.Attach(Tasks, PendingTasks, IncomingPartnerRequests, OutgoingPartnerRequests, MyTasksView, PartnerTasksView);
            _live.PartnerDisconnected += () => {
                IsPartnerVerified = false;
                OnPropertyChanged(nameof(ConnectionSummary));
                RecomputeOutgoingFlags();
            };
            // Initialize commands
            AcceptPartnerInviteCommand = new RelayCommand<PartnerRequest>(async r => await AcceptPartnerInviteAsync(r!));
            DeclinePartnerInviteCommand = new RelayCommand<PartnerRequest>(async r => await DeclinePartnerInviteAsync(r!));
            CancelPartnerRequestCommand = new RelayCommand<PartnerRequest>(async r => await CancelPartnerRequestAsync(r!));
            AddTaskCommand = new RelayCommand(async _ => {
                await _actions.AddAsync(
                    NewTaskTitle,
                    NewTaskDescription,
                    NewTaskDueDate,
                    NewTaskCategory,
                    NewTaskAssignee,
                    UserId,
                    PartnerId,
                    _partner.GroupId
                );
                // Clear inputs in the VM (service shouldn’t know about UI fields)
                NewTaskTitle = string.Empty;
                NewTaskDescription = string.Empty;
                NewTaskDueDate = null;
            });
            DeleteTaskCommand = new RelayCommand<TaskItem>(async t => await _actions.DeleteAsync(t!, UserId, _partner.GroupId));
            AcceptTaskCommand = new RelayCommand<TaskItem>(async t => await _actions.AcceptAsync(t!, UserId, _partner.GroupId));
            DeclineTaskCommand = new RelayCommand<TaskItem>(async t => await _actions.DeclineAsync(t!, _partner.GroupId));
            DisconnectPartnerCommand = new RelayCommand(async _ => await DisconnectPartnerAsync());
            SendPartnerRequestCommand = new RelayCommand(
           async _ => await SendPartnerRequestAsync(),
           _ => !string.IsNullOrWhiteSpace(EnteredPartnerCode) && EnteredPartnerCode != UserId
           );
            ToggleThemeCommand = new RelayCommand(_ => {
                var next = _themeService.Toggle();
                _settings.Theme = next;
                _settings.Save();
                ThemeButtonText = next == AppTheme.Light ? "Dark Mode" : "Light Mode";
            });
            SaveDisplayNameCommand = new RelayCommand(
                _ => SaveDisplayName(),
                _ => !string.IsNullOrWhiteSpace(NewDisplayName)
        );
            OutgoingPartnerRequests.CollectionChanged += (_, __) => RecomputeOutgoingFlags();
            _partner.PartnerChanged += async () => {
                IsPartnerVerified = !string.IsNullOrWhiteSpace(PartnerId);
                await _live.ReloadForPartnerAsync();
                OnPropertyChanged(nameof(PartnerId));
                OnPropertyChanged(nameof(NeedsProfileSetup));
                OnPropertyChanged(nameof(DisplayName));
            };
            // If profile not set up yet, prompt for display name
            if(NeedsProfileSetup) NewDisplayName = string.Empty;
            _ = _live.StartAsync(); // fire-and-forget
        }

        
        public void SaveTasks() => _taskService.SaveAll(GroupId);
        private void SaveDisplayName() {
            if(!string.IsNullOrWhiteSpace(NewDisplayName)) {
                _settings.DisplayName = NewDisplayName!.Trim();
                _settings.Save();
                OnPropertyChanged(nameof(NeedsProfileSetup));
                OnPropertyChanged(nameof(DisplayName));
            }
        }
        void RecomputeOutgoingFlags() {
            HasPendingOutgoing = OutgoingPartnerRequests.Any(r => r.Status == "pending");
            OnPropertyChanged(nameof(ConnectionSummary));
        }

        private async Task DisconnectPartnerAsync() {
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

            var myName = _settings.DisplayName ?? "Someone";
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
            _live?.Dispose();
        }

        public Assignee NewTaskAssignee {
            get => _newTaskAssignee;
            set { _newTaskAssignee = value; OnPropertyChanged(); }
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