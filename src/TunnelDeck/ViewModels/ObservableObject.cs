using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TunnelDeck.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged, ILocalizable
{
    protected ObservableObject() => UiText.Observe(this);
    public virtual void RefreshLanguage() => OnPropertyChanged(string.Empty);
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value)) { return false; }
        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
