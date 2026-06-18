using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;

namespace Minetake.Basis.Luau.Tests
{
    public sealed class LuauWhitelistPolicyTests
    {
        [Test]
        public void Common_AllowsTmProTypes()
        {
            var policy = Policy.LuauPropWhitelistPolicy.Instance;
            Assert.IsTrue(policy.CheckTypeAllowed("TMPro.TextMeshProUGUI"));
            Assert.IsTrue(policy.CheckTypeAllowed("TMPro.TMP_Text"));
        }

        [Test]
        public void Common_AllowsUnityUiWildcard()
        {
            var policy = Policy.LuauSceneWhitelistPolicy.Instance;
            Assert.IsTrue(policy.CheckTypeAllowed("UnityEngine.UI.Button"));
            Assert.IsTrue(policy.CheckTypeAllowed("UnityEngine.UI.Image"));
        }

        [Test]
        public void Common_AllowsAudioTypes()
        {
            var policy = Policy.LuauPropWhitelistPolicy.Instance;
            Assert.IsTrue(policy.CheckTypeAllowed("UnityEngine.AudioSource"));
        }

        [Test]
        public void Prop_AllowsPhysicsTypes()
        {
            var policy = Policy.LuauPropWhitelistPolicy.Instance;
            Assert.IsTrue(policy.CheckTypeAllowed("UnityEngine.Rigidbody"));
            Assert.IsTrue(policy.CheckTypeAllowed("UnityEngine.BoxCollider"));
        }

        [Test]
        public void Scene_AllowsNavMeshAndPhysics()
        {
            var policy = Policy.LuauSceneWhitelistPolicy.Instance;
            Assert.IsTrue(policy.CheckTypeAllowed("UnityEngine.AI.NavMesh"));
            Assert.IsTrue(policy.CheckMethodAllowed(typeof(Physics), nameof(Physics.Raycast)));
        }

        [Test]
        public void Scene_BlocksNavMeshMutation()
        {
            var policy = Policy.LuauSceneWhitelistPolicy.Instance;
            Assert.IsFalse(policy.CheckMethodAllowed(typeof(NavMesh), "AddNavMeshData"));
        }

        [Test]
        public void Common_BlocksDangerousGameObjectMethods()
        {
            var policy = Policy.LuauAvatarWhitelistPolicy.Instance;
            Assert.IsFalse(policy.CheckMethodAllowed(typeof(GameObject), nameof(GameObject.AddComponent)));
            Assert.IsFalse(policy.CheckMethodAllowed(typeof(GameObject), "SendMessage"));
        }

        [Test]
        public void Common_BlocksApplicationOpenUrl()
        {
            var policy = Policy.LuauPropWhitelistPolicy.Instance;
            Assert.IsFalse(policy.CheckMethodAllowed(typeof(Application), nameof(Application.OpenURL)));
            Assert.IsTrue(policy.CheckMethodAllowed(typeof(Application), nameof(Application.isPlaying)));
        }

        [Test]
        public void Common_BlocksObjectInstantiate()
        {
            var policy = Policy.LuauPropWhitelistPolicy.Instance;
            Assert.IsFalse(policy.CheckMethodAllowed(typeof(Object), nameof(Object.Instantiate)));
        }

        [Test]
        public void Avatar_AllowsAvatarBridgeType()
        {
            var policy = Policy.LuauAvatarWhitelistPolicy.Instance;
            Assert.IsTrue(policy.CheckTypeAllowed("Minetake.Basis.Luau.Services.BasisLuauAvatarBridge"));
            Assert.IsTrue(policy.TryGetTypeOverride("Basis.Scripts.BasisSdk.BasisAvatar", out var t));
            Assert.AreEqual(typeof(Services.BasisLuauAvatarBridge), t);
        }

        [Test]
        public void Debug_OverridesToLuauDebug()
        {
            var policy = Policy.LuauPropWhitelistPolicy.Instance;
            Assert.IsTrue(policy.TryGetTypeOverride("UnityEngine.Debug", out var t));
            Assert.AreEqual(typeof(Services.BasisLuauDebug), t);
        }
    }
}
