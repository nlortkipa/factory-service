using System.Text.Json;

namespace FactoryService.Api.Services;

public sealed class SmsGateway(HttpClient httpClient, IConfiguration configuration)
{
    private readonly string _smsKey = Environment.GetEnvironmentVariable("SMS_KEY")
        ?? configuration["Sms:Key"]
        ?? "";
    private readonly string _endpoint = configuration["Sms:Endpoint"] ?? "https://smsoffice.ge/api/v2/send/";
    private readonly string _sender = Environment.GetEnvironmentVariable("SMS_SENDER")
        ?? configuration["Sms:Sender"]
        ?? "Beton.ge";

    public async Task SendCodeAsync(string phoneNumber, string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_smsKey))
            throw new InvalidOperationException("SMS_KEY is not configured.");
        if (string.IsNullOrWhiteSpace(_sender) || _sender.Length > 11)
            throw new InvalidOperationException("SMS_SENDER must be a registered sender with at most 11 characters.");

        string destination = NormalizeDestination(phoneNumber);
        if (destination.Length != 12 || !destination.StartsWith("9955", StringComparison.Ordinal))
            throw new InvalidOperationException("The administrator phone number is not configured.");
        string query = $"key={Encode(_smsKey)}" +
                       $"&destination={Encode(destination)}" +
                       $"&sender={Encode(_sender)}" +
                       $"&content={Encode(code)}" +
                       "&urgent=true";
        using HttpResponseMessage response = await httpClient.GetAsync($"{_endpoint}?{query}", cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            JsonElement root = document.RootElement;
            bool success = root.TryGetProperty("Success", out JsonElement successElement) && successElement.GetBoolean();
            int errorCode = root.TryGetProperty("ErrorCode", out JsonElement codeElement) && codeElement.TryGetInt32(out int value) ? value : -1;
            string message = root.TryGetProperty("Message", out JsonElement messageElement) ? messageElement.GetString() ?? "Unknown error" : "Unknown error";
            if (!success || errorCode != 0)
                throw new InvalidOperationException($"SMS provider rejected the request (code {errorCode}): {message}");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("SMS provider returned an invalid response.", exception);
        }
    }

    private static string NormalizeDestination(string phoneNumber)
    {
        string digits = new(phoneNumber.Where(char.IsDigit).ToArray());
        return digits.Length == 9 && digits[0] == '5' ? $"995{digits}" : digits;
    }

    private static string Encode(string value) => Uri.EscapeDataString(value);
}
