using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Publishes a camera or render texture to external video apps (Spout2 on Windows, etc.).
/// </summary>
[DisallowMultipleComponent]
public sealed class BasisMediaStreamPublisher : MonoBehaviour
{
    public Camera sourceCamera;
    public RenderTexture sourceTexture;
    public string streamName;
    public BasisMediaStreamBackend backend = BasisMediaStreamBackend.Auto;
    public bool publishOnEnable = false;
    public bool autoCaptureWhenNoTargetTexture = true;

    [Tooltip("When true, LateUpdate does not publish; call PublishFrame() after the source texture is ready (e.g. from endCameraRendering).")]
    public bool deferLateUpdatePublish;

    public bool IsPublishing => _publishing;
    public bool IsSupported => BasisMediaStreamNative.IsSupported;

    private IntPtr _handle;
    private CommandBuffer _renderCmd;
    private static IntPtr _renderEventFunc;
    private bool _publishing;
    private BasisMediaStreamCaptureRegistry.Entry _captureEntry;

    private void Reset()
    {
        sourceCamera = GetComponent<Camera>();
    }

    private void OnEnable()
    {
        if (publishOnEnable)
            StartPublishing();
    }

    private void OnDisable()
    {
        StopPublishing();
    }

    private void OnDestroy()
    {
        StopPublishing();
    }

    private void LateUpdate()
    {
        if (deferLateUpdatePublish) return;
        PublishFrame();
    }

    /// <summary>Sends the current <see cref="ResolveSourceTexture"/> to the active backend (e.g. Spout).</summary>
    public void PublishFrame()
    {
        if (!_publishing || _handle == IntPtr.Zero) return;

        RenderTexture rt = ResolveSourceTexture();
        if (rt == null || !rt.IsCreated()) return;

        IntPtr nativePtr = rt.GetNativeTexturePtr();
        if (nativePtr == IntPtr.Zero) return;

        BasisMediaStreamNative.basis_mediastream_publisher_set_texture(_handle, nativePtr, rt.width, rt.height);

        if (_renderEventFunc == IntPtr.Zero)
            _renderEventFunc = BasisMediaStreamNative.basis_mediastream_get_render_event_func();
        if (_renderEventFunc == IntPtr.Zero) return;

        _renderCmd ??= new CommandBuffer { name = "BasisMediaStream.Publish" };
        _renderCmd.Clear();
        _renderCmd.IssuePluginEventAndData(_renderEventFunc, BasisMediaStreamNative.RenderUpdate, _handle);
        Graphics.ExecuteCommandBuffer(_renderCmd);
    }

    public void StartPublishing()
    {
        if (_publishing) return;
        if (!BasisMediaStreamNative.IsSupported)
        {
            Debug.LogWarning("[BasisMediaStream] No media stream backend available on this platform.");
            return;
        }

        Camera cam = sourceCamera != null ? sourceCamera : GetComponent<Camera>();
        if (sourceTexture == null && cam == null)
        {
            Debug.LogWarning("[BasisMediaStream] No source camera or texture assigned.");
            return;
        }

        if (cam != null && cam.targetTexture == null && autoCaptureWhenNoTargetTexture)
            _captureEntry = BasisMediaStreamCaptureRegistry.Register(cam);

        RenderTexture rt = ResolveSourceTexture();
        int w = rt != null ? rt.width : (cam != null ? cam.pixelWidth : 1);
        int h = rt != null ? rt.height : (cam != null ? cam.pixelHeight : 1);
        string name = string.IsNullOrWhiteSpace(streamName)
            ? $"Basis.Stream.{gameObject.name}"
            : streamName;

        _handle = BasisMediaStreamNative.basis_mediastream_publisher_create((int)backend, name, w, h);
        if (_handle == IntPtr.Zero)
        {
            string detail = BasisMediaStreamNative.TryGetLastError(_handle);
            Debug.LogError(string.IsNullOrEmpty(detail)
                ? $"[BasisMediaStream] Failed to create publisher for stream '{name}'."
                : $"[BasisMediaStream] Failed to create publisher for stream '{name}': {detail}");
            return;
        }

        _publishing = true;
        Debug.Log($"[BasisMediaStream] Publishing '{name}' ({w}x{h}).");
    }

    public void StopPublishing()
    {
        if (!_publishing && _handle == IntPtr.Zero) return;

        Camera cam = sourceCamera != null ? sourceCamera : GetComponent<Camera>();
        if (_captureEntry != null && cam != null)
        {
            BasisMediaStreamCaptureRegistry.Unregister(cam);
            _captureEntry = null;
        }

        if (_handle != IntPtr.Zero)
        {
            BasisMediaStreamNative.basis_mediastream_publisher_destroy(_handle);
            _handle = IntPtr.Zero;
        }

        _publishing = false;
    }

    private RenderTexture ResolveSourceTexture()
    {
        if (sourceTexture != null)
            return sourceTexture;

        Camera cam = sourceCamera != null ? sourceCamera : GetComponent<Camera>();
        if (cam == null) return null;

        if (cam.targetTexture != null)
            return cam.targetTexture;

        if (autoCaptureWhenNoTargetTexture
            && BasisMediaStreamCaptureRegistry.TryGet(cam, out var entry)
            && entry?.OutputRT != null)
            return entry.OutputRT;

        return null;
    }
}
