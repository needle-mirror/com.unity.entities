using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;

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

            int indexOfArityStart = typeName.IndexOf('`');

            const int NotFound = -1;

            if (indexOfArityStart == NotFound)
                return typeName;

            /*
             If this is a nested type within a generic type, e.g.:

                 class MyGenericClass<T1>
                 {
                     struct NestedType {}
                     struct GenericNestedType<T2> {}
                 }

                 class MyDerivedClass : MyGenericClass<MyComponent>
                 {
                    void RunUpdate1(NestedType nestedType) <-- This parameter type will be written as: "MyGenericClass`1.NestedType<MyComponent>"
                    {
                        Entities.ForEach(() => { }).ScheduleParallel();
                    }
                    void RunUpdate2(GenericNestedType<YourComponent> nestedType) <-- This parameter type will be written as: "MyGenericClass`1.GenericNestedType`1.<MyComponent, YourComponent>"
                    {
                        Entities.ForEach(() => { }).ScheduleParallel();
                    }
                 }

                 Given the code sample above, we want to output "MyGenericClass<MyComponent>.NestedType" for `RunUpdate1()`,
                 and "MyGenericClass<MyComponent>.GenericNestedType<YourComponent>" for `RunUpdate2()`.
             */

            int typeParameterNameStart = typeName.IndexOf('<');
            int potentialNestedTypeSeparatorIndex = indexOfArityStart + 2;

            const char nestedTypeSeparator = '.';
            bool isNestedTypeInGenericType = typeName[potentialNestedTypeSeparatorIndex] == nestedTypeSeparator && typeParameterNameStart != NotFound;

            if (isNestedTypeInGenericType)
            {
                int typeParameterNameEnd = typeName.IndexOf('>');

                // E.g. from "MyGenericClass`1.GenericNestedType`1.<MyComponent, YourComponent>", retrieve ["MyComponent", "YourComponent"]
                // From "MyGenericClass`1.NestedType<MyComponent>", retrieve ["MyComponent"]
                string[] allTypeParameterNames = typeName.Substring(startIndex: typeParameterNameStart + 1, length: typeParameterNameEnd - (typeParameterNameStart + 1)).Split(',');

                int typeParamNamesStartIndex = 0;
                var formattedStringBuilder = new StringBuilder();

                // To visualise, visit https://regex101.com/ or any other Regex debugging site.
                // Enter the pattern "[.]*(?<genericTypeName>[a-zA-Z_.]+)`(?<numTypeParameters>\d+)*|[.]*(?<nonGenericTypeName>[a-zA-Z]+)(?=<)".
                // Enter these test strings:
                // 1. Unity.Entities.Tests.ForEachCodegen.ForEachCodegenTests.MyGenericTestBaseSystem_WithNestedGenericType`2.NestedType`1.AnotherNestedType`1.<Unity.Entities.Tests.EcsTestData,Unity.Entities.Tests.EcsTestData2,Unity.Entities.Tests.EcsTestData3,Unity.Entities.Tests.EcsTestData4>
                // 2. Unity.Entities.Tests.ForEachCodegen.ForEachCodegenTests.MyGenericTestBaseSystem_OneType`1.Nested_Type<Unity.Entities.Tests.EcsTestData>
                // 3. Unity.Collections.NativeParallelMultiHashMap`2.ParallelWriter<Unity.Entities.Entity,System.Int32>
                // Inspect the found matches.
                var matches = Regex.Matches(typeName, pattern: @"[.]*(?<genericTypeName>[a-zA-Z_0-9.]+)`(?<numTypeParameters>\d+)*|[.]*(?<nonGenericTypeName>[a-zA-Z_0-9]+)(?=<)").ToArray();

                for (var matchIndex = 0; matchIndex < matches.Length; matchIndex++)
                {
                    var match = matches[matchIndex];

                    int numTypeParameters = 0;
                    string genericTypeName = default;
                    string nonGenericTypeName = default;

                    foreach (Group group in match.Groups)
                        switch (group.Name)
                        {
                            case "numTypeParameters":
                                if (int.TryParse(group.Value, out int paramCount)) // Int-parsing will succeed iff we're dealing with a generic type name.
                                    numTypeParameters = paramCount;
                                break;
                            case "genericTypeName":
                                genericTypeName = group.Value;
                                break;
                            case "nonGenericTypeName":
                                nonGenericTypeName = group.Value;
                                break;
                        }

                    if (matchIndex > 0)
                        formattedStringBuilder.Append(nestedTypeSeparator);

                    if (!string.IsNullOrEmpty(genericTypeName))
                    {
                        formattedStringBuilder.Append(genericTypeName);
                        formattedStringBuilder.Append('<');

                        for (int i = typeParamNamesStartIndex; i < typeParamNamesStartIndex + numTypeParameters; i++)
                        {
                            formattedStringBuilder.Append(allTypeParameterNames[i]);
                            if (i < typeParamNamesStartIndex + numTypeParameters - 1)
                                formattedStringBuilder.Append(',');
                        }
                        formattedStringBuilder.Append('>');
                        typeParamNamesStartIndex += numTypeParameters;
                    }

                    else if (!string.IsNullOrEmpty(nonGenericTypeName))
                        formattedStringBuilder.Append($"{nonGenericTypeName}");
                }

                return formattedStringBuilder.ToString();
            }

            return typeParameterNameStart != NotFound ? typeName.Remove(indexOfArityStart, typeParameterNameStart - indexOfArityStart) : typeName;
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
