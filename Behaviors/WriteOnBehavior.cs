using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace TaskMate.Behaviors {
	public static class WriteOnBehavior {
		// ===== Attached Props =====
		public static readonly DependencyProperty EnabledProperty =
			DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(WriteOnBehavior),
				new PropertyMetadata(false, OnEnabledChanged));
		public static void SetEnabled(DependencyObject d, bool v) => d.SetValue(EnabledProperty, v);
		public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);

		public static readonly DependencyProperty DurationProperty =
			DependencyProperty.RegisterAttached("Duration", typeof(TimeSpan), typeof(WriteOnBehavior),
				new PropertyMetadata(TimeSpan.FromSeconds(1.4)));
		public static void SetDuration(DependencyObject d, TimeSpan v) => d.SetValue(DurationProperty, v);
		public static TimeSpan GetDuration(DependencyObject d) => (TimeSpan)d.GetValue(DurationProperty);

		public static readonly DependencyProperty DelayProperty =
			DependencyProperty.RegisterAttached("Delay", typeof(TimeSpan), typeof(WriteOnBehavior),
				new PropertyMetadata(TimeSpan.Zero));
		public static void SetDelay(DependencyObject d, TimeSpan v) => d.SetValue(DelayProperty, v);
		public static TimeSpan GetDelay(DependencyObject d) => (TimeSpan)d.GetValue(DelayProperty);

		public static readonly DependencyProperty ShowPenTipProperty =
			DependencyProperty.RegisterAttached("ShowPenTip", typeof(bool), typeof(WriteOnBehavior),
				new PropertyMetadata(true));
		public static void SetShowPenTip(DependencyObject d, bool v) => d.SetValue(ShowPenTipProperty, v);
		public static bool GetShowPenTip(DependencyObject d) => (bool)d.GetValue(ShowPenTipProperty);

		public static readonly DependencyProperty PenTipBrushProperty =
			DependencyProperty.RegisterAttached("PenTipBrush", typeof(Brush), typeof(WriteOnBehavior),
				new PropertyMetadata(Brushes.Black));
		public static void SetPenTipBrush(DependencyObject d, Brush v) => d.SetValue(PenTipBrushProperty, v);
		public static Brush GetPenTipBrush(DependencyObject d) => (Brush)d.GetValue(PenTipBrushProperty);

		public static readonly DependencyProperty EasingProperty =
			DependencyProperty.RegisterAttached("Easing", typeof(IEasingFunction), typeof(WriteOnBehavior),
				new PropertyMetadata(new CubicEase { EasingMode = EasingMode.EaseInOut }));
		public static void SetEasing(DependencyObject d, IEasingFunction v) => d.SetValue(EasingProperty, v);
		public static IEasingFunction GetEasing(DependencyObject d) => (IEasingFunction)d.GetValue(EasingProperty);

		private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
			if(d is not TextBlock tb) return;

			void Run() {
				if(!tb.IsLoaded) return;

				// measure to get a stable ActualWidth/Height
				tb.UpdateLayout();
				double w = Math.Max(1, tb.ActualWidth);
				double h = Math.Max(1, tb.ActualHeight);
				double pad = Math.Ceiling(tb.FontSize * 0.2);
				var finalRect = new Rect(-pad * 0.25, 0, w + pad, h);

				// 1) Clip that grows from 0 -> full width
				var clip = new RectangleGeometry(new Rect(finalRect.X, 0, 0, h));
				tb.Clip = clip;

				var dur = GetDuration(tb);
				var delay = GetDelay(tb);
				var ease = GetEasing(tb);

				var anim = new RectAnimation(
					new Rect(finalRect.X, 0, 0, h),   // start at padded X, zero width
					finalRect,                        // grow to padded rect
					new Duration(dur)
				) {
					BeginTime = delay,
					EasingFunction = ease,
					FillBehavior = FillBehavior.Stop
				};

				// keep the clip at final size after animation completes
				anim.Completed += (_, __) => tb.Clip = null;
				clip.BeginAnimation(RectangleGeometry.RectProperty, anim);


				// 2) Optional “pen tip” that travels with the clip edge
				if(GetShowPenTip(tb)) {
					var host = Window.GetWindow(tb);
					var layer = LogicalTreeHelper.FindLogicalNode(host, "EffectsLayer") as Canvas;
					if(layer != null) {
						// size + look
						var tip = new Ellipse {
							Width = Math.Max(4, h * 0.12),
							Height = Math.Max(4, h * 0.12),
							Fill = GetPenTipBrush(tb),
							IsHitTestVisible = false,
							Opacity = 0.95,
						};
						Panel.SetZIndex(tip, 1000);

						// absolute coordinates of the TextBlock in the overlay layer
						var tl = tb.TranslatePoint(new Point(0, 0), layer);
						// same baseline-ish Y you had, but in layer coords
						double baseY = tl.Y + h * 0.4;

						// include our left padding offset (finalRect.X) so the dot starts slightly before the text
						double startX = tl.X + finalRect.X;
						double endX = startX + finalRect.Width;

						// amplitude of the bob (~2–6px depending on font size)
						double amp = Math.Max(6, h * 0.1);

						Canvas.SetLeft(tip, startX);
						Canvas.SetTop(tip, baseY);
						layer.Children.Add(tip);

						var tipX = new DoubleAnimation(startX, endX, new Duration(dur)) {
							BeginTime = delay,
							EasingFunction = ease
						};
						var tipFade = new DoubleAnimation(0.95, 0.0,
							new Duration(TimeSpan.FromMilliseconds(dur.TotalMilliseconds * 0.25))) {
							BeginTime = delay + TimeSpan.FromMilliseconds(dur.TotalMilliseconds * 0.80)
						};
						var tipY = new DoubleAnimationUsingKeyFrames {
							BeginTime = delay,
							Duration = new Duration(dur)
						};
						// five gentle waves across the write
						tipY.KeyFrames.Add(new EasingDoubleKeyFrame(baseY - amp * 0.50, KeyTime.FromPercent(0.00), ease));
						tipY.KeyFrames.Add(new EasingDoubleKeyFrame(baseY + amp * 0.30, KeyTime.FromPercent(0.20), ease));
						tipY.KeyFrames.Add(new EasingDoubleKeyFrame(baseY - amp * 0.25, KeyTime.FromPercent(0.40), ease));
						tipY.KeyFrames.Add(new EasingDoubleKeyFrame(baseY + amp * 0.20, KeyTime.FromPercent(0.60), ease));
						tipY.KeyFrames.Add(new EasingDoubleKeyFrame(baseY - amp * 0.15, KeyTime.FromPercent(0.80), ease));
						tipY.KeyFrames.Add(new EasingDoubleKeyFrame(baseY, KeyTime.FromPercent(1.00), ease));


						tip.BeginAnimation(UIElement.OpacityProperty, tipFade);
						tip.BeginAnimation(Canvas.LeftProperty, tipX);
						tip.BeginAnimation(Canvas.TopProperty, tipY);

						tipFade.Completed += (_, __) => layer.Children.Remove(tip);
					}
				}
			}

			if((bool)e.NewValue) {
				if(tb.IsLoaded) {
					Run();
				}
				else {
					tb.Loaded += OnLoadedOnce;
				}
			}
			else {
				// disabled: remove clip
				tb.Clip = null;
			}

			void OnLoadedOnce(object? s, RoutedEventArgs args) {
				tb.Loaded -= OnLoadedOnce;
				Run();
			}
		}
	}
}
