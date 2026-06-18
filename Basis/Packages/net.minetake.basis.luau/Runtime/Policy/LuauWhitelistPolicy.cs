using System;
using System.Collections.Generic;
using System.Reflection;
using Basis.Scripts.BasisSdk;
using Minetake.Basis.Luau.Services;
using UnityEngine;

namespace Minetake.Basis.Luau.Policy
{
    public abstract class LuauWhitelistPolicy
    {
        protected abstract HashSet<string> ExtraWhiteListType { get; }
        protected abstract HashSet<string> ExtraWhiteListFields { get; }
        protected abstract Dictionary<Type, HashSet<string>> ExtraMethodWhitelist { get; }

        public LuauHostKind HostKind { get; }

        protected LuauWhitelistPolicy(LuauHostKind hostKind)
        {
            HostKind = hostKind;
        }

        public bool CheckTypeAllowed(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return false;
            }

            if (LuauWhitelistCommonData.CommonWhiteListType.Contains(typeName))
            {
                return true;
            }

            if (ExtraWhiteListType.Contains(typeName))
            {
                return true;
            }

            foreach (string allowedType in LuauWhitelistCommonData.CommonWhiteListType)
            {
                if (MatchesWildcard(allowedType, typeName))
                {
                    return true;
                }
            }

            foreach (string allowedType in ExtraWhiteListType)
            {
                if (MatchesWildcard(allowedType, typeName))
                {
                    return true;
                }
            }

            return false;
        }

        public bool CheckFieldAllowed(string typeName, string fieldName)
        {
            if (!CheckTypeAllowed(typeName))
            {
                return false;
            }

            string fullField = typeName + "." + fieldName;
            if (LuauWhitelistCommonData.CommonWhiteListFields.Contains(fullField))
            {
                return true;
            }

            if (ExtraWhiteListFields.Contains(fullField))
            {
                return true;
            }

            foreach (string allowedField in LuauWhitelistCommonData.CommonWhiteListFields)
            {
                if (MatchesWildcard(allowedField, fullField))
                {
                    return true;
                }
            }

            foreach (string allowedField in ExtraWhiteListFields)
            {
                if (MatchesWildcard(allowedField, fullField))
                {
                    return true;
                }
            }

            return false;
        }

        public bool CheckPropertyAllowed(Type declaringType, string propertyName)
        {
            if (declaringType == null || string.IsNullOrEmpty(propertyName))
            {
                return false;
            }

            string getter = "get_" + propertyName;
            string setter = "set_" + propertyName;
            return CheckMethodAllowed(declaringType, getter) || CheckMethodAllowed(declaringType, setter);
        }

        public bool CheckMethodAllowed(Type declaringType, string methodName)
        {
            if (declaringType == null || string.IsNullOrEmpty(methodName))
            {
                return false;
            }

            if (methodName.Contains("Invoke", StringComparison.Ordinal))
            {
                return false;
            }

            if (declaringType == typeof(Application) && IsBlockedApplicationMethod(methodName))
            {
                return false;
            }

            if (declaringType == typeof(UnityEngine.Object) &&
                (methodName == nameof(UnityEngine.Object.Instantiate) || methodName == "InstantiateAsync"))
            {
                return false;
            }

            if (declaringType == typeof(GameObject) && (
                methodName == nameof(GameObject.AddComponent) ||
                methodName == "SendMessage" ||
                methodName == "SendMessageUpwards" ||
                methodName == "BroadcastMessage"))
            {
                return false;
            }

            if (declaringType == typeof(Component) && (
                methodName == "SendMessage" ||
                methodName == "SendMessageUpwards" ||
                methodName == "BroadcastMessage"))
            {
                return false;
            }

            if (declaringType == typeof(Animator) && (
                methodName == "GetBehaviour" ||
                methodName == "GetBehaviours"))
            {
                return false;
            }

            bool inCommon = LuauWhitelistCommonData.CommonMethodWhitelist.TryGetValue(declaringType, out HashSet<string> commonAllowed);
            bool inExtra = ExtraMethodWhitelist.TryGetValue(declaringType, out HashSet<string> extraAllowed);
            if (inCommon || inExtra)
            {
                bool allowed = (inCommon && commonAllowed.Contains(methodName)) ||
                               (inExtra && extraAllowed.Contains(methodName));
                if (!allowed)
                {
                    return false;
                }
            }

            return true;
        }

        public bool TryGetTypeOverride(string typeName, out Type runtimeType)
        {
            runtimeType = null;
            if (TryExtraGetTypeOverride(typeName, out runtimeType))
            {
                return true;
            }

            switch (typeName)
            {
                case "Minetake.Basis.Luau.Services.BasisNetworkShim":
                case "Basis.Shims.BasisNetworkShim":
                case "Basis.BasisNetworkShim":
                    runtimeType = typeof(BasisNetworkShim);
                    return true;
                case "Minetake.Basis.Luau.Services.BasisNetworkShim+NetworkReadyEvent":
                case "Basis.Shims.BasisNetworkShim+NetworkReadyEvent":
                    runtimeType = typeof(BasisNetworkShim.NetworkReadyEvent);
                    return true;
                case "Minetake.Basis.Luau.Services.BasisNetworkShim+ServerOwnershipDestroyedEvent":
                case "Basis.Shims.BasisNetworkShim+ServerOwnershipDestroyedEvent":
                    runtimeType = typeof(BasisNetworkShim.ServerOwnershipDestroyedEvent);
                    return true;
                case "Minetake.Basis.Luau.Services.BasisNetworkShim+OwnershipTransferEvent":
                case "Basis.Shims.BasisNetworkShim+OwnershipTransferEvent":
                    runtimeType = typeof(BasisNetworkShim.OwnershipTransferEvent);
                    return true;
                case "Minetake.Basis.Luau.Services.BasisNetworkShim+NetworkMessageEvent":
                case "Basis.Shims.BasisNetworkShim+NetworkMessageEvent":
                    runtimeType = typeof(BasisNetworkShim.NetworkMessageEvent);
                    return true;
                case "Minetake.Basis.Luau.Services.BasisNetworkShim+PlayerJoinedEvent":
                case "Basis.Shims.BasisNetworkShim+PlayerJoinedEvent":
                    runtimeType = typeof(BasisNetworkShim.PlayerJoinedEvent);
                    return true;
                case "Minetake.Basis.Luau.Services.BasisNetworkShim+PlayerLeftEvent":
                case "Basis.Shims.BasisNetworkShim+PlayerLeftEvent":
                    runtimeType = typeof(BasisNetworkShim.PlayerLeftEvent);
                    return true;
                case "UnityEngine.Video.VideoPlayer":
                    runtimeType = typeof(BasisLuauVideoPlayer);
                    return true;
                case "UnityEngine.Video.VideoPlayer+ErrorEventHandler":
                    runtimeType = typeof(BasisLuauVideoPlayer.ErrorEventHandlerShim);
                    return true;
                case "UnityEngine.Video.VideoPlayer+EventHandler":
                    runtimeType = typeof(BasisLuauVideoPlayer.EventHandlerShim);
                    return true;
                case "UnityEngine.Video.VideoPlayer+FrameReadyEventHandler":
                    runtimeType = typeof(BasisLuauVideoPlayer.FrameReadyEventHandlerShim);
                    return true;
                case "UnityEngine.Video.VideoPlayer+TimeEventHandler":
                    runtimeType = typeof(BasisLuauVideoPlayer.TimeEventHandlerShim);
                    return true;
                case "UnityEngine.Debug":
                    runtimeType = typeof(BasisLuauDebug);
                    return true;
                default:
                    return false;
            }
        }

        public Type ResolveType(string typeName)
        {
            if (TryGetTypeOverride(typeName, out Type overrideType))
            {
                return overrideType;
            }

            if (!CheckTypeAllowed(typeName))
            {
                return null;
            }

            return Type.GetType(typeName + ", UnityEngine.CoreModule") ??
                   Type.GetType(typeName + ", UnityEngine.PhysicsModule") ??
                   Type.GetType(typeName + ", UnityEngine.AIModule") ??
                   Type.GetType(typeName + ", Unity.TextMeshPro") ??
                   Type.GetType(typeName + ", UnityEngine.UI") ??
                   Type.GetType(typeName + ", UnityEngine") ??
                   Type.GetType(typeName + ", mscorlib") ??
                   Type.GetType(typeName);
        }

        protected virtual bool TryExtraGetTypeOverride(string typeName, out Type runtimeType)
        {
            runtimeType = null;
            return false;
        }

        static bool IsBlockedApplicationMethod(string name) =>
            name == nameof(Application.OpenURL) ||
            name == nameof(Application.Quit) ||
            name == "Unload" ||
            name == "CanStreamedLevelBeLoaded" ||
            name == "ExternalCall" ||
            name == "ExternalEval" ||
            name == "GetBuildTags" ||
            name == "RequestUserAuthorization" ||
            name == "SetBuildTags" ||
            name == "SetStackTraceLogType" ||
            name.StartsWith("Load", StringComparison.Ordinal);

        static bool MatchesWildcard(string pattern, string target)
        {
            if (!pattern.Contains('*', StringComparison.Ordinal))
            {
                return false;
            }

            string[] parts = pattern.Split('*');
            return target.StartsWith(parts[0], StringComparison.Ordinal) &&
                   target.EndsWith(parts[1], StringComparison.Ordinal);
        }

        public static LuauWhitelistPolicy ForHost(LuauHostKind kind) => kind switch
        {
            LuauHostKind.Avatar => LuauAvatarWhitelistPolicy.Instance,
            LuauHostKind.Prop => LuauPropWhitelistPolicy.Instance,
            LuauHostKind.Scene => LuauSceneWhitelistPolicy.Instance,
            _ => LuauPropWhitelistPolicy.Instance,
        };
    }

    public sealed class LuauAvatarWhitelistPolicy : LuauWhitelistPolicy
    {
        public static readonly LuauAvatarWhitelistPolicy Instance = new();

        LuauAvatarWhitelistPolicy() : base(LuauHostKind.Avatar) { }

        protected override HashSet<string> ExtraWhiteListType => LuauAvatarWhitelistData.ExtraWhiteListType;
        protected override HashSet<string> ExtraWhiteListFields => LuauAvatarWhitelistData.ExtraWhiteListFields;
        protected override Dictionary<Type, HashSet<string>> ExtraMethodWhitelist => LuauAvatarWhitelistData.ExtraMethodWhitelist;

        protected override bool TryExtraGetTypeOverride(string typeName, out Type runtimeType)
        {
            switch (typeName)
            {
                case "Basis.Scripts.BasisSdk.BasisAvatar":
                    runtimeType = typeof(BasisLuauAvatarBridge);
                    return true;
                case "Basis.Scripts.BasisSdk.BasisAvatar+OnReady":
                    runtimeType = typeof(BasisLuauAvatarBridge.OnReady);
                    return true;
                default:
                    runtimeType = null;
                    return false;
            }
        }
    }

    public sealed class LuauPropWhitelistPolicy : LuauWhitelistPolicy
    {
        public static readonly LuauPropWhitelistPolicy Instance = new();

        LuauPropWhitelistPolicy() : base(LuauHostKind.Prop) { }

        protected override HashSet<string> ExtraWhiteListType => LuauPropWhitelistData.ExtraWhiteListType;
        protected override HashSet<string> ExtraWhiteListFields => LuauPropWhitelistData.ExtraWhiteListFields;
        protected override Dictionary<Type, HashSet<string>> ExtraMethodWhitelist => LuauPropWhitelistData.ExtraMethodWhitelist;
    }

    public sealed class LuauSceneWhitelistPolicy : LuauWhitelistPolicy
    {
        public static readonly LuauSceneWhitelistPolicy Instance = new();

        LuauSceneWhitelistPolicy() : base(LuauHostKind.Scene) { }

        protected override HashSet<string> ExtraWhiteListType => LuauSceneWhitelistData.ExtraWhiteListType;
        protected override HashSet<string> ExtraWhiteListFields => LuauSceneWhitelistData.ExtraWhiteListFields;
        protected override Dictionary<Type, HashSet<string>> ExtraMethodWhitelist => LuauSceneWhitelistData.ExtraMethodWhitelist;
    }
}
