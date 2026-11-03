namespace IsConnected;

static class Program
{
    private const string SingleInstanceMutexName = @"Local\IsConnected.Tray";
    private const string ExitEventName = @"Local\IsConnected.Exit";
    private const string ExitArgument = "--exit";
    private const int ExitWaitMs = 10_000;

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains(ExitArgument, StringComparer.OrdinalIgnoreCase))
        {
            RequestRunningInstanceExit();
            return;
        }

        using var singleInstance = new Mutex(true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();

        using var exitRequested = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        using var context = new TrayApplicationContext();
        var uiThread = new WindowsFormsSynchronizationContext();
        var exitRegistration = ThreadPool.RegisterWaitForSingleObject(
            exitRequested,
            (_, _) => uiThread.Post(_ => context.ExitThread(), null),
            null,
            Timeout.Infinite,
            executeOnlyOnce: true);

        try
        {
            Application.Run(context);
        }
        finally
        {
            exitRegistration.Unregister(null);
        }
    }

    // Lets the installer close the tray app cleanly, so its files can be replaced or removed
    // and no stale icon stays in the notification area.
    private static void RequestRunningInstanceExit()
    {
        if (!EventWaitHandle.TryOpenExisting(ExitEventName, out var exitRequested))
        {
            return;
        }

        using (exitRequested)
        {
            exitRequested.Set();
        }

        using var singleInstance = new Mutex(false, SingleInstanceMutexName);

        try
        {
            if (singleInstance.WaitOne(ExitWaitMs))
            {
                singleInstance.ReleaseMutex();
            }
        }
        catch (AbandonedMutexException)
        {
            // The running instance has exited, which is what this call waits for.
        }
    }
}
