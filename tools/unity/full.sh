#!/bin/bash
# Rebuild all level scenes + the Linux player, run EditMode tests and (with "play") PlayMode tests, print failures.
# usage: tools/unity/full.sh [play]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
UNITY="${UNITY:-$HOME/Unity/Hub/Editor/6000.6.9f1/Editor/Unity}"
cd "$ROOT"

status=0
timeout 1800 "$UNITY" -batchmode -projectPath unity -executeMethod BuildScript.BuildScenesAndLinux -quit -logFile unity/Logs/build.log || status=$?
grep -E "error CS|\[BuildScript\]" unity/Logs/build.log | head -5 || true
if [ "$status" -ne 0 ] || ! grep -q "\[BuildScript\] .*build succeeded" unity/Logs/build.log; then
  echo "BUILD FAILED (exit $status), see unity/Logs/build.log" >&2
  exit 1
fi

status=0
timeout 1200 "$UNITY" -batchmode -nographics -projectPath unity -runTests -testPlatform EditMode -testResults Logs/editmode.xml -logFile unity/Logs/tests.log || status=$?
echo "editmode exit $status"; grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' unity/Logs/editmode.xml | head -1 || true
rm -f unity/Logs/playmode.xml
if [ "${1:-}" = "play" ]; then
  status=0
  timeout 3000 "$UNITY" -batchmode -projectPath unity -runTests -testPlatform PlayMode -testResults Logs/playmode.xml -logFile unity/Logs/tests_play.log || status=$?
  echo "playmode exit $status"; grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' unity/Logs/playmode.xml | head -1 || true
  grep -E "\[CampaignLevelsTests\]|hold from start" unity/Logs/tests_play.log | head -40 || true
fi
python3 - <<'PY'
import re
for f in ['unity/Logs/editmode.xml', 'unity/Logs/playmode.xml']:
    try:
        s = open(f).read()
    except OSError:
        continue
    for m in re.finditer(r'<test-case [^>]*fullname="([^"]*)"[^>]*result="Failed".*?<message><!\[CDATA\[(.*?)\]\]>', s, re.S):
        print('FAIL', m.group(1), m.group(2)[:400])
PY
