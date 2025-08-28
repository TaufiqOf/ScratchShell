using System.Globalization;
using ScratchShell.Resources;
using ScratchShell.Properties;

namespace ScratchShell.Services;

/// <summary>
/// Manages application localization and language switching
/// </summary>
public static class LocalizationManager
{
    public static event EventHandler? LanguageChanged;

    /// <summary>
    /// Gets all supported languages in the application
    /// </summary>
    public static readonly Dictionary<string, LanguageInfo> SupportedLanguages = new()
    {
        { "en", new LanguageInfo("en", "English", "English") },
        { "bn", new LanguageInfo("bn", "বাংলা", "Bangla") },
        { "es", new LanguageInfo("es", "Español", "Spanish") }
    };

    /// <summary>
    /// Gets the current language code
    /// </summary>
    public static string CurrentLanguage => Settings.Default.CurrentLanguage ?? "en";

    /// <summary>
    /// Gets the current language information
    /// </summary>
    public static LanguageInfo CurrentLanguageInfo => 
        SupportedLanguages.TryGetValue(CurrentLanguage, out var info) ? info : SupportedLanguages["en"];

    /// <summary>
    /// Changes the application language
    /// </summary>
    /// <param name="languageCode">The language code (e.g., "en", "bn")</param>
    public static void ChangeLanguage(string languageCode)
    {
        if (!SupportedLanguages.ContainsKey(languageCode))
            throw new ArgumentException($"Unsupported language: {languageCode}");

        if (CurrentLanguage == languageCode)
            return;

        // Save the language preference
        Settings.Default.CurrentLanguage = languageCode;
        Settings.Default.Save();

        // Set the culture for the current thread and resource manager
        SetCulture(languageCode);

        // Notify subscribers that language has changed
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Initializes the localization system on application startup
    /// </summary>
    public static void Initialize()
    {
        var savedLanguage = Settings.Default.CurrentLanguage;
        
        // If no language is saved, detect system language or default to English
        if (string.IsNullOrEmpty(savedLanguage))
        {
            savedLanguage = DetectSystemLanguage();
            Settings.Default.CurrentLanguage = savedLanguage;
            Settings.Default.Save();
        }

        SetCulture(savedLanguage);
    }

    /// <summary>
    /// Sets the culture for the current thread and UI
    /// </summary>
    private static void SetCulture(string languageCode)
    {
        try
        {
            var culture = new CultureInfo(languageCode);
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            // Set culture for the resource manager to ensure proper resource lookup
            Langauge.Culture = culture;
        }
        catch (CultureNotFoundException)
        {
            // Fallback to English if culture is not found
            var fallbackCulture = new CultureInfo("en");
            Thread.CurrentThread.CurrentCulture = fallbackCulture;
            Thread.CurrentThread.CurrentUICulture = fallbackCulture;
            CultureInfo.DefaultThreadCurrentCulture = fallbackCulture;
            CultureInfo.DefaultThreadCurrentUICulture = fallbackCulture;
            
            // Set fallback culture for resource manager
            Langauge.Culture = fallbackCulture;
        }
    }

    /// <summary>
    /// Detects the system language and returns a supported language code
    /// </summary>
    private static string DetectSystemLanguage()
    {
        var systemLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        
        // Check if system language is supported
        if (SupportedLanguages.ContainsKey(systemLanguage))
            return systemLanguage;

        // Check for Bangla variants
        if (systemLanguage == "bn" || CultureInfo.CurrentUICulture.Name.StartsWith("bn"))
            return "bn";

        // Default to English
        return "en";
    }

    /// <summary>
    /// Gets a localized string by key
    /// </summary>
    /// <param name="key">The resource key</param>
    /// <returns>The localized string or the key if not found</returns>
    public static string GetString(string key)
    {
        try
        {
            return Langauge.ResourceManager.GetString(key, Langauge.Culture) ?? key;
        }
        catch
        {
            return key;
        }
    }

    /// <summary>
    /// Gets a formatted localized string
    /// </summary>
    /// <param name="key">The resource key</param>
    /// <param name="args">Format arguments</param>
    /// <returns>The formatted localized string</returns>
    public static string GetString(string key, params object[] args)
    {
        try
        {
            var format = Langauge.ResourceManager.GetString(key, Langauge.Culture) ?? key;
            return string.Format(format, args);
        }
        catch
        {
            return key;
        }
    }
}

/// <summary>
/// Information about a supported language
/// </summary>
public record LanguageInfo(string Code, string NativeName, string EnglishName)
{
    /// <summary>
    /// Gets the display name for the language (native name first, English in parentheses if different)
    /// </summary>
    public string DisplayName => NativeName == EnglishName ? NativeName : $"{NativeName} ({EnglishName})";
}