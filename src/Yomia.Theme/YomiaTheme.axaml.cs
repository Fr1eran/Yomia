using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Yomia.Theme;

public class YomiaTheme : Styles
{
    public YomiaTheme(IServiceProvider? serviceProvider = null)
    {
        AvaloniaXamlLoader.Load(serviceProvider, this);
    }
}
