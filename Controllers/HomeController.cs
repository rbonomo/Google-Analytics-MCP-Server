using System.ComponentModel;
using System.Reflection;
using GoogleAnalyticsMcp.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace GoogleAnalyticsMcp.Controllers;

/// <summary>
/// Status page for administrators: shows the MCP endpoint, configuration state and tool list.
/// </summary>
public class HomeController(IOptions<GoogleAnalyticsOptions> gaOptions, IOptions<McpSecurityOptions> mcpOptions) : Controller
{
    public IActionResult Index()
    {
        var ga = gaOptions.Value;
        var mcp = mcpOptions.Value;

        var model = new StatusViewModel
        {
            EndpointUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}{mcp.Path}",
            ApiKeyProtected = mcp.ApiKeys.Any(k => !string.IsNullOrWhiteSpace(k)),
            CredentialSource = !string.IsNullOrWhiteSpace(ga.CredentialsJson)
                ? "Service account key from configuration (JSON)"
                : string.IsNullOrWhiteSpace(ga.CredentialsPath)
                    ? "Application Default Credentials"
                    : $"Service account key file ({(System.IO.File.Exists(ga.CredentialsPath) ? "found" : "NOT FOUND")})",
            DefaultPropertyId = ga.DefaultPropertyId,
            Tools = typeof(HomeController).Assembly.GetTypes()
                .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
                .SelectMany(t => t.GetMethods())
                .Select(m => (Tool: m.GetCustomAttribute<McpServerToolAttribute>(), Description: m.GetCustomAttribute<DescriptionAttribute>()))
                .Where(x => x.Tool is not null)
                .Select(x => new ToolInfo(x.Tool!.Name ?? "", x.Description?.Description ?? ""))
                .OrderBy(t => t.Name)
                .ToList(),
        };

        return View(model);
    }

    public IActionResult Error() => View();
}

public sealed class StatusViewModel
{
    public required string EndpointUrl { get; init; }
    public required bool ApiKeyProtected { get; init; }
    public required string CredentialSource { get; init; }
    public string? DefaultPropertyId { get; init; }
    public required List<ToolInfo> Tools { get; init; }
}

public sealed record ToolInfo(string Name, string Description);
