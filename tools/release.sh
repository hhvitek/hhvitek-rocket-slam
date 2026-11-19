#!/usr/bin/env bash
# Package the Linux and Windows builds and publish them as a GitHub Release.
#
# Usage:
#   tools/release.sh [VERSION] [--build] [--dry-run]
#
#   VERSION    X.Y.Z (no "v"). Default: 1.0.0 for the first release, then the latest
#              vX.Y.Z tag with the patch number bumped (1.0.0 -> 1.0.1 -> ...).
#   --build    rebuild both players with Unity first (BuildScript.BuildLinux / BuildWindows).
#              Close the Unity editor before using it (one Unity process per project).
#   --dry-run  build the zips and print what would happen; no tag, no push, no API calls.
#
# Needs a GitHub token in GITHUB_TOKEN (or GH_TOKEN) with write access to the repository
# contents (fine-grained token: "Contents: Read and write" on this repo). The token is read
# from the environment only and never written anywhere.
#
# Output zips: builds/release/RocketSlam-<version>-linux.zip and -windows.zip (git-ignored).
set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
UNITY=${UNITY:-"$HOME/Unity/Hub/Editor/6000.6.9f1/Editor/Unity"}
BUILDS="$ROOT/builds"
OUT="$BUILDS/release"

VERSION=""
DO_BUILD=0
DRY_RUN=0

die() { echo "release: $*" >&2; exit 1; }

for arg in "$@"; do
  case "$arg" in
    --build) DO_BUILD=1 ;;
    --dry-run) DRY_RUN=1 ;;
    -h|--help) sed -n '2,19p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    [0-9]*.[0-9]*.[0-9]*) VERSION="$arg" ;;
    *) die "unknown argument '$arg' (see --help)" ;;
  esac
done

for cmd in git zip curl python3 sha256sum; do
  command -v "$cmd" >/dev/null || die "missing command: $cmd"
done

cd "$ROOT"

# --- repository and version -------------------------------------------------------------
REMOTE_URL=$(git remote get-url origin) || die "no 'origin' remote"
SLUG=$(printf '%s' "$REMOTE_URL" | sed -E 's#^(git@github\.com:|https://github\.com/)##; s#\.git$##')
[[ "$SLUG" =~ ^[^/]+/[^/]+$ ]] || die "origin is not a GitHub repository: $REMOTE_URL"

git fetch --quiet --tags origin

if [[ -z "$VERSION" ]]; then
  LATEST=$(git tag -l 'v[0-9]*.[0-9]*.[0-9]*' --sort=-v:refname | head -n1)
  if [[ -z "$LATEST" ]]; then
    VERSION="1.0.0"
  else
    IFS=. read -r MAJ MIN PAT <<<"${LATEST#v}"
    VERSION="$MAJ.$MIN.$((PAT + 1))"
  fi
fi
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "version must be X.Y.Z, got '$VERSION'"
TAG="v$VERSION"
git rev-parse -q --verify "refs/tags/$TAG" >/dev/null && die "tag $TAG already exists"

BRANCH=$(git rev-parse --abbrev-ref HEAD)
if [[ $DRY_RUN -eq 0 ]]; then
  TOKEN=${GITHUB_TOKEN:-${GH_TOKEN:-}}
  [[ -n "$TOKEN" ]] || die "set GITHUB_TOKEN (or GH_TOKEN) to a token with Contents: write"
  [[ -z "$(git status --porcelain)" ]] || die "working tree is not clean; commit or stash first"
  git rev-parse -q --verify "origin/$BRANCH" >/dev/null || die "branch $BRANCH is not on origin"
  [[ "$(git rev-parse HEAD)" == "$(git rev-parse "origin/$BRANCH")" ]] \
    || die "HEAD is not pushed to origin/$BRANCH; push first"
fi

echo "release: $SLUG $TAG from $BRANCH @ $(git rev-parse --short HEAD)$([[ $DRY_RUN -eq 1 ]] && echo ' (dry run)')"

# --- optional rebuild ---------------------------------------------------------------------
if [[ $DO_BUILD -eq 1 ]]; then
  [[ -x "$UNITY" ]] || die "Unity not found at $UNITY (set UNITY=...)"
  mkdir -p "$ROOT/unity/Logs"
  for method in BuildLinux BuildWindows; do
    echo "release: building $method ..."
    "$UNITY" -batchmode -projectPath "$ROOT/unity" -executeMethod "BuildScript.$method" -quit \
      -logFile "$ROOT/unity/Logs/release_$method.log" \
      || die "Unity $method failed, see unity/Logs/release_$method.log"
  done
fi

[[ -x "$BUILDS/Linux/RocketSlam.x86_64" ]] || die "missing builds/Linux/RocketSlam.x86_64 (use --build)"
[[ -f "$BUILDS/Windows/RocketSlam.exe" ]] || die "missing builds/Windows/RocketSlam.exe (use --build)"

# --- package ------------------------------------------------------------------------------
mkdir -p "$OUT"
STAGE=$(mktemp -d "$OUT/stage.XXXXXX")
trap 'rm -rf "$STAGE"' EXIT

package() { # <platform> <source dir>
  local platform=$1 src=$2 name="RocketSlam-$VERSION-$1"
  local zip="$OUT/$name.zip"
  mkdir -p "$STAGE/$name"
  cp -al "$src"/. "$STAGE/$name"/   # hard links: fast, no extra disk space
  # never ship Unity's debug side folders, logs or working screenshots
  find "$STAGE/$name" -maxdepth 1 \( -name '*_BackUpThisFolder_ButDontShipItWithYourGame' \
    -o -name '*_BurstDebugInformation_DoNotShip' -o -name '*.log' -o -name 'screenshots' \) \
    -exec rm -rf {} +
  rm -f "$zip"
  (cd "$STAGE" && zip -qr -9 "$zip" "$name")
  unzip -tq "$zip" >/dev/null || die "zip test failed: $zip"
  echo "release: $(du -h "$zip" | cut -f1)  $zip" >&2
  echo "$zip"
}

LINUX_ZIP=$(package linux "$BUILDS/Linux")
WINDOWS_ZIP=$(package windows "$BUILDS/Windows")
SUMS=$(cd "$OUT" && sha256sum "$(basename "$LINUX_ZIP")" "$(basename "$WINDOWS_ZIP")")

NOTES=$(cat <<EOF
Rocket Slam $VERSION

Downloads:
- Linux (x86_64): $(basename "$LINUX_ZIP") - unzip, then run RocketSlam-$VERSION-linux/RocketSlam.x86_64
- Windows (64-bit): $(basename "$WINDOWS_ZIP") - unzip, then run RocketSlam-$VERSION-windows/RocketSlam.exe

How to play: hold SPACE (gamepad A / right trigger) to fire the engine when the screen says FIRE NOW.
See README.md for controls, levels and scoring.

SHA-256:
\`\`\`
$SUMS
\`\`\`
EOF
)

if [[ $DRY_RUN -eq 1 ]]; then
  echo "release: dry run - would tag $TAG at $(git rev-parse --short HEAD), push it,"
  echo "         create the release on $SLUG and upload the two zips. Notes:"
  printf '%s\n' "$NOTES" | sed 's/^/    /'
  exit 0
fi

# --- tag, release, upload -----------------------------------------------------------------
api() { # <method> <url> [curl args...] -- prints the response body, fails on HTTP >= 400
  local method=$1 url=$2; shift 2
  curl -sS --fail-with-body -X "$method" "$url" \
    -H @<(printf 'Authorization: Bearer %s\n' "$TOKEN") \
    -H "Accept: application/vnd.github+json" \
    -H "X-GitHub-Api-Version: 2022-11-28" "$@"
}

git tag -a "$TAG" -m "Rocket Slam $VERSION"
git push --quiet origin "refs/tags/$TAG" || { git tag -d "$TAG" >/dev/null; die "pushing tag $TAG failed"; }
echo "release: pushed tag $TAG"

PAYLOAD=$(TAG="$TAG" NAME="Rocket Slam $VERSION" BODY="$NOTES" python3 -c \
  'import json, os; print(json.dumps({"tag_name": os.environ["TAG"], "name": os.environ["NAME"],
   "body": os.environ["BODY"], "draft": False, "prerelease": False}))')

if ! RESPONSE=$(api POST "https://api.github.com/repos/$SLUG/releases" --data "$PAYLOAD"); then
  echo "$RESPONSE" >&2
  die "creating the release failed; tag $TAG is pushed (delete with: git push origin :refs/tags/$TAG && git tag -d $TAG)"
fi
read -r RELEASE_ID RELEASE_URL < <(printf '%s' "$RESPONSE" | python3 -c \
  'import json, sys; r = json.load(sys.stdin); print(r["id"], r["html_url"])')

for zip in "$LINUX_ZIP" "$WINDOWS_ZIP"; do
  echo "release: uploading $(basename "$zip") ..."
  api POST "https://uploads.github.com/repos/$SLUG/releases/$RELEASE_ID/assets?name=$(basename "$zip")" \
    -H "Content-Type: application/zip" --data-binary @"$zip" >/dev/null \
    || die "upload of $(basename "$zip") failed; finish it on $RELEASE_URL"
done

echo "release: done -> $RELEASE_URL"
