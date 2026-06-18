using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Luau;
using Minetake.Basis.Luau.Policy;
using Minetake.Basis.Luau.Registry;
using UnityEngine;

namespace Minetake.Basis.Luau.Bindings
{
    public static class LuauValueMarshaller
    {
        public static LuauValue ToLuau(LuauHostBase host, object value, Type type = null)
        {
            if (value == null)
            {
                return default;
            }

            type ??= value.GetType();
            if (value is string s)
            {
                return s;
            }

            if (value is bool b)
            {
                return b;
            }

            if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
            {
                return Convert.ToDouble(value);
            }

            if (value is Enum)
            {
                return Convert.ToDouble(value);
            }

            if (value is UnityEngine.Object unityObject)
            {
                return host.RegisterObject(unityObject).ToRaw();
            }

            if (value is Vector2 v2)
            {
                return Table(host, ("x", v2.x), ("y", v2.y));
            }

            if (value is Vector3 v3)
            {
                return Table(host, ("x", v3.x), ("y", v3.y), ("z", v3.z));
            }

            if (value is Vector4 v4)
            {
                return Table(host, ("x", v4.x), ("y", v4.y), ("z", v4.z), ("w", v4.w));
            }

            if (value is Quaternion q)
            {
                return Table(host, ("x", q.x), ("y", q.y), ("z", q.z), ("w", q.w));
            }

            if (value is Color c)
            {
                return Table(host, ("r", c.r), ("g", c.g), ("b", c.b), ("a", c.a));
            }

            if (value is Color32 c32)
            {
                return Table(host, ("r", c32.r), ("g", c32.g), ("b", c32.b), ("a", c32.a));
            }

            if (value is Array array)
            {
                return ArrayToTable(host, array);
            }

            if (value is IList list)
            {
                return ListToTable(host, list);
            }

            return value.ToString();
        }

        public static object FromLuau(LuauHostBase host, LuauWhitelistPolicy policy, LuauValue value, Type targetType)
        {
            if (targetType == null)
            {
                return null;
            }

            if (targetType == typeof(string))
            {
                return value.Type == LuauType.String ? value.Read<string>() : value.ToString();
            }

            if (targetType == typeof(bool))
            {
                return value.Type == LuauType.Boolean && value.Read<bool>();
            }

            if (targetType.IsEnum)
            {
                if (value.Type == LuauType.String)
                {
                    return Enum.Parse(targetType, value.Read<string>(), true);
                }

                return Enum.ToObject(targetType, Convert.ToInt32(value.Read<double>()));
            }

            if (targetType == typeof(int)) return Convert.ToInt32(value.Read<double>());
            if (targetType == typeof(float)) return (float)value.Read<double>();
            if (targetType == typeof(double)) return value.Read<double>();
            if (targetType == typeof(byte)) return (byte)value.Read<double>();
            if (targetType == typeof(short)) return (short)value.Read<double>();
            if (targetType == typeof(long)) return (long)value.Read<double>();

            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType))
            {
                double raw = value.Read<double>();
                var handle = LuauObjectHandle.FromRaw((ulong)raw);
                return host.Registry.TryResolve(handle, out UnityEngine.Object obj) ? obj : null;
            }

            if (targetType == typeof(Vector2)) return ReadVector2(value);
            if (targetType == typeof(Vector3)) return ReadVector3(value);
            if (targetType == typeof(Vector4)) return ReadVector4(value);
            if (targetType == typeof(Quaternion)) return ReadQuaternion(value);
            if (targetType == typeof(Color)) return ReadColor(value);

            return null;
        }

        public static object[] FromLuauArgs(LuauHostBase host, LuauWhitelistPolicy policy, LuauValue[] args, ParameterInfo[] parameters)
        {
            if (parameters == null || parameters.Length == 0)
            {
                return Array.Empty<object>();
            }

            var converted = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                LuauValue arg = i < args.Length ? args[i] : default;
                converted[i] = FromLuau(host, policy, arg, parameters[i].ParameterType);
            }

            return converted;
        }

        static LuauValue Table(LuauHostBase host, params (string key, double number)[] fields)
        {
            LuauState thread = host.CreateSandboxedThread();
            LuauTable table = thread.CreateTable();
            for (int i = 0; i < fields.Length; i++)
            {
                table[fields[i].key] = fields[i].number;
            }

            return table;
        }

        static LuauValue ArrayToTable(LuauHostBase host, Array array)
        {
            LuauState thread = host.CreateSandboxedThread();
            LuauTable table = thread.CreateTable();
            for (int i = 0; i < array.Length; i++)
            {
                table[i + 1] = ToLuau(host, array.GetValue(i));
            }

            return table;
        }

        static LuauValue ListToTable(LuauHostBase host, IList list)
        {
            LuauState thread = host.CreateSandboxedThread();
            LuauTable table = thread.CreateTable();
            for (int i = 0; i < list.Count; i++)
            {
                table[i + 1] = ToLuau(host, list[i]);
            }

            return table;
        }

        static Vector2 ReadVector2(LuauValue value)
        {
            LuauTable t = value.Read<LuauTable>();
            return new Vector2((float)t["x"].Read<double>(), (float)t["y"].Read<double>());
        }

        static Vector3 ReadVector3(LuauValue value)
        {
            LuauTable t = value.Read<LuauTable>();
            return new Vector3((float)t["x"].Read<double>(), (float)t["y"].Read<double>(), (float)t["z"].Read<double>());
        }

        static Vector4 ReadVector4(LuauValue value)
        {
            LuauTable t = value.Read<LuauTable>();
            return new Vector4((float)t["x"].Read<double>(), (float)t["y"].Read<double>(), (float)t["z"].Read<double>(), (float)t["w"].Read<double>());
        }

        static Quaternion ReadQuaternion(LuauValue value)
        {
            LuauTable t = value.Read<LuauTable>();
            return new Quaternion(
                (float)t["x"].Read<double>(),
                (float)t["y"].Read<double>(),
                (float)t["z"].Read<double>(),
                (float)t["w"].Read<double>());
        }

        static Color ReadColor(LuauValue value)
        {
            LuauTable t = value.Read<LuauTable>();
            return new Color((float)t["r"].Read<double>(), (float)t["g"].Read<double>(), (float)t["b"].Read<double>(), (float)t["a"].Read<double>());
        }
    }
}
