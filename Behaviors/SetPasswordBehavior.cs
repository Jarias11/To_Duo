using System.Windows;
using System.Windows.Controls;

namespace TaskMate.Behaviors {
    public static class SetPasswordBehavior {
        public static readonly DependencyProperty SetProperty =
            DependencyProperty.RegisterAttached("Set", typeof(object), typeof(SetPasswordBehavior),
                new PropertyMetadata(null, OnChanged));
        public static void SetSet(DependencyObject d, object v) => d.SetValue(SetProperty, v);
        public static object GetSet(DependencyObject d) => d.GetValue(SetProperty);

        public static readonly DependencyProperty PathProperty =
            DependencyProperty.RegisterAttached("Path", typeof(string), typeof(SetPasswordBehavior),
                new PropertyMetadata("Password"));
        public static void SetPath(DependencyObject d, string v) => d.SetValue(PathProperty, v);
        public static string GetPath(DependencyObject d) => (string)d.GetValue(PathProperty);

        private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is PasswordBox pb) {
                pb.PasswordChanged -= Handle;
                pb.PasswordChanged += Handle;
            }
        }

        private static void Handle(object sender, RoutedEventArgs e) {
            if (sender is not PasswordBox pb) return;
            var ctx = pb.DataContext; if (ctx is null) return;
            ctx.GetType().GetProperty(GetPath(pb))?.SetValue(ctx, pb.Password);
        }
    }
}
