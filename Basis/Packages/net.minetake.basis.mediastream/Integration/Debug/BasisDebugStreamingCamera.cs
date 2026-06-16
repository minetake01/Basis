using Basis.Scripts.BasisSdk.Interactions;
using Basis.Scripts.Drivers;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Basis.MediaStream
{
    /// <summary>
    /// Minimal debug camera for Media Stream (Spout) output.
    /// Streams on spawn; press interact while held to toggle prop visibility.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BasisPickupInteractable))]
    [DefaultExecutionOrder(-100)]
    public sealed class BasisDebugStreamingCamera : MonoBehaviour
    {
        private const string CaptureCameraName = "CaptureCamera";
        private const string BodyName = "Body";

        [Header("Capture")]
        [Tooltip("Render texture width for capture and stream output.")]
        public int captureWidth = 1920;

        [Tooltip("Render texture height for capture and stream output.")]
        public int captureHeight = 1080;

        [Tooltip("Field of view for the capture camera.")]
        public float fieldOfView = 60f;

        [Header("Stream")]
        [Tooltip("Hide the prop body mesh. Toggle with interact while held.")]
        public bool hidePropWhileStreaming;

        [Tooltip("Prefix for the Spout stream name. Instance id is appended.")]
        public string streamNamePrefix = "Basis.DebugCamera";

        public Camera CaptureCamera { get; private set; }

        private static int _nextInstanceId;

        private BasisPickupInteractable _pickup;
        private BasisMediaStreamPublisher _publisher;
        private RenderTexture _captureRt;
        private RenderTexture _streamRt;
        private MeshRenderer _bodyRenderer;
        private int _instanceId;
        private bool _streaming;

        private void Awake()
        {
            _instanceId = _nextInstanceId++;
            _pickup = GetComponent<BasisPickupInteractable>();
            EnsurePhysics();
            BuildVisuals();
            BuildCaptureCamera();
        }

        private void Start()
        {
            _pickup.OnPickupUse.AddListener(OnPickupUseWhileHeld);
            StartStreaming();
        }

        private void LateUpdate()
        {
            if (_streaming && CaptureCamera != null)
                CaptureCamera.enabled = true;
        }

        private void OnDestroy()
        {
            if (_pickup != null)
                _pickup.OnPickupUse.RemoveListener(OnPickupUseWhileHeld);

            ShutdownStream();
            ReleaseCaptureResources();
            BasisCullingCameraRegistry.Unregister(CaptureCamera);
        }

        private void EnsurePhysics()
        {
            if (!TryGetComponent(out Rigidbody body))
            {
                body = gameObject.AddComponent<Rigidbody>();
                body.mass = 0.5f;
                body.linearDamping = 1f;
                body.angularDamping = 2f;
            }

            if (!TryGetComponent(out BoxCollider collider))
            {
                collider = gameObject.AddComponent<BoxCollider>();
                collider.size = new Vector3(0.18f, 0.12f, 0.08f);
                collider.center = new Vector3(0f, 0f, 0.02f);
            }

            _pickup.RigidRef = body;
            _pickup.AutoHold = BasisPickupInteractable.BasisAutoHold.Yes;
            _pickup.LerpToHandOnPickup = true;
            _pickup.InteractRange = 0.25f;
            _pickup.AllowDirectGrab = true;
        }

        private void BuildVisuals()
        {
            Transform existing = transform.Find(BodyName);
            GameObject bodyGo = existing != null ? existing.gameObject : new GameObject(BodyName);
            if (existing == null)
                bodyGo.transform.SetParent(transform, false);

            bodyGo.transform.localPosition = Vector3.zero;
            bodyGo.transform.localRotation = Quaternion.identity;
            bodyGo.transform.localScale = new Vector3(0.16f, 0.1f, 0.06f);

            if (!bodyGo.TryGetComponent(out MeshFilter filter))
                filter = bodyGo.AddComponent<MeshFilter>();
            filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

            if (!bodyGo.TryGetComponent(out _bodyRenderer))
                _bodyRenderer = bodyGo.AddComponent<MeshRenderer>();

            _bodyRenderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                color = new Color(0.2f, 0.22f, 0.25f, 1f)
            };
        }

        private void BuildCaptureCamera()
        {
            Transform existing = transform.Find(CaptureCameraName);
            GameObject cameraGo = existing != null ? existing.gameObject : new GameObject(CaptureCameraName);
            if (existing == null)
                cameraGo.transform.SetParent(transform, false);

            cameraGo.transform.localPosition = new Vector3(0f, 0.02f, 0.06f);
            cameraGo.transform.localRotation = Quaternion.identity;

            CaptureCamera = cameraGo.GetComponent<Camera>();
            if (CaptureCamera == null)
                CaptureCamera = cameraGo.AddComponent<Camera>();

            if (!cameraGo.TryGetComponent(out UniversalAdditionalCameraData urpData))
                urpData = cameraGo.AddComponent<UniversalAdditionalCameraData>();
            urpData.renderType = CameraRenderType.Base;
            urpData.SetRenderer(1);

            CaptureCamera.fieldOfView = fieldOfView;
            CaptureCamera.nearClipPlane = 0.05f;
            CaptureCamera.farClipPlane = 500f;
            CaptureCamera.forceIntoRenderTexture = true;
            CaptureCamera.allowHDR = true;
            CaptureCamera.allowMSAA = true;
            CaptureCamera.useOcclusionCulling = true;
            CaptureCamera.targetDisplay = 1;
            SyncBackgroundFromMainCamera();

            EnsureCaptureRenderTexture();
            CaptureCamera.targetTexture = _captureRt;
            BasisCullingCameraRegistry.Register(CaptureCamera);
        }

        private void OnPickupUseWhileHeld(BasisPickUpUseMode mode)
        {
            if (mode != BasisPickUpUseMode.OnPickUpUseDown)
                return;
            if (!_pickup.Inputs.AnyInteracting())
                return;

            hidePropWhileStreaming = !hidePropWhileStreaming;
            ApplyPropVisibility();
        }

        private void SyncBackgroundFromMainCamera()
        {
            if (BasisLocalCameraDriver.Instance == null || CaptureCamera == null)
                return;

            Camera main = BasisLocalCameraDriver.Instance.Camera;
            if (main == null)
                return;

            CaptureCamera.clearFlags = main.clearFlags;
            CaptureCamera.backgroundColor = main.backgroundColor;
            CaptureCamera.cullingMask = main.cullingMask;

            bool hasMainSky = main.TryGetComponent(out Skybox mainSky) && mainSky.material != null;
            bool hasCapSky = CaptureCamera.TryGetComponent(out Skybox capSky);
            if (hasMainSky)
            {
                if (!hasCapSky)
                    capSky = CaptureCamera.gameObject.AddComponent<Skybox>();
                capSky.material = mainSky.material;
            }
            else if (hasCapSky)
            {
                capSky.material = null;
            }
        }

        private void EnsureCaptureRenderTexture()
        {
            int w = Mathf.Max(16, captureWidth);
            int h = Mathf.Max(16, captureHeight);

            if (_captureRt != null && _captureRt.IsCreated()
                && _captureRt.width == w && _captureRt.height == h)
                return;

            ReleaseCaptureRenderTexture();

            _captureRt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
            {
                name = $"DebugStreamCapture_{_instanceId}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1,
                useMipMap = false,
            };
            _captureRt.Create();

            if (CaptureCamera != null)
                CaptureCamera.targetTexture = _captureRt;
        }

        private void ReleaseCaptureRenderTexture()
        {
            if (_captureRt == null)
                return;
            _captureRt.Release();
            Destroy(_captureRt);
            _captureRt = null;
        }

        private void ReleaseCaptureResources()
        {
            ReleaseCaptureRenderTexture();
            ReleaseStreamRenderTexture();

            if (_bodyRenderer != null && _bodyRenderer.sharedMaterial != null)
            {
                Destroy(_bodyRenderer.sharedMaterial);
                _bodyRenderer.sharedMaterial = null;
            }
        }

        private void StartStreaming()
        {
            if (_streaming)
                return;

            if (!BasisMediaStream.IsSupported)
            {
                Debug.LogWarning("[net.minetake.basis.mediastream] Media stream is not supported on this platform or graphics API.");
                return;
            }

            EnsureCaptureRenderTexture();
            EnsurePublisher();
            EnsureStreamRenderTexture(captureWidth, captureHeight);

            if (_publisher.IsPublishing)
                _publisher.StopPublishing();

            _publisher.deferLateUpdatePublish = true;
            _publisher.StartPublishing();

            if (!_publisher.IsPublishing)
            {
                Debug.LogError($"[net.minetake.basis.mediastream] Failed to start stream '{_publisher.streamName}'.");
                return;
            }

            SubscribeRendering();
            if (CaptureCamera != null)
                CaptureCamera.enabled = true;

            _streaming = true;
            ApplyPropVisibility();
            Debug.Log($"[net.minetake.basis.mediastream] Stream started: '{_publisher.streamName}'.");
        }

        private void ShutdownStream()
        {
            UnsubscribeRendering();
            ReleaseStreamRenderTexture();

            if (_publisher != null)
            {
                _publisher.sourceTexture = null;
                _publisher.deferLateUpdatePublish = false;
                _publisher.StopPublishing();
            }

            _streaming = false;
        }

        private void EnsurePublisher()
        {
            if (CaptureCamera == null)
                return;

            GameObject captureGo = CaptureCamera.gameObject;
            if (_publisher == null)
            {
                _publisher = captureGo.GetComponent<BasisMediaStreamPublisher>()
                    ?? captureGo.AddComponent<BasisMediaStreamPublisher>();
            }

            _publisher.publishOnEnable = false;
            _publisher.sourceCamera = CaptureCamera;
            _publisher.streamName = $"{streamNamePrefix}.{_instanceId}";
            _publisher.backend = BasisMediaStreamBackend.Auto;
            _publisher.autoCaptureWhenNoTargetTexture = false;
        }

        private void SubscribeRendering()
        {
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        private void UnsubscribeRendering()
        {
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (!_streaming || _publisher == null || !_publisher.IsPublishing)
                return;
            if (cam != CaptureCamera)
                return;

            RenderTexture source = CaptureCamera.targetTexture;
            if (source == null || !source.IsCreated())
                return;

            EnsureStreamRenderTexture(source.width, source.height);
            if (_streamRt == null)
                return;

            BasisMediaStreamGraphics.BlitFlipVertical(source, _streamRt);
            _publisher.sourceTexture = _streamRt;
            _publisher.PublishFrame();
        }

        private void EnsureStreamRenderTexture(int w, int h)
        {
            w = Mathf.Max(1, w);
            h = Mathf.Max(1, h);

            if (_streamRt != null && _streamRt.IsCreated()
                && _streamRt.width == w && _streamRt.height == h
                && _streamRt.format == RenderTextureFormat.ARGB32)
                return;

            ReleaseStreamRenderTexture();

            _streamRt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name = $"DebugStreamSpout_{_instanceId}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1,
                useMipMap = false,
            };
            _streamRt.Create();
        }

        private void ReleaseStreamRenderTexture()
        {
            if (_streamRt == null)
                return;
            _streamRt.Release();
            Destroy(_streamRt);
            _streamRt = null;
        }

        private void ApplyPropVisibility()
        {
            if (_bodyRenderer != null)
                _bodyRenderer.enabled = !hidePropWhileStreaming;
        }
    }
}
