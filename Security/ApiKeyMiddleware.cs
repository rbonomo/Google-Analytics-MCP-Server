using System.Security.Cryptography;
using System.Text;
using GoogleAnalyticsMcp.Options;
using Microsoft.Extensions.Options;

namespace GoogleAnalyticsMcp.Security;

/// <summary>
/// Guards the MCP endpoint with a shared API key. Accepts either
/// "Authorization: Bearer {key}" or "X-API-Key: {key}".
/// </summary>
public sealed class ApiKeyMiddleware(RequestDelegate next, IOptions<McpSecurityOptions> options, ILogger<ApiKeyMiddleware> logger)
{
    private readonly McpSecurityOptions _options = options.Value;
    private readonly byte[][] _keys = options.Value.ApiKeys
        .Where(k => !string.IsNullOrWhiteSpace(k))
        .Select(k => Encoding.UTF8.GetBytes(k))
        .ToArray();

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(_options.Path) || _keys.Length == 0)
        {
            await next(context);
            return;
        }

        var supplied = GetSuppliedKey(context.Request);
        if (supplied is not null && IsValid(supplied))
        {
            await next(context);
            return;
        }

        logger.LogWarning("Rejected MCP request from {RemoteIp}: missing or invalid API key", context.Connection.RemoteIpAddress);
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer";
        await context.Response.WriteAsync("Unauthorized");
    }

    private static string? GetSuppliedKey(HttpRequest request)
    {
        var auth = request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return auth["Bearer ".Length..].Trim();
        }

        var header = request.Headers["X-API-Key"].ToString();
        return string.IsNullOrEmpty(header) ? null : header.Trim();
    }

    private bool IsValid(string supplied)
    {
        var bytes = Encoding.UTF8.GetBytes(supplied);
        var match = false;
        foreach (var key in _keys)
        {
            // Check every key so timing doesn't reveal which one matched.
            match |= CryptographicOperations.FixedTimeEquals(bytes, key);
        }
        return match;
    }
}
