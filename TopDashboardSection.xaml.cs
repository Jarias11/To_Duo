using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TaskMate.Views {
	public partial class TopDashboardSection : UserControl {
		public TopDashboardSection() {
			InitializeComponent();

			// When the template is ready, find the Tabs inside it.
			QuickToolsExpander.Loaded += (_, __) => {
				var tabs = QuickToolsExpander.Template?
					.FindName("Tabs", QuickToolsExpander) as TabControl;

				if(tabs == null) return;

				// 1) Selecting any tab expands the section
				tabs.SelectionChanged += (_, __2) => {
					if(!QuickToolsExpander.IsExpanded)
						QuickToolsExpander.IsExpanded = true;
				};

				// 2) Clicking the *already selected* tab toggles expand/collapse
				tabs.PreviewMouseLeftButtonDown += (s, e) => {
					var fe = e.OriginalSource as DependencyObject;
					var tabItem = FindAncestor<TabItem>(fe);
					if(tabItem != null && tabItem.IsSelected) {
						QuickToolsExpander.IsExpanded = !QuickToolsExpander.IsExpanded;
						e.Handled = true; // prevents reselect flicker
					}
				};
			};
		}

		private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject {
			while(start != null && start is not T)
				start = VisualTreeHelper.GetParent(start);
			return start as T;
		}
	}
}