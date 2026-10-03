using System.Windows.Input;

namespace Mark.Designer.ViewModels;

/// <summary>
/// A command that runs an asynchronous action (e.g. a call to the licence server). It cannot run again while running,
/// and reports that through <see cref="CanExecute"/> and <see cref="IsRunning"/>.
/// </summary>
public sealed class AsyncCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;

    public AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool IsRunning { get; private set; }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !IsRunning && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter) => await ExecuteAsync();

    /// <summary>Runs the action (tests await this).</summary>
    public async Task ExecuteAsync()
    {
        if (!CanExecute(null)) return;
        IsRunning = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute();
        }
        finally
        {
            IsRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
