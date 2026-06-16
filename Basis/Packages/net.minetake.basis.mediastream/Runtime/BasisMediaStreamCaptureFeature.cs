using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP feature that copies registered cameras' post-processed color into per-publisher RTs.
/// </summary>
public class BasisMediaStreamCaptureFeature : ScriptableRendererFeature
{
    [Serializable]
    public class Settings
    {
        public RenderPassEvent when = RenderPassEvent.AfterRenderingPostProcessing;
        public Material srpBlitMaterial;
        public bool disableSrpBlitColorConversionKeywords = true;
        public bool matchCameraSRGB = true;
        public FilterMode filterMode = FilterMode.Bilinear;
    }

    public Settings settings = new Settings();

    class Pass : ScriptableRenderPass
    {
        public Settings settings;

        public Pass()
        {
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
        {
            var cameraData = frameContext.Get<UniversalCameraData>();
            var camera = cameraData.camera;
            if (camera == null) return;
            if (!BasisMediaStreamCaptureRegistry.TryGet(camera, out var entry) || entry == null) return;
            if (entry.OutputRT == null || !entry.OutputRT.IsCreated()) return;

            var resourceData = frameContext.Get<UniversalResourceData>();
            var src = resourceData.activeColorTexture;
            var dst = renderGraph.ImportTexture(entry.OutputHandle);

            var srcDesc = renderGraph.GetTextureDesc(src);
            var dstDesc = renderGraph.GetTextureDesc(dst);
            bool msaaMatches = srcDesc.msaaSamples == dstDesc.msaaSamples;
            bool canCopy = msaaMatches && RenderGraphUtils.CanAddCopyPassMSAA();

            if (canCopy)
            {
                renderGraph.AddCopyPass(src, dst, passName: "MediaStream CameraColor Copy");
            }
            else if (settings.srpBlitMaterial != null)
            {
                if (settings.disableSrpBlitColorConversionKeywords)
                {
                    settings.srpBlitMaterial.DisableKeyword("_LINEAR_TO_SRGB_CONVERSION");
                    settings.srpBlitMaterial.DisableKeyword("_SRGB_TO_LINEAR_CONVERSION");
                }
                var blitParams = new RenderGraphUtils.BlitMaterialParameters(src, dst, settings.srpBlitMaterial, 0);
                renderGraph.AddBlitPass(blitParams, passName: "MediaStream CameraColor Blit");
            }
        }

        public static void EnsureRT(Camera camera, Settings settings, BasisMediaStreamCaptureRegistry.Entry entry, in RenderingData renderingData)
        {
            var camDesc = renderingData.cameraData.cameraTargetDescriptor;
            int w = Mathf.Max(1, camDesc.width);
            int h = Mathf.Max(1, camDesc.height);

            var desc = camDesc;
            desc.width = w;
            desc.height = h;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.useMipMap = false;
            desc.autoGenerateMips = false;
            if (settings.matchCameraSRGB)
                desc.sRGB = camDesc.sRGB;
            desc.graphicsFormat = SystemInfo.GetCompatibleFormat(camDesc.graphicsFormat, GraphicsFormatUsage.Render);

            bool needsRebuild = entry.OutputRT == null || !entry.OutputRT.IsCreated()
                || entry.OutputRT.width != desc.width || entry.OutputRT.height != desc.height
                || entry.OutputRT.descriptor.graphicsFormat != desc.graphicsFormat
                || entry.OutputRT.descriptor.sRGB != desc.sRGB;

            if (!needsRebuild) return;

            BasisMediaStreamCaptureRegistry.ReleaseEntry(entry);

            entry.OutputRT = new RenderTexture(desc)
            {
                name = $"MediaStreamCapture_{camera.name}",
                filterMode = settings.filterMode,
                wrapMode = TextureWrapMode.Clamp,
            };
            entry.OutputRT.Create();
            entry.OutputHandle = RTHandles.Alloc(entry.OutputRT);
        }
    }

    Pass _pass;

    public override void Create()
    {
        _pass = new Pass { settings = settings, renderPassEvent = settings.when };
        if (settings.srpBlitMaterial && settings.disableSrpBlitColorConversionKeywords)
        {
            settings.srpBlitMaterial.DisableKeyword("_LINEAR_TO_SRGB_CONVERSION");
            settings.srpBlitMaterial.DisableKeyword("_SRGB_TO_LINEAR_CONVERSION");
        }
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var camera = renderingData.cameraData.camera;
        if (camera == null) return;
        if (renderingData.cameraData.isSceneViewCamera || renderingData.cameraData.isPreviewCamera) return;
        if (!BasisMediaStreamCaptureRegistry.TryGet(camera, out var entry) || entry == null) return;

        Pass.EnsureRT(camera, settings, entry, renderingData);
        _pass.settings = settings;
        _pass.renderPassEvent = settings.when;
        renderer.EnqueuePass(_pass);
    }
}
