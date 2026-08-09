namespace Scanio.Presentation;

public static class WindowShutdownGuard
{
    public static async Task WaitAsync(Task driverShutdown, Task graceDeadline)
    {
        ArgumentNullException.ThrowIfNull(driverShutdown);
        ArgumentNullException.ThrowIfNull(graceDeadline);

        var completed = await Task.WhenAny(driverShutdown, graceDeadline).ConfigureAwait(false);
        if (ReferenceEquals(completed, driverShutdown))
        {
            await ObserveAsync(driverShutdown).ConfigureAwait(false);
            return;
        }

        _ = ObserveAsync(driverShutdown);
    }

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Closing the main window must not be held hostage by a serial
            // driver cleanup failure. Observing the task prevents an
            // abandoned cleanup exception from becoming unobserved.
        }
    }
}
