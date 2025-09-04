using ScratchShell.UserControls.ThemeControl;
using System.Globalization;
using System.Windows.Data;

namespace ScratchShell.Helpers;

/// <summary>
/// Converter to check if a theme template is the selected one and return Visibility
/// </summary>
public class ThemeIsSelectedToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 2 && values[0] is ThemeTemplate current && values[1] is ThemeTemplate selected)
        {
            return ReferenceEquals(current, selected) ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
