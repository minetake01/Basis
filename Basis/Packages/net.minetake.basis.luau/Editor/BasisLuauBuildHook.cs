#if UNITY_EDITOR
using Basis.Scripts.BasisSdk;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Minetake.Basis.Luau.Editor
{
    public static class BasisLuauBuildHook
    {
        [InitializeOnLoadMethod]
        static void Initialize()
        {
            BasisAssetBundlePipeline.OnBeforeBuildPrefab -= HandleBeforeBuildPrefab;
            BasisAssetBundlePipeline.OnBeforeBuildPrefab += HandleBeforeBuildPrefab;
            BasisAssetBundlePipeline.OnBeforeBuildScene -= HandleBeforeBuildScene;
            BasisAssetBundlePipeline.OnBeforeBuildScene += HandleBeforeBuildScene;
            BasisAvatarSDKInspector.OnBeforeTestInEditor -= HandleBeforeTestInEditor;
            BasisAvatarSDKInspector.OnBeforeTestInEditor += HandleBeforeTestInEditor;
            BasisPropSDKInspector.OnBeforeTestInEditor -= HandleBeforeTestInEditor;
            BasisPropSDKInspector.OnBeforeTestInEditor += HandleBeforeTestInEditor;
        }

        static void HandleBeforeTestInEditor(GameObject prefabRoot) =>
            BasisLuauAuthoringConverter.ConvertHierarchy(
                prefabRoot,
                BasisLuauAuthoringConverter.ConversionMode.Build);

        static void HandleBeforeBuildPrefab(GameObject prefabRoot, BasisAssetBundleObject settings) =>
            BasisLuauAuthoringConverter.ConvertHierarchy(
                prefabRoot,
                BasisLuauAuthoringConverter.ConversionMode.Build);

        static void HandleBeforeBuildScene(Scene scene, BasisAssetBundleObject settings) =>
            BasisLuauAuthoringConverter.ConvertScene(
                scene,
                BasisLuauAuthoringConverter.ConversionMode.Build);
    }
}
#endif
