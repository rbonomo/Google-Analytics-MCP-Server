#!/usr/bin/env bash
# Launches the server in stdio mode for MCP clients (used by .mcp.json).
# Installs the .NET 10 SDK and builds on first use; "--prepare" does that and exits.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DLL="$ROOT/bin/Release/net10.0/GoogleAnalyticsMcp.dll"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}" PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

prepare() {
  if ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
    echo "Installing .NET 10 SDK..." >&2
    curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir "$HOME/.dotnet" >&2 9>&-
  fi
  if [ ! -f "$DLL" ] || [ -n "$(find "$ROOT" \( -name '*.cs' -o -name '*.csproj' \) -newer "$DLL" -not -path '*/obj/*' -print -quit)" ]; then
    echo "Building GoogleAnalyticsMcp..." >&2
    dotnet build "$ROOT/GoogleAnalyticsMcp.csproj" -c Release -nologo -v q --disable-build-servers >&2 9>&-
  fi
}

# Stdout belongs to the MCP protocol, so keep every setup message on stderr.
# The lock stops the SessionStart hook and the MCP launch from building at the same time.
exec 9>"${TMPDIR:-/tmp}/google-analytics-mcp.lock"
if command -v flock >/dev/null; then flock 9; fi
prepare
exec 9>&-

[ "${1:-}" = "--prepare" ] && exit 0
exec dotnet "$DLL" --stdio
