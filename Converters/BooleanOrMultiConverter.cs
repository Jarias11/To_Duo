using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
namespace TaskMate.Converters {
	public sealed class BooleanOrMultiConverter : IMultiValueConverter {
		public static BooleanOrMultiConverter Instance { get; } = new();
		public object Convert(object[] values, Type t, object p, CultureInfo c)
			=> values.Any(v => v is string s && !string.IsNullOrWhiteSpace(s));
		public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => throw new NotSupportedException();
	}
}

    