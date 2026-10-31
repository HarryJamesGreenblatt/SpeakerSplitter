using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using SpeakerSplitter.Devices;
using SpeakerSplitter.Wpf;
using SpeakerSplitter.Wpf.Playback;

namespace SpeakerSplitter.Tests.Playback;

public class ControllerViewModelTests
{
    private static readonly EndpointInfo Primary = new("primary", "Primary", true, null);
    private static readonly EndpointInfo Secondary = new("secondary", "Secondary", false, null);

    [Fact]
    public void Constructor_ShowsButPreventsSelectingTheDefaultEndpoint()
    {
        var model = new ControllerViewModel(() => [Primary, Secondary]);
        var primary = model.Devices[0];

        Assert.False(primary.CanSelect);
        Assert.Contains("default", primary.DisplayName);
        Assert.Contains("feed back", primary.SelectionHint);
        Assert.Throws<InvalidOperationException>(() => primary.IsSelected = true);
        Assert.False(primary.IsSelected);
        model.Devices[1].IsSelected = true;
        Assert.True(model.Devices[1].IsSelected);
    }

    [Fact]
    public void RefreshDevices_WhenDefaultChanges_DisablesTheNewDefault()
    {
        IReadOnlyList<EndpointInfo> current = [Primary, Secondary];
        var model = new ControllerViewModel(() => current);
        model.Devices[1].IsSelected = true;
        current = [Primary with { IsDefault = false }, Secondary with { IsDefault = true }];

        model.RefreshDevices();

        Assert.True(model.Devices[0].CanSelect);
        Assert.False(model.Devices[1].CanSelect);
        Assert.False(model.Devices[1].IsSelected);
    }

    [Fact]
    public void Constructor_ReportsMissingDefaultAndEnumerationFailure()
    {
        var missing = new ControllerViewModel(() => [Secondary]);
        var failed = new ControllerViewModel(() => throw new COMException("enumeration failed"));

        Assert.Contains("No default output", missing.Status);
        Assert.Contains("enumeration failed", failed.Status);
        Assert.False(missing.ToggleCommand.CanExecute(null));
        Assert.False(failed.ToggleCommand.CanExecute(null));
    }

    [Fact]
    public void ToggleCommand_StartsOnlySelectedTargets_LocksSelection_AndCreatesFreshSessions()
    {
        var sessions = new List<FakeSession>();
        var model = new ControllerViewModel(() => [Primary, Secondary], () =>
        {
            var session = new FakeSession();
            sessions.Add(session);
            return session;
        });
        Assert.False(model.ToggleCommand.CanExecute(null));
        model.Devices[1].IsSelected = true;

        model.ToggleCommand.Execute(null);

        Assert.True(model.IsRunning);
        Assert.Equal([Secondary], sessions[0].Targets);
        Assert.False(model.Devices[1].CanSelect);
        Assert.Throws<InvalidOperationException>(() => model.Devices[1].IsSelected = false);
        Assert.False(model.RefreshCommand.CanExecute(null));
        model.ToggleCommand.Execute(null);
        Assert.False(model.IsRunning);
        Assert.True(model.Devices[1].CanSelect);
        Assert.Equal(1, sessions[0].StopCalls);
        Assert.Equal(1, sessions[0].DisposeCalls);
        model.ToggleCommand.Execute(null);
        Assert.Equal(2, sessions.Count);
        model.Stop();
    }

    [Fact]
    public void ToggleCommand_WhenStartFails_ReleasesSessionAndReportsTheFailure()
    {
        var session = new FakeSession { FailStart = true };
        var model = new ControllerViewModel(() => [Primary, Secondary], () => session);
        model.Devices[1].IsSelected = true;

        model.ToggleCommand.Execute(null);

        Assert.False(model.IsRunning);
        Assert.Equal(1, session.StopCalls);
        Assert.Equal(1, session.DisposeCalls);
        Assert.Contains("device changed", model.Status);
        Assert.True(model.Devices[1].CanSelect);
    }

    [Fact]
    public void ToggleCommand_WhenTargetChanges_ReportsEngineProblemsAndReleasesSession()
    {
        var session = new FakeSession
        {
            StartError = new SplitterStartException(
                StartFailure.TargetsChanged, "Outputs changed.", ["Secondary is no longer active."]),
        };
        var model = new ControllerViewModel(() => [Primary, Secondary], () => session);
        model.Devices[1].IsSelected = true;

        model.ToggleCommand.Execute(null);

        Assert.False(model.IsRunning);
        Assert.Contains("Secondary is no longer active", model.Status);
        Assert.Equal(1, session.DisposeCalls);
    }

    [Fact]
    public void Lost_EnqueuesUiWork_AndStaleNotificationsDoNotStopTheNextSession()
    {
        var queue = new Queue<Action>();
        var oldSession = new FakeSession();
        var newSession = new FakeSession();
        var next = oldSession;
        var model = new ControllerViewModel(() => [Primary, Secondary], () => next, queue.Enqueue);
        model.Devices[1].IsSelected = true;
        model.ToggleCommand.Execute(null);

        oldSession.RaiseLoss("old device gone");

        Assert.True(model.IsRunning);
        Assert.Single(queue);
        model.Stop();
        next = newSession;
        model.ToggleCommand.Execute(null);
        queue.Dequeue()();
        Assert.True(model.IsRunning);
        Assert.Equal(0, newSession.StopCalls);
        newSession.RaiseLoss("new device gone");
        queue.Dequeue()();
        Assert.False(model.IsRunning);
        Assert.Contains("new device gone", model.Status);
        Assert.Equal(1, newSession.DisposeCalls);
    }

    [Fact]
    public void Stop_WhenStopAndDisposeFail_AttemptsBothAndReportsBothErrors()
    {
        var session = new FakeSession { FailStop = true, FailDispose = true };
        var model = new ControllerViewModel(() => [Primary, Secondary], () => session);
        model.Devices[1].IsSelected = true;
        model.ToggleCommand.Execute(null);

        model.Stop();

        Assert.Equal(1, session.StopCalls);
        Assert.Equal(1, session.DisposeCalls);
        Assert.Contains("stop failed", model.Status);
        Assert.Contains("dispose failed", model.Status);
        Assert.False(model.IsRunning);
    }

    [Fact]
    public void Dispose_ReleasesTheActiveSessionOnce_AndIgnoresQueuedLoss()
    {
        var queue = new Queue<Action>();
        var session = new FakeSession();
        var model = new ControllerViewModel(() => [Primary, Secondary], () => session, queue.Enqueue);
        model.Devices[1].IsSelected = true;
        model.ToggleCommand.Execute(null);
        session.RaiseLoss("late loss");

        model.Dispose();
        model.Dispose();
        queue.Dequeue()();

        Assert.Equal(1, session.StopCalls);
        Assert.Equal(1, session.DisposeCalls);
        Assert.False(model.ToggleCommand.CanExecute(null));
        Assert.False(model.RefreshCommand.CanExecute(null));
        Assert.False(model.Devices[1].CanSelect);
        Assert.Equal("Stopped.", model.Status);
    }

    [Fact]
    public void Dispose_WhenCleanupFails_ReportsAndRethrowsBothErrors()
    {
        var session = new FakeSession { FailStop = true, FailDispose = true };
        var model = new ControllerViewModel(() => [Primary, Secondary], () => session);
        model.Devices[1].IsSelected = true;
        model.ToggleCommand.Execute(null);

        var error = Assert.Throws<AggregateException>(model.Dispose);

        Assert.Equal(2, error.InnerExceptions.Count);
        Assert.Contains("stop failed", model.Status);
        Assert.Contains("dispose failed", model.Status);
        Assert.False(model.ToggleCommand.CanExecute(null));
        model.Dispose();
        Assert.Equal(1, session.DisposeCalls);
    }

    [Fact]
    public void WindowClose_ReleasesTheActiveSession_ButCancelledCloseDoesNot()
    {
        RunOnUiThread(() =>
        {
            var session = new FakeSession();
            var model = new ControllerViewModel(() => [Primary, Secondary], () => session);
            model.Devices[1].IsSelected = true;
            model.ToggleCommand.Execute(null);
            var window = new MainWindow(model);
            window.Show();
            System.ComponentModel.CancelEventHandler cancel = (_, args) => args.Cancel = true;
            window.Closing += cancel;

            window.Close();

            Assert.Equal(0, session.DisposeCalls);
            Assert.True(model.IsRunning);
            window.Closing -= cancel;
            window.Close();
            Assert.Equal(1, session.StopCalls);
            Assert.Equal(1, session.DisposeCalls);
        });
    }

    private static void RunOnUiThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF lifecycle check timed out.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class FakeSession : IPlaybackSession
    {
        public bool FailStart { get; init; }
        public Exception? StartError { get; init; }
        public bool FailStop { get; init; }
        public bool FailDispose { get; init; }
        public int StopCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public IReadOnlyList<EndpointInfo> Targets { get; private set; } = [];
        public event Action<string>? Lost;

        public void Start(IReadOnlyList<EndpointInfo> targets)
        {
            Targets = targets;
            if (StartError is not null)
            {
                throw StartError;
            }

            if (FailStart)
            {
                throw new InvalidOperationException("device changed");
            }
        }

        public void Stop()
        {
            StopCalls++;
            if (FailStop)
            {
                throw new InvalidOperationException("stop failed");
            }
        }

        public void Dispose()
        {
            DisposeCalls++;
            if (FailDispose)
            {
                throw new InvalidOperationException("dispose failed");
            }
        }

        public void RaiseLoss(string reason) => Lost?.Invoke(reason);
    }
}
