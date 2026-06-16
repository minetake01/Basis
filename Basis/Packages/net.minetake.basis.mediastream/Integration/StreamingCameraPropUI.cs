using UnityEngine;

namespace Basis.MediaStream
{
    /// <summary>
    /// Marker component for the experimental streaming camera prefab.
    /// Uses <see cref="BasisDebugStreamingCamera"/> instead of the SDK photo camera.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BasisDebugStreamingCamera))]
    public sealed class StreamingCameraPropUI : MonoBehaviour
    {
    }
}
