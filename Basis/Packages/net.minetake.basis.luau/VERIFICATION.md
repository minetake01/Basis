# Manual verification checklist (Phase 0A–7)

## Editor

- [ ] Import `net.minetake.luau` + `net.minetake.basis.luau` without asmdef errors
- [ ] `LuauState.Create()` / `DoString` smoke test
- [ ] Attach `BasisLuauBehaviour` + `LuauPropHost`, export prop → `LuauScriptProxy` remains, behaviour removed
- [ ] Export without host → build **throws** (fail-fast)
- [ ] ObjectRotator demo rotates target transform via handle
- [ ] TmpLabel updates TMP text via `basis_object.setField`
- [ ] Handle slots resolve after export (serialized `slotObjects` → runtime handles)

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

## Whitelist / bindings

- [ ] `Minetake.Basis.Luau.Tests` policy tests pass (TMPro, UI, Physics, NavMesh, blocked APIs)
- [ ] Prop: AudioSource play via `basis_object.call`
- [ ] Scene: `Physics.Raycast` via `basis_object.callStatic`
- [ ] Scene: `NavMesh.SamplePosition` via `basis_object.callStatic`
- [ ] Avatar: `basis_avatar.resolveAvatar` + face parameter fields
- [ ] OSC subscribe/publish (`OscNetworkEcho.luau`)
- [ ] Network `basis_network.onMessage` + `sendBytes`
- [ ] ContentPolice-sanitized `basis_instantiate.clone`
- [ ] `onTriggerEnter` receives collider handle

## Build native limits

```powershell
./Packages/net.minetake.luau/Native~/build.ps1   # Windows x64
```

```bash
./Packages/net.minetake.luau/Native~/build.sh    # Linux x64
```

## Tests

- [ ] `Minetake.Basis.Luau.Tests` (handle registry + whitelist policy)
- [ ] `HVR.Basis.Comms.Tests` OscBridgeTests (BasisLuauOscHost)
