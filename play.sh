#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_PATH="$ROOT_DIR/Builds/macOS/OrchardOfTheLastSeason.app"
PROJECT_DIR="$ROOT_DIR/OrchardOfTheLastSeason"

usage() {
  cat <<'EOF'
Usage: ./play.sh [options]

Options:
  --build       Rebuild the standalone executable before launching
  --console     Run the executable binary attached to this terminal (macOS)
  --unity       Open the project directly in Unity Editor
  -h, --help    Show this help message
EOF
}

REBUILD=0
CONSOLE=0
OPEN_UNITY=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --build)
      REBUILD=1
      shift
      ;;
    --console)
      CONSOLE=1
      shift
      ;;
    --unity)
      OPEN_UNITY=1
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

if [[ "$OPEN_UNITY" -eq 1 ]]; then
  UNITY_APP="/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app"
  if [[ -d "$UNITY_APP" ]]; then
    open -a "$UNITY_APP" --args -projectPath "$PROJECT_DIR"
  else
    echo "Unity 6000.3.25f1 not found at $UNITY_APP; open OrchardOfTheLastSeason via Unity Hub." >&2
    exit 1
  fi
  exit 0
fi

OS="$(uname -s)"
if [[ "$OS" == "Darwin" ]]; then
  if [[ "$REBUILD" -eq 1 || ! -d "$APP_PATH" ]]; then
    "$ROOT_DIR/build.sh" --target StandaloneOSX
  fi

  if [[ "$CONSOLE" -eq 1 ]]; then
    BIN_PATH="$(find "$APP_PATH/Contents/MacOS" -maxdepth 1 -type f -perm +111 | head -n 1)"
    if [[ -z "$BIN_PATH" ]]; then
      echo "Error: Could not find executable inside $APP_PATH/Contents/MacOS" >&2
      exit 1
    fi
    exec "$BIN_PATH"
  else
    echo "Launching $APP_PATH"
    open "$APP_PATH"
  fi
elif [[ "$OS" == "Linux" ]]; then
  LINUX_BIN="$ROOT_DIR/Builds/Linux/OrchardOfTheLastSeason.x86_64"
  if [[ "$REBUILD" -eq 1 || ! -x "$LINUX_BIN" ]]; then
    "$ROOT_DIR/build.sh" --target StandaloneLinux64
  fi
  exec "$LINUX_BIN"
else
  echo "Unsupported host OS for play.sh: $OS. Use ./build.sh or open OrchardOfTheLastSeason in Unity 6." >&2
  exit 1
fi
