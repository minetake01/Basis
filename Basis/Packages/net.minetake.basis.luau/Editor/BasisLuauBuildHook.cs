#if UNITY_EDITOR
using System;
using System.Text;
using Basis.Scripts.BasisSdk;
using Luau;
using Luau.Unity;
using UnityEditor;
using UnityEngine;

namespace Minetake.Basis.Luau.Editor
{
    public static class BasisLuauBuildHook
    {
        [InitializeOnLoadMethod]
        static void Initialize()
        {
            BasisAssetBundlePipeline.OnBeforeBuildPrefab -= HandleBeforeBuildPrefab;
            BasisAssetBundlePipeline.OnBeforeBuildPrefab += HandleBeforeBuildPrefab;
            BasisAvatarSDKInspector.OnBeforeTestInEditor -= HandleBeforeTestInEditor;
            BasisAvatarSDKInspector.OnBeforeTestInEditor += HandleBeforeTestInEditor;
        }

        static void HandleBeforeTestInEditor(GameObject prefabRoot) => HandleBeforeBuildPrefab(prefabRoot, null);

        static void HandleBeforeBuildPrefab(GameObject prefabRoot, BasisAssetBundleObject settings)
        {
            if (prefabRoot == null)
            {
                return;
            }

            BasisLuauBehaviour[] behaviours = prefabRoot.GetComponentsInChildren<BasisLuauBehaviour>(true);
            if (behaviours.Length == 0)
            {
                return;
            }

            LuauHostBase host = prefabRoot.GetComponentInChildren<LuauHostBase>(true);
            if (host == null)
            {
                throw new InvalidOperationException(
                    $"Basis Luau build failed: '{prefabRoot.name}' contains BasisLuauBehaviour but no LuauHostBase (Prop/Scene/Avatar).");
            }

            for (int i = 0; i < behaviours.Length; i++)
            {
                BasisLuauBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                {
                    continue;
                }

                if (behaviour.Script == null)
                {
                    throw new InvalidOperationException(
                        $"Basis Luau build failed: BasisLuauBehaviour on '{behaviour.gameObject.name}' has no LuauAsset assigned.");
                }

                if (behaviour.HostKind != host.HostKind)
                {
                    throw new InvalidOperationException(
                        $"Basis Luau build failed: host kind mismatch on '{behaviour.gameObject.name}'.");
                }

                byte[] bytecode;
                if (behaviour.Script.IsPrecompiled)
                {
                    bytecode = behaviour.Script.AsSpan().ToArray();
                }
                else
                {
                    SerializedObject so = new SerializedObject(behaviour.Script);
                    string source = so.FindProperty("text").stringValue;
                    bytecode = LuauCompiler.Compile(Encoding.UTF8.GetBytes(source));
                }

                LuauScriptProxy proxy = behaviour.gameObject.GetComponent<LuauScriptProxy>();
                if (proxy == null)
                {
                    proxy = behaviour.gameObject.AddComponent<LuauScriptProxy>();
                }

                proxy.Configure(behaviour.HostKind, bytecode, behaviour.HandleSlots, behaviour.gameObject.name);
                UnityEngine.Object.DestroyImmediate(behaviour, true);
                EditorUtility.SetDirty(proxy);
            }
        }
    }
}
#endif
