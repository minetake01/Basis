#if BASIS_FRAMEWORK_EXISTS
using System;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Networking;
using UnityEngine;

#if COGNITIVE3D_EXISTS
using Cognitive3D;
#endif

namespace Net.Minetake.Basis.Cognitive3D
{
    /// <summary>
    /// Lifecycle bridge binding Cognitive3D to Basis VR initialization and network sessions.
    /// Manages manager bootstrap, LobbyId synchronization with multiplayer rooms, and teardown flushing.
    /// </summary>
    public static class BasisCognitive3DBridge
    {
        private static bool _initialized;
        private static bool _hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_hooked) return;
            _hooked = true;

            BasisDeviceManagement.OnInitializationCompleted += HandleDeviceManagementInitialized;
            Application.quitting += HandleApplicationQuitting;
        }

        private static void HandleDeviceManagementInitialized()
        {
            if (_initialized) return;

            if (!BasisCognitive3DSettings.IsTrackingAllowed)
            {
                BasisDebug.Log("Cognitive3D integration disabled by settings or user opt-out.", BasisDebug.LogTag.Normal);
                return;
            }

            InitializeManager();
            HookNetworkEvents();
            _initialized = true;
        }

        private static void InitializeManager()
        {
#if COGNITIVE3D_EXISTS
            try
            {
                if (Cognitive3D_Manager.Instance == null)
                {
                    GameObject managerGo = new GameObject("[Cognitive3D_Manager]");
                    UnityEngine.Object.DontDestroyOnLoad(managerGo);
                    Cognitive3D_Manager manager = managerGo.AddComponent<Cognitive3D_Manager>();

                    // Configure persistent anonymous participant ID
                    string participantId = BasisCognitive3DSettings.GetOrCreateParticipantId();
                    manager.SetParticipantId(participantId);

                    BasisDebug.Log($"Cognitive3D initialized successfully with ParticipantId: {participantId}", BasisDebug.LogTag.Normal);
                }
            }
            catch (Exception ex)
            {
                BasisDebug.LogError($"Failed to initialize Cognitive3D_Manager: {ex.Message}", BasisDebug.LogTag.Normal);
            }
#else
            BasisDebug.Log("Cognitive3D Unity SDK (com.cognitive3d.c3d-sdk) not present in project; bridge idle.", BasisDebug.LogTag.Normal);
#endif
        }

        private static void HookNetworkEvents()
        {
            BasisNetworkPlayer.OnLocalPlayerJoined += HandleLocalPlayerJoined;
            BasisNetworkPlayer.OnLocalPlayerLeft += HandleLocalPlayerLeft;
        }

        private static void HandleLocalPlayerJoined(BasisNetworkPlayer netPlayer, Basis.Scripts.BasisSdk.Players.BasisLocalPlayer localPlayer)
        {
#if COGNITIVE3D_EXISTS
            if (!BasisCognitive3DSettings.IsTrackingAllowed) return;

            // Generate lobby identifier from connected server host and port
            string lobbyId = $"{BasisNetworkManagement.Ip}:{BasisNetworkManagement.Port}";
            Cognitive3D_Manager.SetLobbyId(lobbyId);
            BasisDebug.Log($"Cognitive3D LobbyId set to: {lobbyId}", BasisDebug.LogTag.Networking);
#endif
        }

        private static void HandleLocalPlayerLeft(BasisNetworkPlayer netPlayer, Basis.Scripts.BasisSdk.Players.BasisLocalPlayer localPlayer)
        {
#if COGNITIVE3D_EXISTS
            // Flush buffered events upon leaving a network room
            Cognitive3D_Manager.Instance?.FlushData();
#endif
        }

        private static void HandleApplicationQuitting()
        {
#if COGNITIVE3D_EXISTS
            if (_initialized && Cognitive3D_Manager.Instance != null)
            {
                Cognitive3D_Manager.Instance.EndSession();
            }
#endif
        }
    }
}
#endif
