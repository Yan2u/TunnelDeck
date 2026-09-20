using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TunnelDeck.Core.Presentation;

public abstract class ObservableModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) { return false; }
        field = value;
        OnPropertyChanged(name);
        return true;
    }
    protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public interface IViewDispatcher
{
    bool HasThreadAccess { get; }
    bool TryEnqueue(Action action);
}
