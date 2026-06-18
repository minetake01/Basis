# net.minetake.luau

Vendored [nuskey8/luau-dotnet](https://github.com/nuskey8/luau-dotnet) for Basis UGC scripting.

## Upstream pin

- Repository: `https://github.com/nuskey8/luau-dotnet`
- Commit: `820559978beca636c871c68a4000082e566dcb31` (v0.1.6)
- Unity package path: `src/Luau.Unity/Assets/Luau.Unity`

## Assembly names (unchanged)

- `Luau`
- `Luau.Native`
- `Luau.Unity`

UPM package name is `net.minetake.luau` only.

## Native execution limits

`Native~/basis_luau_limits.c` provides interrupt (`luaL_error` in native only) and custom `lua_Alloc` hard memory cap.

Rebuild native plugins after changing limits sources:

```powershell
./Native~/build.ps1
```

```bash
./Native~/build.sh
```

Exports are linked into `libluau` (same `ffi_*` surface as luau-dotnet). Until rebuilt, Editor uses the stock prebuilt `libluau` binaries; limits FFI entry points require a native rebuild per platform.

## Platforms

Windows x64, Linux x64/arm64, macOS, iOS arm64/x64, Android arm64/x64.

WebGL is **not** supported (`#error` in `Runtime/PlatformGuard.cs`).

## Dependencies

Unity projects also need `System.Runtime.CompilerServices.Unsafe` and `System.Text.Json` (see upstream luau-dotnet README).
