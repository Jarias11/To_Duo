using System.Text;
using System.Windows;
using TaskMate.ViewModels;
using TaskMate.Services;
using TaskMate.Orchestration;
using TaskMate.Services.Notifications;
using TaskMate.Sync;

namespace TaskMate;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window {
    public MainWindow() {
        InitializeComponent();

        // Compose services
        var settingsSvc = new SettingsService();
        var partnerSvc = new PartnerService(settingsSvc);
        var themeSvc = new ThemeService();

        var rest = AppServices.FirestoreRest;

        var personalRepo = new FirestorePersonalTaskRepository(rest);
        var requestRepo = new FirestoreRequestRepository(rest, personalRepo);
        var requestSvc = new RequestService(personalRepo, requestRepo);

        var partnerReqs = new PartnerRequestService(
            new FirestorePartnerRequestRepository(rest)
        );
        var taskSvc = new TaskService();

        var activity = new ActivityLogService(Dispatcher, settingsSvc);
        var live = new LiveSyncCoordinator(requestSvc, partnerSvc);

        var taskActions = new TaskActions(requestSvc, taskSvc, Dispatcher, activity, settingsSvc);

        var pairing = new PairingOrchestrator(partnerReqs, partnerSvc, live, Dispatcher, activity, settingsSvc);
        var anim = new AnimationService(settingsSvc);
        var dialogs = new TaskDialogService(taskActions, partnerSvc, anim);

        AppServices.Notifications = new NotificationService();
        AppServices.Tasks = taskSvc;
        AppServices.TaskDialogs = dialogs;
        AppServices.Partner = partnerSvc;
        AppServices.Pairing = pairing;
        AppServices.Actions = taskActions;
        AppServices.Settings = settingsSvc;


        try {
            DataContext = new MainViewModel(taskSvc, partnerSvc, themeSvc, settingsSvc, live, taskActions, pairing, dialogs, activity, anim);
        }
        catch(Exception ex) {
            Console.WriteLine($"Error setting DataContext: {ex.Message}");
            System.Diagnostics.Trace.WriteLine(ex.ToString());
            MessageBox.Show(ex.ToString(), "Startup error");
            throw;
        }
    }
    private void CheckBox_Changed(object sender, RoutedEventArgs e) {
        if(DataContext is MainViewModel vm) {
            vm.SaveTasks();
        }
    }
    protected override void OnClosed(EventArgs e) {
        if(DataContext is IDisposable d) d.Dispose(); // this should call through to LiveSync/Activity/Partner disposes
        base.OnClosed(e);
        Application.Current.Shutdown(); // belt-and-suspenders
    }
}