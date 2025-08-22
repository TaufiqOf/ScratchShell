using ScratchShell.Properties;
using ScratchShell.ViewModels.Windows;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace ScratchShell.Views.Windows;

public partial class MainWindow : INavigationWindow
{
    public MainWindowViewModel ViewModel { get; }

    public MainWindow(
        MainWindowViewModel viewModel,
        INavigationViewPageProvider navigationViewPageProvider,
        INavigationService navigationService,
        IContentDialogService contentDialogService
    )
    {
        ViewModel = viewModel;
        DataContext = this;

        SystemThemeWatcher.Watch(this);

        this.Loaded += MainWindowLoaded;
        this.Closing += MainWindow_Closing;

        InitializeComponent();
        SetPageService(navigationViewPageProvider);

        navigationService.SetNavigationControl(RootNavigation);
        contentDialogService.SetDialogHost(RootContentDialog);
    }

    private void MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        // Restore window size and position from settings
        if (Settings.Default.MainWindowWidth > 0)
            this.Width = Settings.Default.MainWindowWidth;
        if (Settings.Default.MainWindowHeight > 0)
            this.Height = Settings.Default.MainWindowHeight;
        if (Settings.Default.MainWindowTop >= 0)
            this.Top = Settings.Default.MainWindowTop;
        if (Settings.Default.MainWindowLeft >= 0)
            this.Left = Settings.Default.MainWindowLeft;
        if (Settings.Default.MainWindowMaximized)
            this.WindowState = WindowState.Maximized;

        ViewModel.Loaded();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Save window size and position to settings
        Settings.Default.MainWindowMaximized = this.WindowState == WindowState.Maximized;
        if (this.WindowState == WindowState.Normal)
        {
            Settings.Default.MainWindowWidth = this.Width;
            Settings.Default.MainWindowHeight = this.Height;
            Settings.Default.MainWindowTop = this.Top;
            Settings.Default.MainWindowLeft = this.Left;
        }
        else
        {
            // Save RestoreBounds if maximized
            Settings.Default.MainWindowWidth = this.RestoreBounds.Width;
            Settings.Default.MainWindowHeight = this.RestoreBounds.Height;
            Settings.Default.MainWindowTop = this.RestoreBounds.Top;
            Settings.Default.MainWindowLeft = this.RestoreBounds.Left;
        }
        Settings.Default.Save();
    }

    #region INavigationWindow methods

    public INavigationView GetNavigation() => RootNavigation;

    public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);

    public void SetPageService(INavigationViewPageProvider navigationViewPageProvider) => RootNavigation.SetPageProviderService(navigationViewPageProvider);

    public void ShowWindow() => Show();

    public void CloseWindow() => Close();

    #endregion INavigationWindow methods

    /// <summary>
    /// Raises the closed event.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        // Make sure that closing this window will begin the process of closing the application.
        Application.Current.Shutdown();
    }

    INavigationView INavigationWindow.GetNavigation()
    {
        throw new NotImplementedException();
    }

    public void SetServiceProvider(IServiceProvider serviceProvider)
    {
        throw new NotImplementedException();
    }
}