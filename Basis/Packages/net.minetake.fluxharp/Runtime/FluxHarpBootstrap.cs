using Basis.Scripts.BasisSdk.Players;
using UnityEngine;

namespace Net.Minetake.FluxHarp
{
    /// <summary>
    /// The instrument is part of the client: it is there as soon as you are, wherever you are, and the room's
    /// expression arrives with the room. The session starts at load so edits from others are received even
    /// before the instrument is drawn.
    /// </summary>
    public static class FluxHarpBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            FluxHarpNetwork.Initialize();
            BasisLocalPlayer.OnLocalPlayerInitialized -= SpawnWhenReady;
            if (BasisLocalPlayer.PlayerReady) SpawnWhenReady();
            else BasisLocalPlayer.OnLocalPlayerInitialized += SpawnWhenReady;
        }

        private static void SpawnWhenReady()
        {
            BasisLocalPlayer.OnLocalPlayerInitialized -= SpawnWhenReady;
            FluxHarpInstrument.Spawn().PlaceOnArrival();
            BasisLocalPlayer.OnTeleportEvent -= PlaceWhereYouArrive;
            BasisLocalPlayer.OnTeleportEvent += PlaceWhereYouArrive;
        }

        private static void PlaceWhereYouArrive() => FluxHarpInstrument.Instance?.PlaceOnArrival();
    }
}
