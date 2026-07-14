#!/usr/bin/env bash
# Dev smoke-test driver for the MLQT MCP server (stdio transport).
#
# Sends the standard MCP handshake (initialize + notifications/initialized), then any
# additional JSON-RPC request lines supplied on stdin, and prints the server's responses.
#
# IMPORTANT: stdin must stay open until the server has flushed its async responses. Closing
# stdin (EOF) triggers server shutdown, which discards any responses still queued. This driver
# holds stdin open for a few seconds after sending requests to let responses flush.
#
# Usage:
#   ./dev/smoke.sh < requests.jsonl        # requests.jsonl = one JSON-RPC request per line
#   printf '%s\n' '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' | ./dev/smoke.sh
#
# Env:
#   MCP_EXE   override path to the built server exe
#   MCP_WAIT  seconds to hold stdin open after sending requests (default 3)

set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exe="${MCP_EXE:-$here/bin/Debug/net10.0/MLQT.McpServer.exe}"
wait_s="${MCP_WAIT:-3}"

if [[ ! -f "$exe" ]]; then
  echo "server exe not found: $exe (build first: dotnet build MLQT.McpServer/MLQT.McpServer.csproj)" >&2
  exit 1
fi

extra="$(cat)"

{
  printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"smoke","version":"1.0"}}}'
  printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
  [[ -n "$extra" ]] && printf '%s\n' "$extra"
  sleep "$wait_s"
} | "$exe" 2>/dev/null
