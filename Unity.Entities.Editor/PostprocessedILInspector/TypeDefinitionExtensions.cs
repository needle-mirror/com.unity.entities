using System.Collections.Generic;
using Mono.Cecil;

namespace Unity.Entities.Editor
{
    internal static class TypeDefinitionExtensions
    {
        private const string ClosureClassPrefix = "<>c__DisplayClass_";
        private static readonly Dictionary<TypeDefinition, string> TypeDefinitionsToUserFriendlyNames = new Dictionary<TypeDefinition, string>();
        
        public static string GetUserFriendlyName(this TypeDefinition typeDefinition)
        {
            if (!TypeDefinitionsToUserFriendlyNames.ContainsKey(typeDefinition))
            {
                string userFriendlyName = 
                    !typeDefinition.Name.Contains(ClosureClassPrefix) 
                        ? typeDefinition.Name 
                        : typeDefinition.Name.Replace(ClosureClassPrefix, $"{typeDefinition.DeclaringType.Name}.");
                
                TypeDefinitionsToUserFriendlyNames.Add(typeDefinition, userFriendlyName);
            }
            
            return TypeDefinitionsToUserFriendlyNames[typeDefinition];
        }
    }
}