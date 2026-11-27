using FactoryService.Common;

namespace FactoryService.Companion;

internal interface IPanelDisplay
{
    Task ShowQrAsync(AgentQrResponse response, Func<bool> canWrite, CancellationToken cancellationToken);
    Task ShowUnavailableAsync(string detail, Func<bool> canWrite, CancellationToken cancellationToken);
}

internal sealed class QrRefreshController(
    IPanelSession session,
    IPanelDisplay panel,
    Func<AgentConfig> loadConfiguration,
    Func<AgentConfig, string?, CancellationToken, Task<AgentQrResponse>> requestQr,
    Action<Exception> reportError,
    Func<DateTimeOffset>? utcNow = null) : IDisposable
{
    private DateTimeOffset Now => utcNow?.Invoke() ?? DateTimeOffset.UtcNow;
    private bool? _active;
    private bool _running;
    private bool _resetPanel = true;
    private bool _disposed;
    private DateTimeOffset _nextRefresh = DateTimeOffset.MinValue;
    private CancellationTokenSource? _request;
    private AgentQrResponse? _displayed;
    private AgentConfig? _displayedConfig;
    private bool _cleared;

    public async Task TickAsync(bool force = false)
    {
        if (_disposed) return;
        bool active = session.CanPublish;
        if (active != _active || force)
        {
            _active = active;
            _resetPanel = true;
            _nextRefresh = DateTimeOffset.MinValue;
            _request?.Cancel();
        }
        if (_running || Now < _nextRefresh) return;

        _running = true;
        using var request = new CancellationTokenSource();
        _request = request;
        CancellationToken token = request.Token;
        try
        {
            if (_resetPanel)
            {
                _displayed = null;
                _displayedConfig = null;
                await panel.ShowUnavailableAsync("შეფასება დროებით მიუწვდომელია", () => session.CanClear, token);
                token.ThrowIfCancellationRequested();
                _resetPanel = false;
            }
            if (!active)
            {
                _nextRefresh = DateTimeOffset.MaxValue;
                return;
            }

            if (!session.CanPublish) throw new OperationCanceledException();
            AgentConfig config = loadConfiguration();
            if (string.IsNullOrWhiteSpace(config.OperatorId))
            {
                if (_displayed is not null)
                    await panel.ShowUnavailableAsync("შეფასება დროებით მიუწვდომელია", () => session.CanClear, token);
                token.ThrowIfCancellationRequested();
                _displayed = null;
                _displayedConfig = null;
                _nextRefresh = Now.AddSeconds(5);
                return;
            }
            string? currentToken = config == _displayedConfig && _displayed?.ExpiresAt > Now ? _displayed.Token : null;
            AgentQrResponse response = await requestQr(config, currentToken, token);
            token.ThrowIfCancellationRequested();
            // Includes a second ownership/config check inside the serialized panel write.
            bool CanWrite() => session.CanPublish && loadConfiguration() == config;
            if (!CanWrite()) throw new OperationCanceledException();
            if (response != _displayed || config != _displayedConfig)
                await panel.ShowQrAsync(response, CanWrite, token);
            token.ThrowIfCancellationRequested();
            _displayed = response;
            _displayedConfig = config;
            // Keep the same QR until it is consumed or expires; avoid redundant USB frames.
            _nextRefresh = response.ExpiresAt < Now.AddSeconds(5) ? response.ExpiresAt : Now.AddSeconds(5);
        }
        catch (OperationCanceledException)
        {
            _resetPanel = true;
            _nextRefresh = DateTimeOffset.MinValue;
        }
        catch (Exception exception)
        {
            if (token.IsCancellationRequested || _disposed) return;
            reportError(exception);
            if (exception is not TurmoPanelException)
            {
                string detail = exception is AgentApiException apiException
                    ? string.Join("\n", new[] { apiException.Message, apiException.HelpdeskReference }.Where(value => !string.IsNullOrWhiteSpace(value)))
                    : "შეამოწმეთ კონფიგურაცია და სერვერთან კავშირი";
                try { await panel.ShowUnavailableAsync(detail, () => session.CanClear, token); }
                catch (OperationCanceledException) { }
                catch (Exception panelException) { reportError(panelException); }
            }
            _resetPanel = true;
            _nextRefresh = token.IsCancellationRequested ? DateTimeOffset.MinValue : Now.AddSeconds(5);
        }
        finally
        {
            _request = null;
            _running = false;
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (_cleared) return;
        _cleared = true;
        _disposed = true;
        _request?.Cancel();
        await panel.ShowUnavailableAsync("შეფასება დროებით მიუწვდომელია", () => session.CanClear, cancellationToken);
    }

    public void Dispose()
    {
        _disposed = true;
        _request?.Cancel();
    }
}
