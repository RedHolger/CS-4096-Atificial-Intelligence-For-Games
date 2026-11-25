#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$ROOT_DIR/OrchardOfTheLastSeason"
LOG_DIR="$PROJECT_DIR/Logs"
TARGET="StandaloneOSX"
OUT_PATH=""
PREPARE_ONLY=0

usage() {
  cat <<'EOF'
Usage: ./build.sh [options]

Options:
  --target <target>   Build target: macos (default), win64, linux64, webgl
  --out <path>        Custom output path for the built executable/folder
  --prepare-only      Create/update Main.unity and ProjectSettings without building
  -h, --help          Show this help message

Environment:
  UNITY_BIN           Optional explicit path to the Unity Editor binary
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --target)
      TARGET="${2:-}"
      shift 2
      ;;
    --out)
      OUT_PATH="${2:-}"
      shift 2
      ;;
    --prepare-only)
      PREPARE_ONLY=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

find_unity() {
  if [[ -n "${UNITY_BIN:-}" && -x "${UNITY_BIN}" ]]; then
    echo "$UNITY_BIN"
    return 0
  fi

  local os
  os="$(uname -s)"
  if [[ "$os" == "Darwin" ]]; then
    local preferred="/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity"
    if [[ -x "$preferred" ]]; then
      echo "$preferred"
      return 0
    fi
    local candidate
    for candidate in /Applications/Unity/Hub/Editor/6000.*/Unity.app/Contents/MacOS/Unity /Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity; do
      if [[ -x "$candidate" ]]; then
        echo "$candidate"
        return 0
      fi
    done
  elif [[ "$os" == "Linux" ]]; then
    local candidate
    for candidate in "$HOME/Unity/Hub/Editor/6000.3.25f1/Editor/Unity" "$HOME/Unity/Hub/Editor/"*/Editor/Unity /opt/unity/Editor/Unity; do
      if [[ -x "$candidate" ]]; then
        echo "$candidate"
        return 0
      fi
    done
  fi

  if command -v unity-editor >/dev/null 2>&1; then
    command -v unity-editor
    return 0
  fi

  return 1
}

UNITY="$(find_unity || true)"
if [[ -z "$UNITY" ]]; then
  echo "Error: Could not locate Unity 6 Editor (6000.3.25f1)." >&2
  echo "Install Unity 6000.3.25f1 via Unity Hub or set UNITY_BIN=/path/to/Unity." >&2
  exit 1
fi

mkdir -p "$LOG_DIR"

if [[ "$PREPARE_ONLY" -eq 1 ]]; then
  LOG_FILE="$LOG_DIR/prepare_project.log"
  echo "Preparing Unity project with: $UNITY"
  "$UNITY" -batchmode -quit \
    -projectPath "$PROJECT_DIR" \
    -executeMethod Orchard.EditorTools.OrchardBuild.PrepareProject \
    -logFile "$LOG_FILE"
  echo "Project prepared. Log: $LOG_FILE"
  exit 0
fi

LOG_FILE="$LOG_DIR/build_${TARGET}.log"
ARGS=(
  -batchmode
  -projectPath "$PROJECT_DIR"
  -executeMethod Orchard.EditorTools.OrchardBuild.BuildFromCommandLine
  -orchardTarget "$TARGET"
  -logFile "$LOG_FILE"
)

if [[ -n "$OUT_PATH" ]]; then
  ARGS+=(-orchardBuildPath "$OUT_PATH")
fi

echo "Building Orchard of the Last Season ($TARGET) using:"
echo "  $UNITY"
"$UNITY" "${ARGS[@]}"

echo "Build complete. Log: $LOG_FILE"
if [[ "$TARGET" == "StandaloneOSX" || "$TARGET" == "macos" || "$TARGET" == "osx" ]]; then
  APP_PATH="${OUT_PATH:-$ROOT_DIR/Builds/macOS/OrchardOfTheLastSeason.app}"
  echo "Executable: $APP_PATH"
fi
