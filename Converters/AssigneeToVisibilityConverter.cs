using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TaskMate.Converters
{
    public class AssigneeToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
{
    Console.WriteLine($"[Converter] Value={value}, Param={parameter}");
    
    if (value == null || parameter == null)
        return Visibility.Collapsed;

    return value.ToString() == parameter.ToString()
        ? Visibility.Visible
        : Visibility.Collapsed;
}

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}