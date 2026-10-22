namespace GoogleAnalyticsMcp.Options;

/// <summary>
/// Settings bound from the "GoogleAnalytics" configuration section.
/// </summary>
public sealed class GoogleAnalyticsOptions
{
    public const string SectionName = "GoogleAnalytics";

    /// <summary>
    /// Path to a service account JSON key file. When empty, Application Default
    /// Credentials are used (GOOGLE_APPLICATION_CREDENTIALS, gcloud ADC, or the
    /// metadata server when running on Google Cloud).
    /// </summary>
    public string? CredentialsPath { get; set; }

    /// <summary>
    /// Service account key contents, as raw JSON or base64-encoded JSON. Takes precedence
    /// over <see cref="CredentialsPath"/>. Useful where only environment variables can be set.
    /// </summary>
    public string? CredentialsJson { get; set; }

    /// <summary>
    /// Optional property used when a tool call omits the property ID, e.g. "123456789".
    /// </summary>
    public string? DefaultPropertyId { get; set; }

    /// <summary>
    /// Hard cap on rows returned by a single report call, to keep responses small
    /// enough for an LLM context window.
    /// </summary>
    public int MaxRows { get; set; } = 10_000;
}

/// <summary>
/// Settings bound from the "Mcp" configuration section.
/// </summary>
public sealed class McpSecurityOptions
{
    public const string SectionName = "Mcp";

    /// <summary>
    /// Route the MCP endpoint is mapped to.
    /// </summary>
    public string Path { get; set; } = "/mcp";

    /// <summary>
    /// Accepted API keys. Clients send one as "Authorization: Bearer {key}" or
    /// "X-API-Key: {key}". When empty, the endpoint is open (development only).
    /// </summary>
    public List<string> ApiKeys { get; set; } = [];
}
