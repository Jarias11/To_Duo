using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using TaskMate.ViewModels;
using TaskMate.Services;
using TaskMate.Orchestration;


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

        var requestSvc = new RequestService();
        var partnerReqs = new PartnerRequestService();
        var taskSvc = new TaskService();

        var activity = new ActivityLogService(Dispatcher, settingsSvc);
        var live = new LiveSyncCoordinator(requestSvc, partnerSvc);

        var taskActions = new TaskActions(requestSvc, taskSvc, Dispatcher, activity, settingsSvc);
        var dialogs = new TaskDialogService(taskActions, partnerSvc);
        var pairing = new PairingOrchestrator(partnerReqs, partnerSvc, live, Dispatcher, activity, settingsSvc);

        try {
            DataContext = new MainViewModel(taskSvc, partnerSvc, themeSvc, settingsSvc, live, taskActions, pairing, dialogs, activity);
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