using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
