// Views/TaskListPopoutWindow.xaml.cs
using System.Windows;
using System.Windows.Input;
using TaskMate.Models.Enums;
using TaskMate.ViewModels;

namespace TaskMate.Views {
  public partial class TaskListPopoutWindow : Window {
    public static readonly DependencyProperty ListKindProperty =
        DependencyProperty.Register(nameof(ListKind), typeof(TaskListKind), typeof(TaskListPopoutWindow),
            new PropertyMetadata(TaskListKind.Mine));

    public TaskListKind ListKind {
      get => (TaskListKind)GetValue(ListKindProperty);
      set => SetValue(ListKindProperty, value);
    }

    // Pin: you already have the behavior; keep the ICommand if you had it in the VM.
    public ICommand TogglePinCommand { get; }
    public ICommand StartDragCommand { get; }
    public ICommand CloseCommand { get; }

    public TaskListPopoutWindow() {
      InitializeComponent();

      Topmost = true;                 // default pinned
      TogglePinCommand = new RelayCommand(_ => Topmost = !Topmost);
      StartDragCommand = new RelayCommand(_ => { try { DragMove(); } catch { } });
      CloseCommand = new RelayCommand(_ => Close());

      Loaded += (_, __) => {
        if(Application.Current?.MainWindow is Window mw) mw.Hide();
      };
      Closed += (_, __) => {
        if(Application.Current?.MainWindow is Window mw) mw.Show();
      };

      if(DataContext == null && Application.Current?.MainWindow is Window mw2)
        DataContext = mw2.DataContext;
    }
  }
}
