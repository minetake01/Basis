#if BASIS_FRAMEWORK_EXISTS
using Basis.Scripts.Device_Management.EyeTracking;
using Basis.Scripts.Drivers.Local;
using UnityEngine;

namespace Net.Minetake.Basis.Cognitive3D
{
    /// <summary>
    /// Connects Basis VR's eye-tracking arbitration pipeline to Cognitive3D gaze sampling.
    /// Bridges OpenXR hardware eye gaze, OSC face tracking, and HMD forward-fallback gaze.
    /// </summary>
    public static class BasisCognitive3DGazeDriver
    {
        /// <summary>
        /// Retrieves the current best gaze ray in world coordinates.
        /// Prioritizes arbitrated eye tracking data from BasisEyeTrackingManager;
        /// falls back to the local HMD camera forward direction if eye tracking is unavailable.
        /// </summary>
        public static bool TryGetGazeRay(out Vector3 origin, out Vector3 direction)
        {
            if (BasisEyeTrackingManager.Current.HasWorldRay)
            {
                origin = BasisEyeTrackingManager.Current.GazeOrigin;
                direction = BasisEyeTrackingManager.Current.GazeDirection;
                return true;
            }

            // Fallback: local HMD camera center and forward orientation
            origin = BasisLocalCameraDriver.Position;
            direction = BasisLocalCameraDriver.Forward();
            return direction.sqrMagnitude > 0.001f;
        }

        /// <summary>
        /// Indicates whether true eye tracking (hardware or OSC) is active and reporting valid rays.
        /// </summary>
        public static bool IsEyeTrackingActive => BasisEyeTrackingManager.Current.HasWorldRay;
    }
}
#endif
