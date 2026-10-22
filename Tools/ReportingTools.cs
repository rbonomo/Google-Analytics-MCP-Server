using System.ComponentModel;
using System.Text.Json;
using Google.Analytics.Data.V1Beta;
using GoogleAnalyticsMcp.Services;
using ModelContextProtocol.Server;
using static GoogleAnalyticsMcp.Services.ToolHelpers;

namespace GoogleAnalyticsMcp.Tools;

/// <summary>
/// Reporting tools backed by the Google Analytics Data API (GA4).
/// </summary>
[McpServerToolType]
public sealed class ReportingTools(GoogleAnalyticsClients clients)
{
    private const string PropertyIdHelp = "GA4 property ID, e.g. 123456789. Optional when a default property is configured.";

    private const string FilterHelp =
        "Optional GA4 Data API FilterExpression as JSON. Examples: " +
        "{\"filter\":{\"fieldName\":\"country\",\"stringFilter\":{\"value\":\"United States\"}}} | " +
        "{\"andGroup\":{\"expressions\":[{\"filter\":{\"fieldName\":\"deviceCategory\",\"inListFilter\":{\"values\":[\"mobile\",\"tablet\"]}}}," +
        "{\"notExpression\":{\"filter\":{\"fieldName\":\"pagePath\",\"stringFilter\":{\"matchType\":\"BEGINS_WITH\",\"value\":\"/admin\"}}}}]}} | " +
        "{\"filter\":{\"fieldName\":\"sessions\",\"numericFilter\":{\"operation\":\"GREATER_THAN\",\"value\":{\"int64Value\":\"100\"}}}}";

    private const string OrderByHelp =
        "Optional array of GA4 OrderBy objects as JSON. Examples: " +
        "[{\"metric\":{\"metricName\":\"sessions\"},\"desc\":true}] | [{\"dimension\":{\"dimensionName\":\"date\"}}]";

    [McpServerTool(Name = "run_report", Title = "Run GA4 report", ReadOnly = true, Idempotent = true)]
    [Description("Runs a Google Analytics 4 report for a date range and returns one row per unique dimension combination. " +
                 "Use get_metadata to discover valid dimension and metric API names (e.g. dimensions: date, country, " +
                 "sessionDefaultChannelGroup, pagePath, deviceCategory; metrics: activeUsers, sessions, screenPageViews, " +
                 "conversions, totalRevenue, engagementRate). Dates accept YYYY-MM-DD, 'today', 'yesterday' or 'NdaysAgo'.")]
    public Task<string> RunReport(
        [Description("Metric API names, e.g. [\"activeUsers\",\"sessions\"]. Max 10.")] string[] metrics,
        [Description("Start date: YYYY-MM-DD, 'today', 'yesterday' or 'NdaysAgo'. Default '28daysAgo'.")] string? startDate = null,
        [Description("End date: YYYY-MM-DD, 'today', 'yesterday' or 'NdaysAgo'. Default 'yesterday'.")] string? endDate = null,
        [Description("Dimension API names, e.g. [\"date\",\"country\"]. Max 9. Omit for a single totals row.")] string[]? dimensions = null,
        [Description(PropertyIdHelp)] string? propertyId = null,
        [Description("Optional comparison period start date (adds a 'dateRange' dimension to rows).")] string? compareStartDate = null,
        [Description("Optional comparison period end date.")] string? compareEndDate = null,
        [Description(FilterHelp)] JsonElement? dimensionFilter = null,
        [Description("Optional FilterExpression on metrics, applied after aggregation (same JSON shape as dimensionFilter).")] JsonElement? metricFilter = null,
        [Description(OrderByHelp)] JsonElement? orderBys = null,
        [Description("Maximum rows to return. Default 1000.")] int limit = 1000,
        [Description("Row offset for paging. Default 0.")] int offset = 0,
        [Description("Include a totals row for each metric.")] bool includeTotals = false,
        [Description("Return rows whose metrics are all zero.")] bool keepEmptyRows = false,
        [Description("Optional ISO 4217 currency code for revenue metrics, e.g. USD. Defaults to the property currency.")] string? currencyCode = null,
        CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
    {
        var request = new RunReportRequest
        {
            Property = clients.ResolvePropertyName(propertyId),
            Limit = ClampLimit(limit),
            Offset = Math.Max(0, offset),
            KeepEmptyRows = keepEmptyRows,
            ReturnPropertyQuota = true,
            CurrencyCode = currencyCode ?? string.Empty,
            DimensionFilter = ParseProto<FilterExpression>(dimensionFilter),
            MetricFilter = ParseProto<FilterExpression>(metricFilter),
        };

        request.DateRanges.Add(new DateRange
        {
            StartDate = Default(startDate, "28daysAgo"),
            EndDate = Default(endDate, "yesterday"),
            Name = compareStartDate is null ? string.Empty : "current",
        });
        if (!string.IsNullOrWhiteSpace(compareStartDate))
        {
            request.DateRanges.Add(new DateRange
            {
                StartDate = compareStartDate,
                EndDate = Default(compareEndDate, compareStartDate),
                Name = "previous",
            });
        }

        request.Dimensions.AddRange(SplitNames(dimensions).Select(n => new Dimension { Name = n }));
        request.Metrics.AddRange(RequireMetrics(metrics).Select(n => new Metric { Name = n }));
        request.OrderBys.AddRange(ParseProtoList<OrderBy>(orderBys));
        if (includeTotals)
        {
            request.MetricAggregations.Add(MetricAggregation.Total);
        }

        var data = await clients.GetDataClientAsync();
        var response = await data.RunReportAsync(request, cancellationToken);

        var headers = new ReportHeaders(response.DimensionHeaders, response.MetricHeaders, response.Rows.Count);
        return FlattenReport(headers, response.Rows, response.Totals, response.RowCount, new
        {
            response.Metadata.CurrencyCode,
            response.Metadata.TimeZone,
            response.Metadata.SubjectToThresholding,
            dataLossFromOtherRow = response.Metadata.DataLossFromOtherRow,
            sampling = response.Metadata.SamplingMetadatas.Count == 0 ? null : response.Metadata.SamplingMetadatas
                .Select(s => new { s.SamplesReadCount, s.SamplingSpaceSize }),
        }, response.PropertyQuota);
    });

    [McpServerTool(Name = "run_realtime_report", Title = "Run GA4 realtime report", ReadOnly = true, Idempotent = true)]
    [Description("Runs a realtime report covering activity in roughly the last 30 minutes (up to 60 for GA4 360). " +
                 "Only realtime-compatible fields work, e.g. dimensions: country, city, deviceCategory, unifiedScreenName, " +
                 "eventName, minutesAgo, platform; metrics: activeUsers, eventCount, screenPageViews, keyEvents.")]
    public Task<string> RunRealtimeReport(
        [Description("Metric API names, e.g. [\"activeUsers\"].")] string[] metrics,
        [Description("Dimension API names, e.g. [\"country\"]. Omit for a single totals row.")] string[]? dimensions = null,
        [Description(PropertyIdHelp)] string? propertyId = null,
        [Description(FilterHelp)] JsonElement? dimensionFilter = null,
        [Description("Optional FilterExpression on metrics (same JSON shape as dimensionFilter).")] JsonElement? metricFilter = null,
        [Description(OrderByHelp)] JsonElement? orderBys = null,
        [Description("Only include activity from the last N minutes (1-29 standard, up to 59 for GA4 360). Default 29.")] int minutes = 29,
        [Description("Maximum rows to return. Default 1000.")] int limit = 1000,
        [Description("Include a totals row for each metric.")] bool includeTotals = false,
        CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
    {
        var request = new RunRealtimeReportRequest
        {
            Property = clients.ResolvePropertyName(propertyId),
            Limit = ClampLimit(limit),
            ReturnPropertyQuota = true,
            DimensionFilter = ParseProto<FilterExpression>(dimensionFilter),
            MetricFilter = ParseProto<FilterExpression>(metricFilter),
        };

        request.MinuteRanges.Add(new MinuteRange { StartMinutesAgo = Math.Clamp(minutes, 1, 59), EndMinutesAgo = 0 });
        request.Dimensions.AddRange(SplitNames(dimensions).Select(n => new Dimension { Name = n }));
        request.Metrics.AddRange(RequireMetrics(metrics).Select(n => new Metric { Name = n }));
        request.OrderBys.AddRange(ParseProtoList<OrderBy>(orderBys));
        if (includeTotals)
        {
            request.MetricAggregations.Add(MetricAggregation.Total);
        }

        var data = await clients.GetDataClientAsync();
        var response = await data.RunRealtimeReportAsync(request, cancellationToken);

        var headers = new ReportHeaders(response.DimensionHeaders, response.MetricHeaders, response.Rows.Count);
        return FlattenReport(headers, response.Rows, response.Totals, response.RowCount, null, response.PropertyQuota);
    });

    [McpServerTool(Name = "get_metadata", Title = "List available dimensions and metrics", ReadOnly = true, Idempotent = true)]
    [Description("Lists the dimensions and metrics available for reports on a property, including its custom definitions. " +
                 "Use 'search' to narrow the (long) list, e.g. 'revenue', 'page', 'source'.")]
    public Task<string> GetMetadata(
        [Description(PropertyIdHelp)] string? propertyId = null,
        [Description("Optional case-insensitive text to match against API name, UI name, description or category.")] string? search = null,
        [Description("Which fields to return: 'all' (default), 'dimensions' or 'metrics'.")] string type = "all",
        CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
    {
        var data = await clients.GetDataClientAsync();
        var metadata = await data.GetMetadataAsync($"{clients.ResolvePropertyName(propertyId)}/metadata", cancellationToken);

        bool Matches(params string[] fields) =>
            string.IsNullOrWhiteSpace(search) || fields.Any(f => f.Contains(search, StringComparison.OrdinalIgnoreCase));

        var wantDimensions = !type.Equals("metrics", StringComparison.OrdinalIgnoreCase);
        var wantMetrics = !type.Equals("dimensions", StringComparison.OrdinalIgnoreCase);

        return new
        {
            dimensions = wantDimensions
                ? metadata.Dimensions
                    .Where(d => Matches(d.ApiName, d.UiName, d.Description, d.Category))
                    .Select(d => new { d.ApiName, d.UiName, d.Category, d.Description, custom = d.CustomDefinition })
                    .ToList()
                : null,
            metrics = wantMetrics
                ? metadata.Metrics
                    .Where(m => Matches(m.ApiName, m.UiName, m.Description, m.Category))
                    .Select(m => new { m.ApiName, m.UiName, m.Category, type = m.Type.ToString(), m.Description, custom = m.CustomDefinition })
                    .ToList()
                : null,
        };
    });

    private long ClampLimit(int limit) => Math.Clamp(limit, 1, clients.Options.MaxRows);

    private static string Default(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static List<string> RequireMetrics(string[]? metrics)
    {
        var names = SplitNames(metrics);
        return names.Count > 0 ? names : throw new ArgumentException("At least one metric is required, e.g. [\"activeUsers\"].");
    }
}
