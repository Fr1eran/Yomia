using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Yomia.ViewModels;
using Yomia.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Yomia;

public class App : Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            services.AddTransient<HomeViewModel>();
            services.AddTransient<LibraryViewModel>();
            services.AddTransient<BookmarksViewModel>();
            services.AddTransient<HistoryViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<MainViewModel>();

            _serviceProvider = services.BuildServiceProvider();

            desktop.MainWindow = new MainWindow
            {
                DataContext = _serviceProvider.GetRequiredService<MainViewModel>(),
            };
        }


        base.OnFrameworkInitializationCompleted();
    }
}
