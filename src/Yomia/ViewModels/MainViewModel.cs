using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Yomia.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CurrentRoute))]
    private ViewModelBase? _currentPage;

    public MainViewModel()
    {
        _currentPage = HomeVM;
    }

    public string? CurrentRoute => CurrentPage switch
    {
        HomeViewModel => "Home",
        LibraryViewModel => "Library",
        BookmarksViewModel => "Bookmarks",
        HistoryViewModel => "History",
        SettingsViewModel => "Settings",
        _ => null
    };

    public HomeViewModel HomeVM { get; } = new();
    public LibraryViewModel LibraryVM { get; } = new();
    public BookmarksViewModel BookmarksVM { get; } = new();
    public HistoryViewModel HistoryVM { get; } = new();
    public SettingsViewModel SettingsVM { get; } = new();

    [RelayCommand]
    private void Navigate(string page)
    {
        CurrentPage = page switch
        {
            "Home" => HomeVM,
            "Library" => LibraryVM,
            "Bookmarks" => BookmarksVM,
            "History" => HistoryVM,
            "Settings" => SettingsVM,
            _ => HomeVM
        };
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        var app = Application.Current;
        if (app != null)
            app.RequestedThemeVariant =
                app.ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
    }
}
