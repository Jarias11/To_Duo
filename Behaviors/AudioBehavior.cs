using System.Windows;
using System.Windows.Input;
using TaskMate.Services;

namespace TaskMate.Behaviors {
	public static class AudioBehavior {
		public static readonly DependencyProperty HoverSoundProperty =
			DependencyProperty.RegisterAttached(
				"HoverSound",
				typeof(bool),
				typeof(AudioBehavior),
				new PropertyMetadata(false, OnHoverSoundChanged));

		public static bool GetHoverSound(DependencyObject obj) =>
			(bool)obj.GetValue(HoverSoundProperty);

		public static void SetHoverSound(DependencyObject obj, bool value) =>
			obj.SetValue(HoverSoundProperty, value);

		private static void OnHoverSoundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
			if(d is UIElement element) {
				if((bool)e.NewValue) {
					element.MouseEnter += Element_MouseEnter;
				}
				else {
					element.MouseEnter -= Element_MouseEnter;
				}
			}
		}

		private static void Element_MouseEnter(object sender, MouseEventArgs e) {
			SoundService.PlayHover();
		}
	}
}
