using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace TradeOverlay.Converters;

/// <summary>Returns Red brush when over limit, Green brush otherwise.</summary>
public class TradeCountColorConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2
            && values[0] is int count
            && values[1] is int max)
        {
            if (count > max)
                return new SolidColorBrush(Color.FromRgb(0xFF, 0x17, 0x44));   // red
            if (count >= max)
                return new SolidColorBrush(Color.FromRgb(0xFF, 0x91, 0x00));   // orange warning
            return new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));       // green
        }
        return Brushes.White;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Bool → Visibility.</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Inverted Bool → Visibility.</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Returns Green for positive P&L, Red for negative, White for zero.</summary>
public class PnLColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal d)
        {
            if (d > 0) return new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)); // green
            if (d < 0) return new SolidColorBrush(Color.FromRgb(0xFF, 0x17, 0x44)); // red
        }
        return new SolidColorBrush(Color.FromRgb(0xEA, 0xEA, 0xEA)); // neutral white
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Formats a decimal as ₹ currency.
/// Negative values show as -₹1,234.56 (not ₹-1,234.56).
/// </summary>
public class CurrencyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal d)
            return d < 0 ? $"-₹{Math.Abs(d):N2}" : $"₹{d:N2}";
        return "₹0.00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Returns Visible when a string is non-empty, Collapsed otherwise.</summary>
public class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrWhiteSpace(s)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
