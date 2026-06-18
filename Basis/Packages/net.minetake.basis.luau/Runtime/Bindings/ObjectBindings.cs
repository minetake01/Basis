using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Luau;
using Minetake.Basis.Luau.Policy;
using Minetake.Basis.Luau.Registry;
using UnityEngine;

namespace Minetake.Basis.Luau.Bindings
{
    [LuauLibrary("basis_object")]
    public partial class ObjectBindings
    {
        static readonly ConcurrentDictionary<MethodCacheKey, MethodInfo> MethodCache = new();

        public static void Install(LuauState state) => state?.OpenLibrary<ObjectBindings>();

        [LuauMember("getTypeName")]
        public static string GetTypeName(double handleRaw)
        {
            if (!TryResolveObject(handleRaw, out UnityEngine.Object obj))
            {
                return string.Empty;
            }

            return obj.GetType().FullName;
        }

        [LuauMember("getField")]
        public static LuauValue GetField(double handleRaw, string memberName)
        {
            if (!TryResolveObject(handleRaw, out UnityEngine.Object obj) || string.IsNullOrEmpty(memberName))
            {
                return default;
            }

            var host = LuauBindingContext.Host;
            var policy = host.Policy;
            Type type = obj.GetType();
            string typeName = type.FullName;

            if (policy.CheckFieldAllowed(typeName, memberName))
            {
                FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    return LuauValueMarshaller.ToLuau(host, field.GetValue(obj));
                }
            }

            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property?.GetMethod != null && policy.CheckMethodAllowed(type, property.GetMethod.Name))
            {
                return LuauValueMarshaller.ToLuau(host, property.GetValue(obj));
            }

            return default;
        }

        [LuauMember("setField")]
        public static void SetField(double handleRaw, string memberName, LuauValue value)
        {
            if (!TryResolveObject(handleRaw, out UnityEngine.Object obj) || string.IsNullOrEmpty(memberName))
            {
                return;
            }

            var host = LuauBindingContext.Host;
            var policy = host.Policy;
            Type type = obj.GetType();
            string typeName = type.FullName;

            if (policy.CheckFieldAllowed(typeName, memberName))
            {
                FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    field.SetValue(obj, LuauValueMarshaller.FromLuau(host, policy, value, field.FieldType));
                    return;
                }
            }

            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property?.SetMethod != null && policy.CheckMethodAllowed(type, property.SetMethod.Name))
            {
                property.SetValue(obj, LuauValueMarshaller.FromLuau(host, policy, value, property.PropertyType));
            }
        }

        [LuauMember("call")]
        public static LuauValue Call([FromLuauState] LuauState state)
        {
            ReadInvocation(state, out LuauValue target, out string methodName, out LuauValue[] args);
            double handleRaw = target.Read<double>();
            if (!TryResolveObject(handleRaw, out UnityEngine.Object obj) || string.IsNullOrEmpty(methodName))
            {
                return default;
            }

            return InvokeMethod(LuauBindingContext.Host, obj, obj.GetType(), methodName, args, false);
        }

        [LuauMember("callStatic")]
        public static LuauValue CallStatic([FromLuauState] LuauState state)
        {
            ReadInvocation(state, out LuauValue target, out string methodName, out LuauValue[] args);
            string typeName = target.Read<string>();
            var host = LuauBindingContext.Host;
            if (host == null || string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(methodName))
            {
                return default;
            }

            Type type = host.Policy.ResolveType(typeName);
            if (type == null)
            {
                return default;
            }

            return InvokeMethod(host, null, type, methodName, args, true);
        }

        static void ReadInvocation(
            LuauState state,
            out LuauValue target,
            out string methodName,
            out LuauValue[] args)
        {
            int count = state?.GetTop() ?? 0;
            if (count < 2)
            {
                throw new LuauException("basis_object invocation requires a target and method name");
            }

            target = state.ToValue(1);
            methodName = state.ToValue(2).Read<string>();
            args = new LuauValue[count - 2];
            for (int i = 0; i < args.Length; i++)
            {
                args[i] = state.ToValue(i + 3);
            }
        }

        [LuauMember("getComponent")]
        public static double GetComponent(double handleRaw, string typeName)
        {
            if (!TryResolveObject(handleRaw, out UnityEngine.Object obj) || string.IsNullOrEmpty(typeName))
            {
                return 0;
            }

            var host = LuauBindingContext.Host;
            Type componentType = host.Policy.ResolveType(typeName);
            if (componentType == null)
            {
                return 0;
            }

            if (obj is GameObject go)
            {
                Component component = go.GetComponent(componentType);
                return component != null ? host.RegisterObject(component).ToRaw() : 0;
            }

            if (obj is Component comp)
            {
                Component component = comp.GetComponent(componentType);
                return component != null ? host.RegisterObject(component).ToRaw() : 0;
            }

            return 0;
        }

        static LuauValue InvokeMethod(LuauHostBase host, object target, Type declaringType, string methodName, LuauValue[] args, bool isStatic)
        {
            var policy = host.Policy;
            MethodInfo method = FindCallableMethod(declaringType, methodName, args?.Length ?? 0, isStatic);
            if (method == null || !policy.CheckMethodAllowed(method.DeclaringType ?? declaringType, method.Name))
            {
                return default;
            }

            object[] converted = LuauValueMarshaller.FromLuauArgs(host, policy, args ?? Array.Empty<LuauValue>(), method.GetParameters());
            object result = method.Invoke(isStatic ? null : target, converted);
            return LuauValueMarshaller.ToLuau(host, result, method.ReturnType);
        }

        static MethodInfo FindCallableMethod(Type type, string methodName, int argCount, bool isStatic)
        {
            var key = new MethodCacheKey(type, methodName, argCount, isStatic);
            if (MethodCache.TryGetValue(key, out MethodInfo cached))
            {
                return cached;
            }

            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            MethodInfo[] methods = type.GetMethods(flags);
            MethodInfo match = null;
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name != methodName)
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == argCount)
                {
                    match = method;
                    break;
                }
            }

            if (match != null)
            {
                MethodCache[key] = match;
            }

            return match;
        }

        static bool TryResolveObject(double handleRaw, out UnityEngine.Object obj)
        {
            obj = null;
            var host = LuauBindingContext.Host;
            if (host == null)
            {
                return false;
            }

            var handle = LuauObjectHandle.FromRaw((ulong)handleRaw);
            return host.Registry.TryResolve(handle, out obj);
        }

        readonly struct MethodCacheKey : IEquatable<MethodCacheKey>
        {
            public MethodCacheKey(Type type, string name, int argCount, bool isStatic)
            {
                Type = type;
                Name = name;
                ArgCount = argCount;
                IsStatic = isStatic;
            }

            Type Type { get; }
            string Name { get; }
            int ArgCount { get; }
            bool IsStatic { get; }

            public bool Equals(MethodCacheKey other) =>
                Type == other.Type && Name == other.Name && ArgCount == other.ArgCount && IsStatic == other.IsStatic;

            public override bool Equals(object obj) => obj is MethodCacheKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(Type, Name, ArgCount, IsStatic);
        }
    }
}
