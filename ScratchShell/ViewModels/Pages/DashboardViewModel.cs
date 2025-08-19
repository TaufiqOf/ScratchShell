using Ionic.Zip;
using ScratchShell.Constants;
using ScratchShell.Enums;
using ScratchShell.Models;
using ScratchShell.Properties;
using ScratchShell.Services;
using ScratchShell.View.Dialog;
using ScratchShell.ViewModels.Models;
using ScratchShell.Views.Dialog;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Forms;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace ScratchShell.ViewModels.Pages
{
    public partial class DashboardViewModel : ObservableObject, INavigationAware
    {
        private bool _isInitialized = false;

        [ObservableProperty]
        private ObservableCollection<ServerViewModel> _servers = new();

        [ObservableProperty]
        private ICollectionView _filteredServers;

        private string _searchText;

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    SearchCommand.Execute(value); // Trigger command or logic here
                }
            }
        }

        private IContentDialogService _contentDialogService;
        private readonly INavigationService _navigationService;

        public DashboardViewModel(IContentDialogService contentDialogService, INavigationService navigationService)
        {
            _contentDialogService = contentDialogService;
            _navigationService = navigationService;
            FilteredServers = CollectionViewSource.GetDefaultView(Servers);
            FilteredServers.Filter = o => o is ServerViewModel svm && !svm.IsDeleted;
            ServerManager.OnServerSelected += ServerManagerOnServerSelected;
            ServerManager.OnServerAdded += ServerManagerOnServerAdded;
            ServerManager.OnServerRemoved += ServerManagerOnServerRemoved;
            ServerManager.OnServerEdited += ServerManagerOnServerEdited;
            ServerManager.OnServerInitialized += ServerManagerOnServerInitialized;
        }

        private async Task ServerManagerOnServerInitialized()
        {
            Servers.Clear();
            ServerManager.Servers.Where(q => !q.IsDeleted).Select(q => new ServerViewModel(q, _contentDialogService)).ToList().ForEach(q =>
            {
                Servers.Add(q);
            });
            FilteredServers.Refresh();
            await Task.CompletedTask;
        }

        private async Task ServerManagerOnServerEdited(Server? server)
        {
            await Task.CompletedTask;
        }

        private async Task ServerManagerOnServerRemoved(Server? server)
        {
            FilteredServers.Refresh();
            await Task.CompletedTask;
        }

        private async Task ServerManagerOnServerAdded(Server? server)
        {
            Servers.Add(new ServerViewModel(server, _contentDialogService));
            FilteredServers.Refresh();
            await Task.CompletedTask;
        }

        private async Task ServerManagerOnServerSelected(Server? server)
        {
            SessionService.AddSession(new ServerViewModel(server, _contentDialogService));
            _navigationService.Navigate(typeof(Views.Pages.SessionPage));
            await Task.CompletedTask;
        }

        public Task OnNavigatedToAsync()
        {
            if (!_isInitialized)
                InitializeViewModel();

            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        private void InitializeViewModel()
        {
            Servers.Clear();
            ServerManager.Servers.Where(q => !q.IsDeleted).Select(q => new ServerViewModel(q, _contentDialogService)).ToList().ForEach(q =>
            {
                Servers.Add(q);
            });
            FilteredServers.Refresh();
            _isInitialized = true;
        }

        [RelayCommand]
        private async Task OnSearch()
        {
            FilteredServers.Filter = o => o is ServerViewModel svm && !svm.IsDeleted && (svm.Name.Contains(SearchText, StringComparison.InvariantCultureIgnoreCase) || svm.Host.Contains(SearchText, StringComparison.InvariantCultureIgnoreCase) || svm.Port.ToString().Contains(SearchText, StringComparison.InvariantCultureIgnoreCase));
            FilteredServers.Refresh();
            await Task.CompletedTask;
        }

        [RelayCommand]
        private async Task OnAddServer()
        {
            if (_contentDialogService is not null)
            {
                var serverViewModel = new ServerViewModel(_contentDialogService);
                if (_contentDialogService is not null)
                {
                    var serverContentDialog = new ServerContentDialog(_contentDialogService.GetDialogHost(), serverViewModel);

                    var contentDialogResult = await serverContentDialog.ShowAsync();
                    if (contentDialogResult == ContentDialogResult.Primary)
                    {
                        ServerManager.AddServer(serverViewModel.ToServer());
                    }
                }
            }
        }

        [RelayCommand]
        private async Task OnOpenTerminal(string? mode)
        {
            bool runAsAdmin = string.Equals(mode, TerminalConstant.Elevated, StringComparison.OrdinalIgnoreCase);
            var shell = CommonService.GetEnumValue<ShellType>(Settings.Default.DefaultShellType);
            try
            {
                TerminalService.Launch(
                    TerminalConstant.Builder.Open,
                    new { Path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), ShellType = shell },
                    shell,
                    runAsAdmin,
                    ApplicationConstant.Name);
            }
            catch (Win32Exception)
            {
                // This happens if the user cancels the UAC prompt
                await _contentDialogService.ShowSimpleDialogAsync(
                    new SimpleContentDialogCreateOptions
                    {
                        Title = "Cancelled",
                        Content = "SSH terminal was not opened (admin permission was declined).",
                        CloseButtonText = "OK"
                    });
            }
            catch (Exception ex)
            {
                await _contentDialogService.ShowSimpleDialogAsync(
                    new SimpleContentDialogCreateOptions
                    {
                        Title = "Error",
                        Content = ex.Message,
                        CloseButtonText = "OK"
                    });
            }
        }

        [RelayCommand]
        private async Task OnExport()
        {
            try
            {
                var exportServersDialog = new ExportServersDialog(_contentDialogService, Servers);
                var contentDialogResult = await exportServersDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                await _contentDialogService.ShowSimpleDialogAsync(
                    new SimpleContentDialogCreateOptions
                    {
                        Title = "Error",
                        Content = ex.Message,
                        CloseButtonText = "OK"
                    });
            }
        }

        [RelayCommand]
        private async Task OnImport()
        {
            try
            {
                var saveDialog = new OpenFileDialog();
                saveDialog.Filter = "(*.ss)|*.ss|All Files (*.*)|*.*";
                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    var passwordDialog = new PasswordDialog(_contentDialogService);
                    var contentDialogResult = await passwordDialog.ShowAsync();
                    if (contentDialogResult != ContentDialogResult.Primary)
                    {
                        return;
                    }
                    var exported = await ServerExportImportService.ImportServers(saveDialog.FileName, passwordDialog.PasswordBox.Password);
                    exported.ForEach(ServerManager.AddServer);
                }
            }
            catch (BadPasswordException)
            {
                await _contentDialogService.ShowSimpleDialogAsync(
                    new SimpleContentDialogCreateOptions
                    {
                        Title = "Import",
                        Content = "Invalid Password",
                        CloseButtonText = "OK"
                    });
            }
            catch (Exception ex)
            {
                await _contentDialogService.ShowSimpleDialogAsync(
                    new SimpleContentDialogCreateOptions
                    {
                        Title = "Error",
                        Content = ex.Message,
                        CloseButtonText = "OK"
                    });
            }
        }
    }
}