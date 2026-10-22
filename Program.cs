using GoogleAnalyticsMcp.Options;
using GoogleAnalyticsMcp.Security;
using GoogleAnalyticsMcp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<GoogleAnalyticsOptions>(builder.Configuration.GetSection(GoogleAnalyticsOptions.SectionName));
builder.Services.Configure<McpSecurityOptions>(builder.Configuration.GetSection(McpSecurityOptions.SectionName));
builder.Services.AddSingleton<GoogleAnalyticsClients>();

builder.Services.AddControllersWithViews();

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "google-analytics", Version = "1.0.0" };
        options.ServerInstructions =
            "Read-only access to Google Analytics 4. Start with get_account_summaries to find property IDs, " +
            "use get_metadata to look up valid dimension/metric names, then call run_report or run_realtime_report.";
    })
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
