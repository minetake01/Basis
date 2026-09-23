#if BASIS_FRAMEWORK_EXISTS
using Basis.BasisUI;
using Basis.Scripts.Device_Management;
using UnityEngine;

namespace Net.Minetake.Basis.Cognitive3D
{
    /// <summary>
    /// Injects Cognitive3D settings and status readout into the Basis Developer settings tab.
    /// Follows the established SettingsProvider extension pattern.
    /// </summary>
    public static class SettingsProviderCognitive3D
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Register()
        {
            BasisDeviceManagement.mainThreadActions.Enqueue(() =>
            {
                SettingsProvider.DeveloperSectionBuilders.Add(BuildSection);
            });
        }

        private static void BuildSection(RectTransform parent)
        {
            PanelSectionToggle sectionToggle = PanelSectionToggle.CreateNewEntry(parent);
            sectionToggle.SetTitle("Cognitive3D Spatial Analytics");

            PanelElementDescriptor group = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, parent);
            group.SetDescription("Spatial analytics, fixation eye-tracking telemetry, and session recording.");
            var content = group.ContentParent;

            // Global Enable Toggle
            PanelToggle enableToggle = PanelToggle.CreateNewEntry(content);
            enableToggle.Descriptor.SetTitle("Enable Cognitive3D");
            enableToggle.Descriptor.SetDescription("Enables local session tracking and telemetry capture.");
            enableToggle.SetValueWithoutNotify(BasisCognitive3DSettings.Enable.RawValue);
            enableToggle.OnValueChanged += value => BasisCognitive3DSettings.Enable.SetValue(value);

            // User Opt-Out Toggle (Privacy)
            PanelToggle optOutToggle = PanelToggle.CreateNewEntry(content);
            optOutToggle.Descriptor.SetTitle("Opt Out (Do Not Track)");
            optOutToggle.Descriptor.SetDescription("Explicitly drop and discard all telemetry events.");
            optOutToggle.SetValueWithoutNotify(BasisCognitive3DSettings.OptOut.RawValue);
            optOutToggle.OnValueChanged += value => BasisCognitive3DSettings.OptOut.SetValue(value);

            // Audio Recording Consent Toggle
            PanelToggle audioToggle = PanelToggle.CreateNewEntry(content);
            audioToggle.Descriptor.SetTitle("Record Voice Audio");
            audioToggle.Descriptor.SetDescription("Enables microphone audio sync for session replay transcriptions.");
            audioToggle.SetValueWithoutNotify(BasisCognitive3DSettings.RecordAudio.RawValue);
            audioToggle.OnValueChanged += value => BasisCognitive3DSettings.RecordAudio.SetValue(value);

            // Participant ID Display / Status
            PanelElementDescriptor participantGroup = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Default, content);
            string participantId = BasisCognitive3DSettings.GetOrCreateParticipantId();
            participantGroup.SetTitle("Participant UUID");
            participantGroup.SetDescription(participantId);
        }
    }
}
#endif
