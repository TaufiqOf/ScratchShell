using ScratchShell.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace ScratchShell.Views.Pages
{
    public partial class SessionPage : INavigableView<SessionViewModel>
    {
        public SessionViewModel ViewModel { get; }

        public SessionPage(SessionViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}