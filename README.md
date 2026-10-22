# Google Analytics MCP Server (ASP.NET Core / IIS)

A read-only [Model Context Protocol](https://modelcontextprotocol.io) server for **Google Analytics 4**, built on
ASP.NET Core (.NET 10) with the official C# MCP SDK. It exposes a Streamable HTTP endpoint at `/mcp` and is designed
to be hosted on IIS. An MVC status page at `/` shows the endpoint URL, configuration state and the tool list.

## Tools

| Tool | What it does |
|---|---|
| `get_account_summaries` | Lists accounts and GA4 properties the credentials can see (start here to find property IDs). |
| `get_property_details` | Property settings: time zone, currency, industry, service level. |
| `get_custom_dimensions_and_metrics` | Custom definitions, with the `customEvent:` / `customUser:` API names to use in reports. |
| `list_google_ads_links` | Google Ads accounts linked to the property. |
| `get_metadata` | Searchable list of every dimension and metric available on the property. |
| `run_report` | Core reporting: date range (+ optional comparison period), dimensions, metrics, filters, ordering, paging, totals. |
| `run_realtime_report` | Activity in the last 1–30 minutes (up to 60 on GA4 360). |

Filters and ordering take the GA4 Data API JSON shapes (`FilterExpression`, `OrderBy`), e.g.

```json
{ "filter": { "fieldName": "country", "stringFilter": { "value": "United States" } } }
```

Reports are returned as compact JSON: `{ rowCount, dimensions, metrics, rows: [{ "date": "20261001", "sessions": "512" }], metadata, quota }`.

## 1. Google Cloud setup

1. In a Google Cloud project, enable the **Google Analytics Data API** and **Google Analytics Admin API**.
2. Create a **service account** and download a JSON key.
3. In Google Analytics → **Admin → Property access management**, add the service account's email
   (`...@...iam.gserviceaccount.com`) with the **Viewer** role on each property (or at the account level).

The server only requests the `analytics.readonly` scope.

## 2. Configuration

`appsettings.json` (or environment variables, e.g. `GoogleAnalytics__CredentialsPath`, `Mcp__ApiKeys__0`):

```json
"GoogleAnalytics": {
  "CredentialsPath": "C:\\secure\\ga-service-account.json",
  "DefaultPropertyId": "123456789",
  "MaxRows": 10000
},
"Mcp": {
  "Path": "/mcp",
  "ApiKeys": [ "a-long-random-secret" ]
}
```

- **CredentialsPath** – service account key file. Leave empty to use Application Default Credentials
  (`GOOGLE_APPLICATION_CREDENTIALS`, `gcloud auth application-default login`, or GCP metadata server).
- **DefaultPropertyId** – used when a tool call omits `propertyId`. Optional.
- **ApiKeys** – clients must send `Authorization: Bearer <key>` or `X-API-Key: <key>`. **If empty the endpoint is open**
  (a warning is logged and shown on the status page). Generate a key with
  `[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))` in PowerShell.

Keep secrets out of source control: on the server, prefer environment variables set in `web.config`
(`<environmentVariables>`) or the app pool, and store the JSON key outside the site folder.

## 3. Run locally

```bash
dotnet run
# status page: http://localhost:5163/   MCP endpoint: http://localhost:5163/mcp
```

Test with the MCP Inspector: `npx @modelcontextprotocol/inspector` → transport *Streamable HTTP* →
URL `http://localhost:5163/mcp` → add header `Authorization: Bearer <key>`.

## 4. Deploy to IIS

1. On the server install the **.NET 10 Hosting Bundle** (includes the ASP.NET Core Module V2), then `iisreset`.
2. Publish:
   ```bash
   dotnet publish -c Release -o ./publish
   ```
3. In IIS Manager create a site (or application) pointing at the publish folder. Set its app pool to
   **No Managed Code**.
4. Grant the app pool identity (`IIS AppPool\<PoolName>`) **read** access to the service account JSON key file.
5. Bind **HTTPS** with a certificate – API keys must never travel over plain HTTP.
6. Set `GoogleAnalytics__CredentialsPath` and `Mcp__ApiKeys__0` as `<environmentVariable>` entries in the
   published `web.config`, then browse to the site root to confirm the status page shows *API key required* and the
   key file as *found*.

Notes:
- The MCP transport runs in **stateless** mode, so app pool recycles, web gardens and load-balanced farms work without
  sticky sessions.
- Dynamic compression is disabled in `web.config` so streamed (`text/event-stream`) responses aren't buffered.
- Set `stdoutLogEnabled="true"` in `web.config` temporarily if the site fails to start (logs go to `.\logs`).

## 5. Connect a client

**Claude Code**

```bash
claude mcp add --transport http google-analytics https://analytics-mcp.example.com/mcp \
  --header "Authorization: Bearer <key>"
```

**Claude Desktop / other clients** (`mcp.json`-style config):

```json
{
  "mcpServers": {
    "google-analytics": {
      "type": "http",
      "url": "https://analytics-mcp.example.com/mcp",
      "headers": { "Authorization": "Bearer <key>" }
    }
  }
}
```

Example prompts: *"Which channels drove the most sessions last month vs. the month before?"*,
*"Top 10 landing pages by conversions in the last 7 days"*, *"How many users are on the site right now, by country?"*

## Project layout

```
Program.cs                      Host setup: MCP server (stateless HTTP), API key middleware, MVC
Options/GoogleAnalyticsOptions  Strongly typed settings (GoogleAnalytics, Mcp sections)
Services/GoogleAnalyticsClients Credential loading + cached Data/Admin API clients
Services/ToolHelpers            JSON output, filter parsing, report flattening, error mapping
Security/ApiKeyMiddleware       Bearer / X-API-Key check on the MCP path (constant-time compare)
Tools/AdminTools                Account, property, custom definition and Ads link tools
Tools/ReportingTools            run_report, run_realtime_report, get_metadata
Controllers + Views             Status page
web.config                      IIS (ASP.NET Core Module V2, in-process)
```
