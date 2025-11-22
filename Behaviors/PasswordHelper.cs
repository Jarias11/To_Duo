using System.Windows;
using System.Windows.Controls;

namespace TaskMate.Behaviors {
    public static class PasswordHelper {
        public static readonly DependencyProperty BoundPasswordProperty =
            DependencyProperty.RegisterAttached(
                "BoundPassword",
                typeof(string),
                typeof(PasswordHelper),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

        public static readonly DependencyProperty BindPasswordProperty =
            DependencyProperty.RegisterAttached(
                "BindPassword",
                typeof(bool),
                typeof(PasswordHelper),
                new PropertyMetadata(false, OnBindPasswordChanged));

        private static readonly DependencyProperty UpdatingPasswordProperty =
            DependencyProperty.RegisterAttached(
                "UpdatingPassword",
                typeof(bool),
                typeof(PasswordHelper),
                new PropertyMetadata(false));

        public static string GetBoundPassword(DependencyObject obj) => (string)obj.GetValue(BoundPasswordProperty);
        public static void SetBoundPassword(DependencyObject obj, string value) => obj.SetValue(BoundPasswordProperty, value);

        public static bool GetBindPassword(DependencyObject obj) => (bool)obj.GetValue(BindPasswordProperty);
        public static void SetBindPassword(DependencyObject obj, bool value) => obj.SetValue(BindPasswordProperty, value);

        private static bool GetUpdatingPassword(DependencyObject obj) => (bool)obj.GetValue(UpdatingPasswordProperty);
        private static void SetUpdatingPassword(DependencyObject obj, bool value) => obj.SetValue(UpdatingPasswordProperty, value);

        private static void OnBindPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is PasswordBox pb) {
                if ((bool)e.NewValue) {
                    pb.PasswordChanged += HandlePasswordChanged;
                } else {
                    pb.PasswordChanged -= HandlePasswordChanged;
                }
            }
        }

        private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is PasswordBox pb) {
                pb.PasswordChanged -= HandlePasswordChanged;
                if (!GetUpdatingPassword(pb)) {
                    pb.Password = e.NewValue?.ToString() ?? string.Empty;
                }
                pb.PasswordChanged += HandlePasswordChanged;
            }
        }

        private static void HandlePasswordChanged(object sender, RoutedEventArgs e) {
            var pb = (PasswordBox)sender;
            SetUpdatingPassword(pb, true);
            SetBoundPassword(pb, pb.Password);
            SetUpdatingPassword(pb, false);
        }
    }
}
