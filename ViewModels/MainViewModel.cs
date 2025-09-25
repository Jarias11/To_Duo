using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Data;
using TaskMate.Models;
using TaskMate.Services;
using TaskMate.Models.Enums;
using TaskMate.Orchestration;

namespace TaskMate.ViewModels {
    public class MainViewModel : INotifyPropertyChanged, IDisposable {
        //Public properties
        public Assignee[] Assignees { get; } = (Assignee[])Enum.GetValues(typeof(Assignee));
        public bool ShowPartnerList => IsPartnerVerified;
        public bool NeedsProfileSetup => _settings.NeedsProfileSetup;
        public string UserId => _partner.UserId;
        public string? PartnerId => _partner.PartnerId;
        public string? DisplayName => _settings.DisplayName;
        public string ConnectionSummary => IsPartnerVerified ? $"Connected to: {PartnerId}" : "No partner connected yet";
        private string? _newActivityMessage;

        //Readonly services
        private readonly ILiveSyncCoordinator _live;
        private readonly ITaskService _taskService;
        private readonly ITaskActions _actions;
        private readonly IPartnerService _partner;
        private readonly IThemeService _themeService;
        private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly ISettingsService _settings;
        private readonly IPairingOrchestrator _pairing;
        private readonly ITaskDialogService _dialogs;
        private readonly IActivityLogService _activity;

        // Backing fields + properties
        private const string CreateNewCategory = "Create New…";
        private DateTime _now = DateTime.Now;
        private string? _newDisplayName;
        private bool _isPartnerVerified;
        private bool _hasPendingOutgoing;
        private bool _isCategoryPromptOpen;
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
        public ICollectionView MyCompletedTasks { get; }
        public ICollectionView PartnerCompletedTasks { get; }
        public ObservableCollection<TaskItem> Tasks => _taskService.Tasks;
        public ObservableCollection<TaskItem> PendingTasks => _taskService.PendingTasks;
        public ObservableCollection<string> Categories { get; } = new() { CreateNewCategory };
        public ObservableCollection<PartnerRequest> IncomingPartnerRequests { get; } = new();
        public ObservableCollection<PartnerRequest> OutgoingPartnerRequests { get; } = new();
        public ObservableCollection<ActivityEntry> ActivityFeed => _activity.Feed;
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
        public ICommand ShowTaskDetailsCommand { get; }
        public ICommand ToggleCompleteCommand { get; }
        public ICommand SendActivityMessageCommand { get; }


        public MainViewModel(ITaskService taskService, IPartnerService partnerService, IThemeService themeService, ISettingsService settingsService, ILiveSyncCoordinator live, ITaskActions actions, IPairingOrchestrator pairing, ITaskDialogService dialogs, IActivityLogService activity) {

            // Dependency injection of services
            _activity = activity;
            _taskService = taskService;
            _actions = actions;
            _partner = partnerService;
            _themeService = themeService;
            _settings = settingsService;
            _pairing = pairing;
            _dialogs = dialogs;
            _live = live;
            _clock.Tick += (_, __) => Now = DateTime.Now;
            _clock.Start();

            OnPropertyChanged(nameof(PartnerId));


            MyTasksView = CollectionViewSource.GetDefaultView(Tasks);
            IsPartnerVerified = !string.IsNullOrWhiteSpace(PartnerId);
            MyTasksView.Filter = o => o is TaskItem t && t.AssignedTo == Assignee.Me && !t.IsCompleted;
            PartnerTasksView = new CollectionViewSource { Source = Tasks }.View;
            PartnerTasksView.Filter = o => o is TaskItem t && t.AssignedTo == Assignee.Partner && !t.IsCompleted;

            MyCompletedTasks = new CollectionViewSource { Source = Tasks }.View;
            MyCompletedTasks.Filter = o => o is TaskItem t && t.AssignedTo == Assignee.Me && t.IsCompleted;

            PartnerCompletedTasks = new CollectionViewSource { Source = Tasks }.View;
            PartnerCompletedTasks.Filter = o => o is TaskItem t && t.AssignedTo == Assignee.Partner && t.IsCompleted;

            // Apply saved theme on startup
            var saved = _settings.Theme;
            _themeService.Apply(saved);
            ThemeButtonText = saved == AppTheme.Light ? "Dark Mode" : "Light Mode";
            // Live sync
            _live.Attach(Tasks, PendingTasks, MyTasksView, PartnerTasksView);
            _pairing.PartnerDisconnected += () => {
                IsPartnerVerified = false;
                HasPendingOutgoing = _pairing.HasPendingOutgoing; // stay in sync
                OnPropertyChanged(nameof(ConnectionSummary));
            };

            // Bind orchestrator to the VM-owned collections
            _pairing.Attach(IncomingPartnerRequests, OutgoingPartnerRequests);

            // Keep HasPendingOutgoing + ConnectionSummary updated without VM list math
            _pairing.OutgoingChanged += () => {
                HasPendingOutgoing = _pairing.HasPendingOutgoing;
                OnPropertyChanged(nameof(ConnectionSummary));
            };

            // If partner changes at runtime, realign everything (pairing + UI)
            _partner.PartnerChanged += async () => {
                IsPartnerVerified = !string.IsNullOrWhiteSpace(PartnerId);
                await _live.ReloadForPartnerAsync();
                OnPropertyChanged(nameof(PartnerId));
                OnPropertyChanged(nameof(NeedsProfileSetup));
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(ConnectionSummary));

                if(IsPartnerVerified) {
                    // NEW: Clear stale incoming/outgoing rows
                    _pairing.ClearRequests();
                    var since = _settings.PairedSinceUtc ?? DateTime.UtcNow;
                    await _activity.StartPartnerSinceAsync(PartnerId!, since);

                    // Optional: also purge server-side partner requests between the pair
                    //try { await _pairing.PurgePairAsync(UserId, PartnerId!); }
                    //catch { /* log if you want; non-fatal */ }
                }
                else {
                    await _activity.StopPartnerAsync();
                }
            };







            // Initialize commands
            AcceptPartnerInviteCommand = new RelayCommand<PartnerRequest>(async r => await _pairing.AcceptAsync(UserId, r!));
            DeclinePartnerInviteCommand = new RelayCommand<PartnerRequest>(async r => await _pairing.DeclineAsync(UserId, r!));
            CancelPartnerRequestCommand = new RelayCommand<PartnerRequest>(async r => await _pairing.CancelAsync(UserId, r!));
            DisconnectPartnerCommand = new RelayCommand(async _ => await _pairing.DisconnectAsync(UserId, PartnerId ?? string.Empty));
            SendPartnerRequestCommand = new RelayCommand(async _ => await _pairing.SendAsync(UserId, EnteredPartnerCode!, DisplayName ?? "Someone"),
                _ => !string.IsNullOrWhiteSpace(EnteredPartnerCode) && EnteredPartnerCode != UserId
            );

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

                ApplyCategorySorts();
                // Clear inputs in the VM (service shouldn’t know about UI fields)
                NewTaskTitle = string.Empty;
                NewTaskDescription = string.Empty;
                NewTaskDueDate = null;
            });
            DeleteTaskCommand = new RelayCommand<TaskItem>(async t => await _actions.DeleteAsync(t!, UserId, _partner.GroupId), t => t?.CanDecide == true);
            AcceptTaskCommand = new RelayCommand<TaskItem>(async t => await _actions.AcceptAsync(t!, UserId, _partner.GroupId), t => t?.CanDecide == true);
            DeclineTaskCommand = new RelayCommand<TaskItem>(async t => await _actions.DeclineAsync(t!, _partner.GroupId), t => t?.CanDecide == true);
            ShowTaskDetailsCommand = new RelayCommand<TaskItem>(
    async t => { if(t != null) await _dialogs.ShowTaskDetailsAsync(t); },
    t => t != null
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

            ToggleCompleteCommand = new RelayCommand<TaskItem>(async t => {
                if(t == null) return;

                // flip
                if(t.IsCompleted)
                    t.CompletedAt ??= DateTime.UtcNow;
                else
                    t.CompletedAt = null;

                t.UpdatedAt = DateTime.UtcNow;

                await _actions.UpdateAsync(t, UserId, _partner.GroupId);

                // refresh the four views so items "jump" between active/completed
                MyTasksView?.Refresh();
                PartnerTasksView?.Refresh();
                MyCompletedTasks?.Refresh();
                PartnerCompletedTasks?.Refresh();
                _taskService.SaveAll(GroupId);
            });
            SendActivityMessageCommand = new RelayCommand(async _ => {
                var text = (NewActivityMessage ?? string.Empty).Trim();
                if(text.Length == 0) return;

                await _activity.PostMessageAsync(text, UserId);
                NewActivityMessage = string.Empty; // clear box
            },
_ => !string.IsNullOrWhiteSpace(NewActivityMessage));

            var local = TaskMate.Data.TaskDataService.LoadTasks();
            foreach(var t in local) _taskService.Tasks.Add(t);
            _ = _live.StartAsync();

            if(IsPartnerVerified && PartnerId is not null) {
                _ = _live.ReloadForPartnerAsync(); // safe now because StartAsync just ran
                var since = _settings.PairedSinceUtc ?? DateTime.UtcNow;
                _ = _activity.StartPartnerSinceAsync(PartnerId, since);

            }
            _ = _activity.StartMineAsync(UserId);
            _pairing.Start(UserId);


            _ = InitializeCategoriesAsync();
            // If profile not set up yet, prompt for display name
            if(NeedsProfileSetup) NewDisplayName = string.Empty;



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
        private async Task InitializeCategoriesAsync() {
            // 1) Load persisted categories (per-user) — see #3 for the storage helper.
            var stored = CategoryDataService.Load(UserId); // returns List<string>
            MergeCategories(stored);

            // 2) Also surface categories seen in tasks (local/remote) so sorting/filtering never “loses” them.
            var fromTasks = Tasks.Select(t => t.Category)
                                 .Where(c => !string.IsNullOrWhiteSpace(c))
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .ToList();
            MergeCategories(fromTasks);

            // Optional: pick the last used category (persist that too if you want)
        }
        // Utility to keep the sentinel at index 0 and dedupe:
        private void MergeCategories(IEnumerable<string> incoming) {
            foreach(var c in incoming.Where(s => !string.IsNullOrWhiteSpace(s))) {
                if(!Categories.Contains(c!, StringComparer.OrdinalIgnoreCase)) {
                    Categories.Add(c!);
                }
            }
        }
        private async Task PromptAndAddCategoryAsync() {
            // Use whatever dialog service you already have; falling back to a simple window if needed.
            var name = await _dialogs.PromptTextAsync(
                title: "New Category",
                message: "What should we call this category?",
                placeholder: "e.g., Chores, School, Errands");

            if(string.IsNullOrWhiteSpace(name)) {
                // Revert selection if user cancels
                OnPropertyChanged(nameof(NewTaskCategory));
                return;
            }

            name = name.Trim();

            // don't allow the sentinel and dedupe
            if(!Categories.Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase))) {
                Categories.Add(name);
                CategoryDataService.Save(
        UserId,
        Categories.Where(c => !string.Equals(c, CreateNewCategory, StringComparison.Ordinal))
                  .ToList()
    );
            }

            // select the new/existing category
            _newTaskCategory = Categories.First(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
            OnPropertyChanged(nameof(NewTaskCategory));

            // refresh your sort if you’re using it
            ApplyCategorySorts();
        }
        // Call this after you update Categories OR after tasks change:
        private void ApplyCategorySorts() {
            var order = Categories.Where(c => c != CreateNewCategory)
                                  .Select((c, i) => (c, i))
                                  .ToDictionary(t => t.c, t => t.i, StringComparer.OrdinalIgnoreCase);

            int KeyOf(string? cat) => cat is string s && order.TryGetValue(s, out var idx) ? idx : int.MaxValue;

            var comparer = Comparer<TaskItem>.Create((a, b) => {
                var ka = KeyOf(a.Category);
                var kb = KeyOf(b.Category);
                if(ka != kb) return ka.CompareTo(kb);
                // stable fallback: then by Title
                return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
            });

            // Apply to both active views; CustomSort only works on ListCollectionView
            if(MyTasksView is ListCollectionView lv1) lv1.CustomSort = comparer;
            if(PartnerTasksView is ListCollectionView lv2) lv2.CustomSort = comparer;
            if(MyCompletedTasks is ListCollectionView lv3) lv3.CustomSort = comparer;
            if(PartnerCompletedTasks is ListCollectionView lv4) lv4.CustomSort = comparer;
        }


        public DateTime Now {
            get => _now;
            private set { _now = value; OnPropertyChanged(nameof(Now)); }
        }
        public void Dispose() {
            try { _clock.Stop(); } catch { }

            // Stop partner listener first (async, but we can just fire-and-forget here)
            try { _ = _activity.StopPartnerAsync(); } catch { }

            // Dispose services that own listeners/threads
            try { _live?.Dispose(); } catch { }
            try { (_pairing as IDisposable)?.Dispose(); } catch { }
            try { (_activity as IDisposable)?.Dispose(); } catch { }

            // If any of your other services implement IDisposable, dispose them too:
            try { (_taskService as IDisposable)?.Dispose(); } catch { }

            GC.SuppressFinalize(this);
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
                if(!_isPartnerVerified)
                    AssignToPartner = false;
            }
        }
        public bool AssignToPartner {
            get => NewTaskAssignee == Assignee.Partner;
            set {
                var next = value ? Assignee.Partner : Assignee.Me;
                if(NewTaskAssignee == next) return;
                NewTaskAssignee = next;     // already raises OnPropertyChanged()
                OnPropertyChanged(nameof(AssignToPartner));


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
                if(value == _newTaskCategory) return;

                // If user chose "Create New…", open prompt *once* and bail
                if(string.Equals(value, CreateNewCategory, StringComparison.Ordinal)) {
                    if(_isCategoryPromptOpen) return;      // debounce
                    _isCategoryPromptOpen = true;

                    // Clear selection so the ComboBox is no longer on the sentinel
                    // (prevents a re-trigger after the dialog closes)
                    _newTaskCategory = null;
                    OnPropertyChanged(); // updates the ComboBox

                    _ = PromptAndAddCategoryAsync().ContinueWith(_ => {
                        _isCategoryPromptOpen = false;
                    }, TaskScheduler.FromCurrentSynchronizationContext());
                    return;
                }

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
        public string? NewActivityMessage {
            get => _newActivityMessage;
            set { _newActivityMessage = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
        }


    }
}