using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Layout;

namespace Yomia.Converters;

public static class SidebarConverters
{
    public static readonly IValueConverter Padding =
        new FuncValueConverter<bool, Thickness>(expanded =>
            expanded ? new Thickness(16, 8) : new Thickness(8));

    public static readonly IValueConverter ToggleAlignment =
        new FuncValueConverter<bool, HorizontalAlignment>(expanded =>
            expanded ? HorizontalAlignment.Right : HorizontalAlignment.Center);

    public static readonly IValueConverter CollapsedToolTip =
        new FuncValueConverter<bool, string?, string?>((expanded, text) => expanded ? null : text);
}
