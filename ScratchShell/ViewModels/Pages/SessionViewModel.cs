using ScratchShell.Services;
using ScratchShell.ViewModels.Models;
using System.Collections.ObjectModel;
using Wpf.Ui.Abstractions.Controls;

namespace ScratchShell.ViewModels.Pages;

public partial class SessionViewModel : ObservableObject, INavigationAware
{
    private bool _isInitialized = false;

    [ObservableProperty]
    private ObservableCollection<TabItemViewModel> _tabs;

    [ObservableProperty]
    private TabItemViewModel _selectedTab;

    public SessionViewModel()
    {
        Tabs = SessionService.Sessions;
        SessionService.SessionSelected += SessionServiceSessionSelected;
    }

    private Task SessionServiceSessionSelected(TabItemViewModel tabViewModel)
    {
        SelectedTab = Tabs.FirstOrDefault(t => t.Id == tabViewModel.Id) ?? Tabs.First();
        return Task.CompletedTask;
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
        _isInitialized = true;
    }
}