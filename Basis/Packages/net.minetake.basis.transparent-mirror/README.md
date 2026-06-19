# net.minetake.basis.transparent-mirror

VRChat-style world mirror prop for Basis.

Props are user-generated content: custom logic must run through **Cilbox** (`[Cilboxable]` + `CilboxPropBasis`), while mirror rendering uses the approved native `BasisSDKMirror` component.

## Modes (local per player)

Press the mode button to cycle:

1. **Off** — mirror disabled (initial state)
2. **Full** — reflects world geometry and avatars
3. **Transparent** — reflects avatars only with a transparent background

## Installation (VPM)

Add both VPM repositories, then install this package:

| Repository | URL |
|---|---|
| Basis (official) | `https://raw.githubusercontent.com/basisvr/Basis/vpm/vpm-repo.json` |
| Minetake | `https://basis.minetake.net/vpm-repo.json` |

Install `net.minetake.basis.transparent-mirror` via your VPM client (VRChat Creator Companion, ALCOM, etc.). Dependencies resolve from the Basis official repo.

After install, register Addressables entries and build the prop bundle (see below).

## Usage

### Spawn from library

Requires Addressables entries (same pattern as `net.minetake.basis.mediastream`):

- `Transparent Mirror` → prefab
- `TransparentMirrorEmbeddedItemsCatalog` → embedded items catalog asset

Build the prop bundle from the prefab so Cilbox can compile `[Cilboxable]` scripts into `CilboxProxy` before shipping.

The embedded catalog entry is **library-only** (`IsPinned: false`): it appears in the Props library but does not add an Esc menu shortcut. Placement uses raycast mode so scale can be adjusted like a normal prop.

### Place in a world scene

1. Drag `Prefabs/TransparentMirror` into your world scene.
2. Orient the mirror plane so **+Z** points out of the reflective surface.
3. Build the scene with `BasisScene`.

## Package layout

- `Integration/` — cilbox mode controller, embedded-items bootstrap
- `Shader/` — `TransparentMirror.shader`
- `Materials/` — opaque and transparent mirror materials
- `Prefabs/` — `TransparentMirror.prefab` (`BasisSDKMirror` + `CilboxPropBasis`)
- `Settings/` — embedded items catalog
