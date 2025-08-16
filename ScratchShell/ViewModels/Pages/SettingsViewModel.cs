using ScratchShell.Constants;
using ScratchShell.Enums;
using ScratchShell.Services;
using ScratchShell.Properties;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Appearance;

namespace ScratchShell.ViewModels.Pages
{
    public partial class SettingsViewModel : ObservableObject, INavigationAware
    {
        private bool _isInitialized = false;

        [ObservableProperty]
        private string _appVersion = String.Empty;

        [ObservableProperty]
        private string _credit = String.Empty;

        [ObservableProperty]
        private IEnumerable<ShellType> _shellTypes;

        private ShellType _shellType;

        public ShellType ShellType
        {
            get => _shellType;
            set
            {
                if (SetProperty(ref _shellType, value))
                {
                    Settings.Default.DefaultShellType = value.ToString();
                    Settings.Default.Save();
                }
            }
        }

        [ObservableProperty]
        private ApplicationTheme _currentTheme = ApplicationTheme.Unknown;
        public SettingsViewModel()
        {
            ShellTypes = Enum.GetValues(typeof(ShellType)).Cast<ShellType>();
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
            CurrentTheme = ApplicationThemeManager.GetAppTheme();
            Credit = ApplicationConstant.Credit;
            AppVersion = $"{ApplicationConstant.Name} - {GetAssemblyVersion()}";
            ShellType = CommonService.GetEnumValue<ShellType>(Settings.Default.DefaultShellType);

            _isInitialized = true;
        }
        private string GetAssemblyVersion()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? String.Empty;
        }

        [RelayCommand]
        private void OnChangeTheme(string parameter)
        {
            switch (parameter)
            {
                case "theme_light":
                    if (CurrentTheme == ApplicationTheme.Light)
                        break;

                    ApplicationThemeManager.Apply(ApplicationTheme.Light);
                    CurrentTheme = ApplicationTheme.Light;

                    break;

                default:
                    if (CurrentTheme == ApplicationTheme.Dark)
                        break;

                    ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                    CurrentTheme = ApplicationTheme.Dark;

                    break;
            }
            Settings.Default.CurrentTheme = CurrentTheme.ToString();
            Settings.Default.Save();
        }
    }
}
