using System.Windows;

namespace TaskMate.Views {
	public partial class TaskBoardPopoutWindow : Window {
		public TaskBoardPopoutWindow() {
			InitializeComponent();
			Owner = Application.Current?.MainWindow;
		}
	}
}