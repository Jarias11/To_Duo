using System;
using System.Linq;
using System.Windows;

namespace TaskMate.Services
{
    public enum AppTheme { Light, Dark }

    public interface IThemeService
    {
        AppTheme Current { get; }
        void Apply(AppTheme theme);
        AppTheme Toggle();
    }

    public sealed class ThemeService : IThemeService
    {
        private const string LightDict = "Themes/Theme.Light.xaml";
        private const string DarkDict  = "Themes/Theme.Dark.xaml";

        public AppTheme Current { get; private set; } = AppTheme.Light;

        public void Apply(AppTheme theme)
        {
            var app = Application.Current;
            if (app == null) return;

            var targetUri = new Uri(theme == AppTheme.Dark ? DarkDict : LightDict, UriKind.Relative);

            // Remove any previous theme dictionaries
            var md = app.Resources.MergedDictionaries;
            var oldThemes = md.Where(d =>
                d.Source != null &&
                (d.Source.OriginalString.EndsWith("Theme.Light.xaml", StringComparison.OrdinalIgnoreCase) ||
                 d.Source.OriginalString.EndsWith("Theme.Dark.xaml",  StringComparison.OrdinalIgnoreCase)))
                .ToList();
            foreach (var d in oldThemes) md.Remove(d);

            // Add the new one
            md.Add(new ResourceDictionary { Source = targetUri });
            Current = theme;
        }

        public AppTheme Toggle()
        {
            var next = Current == AppTheme.Light ? AppTheme.Dark : AppTheme.Light;
            Apply(next);
            return next;
        }
    }
}