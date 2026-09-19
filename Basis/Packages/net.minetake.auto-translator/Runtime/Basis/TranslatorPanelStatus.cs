using Basis.BasisUI;
using UnityEngine;

namespace Net.Minetake.AutoTranslator.BasisIntegration
{
    public sealed class TranslatorPanelStatus : MonoBehaviour
    {
        public PanelElementDescriptor Label;
        private string last;
        private void Update()
        {
            var host = TranslatorHost.Instance;
            if (host != null && Label != null && last != host.Status)
            { last = host.Status; Label.SetDescription(last); }
        }
    }
}
