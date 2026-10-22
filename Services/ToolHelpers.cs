using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Analytics.Data.V1Beta;
using Google.Protobuf;
using Grpc.Core;
using ModelContextProtocol;

namespace GoogleAnalyticsMcp.Services;

/// <summary>
/// Shared plumbing for the MCP tools: JSON output, protobuf parsing of filter
/// arguments, report flattening and error translation.
/// </summary>
public static class ToolHelpers
{
    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonParser ProtoParser = new(JsonParser.Settings.Default.WithIgnoreUnknownFields(false));

    public static string ToJson(object value) => JsonSerializer.Serialize(value, OutputOptions);

    /// <summary>
    /// Runs a Google API call and turns failures into MCP errors whose message the
    /// model can actually read and act on (the SDK hides messages of other exceptions).
    /// </summary>
    public static async Task<string> ExecuteAsync(Func<Task<object>> call)
    {
        try
        {
            return ToJson(await call());
        }
        catch (RpcException ex)
        {
            throw new McpException($"Google Analytics API error ({ex.StatusCode}): {ex.Status.Detail}");
        }
        catch (ArgumentException ex)
        {
            throw new McpException(ex.Message);
        }
        catch (InvalidProtocolBufferException ex)
        {
            throw new McpException($"Invalid filter/order JSON: {ex.Message}");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("credentials", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException($"Google credentials are not configured: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses a JSON argument into a protobuf message using the GA4 API's REST JSON shape.
    /// </summary>
    public static T? ParseProto<T>(JsonElement? json) where T : IMessage<T>, new()
    {
        if (json is null || json.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return default;
        }

        // Accept either a JSON object or a string containing JSON.
        var text = json.Value.ValueKind == JsonValueKind.String ? json.Value.GetString()! : json.Value.GetRawText();
        return string.IsNullOrWhiteSpace(text) ? default : ProtoParser.Parse<T>(text);
    }

    public static IEnumerable<T> ParseProtoList<T>(JsonElement? json) where T : IMessage<T>, new()
    {
        if (json is null || json.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        var element = json.Value;
        if (element.ValueKind == JsonValueKind.String)
        {
            element = JsonDocument.Parse(element.GetString()!).RootElement;
        }

        return element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray().Select(e => ProtoParser.Parse<T>(e.GetRawText())).ToList()
            : [ProtoParser.Parse<T>(element.GetRawText())];
    }

    /// <summary>
    /// Flattens a report into { headers, rows: [{ dim: value, metric: value }] } which
    /// is far more compact and readable for a model than the raw protobuf JSON.
    /// </summary>
    public static object FlattenReport(
        ReportHeaders headers,
        IEnumerable<Row> rows,
        IEnumerable<Row> totals,
        int rowCount,
        object? metadata,
        PropertyQuota? quota)
    {
        return new
        {
            rowCount,
            returnedRows = headers.RowsReturned,
            dimensions = headers.Dimensions,
            metrics = headers.Metrics.Select(m => new { m.Name, type = m.Type.ToString() }),
            rows = rows.Select(headers.ToRecord).ToList(),
            totals = totals.Select(headers.ToRecord).ToList() is { Count: > 0 } t ? t : null,
            metadata,
            quota = quota is null ? null : new
            {
                tokensPerDayRemaining = quota.TokensPerDay?.Remaining,
                tokensPerHourRemaining = quota.TokensPerHour?.Remaining,
                concurrentRequestsRemaining = quota.ConcurrentRequests?.Remaining,
            },
        };
    }

    /// <summary>
    /// Header information captured once per report so each row can be turned into a record.
    /// </summary>
    public sealed class ReportHeaders(IList<DimensionHeader> dimensions, IList<MetricHeader> metrics, int rowsReturned)
    {
        public List<string> Dimensions { get; } = dimensions.Select(d => d.Name).ToList();
        public IList<MetricHeader> Metrics { get; } = metrics;
        public int RowsReturned { get; } = rowsReturned;

        public Dictionary<string, string> ToRecord(Row row)
        {
            var record = new Dictionary<string, string>(Dimensions.Count + Metrics.Count);
            for (var i = 0; i < Dimensions.Count && i < row.DimensionValues.Count; i++)
            {
                record[Dimensions[i]] = row.DimensionValues[i].Value;
            }
            for (var i = 0; i < Metrics.Count && i < row.MetricValues.Count; i++)
            {
                record[Metrics[i].Name] = row.MetricValues[i].Value;
            }
            return record;
        }
    }

    /// <summary>
    /// Converts a protobuf message to its canonical JSON form so it serializes cleanly.
    /// </summary>
    public static JsonElement ProtoToJson(IMessage message) =>
        JsonDocument.Parse(JsonFormatter.Default.Format(message)).RootElement.Clone();

    public static List<string> SplitNames(IEnumerable<string>? names) =>
        (names ?? [])
            .SelectMany(n => n.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
