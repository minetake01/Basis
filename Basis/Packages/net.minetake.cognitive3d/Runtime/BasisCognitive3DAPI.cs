#if BASIS_FRAMEWORK_EXISTS
using System;
using System.Collections.Generic;
using UnityEngine;

#if COGNITIVE3D_EXISTS
using Cognitive3D;
#endif

namespace Net.Minetake.Basis.Cognitive3D
{
    /// <summary>
    /// Developer-facing public API facade for Cognitive3D telemetry in Basis VR applications.
    /// Provides fail-safe methods to record custom events, manage dynamic object engagement,
    /// and annotate sessions without direct dependency leaks.
    /// </summary>
    public static class BasisCognitive3DAPI
    {
        /// <summary>
        /// Sends a custom event with optional spatial position and metadata properties.
        /// Discarded if telemetry is disabled or user has opted out.
        /// </summary>
        /// <param name="eventName">Identifier for the custom event.</param>
        /// <param name="position">World position of the event (defaults to HMD camera position if null).</param>
        /// <param name="properties">Key-value metadata dictionary.</param>
        public static void SendCustomEvent(string eventName, Vector3? position = null, Dictionary<string, object> properties = null)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                throw new ArgumentNullException(nameof(eventName), "Event name must not be null or empty.");
            }

            if (!BasisCognitive3DSettings.IsTrackingAllowed)
            {
                return;
            }

#if COGNITIVE3D_EXISTS
            try
            {
                CustomEvent customEvent = new CustomEvent(eventName);

                if (properties != null)
                {
                    foreach (KeyValuePair<string, object> kvp in properties)
                    {
                        customEvent.SetProperty(kvp.Key, kvp.Value);
                    }
                }

                if (position.HasValue)
                {
                    customEvent.Send(position.Value);
                }
                else
                {
                    customEvent.Send();
                }
            }
            catch (Exception ex)
            {
                BasisDebug.LogError($"[Cognitive3D] SendCustomEvent failed for '{eventName}': {ex.Message}", BasisDebug.LogTag.Normal);
            }
#endif
        }

        /// <summary>
        /// Begins an active engagement state on a DynamicObject (e.g. grabbed tool, operated switch).
        /// </summary>
        /// <param name="target">GameObject carrying a DynamicObject component.</param>
        /// <param name="actionName">Action label describing the engagement (e.g. "Grab", "Use").</param>
        public static void BeginEngagement(GameObject target, string actionName)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (string.IsNullOrEmpty(actionName)) throw new ArgumentNullException(nameof(actionName));

            if (!BasisCognitive3DSettings.IsTrackingAllowed) return;

#if COGNITIVE3D_EXISTS
            try
            {
                DynamicObject dynamicObj = target.GetComponent<DynamicObject>();
                if (dynamicObj != null)
                {
                    dynamicObj.BeginEngagement(actionName);
                }
                else
                {
                    BasisDebug.LogWarning($"[Cognitive3D] Target GameObject '{target.name}' has no DynamicObject component attached.", BasisDebug.LogTag.Normal);
                }
            }
            catch (Exception ex)
            {
                BasisDebug.LogError($"[Cognitive3D] BeginEngagement failed on '{target.name}': {ex.Message}", BasisDebug.LogTag.Normal);
            }
#endif
        }

        /// <summary>
        /// Concludes an active engagement state on a DynamicObject.
        /// </summary>
        /// <param name="target">GameObject carrying a DynamicObject component.</param>
        public static void EndEngagement(GameObject target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (!BasisCognitive3DSettings.IsTrackingAllowed) return;

#if COGNITIVE3D_EXISTS
            try
            {
                DynamicObject dynamicObj = target.GetComponent<DynamicObject>();
                dynamicObj?.EndEngagement();
            }
            catch (Exception ex)
            {
                BasisDebug.LogError($"[Cognitive3D] EndEngagement failed on '{target.name}': {ex.Message}", BasisDebug.LogTag.Normal);
            }
#endif
        }

        /// <summary>
        /// Associates metadata with a custom event referencing a specific DynamicObject.
        /// </summary>
        public static void SendObjectEvent(string eventName, GameObject target, Dictionary<string, object> properties = null)
        {
            if (string.IsNullOrEmpty(eventName)) throw new ArgumentNullException(nameof(eventName));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (!BasisCognitive3DSettings.IsTrackingAllowed) return;

#if COGNITIVE3D_EXISTS
            try
            {
                DynamicObject dynamicObj = target.GetComponent<DynamicObject>();
                CustomEvent customEvent = new CustomEvent(eventName);

                if (dynamicObj != null)
                {
                    customEvent.SetDynamicObject(dynamicObj);
                }

                if (properties != null)
                {
                    foreach (KeyValuePair<string, object> kvp in properties)
                    {
                        customEvent.SetProperty(kvp.Key, kvp.Value);
                    }
                }

                customEvent.Send(target.transform.position);
            }
            catch (Exception ex)
            {
                BasisDebug.LogError($"[Cognitive3D] SendObjectEvent failed on '{target.name}': {ex.Message}", BasisDebug.LogTag.Normal);
            }
#endif
        }
    }
}
#endif
