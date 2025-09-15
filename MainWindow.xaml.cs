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
using TaskMate.ViewModels;


namespace TaskMate;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window {
    public MainWindow() {
        InitializeComponent();



        // Compose services
        var taskSvc = new TaskService();
        var partnerSvc = new PartnerService();
        var themeSvc = new ThemeService();
        var settingsSvc = new SettingsService();
        var requestSvc = new RequestService();
        var partnerReqs = new PartnerRequestService();
        var live = new LiveSyncCoordinator(requestSvc, partnerReqs, partnerSvc);

        var taskActions = new TaskActions(requestSvc, taskSvc, Dispatcher);
        try {
            DataContext = new MainViewModel(taskSvc, partnerSvc, themeSvc, settingsSvc, requestSvc, partnerReqs, live, taskActions);
        }
        catch(Exception ex) {
            Console.WriteLine($"Error setting DataContext: {ex.Message}");
        }
    }
    private void CheckBox_Changed(object sender, RoutedEventArgs e) {
        if(DataContext is MainViewModel vm) {
            vm.SaveTasks();
        }
    }
}