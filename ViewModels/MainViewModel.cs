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
using System.Windows;        // Application.Current
using System.Windows.Media;

namespace TaskMate.ViewModels {
    public class MainViewModel : INotifyPropertyChanged, IDisposable {
        //Public properties
        public Assignee[] Assignees { get; } = (Assignee[])Enum.GetValues(typeof(Assignee));
        // ADD near other public props
        public bool IsMuted => !_settings.SoundsEnabled;
        public bool IsDarkTheme => _settings.Theme == AppTheme.Dark;
        public string SoundButtonText => _settings.SoundsEnabled ? "Mute" : "Unmute";
        public bool ShowPartnerList => IsPartnerVerified;
        public bool NeedsProfileSetup => _settings.NeedsProfileSetup;
        public string UserId => _partner.UserId;
        public string? PartnerId => _partner.PartnerId;
        public string? DisplayName => _settings.DisplayName;
        public string ConnectionSummary => IsPartnerVerified ? $"Connected to: {PartnerId}" : "No partner connected yet";
        public bool AnimationsEnabled => _anim.Enabled;
        public string AnimButtonText => _anim.Enabled ? "Disable Animations" : "Enable Animations";
        private string? _newActivityMessage;
        private static int _openPopouts;
        private bool _hasNewActivity;
        public bool HasNewActivity {
            get => _hasNewActivity;
            private set {
                if(_hasNewActivity == value) return;
                _hasNewActivity = value;
                _settings.UnreadActivity = value;
                _settings.Save();
                OnPropertyChanged();
            }
        }

        private bool _hasNewPending;
        public bool HasNewPending {
            get => _hasNewPending;
            private set {
                if(_hasNewPending == value) return;
                _hasNewPending = value;
                _settings.UnreadPending = value;
                _settings.Save();
                OnPropertyChanged();
            }
        }

        private bool _hasNewCompleted;
        public bool HasNewCompleted {
            get => _hasNewCompleted;
            private set {
                if(_hasNewCompleted == value) return;
                _hasNewCompleted = value;
                _settings.UnreadCompleted = value;
                _settings.Save();
                OnPropertyChanged();
            }
        }

        private bool _hasNewConnections;
        public bool HasNewConnections {
            get => _hasNewConnections;
            private set {
                if(_hasNewConnections == value) return;
                _hasNewConnections = value;
                _settings.UnreadConnections = value;
                _settings.Save();
                OnPropertyChanged();
            }
        }
        private double _volumePercent;
        public double VolumePercent {
            get => _volumePercent;
            set {
                if(_volumePercent == value) return;
                _volumePercent = value;

                // convert 0–100 slider → 0.0–1.0 sound volume
                float normalized = (float)(value / 100.0);

                _settings.SoundVolume = normalized;
                _settings.Save();

                SoundService.SetGlobalVolume(normalized);

                OnPropertyChanged();
            }
        }
        private bool _connectionsPrimed;



        // === Auth constants (move to config later) ===
        private const string ProjectId = "taskmate-4777f";
        private const string WebApiKey = "AIzaSyD0umHa8ERVEYSV7TdUc54FQ4-665lyDnw";

        // === Auth state for overlay ===
        private bool _needsAuthSetup; public bool NeedsAuthSetup { get => _needsAuthSetup; set { if(_needsAuthSetup == value) return; _needsAuthSetup = value; OnPropertyChanged(); } }
        private string? _email; public string? Email { get => _email; set { _email = value; OnPropertyChanged(); } }
        private string? _password; public string? Password { get => _password; set { _password = value; OnPropertyChanged(); } }
        private bool _isCreateAccount; public bool IsCreateAccount { get => _isCreateAccount; set { _isCreateAccount = value; OnPropertyChanged(); OnPropertyChanged(nameof(AuthButtonText)); } }
        private string? _authError; public string? AuthError { get => _authError; set { _authError = value; OnPropertyChanged(); } }
        public string AuthButtonText => IsCreateAccount ? "Create account" : "Sign in";




        public IReadOnlyList<KeyValuePair<TaskSortMode, string>> SortOptions { get; } =
        new[] {
        new KeyValuePair<TaskSortMode,string>(TaskSortMode.DueSoon, "Due soon"),
        new KeyValuePair<TaskSortMode,string>(TaskSortMode.Newest,  "Newest"),
        new KeyValuePair<TaskSortMode,string>(TaskSortMode.Oldest,  "Oldest"),
        new KeyValuePair<TaskSortMode,string>(TaskSortMode.Category,"Category"),
        };

        private bool _isSettingsOpen;
        public bool IsSettingsOpen {
            get => _isSettingsOpen;
            set { if(_isSettingsOpen == value) return; _isSettingsOpen = value; OnPropertyChanged(); }
        }

        public bool NotificationsEnabled {
            get => _settings.NotificationsEnabled;
            set {
                if(_settings.NotificationsEnabled == value) return;
                _settings.NotificationsEnabled = value;
                _settings.Save();

                if(!value) {
                    // Turn OFF path
                    AppServices.Notifications.DisableAllToasts();          // 1) unschedule everything

                    // 2) disable/delete the OS task
                    // If you know your published notifier path, pass it when enabling (below).
                    TaskMate.Services.Notifications.TaskSchedulerUtil.DisableNotifierTask();

                    // 3) stop any in-app timers that specifically exist to trigger toasts
                    // e.g., if you have a due-reminders DispatcherTimer: _dueTimer.Stop();
                }
                else {
                    // Turn ON path (optional now — you can also only enable on a separate “Set up notifications” flow)
                    // var exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TaskMate.Notifier", "TaskMate.Notifier.exe");
                    // TaskMate.Services.Notifications.TaskSchedulerUtil.EnableOrCreateNotifierTask(exe);
                }

                OnPropertyChanged();
            }
        }
        private TaskSortMode _selectedSort = TaskSortMode.DueSoon;
        public TaskSortMode SelectedSort {
            get => _selectedSort;
            set {
                if(_selectedSort == value) return;
                _selectedSort = value;
                OnPropertyChanged();
                ApplySorts();
            }
        }
        private int _myActiveCount;
        public int MyActiveCount {
            get => _myActiveCount;
            private set { if(_myActiveCount != value) { _myActiveCount = value; OnPropertyChanged(); } }
        }

        private int _myCompletedCount;
        public int MyCompletedCount {
            get => _myCompletedCount;
            private set { if(_myCompletedCount != value) { _myCompletedCount = value; OnPropertyChanged(); } }
        }
        private int _partnerActiveCount;
        public int PartnerActiveCount {
            get => _partnerActiveCount;
            private set { if(_partnerActiveCount != value) { _partnerActiveCount = value; OnPropertyChanged(); } }
        }
        private int _selectedTopTab;
        public int SelectedTopTab {
            get => _selectedTopTab;
            set { if(_selectedTopTab == value) return; _selectedTopTab = value; OnPropertyChanged(); ClearTabNotification(value); }
        }
        private void ClearTabNotification(int tabIndex) {
            switch(tabIndex) {
                case 0: HasNewActivity = false; break;
                case 2: HasNewPending = false; break;
                case 3: HasNewCompleted = false; break;
                case 4: HasNewConnections = false; break;
            }

            OnPropertyChanged(nameof(HasNewActivity));
            OnPropertyChanged(nameof(HasNewPending));
            OnPropertyChanged(nameof(HasNewCompleted));
            OnPropertyChanged(nameof(HasNewConnections));
        }

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
        private readonly IAnimationService _anim;
        private readonly Services.Auth.IAuthService _auth = AppServices.Auth;

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
        public ICollectionView CompletedAllView { get; }
        public ObservableCollection<TaskItem> Tasks => _taskService.Tasks;
        public ObservableCollection<TaskItem> PendingTasks => _taskService.PendingTasks;
        public ObservableCollection<TaskItem> SentPendingTasks => _taskService.SentPendingTasks;
        public ObservableCollection<string> Categories { get; } = new() { CreateNewCategory };
        public ObservableCollection<PartnerRequest> IncomingPartnerRequests { get; } = new();
        public ObservableCollection<PartnerRequest> OutgoingPartnerRequests { get; } = new();
        public ObservableCollection<ActivityEntry> ActivityFeed => _activity.Feed;
        // Commands
        public ICommand ToggleThemeCommand { get; private set; }
        public ICommand SendPartnerRequestCommand { get; private set; }
        public ICommand AcceptPartnerInviteCommand { get; private set; }
        public ICommand DeclinePartnerInviteCommand { get; private set; }
        public ICommand CancelPartnerRequestCommand { get; private set; }
        public ICommand DisconnectPartnerCommand { get; private set; }
        public ICommand SaveDisplayNameCommand { get; private set; }
        public ICommand AddTaskCommand { get; private set; }
        public ICommand DeleteTaskCommand { get; private set; }
        public ICommand AcceptTaskCommand { get; private set; }
        public ICommand DeclineTaskCommand { get; private set; }
        public ICommand ShowTaskDetailsCommand { get; private set; }
        public ICommand ToggleCompleteCommand { get; private set; }
        public ICommand SendActivityMessageCommand { get; private set; }
        public ICommand ReactToActivityCommand { get; private set; }
        public ICommand PopOutBothCommand { get; private set; }
        public ICommand PopOutMineCommand { get; private set; }
        public ICommand PopOutPartnerCommand { get; private set; }
        public ICommand ToggleSoundCommand { get; private set; }
        public ICommand ToggleAnimationsCommand { get; private set; }
        public ICommand ToggleNotificationsCommand { get; private set; }
        public ICommand ToggleSettingsCommand { get; private set; }
        public ICommand AuthContinueCommand { get; private set; }



        public MainViewModel(
            ITaskService taskService,
            IPartnerService partnerService,
            IThemeService themeService,
            ISettingsService settingsService,
            ILiveSyncCoordinator live,
            ITaskActions actions,
            IPairingOrchestrator pairing,
            ITaskDialogService dialogs,
            IActivityLogService activity,
            IAnimationService anim) {
            // ===== Dependency injection of services (unchanged) =====
            _activity = activity;
            _taskService = taskService;
            _actions = actions;
            _partner = partnerService;
            _themeService = themeService;
            _settings = settingsService;
            _pairing = pairing;
            _dialogs = dialogs;
            _live = live;
            _anim = anim;

            _clock.Tick += (_, __) => Now = DateTime.Now;
            _clock.Start();
            HasNewActivity = _settings.UnreadActivity;
            HasNewPending = _settings.UnreadPending;
            HasNewCompleted = _settings.UnreadCompleted;
            HasNewConnections = _settings.UnreadConnections;
            VolumePercent = _settings.SoundVolume * 100.0;
            SoundService.SetGlobalVolume(_settings.SoundVolume);

            OnPropertyChanged(nameof(PartnerId));

            // ===== Views setup (unchanged) =====
            MyTasksView = CollectionViewSource.GetDefaultView(Tasks);
            IsPartnerVerified = !string.IsNullOrWhiteSpace(PartnerId);
            MyTasksView.Filter = o => o is TaskItem t && t.AssignedTo == Assignee.Me && !t.IsCompleted;

            PartnerTasksView = new CollectionViewSource { Source = Tasks }.View;
            PartnerTasksView.Filter = o => o is TaskItem t && t.AssignedTo == Assignee.Partner && !t.IsCompleted;

            CompletedAllView = new CollectionViewSource { Source = Tasks }.View;
            CompletedAllView.Filter = o => o is TaskItem t && t.IsCompleted;

            // ===== Apply saved theme/sound on startup (unchanged) =====
            var saved = _settings.Theme;
            _themeService.Apply(saved);
            ThemeButtonText = saved == AppTheme.Light ? "Dark Mode" : "Light Mode";

            SoundService.Enable(_settings.SoundsEnabled);
            OnPropertyChanged(nameof(IsDarkTheme));
            OnPropertyChanged(nameof(IsMuted));
            OnPropertyChanged(nameof(SoundButtonText));

            // ===== Auth overlay command MUST be available before we return =====
            AuthContinueCommand = new RelayCommand(async _ => await AuthContinueAsync());

            // ===== Phase 1: Auth gate — continue boot only after auth =====
            _ = InitializeAuthAsync();
            return;
        }

        private void BootstrapAfterAuth() {
            // ===== Live sync =====
            _live.Attach(Tasks, PendingTasks, SentPendingTasks, MyTasksView, PartnerTasksView);
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

                // Debounce the very first snapshot after startup
                if(!_connectionsPrimed) {
                    _connectionsPrimed = true;
                    return;
                }
                if(IncomingPartnerRequests.Count == 0 && OutgoingPartnerRequests.Count == 0)
                    return;

                if(SelectedTopTab != 4) {
                    HasNewConnections = true;
                }
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
                    // try { await _pairing.PurgePairAsync(UserId, PartnerId!); }
                    // catch { /* log if you want; non-fatal */ }
                }
                else {
                    await _activity.StopPartnerAsync();
                }
                UpdateHeaderCounts();
            };
            if(!string.IsNullOrWhiteSpace(PartnerId)) {
                var since = _settings.PairedSinceUtc ?? DateTime.UtcNow;
                _ = _activity.StartPartnerSinceAsync(PartnerId!, since);
            }

            // ===== Commands =====
            AcceptPartnerInviteCommand = new RelayCommand<PartnerRequest>(async r => await _pairing.AcceptAsync(UserId, r!));
            DeclinePartnerInviteCommand = new RelayCommand<PartnerRequest>(async r => await _pairing.DeclineAsync(UserId, r!));
            CancelPartnerRequestCommand = new RelayCommand<PartnerRequest>(async r => await _pairing.CancelAsync(UserId, r!));
            DisconnectPartnerCommand = new RelayCommand(async _ => await _pairing.DisconnectAsync(UserId, PartnerId ?? string.Empty));

            SendPartnerRequestCommand = new RelayCommand(
                async _ => await _pairing.SendAsync(UserId, EnteredPartnerCode!, DisplayName ?? "Someone"),
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
                OnPropertyChanged(nameof(IsDarkTheme));
            });

            SaveDisplayNameCommand = new RelayCommand(
                _ => SaveDisplayName(),
                _ => !string.IsNullOrWhiteSpace(NewDisplayName)
            );

            ToggleCompleteCommand = new RelayCommand<TaskItem>(async t => {
                if(t == null) return;
                var wasCompleted = t.IsCompleted;

                // flip
                if(t.IsCompleted)
                    t.CompletedAt ??= DateTime.UtcNow;
                else
                    t.CompletedAt = null;

                t.UpdatedAt = DateTime.UtcNow;

                await _actions.UpdateAsync(t, UserId, _partner.GroupId);

                // refresh the four views so items "jump" between active/completed"
                MyTasksView?.Refresh();
                PartnerTasksView?.Refresh();
                CompletedAllView?.Refresh();
                _taskService.SaveAll(GroupId);

                // 🎊 fire only on the transition to completed
                if(wasCompleted) {
                    _anim.Confetti(); // defaults to MainWindow + ~80 pieces
                }
            });

            SendActivityMessageCommand = new RelayCommand(async _ => {
                var text = (NewActivityMessage ?? string.Empty).Trim();
                if(text.Length == 0) return;

                await _activity.PostMessageAsync(text, UserId);
                SoundService.PlaySent();
                NewActivityMessage = string.Empty; // clear box
            }, _ => !string.IsNullOrWhiteSpace(NewActivityMessage));

            ReactToActivityCommand = new RelayCommand(async obj => {
                if(obj is not Tuple<ActivityEntry, string> t) return;
                var (entry, reaction) = t;

                // which collection owns this entry (mine or partner’s)
                var ownerUserId = entry.IsMine ? UserId : (PartnerId ?? string.Empty);
                if(string.IsNullOrWhiteSpace(ownerUserId) || string.IsNullOrWhiteSpace(entry.Id)) return;

                var reactorId = UserId; // I’m reacting
                await _activity.ReactAsync(ownerUserId, entry.Id, reactorId, reaction);
            });

            PopOutBothCommand = new RelayCommand(_ => {
                var w = new TaskMate.Views.TaskBoardPopoutWindow { DataContext = this };
                ShowPopoutAndHideMain(w);
                SoundService.PlayTaskCreated();
            });

            PopOutMineCommand = new RelayCommand(_ => {
                var w = new Views.TaskListPopoutWindow {
                    Owner = Application.Current.MainWindow,
                    DataContext = this,                 // so MyTasksView / PartnerTasksView are available
                    ListKind = TaskListKind.Mine        // or Partner
                };
                w.Show();
            });

            // Partner
            PopOutPartnerCommand = new RelayCommand(_ => {
                var w = new Views.TaskListPopoutWindow {
                    Owner = Application.Current.MainWindow,
                    DataContext = this,                 // so MyTasksView / PartnerTasksView are available
                    ListKind = TaskListKind.Partner
                };
                w.Show();
            });

            ToggleSoundCommand = new RelayCommand(_ => {
                var next = !_settings.SoundsEnabled;
                _settings.SoundsEnabled = next;
                _settings.Save();
                SoundService.Enable(next);
                OnPropertyChanged(nameof(IsMuted));          // <— notify
                OnPropertyChanged(nameof(SoundButtonText));  // (optional tooltip)
            });

            ToggleAnimationsCommand = new RelayCommand(_ => {
                _anim.Enabled = !_anim.Enabled;
                OnPropertyChanged(nameof(AnimationsEnabled));
                OnPropertyChanged(nameof(AnimButtonText));
            });

            ToggleNotificationsCommand = new RelayCommand(_ => {
                NotificationsEnabled = !_settings.NotificationsEnabled;
            });

            ToggleSettingsCommand = new RelayCommand(_ => IsSettingsOpen = !IsSettingsOpen);

            // ===== Load local data, attach handlers, start services =====
            var local = TaskMate.Data.TaskDataService.LoadTasks();
            foreach(var t in local) _taskService.Tasks.Add(t);
            foreach(var t in Tasks) AttachTaskHandlers(t);

            // Track add/remove so new categories from partner are merged automatically
            Tasks.CollectionChanged += (_, e) => {
                if(e.NewItems != null)
                    foreach(TaskItem t in e.NewItems) AttachTaskHandlers(t);

                if(e.OldItems != null)
                    foreach(TaskItem t in e.OldItems) DetachTaskHandlers(t);

                UpdateHeaderCounts();

                // Keep the list sorted if we are in Category mode
                if(SelectedSort == TaskSortMode.Category) {
                    ApplyCategorySorts();
                    RefreshAllViews();
                }
            };

            UpdateHeaderCounts();
            // Notify "Pending" tab when incoming or sent pending tasks change
            PendingTasks.CollectionChanged += (_, __) => {
                if(SelectedTopTab != 2) {
                    HasNewPending = true;
                }
            };

            SentPendingTasks.CollectionChanged += (_, __) => {
                if(SelectedTopTab != 2) {
                    HasNewPending = true;
                }
            };
            _ = _live.StartAsync();

            if(IsPartnerVerified && PartnerId is not null) {
                _ = _live.ReloadForPartnerAsync(); // safe now because StartAsync just ran
                var since = _settings.PairedSinceUtc ?? DateTime.UtcNow;
                _ = _activity.StartPartnerSinceAsync(PartnerId, since);
            }

            _ = _activity.StartMineAsync(UserId);
            _pairing.Start(UserId);

            _activity.Feed.CollectionChanged += (_, __) => {
                if(SelectedTopTab != 0) {
                    HasNewActivity = true;
                }
            };

            // Re-apply sort after categories are loaded (async) and whenever Categories changes.
            _ = InitializeCategoriesAsync().ContinueWith(_ => {
                if(SelectedSort == TaskSortMode.Category) {
                    ApplyCategorySorts();
                    RefreshAllViews();
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());

            // If the Categories collection changes later (e.g., partner sync pulls a new category),
            // re-apply the category sort automatically.
            Categories.CollectionChanged += (_, __) => {
                if(SelectedSort == TaskSortMode.Category) {
                    ApplyCategorySorts();
                    RefreshAllViews();
                }
            };

            // Run initial sort immediately (uses whatever we have right now)
            ApplySorts();

            // If profile not set up yet, prompt for display name
            if(NeedsProfileSetup) NewDisplayName = string.Empty;
        }
        private async Task InitializeAuthAsync() {
            try {
                var have = await _auth.TrySilentSignInAsync();
                if(!have) {
                    NeedsAuthSetup = true; // show overlay Step 1 

                    return;
                }
                await AfterAuthAsync();
            }
            catch(Exception ex) { AuthError = ex.Message; NeedsAuthSetup = true; }
        }

        private async Task AfterAuthAsync() {
            try { // Use the injected settings service; do NOT create a new instance _settings.EnsureUserId(_auth.Uid!); 
                  // // The overlay for Step 2 is driven by DisplayName being empty, // so just notify the bindings to re-evaluate. 
                OnPropertyChanged(nameof(DisplayName)); OnPropertyChanged(nameof(NeedsProfileSetup));
            }
            catch { // non-fatal 
            }
            TaskMate.Services.Notifications.ToastRegistration.EnsureRegistered(); TaskMate.Services.Notifications.ToastRegistration.ShowSimple("TaskMate", "Notifications ready"); NeedsAuthSetup = false; // hide Step 1 
            BootstrapAfterAuth(); await Task.CompletedTask;
        }
        private async Task AuthContinueAsync() {
            AuthError = null;
            if(string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password)) { AuthError = "Email and password are required."; return; }
            try {
                if(IsCreateAccount)
                    await _auth.SignUpWithEmailAsync(Email.Trim(), Password, WebApiKey);
                else
                    await _auth.SignInWithEmailAsync(Email.Trim(), Password, WebApiKey); await AfterAuthAsync();
            }
            catch(Exception ex) { AuthError = ex.Message; }
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
                  .ToList());
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

            int KeyOf(string? cat)
                => cat is string s && order.TryGetValue(s, out var idx) ? idx : int.MaxValue;

            // Non-generic IComparer so it plugs straight into ListCollectionView.CustomSort
            System.Collections.IComparer comparer =
                Comparer<TaskItem>.Create((a, b) => {
                    int ka = KeyOf(a.Category);
                    int kb = KeyOf(b.Category);
                    if(ka != kb) return ka.CompareTo(kb);
                    // stable fallback: then by Title
                    return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
                });

            void SetSort(ICollectionView v) {
                if(v is ListCollectionView lv) lv.CustomSort = comparer;
            }

            SetSort(MyTasksView);
            SetSort(PartnerTasksView);
            SetSort(CompletedAllView);
        }

        private void ApplySorts() {
            if(SelectedSort == TaskSortMode.Category) {
                ApplyCategorySorts();
                RefreshAllViews();
                return;
            }

            int NullsLast<T>(T? a, T? b) where T : struct, IComparable<T> {
                var hasA = a.HasValue; var hasB = b.HasValue;
                if(hasA && hasB) return a.Value.CompareTo(b.Value);
                if(hasA && !hasB) return -1;
                if(!hasA && hasB) return 1;
                return 0;
            }

            // NOTE: non-generic IComparer
            System.Collections.IComparer comparer = SelectedSort switch {
                TaskSortMode.DueSoon => (System.Collections.IComparer)Comparer<TaskItem>.Create((a, b) => {
                    int byDue = NullsLast(a.DueDate, b.DueDate);
                    if(byDue != 0) return byDue;
                    return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
                }),

                TaskSortMode.Newest => (System.Collections.IComparer)Comparer<TaskItem>.Create((a, b) => {
                    DateTime A() => a.UpdatedAt ?? a.CompletedAt ?? a.DueDate ?? DateTime.MinValue;
                    DateTime B() => b.UpdatedAt ?? b.CompletedAt ?? b.DueDate ?? DateTime.MinValue;
                    int byTimeDesc = -A().CompareTo(B()); // newest first
                    if(byTimeDesc != 0) return byTimeDesc;
                    return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
                }),

                TaskSortMode.Oldest => (System.Collections.IComparer)Comparer<TaskItem>.Create((a, b) => {
                    DateTime A() => a.UpdatedAt ?? a.CompletedAt ?? a.DueDate ?? DateTime.MaxValue;
                    DateTime B() => b.UpdatedAt ?? b.CompletedAt ?? b.DueDate ?? DateTime.MaxValue;
                    int byTimeAsc = A().CompareTo(B());   // oldest first
                    if(byTimeAsc != 0) return byTimeAsc;
                    return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
                }),

                _ => (System.Collections.IComparer)Comparer<TaskItem>.Create((a, b) => 0)
            };

            void SetSort(ICollectionView v) {
                if(v is ListCollectionView lv) lv.CustomSort = comparer;
            }

            SetSort(MyTasksView);
            SetSort(PartnerTasksView);
            SetSort(CompletedAllView);
            RefreshAllViews();
        }
        private void AttachTaskHandlers(TaskItem t) {
            if(t == null) return;
            t.PropertyChanged += OnTaskPropertyChanged;
            // Ensure its category is known (covers initial attach and new items)
            MergeCategories(new[] { t.Category! });
            UpdateHeaderCounts();
        }

        private void DetachTaskHandlers(TaskItem t) {
            if(t == null) return;
            t.PropertyChanged -= OnTaskPropertyChanged;
            UpdateHeaderCounts();
        }

        private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if(e.PropertyName == nameof(TaskItem.Category) && sender is TaskItem t) {
                MergeCategories(new[] { t.Category! });
                if(SelectedSort == TaskSortMode.Category) {
                    ApplyCategorySorts();
                    RefreshAllViews();
                }
            }
            if(e.PropertyName == nameof(TaskItem.IsCompleted) ||
                e.PropertyName == nameof(TaskItem.AssignedTo)) {
                UpdateHeaderCounts();
                // If a task just flipped completed and we’re not on the Completed tab, show indicator
                if(e.PropertyName == nameof(TaskItem.IsCompleted) && SelectedTopTab != 2) {
                    HasNewCompleted = true;
                }
            }
        }

        private void RefreshAllViews() {
            MyTasksView?.Refresh();
            PartnerTasksView?.Refresh();
            CompletedAllView?.Refresh();
        }
        private void UpdateHeaderCounts() {
            MyActiveCount = Tasks.Count(t => t.AssignedTo == Assignee.Me && !t.IsCompleted);
            MyCompletedCount = Tasks.Count(t => t.AssignedTo == Assignee.Me && t.IsCompleted);
            PartnerActiveCount = Tasks.Count(t => t.AssignedTo == Assignee.Partner && !t.IsCompleted);
        }
        private static void ShowPopoutAndHideMain(Window popout) {
            var main = Application.Current?.MainWindow;
            if(main == null || popout == null) return;

            // Hide main if this is the first popout
            if(_openPopouts == 0)
                main.Hide();

            _openPopouts++;

            popout.Closed += (_, __) => {
                _openPopouts = Math.Max(0, _openPopouts - 1);
                if(_openPopouts == 0 && main.IsLoaded)
                    main.Show();
            };

            popout.Show(); // non-modal
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