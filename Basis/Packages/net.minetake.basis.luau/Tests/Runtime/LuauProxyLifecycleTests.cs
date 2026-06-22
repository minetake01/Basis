using System.Collections;
using System.Text;
using Luau;
using Luau.Unity;
using Minetake.Basis.Luau.Runtime;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace Minetake.Basis.Luau.Tests
{
    public sealed class LuauProxyLifecycleTests
    {
        [UnityTest]
        public IEnumerator StandardLibrariesAndNativeUpdateAreReadyWhenLoadCompletes()
        {
            GameObject hostObject = new GameObject("Luau lifecycle test host");
            try
            {
                LuauPropHost host = hostObject.AddComponent<LuauPropHost>();
                TMP_Text label = new GameObject("Lifecycle output").AddComponent<TextMeshPro>();
                label.transform.SetParent(hostObject.transform);
                label.text = "not-updated";
                LuauScriptProxy proxy = hostObject.AddComponent<LuauScriptProxy>();
                proxy.Configure(
                    LuauHostKind.Prop,
                    CompileSigned(@"
local match = string.match('12:34:56', '%d+:%d+:%d+')
assert(match == '12:34:56')
return {
    update = function(dt)
        if dt > 0 then
            basis_ui.setText(__luau_handles[1], 'updated')
        end
    end
}
"),
                    new Object[] { label },
                    "standard-library-registration-test",
                    host);

                for (int frame = 0; frame < 10 && label.text != "updated"; frame++)
                {
                    yield return null;
                }

                Assert.That(proxy.IsEnabled, Is.True, proxy.DisableReason);
                Assert.That(proxy.ProxyId, Is.Not.Zero);
                Assert.That(proxy.IsNativeRegistered, Is.True);
                Assert.That(label.text, Is.EqualTo("updated"), "Native update dispatch must receive a positive delta time.");
                proxy.TryGetFunction("update", out LuauFunction update);
                Assert.That(LuauFunctionRefHelper.GetReference(update), Is.GreaterThan(0), "Native update must use a valid Lua registry reference.");
            }
            finally
            {
                Object.Destroy(hostObject);
            }
        }

        [UnityTest]
        public IEnumerator NativeUpdateRefreshesBasisTimeNow()
        {
            GameObject hostObject = new GameObject("Luau clock test host");
            try
            {
                LuauPropHost host = hostObject.AddComponent<LuauPropHost>();
                TMP_Text label = new GameObject("Clock output").AddComponent<TextMeshPro>();
                label.transform.SetParent(hostObject.transform);
                label.text = string.Empty;
                LuauScriptProxy proxy = hostObject.AddComponent<LuauScriptProxy>();
                proxy.Configure(
                    LuauHostKind.Prop,
                    CompileSigned(@"
local LABEL = 0
local elapsed = 0
return {
    start = function()
        if __luau_handles and __luau_handles[1] then
            LABEL = __luau_handles[1]
        end
    end,
    update = function(dt)
        elapsed = elapsed + dt
        if elapsed >= 1.0 and LABEL ~= 0 then
            elapsed = 0
            basis_ui.setText(LABEL, basis_time.now())
        end
    end,
}
"),
                    new Object[] { label },
                    "basis-time-now-test",
                    host);

                for (int frame = 0; frame < 120 && string.IsNullOrEmpty(label.text); frame++)
                {
                    yield return null;
                }

                string first = label.text;
                Assert.That(first, Is.Not.Empty, "basis_time.now() should publish through native update.");

                for (int frame = 0; frame < 120; frame++)
                {
                    yield return null;
                }

                Assert.That(label.text, Is.Not.EqualTo(first), "basis_time.now() should refresh on subsequent updates.");
                Assert.That(proxy.IsEnabled, Is.True, proxy.DisableReason);
                Assert.That(proxy.IsNativeRegistered, Is.True);
            }
            finally
            {
                Object.Destroy(hostObject);
            }
        }

        [UnityTest]
        public IEnumerator ModuleInitializationFailurePreservesItsDiagnostic()
        {
            GameObject hostObject = new GameObject("Luau module failure test host");
            try
            {
                LuauPropHost host = hostObject.AddComponent<LuauPropHost>();
                LuauScriptProxy proxy = hostObject.AddComponent<LuauScriptProxy>();
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("expected module failure"));
                proxy.Configure(
                    LuauHostKind.Prop,
                    CompileSigned("error('expected module failure')"),
                    System.Array.Empty<Object>(),
                    "module-failure-diagnostic-test",
                    host);

                yield return null;

                Assert.That(proxy.IsEnabled, Is.False);
                Assert.That(proxy.IsNativeRegistered, Is.False);
                Assert.That(proxy.DisableReason, Does.Contain("expected module failure"));
            }
            finally
            {
                Object.Destroy(hostObject);
            }
        }

        static byte[] CompileSigned(string source)
        {
            byte[] bytecode = LuauCompiler.Compile(Encoding.UTF8.GetBytes(source));
            return LuauBytecodeSigner.AttachSignature(bytecode);
        }
    }
}
