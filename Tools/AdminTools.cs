using System.ComponentModel;
using Google.Analytics.Admin.V1Beta;
using GoogleAnalyticsMcp.Services;
using ModelContextProtocol.Server;
using static GoogleAnalyticsMcp.Services.ToolHelpers;

namespace GoogleAnalyticsMcp.Tools;

/// <summary>
/// Account and property information from the Google Analytics Admin API.
/// </summary>
[McpServerToolType]
public sealed class AdminTools(GoogleAnalyticsClients clients)
{
    [McpServerTool(Name = "get_account_summaries", Title = "List GA accounts and properties", ReadOnly = true, Idempotent = true)]
    [Description("Lists every Google Analytics account and GA4 property the server's credentials can access. " +
                 "Use this first to discover property IDs for the other tools.")]
    public Task<string> GetAccountSummaries(CancellationToken cancellationToken) => ExecuteAsync(async () =>
    {
        var admin = await clients.GetAdminClientAsync();
        var accounts = new List<object>();

        await foreach (var summary in admin.ListAccountSummariesAsync(new ListAccountSummariesRequest { PageSize = 200 })
                           .WithCancellation(cancellationToken))
        {
            accounts.Add(new
            {
                account = summary.Account,
                displayName = summary.DisplayName,
                properties = summary.PropertySummaries.Select(p => new
                {
                    propertyId = p.Property.Replace("properties/", ""),
                    displayName = p.DisplayName,
                    propertyType = p.PropertyType.ToString(),
                    parent = p.Parent,
                }),
            });
        }

        return new { accounts };
    });

    [McpServerTool(Name = "get_property_details", Title = "Get GA4 property details", ReadOnly = true, Idempotent = true)]
    [Description("Returns settings for a GA4 property: display name, time zone, currency, industry, service level and timestamps.")]
    public Task<string> GetPropertyDetails(
        [Description("GA4 property ID, e.g. 123456789. Optional when a default property is configured.")] string? propertyId,
        CancellationToken cancellationToken) => ExecuteAsync(async () =>
    {
        var admin = await clients.GetAdminClientAsync();
        var property = await admin.GetPropertyAsync(clients.ResolvePropertyName(propertyId), cancellationToken);
        return ProtoToJson(property);
    });

    [McpServerTool(Name = "get_custom_dimensions_and_metrics", Title = "List custom dimensions and metrics", ReadOnly = true, Idempotent = true)]
    [Description("Lists the custom dimensions and custom metrics defined on a GA4 property. " +
                 "In reports, event-scoped custom dimensions are queried as 'customEvent:{parameterName}' and " +
                 "user-scoped ones as 'customUser:{parameterName}'.")]
    public Task<string> GetCustomDimensionsAndMetrics(
        [Description("GA4 property ID, e.g. 123456789. Optional when a default property is configured.")] string? propertyId,
        CancellationToken cancellationToken) => ExecuteAsync(async () =>
    {
        var admin = await clients.GetAdminClientAsync();
        var parent = clients.ResolvePropertyName(propertyId);

        var dimensions = new List<object>();
        await foreach (var d in admin.ListCustomDimensionsAsync(parent, pageSize: 200).WithCancellation(cancellationToken))
        {
            dimensions.Add(new
            {
                d.ParameterName,
                d.DisplayName,
                d.Description,
                scope = d.Scope.ToString(),
                apiName = d.Scope switch
                {
                    CustomDimension.Types.DimensionScope.User => $"customUser:{d.ParameterName}",
                    CustomDimension.Types.DimensionScope.Item => $"customItem:{d.ParameterName}",
                    _ => $"customEvent:{d.ParameterName}",
                },
            });
        }

        var metrics = new List<object>();
        await foreach (var m in admin.ListCustomMetricsAsync(parent, pageSize: 200).WithCancellation(cancellationToken))
        {
            metrics.Add(new
            {
                m.ParameterName,
                m.DisplayName,
                m.Description,
                scope = m.Scope.ToString(),
                measurementUnit = m.MeasurementUnit.ToString(),
                apiName = $"customEvent:{m.ParameterName}",
            });
        }

        return new { customDimensions = dimensions, customMetrics = metrics };
    });

    [McpServerTool(Name = "list_google_ads_links", Title = "List Google Ads links", ReadOnly = true, Idempotent = true)]
    [Description("Lists the Google Ads accounts linked to a GA4 property.")]
    public Task<string> ListGoogleAdsLinks(
        [Description("GA4 property ID, e.g. 123456789. Optional when a default property is configured.")] string? propertyId,
        CancellationToken cancellationToken) => ExecuteAsync(async () =>
    {
        var admin = await clients.GetAdminClientAsync();
        var links = new List<object>();
        await foreach (var link in admin.ListGoogleAdsLinksAsync(clients.ResolvePropertyName(propertyId), pageSize: 200)
                           .WithCancellation(cancellationToken))
        {
            links.Add(ProtoToJson(link));
        }
        return new { googleAdsLinks = links };
    });
}
