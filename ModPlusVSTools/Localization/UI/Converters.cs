using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ModPlusVSTools.Localization.UI
{
    /// <summary>
    /// <c>true</c> → Collapsed, <c>false</c> → Visible.
    /// </summary>
    public sealed class InverseBoolToVisibilityConverter : IValueConverter
    {
        /// <summary>Экземпляр.</summary>
        public static readonly InverseBoolToVisibilityConverter Instance = new InverseBoolToVisibilityConverter();

        /// <inheritdoc/>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is true ? Visibility.Collapsed : Visibility.Visible;

        /// <inheritdoc/>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Число → Visible, если больше нуля (иначе Collapsed). С параметром <c>Invert</c> — наоборот.
    /// </summary>
    public sealed class CountToVisibilityConverter : IValueConverter
    {
        /// <inheritdoc/>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var visible = value is int count && count > 0;
            if (parameter as string == "Invert")
                visible = !visible;

            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <inheritdoc/>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Инверсия bool.
    /// </summary>
    public sealed class InverseBoolConverter : IValueConverter
    {
        /// <summary>Экземпляр.</summary>
        public static readonly InverseBoolConverter Instance = new InverseBoolConverter();

        /// <inheritdoc/>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => !(value is true);

        /// <inheritdoc/>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => !(value is true);
    }

    /// <summary>
    /// Не <c>null</c> → <c>true</c>.
    /// </summary>
    public sealed class NullToBoolConverter : IValueConverter
    {
        /// <summary>Экземпляр.</summary>
        public static readonly NullToBoolConverter Instance = new NullToBoolConverter();

        /// <inheritdoc/>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value != null;

        /// <inheritdoc/>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
