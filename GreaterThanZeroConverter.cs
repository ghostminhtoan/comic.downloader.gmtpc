using System;
using System.Globalization;
using System.Windows.Data;

namespace get_link_manga
{
    /// <summary>
    /// Converter that returns true when int value > 0. Used for error count visibility.
    /// </summary>
    public class GreaterThanZeroConverter : IValueConverter
    {
        public static readonly GreaterThanZeroConverter Instance = new GreaterThanZeroConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int intValue)
                return intValue > 0;
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
