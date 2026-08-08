using System.Net;
using System.Net.Http.Headers;
using AmlAgent.Adapters.Canonical;
using AmlAgent.Adapters.Configuration;
using AmlAgent.Adapters.Formats;

namespace AmlAgent.Adapters.Web;

/// <summary>
/// Loads a transaction ledger from a REST/JSON API into the canonical
/// model. ConnectionProfile resolves the base URL; Query (if set) is
/// appended as the endpoint path. An optional bearer token is itself
/// resolved from a second connection profile named via
/// DataSourceConfiguration.ExtraOptions["AuthTokenProfile"] -- so no token
/// ever needs to sit in a task file either, keeping the same env-var-only
/// security boundary as the database adapters.
/// </summary>
public sealed class RestApiDataAdapter : IAmlDataAdapter
{
    private readonly HttpClient _httpClient;

    public RestApiDataAdapter(HttpClient? httpClient = null) => _httpClient = httpClient ?? new HttpClient();

    public string AdapterId => "rest";
    public string AdapterVersion => "1.0.0";

    public async Task<CanonicalAmlDataset> LoadAsync(DataSourceConfiguration source, CancellationToken cancellationToken = default)
    {
        var baseUrl = ConnectionProfileResolver.Resolve(source.ConnectionProfile, AdapterId);
        var url = CombineUrl(baseUrl, source.Query);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var tokenProfile = source.Option("AuthTokenProfile");
        if (!string.IsNullOrWhiteSpace(tokenProfile))
        {
            var token = ConnectionProfileResolver.Resolve(tokenProfile, AdapterId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new AdapterSourceException(AdapterId, $"REST request to '{url}' failed: {ex.Message}", ex);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new AdapterSourceException(AdapterId, $"API authentication failed: HTTP {(int)response.StatusCode} from '{url}'");
            if (!response.IsSuccessStatusCode)
                throw new AdapterSourceException(AdapterId, $"REST request to '{url}' returned HTTP {(int)response.StatusCode}");

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonRecordParser.ParseTransactions(body, "rest", url, AdapterId, AdapterVersion);
        }
    }

    private static string CombineUrl(string baseUrl, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return baseUrl;
        return baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
    }
}
