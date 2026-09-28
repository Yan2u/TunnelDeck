using System.Collections;
using System.Globalization;
using System.Resources;

namespace TunnelDeck.Core.Localization;

/// <summary>Shared resources for native XAML and presentation models, independent of Windows APIs.</summary>
public static partial class UiText
{
    private static readonly ResourceManager Resources = new("TunnelDeck.Core.Localization.Strings", typeof(UiText).Assembly);
    private static readonly List<WeakReference<ILocalizable>> Observers = [];
    private static readonly object Sync = new();
    private static volatile CultureInfo _culture = CultureInfo.GetCultureInfo("en-US");

    public const string English = "en-US";
    public const string SimplifiedChinese = "zh-CN";
    public static CultureInfo Culture => _culture;
    public static string Language => _culture.Name;
    public static bool IsSupported(string? language) => language is English or SimplifiedChinese;

    public static string Get(string key, params object?[] arguments) => Format(key, Culture, arguments);
    public static string Invariant(string key, params object?[] arguments) => Format(key, CultureInfo.GetCultureInfo(English), arguments);

    public static IReadOnlyDictionary<string, string> Snapshot() =>
        Resources.GetResourceSet(CultureInfo.GetCultureInfo(English), true, true)!.Cast<DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => Get((string)entry.Key), StringComparer.Ordinal);

    public static string Format(string key, CultureInfo culture, params object?[] arguments)
    {
        string template = Resources.GetString(key, culture) ?? throw new MissingManifestResourceException($"Missing UI resource: {key}");
        return arguments.Length == 0 ? template : string.Format(culture, template, arguments);
    }

    /// <summary>Called on the view dispatcher. Culture selection never depends on a worker thread's culture.</summary>
    public static void SetLanguage(string language)
    {
        if (!IsSupported(language)) { throw new ArgumentOutOfRangeException(nameof(language)); }
        if (language == Language) { return; }
        _culture = CultureInfo.GetCultureInfo(language);
        List<ILocalizable> live = [];
        lock (Sync)
        {
            Observers.RemoveAll(reference => !reference.TryGetTarget(out _));
            foreach (WeakReference<ILocalizable> reference in Observers)
            {
                if (reference.TryGetTarget(out ILocalizable? observer)) { live.Add(observer); }
            }
        }
        foreach (ILocalizable observer in live) { observer.RefreshLanguage(); }
    }

    public static void Observe(ILocalizable observer)
    {
        lock (Sync)
        {
            // Weak subscriptions allow navigated-away pages and rows to be collected normally.
            if (Observers.Count % 64 == 0) { Observers.RemoveAll(reference => !reference.TryGetTarget(out _)); }
            Observers.Add(new(observer));
        }
    }
}

public interface ILocalizable
{
    void RefreshLanguage();
}
