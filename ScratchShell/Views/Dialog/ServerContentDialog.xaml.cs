using ScratchShell.ViewModels.Models;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace ScratchShell.View.Dialog
{
    /// <summary>
    /// Interaction logic for ServerContentDialog.xaml
    /// </summary>
    public partial class ServerContentDialog : ContentDialog
    {
        public ServerViewModel ViewModel { get; }

        public ServerContentDialog(ContentPresenter? contentPresenter, ServerViewModel viewModel)
            : base(contentPresenter)
        {
            InitializeComponent();
            this.ViewModel = viewModel;
            PasswordInput.Password = viewModel.Password;
            KeyFilePasswordInput.Password = viewModel.KeyFilePassword;
            DataContext = new ServerViewModel(viewModel, viewModel.ContentDialogService);
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is ServerViewModel viewModel)
            {
                viewModel.Password = PasswordInput.Password;
            }
        }

        private void KeyFilePasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is ServerViewModel viewModel)
            {
                viewModel.KeyFilePassword = KeyFilePasswordInput.Password;
            }
        }

        protected override void OnButtonClick(ContentDialogButton button)
        {
            if(button == ContentDialogButton.Primary)
            {
                if (DataContext is ServerViewModel viewModel)
                {
                    ViewModel.Name = viewModel.Name;
                    ViewModel.Host = viewModel.Host;
                    ViewModel.Port = viewModel.Port;
                    ViewModel.Username = viewModel.Username;
                    ViewModel.UseKeyFile = viewModel.UseKeyFile;
                    ViewModel.Password =viewModel.Password;
                    ViewModel.KeyFilePassword = viewModel.KeyFilePassword;
                    ViewModel.ProtocolType = viewModel.ProtocolType;
                }
            }
            base.OnButtonClick(button);
        }
    }
}
