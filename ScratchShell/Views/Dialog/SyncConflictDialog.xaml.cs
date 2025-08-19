using ScratchShell.Enums;
using ScratchShell.Models;
using System.ComponentModel;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace ScratchShell.Views.Dialog
{
    public partial class SyncConflictDialog : ContentDialog, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private UserSettingsData? _localSettings;
        private UserSettingsData? _serverSettings;
        private DateTime? _clientLastSynced;
        private DateTime? _serverLastSynced;

        public UserSettingsData? LocalSettings
        {
            get => _localSettings;
            set
            {
                if (_localSettings != value)
                {
                    _localSettings = value;
                    OnPropertyChanged(nameof(LocalSettings));
                }
            }
        }

        public UserSettingsData? ServerSettings
        {
            get => _serverSettings;
            set
            {
                if (_serverSettings != value)
                {
                    _serverSettings = value;
                    OnPropertyChanged(nameof(ServerSettings));
                }
            }
        }

        public DateTime? ClientLastSynced
        {
            get => _clientLastSynced;
            set
            {
                if (_clientLastSynced != value)
                {
                    _clientLastSynced = value;
                    OnPropertyChanged(nameof(ClientLastSynced));
                }
            }
        }

        public DateTime? ServerLastSynced
        {
            get => _serverLastSynced;
            set
            {
                if (_serverLastSynced != value)
                {
                    _serverLastSynced = value;
                    OnPropertyChanged(nameof(ServerLastSynced));
                }
            }
        }

        public ConflictResolution SelectedResolution { get; private set; } = ConflictResolution.UseLocal;

        public SyncConflictDialog(IContentDialogService contentDialogService) : base(contentDialogService.GetDialogHost())
        {
            InitializeComponent();
            DataContext = this;
        }

        protected override void OnButtonClick(ContentDialogButton button)
        {
            switch (button)
            {
                case ContentDialogButton.Primary:
                    SelectedResolution = ConflictResolution.UseLocal;
                    break;

                case ContentDialogButton.Secondary:
                    SelectedResolution = ConflictResolution.UseServer;
                    break;

                default:
                    SelectedResolution = ConflictResolution.UseLocal; // Default fallback
                    break;
            }

            base.OnButtonClick(button);
        }

        public void SetConflictData(UserSettingsData localSettings, UserSettingsData serverSettings,
                                   DateTime? clientLastSynced, DateTime? serverLastSynced)
        {
            LocalSettings = localSettings;
            ServerSettings = serverSettings;
            ClientLastSynced = clientLastSynced;
            ServerLastSynced = serverLastSynced;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}