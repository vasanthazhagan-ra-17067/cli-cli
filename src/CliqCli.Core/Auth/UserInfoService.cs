using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CliqCli.Core.Auth;

/// <summary>
/// Calls <c>GET https://accounts.{domain}/oauth/user/info</c> to retrieve the authenticated
/// user's email address from the Zoho Accounts API (OQ-001).
/// </summary>
public sealed class UserInfoService : IUserInfoService
{
    private readonly HttpClient _httpClient;

    public UserInfoService(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<string?> GetUserEmailAsync(string domain, string token, CancellationToken ct = default)
    {
        var url = $"https://accounts.{domain}/oauth/user/info";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Zoho-oauthtoken", token);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new CliqCliException(
                $"Network error contacting Zoho user-info API: {ex.Message}",
                ErrorCodes.IoError, 1);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new CliqCliException(
                $"Timeout contacting Zoho user-info API: {ex.Message}",
                ErrorCodes.IoError, 1);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new CliqCliException(
                "Invalid token: Zoho user-info API returned 401 Unauthorized.",
                ErrorCodes.AuthFailure, exitCode: 2);

        if (!response.IsSuccessStatusCode)
            throw new CliqCliException(
                $"Zoho user-info API returned HTTP {(int)response.StatusCode}.",
                ErrorCodes.IoError, 1);

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("Email", out var emailElement)
            && emailElement.ValueKind == JsonValueKind.String)
        {
            return emailElement.GetString();
        }

        return null;
    }
}
