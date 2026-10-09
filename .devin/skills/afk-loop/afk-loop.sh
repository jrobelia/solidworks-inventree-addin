#!/usr/bin/env bash
set -euo pipefail
script="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/scripts/driver.py"
if command -v cygpath >/dev/null 2>&1; then script="$(cygpath -w "$script")"; fi
exec "${AFK_PYTHON:-python}" "$script" "$@"
