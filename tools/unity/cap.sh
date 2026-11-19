#!/bin/bash
# Run the built Linux player on level N with extra args (e.g. -autocapture screenshots/m4b -autocapture-m4b).
# usage: tools/unity/cap.sh N [extra args]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="$ROOT/screenshots/m4b"
PLAYER="$ROOT/builds/Linux/RocketSlam.x86_64"
[ -x "$PLAYER" ] || { echo "no player at $PLAYER (run full.sh first)" >&2; exit 1; }
[ $# -ge 1 ] || { echo "usage: $0 N [extra args]" >&2; exit 2; }
N="$1"; shift
mkdir -p "$OUT"
cd "$ROOT"
status=0
timeout 300 "$PLAYER" -screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -level "$N" "$@" -logFile "$OUT/player_$N.log" > /dev/null 2>&1 || status=$?
echo "exit $status"
grep -E "\[AutoCapture\] (result|.*png)|Exception|Error|amdgpu|signal" "$OUT/player_$N.log" | grep -v "Curl\|Access token" | head -20 || true
exit "$status"
