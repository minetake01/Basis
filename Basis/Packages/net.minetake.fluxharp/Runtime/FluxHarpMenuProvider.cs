using Basis.BasisUI;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace Net.Minetake.FluxHarp
{
    /// <summary>
    /// The instrument's only presence in the menu: one entry that opens a panel with what the space itself
    /// should not carry — your volume, bringing the instrument to you, and the pieces to start from.
    /// </summary>
    [AutoStaticsCleanup]
    public sealed partial class FluxHarpMenuProvider : BasisMenuActionProvider<BasisMainMenu>
    {
        public const string StaticTitle = "Flux Harp";
        private static FluxHarpMenuProvider _instance;

        public override string Title => StaticTitle;
        public override string IconAddress => AddressableAssets.Sprites.World;
        public override int Order => 12;
        public override bool Hidden => false;

        [RuntimeInitializeOnLoadMethod]
        public static void AddToMenu()
        {
            _instance = new FluxHarpMenuProvider();
            BasisMenuBase<BasisMainMenu>.AddProvider(_instance);
            SettingsProvider.AudioTabExtraBuilder -= BuildAudioEntry;
            SettingsProvider.AudioTabExtraBuilder += BuildAudioEntry;
        }

        public override void RunAction()
        {
            if (BasisMainMenu.ActiveMenuTitle == Title)
            {
                BasisMainMenu.CloseActivePanel();
                return;
            }

            BasisMenuPanel panel = BasisMainMenu.CreateActiveMenu(
                BasisMenuPanel.PanelData.Standard(Title),
                BasisMenuPanel.PanelStyles.Page);
            BoundButton?.BindActiveStateToAddressablesInstance(panel);
            RectTransform parent = panel.Descriptor.ContentParent;

            PanelElementDescriptor group = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, parent);
            group.SetTitle(StaticTitle);
            RectTransform content = group.ContentParent;

            PanelToggle on = PanelToggle.CreateNewEntry(content);
            on.Descriptor.SetTitle("Instrument");
            on.SetValueWithoutNotify(FluxHarpInstrument.Instance != null);
            on.OnValueChanged = v =>
            {
                if (v) FluxHarpInstrument.Spawn();
                else FluxHarpInstrument.Despawn();
            };

            VolumeSlider(content);

            RectTransform actions = PanelElementDescriptor.BuildActionRow(content, "FluxHarpActions");
            PanelButton here = PanelButton.CreateNew(actions);
            here.Descriptor.SetTitle("Bring here");
            here.OnClicked += () => FluxHarpInstrument.Spawn().PlaceHere();

            PanelElementDescriptor pieces = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, parent);
            pieces.SetTitle("Start from");
            RectTransform row = PanelElementDescriptor.BuildActionRow(pieces.ContentParent, "FluxHarpPieces");
            foreach (FluxPreset preset in FluxPresets.All)
            {
                PanelButton b = PanelButton.CreateNew(row);
                b.Descriptor.SetTitle(FluxPresets.Name(preset));
                b.OnClicked += () =>
                {
                    FluxHarpInstrument.Spawn();
                    FluxHarpNetwork.Request(FluxOp.LoadPreset(preset));
                };
            }
        }

        private static void VolumeSlider(RectTransform content)
        {
            PanelSlider volume = PanelSlider.CreateEntryAndBind(content,
                PanelSlider.SliderSettings.Percentage("Instrument volume"), FluxHarpSettings.Volume);
            volume.Descriptor.SetTooltip("How loud the instrument is for you. Only you hear this change.");
        }

        private static void BuildAudioEntry(RectTransform parent)
        {
            PanelElementDescriptor group = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, parent);
            group.SetTitle(StaticTitle);
            VolumeSlider(group.ContentParent);
        }
    }
}
