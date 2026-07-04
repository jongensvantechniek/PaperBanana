#!/usr/bin/env bash
# Start the PaperBanana matplotlib plot-execution sidecar.
set -euo pipefail
cd "$(dirname "$0")"
export PORT="${PORT:-8500}"
exec python3 plot_service.py
