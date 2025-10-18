using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace TaskMate.Views {
	public partial class TopDashboardSection : UserControl {
		public TopDashboardSection() {
			InitializeComponent();
		}
		private CustomPopupPlacement[] OnPopupPlacement(Size popupSize, Size targetSize, Point offset) {
			const double margin = 8;   // padding from window edges
			const double gap = 3;   // gap between button and popup

			var win = Window.GetWindow(this);
			if(win == null)
				return new[] { new CustomPopupPlacement(new Point(0, targetSize.Height + gap), PopupPrimaryAxis.None) };

			// Button position relative to window
			var targetTopLeft = SettingsBtn.TransformToAncestor(win).Transform(new Point(0, 0));

			// Default: right-aligned UNDER the button
			double x = targetSize.Width - popupSize.Width; // align right edges
			double y = targetSize.Height + gap;

			// Keep inside window horizontally
			double minX = margin - targetTopLeft.X;
			double maxX = win.ActualWidth - popupSize.Width - margin - targetTopLeft.X;
			x = Math.Max(minX, Math.Min(x, maxX));

			// If there isn't enough room below, flip ABOVE the button
			bool fitsBelow = (targetTopLeft.Y + targetSize.Height + gap + popupSize.Height) <= (win.ActualHeight - margin);
			if(!fitsBelow)
				y = -popupSize.Height - gap;

			// Also ensure the top doesn't go off-screen when flipped
			double minY = margin - targetTopLeft.Y;
			if(y < minY) y = minY;

			return new[] { new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.Vertical) };
		}
	}
}