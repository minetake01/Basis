#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
PACKAGE_ROOT="$(cd "$ROOT/.." && pwd)"
PATCHES_DIR="$ROOT/patches"
PLUGINS_DIR="$PACKAGE_ROOT/Native/Plugins"
WORK_ROOT="${BASIS_LUAU_BUILD_ROOT:-${LOCALAPPDATA:-/tmp}/basis-luau-build}"
REPO_DIR="$WORK_ROOT/luau-dotnet"
PIN="820559978beca636c871c68a4000082e566dcb31"

usage() {
  echo "Usage: $0 <linux-x64|linux-arm64|osx>"
  exit 1
}

TARGET_PLATFORM="${1:-}"
case "$TARGET_PLATFORM" in
  linux-x64) CARGO_TARGET="x86_64-unknown-linux-gnu"; OUT_DIR="$PLUGINS_DIR/linux-x64" ;;
  linux-arm64) CARGO_TARGET="aarch64-unknown-linux-gnu"; OUT_DIR="$PLUGINS_DIR/linux-arm64" ;;
  osx) CARGO_TARGET="$(rustc -vV | awk '/host:/ {print $2}')"; OUT_DIR="$PLUGINS_DIR/osx" ;;
  *) usage ;;
esac

mkdir -p "$WORK_ROOT" "$OUT_DIR"

if [[ ! -d "$REPO_DIR/.git" ]]; then
  git clone --filter=blob:none --no-checkout https://github.com/nuskey8/luau-dotnet.git "$REPO_DIR"
  (cd "$REPO_DIR" && git checkout "$PIN" && git submodule update --init --recursive)
fi

FFI_DIR="$REPO_DIR/native/luau-ffi"
cp "$PATCHES_DIR/luau_ffi_extras.c" "$FFI_DIR/src/luau_ffi_extras.c"
cp "$PATCHES_DIR/luau_ffi_anchor.rs" "$FFI_DIR/src/luau_ffi_anchor.rs"

python3 - <<'PY' "$FFI_DIR/src/lib.rs"
import pathlib, sys
path = pathlib.Path(sys.argv[1])
text = path.read_text()
if "mod luau_ffi_anchor" not in text:
    text = text.replace("mod luau_ffi;", "mod luau_ffi;\nmod luau_ffi_anchor;")
    path.write_text(text)
PY

python3 - <<'PY' "$FFI_DIR/build.rs"
import pathlib, sys
path = pathlib.Path(sys.argv[1])
text = path.read_text()
if "luau_ffi_extras.c" not in text:
    inject = '''
    cc::Build::new()
        .file("src/luau_ffi_extras.c")
        .include("../../luau/VM/include")
        .compile("luau_ffi_extras");
'''
    text = text.replace("fn main() {", "fn main() {" + inject)
    path.write_text(text)
PY

python3 - <<'PY' "$REPO_DIR/src/Luau/LuauState.cs"
import pathlib, sys
path = pathlib.Path(sys.argv[1])
text = path.read_text()
text = text.replace(
    "if (from != null) lua_close(l);\n        else lua_unref(l, reference);",
    "if (from != null) lua_unref(from.AsPointer(), reference);\n        else lua_close(l);",
)
path.write_text(text)
PY

(cd "$FFI_DIR" && cargo build --release --target "$CARGO_TARGET")
ARTIFACT_DIR="$FFI_DIR/target/$CARGO_TARGET/release"

if [[ -f "$ARTIFACT_DIR/libluau.so" ]]; then
  cp "$ARTIFACT_DIR/libluau.so" "$OUT_DIR/libluau.so"
elif [[ -f "$ARTIFACT_DIR/luau.so" ]]; then
  cp "$ARTIFACT_DIR/luau.so" "$OUT_DIR/libluau.so"
  cp "$ARTIFACT_DIR/luau.so" "$OUT_DIR/luau.so"
elif [[ -f "$ARTIFACT_DIR/libluau.dylib" ]]; then
  cp "$ARTIFACT_DIR/libluau.dylib" "$OUT_DIR/libluau.dylib"
else
  echo "libluau artifact missing" >&2
  exit 1
fi

(cd "$REPO_DIR/src/Luau" && dotnet build -c Release)
LUau_DLL="$(find "$REPO_DIR/src/Luau/bin" -name Luau.dll | head -n 1)"
cp "$LUau_DLL" "$PACKAGE_ROOT/Runtime/Luau.dll"

export BASIS_LUAU_INCLUDE="$REPO_DIR/luau/VM/include"
cc -shared -fPIC -O2 -DBASIS_LUAU_LIMITS_EXPORT \
  -I"$BASIS_LUAU_INCLUDE" \
  "$ROOT/basis_luau_limits.c" \
  -L"$ARTIFACT_DIR" -lluau \
  -o "$OUT_DIR/libbasis_luau_limits.so"

echo "Built $OUT_DIR"
