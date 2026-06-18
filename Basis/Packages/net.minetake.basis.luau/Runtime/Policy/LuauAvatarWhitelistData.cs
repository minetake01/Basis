using System;
using System.Collections.Generic;
using System.Reflection;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking.NetworkedAvatar;
using HVR.Vixxy;
using UnityEngine;
using UnityEngine.AI;

namespace Minetake.Basis.Luau.Policy
{
    internal static partial class LuauAvatarWhitelistData
    {
        internal static readonly HashSet<string> ExtraWhiteListType = new HashSet<string>()
        {

			// Avatar-specific Basis shim types
			"Minetake.Basis.Luau.Services.BasisNet*", // Restrictive, only used as a type and for events.
			"Minetake.Basis.Luau.Services.BasisLuauAvatarBridge",
			"Minetake.Basis.Luau.Services.BasisLuauAvatarBridge+OnReady",
			"Minetake.Basis.Luau.Services.BasisLuauAvatarBridge+AvatarReadyEvent",
			"Minetake.Basis.Luau.Services.BasisLuauInstantiateService",
			"Minetake.Basis.Luau.Services.BasisLuauDebug",

			// HVR Vixxy
			"HVR.Vixxy.HVRVixxyMenuItem", // Restrictive, see method whitelist.
		
        };

        internal static readonly HashSet<string> ExtraWhiteListFields = new HashSet<string>()
        {

			"Minetake.Basis.Luau.Services.BasisLuauAvatarBridge.Animator",
			"Minetake.Basis.Luau.Services.BasisLuauAvatarBridge.FaceVisemeMesh",
			"Minetake.Basis.Luau.Services.BasisLuauAvatarBridge.FaceBlinkMesh",
			"Minetake.Basis.Luau.Services.BasisLuauAvatarBridge.AvatarEyePosition",
			"Minetake.Basis.Luau.Services.BasisLuauAvatarBridge.AvatarMouthPosition",
			"Minetake.Basis.Luau.Services.BasisLuauAvatarBridge.FaceVisemeMovement",
			"Minetake.Basis.Luau.Services.BasisLuauAvatarBridge.BlinkViseme",
			"Minetake.Basis.Luau.Services.BasisLuauAvatarBridge.laughterBlendTarget",
			"Minetake.Basis.Luau.Services.BasisLuauAvatarBridge.AnimatorHumanScale",
			"Minetake.Basis.Luau.Services.BasisLuauAvatarBridge.IsOwnedLocally",
			"Minetake.Basis.Luau.Services.BasisLuauAvatarBridge.HumanScale",
			"Basis.Scripts.BasisSdk.BasisProcessingAvatarOptions.doNotAutoRenameBones",
		
        };

        internal static readonly Dictionary<Type, HashSet<string>> ExtraMethodWhitelist = new Dictionary<Type, HashSet<string>>()
        {

			{ typeof(UnityEngine.GameObject), new HashSet<string>{
				typeof(GameObject).GetProperty(nameof(GameObject.transform)).GetGetMethod().Name,
				typeof(GameObject).GetProperty(nameof(GameObject.activeSelf)).GetGetMethod().Name,
				typeof(GameObject).GetProperty(nameof(GameObject.activeInHierarchy)).GetGetMethod().Name,
				typeof(GameObject).GetProperty(nameof(GameObject.layer)).GetGetMethod().Name,
				} },
			{ typeof(UnityEngine.LayerMask), new HashSet<string>{
				".ctor",
				"get_value",
				"op_Implicit",
				} },
			{ typeof(HVRVixxyMenuItem), new HashSet<string>{
				nameof(HVRVixxyMenuItem.GetValue),
				nameof(HVRVixxyMenuItem.ApplyValue),
				} },
        };
    }
}
