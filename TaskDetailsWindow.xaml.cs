using System.Windows;
using TaskMate.ViewModels;
using TaskMate.Views;


namespace TaskMate.Views   // 👈 MUST match x:Class in XAML
{
  public partial class TaskDetailsWindow : Window {
    public TaskDetailsWindow() {
      InitializeComponent();
      Loaded += (_, __) => {
        if(DataContext is TaskDetailsViewModel vm)
          vm.CloseRequested += () => Dispatcher.Invoke(Close);
      };
    }
  }
}