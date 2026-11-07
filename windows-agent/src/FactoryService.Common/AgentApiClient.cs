using System.Net.Http.Json;
using System.Text.Json;

namespace FactoryService.Common;

public sealed record AgentQrResponse(string Token, string SurveyUrl, DateTimeOffset ExpiresAt, string OperatorName, string PlantName);
public sealed record AgentApiError(string Code, string Message, string? HelpdeskReference);

public sealed class AgentApiClient : IDisposable
{
    private readonly AgentConfig _config;
    private readonly HttpClient _httpClient;

    public AgentApiClient(AgentConfig config)
    {
        _config = config;
        _httpClient = new HttpClient { BaseAddress = new Uri(config.ApiBaseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<AgentQrResponse> RequestQrAsync(CancellationToken cancellationToken = default, string? currentToken = null)
    {
        var request = new { _config.OperatorId, CurrentToken = currentToken };
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/agent/qr", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            AgentApiError? error = null;
            try { error = await response.Content.ReadFromJsonAsync<AgentApiError>(cancellationToken: cancellationToken); }
            catch (JsonException) { /* A proxy may return HTML or an empty error body. */ }
            throw new AgentApiException(error?.Message ?? $"სერვერმა დააბრუნა შეცდომა ({(int)response.StatusCode}).", error?.Code, error?.HelpdeskReference);
        }
        return await response.Content.ReadFromJsonAsync<AgentQrResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("QR API returned an empty response.");
    }

    public void Dispose() => _httpClient.Dispose();
}

public sealed class AgentApiException(string message, string? code, string? helpdeskReference) : Exception(message)
{
    public string? Code { get; } = code;
    public string? HelpdeskReference { get; } = helpdeskReference;
}
