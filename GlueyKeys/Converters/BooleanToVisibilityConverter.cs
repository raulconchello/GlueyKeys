using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GlueyKeys.Converters;

public class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            // Check if we should invert the logic
            bool invert = parameter is string paramStr && paramStr.Equals("Invert", StringComparison.OrdinalIgnoreCase);

            if (invert)
                boolValue = !boolValue;

            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            bool result = visibility == Visibility.Visible;

            bool invert = parameter is string paramStr && paramStr.Equals("Invert", StringComparison.OrdinalIgnoreCase);

            return invert ? !result : result;
        }

        return false;
    }
}
