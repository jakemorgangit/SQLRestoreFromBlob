using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SQLRestoreFromBlob.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool invert = parameter?.ToString() == "Invert";
        bool flag = value is bool b && b;
        if (invert) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}

public class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not long bytes) return "—";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:F1} {units[unit]}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool invert = parameter?.ToString() == "Invert";
        bool isNull = value is null || (value is string s && string.IsNullOrEmpty(s));
        if (invert) isNull = !isNull;
        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToBrushConverter : IValueConverter
{
    public Brush? TrueBrush { get; set; }
    public Brush? FalseBrush { get; set; }

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? TrueBrush : FalseBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BackupTypeToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var type = value?.ToString() ?? "";
        return type switch
        {
            "Full" => new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xD9)),
            "Differential" => new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12)),
            "TransactionLog" => new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60)),
            _ => new SolidColorBrush(Color.FromRgb(0x88, 0x90, 0xA4))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class DateTimeToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateTime dt)
            return dt.ToString("yyyy-MM-dd HH:mm:ss");
        if (value is DateTimeOffset dto)
            return dto.ToString("yyyy-MM-dd HH:mm:ss");
        return "—";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class EnumToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter is not string paramStr) return false;
        return value?.ToString() == paramStr;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter is string paramStr && targetType.IsEnum)
            return Enum.Parse(targetType, paramStr);
        return Binding.DoNothing;
    }
}
