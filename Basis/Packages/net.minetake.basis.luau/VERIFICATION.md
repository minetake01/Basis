# Manual verification checklist (Phase 0A–7)

## Editor

- [x] Import `net.minetake.luau` + `net.minetake.basis.luau` without asmdef errors
- [ ] Rebuild natives (`Native~/build-libluau.ps1`) and deploy (`Native~/deploy-built.ps1` if DLLs were locked)
- [x] `Native~/test-limits.ps1` prints `ok`
- [x] ClockProp in `Main.unity` enters Play and TMP clock label updates (date/time)
- [x] ClockProp enters Play without `basis_luau_newstate_with_limits failed`
- [x] Play preview keeps `BasisLuauBehaviour`, adds one proxy, then removes the proxy without dirtying the source scene
- [ ] Prop Inspector **Test In Editor** spawns prop and Luau scripts run
- [ ] Attach `BasisLuauBehaviour` + `LuauPropHost`, export prop → `LuauScriptProxy` remains, behaviour removed, `boundHost` set
- [ ] Scene BEE export leaves source scene `BasisLuauBehaviour` intact; bundle contains `LuauScriptProxy`
- [ ] Export without host → build **throws** (fail-fast)
- [ ] Play Mode with invalid Luau setup → conversion exits Play immediately (no partial proxy swap)
- [ ] `LuauScriptProxy` without `boundHost` → runtime fail-fast (no silent no-op)
- [ ] ObjectRotator demo rotates target transform via handle
- [x] ClockProp updates TMP text via `basis_object.setField`
- [x] ClockProp handle slot resolves to its serialized TMP component
- [ ] Scene Luau script with sibling `LuauSceneHost` resolves host and runs under Play Mode

## Native limits (requires patched `libluau` + `basis_luau_limits` per platform)

- [ ] Top-level / module chunk `while true do end` stops within 500ms (protected load + lifecycle)
- [ ] Memory bomb disables entire host state (all proxies)
- [ ] Post-disable lifecycle calls are no-op
- [ ] Multiple proxies: all child `ReleaseThreadState` (unref) then root `Dispose` — no UAF / double-close
- [ ] After timeout on proxy A, proxy B error is **not** misclassified as Timeout (`last_reason` reset per execution)

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

## Build native stack (Windows x64)

```powershell
./Packages/net.minetake.luau/Native~/build-libluau.ps1
./Packages/net.minetake.luau/Native~/deploy-built.ps1   # if Editor had DLLs locked
./Packages/net.minetake.luau/Native~/test-limits.ps1
```

```bash
./Packages/net.minetake.luau/Native~/build-libluau.sh linux-x64
```

## Tests

- [ ] `Minetake.Basis.Luau.Tests` (handle registry + whitelist policy)
- [ ] `HVR.Basis.Comms.Tests` OscBridgeTests (BasisLuauOscHost)
