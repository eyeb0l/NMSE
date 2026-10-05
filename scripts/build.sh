#!/usr/bin/env bash
# Unix entry point; all build logic lives in build.py.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec python3 "$SCRIPT_DIR/build.py" "$@"
