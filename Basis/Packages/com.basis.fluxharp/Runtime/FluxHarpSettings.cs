using Basis;
using Basis.Scripts.Settings;
using Unity.Scripting.LifecycleManagement;

namespace Basis.FluxHarp
{
    /// <summary>The listener's own settings for the instrument. Never shared: this is how loud it is for you.</summary>
    [NoAutoStaticsCleanup]
    public static class FluxHarpSettings
    {
        static FluxHarpSettings()
        {
            BasisSettingsBindingPostLoad.Register(typeof(FluxHarpSettings));
        }

        /// <summary>Instrument volume, 0–100, applied on top of the master volume.</summary>
        public static BasisSettingsBinding<float> Volume = new("fluxharpvolume", new BasisPlatformDefault<float>(70));

        /// <summary>Linear gain of <see cref="Volume"/>, perceptually tapered.</summary>
        public static float Gain
        {
            get
            {
                float v = UnityEngine.Mathf.Clamp01(Volume.RawValue / 100f);
                return v * v;
            }
        }
    }
}
