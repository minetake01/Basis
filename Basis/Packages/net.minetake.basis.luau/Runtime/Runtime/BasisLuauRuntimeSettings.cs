using UnityEngine;

namespace Minetake.Basis.Luau.Runtime
{
    [CreateAssetMenu(fileName = "BasisLuauRuntimeSettings", menuName = "Basis/Luau Runtime Settings")]
    public sealed class BasisLuauRuntimeSettings : ScriptableObject
    {
        public static BasisLuauRuntimeSettings Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetInstance() => Instance = null;

        [Header("Security")]
        [Tooltip("Editor/Development only. Never enable in release player builds.")]
        public bool allowUnsignedBytecodeInDev = true;

        [Tooltip("Require signed bytecode in all builds when runtime bridge is active.")]
        public bool requireSignedBytecode = true;

        public int maxBytecodeBytes = 512 * 1024;
        public int maxCommandBytesPerSlice = 256 * 1024;
        public int maxCommandsPerSlice = 8192;
        public int maxBufferBytesPerHost = 4 * 1024 * 1024;
        public int maxBuffersPerHost = 256;
        public int maxPayloadBytes = 1024 * 1024;

        [Header("Workers")]
        public bool useWorkerScheduler = false;
        public int maxWorkers = 4;

        public static BasisLuauRuntimeSettings GetOrCreate()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var loaded = Resources.Load<BasisLuauRuntimeSettings>("BasisLuauRuntimeSettings");
            Instance = loaded != null ? loaded : CreateInstance<BasisLuauRuntimeSettings>();
            return Instance;
        }

        public bool MayLoadUnsignedBytecode()
        {
#if BASIS_LUAU_DEV_BRIDGE
            return allowUnsignedBytecodeInDev && Debug.isDebugBuild;
#else
            return false;
#endif
        }
    }
}
