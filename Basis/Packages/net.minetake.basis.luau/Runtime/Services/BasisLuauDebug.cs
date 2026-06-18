using Basis;
using UnityEngine;

namespace Minetake.Basis.Luau.Services
{
    public static class BasisLuauDebug
    {
        public static BasisDebug.LogTag LogTag = BasisDebug.LogTag.Props;

        public static void Log(object message) => BasisDebug.Log(message?.ToString() ?? string.Empty, LogTag);
        public static void Log(object message, Object context) => BasisDebug.Log(message?.ToString() ?? string.Empty, LogTag);
        public static void LogFormat(string format, params object[] args) => BasisDebug.Log(string.Format(format, args), LogTag);
        public static void LogFormat(Object context, string format, params object[] args) => BasisDebug.Log(string.Format(format, args), LogTag);

        public static void LogError(object message) => BasisDebug.LogError(message?.ToString() ?? string.Empty, LogTag);
        public static void LogError(object message, Object context) => BasisDebug.LogError(message?.ToString() ?? string.Empty, context, LogTag);
        public static void LogErrorFormat(string format, params object[] args) => BasisDebug.LogError(string.Format(format, args), LogTag);
        public static void LogErrorFormat(Object context, string format, params object[] args) =>
            BasisDebug.LogError(string.Format(format, args), context, LogTag);

        public static void LogWarning(object message) => BasisDebug.LogWarning(message?.ToString() ?? string.Empty, LogTag);
        public static void LogWarning(object message, Object context) => BasisDebug.LogWarning(message?.ToString() ?? string.Empty, LogTag);
        public static void LogWarningFormat(string format, params object[] args) => BasisDebug.LogWarning(string.Format(format, args), LogTag);
        public static void LogWarningFormat(Object context, string format, params object[] args) =>
            BasisDebug.LogWarning(string.Format(format, args), LogTag);

        public static void LogAssertion(object message) => BasisDebug.LogWarning($"[Assert] {message}", LogTag);
        public static void LogAssertion(object message, Object context) => BasisDebug.LogWarning($"[Assert] {message}", LogTag);
        public static void LogAssertionFormat(string format, params object[] args) => BasisDebug.LogWarning($"[Assert] {string.Format(format, args)}", LogTag);
        public static void LogAssertionFormat(Object context, string format, params object[] args) =>
            BasisDebug.LogWarning($"[Assert] {string.Format(format, args)}", LogTag);

        public static void Assert(bool condition) { if (!condition) LogAssertion("Assertion failed"); }
        public static void Assert(bool condition, Object context) { if (!condition) LogAssertion("Assertion failed", context); }
        public static void Assert(bool condition, object message) { if (!condition) LogAssertion(message); }
        public static void Assert(bool condition, object message, Object context) { if (!condition) LogAssertion(message, context); }
        public static void AssertFormat(bool condition, string format, params object[] args)
        {
            if (!condition) LogAssertionFormat(format, args);
        }

        public static void AssertFormat(bool condition, Object context, string format, params object[] args)
        {
            if (!condition) LogAssertionFormat(context, format, args);
        }
    }
}
