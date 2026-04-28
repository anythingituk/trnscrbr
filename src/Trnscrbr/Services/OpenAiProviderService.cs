using System.Net.Http;
using System.Net.Http.Headers;

namespace Trnscrbr.Services;

public sealed class OpenAiProviderService
{
    private static readonly Uri ModelsUri = new("https://api.openai.com/v1/models");
    private readonly HttpClient _httpClient = new();

    public async Task<ProviderTestResult> TestApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return ProviderTestResult.Fail("API key is empty.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, ModelsUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return ProviderTestResult.Success();
            }

            return ProviderTestResult.Fail($"OpenAI test failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ProviderTestResult.Fail($"OpenAI test failed: {ex.Message}");
        }
    }
}

public sealed record ProviderTestResult(bool IsSuccess, string Message)
{
    public static ProviderTestResult Success() => new(true, "Connection successful.");

    public static ProviderTestResult Fail(string message) => new(false, message);
}
