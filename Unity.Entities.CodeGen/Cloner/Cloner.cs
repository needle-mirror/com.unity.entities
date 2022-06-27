using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Unity.Entities;
using Unity.Entities.CodeGen;

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

                var originalPropertyIdsToDefinitions = properties.Original.ToDictionary(GetPropertyName, method => method);

                foreach (var rewrittenProperty in properties.Rewritten)
                {
                    if (!originalPropertyIdsToDefinitions.ContainsKey(rewrittenProperty.Source))
                    {
                        throw new InvalidOperationException(
                            $"Method Cloner ILPP: Cannot find method {rewrittenProperty.Source} in {typeDef.FullName}. " +
                            $"Method candidates are {string.Join(", ", originalMethodIdsToDefinitions.Keys)}");
                    }

                    typeDef.UpdateOriginalProperty(originalPropertyIdsToDefinitions, rewrittenProperty);
                    madeChange = true;
                }
            }
            return madeChange;
        }

        protected override bool PostProcessUnmanagedImpl(TypeDefinition[] unmanagedComponentSystemTypes)
        {
            return false;
        }

        // Convert nullable types names to ? suffix (System.Nullable<System.Int32> -> System.Int32?)
        // Remove /& characters and `# for type arity
        static string CleanupParameterTypeName(string typeName)
        {
            typeName = Regex.Replace(typeName, @"System\.Nullable`1<(.*)>",
                m => $"{m.Groups[1].Value}?");

            typeName = typeName.Replace('/', '.').Replace("&", "").Replace(" ", string.Empty);
            var indexOfArityStart = typeName.IndexOf('`');
            if (indexOfArityStart != -1)
            {
                var indexOfArityEnd = typeName.IndexOf('<');
                if (indexOfArityEnd != -1)
                    return typeName.Remove(indexOfArityStart, indexOfArityEnd - indexOfArityStart);
            }

            return typeName;
        }

        static string GetPropertyName(PropertyDefinition propertyDefinition)
        {
            return $"{propertyDefinition.DeclaringType.ToString().Replace("/", ".")}.{propertyDefinition.Name}";
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
