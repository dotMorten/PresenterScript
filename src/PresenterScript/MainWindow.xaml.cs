using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace PresenterScript;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Closing += OnClosing;

        // Restores size, position and monitor (e.g. the off-screen presenter display) between sessions.
        WinUIEx.WindowManager.Get(this).PersistenceId = "MainWindow";

        RootFrame.Navigate(typeof(MainPage));
        Page.ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => HelpWindow.CloseIfOpen();
        UpdateTitle();
    }

    private MainPage Page => (MainPage)RootFrame.Content;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.MainViewModel.DisplayTitle))
        {
            UpdateTitle();
        }
    }

    private void UpdateTitle()
    {
        Title = Page.ViewModel.DisplayTitle;
        AppTitleBar.Title = Title;
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args) =>
        // Unsaved edits are kept in the scratch space and restored on the next launch, so there's nothing to ask.
        Page.ViewModel.PersistScratch();
}
