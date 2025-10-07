using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TaskMate.Behaviors {
	public static class TabCloseOnDoubleClickBehavior {
		public static readonly DependencyProperty EnabledProperty =
			DependencyProperty.RegisterAttached(
				"Enabled",
				typeof(bool),
				typeof(TabCloseOnDoubleClickBehavior),
				new PropertyMetadata(false, OnEnabledChanged));

		public static void SetEnabled(DependencyObject obj, bool value) => obj.SetValue(EnabledProperty, value);
		public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);

		public static readonly DependencyProperty IsCollapsedProperty =
			DependencyProperty.RegisterAttached(
				"IsCollapsed",
				typeof(bool),
				typeof(TabCloseOnDoubleClickBehavior),
				new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

		public static void SetIsCollapsed(DependencyObject obj, bool value) => obj.SetValue(IsCollapsedProperty, value);
		public static bool GetIsCollapsed(DependencyObject obj) => (bool)obj.GetValue(IsCollapsedProperty);

		private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
			if(d is not TabControl tab) return;

			if((bool)e.NewValue)
				tab.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
			else
				tab.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
		}

		private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
			if(sender is not TabControl tab) return;

			// Only react when the header (a TabItem) was clicked
			var item = FindParent<TabItem>(e.OriginalSource as DependencyObject);
			if(item is null) return;

			var collapsed = GetIsCollapsed(tab);

			// Double-click on the selected pill toggles collapsed
			if(e.ClickCount == 2 && item.IsSelected) {
				SetIsCollapsed(tab, !collapsed);
				return; // don't interfere with selection
			}

			// Single-click while collapsed: reopen immediately if you clicked the selected pill
			if(e.ClickCount == 1 && collapsed) {
				// If you clicked a different pill, selection will change anyway
				// If you clicked the same selected pill, just reopen
				SetIsCollapsed(tab, false);
			}
		}

		private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject {
			while(child != null && child is not T)
				child = VisualTreeHelper.GetParent(child);
			return child as T;
		}
	}
}
