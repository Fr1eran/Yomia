using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Window = ShadUI.Window;

namespace Yomia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnToggleFullScreen(object? sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.FullScreen)
            ExitFullScreen();
        else
            WindowState = WindowState.FullScreen;
    }

    private async void OnOpenComic(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Comic",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Comic archives")
                {
                    Patterns = ["*.cbz", "*.cbr", "*.cb7", "*.zip", "*.rar", "*.7z"]
                },
                FilePickerFileTypes.All
            ]
        });

        if (files.Count == 0)
        {
            return;
        }

        using var file = files[0];
        var path = file.TryGetLocalPath();

        System.Diagnostics.Debug.WriteLine(path ?? "The selected item has no local path.");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!e.Handled && e.Key == Key.Escape && WindowState == WindowState.FullScreen)
        {
            ExitFullScreen();
            e.Handled = true;
        }
    }
}
