using ScratchShell.Constants;
using ScratchShell.Services;
using ScratchShell.View.Dialog;
using ScratchShell.ViewModels.Models;
using ScratchShell.Views.Dialog;
using System.Collections.ObjectModel;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace ScratchShell.ViewModels.Windows
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly IContentDialogService _contentDialogService;

        public MainWindowViewModel(IContentDialogService contentDialogService)
        {
            this._contentDialogService = contentDialogService;
        }

        private async void ShowLogin()
        {
            if (_contentDialogService is not null)
            {
                var loginDialog = new LoginDialog(_contentDialogService);

                var contentDialogResult = await loginDialog.ShowAsync();
                if(contentDialogResult == ContentDialogResult.Secondary)
                {
                    await Register();
                }
                if (contentDialogResult == ContentDialogResult.None)
                {
                    Application.Current.Shutdown();
                }
            }
        }

        private async Task Register()
        {
            if (_contentDialogService is not null)
            {
                var registerDialog = new RegisterDialog(_contentDialogService);

                var contentDialogResult = await registerDialog.ShowAsync();
                if (contentDialogResult == ContentDialogResult.Primary)
                {
                    // Registration successful, continue with the app
                    if (registerDialog.IsRegistrationSuccessful)
                    {
                        // You can store the token or user info here if needed
                        // For now, just continue to the main app
                        return;
                    }
                }
                else if (contentDialogResult == ContentDialogResult.Secondary)
                {
                    // Back to login button pressed
                    ShowLogin();
                }
                else if (contentDialogResult == ContentDialogResult.None)
                {
                    // Cancel or close - back to login
                    ShowLogin();
                }
            }
        }

        internal void Loaded()
        {
            ShowLogin();
        }

        [ObservableProperty]
        private string _applicationTitle = ApplicationConstant.Name;

        [ObservableProperty]
        private ObservableCollection<object> _menuItems = new()
        {
            new NavigationViewItem()
            {
                Content = "Home",
                Icon = new SymbolIcon { Symbol = SymbolRegular.Home24 },
                TargetPageType = typeof(Views.Pages.DashboardPage),
            },
            new NavigationViewItem()
            {
                Content = "Session",
                Icon = new SymbolIcon { Symbol = SymbolRegular.DataHistogram24 },
                TargetPageType = typeof(Views.Pages.SessionPage)
            }
        };

        [ObservableProperty]
        private ObservableCollection<object> _footerMenuItems = new()
        {
            new NavigationViewItem()
            {
                Content = "Settings",
                Icon = new SymbolIcon { Symbol = SymbolRegular.Settings24 },
                TargetPageType = typeof(Views.Pages.SettingsPage)
            }
        };

        [ObservableProperty]
        private ObservableCollection<MenuItem> _trayMenuItems = new()
        {
            new MenuItem { Header = "Home", Tag = "tray_home" }
        };
    }
}
