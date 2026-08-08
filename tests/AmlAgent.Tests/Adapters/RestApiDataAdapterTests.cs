using System.Net;
using System.Text;
using AmlAgent.Adapters;
using AmlAgent.Adapters.Configuration;
using AmlAgent.Adapters.Web;
using Xunit;

namespace AmlAgent.Tests.Adapters;

/// <summary>
/// Genuine end-to-end tests against a real local HTTP server (System.Net.HttpListener
/// spun up in-process on 127.0.0.1) -- not a mocked HttpMessageHandler. This is the
/// approach the CLI-Only spec itself calls for: "REST adapter tested against a real
/// local test HTTP server". No external infrastructure needed, so these run always-on.
/// </summary>
public class RestApiDataAdapterTests : IDisposable
{
    private const string ProfileName = "test-rest";
    private const string TokenProfileName = "test-rest-token";
    private readonly List<string> _setEnvVars = new();

    public void Dispose()
    {
        foreach (var v in _setEnvVars)
            Environment.SetEnvironmentVariable(v, null);
    }

    private void SetProfile(string profileName, string value)
    {
        var envVar = ConnectionProfileResolver.EnvVarNameFor(profileName);
        Environment.SetEnvironmentVariable(envVar, value);
        _setEnvVars.Add(envVar);
    }

    [SkippableFact]
    public async Task LoadAsync_RealHttpServer_LoadsAndNormalisesTransactions()
    {
        using var server = TestHttpServer.TryStart(_ => TestHttpServer.JsonResponse(HttpStatusCode.OK, """
        [
          {"transaction_id":"T1-001","source_account":"N001","destination_account":"N002","amount":"4500.00","currency":"usd","timestamp":"2026-01-19T10:00:00Z","channel":"wire","jurisdiction":"US","sar_linked":"true"}
        ]
        """));
        Skip.If(server is null, "could not bind a local HttpListener port in this environment");

        SetProfile(ProfileName, server!.BaseUrl);
        var adapter = new RestApiDataAdapter();
        var source = new DataSourceConfiguration("rest", ConnectionProfile: ProfileName, Query: "transactions");
        var dataset = await adapter.LoadAsync(source);

        var t1 = Assert.Single(dataset.Transactions);
        Assert.Equal("T1-001", t1.TransactionId);
        Assert.Equal("N001", t1.SourceAccount);
        Assert.Equal(4500.00m, t1.Amount);
        Assert.Equal("USD", t1.Currency);
        Assert.True(t1.SarLinked);
        Assert.Equal("rest", t1.SourceLineage.SourceType);
        Assert.Equal("rest", t1.SourceLineage.Adapter);
    }

    [SkippableFact]
    public async Task LoadAsync_QueryAppendedToBaseUrl_ServerReceivesCorrectPath()
    {
        string? receivedPath = null;
        using var server = TestHttpServer.TryStart(ctx =>
        {
            receivedPath = ctx.Request.Url!.AbsolutePath;
            return TestHttpServer.JsonResponse(HttpStatusCode.OK, "[]");
        });
        Skip.If(server is null, "could not bind a local HttpListener port in this environment");

        SetProfile(ProfileName, server!.BaseUrl);
        var adapter = new RestApiDataAdapter();
        var source = new DataSourceConfiguration("rest", ConnectionProfile: ProfileName, Query: "v1/transactions");
        await adapter.LoadAsync(source);

        Assert.Equal("/v1/transactions", receivedPath);
    }

    [SkippableFact]
    public async Task LoadAsync_BearerTokenFromSecondProfile_SentAsAuthorizationHeader()
    {
        string? receivedAuthHeader = null;
        using var server = TestHttpServer.TryStart(ctx =>
        {
            receivedAuthHeader = ctx.Request.Headers["Authorization"];
            return TestHttpServer.JsonResponse(HttpStatusCode.OK, "[]");
        });
        Skip.If(server is null, "could not bind a local HttpListener port in this environment");

        SetProfile(ProfileName, server!.BaseUrl);
        SetProfile(TokenProfileName, "sekrit-token-value");
        var adapter = new RestApiDataAdapter();
        var source = new DataSourceConfiguration("rest", ConnectionProfile: ProfileName,
            ExtraOptions: new Dictionary<string, string> { ["AuthTokenProfile"] = TokenProfileName });
        await adapter.LoadAsync(source);

        Assert.Equal("Bearer sekrit-token-value", receivedAuthHeader);
    }

    [SkippableFact]
    public async Task LoadAsync_NoAuthTokenProfileConfigured_SendsNoAuthorizationHeader()
    {
        string? receivedAuthHeader = null;
        using var server = TestHttpServer.TryStart(ctx =>
        {
            receivedAuthHeader = ctx.Request.Headers["Authorization"];
            return TestHttpServer.JsonResponse(HttpStatusCode.OK, "[]");
        });
        Skip.If(server is null, "could not bind a local HttpListener port in this environment");

        SetProfile(ProfileName, server!.BaseUrl);
        var adapter = new RestApiDataAdapter();
        var source = new DataSourceConfiguration("rest", ConnectionProfile: ProfileName);
        await adapter.LoadAsync(source);

        Assert.Null(receivedAuthHeader);
    }

    [SkippableFact]
    public async Task LoadAsync_401Response_ThrowsWithAuthenticationFailedMessage()
    {
        using var server = TestHttpServer.TryStart(_ => TestHttpServer.JsonResponse(HttpStatusCode.Unauthorized, "{}"));
        Skip.If(server is null, "could not bind a local HttpListener port in this environment");

        SetProfile(ProfileName, server!.BaseUrl);
        var adapter = new RestApiDataAdapter();
        var source = new DataSourceConfiguration("rest", ConnectionProfile: ProfileName);

        var ex = await Assert.ThrowsAsync<AdapterSourceException>(() => adapter.LoadAsync(source));
        Assert.Contains("authentication failed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task LoadAsync_403Response_ThrowsWithAuthenticationFailedMessage()
    {
        using var server = TestHttpServer.TryStart(_ => TestHttpServer.JsonResponse(HttpStatusCode.Forbidden, "{}"));
        Skip.If(server is null, "could not bind a local HttpListener port in this environment");

        SetProfile(ProfileName, server!.BaseUrl);
        var adapter = new RestApiDataAdapter();
        var source = new DataSourceConfiguration("rest", ConnectionProfile: ProfileName);

        var ex = await Assert.ThrowsAsync<AdapterSourceException>(() => adapter.LoadAsync(source));
        Assert.Contains("authentication failed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task LoadAsync_500Response_ThrowsAdapterSourceExceptionWithStatusCode()
    {
        using var server = TestHttpServer.TryStart(_ => TestHttpServer.JsonResponse(HttpStatusCode.InternalServerError, "{}"));
        Skip.If(server is null, "could not bind a local HttpListener port in this environment");

        SetProfile(ProfileName, server!.BaseUrl);
        var adapter = new RestApiDataAdapter();
        var source = new DataSourceConfiguration("rest", ConnectionProfile: ProfileName);

        var ex = await Assert.ThrowsAsync<AdapterSourceException>(() => adapter.LoadAsync(source));
        Assert.Contains("500", ex.Message);
    }

    [SkippableFact]
    public async Task LoadAsync_MalformedJsonBody_ThrowsAdapterSourceException()
    {
        using var server = TestHttpServer.TryStart(_ => TestHttpServer.JsonResponse(HttpStatusCode.OK, "{not valid json"));
        Skip.If(server is null, "could not bind a local HttpListener port in this environment");

        SetProfile(ProfileName, server!.BaseUrl);
        var adapter = new RestApiDataAdapter();
        var source = new DataSourceConfiguration("rest", ConnectionProfile: ProfileName);

        await Assert.ThrowsAsync<AdapterSourceException>(() => adapter.LoadAsync(source));
    }

    [Fact]
    public async Task LoadAsync_ConnectionRefused_ThrowsAdapterSourceException()
    {
        SetProfile(ProfileName, "http://127.0.0.1:1/"); // port 1: nothing listens, connection refused
        var adapter = new RestApiDataAdapter();
        var source = new DataSourceConfiguration("rest", ConnectionProfile: ProfileName);

        await Assert.ThrowsAsync<AdapterSourceException>(() => adapter.LoadAsync(source));
    }

    [Fact]
    public async Task LoadAsync_MissingConnectionProfile_ThrowsInvalidAdapterConfigurationException()
    {
        var adapter = new RestApiDataAdapter();
        var source = new DataSourceConfiguration("rest");
        await Assert.ThrowsAsync<InvalidAdapterConfigurationException>(() => adapter.LoadAsync(source));
    }

    /// <summary>Minimal real HTTP server for in-process REST adapter testing -- no mocking framework involved.</summary>
    private sealed class TestHttpServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly Func<HttpListenerContext, (HttpStatusCode Status, string Body)> _handler;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        public string BaseUrl { get; }

        private TestHttpServer(HttpListener listener, int port, Func<HttpListenerContext, (HttpStatusCode, string)> handler)
        {
            _listener = listener;
            _handler = handler;
            BaseUrl = $"http://127.0.0.1:{port}/";
            _loop = Task.Run(AcceptLoop);
        }

        public static TestHttpServer? TryStart(Func<HttpListenerContext, (HttpStatusCode Status, string Body)> handler)
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var port = Random.Shared.Next(20000, 60000);
                var listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                try
                {
                    listener.Start();
                    return new TestHttpServer(listener, port, handler);
                }
                catch (HttpListenerException)
                {
                    listener.Close();
                }
                catch (ObjectDisposedException)
                {
                }
            }
            return null;
        }

        public static (HttpStatusCode, string) JsonResponse(HttpStatusCode status, string body) => (status, body);

        private async Task AcceptLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch (Exception) { return; }

                try
                {
                    var (status, body) = _handler(ctx);
                    var bytes = Encoding.UTF8.GetBytes(body);
                    ctx.Response.StatusCode = (int)status;
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.ContentLength64 = bytes.Length;
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                    ctx.Response.OutputStream.Close();
                }
                catch (Exception)
                {
                    try { ctx.Response.Abort(); } catch { /* best effort */ }
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { /* best effort */ }
            try { _listener.Close(); } catch { /* best effort */ }
        }
    }
}
