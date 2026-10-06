using System.Net.Http.Headers;
using System.Net.Http.Json;
using Troop786.Core;

namespace Troop786.Tablet.Services;

/// <summary>
/// Calls the kiosk endpoints on the existing API Gateway. Any non-success status throws HttpRequestException,
/// which the sync service treats as "try again later" and leaves the votes queued.
/// </summary>
public sealed class HttpKioskApi : IKioskApi
{
    private readonly HttpClient _http;
    private readonly Func<string?> _token;

    public HttpKioskApi(HttpClient http, Func<string?> deviceToken)
    {
        _http = http;
        _token = deviceToken;
    }

    public async Task<VoteBatchResponse> PostVotesAsync(VoteBatchRequest request, CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, "kiosk/votes")
        {
            Content = JsonContent.Create(request)
        };
        var token = _token();
        if (!string.IsNullOrEmpty(token))
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await _http.SendAsync(msg, ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<VoteBatchResponse>(cancellationToken: ct);
        return body ?? throw new HttpRequestException("Empty response from kiosk API");
    }
}
