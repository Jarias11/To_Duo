// Views/ActivityCenterSection.xaml.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
namespace TaskMate.Views {
	public partial class ActivityCenterSection : UserControl {
		public ActivityCenterSection() { InitializeComponent(); }
		private void ReactionButton_Click(object sender, RoutedEventArgs e) {
			if(sender is not FrameworkElement fe)
				return;

			// Walk the *logical* tree upwards until we find the Popup
			DependencyObject current = fe;
			while(current != null && current is not Popup) {
				current = LogicalTreeHelper.GetParent(current);
			}

			if(current is Popup popup) {
				popup.IsOpen = false;  // close immediately after click
			}
		}
	}
}