using System;
using System.Collections.Generic;
using Basis.Editor.Localization;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Helpers.Editor;
using Basis.Scripts.BasisSdk.Players;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static BasisAvatarValidator;

[CustomEditor(typeof(BasisProp))]
public class BasisPropSDKInspector : Editor
{
    private const string PendingTestInEditorPropIdSessionKey = "BasisPropSDKInspector.PendingTestInEditorPropId";

    public delegate void BeforeTestInEditorHandler(GameObject clone);
    public static BeforeTestInEditorHandler OnBeforeTestInEditor;
    private static BasisProp ScheduledTestInEditorProp;

    public VisualTreeAsset visualTree;
    public BasisProp BasisProp;
    public VisualElement rootElement;
    public VisualElement uiElementsRoot;
    private Label resultLabel;
    public BasisAssetBundleObject assetBundleObject;
    public BasisPropValidator BasisPropValidator;

    [InitializeOnLoadMethod]
    private static void InitializeTestInEditorHooks()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !HasPendingTestInEditorPropId())
        {
            return;
        }

        EditorApplication.delayCall -= TryExecutePendingTestInEditor;
        EditorApplication.delayCall += TryExecutePendingTestInEditor;
    }

    private static void TryExecutePendingTestInEditor()
    {
        string pendingPropId = GetPendingTestInEditorPropId();
        if (string.IsNullOrEmpty(pendingPropId))
        {
            return;
        }

        if (!GlobalObjectId.TryParse(pendingPropId, out GlobalObjectId globalObjectId))
        {
            ClearPendingTestInEditorPropId();
            return;
        }

        ClearPendingTestInEditorPropId();
        UnityEngine.Object resolved = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalObjectId);
        if (resolved is not BasisProp prop)
        {
            BasisDebug.LogError("Unable to resolve the pending prop for Test In Editor.", BasisDebug.LogTag.Editor);
            return;
        }

        RequestPropLoad(prop);
    }

    private static bool HasPendingTestInEditorPropId()
    {
        return SessionState.GetBool(PendingTestInEditorPropIdSessionKey + ".Exists", false);
    }

    private static string GetPendingTestInEditorPropId()
    {
        return SessionState.GetString(PendingTestInEditorPropIdSessionKey, string.Empty);
    }

    private static void SetPendingTestInEditorPropId(string propId)
    {
        SessionState.SetString(PendingTestInEditorPropIdSessionKey, propId ?? string.Empty);
        SessionState.SetBool(PendingTestInEditorPropIdSessionKey + ".Exists", !string.IsNullOrEmpty(propId));
    }

    private static void ClearPendingTestInEditorPropId()
    {
        SessionState.EraseString(PendingTestInEditorPropIdSessionKey);
        SessionState.SetBool(PendingTestInEditorPropIdSessionKey + ".Exists", false);
    }

    public void OnEnable()
    {
        visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(BasisSDKConstants.PropuxmlPath);
        BasisProp = (BasisProp)target;
    }

    public void OnDisable()
    {
        if (BasisPropValidator != null)
        {
            BasisPropValidator.OnDestroy();
        }
    }

    public override VisualElement CreateInspectorGUI()
    {
        BasisProp = (BasisProp)target;
        rootElement = new VisualElement();

        if (visualTree != null)
        {
            uiElementsRoot = visualTree.CloneTree();
            rootElement.Add(uiElementsRoot);

            BasisPropValidator = new BasisPropValidator(BasisProp, rootElement);

            Button docButton = DocumentationButton(rootElement, BasisEditorLocalization.Get("sdk.prop.documentation.button"));
            docButton.clicked += delegate
            {
                if (EditorUtility.DisplayDialog(
                        BasisEditorLocalization.Get("sdk.common.dialog.openDocumentation.title"),
                        BasisEditorLocalization.Get("sdk.common.dialog.openDocumentation.body"),
                        BasisEditorLocalization.Get("sdk.common.dialog.yes"),
                        BasisEditorLocalization.Get("sdk.common.dialog.no")))
                {
                    Application.OpenURL(BasisSDKConstants.PropDocumentationURL);
                }
            };
            rootElement.Add(docButton);

            TextField PropNameField = uiElementsRoot.Q<TextField>(BasisSDKConstants.PropName);
            TextField PropDescriptionField = uiElementsRoot.Q<TextField>(BasisSDKConstants.PropDescription);

            PropNameField.value = BasisProp.BasisBundleDescription.AssetBundleName;
            PropDescriptionField.value = BasisProp.BasisBundleDescription.AssetBundleDescription;

            PropNameField.RegisterCallback<ChangeEvent<string>>(PropNameChanged);
            PropDescriptionField.RegisterCallback<ChangeEvent<string>>(PropDescriptionChanged);

            ObjectField PropIconField = uiElementsRoot.Q<ObjectField>(BasisSDKConstants.PropIcon);
            PropIconField.objectType = typeof(Texture2D);
            PropIconField.allowSceneObjects = true;
            PropIconField.value = BasisProp.BasisBundleDescription.AssetBundleIcon;
            PropIconField.RegisterCallback<ChangeEvent<UnityEngine.Object>>(OnIconFieldChanged);

            BasisSDKCommonInspector.CreateContentTagsFoldout(uiElementsRoot, BasisProp);
            BasisSDKCommonInspector.CreateBuildTargetOptions(uiElementsRoot);
            BasisSDKCommonInspector.CreateBuildOptionsDropdown(uiElementsRoot);

            BasisAssetBundleObject assetBundleObject = AssetDatabase.LoadAssetAtPath<BasisAssetBundleObject>(BasisAssetBundleObject.AssetBundleObject);
            Button BuildButton = BasisHelpersGizmo.Button(uiElementsRoot, BasisSDKConstants.BuildButton);
            BuildButton.clicked += () => Build(BuildButton, assetBundleObject.selectedTargets, BasisProp.BasisBundleDescription.AssetBundleIcon);

            Button PropTestInEditorClick = BasisHelpersGizmo.Button(uiElementsRoot, BasisSDKConstants.PropTestInEditor);
            PropTestInEditorClick.clicked += PropTestInEditorClickFunction;
        }
        else
        {
            Debug.LogError("VisualTree is null. Make sure the UXML file is assigned correctly.");
        }

        return rootElement;
    }

    public void PropTestInEditorClickFunction()
    {
#if BASIS_FRAMEWORK_EXISTS
        if (!Application.isPlaying)
        {
            bool result = EditorUtility.DisplayDialog(
                BasisEditorLocalization.Get("sdk.common.dialog.confirm"),
                BasisEditorLocalization.Get("sdk.prop.testInEditor.confirm.body"),
                BasisEditorLocalization.Get("sdk.common.dialog.yes"),
                BasisEditorLocalization.Get("sdk.common.dialog.no"));
            if (result)
            {
                SetPendingTestInEditorPropId(GlobalObjectId.GetGlobalObjectIdSlow(BasisProp).ToString());
                EditorApplication.EnterPlaymode();
            }
        }
        else
        {
            RequestPropLoad();
        }
#else
        RequestPropLoad();
#endif
    }

    public void RequestPropLoad()
    {
        RequestPropLoad(BasisProp);
    }

    private static void RequestPropLoad(BasisProp prop)
    {
#if BASIS_FRAMEWORK_EXISTS
        if (BasisLocalPlayerData.PlayerReady)
        {
            LoadProp(prop);
        }
        else
        {
            ScheduledTestInEditorProp = prop;
            BasisLocalPlayerData.OnLocalPlayerInitialized -= LoadScheduledProp;
            BasisLocalPlayerData.OnLocalPlayerInitialized += LoadScheduledProp;
        }
#else
        LoadProp(prop);
#endif
    }

    private static void LoadScheduledProp()
    {
#if BASIS_FRAMEWORK_EXISTS
        BasisLocalPlayerData.OnLocalPlayerInitialized -= LoadScheduledProp;
        if (ScheduledTestInEditorProp == null)
        {
            return;
        }

        BasisProp prop = ScheduledTestInEditorProp;
        ScheduledTestInEditorProp = null;
        LoadProp(prop);
#endif
    }

    private static void LoadProp(BasisProp prop)
    {
        GameObject clone = UnityEngine.Object.Instantiate(prop.gameObject);
        clone.name = prop.gameObject.name;
        BasisAssetBundlePipeline.DestroyEditorOnlyInAvatar(clone);
        OnBeforeTestInEditor?.Invoke(clone);

        Vector3 position = ResolveSpawnPosition(out Quaternion rotation);
        clone.transform.SetPositionAndRotation(position, rotation);
        clone.transform.SetParent(null, true);
    }

    private static Vector3 ResolveSpawnPosition(out Quaternion rotation)
    {
        Transform reference = null;
        if (Application.isPlaying && Camera.main != null)
        {
            reference = Camera.main.transform;
        }
        else if (SceneView.lastActiveSceneView != null && SceneView.lastActiveSceneView.camera != null)
        {
            reference = SceneView.lastActiveSceneView.camera.transform;
        }

        if (reference != null)
        {
            rotation = Quaternion.Euler(0f, reference.eulerAngles.y, 0f);
            return reference.position + reference.forward * 2f;
        }

        rotation = Quaternion.identity;
        return Vector3.zero;
    }

    private void OnIconFieldChanged(ChangeEvent<UnityEngine.Object> evt)
    {
        BasisProp.BasisBundleDescription.AssetBundleIcon = evt.newValue as Texture2D;
        EditorUtility.SetDirty(BasisProp);
        BasisDebug.Log($"Setting to {BasisProp.BasisBundleDescription.AssetBundleIcon}");
    }

    private void PropNameChanged(ChangeEvent<string> evt)
    {
        BasisProp.BasisBundleDescription.AssetBundleName = evt.newValue;
        EditorUtility.SetDirty(BasisProp);
    }

    private void PropDescriptionChanged(ChangeEvent<string> evt)
    {
        BasisProp.BasisBundleDescription.AssetBundleDescription = evt.newValue;
        EditorUtility.SetDirty(BasisProp);
    }

    private async void Build(Button buildButton, List<BuildTarget> targets, Texture2D Image)
    {
        if (targets == null || targets.Count == 0)
        {
            Debug.LogError("No build targets selected.");
            return;
        }

        if (BasisPropValidator.ValidateProp(out List<BasisValidationIssue> errors, out List<BasisValidationIssue> suggestions, out List<string> passes))
        {
            if (Image == null)
            {
                Image = AssetPreview.GetAssetPreview(BasisProp.gameObject);
            }
            string ImageBytes = null;
            if (Image != null)
            {
                ImageBytes = BasisTextureCompression.ToPngBytes(Image);
            }
            Debug.Log($"Building Prop Bundles for: {string.Join(", ", targets.ConvertAll(t => BasisSDKConstants.targetDisplayNames[t]))}");
            BasisAssetBundleObject assetBundleObject = AssetDatabase.LoadAssetAtPath<BasisAssetBundleObject>(BasisAssetBundleObject.AssetBundleObject);
            (bool success, string message) = await BasisBundleBuild.GameObjectBundleBuild(ImageBytes, BasisProp, targets, assetBundleObject.UseCustomPassword, assetBundleObject.UserSelectedPassword);
            EditorUtility.ClearProgressBar();
            ClearResultLabel();

            resultLabel = new Label
            {
                style = { fontSize = 14 }
            };

            if (success)
            {
                resultLabel.text = BasisEditorLocalization.Get("sdk.common.build.success");
                resultLabel.style.backgroundColor = Color.green;
                resultLabel.style.color = Color.black;
            }
            else
            {
                resultLabel.text = BasisEditorLocalization.Get("sdk.common.build.failed", message);
                resultLabel.style.backgroundColor = Color.red;
                resultLabel.style.color = Color.black;
            }

            uiElementsRoot.Add(resultLabel);
        }
        else
        {
            if (!EditorUtility.DisplayDialog(
                    BasisEditorLocalization.Get("sdk.prop.buildError.title"),
                    BasisEditorLocalization.Get("sdk.prop.buildError.body", string.Join("\n", errors.ConvertAll(e => e.Message))),
                    BasisEditorLocalization.Get("sdk.common.dialog.ok"),
                    BasisEditorLocalization.Get("sdk.common.dialog.openDocumentation")))
            {
                Application.OpenURL(BasisSDKConstants.PropDocumentationURL);
            }
        }
    }

    private void ClearResultLabel()
    {
        if (resultLabel != null)
        {
            uiElementsRoot.Remove(resultLabel);
            resultLabel = null;
        }
    }

    public Button DocumentationButton(VisualElement rootElement, string Text)
    {
        Button fixMeButton = new Button();
        fixMeButton.text = Text;

        Color backgroundColor = new Color(0.5f, 0.5f, 0.5f, 1f);

        fixMeButton.style.backgroundColor = new StyleColor(backgroundColor);
        fixMeButton.style.color = new StyleColor(Color.white);
        fixMeButton.style.fontSize = 14;
        fixMeButton.style.unityFontStyleAndWeight = FontStyle.Bold;
        fixMeButton.style.paddingTop = 6;
        fixMeButton.style.paddingBottom = 6;
        fixMeButton.style.paddingLeft = 12;
        fixMeButton.style.paddingRight = 12;
        fixMeButton.style.marginBottom = 10;
        fixMeButton.style.borderTopLeftRadius = 8;
        fixMeButton.style.borderTopRightRadius = 8;
        fixMeButton.style.borderBottomLeftRadius = 8;
        fixMeButton.style.borderBottomRightRadius = 8;
        fixMeButton.style.borderLeftWidth = 0;
        fixMeButton.style.borderRightWidth = 0;
        fixMeButton.style.borderTopWidth = 0;
        fixMeButton.style.borderBottomWidth = 3;
        fixMeButton.style.unityTextAlign = TextAnchor.MiddleCenter;
        fixMeButton.style.alignSelf = Align.Auto;

        fixMeButton.RegisterCallback<MouseEnterEvent>(evt =>
        {
            fixMeButton.style.backgroundColor = new StyleColor(new Color(0.4f, 0.4f, 0.4f, 1f));
        });
        fixMeButton.RegisterCallback<MouseLeaveEvent>(evt =>
        {
            fixMeButton.style.backgroundColor = new StyleColor(backgroundColor);
        });

        rootElement.Add(fixMeButton);
        return fixMeButton;
    }
}
