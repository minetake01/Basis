using Luau.Unity;
using Minetake.Basis.Luau.Runtime;
using NUnit.Framework;

namespace Minetake.Basis.Luau.Tests
{
    public class LuauProxyGenerationTests
    {
        [Test]
        public void DisabledProxyGenerationBlocksCommands()
        {
            var generations = new System.Collections.Generic.Dictionary<uint, LuauScriptProxy.ProxyGenerationState>
            {
                [1] = new LuauScriptProxy.ProxyGenerationState { Generation = 3, Disabled = true },
            };

            var cmd = new BasisLuauCommandNative
            {
                Type = (ushort)LuauCommandType.SetUiText,
                HostId = 1,
                ProxyId = 1,
                ProxyGeneration = 3,
            };

            Assert.IsTrue(generations.TryGetValue(cmd.ProxyId, out LuauScriptProxy.ProxyGenerationState state));
            Assert.IsTrue(state.Disabled || state.Generation != cmd.ProxyGeneration);
        }
    }
}
