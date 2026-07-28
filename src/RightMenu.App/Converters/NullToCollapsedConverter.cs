using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RightMenu.App.Converters;

/// <summary>null → Collapsed；非 null → Visible。用于图标占位与可选字段。</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
