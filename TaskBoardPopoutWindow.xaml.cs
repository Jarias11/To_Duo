using System.Windows;
using System.Windows.Input;
using TaskMate.ViewModels;
namespace TaskMate.Views {
	public partial class TaskBoardPopoutWindow : Window {
		public ICommand TogglePinCommand { get; }
		public ICommand StartDragCommand { get; }
		public ICommand CloseCommand { get; }

		public TaskBoardPopoutWindow() {
			InitializeComponent();

			Topmost = true; // default pinned
			TogglePinCommand = new RelayCommand(_ => Topmost = !Topmost);
			StartDragCommand = new RelayCommand(_ => { try { DragMove(); } catch { } });
			CloseCommand = new RelayCommand(_ => Close());

			Loaded += (_, __) => {
				if(Application.Current?.MainWindow is Window mw) mw.Hide();
				// inherit DataContext if not set by caller
				if(DataContext == null && Application.Current?.MainWindow is Window mw2)
					DataContext = mw2.DataContext;
			};
			Closed += (_, __) => {
				if(Application.Current?.MainWindow is Window mw) mw.Show();
			};
		}
	}
}
