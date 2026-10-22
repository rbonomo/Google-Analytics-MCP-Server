using Google.Analytics.Admin.V1Beta;
using Google.Analytics.Data.V1Beta;
using Google.Apis.Auth.OAuth2;
using GoogleAnalyticsMcp.Options;
using Microsoft.Extensions.Options;

namespace GoogleAnalyticsMcp.Services;

/// <summary>
/// Lazily creates and caches the Google Analytics API clients. The gRPC clients are
/// thread-safe and expensive to build, so one instance is shared for the app lifetime.
/// </summary>
public sealed class GoogleAnalyticsClients
{
    private const string ReadOnlyScope = "https://www.googleapis.com/auth/analytics.readonly";

    private readonly GoogleAnalyticsOptions _options;
    private readonly RetryingLazy<GoogleCredential> _credential;
    private readonly RetryingLazy<BetaAnalyticsDataClient> _dataClient;
    private readonly RetryingLazy<AnalyticsAdminServiceClient> _adminClient;

    public GoogleAnalyticsClients(IOptions<GoogleAnalyticsOptions> options)
    {
        _options = options.Value;
        _credential = new(LoadCredentialAsync);
        _dataClient = new(async () => await new BetaAnalyticsDataClientBuilder
        {
            GoogleCredential = await _credential.GetAsync()
        }.BuildAsync());
        _adminClient = new(async () => await new AnalyticsAdminServiceClientBuilder
        {
            GoogleCredential = await _credential.GetAsync()
        }.BuildAsync());
    }

    public Task<BetaAnalyticsDataClient> GetDataClientAsync() => _dataClient.GetAsync();

    public Task<AnalyticsAdminServiceClient> GetAdminClientAsync() => _adminClient.GetAsync();

    public GoogleAnalyticsOptions Options => _options;

    /// <summary>
    /// Normalizes "123", "properties/123" or null (falls back to the configured default)
    /// into the "properties/123" resource name the APIs expect.
    /// </summary>
    public string ResolvePropertyName(string? propertyId)
    {
        var id = string.IsNullOrWhiteSpace(propertyId) ? _options.DefaultPropertyId : propertyId;
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "A property ID is required (no GoogleAnalytics:DefaultPropertyId is configured). " +
                "Call get_account_summaries to find one.");
        }

        id = id.Trim();
        if (id.StartsWith("properties/", StringComparison.OrdinalIgnoreCase))
        {
            id = id["properties/".Length..];
        }

        if (!id.All(char.IsDigit))
        {
            throw new ArgumentException($"'{propertyId}' is not a valid GA4 property ID; expected a number such as 123456789.");
        }

        return $"properties/{id}";
    }

    private async Task<GoogleCredential> LoadCredentialAsync()
    {
        var credential = string.IsNullOrWhiteSpace(_options.CredentialsPath)
            ? await GoogleCredential.GetApplicationDefaultAsync()
            : (await CredentialFactory.FromFileAsync<ServiceAccountCredential>(_options.CredentialsPath, CancellationToken.None)).ToGoogleCredential();

        return credential.CreateScoped(ReadOnlyScope);
    }
}

/// <summary>
/// Like Lazy&lt;Task&lt;T&gt;&gt;, but a failed initialization is discarded so the next call
/// retries. Lets the server recover once credentials are fixed without an app pool recycle.
/// </summary>
internal sealed class RetryingLazy<T>(Func<Task<T>> factory)
{
    private readonly object _gate = new();
    private Task<T>? _task;

    public Task<T> GetAsync()
    {
        lock (_gate)
        {
            if (_task is null || _task.IsFaulted || _task.IsCanceled)
            {
                _task = factory();
            }
            return _task;
        }
    }
}
