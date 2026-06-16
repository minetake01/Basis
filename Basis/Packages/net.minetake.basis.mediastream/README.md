# net.minetake.basis.mediastream

Experimental package that publishes Unity camera render textures to external video applications (OBS, Resolume, etc.).

`com.basis.mediaplayer` receives live video **into** Unity; this package sends rendered frames **out**.

Official integration with `com.basis.framework` / `com.basis.sdk` (for example wiring into the default Photo Camera prefab) is intentionally deferred to upstream contributors. This package ships its own **Streaming Camera** prop for experiments.

## v1 backends

| Backend | Platform | Status |
|---------|----------|--------|
| Spout2 | Windows D3D11 | Implemented |
| Syphon | macOS | Planned |
| PipeWire | Linux | Planned |

## Generic usage

Attach `BasisMediaStreamPublisher` to any GameObject with a `Camera`, or assign a `RenderTexture` directly:

```csharp
var publisher = gameObject.AddComponent<BasisMediaStreamPublisher>();
publisher.sourceCamera = myCamera;
publisher.streamName = "Basis.MyCamera";
publisher.backend = BasisMediaStreamBackend.Auto;
publisher.enabled = true;
```

For cameras that render to the screen (`targetTexture == null`), enable `autoCaptureWhenNoTargetTexture` and add `BasisMediaStreamCaptureFeature` to the camera's URP renderer.

## Streaming Camera prop (experimental)

When `com.basis.framework` is present, spawn **Minetake Streaming Camera** from the library (pinned embedded prop).

The prefab is a minimal debug camera (no SDK photo camera):

- `BasisDebugStreamingCamera` — capture camera, auto Spout output on spawn
- `BasisPickupInteractable` — grab and hold; press interact while held to toggle hide prop
- `StreamingCameraPropUI` — marker component

Stream names look like `Basis.DebugCamera.0`. Spout output starts automatically when the prop spawns.

## Package layout

| Assembly | Depends on | Contents |
|----------|------------|----------|
| `BasisMediaStream` | URP only | Publisher, native plugin, URP capture feature |
| `BasisMediaStream.Integration` | Framework | Debug streaming camera prop, embedded catalog merge (`Integration/`)

## Building the native plugin (Windows)

```powershell
cd Packages/net.minetake.basis.mediastream/Native~
.\build-win-x64.ps1 -UnityPluginApiDir "C:\Program Files\Unity\Hub\Editor\<ver>\Editor\Data\PluginAPI"
```

Output: `Plugins/Windows/x86_64/basis_mediastream_native.dll`

## OBS

Add a **Spout2** source and select the stream name (for example `Basis.DebugCamera.0` from a streaming camera instance).
