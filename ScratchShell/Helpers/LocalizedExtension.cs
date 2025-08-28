using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using ScratchShell.Services;

namespace ScratchShell.Helpers;

/// <summary>
/// Markup extension for accessing localized resources in XAML with dynamic language change support
/// </summary>
public class LocalizedExtension : MarkupExtension, INotifyPropertyChanged
{
    private static readonly Dictionary<string, List<LocalizedExtension>> _instances = new();
    private static bool _eventSubscribed = false;

    public string Key { get; set; }
    private object? _targetObject;
    private object? _targetProperty;

    public event PropertyChangedEventHandler? PropertyChanged;

    static LocalizedExtension()
    {
        // Subscribe to language change events once
        if (!_eventSubscribed)
        {
            LocalizationManager.LanguageChanged += OnGlobalLanguageChanged;
            _eventSubscribed = true;
        }
    }

    public LocalizedExtension(string key)
    {
        Key = key;
    }

    public LocalizedExtension()
    {
        Key = string.Empty;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
            return string.Empty;

        // Store reference to target for dynamic updates
        if (serviceProvider?.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget target)
        {
            _targetObject = target.TargetObject;
            _targetProperty = target.TargetProperty;

            // Handle case where target is during template instantiation
            if (_targetObject?.GetType().Name == "DeferredResourceReference")
                return this;
        }

        // Register this instance for dynamic updates
        RegisterInstance();

        // For Binding scenarios, return a Binding that updates dynamically
        if (_targetProperty is DependencyProperty)
        {
            var binding = new Binding(nameof(LocalizedValue))
            {
                Source = this,
                Mode = BindingMode.OneWay
            };
            return binding.ProvideValue(serviceProvider);
        }

        // For direct property assignments, return the localized string
        return LocalizedValue;
    }

    /// <summary>
    /// Gets the localized value for the current key and language
    /// </summary>
    public string LocalizedValue
    {
        get
        {
            try
            {
                return LocalizationManager.GetString(Key);
            }
            catch
            {
                return Key; // Fallback to key if localization fails
            }
        }
    }

    private void RegisterInstance()
    {
        if (string.IsNullOrEmpty(Key))
            return;

        lock (_instances)
        {
            if (!_instances.ContainsKey(Key))
                _instances[Key] = new List<LocalizedExtension>();

            if (!_instances[Key].Contains(this))
                _instances[Key].Add(this);
        }
    }

    private void UnregisterInstance()
    {
        if (string.IsNullOrEmpty(Key))
            return;

        lock (_instances)
        {
            if (_instances.ContainsKey(Key))
            {
                _instances[Key].Remove(this);
                if (_instances[Key].Count == 0)
                    _instances.Remove(Key);
            }
        }
    }

    private static void OnGlobalLanguageChanged(object? sender, EventArgs e)
    {
        // Update all registered instances when language changes
        List<LocalizedExtension> instancesToUpdate = new();

        lock (_instances)
        {
            foreach (var kvp in _instances)
            {
                instancesToUpdate.AddRange(kvp.Value);
            }
        }

        // Update instances on UI thread
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            foreach (var instance in instancesToUpdate)
            {
                instance.OnPropertyChanged(nameof(LocalizedValue));
            }
        });
    }

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Cleanup method to remove instance from tracking
    /// </summary>
    ~LocalizedExtension()
    {
        UnregisterInstance();
    }
}