using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TaskMate.Services {
    public enum AppTheme { Light, Dark }

    public interface IThemeService {
        AppTheme Current { get; }
        void Apply(AppTheme theme);
        AppTheme Toggle();
    }

    public sealed class ThemeService : IThemeService {
        private const string LightDict = "Themes/Theme.Light.xaml";
        private const string DarkDict = "Themes/Theme.Dark.xaml";

        public AppTheme Current { get; private set; } = AppTheme.Light;

        public void Apply(AppTheme theme) {
            var app = Application.Current;
            if(app == null) return;

            // swap dictionaries
            var md = app.Resources.MergedDictionaries;
            for(int i = md.Count - 1; i >= 0; i--) {
                var src = md[i].Source?.OriginalString ?? "";
                if(src.EndsWith("Theme.Light.xaml", StringComparison.OrdinalIgnoreCase) ||
                    src.EndsWith("Theme.Dark.xaml", StringComparison.OrdinalIgnoreCase)) {
                    md.RemoveAt(i);
                }
            }
            var uri = new Uri(theme == AppTheme.Dark ? DarkDict : LightDict, UriKind.Relative);
            md.Insert(0, new ResourceDictionary { Source = uri });

            // point the alias to the theme-specific brush
            var themedKey = theme == AppTheme.Dark ? "NotebookPaperBrush.Dark" : "NotebookPaperBrush.Light";
            app.Resources["NotebookPaperBrush"] = (Brush)app.TryFindResource(themedKey)!;

            Current = theme;
        }

        public AppTheme Toggle() {
            var next = Current == AppTheme.Light ? AppTheme.Dark : AppTheme.Light;
            Apply(next);
            return next;
        }
    }
}
