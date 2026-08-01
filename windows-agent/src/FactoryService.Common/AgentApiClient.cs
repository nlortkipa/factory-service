using System.Net.Http.Json;
using System.Text.Json;

namespace FactoryService.Common;

public sealed record AgentQrResponse(string Token, string SurveyUrl, DateTimeOffset ExpiresAt, string OperatorName, string PlantName, string Terminal);
public sealed record AgentApiError(string Code, string Message, string? HelpdeskReference);

public sealed class AgentApiClient : IDisposable
{
    private readonly AgentConfig _config;
    private readonly HttpClient _httpClient;

    public AgentApiClient(AgentConfig config)
    {
        _config = config;
        _httpClient = new HttpClient { BaseAddress = new Uri(config.ApiBaseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(15) };
        _httpClient.DefaultRequestHeaders.Add("X-Agent-Key", config.AgentKey);
    }

    public async Task<AgentQrResponse> RequestQrAsync(CancellationToken cancellationToken = default)
    {
        var request = new
        {
            _config.DeviceId, ComputerName = string.IsNullOrWhiteSpace(_config.ComputerName) ? Environment.MachineName : _config.ComputerName,
            WindowsUsername = _config.EffectiveWindowsUsername,
            ClientVersion = typeof(AgentApiClient).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            IsTest = _config.TestMode
        };
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/agent/qr", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            AgentApiError? error = await response.Content.ReadFromJsonAsync<AgentApiError>(cancellationToken: cancellationToken);
            throw new AgentApiException(error?.Message ?? $"API error {(int)response.StatusCode}", error?.Code, error?.HelpdeskReference);
        }
        return await response.Content.ReadFromJsonAsync<AgentQrResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("QR API returned an empty response.");
    }

    public async Task SendHeartbeatAsync(string status, string? errorCode, DateTimeOffset? lastRenderAt, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            _config.DeviceId, ComputerName = string.IsNullOrWhiteSpace(_config.ComputerName) ? Environment.MachineName : _config.ComputerName,
            WindowsUsername = _config.EffectiveWindowsUsername,
            ClientVersion = typeof(AgentApiClient).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Status = status, ErrorCode = errorCode, LastRenderAt = lastRenderAt
        };
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/agent/heartbeat", request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose() => _httpClient.Dispose();
}

public sealed class AgentApiException(string message, string? code, string? helpdeskReference) : Exception(message)
{
    public string? Code { get; } = code;
    public string? HelpdeskReference { get; } = helpdeskReference;
}
