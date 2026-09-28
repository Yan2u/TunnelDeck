using System.ComponentModel;

namespace TunnelDeck.Localization;

/// <summary>A replaceable resource map refreshes existing XAML, including templates and flyouts.</summary>
[Microsoft.UI.Xaml.Data.Bindable]
public sealed class LocalizedStrings : INotifyPropertyChanged, ILocalizable
{
    public LocalizedStrings() => UiText.Observe(this);
    public IReadOnlyDictionary<string, string> Values { get; private set; } = UiText.Snapshot();
    public string Language => UiText.Language;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void RefreshLanguage()
    {
        // WinUI does not consistently refresh root indexer bindings for an empty property name.
        // Replacing a named property also updates content cached by NavigationViewItem presenters.
        Values = UiText.Snapshot();
        PropertyChanged?.Invoke(this, new(nameof(Values)));
        PropertyChanged?.Invoke(this, new(nameof(Language)));
    }
}
