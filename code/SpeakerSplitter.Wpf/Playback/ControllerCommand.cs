using System.Windows.Input;

namespace SpeakerSplitter.Wpf.Playback;

/// <summary>A controller action whose availability is explicitly updated on the UI thread.</summary>
internal sealed class ControllerCommand : ICommand
{
    #region Fields

    /// <summary>The action invoked when the command is enabled.</summary>
    private readonly Action _execute;

    /// <summary>Determines whether the controller can accept this action now.</summary>
    private readonly Func<bool> _canExecute;

    #endregion

    #region Events

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    #endregion

    #region Constructor(s)

    /// <summary>Creates an action with a live availability check.</summary>
    /// <param name="execute">The controller action.</param>
    /// <param name="canExecute">The availability check.</param>
    public ControllerCommand(Action execute, Func<bool> canExecute)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => _canExecute();

    /// <inheritdoc />
    public void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            throw new InvalidOperationException("The controller action is not available in the current state.");
        }

        _execute();
    }

    /// <summary>Notifies bound controls that availability may have changed.</summary>
    public void NotifyAvailability() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    #endregion
}
