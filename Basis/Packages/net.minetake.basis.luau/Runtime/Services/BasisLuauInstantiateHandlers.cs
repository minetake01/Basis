using System;
using Basis.Scripts.Device_Management;
using Basis.Scripts.BasisSdk;
using UnityEngine;

namespace Minetake.Basis.Luau.Services
{
    public static class BasisLuauInstantiateHandlers
    {
        public static UnityEngine.Object SanitizeInstantiate(UnityEngine.Object original)
        {
            if (original == null)
            {
                return null;
            }

            GameObject rootPrefab = ExtractRootGameObject(original);
            if (rootPrefab == null)
            {
                return UnityEngine.Object.Instantiate(original);
            }

            BasisDeviceManagement mgmt = BasisDeviceManagement.Instance;
            if (mgmt == null || mgmt.CreationGameobject == null)
            {
                Debug.LogError("[BasisLuau] BasisDeviceManagement.CreationGameobject unavailable; refusing Instantiate.");
                return null;
            }

            ChecksRequired checks = new ChecksRequired
            {
                UseContentRemoval = true,
                ScrubPersistentUnityEvents = true,
                DisableAnimatorEvents = true,
                RemoveColliders = false,
                ChangeCollidersToCorrectLayer = false,
            };

            GameObject sanitizedClone = ContentPoliceControl.ContentControl(
                mgmt.CreationGameobject,
                rootPrefab,
                checks,
                rootPrefab.transform.position,
                rootPrefab.transform.rotation,
                false,
                Vector3.zero,
                BundledContentHolder.Selector.Prop,
                null);

            if (sanitizedClone == null)
            {
                return null;
            }

            if (original is GameObject)
            {
                return sanitizedClone;
            }

            Component match = sanitizedClone.GetComponent(original.GetType());
            return match != null ? match : sanitizedClone;
        }

        static GameObject ExtractRootGameObject(UnityEngine.Object o)
        {
            if (o is GameObject go)
            {
                return go;
            }

            if (o is Component comp)
            {
                return comp.gameObject;
            }

            return null;
        }
    }
}
