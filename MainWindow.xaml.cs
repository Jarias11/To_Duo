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



namespace TaskMate;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        try
        {
            DataContext = new MainViewModel();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting DataContext: {ex.Message}");
        }
    }
    private void CheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.SaveTasks();
        }
    }
}