namespace FactoryService.Service;

internal sealed class CompanionSupervisor(
    Func<IReadOnlyList<int>> getSessions, Action<int> ensureStarted, Action<Exception> reportError) : IDisposable
{
    private readonly object _sync = new();
    private bool _stopped;

    public void Refresh()
    {
        // Session events and the retry timer may arrive together. Never launch in parallel.
        lock (_sync)
        {
            if (_stopped) return;
            try
            {
                foreach (int session in getSessions().Distinct())
                {
                    try { ensureStarted(session); }
                    catch (Exception exception) { reportError(exception); }
                }
            }
            catch (Exception exception) { reportError(exception); }
        }
    }

    public void Dispose()
    {
        // Stop waits for any launch in flight, so uninstall/update cannot race a restart.
        lock (_sync) { _stopped = true; }
    }
}
