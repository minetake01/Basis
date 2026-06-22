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
        public int maxBytecodeBytes = 512 * 1024;
        public int maxCommandBytesPerSlice = 256 * 1024;
        public int maxCommandsPerSlice = 8192;
        public int maxBufferBytesPerHost = 4 * 1024 * 1024;
        public int maxBuffersPerHost = 256;
        public int maxPayloadBytes = 1024 * 1024;

        [Header("Workers")]
        public int maxWorkers = 0;

        [Header("Dev")]
        public bool allowUnsignedBytecodeInDev = true;

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

        public bool AllowsUnsignedBytecodeInDev()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return allowUnsignedBytecodeInDev;
#else
            return false;
#endif
        }
    }
}
