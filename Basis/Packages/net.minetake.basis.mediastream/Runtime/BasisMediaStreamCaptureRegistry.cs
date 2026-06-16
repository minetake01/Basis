using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Copies per-camera color buffers for screen-target cameras registered here.
/// Consumed by <see cref="BasisMediaStreamCaptureFeature"/>.
/// </summary>
public static class BasisMediaStreamCaptureRegistry
{
    public sealed class Entry
    {
        public Camera Camera;
        public RenderTexture OutputRT;
        internal RTHandle OutputHandle;
    }

    private static readonly Dictionary<int, Entry> ByCameraId = new();

    public static IReadOnlyDictionary<int, Entry> Entries => ByCameraId;

    public static Entry Register(Camera camera)
    {
        if (camera == null) return null;
        int id = camera.GetInstanceID();
        if (!ByCameraId.TryGetValue(id, out Entry entry))
        {
            entry = new Entry { Camera = camera };
            ByCameraId[id] = entry;
        }
        return entry;
    }

    public static void Unregister(Camera camera)
    {
        if (camera == null) return;
        int id = camera.GetInstanceID();
        if (!ByCameraId.TryGetValue(id, out Entry entry)) return;
        ReleaseEntry(entry);
        ByCameraId.Remove(id);
    }

    public static bool TryGet(Camera camera, out Entry entry)
    {
        entry = null;
        if (camera == null) return false;
        return ByCameraId.TryGetValue(camera.GetInstanceID(), out entry);
    }

    internal static void ReleaseEntry(Entry entry)
    {
        if (entry == null) return;
        if (entry.OutputHandle != null)
        {
            entry.OutputHandle.Release();
            entry.OutputHandle = null;
        }
        if (entry.OutputRT != null)
        {
            if (entry.OutputRT.IsCreated()) entry.OutputRT.Release();
            UnityEngine.Object.Destroy(entry.OutputRT);
            entry.OutputRT = null;
        }
    }
}
