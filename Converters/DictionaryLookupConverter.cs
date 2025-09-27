using System;
using System.Collections;
using System.Globalization;
using System.Windows.Data;

namespace TaskMate.Converters {
	public class DictionaryLookupConverter : IMultiValueConverter {
		public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
			if(values == null || values.Length < 2) return null;

			var dict = values[0] as IDictionary;
			var key = values[1]?.ToString();
			if(dict != null && key != null && dict.Contains(key))
				return dict[key];

			return null; // or string.Empty
		}

		public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
			=> throw new NotSupportedException();
	}
}