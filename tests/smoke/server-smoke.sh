#!/usr/bin/env bash
#
# Smoke test against the real game: boots a throwaway dedicated server with the Release build of the mod
# and checks the mod loaded and hooked the waypoint layer. Takes ~20 s (a fresh world is generated each run).
#
# Usage: VINTAGE_STORY=/path/to/game tests/smoke/server-smoke.sh   (keeps the work dir with KEEP=1)
#
set -euo pipefail

: "${VINTAGE_STORY:?set VINTAGE_STORY to the game directory (the one containing VintagestoryServer.dll)}"
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work="$(mktemp -d -t vsws-smoke.XXXXXX)"
timeout_s="${SMOKE_TIMEOUT:-240}"

cleanup() {
    [[ -n "${keepalive:-}" ]] && kill "$keepalive" 2>/dev/null || true
    [[ -n "${server:-}" ]] && kill "$server" 2>/dev/null || true
    if [[ "${KEEP:-0}" == 1 ]]; then echo "work dir kept: $work"; else rm -rf "$work"; fi
}
trap cleanup EXIT

echo "== Building Release"
dotnet build "$repo/VsWaypointSharing.csproj" -c Release -nologo -v quiet
mkdir -p "$work/mods" "$work/data"
cp "$repo/bin/Release/VsWaypointSharing.zip" "$work/mods/"

echo "== Starting dedicated server (timeout ${timeout_s}s)"
mkfifo "$work/stdin"
sleep infinity > "$work/stdin" & keepalive=$!   # holds the fifo open so the server console doesn't see EOF
dotnet "$VINTAGE_STORY/VintagestoryServer.dll" --dataPath "$work/data" --addModPath "$work/mods" \
    < "$work/stdin" > "$work/console.log" 2>&1 & server=$!

for ((i = 0; i < timeout_s; i++)); do
    grep -q "Dedicated Server now running" "$work/console.log" 2>/dev/null && break
    kill -0 "$server" 2>/dev/null || { echo "FAIL: server exited during startup"; tail -30 "$work/console.log"; exit 1; }
    sleep 1
done
if ((i >= timeout_s)); then echo "FAIL: server did not finish starting"; tail -30 "$work/console.log"; exit 1; fi
echo "   up after ${i}s, stopping"
echo "/stop" > "$work/stdin"
wait "$server" || true
server=

log="$work/data/Logs/server-main.log"
fail=0
check() {
    if grep -q -- "$2" "$log"; then echo "PASS: $1"; else echo "FAIL: $1 (no '$2' in server-main.log)"; fail=1; fi
}
check "mod loaded"                 "Mod 'VsWaypointSharing.zip' (VsWaypointSharing)"
check "mod system started"         "Build of VsWaypointSharing is of type: Release"
check "waypoint layer hooked"      "VsWaypointSharing: waypoint layer hooked"

if grep -E "\[(Error|Fatal)\]" "$log" "$work/data/Logs/server-debug.log"; then
    echo "FAIL: errors in server logs (above)"
    fail=1
else
    echo "PASS: no errors in server logs"
fi

exit "$fail"
