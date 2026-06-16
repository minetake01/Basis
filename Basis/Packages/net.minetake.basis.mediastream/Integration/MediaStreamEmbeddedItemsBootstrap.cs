using System.Reflection;
using Basis.BasisUI;
using Basis.Scripts.UI.UI_Panels;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Basis.MediaStream
{
    /// <summary>
    /// Merges this package's embedded item catalog into the framework library list.
    /// Keeps SDK and framework assets untouched while enabling the streaming camera prop pin.
    /// </summary>
    internal static class MediaStreamEmbeddedItemsBootstrap
    {
        private const string CatalogAddress = "MinetakeMediaStreamEmbeddedItemsCatalog";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void RegisterEmbeddedItems()
        {
            _ = EmbeddedItems.Catalog;

            AsyncOperationHandle<EmbeddedItemsCatalogAsset> handle =
                Addressables.LoadAssetAsync<EmbeddedItemsCatalogAsset>(CatalogAddress);
            EmbeddedItemsCatalogAsset supplemental = handle.WaitForCompletion();
            if (supplemental == null || supplemental.Entries == null || supplemental.Entries.Count == 0)
            {
                Debug.LogWarning(
                    $"[net.minetake.basis.mediastream] Could not load embedded items catalog at address '{CatalogAddress}'. " +
                    "Ensure the asset is registered in Addressables with that address.");
                return;
            }

            if (!TryMergeCatalog(supplemental))
            {
                Debug.LogWarning("[net.minetake.basis.mediastream] Failed to merge embedded items catalog.");
                return;
            }

            EnsureItemKeysIncludeEmbedded();
        }

        private static bool TryMergeCatalog(EmbeddedItemsCatalogAsset supplemental)
        {
            var embeddedItemsType = typeof(EmbeddedItems);
            FieldInfo catalogField = embeddedItemsType.GetField("_catalog", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo rebuildCache = embeddedItemsType.GetMethod("RebuildCache", BindingFlags.Static | BindingFlags.NonPublic);
            if (catalogField == null || rebuildCache == null)
                return false;

            if (catalogField.GetValue(null) is not EmbeddedItemsCatalogAsset catalog)
                return false;

            catalog.Entries.AddRange(supplemental.Entries);
            rebuildCache.Invoke(null, null);
            return true;
        }

        private static void EnsureItemKeysIncludeEmbedded()
        {
            var itemKeysType = typeof(BasisDataStoreItemKeys);
            FieldInfo loadedField = itemKeysType.GetField("_loaded", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo validate = itemKeysType.GetMethod("ValidateEmbeddedKeys", BindingFlags.Static | BindingFlags.NonPublic);
            if (loadedField == null || validate == null)
                return;

            if (loadedField.GetValue(null) is not bool loaded || !loaded)
                return;

            validate.Invoke(null, null);
        }
    }
}
