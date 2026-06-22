# Manual verification checklist (v6 runtime)

## Build / native

- [ ] `Native~/build-runtime.ps1` succeeds
- [ ] `Native~/test-runtime.ps1` succeeds
- [ ] `basis_luau_runtime.dll` deployed to `Native/Plugins/win-x64`

## Unity compile

- [ ] No compile errors in `Minetake.Basis.Luau` + Editor asmdefs
- [ ] Unity console: zero compile errors after domain reload

## Automated tests (Edit Mode)

Run from Unity **Test Runner** (Edit Mode). Do not block the Editor main thread waiting on `TestRunnerApi` callbacks.

- [ ] `Minetake.Basis.Luau.Tests` — authority, command catalog, command flush, proxy generation, runtime security
- [ ] `LuauRuntimeSecurityTests`

## Automated tests (Play Mode)

- [ ] `LuauProxyLifecycleTests.StandardLibrariesAndNativeUpdateAreReadyWhenLoadCompletes`
- [ ] `LuauProxyLifecycleTests.ModuleInitializationFailurePreservesItsDiagnostic`

## Architecture (v6)

- [ ] One `lua_State*` per host via `basis_luau_runtime_root_state` (no dual VM)
- [ ] Worker scheduler runs `update` / `fixedUpdate` / `lateUpdate` (no main-thread lifecycle pump)
- [ ] `transform.rotate` deferred via command ring (ObjectRotator sample)
- [ ] `basis_ui.setText` via `SetUiText` command (TmpLabel sample)
- [ ] Physics events via `LuauEventIngress` → event ring → worker proxy dispatch
- [ ] Stale commands rejected after proxy disable (`proxy_generation`)
- [ ] No `basis_object`, `TransformBindings`, or whitelist policy sources in repo

## Samples

- [ ] `TmpLabel.luau` — `basis_ui.setText` + `basis_time`
- [ ] `ObjectRotator.luau` — `transform.rotate`
- [ ] `OscEcho.luau` / `OscNetworkEcho.luau` — `basis_osc.publishFloat` + subscribe

## Play Mode smoke

- [ ] Enter Play → 3 frames → exit with zero console errors

## IL2CPP (manual matrix)

- [ ] Windows / Linux / macOS Standalone IL2CPP
- [ ] Android arm64 / iOS arm64
- [ ] WebGL surfaces `#error` from `PlatformGuard.cs`
