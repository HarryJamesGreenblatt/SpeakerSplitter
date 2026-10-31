using System.ComponentModel;
using System.Windows;
using SpeakerSplitter.Wpf.Playback;

namespace SpeakerSplitter.Wpf;

/// <summary>The device-selection window, driven on its dispatcher thread.</summary>
public partial class MainWindow : Window
{
    #region Fields

    /// <summary>The controller owned by this window and released on close.</summary>
    private readonly ControllerViewModel _controller;

    #endregion

    #region Constructor(s)

    /// <summary>Initializes the window and lists the active render endpoints.</summary>
    public MainWindow() : this(new ControllerViewModel())
    {
    }

    /// <summary>Creates the window around an owned controller.</summary>
    /// <param name="controller">The controller to release when the window closes.</param>
    public MainWindow(ControllerViewModel controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        InitializeComponent();
        _controller = controller;
        DataContext = controller;
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
        {
            return;
        }

        try
        {
            _controller.Dispose();
        }
        catch (AggregateException exception)
        {
            Environment.ExitCode = 1;
            MessageBox.Show(this, exception.Message, "Audio Cleanup Failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion
}
