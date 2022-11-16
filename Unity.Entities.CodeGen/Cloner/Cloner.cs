using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Unity.Entities;
using Unity.Entities.CodeGen;
using UnityEngine;

namespace Unity.Entities.CodeGen.Cloner
{
    class Cloner : EntitiesILPostProcessor
    {
        protected override bool PostProcessImpl(TypeDefinition[] componentSystemTypes)
        {
            var madeChange = false;
            foreach (var typeDef in componentSystemTypes)
            {
                var methods = typeDef.GetOriginalAndRewrittenMethods();
                var properties = typeDef.GetOriginalAndRewrittenProperties();

                if (!methods.Rewritten.Any() && !properties.Rewritten.Any())
                {
                    continue;
                }

                foreach (var rewrittenProperty in properties.Rewritten)
                {
                    if (!properties.OriginalLookup.ContainsKey(rewrittenProperty.Source))
                    {
                        throw new InvalidOperationException(
                            $"Method Cloner ILPP: Cannot find property {rewrittenProperty.Source} in {typeDef.FullName}. " +
                            $"Property candidates are {string.Join(", ", properties.OriginalLookup.Keys)}");
                    }

                    var originalProperty = properties.OriginalLookup[rewrittenProperty.Source];
                    if (rewrittenProperty.Definition.GetMethod != null)
                        methods.Rewritten.Add((rewrittenProperty.Definition.GetMethod, originalProperty.GetMethod.Name));
                    if (rewrittenProperty.Definition.SetMethod != null)
                        methods.Rewritten.Add((rewrittenProperty.Definition.SetMethod, originalProperty.SetMethod.Name+"_"+originalProperty.SetMethod.Parameters[0].ParameterType.FullName));
                    typeDef.Properties.Remove((((PropertyDefinition Definition, string ConstructorArgument))rewrittenProperty).Definition);
                    madeChange = true;
                }

                var originalMethodIdsToDefinitions = methods.Original.ToDictionary(GetMethodNameAndParamsAsString, method => method);

                foreach (var rewrittenMethod in methods.Rewritten)
                {
                    if (!originalMethodIdsToDefinitions.ContainsKey(rewrittenMethod.Source))
                    {
                        throw new InvalidOperationException(
                            $"Method Cloner ILPP: Cannot find method {rewrittenMethod.Source} in {typeDef.FullName}. " +
                            $"Method candidates are {string.Join(", ", originalMethodIdsToDefinitions.Keys)}");
                    }

                    typeDef.UpdateOriginalMethod(originalMethodIdsToDefinitions, rewrittenMethod);
                    madeChange = true;
                }
            }
            return madeChange;
        }

        protected override bool PostProcessUnmanagedImpl(TypeDefinition[] unmanagedComponentSystemTypes)
        {
            return false;
        }

        // Remove /& characters and `# for type arity
        // Also replace Cecil's System.Int32[0...,0...] syntax with more common System.Int32[,]
        static string CleanupParameterTypeName(string typeName)
        {
            typeName = Regex.Replace(typeName, @"System\.Nullable`1<(.*)>",
                m => $"{m.Groups[1].Value}?");
            typeName = typeName.Replace("0...", string.Empty)
                .Replace('/', '.')
                .Replace("&", "")
                .Replace(" ", string.Empty);
            var indexOfArityStart = typeName.IndexOf('`');
            if (indexOfArityStart != -1)
            {
                var indexOfArityEnd = typeName.IndexOf('<');
                if (indexOfArityEnd != -1)
                    return typeName.Remove(indexOfArityStart, indexOfArityEnd - indexOfArityStart);
            }

            return typeName;
        }

        static string GetMethodNameAndParamsAsString(MethodReference method)
        {
            var strBuilder = new StringBuilder();
            strBuilder.Append(method.Name);

            for (var typeIndex = 0; typeIndex < method.GenericParameters.Count; typeIndex++)
                strBuilder.Append($"_T{typeIndex}");

            foreach (var parameter in method.Parameters)
            {
                if (parameter.ParameterType.IsByReference)
                {
                    if (parameter.IsIn)
                        strBuilder.Append($"_in");
                    else if (parameter.IsOut)
                        strBuilder.Append($"_out");
                    else
                        strBuilder.Append($"_ref");
                }


                strBuilder.Append($"_{CleanupParameterTypeName(parameter.ParameterType.ToString())}");
            }

            return strBuilder.ToString();
        }
    }
}
