#if !NET_DOTS

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;

namespace Unity.Entities.Tests
{
    public class BurstCompatibilityTests
    {
        private static string CachedGuid = null;

        [MenuItem("DOTS/Regenerate Burst Compat Tests")]
        public static void UpdateGeneratedFile()
        {
            var buf = new StringBuilder();

            var methods = GetTestMethods();
            CachedGuid = ComputeGuid(methods);

            buf.AppendLine("// auto-generated");
            buf.AppendLine("#if !NET_DOTS");
            buf.AppendLine("using System;");
            buf.AppendLine("using NUnit.Framework;");
            buf.AppendLine("using Unity.Burst;");
            buf.AppendLine("using Unity.Entities;");
            buf.AppendLine("using Unity.Collections;");
            buf.AppendLine("using System.ComponentModel;");
            buf.AppendLine("namespace Unity.Entities.Tests");
            buf.AppendLine("{");

            buf.AppendLine("[BurstCompile]");
            buf.AppendLine("public unsafe class BurstCompatibilityTests_Generated");
            buf.AppendLine("{");
            buf.AppendLine("    private delegate void TestFunc(IntPtr p);");
            buf.AppendLine($"    public static readonly string Guid = \"{CachedGuid}\";");

            var overloadHandling = new Dictionary<string, int>();

            foreach (var method in methods)
            {
                var safeName = GetSafeName(method);
                if (overloadHandling.ContainsKey(safeName))
                {
                    int num = overloadHandling[safeName]++;
                    safeName += $"_overload{num}";
                }
                else
                {
                    overloadHandling.Add(safeName, 0);
                }

                buf.AppendLine("    [EditorBrowsable(EditorBrowsableState.Never)]");
                buf.AppendLine("    [BurstCompile(CompileSynchronously = true)]");
                buf.AppendLine($"    public static void Burst_{safeName}(IntPtr p)");
                buf.AppendLine("    {");

                // Generate targets for out/ref parameters
                var parameters = method.GetParameters();
                for (int i = 0; i < parameters.Length; ++i)
                {
                    var param = parameters[i];

                    if (param.ParameterType.IsPointer)
                    {
                        TypeToString(param.ParameterType, buf);
                        buf.Append($"* v{i} = (");
                        TypeToString(param.ParameterType, buf);
                        buf.AppendLine($"*) ((byte*)p + {i * 1024});");
                    }
                    else
                    {
                        buf.Append($"var v{i} = default(");
                        TypeToString(param.ParameterType, buf);
                        buf.AppendLine(");");
                    }
                }


                if (method.IsStatic)
                {
                    TypeToString(method.DeclaringType, buf);
                    buf.Append($".{method.Name}");
                }
                else
                {
                    buf.Append($"        var instance = (");
                    TypeToString(method.DeclaringType, buf);
                    buf.AppendLine("*)p;");
                    buf.Append($"        instance->{method.Name}");
                }

                // Make dummy arguments.
                buf.Append("(");

                for (int i = 0; i < parameters.Length; ++i)
                {
                    if (i > 0)
                        buf.Append(" ,");

                    var param = parameters[i];

                    if (param.IsOut)
                    {
                        buf.Append("out ");
                    }
                    else if (param.IsIn)
                    {
                        buf.Append("in ");
                    }
                    else if (param.ParameterType.IsByRef)
                    {
                        buf.Append("ref ");
                    }

                    buf.Append($"v{i}");
                }
                buf.AppendLine(");");

                buf.AppendLine("    }");

                buf.AppendLine("    [EditorBrowsable(EditorBrowsableState.Never)]");
                buf.AppendLine("    [Test]");
                buf.AppendLine($"    public void BurstCompile_{safeName}()");
                buf.AppendLine("    {");
                buf.AppendLine($"        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_{safeName});");
                buf.AppendLine("    }");
            }

            buf.AppendLine("}");
            buf.AppendLine("}");
            buf.AppendLine("#endif");

            string[] guids = AssetDatabase.FindAssets("BurstCompatibilityTests.gen");
            if (guids == null || guids.Length != 1)
            {
                Debug.LogError("Can't find source file BurstCompatibilityTests.gen.cs to update!");
                return;
            }

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);

            File.WriteAllText(path, buf.ToString());
            Debug.Log($"OK - Updated {path}");

            AssetDatabase.Refresh();
        }

        private static void TypeToString(Type t, StringBuilder buf)
        {
            if (t.IsPrimitive || t == typeof(void))
            {
                buf.Append(PrimitiveTypeToString(t));
                return;
            }

            if (t.IsByRef || t.IsPointer)
            {
                TypeToString(t.GetElementType(), buf);
                return;
            }

            if (t.Namespace != "Unity.Entities" && t.Namespace != "Unity.Collections")
            {
                buf.Append(t.Namespace);
                buf.Append(".");
            }

            GetFullTypeName(t, buf);

            if (t.IsConstructedGenericType)
            {
                buf.Append("<");
                var gt = t.GenericTypeArguments;

                for (int i = 0; i < gt.Length; ++i)
                {
                    if (i > 0)
                    {
                        buf.Append(", ");
                    }

                    TypeToString(gt[i], buf);
                }
                buf.Append(">");
            }
        }

        private static string PrimitiveTypeToString(Type type)
        {
            if (type == typeof(void))
                return "void";
            if (type == typeof(bool))
                return "bool";
            if (type == typeof(byte))
                return "byte";
            if (type == typeof(sbyte))
                return "sbyte";
            if (type == typeof(short))
                return "short";
            if (type == typeof(ushort))
                return "ushort";
            if (type == typeof(int))
                return "int";
            if (type == typeof(uint))
                return "uint";
            if (type == typeof(long))
                return "long";
            if (type == typeof(ulong))
                return "ulong";
            if (type == typeof(char))
                return "char";
            if (type == typeof(double))
                return "double";
            if (type == typeof(float))
                return "float";

            throw new InvalidOperationException($"{type} is not a primitive type");
        }

        private static void GetFullTypeName(Type type, StringBuilder buf)
        {
            if (type.DeclaringType != null)
            {
                GetFullTypeName(type.DeclaringType, buf);
                buf.Append(".");
            }

            var name = type.Name;

            if (type.IsConstructedGenericType)
            {
                name = name.Remove(name.IndexOf('`'));
            }

            buf.Append(name);
        }

        [Test]
        public static void EnsureUpToDate()
        {
            if (CachedGuid == null)
            {
                CachedGuid = ComputeGuid(GetTestMethods());
            }

            if (CachedGuid != BurstCompatibilityTests_Generated.Guid)
            {
                throw new ApplicationException("Methods affected by [BurstCompatible] attributes have changed. Please regenerate the compatibility unit tests using the DOTS/Regenerate Burst Compat Tests menu item.");
            }
        }

        private static string ComputeGuid(MethodInfo[] methods)
        {
            var text = new StringBuilder();
            foreach (var m in methods)
            {
                text.Append(m.DeclaringType.FullName);
                text.Append("/");
                text.Append(m.Name);
                text.Append("$");
            }

            using (var h = MD5.Create())
            {
                var hash = h.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
                var result = new StringBuilder(32);
                for (int i = 0; i < 16; ++i)
                {
                    result.AppendFormat("{0:x02}", hash[i]);
                }

                return result.ToString();
            }
        }

        private class MethodComparer : IComparer
        {
            public int Compare(object x, object y)
            {
                MethodInfo lhs = (MethodInfo)x;
                MethodInfo rhs = (MethodInfo)y;

                var ltn = lhs.DeclaringType.FullName;
                var rtn = rhs.DeclaringType.FullName;

                int tc = ltn.CompareTo(rtn);
                if (tc != 0) return tc;

                tc = lhs.Name.CompareTo(rhs.Name);
                if (tc != 0) return tc;

                var lp = lhs.GetParameters();
                var rp = rhs.GetParameters();
                if (lp.Length < rp.Length)
                    return -1;
                if (lp.Length > rp.Length)
                    return 1;

                var lb = new StringBuilder();
                var rb = new StringBuilder();
                for (int i = 0; i < lp.Length; ++i)
                {
                    GetFullTypeName(lp[i].ParameterType, lb);
                    GetFullTypeName(rp[i].ParameterType, rb);

                    tc = lb.ToString().CompareTo(rb.ToString());
                    if (tc != 0)
                        return tc;

                    lb.Clear();
                    rb.Clear();
                }

                return 0;
            }
        }

        private static MethodInfo[] GetTestMethods()
        {
            var methods = new HashSet<MethodInfo>();

            void MaybeAddMethod(MethodInfo m)
            {
                // FIXME
                if (m.IsGenericMethodDefinition)
                    return;

                // FIXME
                if (m.DeclaringType.IsGenericTypeDefinition)
                    return;

                if (m.IsPrivate)
                    return;

                if (m.GetCustomAttribute<ObsoleteAttribute>() != null)
                    return;

                if (m.GetCustomAttribute<NotBurstCompatibleAttribute>() != null)
                    return;

                // FIXME: Ignore properties.
                if (m.IsSpecialName)
                    return;

                if (m.GetParameters().Any((p) => !p.ParameterType.IsValueType && !p.ParameterType.IsPointer))
                    return;

                if (!methods.Contains(m))
                    methods.Add(m);
            }

            foreach (var m in TypeCache.GetMethodsWithAttribute<BurstCompatibleAttribute>())
            {
                MaybeAddMethod(m);
            }

            foreach (var t in TypeCache.GetTypesWithAttribute<BurstCompatibleAttribute>())
            {
                foreach (var m in t.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    MaybeAddMethod(m);
                }
            }

            var array = methods.ToArray();
            Array.Sort(array, new MethodComparer());
            return array;
        }

        private static string GetSafeName(MethodInfo method)
        {
            return GetSafeName(method.DeclaringType) + "_" + method.Name;
        }

        public static readonly Regex r = new Regex(@"[^A-Za-z_0-9]+");

        private static string GetSafeName(Type t)
        {
            return r.Replace(t.FullName, "__");
        }
    }
}

#endif
