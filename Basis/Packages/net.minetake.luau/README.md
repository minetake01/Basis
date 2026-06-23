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

## Native execution limits + runtime (v6)

Four binaries on Windows x64 Editor:

| Binary | Role |
|--------|------|
| `libluau.dll` | Patched luau-dotnet FFI |
| `Luau.dll` | Managed bindings |
| `basis_luau_limits.dll` | Legacy limits-only DLL |
| `basis_luau_runtime.dll` | Unified runtime: limits + bytecode verifier + command ring + RCU snapshot + buffer pool + worker scheduler |

Build runtime (includes limits):

```powershell
./Native~/build-runtime.ps1
./Native~/test-runtime.ps1
```

`basis_luau_close_state` and C# raw pointer ownership are **not** used. Limits context is freed automatically when the root VM is closed via `LuauState.Dispose()`.

### Windows x64 (Editor)

Prerequisites: Visual Studio 2022 C++ workload, Rust (`x86_64-pc-windows-msvc`), CMake (VS component or PATH), libclang (LLVM / `scoop install llvm`), .NET 9 SDK (or `~/.dotnet/sdk9` via [dotnet-install](https://dot.net/v1/dotnet-install.ps1)).

```powershell
./Native~/build-libluau.ps1
```

Build artifacts use short paths (`C:\lb\luau`, `C:\lb\cargo-target`) to avoid Windows MAX_PATH issues during CMake.

If Unity/Cursor locks plugin DLLs, outputs are written as `*.dll.built`. Deploy when unlocked (copies `libluau`, `luau`, and `basis_luau_limits` — limits links against `luau.dll` at load time):

```powershell
./Native~/deploy-built.ps1
```

Limits-only rebuild (after libluau is already built):

```powershell
./Native~/build.ps1
```

Smoke test:

```powershell
./Native~/test-limits.ps1
```

### Linux / macOS (CI matrix)

```bash
./Native~/build-libluau.sh linux-x64
./Native~/build-libluau.sh linux-arm64
./Native~/build-libluau.sh osx
```

## Platforms

Windows x64, Linux x64/arm64, macOS, iOS arm64/x64, Android arm64/x64.

WebGL is **not** supported (`#error` in `Runtime/PlatformGuard.cs`).

## Dependencies

Unity projects need `System.Runtime.CompilerServices.Unsafe` and `System.Text.Json` (see upstream luau-dotnet README).
This package vendors the Windows x64 Player managed dependency set under `Runtime/Dependencies` so IL2CPP builds can link `Luau.dll` without relying on Editor-only package dependencies.
