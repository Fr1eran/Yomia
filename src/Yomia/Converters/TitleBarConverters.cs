using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Styling;

namespace Yomia.Converters;

public class TitleBarConverters
{
    public static readonly IValueConverter IsDarkTheme =
        new FuncValueConverter<ThemeVariant, bool>(theme => theme == ThemeVariant.Dark);

    public static readonly IValueConverter IsNotDarkTheme =
        new FuncValueConverter<ThemeVariant, bool>(theme => theme != ThemeVariant.Dark);

    public static readonly IValueConverter IsFullScreen =
        new FuncValueConverter<WindowState, bool>(state => state == WindowState.FullScreen);

    public static readonly IValueConverter IsNotFullScreen =
        new FuncValueConverter<WindowState, bool>(state => state != WindowState.FullScreen);

    public static readonly IValueConverter ToggleFullscreenTips =
        new FuncValueConverter<WindowState, string>(state =>
            state == WindowState.FullScreen ? "Exit Fullscreen (Esc)" : "Enter Fullscreen");
}
