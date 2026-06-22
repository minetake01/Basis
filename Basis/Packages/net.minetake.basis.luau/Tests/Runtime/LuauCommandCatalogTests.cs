using Minetake.Basis.Luau.Runtime;
using NUnit.Framework;

namespace Minetake.Basis.Luau.Tests
{
    public sealed class LuauCommandCatalogTests
    {
        [Test]
        public void RegisteredCoreCommands()
        {
            Assert.IsTrue(LuauCommandCatalog.IsRegistered(LuauCommandType.SetPosition));
            Assert.IsTrue(LuauCommandCatalog.IsRegistered(LuauCommandType.SetUiText));
            Assert.IsTrue(LuauCommandCatalog.IsRegistered(LuauCommandType.TicketClone));
            Assert.IsTrue(LuauCommandCatalog.IsRegistered(LuauCommandType.EventTriggerEnter));
        }

        [Test]
        public void DenyByDefault()
        {
            Assert.IsFalse(LuauCommandCatalog.IsRegistered(LuauCommandType.Invalid));
            Assert.IsFalse(LuauCommandCatalog.IsRegistered((LuauCommandType)999));
        }
    }
}
