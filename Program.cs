using GoogleAnalyticsMcp.Options;
using GoogleAnalyticsMcp.Security;
using GoogleAnalyticsMcp.Services;
using ModelContextProtocol.Server;

// "--stdio": run as a local process launched by an MCP client (Claude Desktop, Claude Code)
// instead of as a web app. No HTTP listener, no API key; stdout carries the protocol.
if (args.Contains("--stdio"))
{
    var hostBuilder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args.Where(a => a != "--stdio").ToArray(),
        ContentRootPath = AppContext.BaseDirectory,
    });

    // stdout is reserved for MCP messages, so all logging goes to stderr.
    hostBuilder.Logging.ClearProviders();
    hostBuilder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

    AddGoogleAnalytics(hostBuilder.Services, hostBuilder.Configuration);
    hostBuilder.Services
        .AddMcpServer(ConfigureMcpServer)
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    await hostBuilder.Build().RunAsync();
    return;
}

var builder = WebApplication.CreateBuilder(args);

AddGoogleAnalytics(builder.Services, builder.Configuration);
builder.Services.Configure<McpSecurityOptions>(builder.Configuration.GetSection(McpSecurityOptions.SectionName));

builder.Services.AddControllersWithViews();

builder.Services
    .AddMcpServer(ConfigureMcpServer)
    // Stateless mode: no per-session state on the server, so IIS app pool recycles,
    // web gardens and load-balanced farms work without sticky sessions.
    .WithHttpTransport(options => options.Stateless = true)
    .WithToolsFromAssembly();

var app = builder.Build();

var mcpOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<McpSecurityOptions>>().Value;
if (mcpOptions.ApiKeys.Count == 0)
{
    app.Logger.LogWarning("Mcp:ApiKeys is empty - the MCP endpoint at {Path} is NOT protected. Configure an API key before deploying.", mcpOptions.Path);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseMiddleware<ApiKeyMiddleware>();
app.UseRouting();

app.MapMcp(mcpOptions.Path);
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

static void AddGoogleAnalytics(IServiceCollection services, IConfiguration configuration)
{
    services.Configure<GoogleAnalyticsOptions>(configuration.GetSection(GoogleAnalyticsOptions.SectionName));
    services.PostConfigure<GoogleAnalyticsOptions>(options =>
    {
        // Short environment variable names, handy for MCP client configs and cloud environments.
        options.CredentialsJson = NullIfEmpty(options.CredentialsJson) ?? NullIfEmpty(Environment.GetEnvironmentVariable("GA_SERVICE_ACCOUNT_JSON"));
        options.CredentialsPath = NullIfEmpty(options.CredentialsPath) ?? NullIfEmpty(Environment.GetEnvironmentVariable("GA_SERVICE_ACCOUNT_FILE"));
        options.DefaultPropertyId = NullIfEmpty(options.DefaultPropertyId) ?? NullIfEmpty(Environment.GetEnvironmentVariable("GA_PROPERTY_ID"));
    });
    services.AddSingleton<GoogleAnalyticsClients>();
}

static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

static void ConfigureMcpServer(McpServerOptions options)
{
    options.ServerInfo = new() { Name = "google-analytics", Version = "1.0.0" };
    options.ServerInstructions =
        "Read-only access to Google Analytics 4. Start with get_account_summaries to find property IDs, " +
        "use get_metadata to look up valid dimension/metric names, then call run_report or run_realtime_report.";
}
