# Manual verification checklist (Phase 0A–6)

## Editor

- [ ] Import `net.minetake.luau` + `net.minetake.basis.luau` without asmdef errors
- [ ] `LuauState.Create()` / `DoString` smoke test
- [ ] Attach `BasisLuauBehaviour` + `LuauPropHost`, export prop → `LuauScriptProxy` remains, behaviour removed
- [ ] Export without host → build **throws** (fail-fast)
- [ ] ObjectRotator demo rotates target transform via handle

## Native limits (requires `basis_luau_limits` built per platform)

- [ ] `while true do end` stops within 500ms, protected call returns, main thread alive
- [ ] Memory bomb disables entire host state (all proxies)
- [ ] Post-disable lifecycle calls are no-op

## IL2CPP platforms

- [ ] Windows x64 Standalone IL2CPP
- [ ] Linux x64 Standalone IL2CPP
- [ ] macOS Standalone IL2CPP
- [ ] Android arm64 IL2CPP (`libbasis_luau_limits.so` + `libluau.so`)
- [ ] iOS arm64 IL2CPP (static libs linked)
- [ ] WebGL build surfaces `#error` from `PlatformGuard.cs`

## Build native limits

```powershell
./Packages/net.minetake.luau/Native~/build.ps1   # Windows x64
```

```bash
./Packages/net.minetake.luau/Native~/build.sh    # Linux x64
```

Other platforms: integrate `basis_luau_limits.c` into platform CI (same pattern as luau-dotnet native build).

## Tests

- [ ] `Minetake.Basis.Luau.Tests` (handle registry)
- [ ] `HVR.Basis.Comms.Tests` OscBridgeTests (BasisLuauOscHost)
