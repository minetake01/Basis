# net.minetake.basis.transparent-mirror

VRChat-style world mirror prop for Basis.

## Modes (local per player)

Press the mode button to cycle:

1. **Off** — mirror disabled (initial state)
2. **Full** — reflects world geometry and avatars
3. **Transparent** — reflects avatars only with a transparent background

## Usage

### Place in a world scene

1. Drag `Prefabs/TransparentMirror` into your world scene.
2. Orient the mirror plane so **+Z** points out of the reflective surface.
3. Build the scene with `BasisScene`.

### Spawn from library

Requires Addressables entries (same pattern as `net.minetake.basis.mediastream`):

- `Transparent Mirror` → prefab
- `TransparentMirrorEmbeddedItemsCatalog` → embedded items catalog asset

## Package layout

- `Integration/` — mirror runtime, mode controller, interactable button, embedded-items bootstrap
- `Shader/` — `TransparentMirror.shader`
- `Materials/` — opaque and transparent mirror materials
- `Prefabs/` — `TransparentMirror.prefab`
- `Settings/` — embedded items catalog
