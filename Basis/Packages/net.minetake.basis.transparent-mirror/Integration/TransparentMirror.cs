using Basis.BasisUI;
using Basis.Scripts.BasisSdk.Helpers;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Drivers;
using System;
using System.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static UnityEngine.Camera;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Basis.TransparentMirror
{
    public class TransparentMirror : MonoBehaviour
    {
        private enum MirrorClearFlags
        {
            FromReferenceCamera = 0,
            Skybox = 1,
            Color = 2,
            Depth = 3,
            Nothing = 4,
        }

        private static readonly int ReflectionTexLeftId = Shader.PropertyToID("_ReflectionTexLeft");
        private static readonly int ReflectionTexRightId = Shader.PropertyToID("_ReflectionTexRight");
        private static readonly int TransparentModeId = Shader.PropertyToID("_TransparentMode");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

		// 2026/06/16 時点でミラー用のカメラを識別することができないため、prefixで識別しています。
        private const string ReflectionCameraPrefix = "TransparentMirrorCam_";
        private const string BasisSdkReflectionCameraPrefix = "MirrorCam_";

        [Header("Main Settings")]
        public Renderer MirrorRenderer;
        public Material OpaqueMaterial;
        public Material TransparentMaterial;
        [SerializeField] private LayerMask fullReflectingLayers;
        [SerializeField] private float clipPlaneOffset = 0.001f;
        [SerializeField] private float nearClipLimit = 0.01f;
        public float FarClipPlane = 25f;
        public int XSize = 2048;
        public int YSize = 2048;
        public int depth = 24;
        public int Antialiasing = 2;

        [Header("Options")]
        public bool allowXRRendering = true;
        public bool RenderPostProcessing = false;
        public bool OcclusionCulling = false;
        public bool renderShadows = false;

        [Header("Debug / Runtime")]
        public bool IsActive;
        public bool IsAbleToRender;
        public static bool InsideRendering;

        [Header("Cameras")]
        public Camera LeftCamera;
        public Camera RightCamera;
        public RenderTexture PortalTextureLeft;
        public RenderTexture PortalTextureRight;

        public Action OnCamerasRendering;
        public Action OnCamerasFinished;
        public Action<TransparentMirrorMode> OnModeChanged;

        public TransparentMirrorMode CurrentMode { get; private set; } = TransparentMirrorMode.Off;

        private LayerMask avatarReflectingLayers;
        private MirrorClearFlags clearFlags = MirrorClearFlags.FromReferenceCamera;
        private Color clearColor = Color.clear;
        private BasisMeshRendererCheck basisMeshRendererCheck;
        private BasisGazeTarget gazeTarget;
        private Vector3 normal;
        private readonly Vector3 projectionDirection = -Vector3.forward;
        private Matrix4x4 xFlip;
        private bool meshVisibleToCamera;
        private Material runtimeOpaqueMaterial;
        private Material runtimeTransparentMaterial;
        private Transform MirrorPlaneTransform => MirrorRenderer != null ? MirrorRenderer.transform : transform;

        private LayerMask ActiveReflectingLayers =>
            CurrentMode == TransparentMirrorMode.Transparent ? avatarReflectingLayers : fullReflectingLayers;

        private void Awake()
        {
            avatarReflectingLayers = BuildAvatarLayerMask();

            if (fullReflectingLayers == 0)
            {
                int remoteLayer = LayerMask.NameToLayer("RemotePlayerAvatar");
                int localLayer = LayerMask.NameToLayer("LocalPlayerAvatar");
                int defaultLayer = LayerMask.NameToLayer("Default");

                if (remoteLayer >= 0 && localLayer >= 0 && defaultLayer >= 0)
                {
                    fullReflectingLayers = (1 << remoteLayer) | (1 << localLayer) | (1 << defaultLayer);
                }
            }
        }

        private void OnEnable()
        {
            IsActive = false;
            IsAbleToRender = false;
            meshVisibleToCamera = false;

            if (MirrorRenderer == null || OpaqueMaterial == null || TransparentMaterial == null)
            {
                Debug.LogError("[TransparentMirror] MirrorRenderer or materials are not assigned.");
                return;
            }

            DisposeRuntimeMaterials();
            runtimeOpaqueMaterial = new Material(OpaqueMaterial);
            runtimeTransparentMaterial = new Material(TransparentMaterial);
            ConfigureMirrorMaterial(runtimeOpaqueMaterial, false);
            ConfigureMirrorMaterial(runtimeTransparentMaterial, true);

            if (basisMeshRendererCheck == null)
            {
                basisMeshRendererCheck = BasisHelpers.GetOrAddComponent<BasisMeshRendererCheck>(MirrorRenderer.gameObject);
            }

            basisMeshRendererCheck.Check += VisibilityFlag;

            BasisDeviceManagement.OnBootModeChanged += BootModeChanged;
            BasisLocalCameraDriver.InstanceExists += OnLocalCameraReady;
            BasisSettingsDefaults.MirrorQuality.OnChanged += OnMirrorQualityChanged;
            BasisSettingsDefaults.UseMirrorQualityOverride.OnChanged += OnMirrorQualityOverrideChanged;

            if (BasisLocalCameraDriver.HasInstance)
            {
                OnLocalCameraReady();
            }

            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
#if UNITY_EDITOR
            EditorApplication.update += RepaintSceneViews;
#endif
            ApplyMode(CurrentMode, force: true);
        }

        private void OnDisable()
        {
            CleanUp();
        }

        private void OnDestroy()
        {
            BasisDeviceManagement.OnBootModeChanged -= BootModeChanged;
            BasisSettingsDefaults.MirrorQuality.OnChanged -= OnMirrorQualityChanged;
            BasisSettingsDefaults.UseMirrorQualityOverride.OnChanged -= OnMirrorQualityOverrideChanged;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
#if UNITY_EDITOR
            EditorApplication.update -= RepaintSceneViews;
#endif

            DisposeRuntimeMaterials();
        }

        public void SetMode(TransparentMirrorMode mode)
        {
            if (CurrentMode == mode)
            {
                return;
            }

            ApplyMode(mode);
        }

        public void CycleMode()
        {
            TransparentMirrorMode next = CurrentMode switch
            {
                TransparentMirrorMode.Off => TransparentMirrorMode.Full,
                TransparentMirrorMode.Full => TransparentMirrorMode.Transparent,
                _ => TransparentMirrorMode.Off,
            };

            ApplyMode(next);
        }

        private void ApplyMode(TransparentMirrorMode mode, bool force = false)
        {
            if (!force && CurrentMode == mode)
            {
                return;
            }

            CurrentMode = mode;

            switch (mode)
            {
                case TransparentMirrorMode.Off:
                    MirrorRenderer.enabled = false;
                    IsAbleToRender = false;
                    if (IsActive)
                    {
                        DisposePortalResources();
                        IsActive = false;
                    }
                    if (gazeTarget != null)
                    {
                        gazeTarget.enabled = false;
                    }
                    break;

                case TransparentMirrorMode.Full:
                    MirrorRenderer.enabled = true;
                    MirrorRenderer.sharedMaterial = runtimeOpaqueMaterial;
                    clearFlags = MirrorClearFlags.FromReferenceCamera;
                    ApplyReflectionLayers(fullReflectingLayers);
                    UpdateCameraClearFlags();
                    ReinitializeMirror();
                    UpdateRenderGate();
                    if (gazeTarget != null)
                    {
                        gazeTarget.enabled = true;
                    }
                    break;

                case TransparentMirrorMode.Transparent:
                    MirrorRenderer.enabled = true;
                    MirrorRenderer.sharedMaterial = runtimeTransparentMaterial;
                    ApplyReflectionLayers(avatarReflectingLayers);
                    clearFlags = MirrorClearFlags.Color;
                    clearColor = Color.clear;
                    UpdateCameraClearFlags();
                    ReinitializeMirror();
                    UpdateRenderGate();
                    if (gazeTarget != null)
                    {
                        gazeTarget.enabled = true;
                    }
                    break;
            }

            OnModeChanged?.Invoke(CurrentMode);
        }

        private void OnLocalCameraReady()
        {
            if (CurrentMode != TransparentMirrorMode.Off)
            {
                EnsureInitialized(BasisLocalCameraDriver.Instance.Camera);
            }
        }

        private void BootModeChanged(string _) => StartCoroutine(ResetMirror());
        private void OnMirrorQualityChanged(string _) => StartCoroutine(ResetMirror());
        private void OnMirrorQualityOverrideChanged(bool _) => StartCoroutine(ResetMirror());

        private IEnumerator ResetMirror()
        {
            yield return null;
            var mode = CurrentMode;
            CleanUp();
            OnEnable();
            ApplyMode(mode, force: true);
        }

        private void CleanUp()
        {
            BasisLocalCameraDriver.InstanceExists -= OnLocalCameraReady;
            BasisDeviceManagement.OnBootModeChanged -= BootModeChanged;
            BasisSettingsDefaults.MirrorQuality.OnChanged -= OnMirrorQualityChanged;
            BasisSettingsDefaults.UseMirrorQualityOverride.OnChanged -= OnMirrorQualityOverrideChanged;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
#if UNITY_EDITOR
            EditorApplication.update -= RepaintSceneViews;
#endif

            if (basisMeshRendererCheck != null)
            {
                basisMeshRendererCheck.Check -= VisibilityFlag;
            }

            DisposePortalResources();

            if (gazeTarget != null)
            {
                gazeTarget.enabled = false;
            }

            IsActive = false;
            IsAbleToRender = false;
            InsideRendering = false;
        }

        private void DisposeRuntimeMaterials()
        {
            if (runtimeOpaqueMaterial != null)
            {
                Destroy(runtimeOpaqueMaterial);
                runtimeOpaqueMaterial = null;
            }

            if (runtimeTransparentMaterial != null)
            {
                Destroy(runtimeTransparentMaterial);
                runtimeTransparentMaterial = null;
            }
        }

        private void DisposePortalResources()
        {
            if (PortalTextureLeft)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    DestroyImmediate(PortalTextureLeft);
                }
                else
                {
                    Destroy(PortalTextureLeft);
                }
#else
                Destroy(PortalTextureLeft);
#endif
            }

            if (PortalTextureRight)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    DestroyImmediate(PortalTextureRight);
                }
                else
                {
                    Destroy(PortalTextureRight);
                }
#else
                Destroy(PortalTextureRight);
#endif
            }

            BasisCullingCameraRegistry.Unregister(LeftCamera);
            if (LeftCamera)
            {
                Destroy(LeftCamera.gameObject);
            }

            if (RightCamera)
            {
                Destroy(RightCamera.gameObject);
            }

            PortalTextureLeft = null;
            PortalTextureRight = null;
            LeftCamera = RightCamera = null;
        }

        private void ReinitializeMirror()
        {
            if (IsActive)
            {
                DisposePortalResources();
                IsActive = false;
            }

            Camera referenceCamera = BasisLocalCameraDriver.HasInstance ? BasisLocalCameraDriver.Instance.Camera : null;
            EnsureInitialized(referenceCamera);
        }

        private void EnsureInitialized(Camera referenceCamera)
        {
            if (IsActive)
            {
                return;
            }

            if (MirrorRenderer == null)
            {
                Debug.LogError("[TransparentMirror] MirrorRenderer is missing.");
                return;
            }

            DisposePortalResources();

            xFlip = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));

            if (referenceCamera == null)
            {
                return;
            }

            CreatePortalCamera(referenceCamera, StereoscopicEye.Left, ref LeftCamera, ref PortalTextureLeft);
            CreatePortalCamera(referenceCamera, StereoscopicEye.Right, ref RightCamera, ref PortalTextureRight);
            BasisCullingCameraRegistry.Register(LeftCamera);

            BindTextures(runtimeOpaqueMaterial);
            BindTextures(runtimeTransparentMaterial);

            IsActive = true;
            InsideRendering = false;

            if (gazeTarget == null)
            {
                gazeTarget = BasisHelpers.GetOrAddComponent<BasisGazeTarget>(gameObject);
            }

            gazeTarget.Priority = 2f;
            gazeTarget.UseTransformPosition = false;
            gazeTarget.enabled = CurrentMode != TransparentMirrorMode.Off;

            UpdateRenderGate();
        }

        private void BindTextures(Material material)
        {
            if (material == null)
            {
                return;
            }

            material.SetTexture(ReflectionTexLeftId, PortalTextureLeft);
            material.SetTexture(ReflectionTexRightId, PortalTextureRight);
        }

        private static void ConfigureMirrorMaterial(Material material, bool transparent)
        {
            if (material == null)
            {
                return;
            }

            material.SetFloat(TransparentModeId, transparent ? 1f : 0f);
            material.SetFloat(SrcBlendId, transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
            material.SetFloat(DstBlendId, transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            material.SetFloat(ZWriteId, transparent ? 0f : 1f);
            material.SetOverrideTag("RenderType", transparent ? "Transparent" : "Opaque");
            material.renderQueue = transparent ? (int)RenderQueue.Transparent : (int)RenderQueue.Geometry;
        }

        private float GetMirrorAspect()
        {
            if (MirrorRenderer == null)
            {
                return 1f;
            }

            Vector3 scale = MirrorRenderer.transform.lossyScale;
            float width = Mathf.Abs(scale.x);
            float height = Mathf.Abs(scale.y);
            return width / Mathf.Max(height, 0.0001f);
        }

        private void GetEffectiveResolution(out int width, out int height)
        {
            int baseSize;
            if (BasisSettingsDefaults.UseMirrorQualityOverride.RawValue &&
                int.TryParse(BasisSettingsDefaults.MirrorQuality.RawValue, out int overrideRes) && overrideRes > 0)
            {
                baseSize = overrideRes;
            }
            else
            {
                baseSize = Mathf.Max(XSize, YSize);
            }

            float aspect = GetMirrorAspect();
            if (aspect >= 1f)
            {
                width = baseSize;
                height = Mathf.Max(1, Mathf.RoundToInt(baseSize / aspect));
            }
            else
            {
                height = baseSize;
                width = Mathf.Max(1, Mathf.RoundToInt(baseSize * aspect));
            }
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (CurrentMode == TransparentMirrorMode.Off)
            {
                return;
            }

            if (!IsValidSourceCamera(camera))
            {
                return;
            }

            if (!IsActive)
            {
                EnsureInitialized(camera);
            }

            if (!IsActive)
            {
                return;
            }

            if (!IsVisibleToCamera(camera))
            {
                IsAbleToRender = false;
                return;
            }

            IsAbleToRender = true;

            bool renderingLocalCamera = IsBasisLocalCamera(camera);
            if (renderingLocalCamera)
            {
                BasisLocalAvatarDriver.ScaleHeadToNormal();
            }

            OnCamerasRendering?.Invoke();

            normal = MirrorPlaneTransform.TransformDirection(projectionDirection).normalized;

            if (renderingLocalCamera && gazeTarget != null)
            {
                Vector3 eyePos = BasisLocalCameraDriver.Position;
                MirrorPlaneTransform.GetPositionAndRotation(out Vector3 planePosWS, out Quaternion planeRotWS);
                Vector3 eyeLocal = InverseTransformPoint(planePosWS, planeRotWS, eyePos);
                Vector3 reflLocal = Vector3.Reflect(eyeLocal, Vector3.forward);
                gazeTarget.FocusPoint = TransformPoint(planePosWS, planeRotWS, reflLocal);
            }

            RenderBothEyes(camera);

            OnCamerasFinished?.Invoke();

            if (renderingLocalCamera)
            {
                BasisLocalAvatarDriver.ScaleheadToZero();
            }
        }

        private bool IsValidSourceCamera(Camera camera)
        {
            if (camera == null || camera == LeftCamera || camera == RightCamera)
            {
                return false;
            }

            if (camera.name.StartsWith(ReflectionCameraPrefix, StringComparison.Ordinal) ||
                camera.name.StartsWith(BasisSdkReflectionCameraPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            return !InsideRendering;
        }

#if UNITY_EDITOR
        private void RepaintSceneViews()
        {
            if (CurrentMode != TransparentMirrorMode.Off && enabled && gameObject.activeInHierarchy)
            {
                SceneView.RepaintAll();
            }
        }
#endif

        private bool IsBasisLocalCamera(Camera camera)
        {
            return BasisLocalCameraDriver.HasInstance && camera == BasisLocalCameraDriver.Instance.Camera;
        }

        private bool IsVisibleToCamera(Camera camera)
        {
            if (MirrorRenderer == null)
            {
                return false;
            }

            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
            return GeometryUtility.TestPlanesAABB(planes, MirrorRenderer.bounds);
        }

        private void RenderBothEyes(Camera camera)
        {
            if (InsideRendering)
            {
                return;
            }

            InsideRendering = true;

            try
            {
                camera.transform.GetPositionAndRotation(out Vector3 srcPos, out Quaternion srcRot);

                if (camera.stereoEnabled)
                {
                    RenderEye(camera, MonoOrStereoscopicEye.Left, srcPos, srcRot);
                    RenderEye(camera, MonoOrStereoscopicEye.Right, srcPos, srcRot);
                }
                else
                {
                    RenderEye(camera, MonoOrStereoscopicEye.Mono, srcPos, srcRot);
                }
            }
            finally
            {
                InsideRendering = false;
            }
        }

        private void RenderEye(Camera sourceCamera, MonoOrStereoscopicEye eye, Vector3 srcPos, Quaternion srcRot)
        {
            Camera portalCamera = eye == MonoOrStereoscopicEye.Right ? RightCamera : LeftCamera;
            if (!portalCamera)
            {
                return;
            }

            Vector3 eyeOriginWS;
            Matrix4x4 proj;

            if (eye == MonoOrStereoscopicEye.Mono)
            {
                eyeOriginWS = srcPos;
                proj = sourceCamera.projectionMatrix;
            }
            else
            {
                var stereoEye = (StereoscopicEye)eye;
                eyeOriginWS = sourceCamera.GetStereoViewMatrix(stereoEye).inverse.MultiplyPoint(Vector3.zero);
                proj = sourceCamera.GetStereoProjectionMatrix(stereoEye);
            }

            MirrorPlaneTransform.GetPositionAndRotation(out Vector3 planePosWS, out Quaternion planeRotWS);

            Vector3 eyeLocal = InverseTransformPoint(planePosWS, planeRotWS, eyeOriginWS);
            Vector3 fwdLocal = InverseTransformDirection(planeRotWS, srcRot * Vector3.forward);
            Vector3 upLocal = InverseTransformDirection(planeRotWS, srcRot * Vector3.up);

            Vector3 reflPosLocal = Vector3.Reflect(eyeLocal, Vector3.forward);
            Vector3 reflFwdLocal = Vector3.Reflect(fwdLocal, Vector3.forward);
            Vector3 reflUpLocal = Vector3.Reflect(upLocal, Vector3.forward);

            Vector3 reflPosWS = TransformPoint(planePosWS, planeRotWS, reflPosLocal);
            Vector3 reflFwdWS = TransformDirection(planeRotWS, reflFwdLocal);
            Vector3 reflUpWS = TransformDirection(planeRotWS, reflUpLocal);
            Quaternion reflRotWS = Quaternion.LookRotation(reflFwdWS, reflUpWS);

            portalCamera.transform.SetPositionAndRotation(reflPosWS, reflRotWS);
            UpdateCameraClearFlags(portalCamera, sourceCamera);

            Vector4 clipPlaneCamSpace = BasisHelpers.CameraSpacePlane(
                portalCamera.worldToCameraMatrix, planePosWS, normal, clipPlaneOffset);

            clipPlaneCamSpace.x *= -1f;
            CalculateObliqueMatrix(ref proj, clipPlaneCamSpace);

            portalCamera.projectionMatrix = xFlip * proj * xFlip;
            portalCamera.cullingMatrix = portalCamera.projectionMatrix * portalCamera.worldToCameraMatrix;

            if (BasisSettingsDefaults.UseCameraClipOverride.RawValue)
            {
                portalCamera.nearClipPlane = Mathf.Max(0.001f, BasisSettingsDefaults.CameraClipNear.RawValue);
                portalCamera.farClipPlane = BasisSettingsDefaults.CameraClipFar.RawValue;
            }
            else
            {
                portalCamera.nearClipPlane = Mathf.Max(nearClipLimit, portalCamera.nearClipPlane);
                portalCamera.farClipPlane = FarClipPlane;
            }

            SubmitRenderRequest(portalCamera, portalCamera.targetTexture);
        }

        private void SubmitRenderRequest(Camera camera, RenderTexture texture)
        {
            if (!camera || !texture)
            {
                return;
            }

            if (CurrentMode == TransparentMirrorMode.Transparent)
            {
                ClearRenderTexture(texture, Color.clear);
            }

            var request = new UniversalRenderPipeline.SingleCameraRequest
            {
                destination = texture,
                mipLevel = 0,
                slice = 0,
                face = CubemapFace.Unknown
            };

            if (UniversalRenderPipeline.SupportsRenderRequest(camera, request))
            {
                UniversalRenderPipeline.SubmitRenderRequest(camera, request);
            }
        }

        private static void ClearRenderTexture(RenderTexture target, Color color)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, color);
            RenderTexture.active = previous;
        }

        private static Vector3 TransformPoint(Vector3 position, Quaternion rotation, Vector3 pointLocal)
        {
            return rotation * pointLocal + position;
        }

        private static Vector3 TransformDirection(Quaternion rotation, Vector3 directionLocal)
        {
            return rotation * directionLocal;
        }

        private static Vector3 InverseTransformDirection(Quaternion rotation, Vector3 direction)
        {
            return Quaternion.Inverse(rotation) * direction;
        }

        private static Vector3 InverseTransformPoint(Vector3 position, Quaternion rotation, Vector3 point)
        {
            return Quaternion.Inverse(rotation) * (point - position);
        }

        private static void CalculateObliqueMatrix(ref Matrix4x4 projection, float4 clipPlane)
        {
            float4 q = projection.inverse * new float4(math.sign(clipPlane.x), math.sign(clipPlane.y), 1.0f, 1.0f);
            float dot = math.dot(clipPlane, q);
            if (Mathf.Approximately(dot, 0f))
            {
                return;
            }

            float4 c = clipPlane * (2.0f / dot);
            projection[2] = c.x - projection[3];
            projection[6] = c.y - projection[7];
            projection[10] = c.z - projection[11];
            projection[14] = c.w - projection[15];
        }

        private void CreatePortalCamera(Camera sourceCamera, StereoscopicEye eye, ref Camera portalCamera, ref RenderTexture portalTexture)
        {
            GetEffectiveResolution(out int effectiveWidth, out int effectiveHeight);
            bool transparentMode = CurrentMode == TransparentMirrorMode.Transparent;
            var desc = new RenderTextureDescriptor(
                effectiveWidth,
                effectiveHeight,
                transparentMode ? RenderTextureFormat.ARGB32 : RenderTextureFormat.Default,
                depth)
            {
                msaaSamples = transparentMode ? 1 : Mathf.Max(1, Antialiasing),
                sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear,
                useMipMap = false,
                autoGenerateMips = false,
                vrUsage = VRTextureUsage.None,
                dimension = TextureDimension.Tex2D
            };

            portalTexture = new RenderTexture(desc)
            {
                name = $"__TransparentMirrorReflection{eye}_{GetEntityId()}",
                anisoLevel = 0
            };
            portalTexture.Create();

            CreateNewCamera(sourceCamera, out portalCamera);
            portalCamera.targetTexture = portalTexture;
        }

        private void CreateNewCamera(Camera sourceCamera, out Camera newCamera)
        {
            GameObject camObj = new GameObject($"{ReflectionCameraPrefix}{GetEntityId()}_{sourceCamera.GetEntityId()}", typeof(Camera));
            camObj.TryGetComponent(out newCamera);
            newCamera.enabled = false;
            newCamera.CopyFrom(sourceCamera);

            newCamera.depth = 2;
            newCamera.farClipPlane = FarClipPlane;
            newCamera.cullingMask = ActiveReflectingLayers;
            newCamera.useOcclusionCulling = OcclusionCulling;
            UpdateCameraClearFlags(newCamera, sourceCamera);

            if (newCamera.TryGetComponent(out UniversalAdditionalCameraData cameraData))
            {
                cameraData.allowXRRendering = allowXRRendering;
                cameraData.renderPostProcessing = RenderPostProcessing;
                cameraData.renderShadows = renderShadows;
            }
        }

        private void ApplyReflectionLayers(LayerMask mask)
        {
            if (LeftCamera)
            {
                LeftCamera.cullingMask = mask;
            }

            if (RightCamera)
            {
                RightCamera.cullingMask = mask;
            }
        }

        private void UpdateCameraClearFlags()
        {
            Camera refCamera = BasisLocalCameraDriver.HasInstance ? BasisLocalCameraDriver.Instance.Camera : null;
            if (LeftCamera)
            {
                UpdateCameraClearFlags(LeftCamera, refCamera);
            }

            if (RightCamera)
            {
                UpdateCameraClearFlags(RightCamera, refCamera);
            }
        }

        private void UpdateCameraClearFlags(Camera camera, Camera refCamera)
        {
            switch (clearFlags)
            {
                case MirrorClearFlags.Skybox:
                    camera.clearFlags = CameraClearFlags.Skybox;
                    break;
                case MirrorClearFlags.Color:
                    camera.backgroundColor = clearColor;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    break;
                case MirrorClearFlags.Depth:
                    camera.clearFlags = CameraClearFlags.Depth;
                    break;
                case MirrorClearFlags.Nothing:
                    camera.clearFlags = CameraClearFlags.Nothing;
                    break;
                case MirrorClearFlags.FromReferenceCamera:
                default:
                    if (refCamera == null)
                    {
                        return;
                    }

                    camera.backgroundColor = refCamera.backgroundColor;
                    camera.clearFlags = refCamera.clearFlags;
                    break;
            }
        }

        private void VisibilityFlag(bool isVisible)
        {
            meshVisibleToCamera = isVisible;
            UpdateRenderGate();
        }

        private void UpdateRenderGate()
        {
            IsAbleToRender = CurrentMode != TransparentMirrorMode.Off && meshVisibleToCamera && IsActive;
        }

        private static LayerMask BuildAvatarLayerMask()
        {
            int remoteLayer = LayerMask.NameToLayer("RemotePlayerAvatar");
            int localLayer = LayerMask.NameToLayer("LocalPlayerAvatar");
            LayerMask mask = 0;

            if (remoteLayer >= 0)
            {
                mask |= 1 << remoteLayer;
            }

            if (localLayer >= 0)
            {
                mask |= 1 << localLayer;
            }

            return mask;
        }

    }
}
