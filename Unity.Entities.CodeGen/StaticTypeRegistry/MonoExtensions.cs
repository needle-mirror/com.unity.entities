using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Unity.Cecil.Awesome;

namespace Unity.Entities.BuildUtils
{
    internal static class MonoExtensions
    {
        public static string FullNameLikeRuntime(this TypeReference tr)
        {
            // Cecil nested classes are separated by a "/", but System.Type names use "+"
            return tr.FullName.Replace("/", "+");
        }

        public static bool IsCppBasicType(this TypeDefinition type)
        {
            return type.MetadataType == MetadataType.Boolean || type.MetadataType == MetadataType.Void
                || type.MetadataType == MetadataType.SByte || type.MetadataType == MetadataType.Byte
                || type.MetadataType == MetadataType.Int16 || type.MetadataType == MetadataType.UInt16 || type.MetadataType == MetadataType.Char
                || type.MetadataType == MetadataType.Int32 || type.MetadataType == MetadataType.UInt32
                || type.MetadataType == MetadataType.Int64 || type.MetadataType == MetadataType.UInt64
                || type.MetadataType == MetadataType.Single || type.MetadataType == MetadataType.Double
                || type.MetadataType == MetadataType.IntPtr || type.MetadataType == MetadataType.UIntPtr;
        }

        public static Type GetSystemReflectionType(this TypeReference type)
        {
            return Type.GetType(type.GetReflectionName(), true);
        }

        public static string GetReflectionName(this TypeReference type)
        {
            if (type.IsGenericInstance)
            {
                var genericInstance = (GenericInstanceType)type;
                return string.Format("{0}.{1}[{2}]", genericInstance.Namespace, type.Name,
                    String.Join(",", genericInstance.GenericArguments.Select(p => p.GetReflectionName()).ToArray()));
            }

            return type.FullName;
        }



        public static TypeDefinition FixedSpecialType(this TypeReference typeRef)
        {
            TypeDefinition type = typeRef.Resolve();
            if (type.MetadataType == MetadataType.IntPtr) return type.Module.TypeSystem.IntPtr.Resolve();
            if (type.MetadataType == MetadataType.Void) return type.Module.TypeSystem.Void.Resolve();
            if (type.MetadataType == MetadataType.String) return type.Module.TypeSystem.String.Resolve();
            if (IsCppBasicType(type)) return type;
            else return null;
        }


        public static ulong CalculateStableTypeHash(this TypeReference typeDef)
        {
            return Unity.Entities.CodeGen.TypeHash.CalculateStableTypeHash(typeDef);
        }
    }
}

