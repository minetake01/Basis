#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using Luau;
using Luau.Unity;
using Minetake.Basis.Luau;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Minetake.Basis.Luau.Editor
{
    public static class BasisLuauAuthoringConverter
    {
        public enum ConversionMode
        {
            Build,
            Preview,
        }

        public readonly struct ConversionPlan
        {
            public readonly BasisLuauBehaviour Behaviour;
            public readonly LuauHostBase Host;
            public readonly byte[] Bytecode;

            public ConversionPlan(BasisLuauBehaviour behaviour, LuauHostBase host, byte[] bytecode)
            {
                Behaviour = behaviour;
                Host = host;
                Bytecode = bytecode;
            }
        }

        public static void ConvertHierarchy(GameObject root, ConversionMode mode)
        {
            if (root == null)
            {
                return;
            }

            BasisLuauBehaviour[] behaviours = root.GetComponentsInChildren<BasisLuauBehaviour>(true);
            ConvertBehaviours(behaviours, mode);
        }

        public static void ConvertScene(Scene scene, ConversionMode mode)
        {
            if (!scene.IsValid())
            {
                return;
            }

            var behaviours = new List<BasisLuauBehaviour>();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                behaviours.AddRange(roots[i].GetComponentsInChildren<BasisLuauBehaviour>(true));
            }

            ConvertBehaviours(behaviours, mode);
        }

        public static void ConvertBehaviours(IReadOnlyList<BasisLuauBehaviour> behaviours, ConversionMode mode)
        {
            if (behaviours == null || behaviours.Count == 0)
            {
                return;
            }

            ConversionPlan[] plans = BuildPlans(behaviours);
            ApplyPlans(plans, mode);
        }

        public static ConversionPlan[] BuildPlans(IReadOnlyList<BasisLuauBehaviour> behaviours)
        {
            var plans = new List<ConversionPlan>(behaviours.Count);
            var sceneHosts = new Dictionary<Scene, LuauSceneHost>();

            for (int i = 0; i < behaviours.Count; i++)
            {
                BasisLuauBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                {
                    continue;
                }

                string objectName = behaviour.gameObject.name;
                if (behaviour.TryGetComponent(out LuauScriptProxy _))
                {
                    throw new InvalidOperationException(
                        $"Basis Luau conversion failed: '{objectName}' contains both authoring and runtime components.");
                }

                if (behaviour.Script == null)
                {
                    throw new InvalidOperationException(
                        $"Basis Luau conversion failed: BasisLuauBehaviour on '{objectName}' has no LuauAsset assigned.");
                }

                LuauHostBase host = ResolveHost(behaviour, sceneHosts);
                if (host == null)
                {
                    throw new InvalidOperationException(
                        $"Basis Luau conversion failed: BasisLuauBehaviour on '{objectName}' has no resolvable LuauHostBase.");
                }

                if (behaviour.HostKind != host.HostKind)
                {
                    throw new InvalidOperationException(
                        $"Basis Luau conversion failed: host kind mismatch on '{objectName}'. Expected {behaviour.HostKind}, host is {host.HostKind}.");
                }

                byte[] bytecode = CompileScript(behaviour.Script);
                plans.Add(new ConversionPlan(behaviour, host, bytecode));
            }

            return plans.ToArray();
        }

        public static void ApplyPlans(ConversionPlan[] plans, ConversionMode mode)
        {
            if (plans == null || plans.Length == 0)
            {
                return;
            }

            var applied = new List<ConversionPlan>(plans.Length);
            try
            {
                for (int i = 0; i < plans.Length; i++)
                {
                    ConversionPlan plan = plans[i];
                    BasisLuauBehaviour behaviour = plan.Behaviour;
                    if (behaviour == null)
                    {
                        continue;
                    }

                    LuauScriptProxy proxy = behaviour.gameObject.AddComponent<LuauScriptProxy>();

                    proxy.Configure(
                        behaviour.HostKind,
                        plan.Bytecode,
                        behaviour.HandleSlots,
                        behaviour.gameObject.name,
                        plan.Host);

                    if (mode == ConversionMode.Build)
                    {
                        UnityEngine.Object.DestroyImmediate(behaviour, true);
                    }

                    EditorUtility.SetDirty(proxy);
                    applied.Add(plan);
                }
            }
            catch
            {
                if (mode == ConversionMode.Preview)
                {
                    RemovePreviewProxies(applied);
                }

                throw;
            }
        }

        public static IEnumerable<BasisLuauBehaviour> CollectBehavioursInOpenScenes()
        {
            var seen = new HashSet<BasisLuauBehaviour>();
            int sceneCount = SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                GameObject[] roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    BasisLuauBehaviour[] behaviours = roots[r].GetComponentsInChildren<BasisLuauBehaviour>(true);
                    for (int b = 0; b < behaviours.Length; b++)
                    {
                        if (behaviours[b] != null && seen.Add(behaviours[b]))
                        {
                            yield return behaviours[b];
                        }
                    }
                }
            }

            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                GameObject prefabRoot = prefabStage.prefabContentsRoot;
                if (prefabRoot != null)
                {
                    BasisLuauBehaviour[] behaviours = prefabRoot.GetComponentsInChildren<BasisLuauBehaviour>(true);
                    for (int b = 0; b < behaviours.Length; b++)
                    {
                        if (behaviours[b] != null && seen.Add(behaviours[b]))
                        {
                            yield return behaviours[b];
                        }
                    }
                }
            }
        }

        public static void RemovePreviewProxiesInOpenScenes()
        {
            foreach (BasisLuauBehaviour behaviour in CollectBehavioursInOpenScenes())
            {
                LuauScriptProxy proxy = behaviour != null ? behaviour.GetComponent<LuauScriptProxy>() : null;
                if (proxy != null)
                {
                    UnityEngine.Object.DestroyImmediate(proxy, true);
                }
            }
        }

        static void RemovePreviewProxies(IReadOnlyList<ConversionPlan> plans)
        {
            for (int i = 0; i < plans.Count; i++)
            {
                BasisLuauBehaviour behaviour = plans[i].Behaviour;
                LuauScriptProxy proxy = behaviour != null ? behaviour.GetComponent<LuauScriptProxy>() : null;
                if (proxy != null)
                {
                    UnityEngine.Object.DestroyImmediate(proxy, true);
                }
            }
        }

        static LuauHostBase ResolveHost(BasisLuauBehaviour behaviour, Dictionary<Scene, LuauSceneHost> sceneHosts)
        {
            switch (behaviour.HostKind)
            {
                case LuauHostKind.Prop:
                    return behaviour.GetComponentInParent<LuauPropHost>(true);
                case LuauHostKind.Avatar:
                    return behaviour.GetComponentInParent<LuauAvatarHost>(true);
                case LuauHostKind.Scene:
                    return ResolveSceneHost(behaviour.gameObject.scene, sceneHosts);
                default:
                    return null;
            }
        }

        static LuauSceneHost ResolveSceneHost(Scene scene, Dictionary<Scene, LuauSceneHost> sceneHosts)
        {
            if (!scene.IsValid())
            {
                return null;
            }

            if (sceneHosts.TryGetValue(scene, out LuauSceneHost cached))
            {
                return cached;
            }

            LuauSceneHost[] hosts = UnityEngine.Object.FindObjectsByType<LuauSceneHost>(FindObjectsInactive.Include);
            LuauSceneHost match = null;
            for (int i = 0; i < hosts.Length; i++)
            {
                LuauSceneHost host = hosts[i];
                if (host != null && host.gameObject.scene == scene)
                {
                    if (match != null)
                    {
                        throw new InvalidOperationException(
                            $"Basis Luau conversion failed: scene '{scene.name}' contains multiple LuauSceneHost components.");
                    }

                    match = host;
                }
            }

            if (match == null)
            {
                throw new InvalidOperationException(
                    $"Basis Luau conversion failed: scene '{scene.name}' has no LuauSceneHost.");
            }

            sceneHosts[scene] = match;
            return match;
        }

        static byte[] CompileScript(LuauAsset script)
        {
            if (script.IsPrecompiled)
            {
                return script.AsSpan().ToArray();
            }

            SerializedObject so = new SerializedObject(script);
            string source = so.FindProperty("text").stringValue;
            return LuauCompiler.Compile(Encoding.UTF8.GetBytes(source));
        }
    }
}
#endif
