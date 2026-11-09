#!/usr/bin/env bash
# setup.sh — run once per clone. Points git at the versioned hooks in scripts/hooks/.
#
# Usage: bash scripts/setup.sh

set -euo pipefail
cd "$( dirname "$0" )/.."

git config core.hooksPath scripts/hooks
chmod +x scripts/hooks/* 2> /dev/null || true

printf 'core.hooksPath = %s\n' "$( git config core.hooksPath )"
