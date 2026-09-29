using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Fenestration.Designer.ViewModels;

/// <summary>
/// Base class for all view models. Provides <see cref="INotifyPropertyChanged"/> support.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Sets the field and raises <see cref="PropertyChanged"/> if the value changed.
    /// Returns true if the value was updated.
    /// </summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
