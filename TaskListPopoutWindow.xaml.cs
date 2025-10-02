// TaskListPopoutWindow.xaml.cs
using System.Collections;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;   // <-- NEW (for DragMove + MouseButtonEventArgs)

namespace TaskMate.Views {
  public partial class TaskListPopoutWindow : Window {

    public static readonly DependencyProperty PopoutTitleProperty =
      DependencyProperty.Register(nameof(PopoutTitle), typeof(string),
        typeof(TaskListPopoutWindow), new PropertyMetadata(default(string)));

    public static readonly DependencyProperty PopoutItemsProperty =
      DependencyProperty.Register(nameof(PopoutItems), typeof(IEnumerable),
        typeof(TaskListPopoutWindow), new PropertyMetadata(null));

    // background brush for the colored sub-card
    public static readonly DependencyProperty PopoutBackgroundProperty =
      DependencyProperty.Register(nameof(PopoutBackground), typeof(Brush),
        typeof(TaskListPopoutWindow), new PropertyMetadata(null));

    public string PopoutTitle {
      get => (string)GetValue(PopoutTitleProperty);
      set => SetValue(PopoutTitleProperty, value);
    }

    public IEnumerable PopoutItems {
      get => (IEnumerable)GetValue(PopoutItemsProperty);
      set => SetValue(PopoutItemsProperty, value);
    }

    public Brush PopoutBackground {
      get => (Brush)GetValue(PopoutBackgroundProperty);
      set => SetValue(PopoutBackgroundProperty, value);
    }
    public static readonly DependencyProperty PopoutHeaderBackgroundProperty =
    DependencyProperty.Register(nameof(PopoutHeaderBackground), typeof(Brush),
        typeof(TaskListPopoutWindow), new PropertyMetadata(null));

    public Brush PopoutHeaderBackground {
      get => (Brush)GetValue(PopoutHeaderBackgroundProperty);
      set => SetValue(PopoutHeaderBackgroundProperty, value);
    }

    public TaskListPopoutWindow() {
      InitializeComponent();

      // --- NEW: make this feel like a popout that stays tied to the app ---
      Owner = Application.Current?.MainWindow;                  // center relative to main
      WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    // --- NEW: handlers used by the sticky-note header UI in XAML ---

    // Drag the window when the header (“tape”) is grabbed
    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
      if(e.LeftButton == MouseButtonState.Pressed)
        DragMove();
    }

    // Close button on the header
    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    // “Pin on top” toggle
    private void TopmostToggle_OnChecked(object sender, RoutedEventArgs e) => Topmost = true;
    private void TopmostToggle_OnUnchecked(object sender, RoutedEventArgs e) => Topmost = false;
  }
}