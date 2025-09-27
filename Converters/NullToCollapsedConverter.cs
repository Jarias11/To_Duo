using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System;
using System.Windows;

namespace TaskMate.Converters {

	public sealed class NullToCollapsedConverter : IMultiValueConverter, IValueConverter {
		public static NullToCollapsedConverter Instance { get; } = new();
		public object Convert(object v, Type t, object p, CultureInfo c)
			=> string.IsNullOrWhiteSpace(v as string) ? Visibility.Collapsed : Visibility.Visible;
		public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
		public object Convert(object[] values, Type t, object p, CultureInfo c) => Convert(values.FirstOrDefault(), t, p, c);
		public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => throw new NotSupportedException();
	}
}