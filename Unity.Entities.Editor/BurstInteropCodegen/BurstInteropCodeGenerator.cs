using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Tuple = System.Tuple;

#if UNITY_EDITOR

//     To change the generator, use this command:
//
//        $ t4.exe -o BurstInteropCodeGenerator.gen.cs -c Unity.Entities.Editor.BurstInteropCodeGenerator BurstInteropCodeGenerator.tt
//
//     To install the t4 tool (note that the default MS tool will not work):
//
//        $ dotnet tool install dotnet-t4
//

namespace Unity.Entities.Editor
{
    // Required for T4 non-awesomeness
    public abstract class CodeGeneratorBase
    {
        protected StringBuilder GenerationEnvironment { get; set; }

        public virtual void Initialize()
        {
            GenerationEnvironment = null;
        }

        public abstract string TransformText();

        public void Write(string txt)
        {
            if (GenerationEnvironment == null)
                GenerationEnvironment = new StringBuilder();

            GenerationEnvironment.Append(txt);
        }

        private ToStringInstanceHelper _toStringHelper = new ToStringInstanceHelper();

        public ToStringInstanceHelper ToStringHelper => this._toStringHelper;

        public class ToStringInstanceHelper
        {
            private IFormatProvider formatProvider = System.Globalization.CultureInfo.InvariantCulture;

            public IFormatProvider FormatProvider
            {
                get
                {
                    return formatProvider;
                }
                set
                {
                    if ((value != null))
                    {
                        formatProvider = value;
                    }
                }
            }

            public string ToStringWithCulture(object objectToConvert)
            {
                if ((objectToConvert == null))
                {
                    throw new ArgumentNullException("objectToConvert");
                }
                var type = objectToConvert.GetType();
                var iConvertibleType = typeof(global::System.IConvertible);
                if (iConvertibleType.IsAssignableFrom(type))
                {
                    return ((IConvertible)(objectToConvert)).ToString(this.formatProvider);
                }
                MethodInfo methInfo = type.GetMethod("ToString", new[] { iConvertibleType });
                if ((methInfo != null))
                {
                    return ((string)(methInfo.Invoke(objectToConvert, new object[] { formatProvider })));
                }
                return objectToConvert.ToString();
            }
        }
    }

    public partial class BurstInteropCodeGenerator : CodeGeneratorBase
    {
        [MenuItem("DOTS/Regenerate Burst Interop")]
        private static void RegenerateInterop()
        {
            int count = 0;

            var self = new BurstInteropCodeGenerator();

            foreach (var ty in TypeCache.GetTypesWithAttribute<GenerateBurstMonoInteropAttribute>())
            {
                count += self.RegenerateInteropForType(ty);
            }

            if (count > 0)
            {
                AssetDatabase.Refresh();
            }
        }

        private int RegenerateInteropForType(Type ty)
        {
            var methods = new List<MethodInfo>();

            foreach (var m in ty.GetMethods(BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (m.GetCustomAttribute<BurstMonoInteropMethodAttribute>() == null)
                    continue;

                if (!m.Name.StartsWith("_"))
                {
                    Debug.LogWarning($"{ty} member {m} does not follow naming convention: name must start with an underscore (_)");
                    continue;
                }

                methods.Add(m);
            }

            StructType = ty;
            Methods = methods;

            var buf = TransformText();

            var srcAsset = ty.GetCustomAttribute<GenerateBurstMonoInteropAttribute>().AssetName;

            string path = null;
            foreach (var candidate in AssetDatabase.FindAssets(srcAsset))
            {
                string p = AssetDatabase.GUIDToAssetPath(candidate);
                if (p.Contains(".interop.gen"))
                    continue;
                if (!p.EndsWith(".cs"))
                    continue;

                if (path != null)
                {
                    Debug.LogError($"More than one match for {srcAsset}.cs; pick a different name!");
                    return 0;
                }

                path = p;
            }

            if (path == null)
            {
                Debug.LogError($"Can't find source file (next to) {srcAsset}.cs to create or update!");
                return 0;
            }

            path = path.Replace(".cs", ".interop.gen.cs");

            File.WriteAllText(path, buf);
            Debug.Log($"OK - Updated {path}");

            return 1;
        }

        protected static string Prototype(MethodInfo m)
        {
            var buf = new StringBuilder(128);

            var parameters = m.GetParameters();
            for (int x = 0; x < parameters.Length; ++x)
            {
                var p = parameters[x];

                if (x > 0)
                    buf.Append(", ");

                if (p.ParameterType.IsByRef)
                    buf.Append("ref ");
                if (p.IsIn)
                    buf.Append("in ");
                if (p.IsOut)
                    buf.Append("out ");

                TypeToString(p.ParameterType, buf);

                if (p.ParameterType.IsPointer)
                    buf.Append("*");

                buf.Append(" ").Append(p.Name);
            }
            return buf.ToString();
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

        protected static string ReturnType(MethodInfo m)
        {
            var p = m.ReturnParameter;

            var buf = new StringBuilder();
            if (m.ReturnType.IsByRef)
                buf.Append("ref ");
            TypeToString(m.ReturnType, buf);

            if (p.ParameterType.IsPointer)
                buf.Append("*");

            return buf.ToString();
        }

        protected static string CallArgs(MethodInfo m)
        {
            var parameters = m.GetParameters();
            var buf = new StringBuilder();
            for (int x = 0; x < parameters.Length; ++x)
            {
                if (x > 0)
                    buf.Append(", ");

                var p = parameters[x];

                if (p.ParameterType.IsByRef)
                    buf.Append("ref ");
                if (p.IsIn)
                    buf.Append("in ");
                if (p.IsOut)
                    buf.Append("out ");

                buf.Append(p.Name);
            }

            return buf.ToString();
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

        // Interface for text template
        protected Type StructType { get; private set; }
        protected ICollection<MethodInfo> Methods { get; private set; }
    }
}


#endif
