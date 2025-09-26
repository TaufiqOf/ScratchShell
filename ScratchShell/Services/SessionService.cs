using ScratchShell.ViewModels.Models;
using System.Collections.ObjectModel;

namespace ScratchShell.Services;

internal static class SessionService
{
    internal delegate Task SessionDelegate(TabItemViewModel tabViewModel);

    internal static event SessionDelegate SessionSelected;

    internal static ObservableCollection<TabItemViewModel> Sessions;
    private static TabItemViewModel selectedSession;

    internal static TabItemViewModel SelectedSession
    {
        get
        {
            return selectedSession;
        }
        set
        {
            selectedSession = value;
            SessionSelected?.Invoke(value);
        }
    }

    static SessionService()
    {
        Sessions = new ObservableCollection<TabItemViewModel>();
        // Load existing sessions from storage or initialize as needed
        // For example, you might load from a file or database here.
    }

    internal static void AddSession(ServerViewModel serverViewModel)
    {
        TabItemViewModel item = serverViewModel.ToTabItemViewModel();
        Sessions.Add(item);
        SelectedSession = item;
    }

    internal static void RemoveSession(TabItemViewModel tabViewModel)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var tab = Sessions.FirstOrDefault(q => q.Id == tabViewModel.Id);
            Sessions.Remove(tab);
            tab?.RemovedTab();
            tabViewModel.Dispose();
        });
    }
}