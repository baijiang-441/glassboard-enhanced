using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace GlassBoard;

internal sealed class GroupDisplay : IValueConverter
{
    private readonly Preferences prefs;
    private GroupDisplay(Preferences prefs) { this.prefs = prefs; }
    public object Convert(object value, Type target, object parameter, CultureInfo culture) => prefs.GroupLabel(value as string);
    public object ConvertBack(object value, Type target, object parameter, CultureInfo culture) => Binding.DoNothing;
    public static DataTemplate Template(Preferences prefs)
    {
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding { Converter = new GroupDisplay(prefs) });
        return new DataTemplate { VisualTree = label };
    }
}
