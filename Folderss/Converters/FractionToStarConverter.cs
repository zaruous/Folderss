using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Folderss.Converters
{
    public sealed class FractionToStarConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var fraction = value is double ? (double)value : 0;
            if (fraction < 0)
                fraction = 0;

            return new GridLength(fraction, GridUnitType.Star);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
