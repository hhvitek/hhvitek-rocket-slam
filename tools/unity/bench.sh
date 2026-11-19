#!/bin/bash
# Benchmark the built Linux player at 1920x1080 (Easy bot, chase cam) for each level given.
# usage: tools/unity/bench.sh 1 5
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="$ROOT/screenshots/m4b"
PLAYER="$ROOT/builds/Linux/RocketSlam.x86_64"
[ -x "$PLAYER" ] || { echo "no player at $PLAYER (run full.sh first)" >&2; exit 1; }
mkdir -p "$OUT"
for n in "$@"; do
  status=0
  timeout 300 "$PLAYER" -screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -level "$n" \
    -benchmark "$OUT/bench_L$n.txt" -logFile "$OUT/bench_L$n.log" > /dev/null 2>&1 || status=$?
  echo "== L$n exit $status"
  cat "$OUT/bench_L$n.txt" 2>/dev/null || echo "(no benchmark file)"
done
