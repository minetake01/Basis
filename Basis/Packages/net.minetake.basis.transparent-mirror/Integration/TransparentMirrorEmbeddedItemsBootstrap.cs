using System.Reflection;
using Basis.BasisUI;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Basis.TransparentMirror
{
    internal static class TransparentMirrorEmbeddedItemsBootstrap
    {
        private const string CatalogAddress = "TransparentMirrorEmbeddedItemsCatalog";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterEmbeddedItems()
        {
            _ = EmbeddedItems.Catalog;

            AsyncOperationHandle<EmbeddedItemsCatalogAsset> handle =
                Addressables.LoadAssetAsync<EmbeddedItemsCatalogAsset>(CatalogAddress);
            EmbeddedItemsCatalogAsset supplemental = handle.WaitForCompletion();
            if (supplemental == null || supplemental.Entries == null || supplemental.Entries.Count == 0)
            {
                return;
            }

            if (!TryMergeCatalog(supplemental))
            {
                Debug.LogWarning("[net.minetake.basis.transparent-mirror] Failed to merge embedded items catalog.");
            }
        }

        private static bool TryMergeCatalog(EmbeddedItemsCatalogAsset supplemental)
        {
            var embeddedItemsType = typeof(EmbeddedItems);
            FieldInfo catalogField = embeddedItemsType.GetField("_catalog", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo rebuildCache = embeddedItemsType.GetMethod("RebuildCache", BindingFlags.Static | BindingFlags.NonPublic);
            if (catalogField == null || rebuildCache == null)
            {
                return false;
            }

            if (catalogField.GetValue(null) is not EmbeddedItemsCatalogAsset catalog)
            {
                return false;
            }

            catalog.Entries.AddRange(supplemental.Entries);
            rebuildCache.Invoke(null, null);
            return true;
        }
    }
}
