#if BASIS_FRAMEWORK_EXISTS
using System;
using Basis.Scripts.Settings;

namespace Net.Minetake.Basis.Cognitive3D
{
    /// <summary>
    /// Persistent settings for Cognitive3D integration in Basis VR.
    /// Manages enable state, user opt-out, audio recording consent, and custom credentials.
    /// </summary>
    public static class BasisCognitive3DSettings
    {
        static BasisCognitive3DSettings()
        {
            BasisSettingsBindingPostLoad.Register(typeof(BasisCognitive3DSettings));
        }

        /// <summary>
        /// Global enable toggle for Cognitive3D tracking and telemetry.
        /// </summary>
        public static readonly BasisSettingsBinding<bool> Enable =
            new BasisSettingsBinding<bool>("cognitive3d_enable", new BasisPlatformDefault<bool>(true));

        /// <summary>
        /// Explicit user opt-out for telemetry data collection (GDPR/privacy compliance).
        /// When true, all tracking events are immediately discarded.
        /// </summary>
        public static readonly BasisSettingsBinding<bool> OptOut =
            new BasisSettingsBinding<bool>("cognitive3d_optout", new BasisPlatformDefault<bool>(false));

        /// <summary>
        /// Microphone audio recording and speech-to-text transcript consent.
        /// Disabled by default for privacy.
        /// </summary>
        public static readonly BasisSettingsBinding<bool> RecordAudio =
            new BasisSettingsBinding<bool>("cognitive3d_record_audio", new BasisPlatformDefault<bool>(false));

        /// <summary>
        /// Optional custom Application Key overriding the baked-in asset configuration.
        /// </summary>
        public static readonly BasisSettingsBinding<string> CustomApiKey =
            new BasisSettingsBinding<string>("cognitive3d_custom_api_key", new BasisPlatformDefault<string>(string.Empty));

        /// <summary>
        /// Persistent pseudonymous participant UUID for session tracking without PII.
        /// </summary>
        public static readonly BasisSettingsBinding<string> ParticipantId =
            new BasisSettingsBinding<string>("cognitive3d_participant_id", new BasisPlatformDefault<string>(string.Empty));

        /// <summary>
        /// Returns the persisted anonymous participant ID, generating and persisting a new UUID if empty.
        /// </summary>
        public static string GetOrCreateParticipantId()
        {
            string id = ParticipantId.RawValue;
            if (string.IsNullOrWhiteSpace(id))
            {
                id = Guid.NewGuid().ToString("D");
                ParticipantId.SetValue(id);
            }
            return id;
        }

        /// <summary>
        /// Checks whether telemetry transmission is actively permitted.
        /// </summary>
        public static bool IsTrackingAllowed => Enable.RawValue && !OptOut.RawValue;
    }
}
#endif
