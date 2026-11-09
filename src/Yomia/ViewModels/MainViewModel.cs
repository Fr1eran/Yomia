using System;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Yomia.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public MainViewModel()
    {
        CurrentPage = HomeVm;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentRoute))]
    public partial ViewModelBase? CurrentPage { get; set; }

    public string? CurrentRoute => CurrentPage switch
    {
        HomeViewModel => "Home",
        LibraryViewModel => "Library",
        BookmarksViewModel => "Bookmarks",
        HistoryViewModel => "History",
        SettingsViewModel => "Settings",
        _ => null
    };

    private HomeViewModel HomeVm { get; } = new();
    private LibraryViewModel LibraryVm { get; } = new();
    private BookmarksViewModel BookmarksVm { get; } = new();
    private HistoryViewModel HistoryVm { get; } = new();
    private SettingsViewModel SettingsVm { get; } = new();

    [RelayCommand]
    private void Navigate(AppSection section)
    {
        CurrentPage = section switch
        {
            AppSection.Home => HomeVm,
            AppSection.Library => LibraryVm,
            AppSection.Bookmarks => BookmarksVm,
            AppSection.History => HistoryVm,
            AppSection.Settings => SettingsVm,
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, "Unknown section")
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
