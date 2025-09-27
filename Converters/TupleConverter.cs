using System;
using System.Globalization;
using System.Windows.Data;
using TaskMate.Models;

namespace TaskMate.Converters {
    public sealed class TupleConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => new Tuple<ActivityEntry, string>((ActivityEntry)value, parameter?.ToString() ?? "");
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}