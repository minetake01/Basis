#if UNITY_EDITOR
using System;
using Minetake.Basis.Luau;
using UnityEditor;
using UnityEngine;

namespace Minetake.Basis.Luau.Editor
{
    public static class BasisLuauPlayModeHook
    {
        [InitializeOnLoadMethod]
        static void Initialize()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    TryConvertForPlayMode();
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                case PlayModeStateChange.EnteredEditMode:
                    BasisLuauAuthoringConverter.RemovePreviewProxiesInOpenScenes();
                    break;
            }
        }

        static void TryConvertForPlayMode()
        {
            var behaviours = new System.Collections.Generic.List<BasisLuauBehaviour>();
            foreach (BasisLuauBehaviour behaviour in BasisLuauAuthoringConverter.CollectBehavioursInOpenScenes())
            {
                behaviours.Add(behaviour);
            }

            if (behaviours.Count == 0)
            {
                return;
            }

            try
            {
                BasisLuauAuthoringConverter.ConvertBehaviours(
                    behaviours,
                    BasisLuauAuthoringConverter.ConversionMode.Preview);
            }
            catch (Exception ex)
            {
                BasisLuauAuthoringConverter.RemovePreviewProxiesInOpenScenes();
                Debug.LogError($"Basis Luau Play Mode conversion failed: {ex.Message}");
                EditorApplication.isPlaying = false;
            }
        }
    }
}
#endif
