#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
OUT="$ROOT/../Native/Plugins/linux-x64"
mkdir -p "$OUT"
cc -shared -fPIC -O2 -DBASIS_LUAU_LIMITS_EXPORT \
  "$ROOT/basis_luau_limits.c" \
  -o "$OUT/libbasis_luau_limits.so"
echo "Built $OUT/libbasis_luau_limits.so"
