using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Unity.Entities.SourceGen.SystemGenerator.Common;

namespace Unity.Entities.SourceGen.Common
{
    public static class QueryVerification
    {
        public static bool VerifyQueryTypeCorrectness(
            SystemDescription systemDescription,
            Location location,
            IEnumerable<Query> queries,
            string invokedMethodName)
        {
            bool success = true;

            foreach (var query in queries)
            {
                var queryTypeSymbol = query.TypeSymbol;

                bool isValidQueryType =
                    queryTypeSymbol.InheritsFromInterface("Unity.Entities.IComponentData") ||
                    queryTypeSymbol.InheritsFromInterface("Unity.Entities.ISharedComponentData") ||
                    queryTypeSymbol.InheritsFromInterface("Unity.Entities.IBufferElementData") ||
                    queryTypeSymbol.InheritsFromInterface($"Unity.Entities.IAspect") ||
                    queryTypeSymbol.Is("UnityEngine.Object");

                if (!isValidQueryType)
                {
                    QueryConstructionErrors.SGQC001(systemDescription, location, queryTypeSymbol.Name, invokedMethodName);
                    success = false;
                }
            }
            return success;
        }

        public static bool VerifySharedComponentFilterTypesAgainstOtherQueryTypes(
            SystemDescription systemDescription,
            Location location,
            IEnumerable<Query> sharedComponentFilterQueries,
            IEnumerable<Query> queriesToCheckAgainst)
        {
            var componentFilterQueries = sharedComponentFilterQueries.ToArray();

            var success = true;

            foreach (var queryExtension in queriesToCheckAgainst)
            {
                var queryExtensionTypeSymbol = queryExtension.TypeSymbol;
                if (componentFilterQueries.Contains(queryExtension))
                {
                    switch (queryExtension.Type)
                    {
                        case QueryType.All:
                            QueryConstructionErrors.SGQC002(systemDescription, location, queryExtensionTypeSymbol.ToFullName());
                            break;
                        default:
                            QueryConstructionErrors.SGQC003(systemDescription, location, queryExtensionTypeSymbol.ToFullName());
                            break;
                    }
                    success = false;
                }
            }
            return success;
        }

        public static bool VerifyNoMutuallyExclusiveQueries(
            SystemDescription systemDescription,
            Location location,
            IEnumerable<Query> mutuallyExclusiveQueryGroup1,
            IEnumerable<Query> mutuallyExclusiveQueryGroup2,
            string queryGroup1Name,
            string queryGroup2Name,
            bool compareTypeSymbolsOnly = false)
        {
            ITypeSymbol QueryToTypeSymbol(Query query)
            {
                if (query.TypeSymbol.ToFullName().StartsWith("global::Unity.Entities.Ref") &&
                    ((INamedTypeSymbol)query.TypeSymbol).TypeArguments.Length > 0)
                    return ((INamedTypeSymbol)query.TypeSymbol).TypeArguments.First();
                else
                    return query.TypeSymbol;
            }
            var mutuallyExclusiveQueryTypes1 = mutuallyExclusiveQueryGroup1.Select(QueryToTypeSymbol);
            var mutuallyExclusiveQueryTypes2 = mutuallyExclusiveQueryGroup2.Select(QueryToTypeSymbol);

            if (compareTypeSymbolsOnly)
            {
                var conflicts =
                    mutuallyExclusiveQueryTypes1.Where(type1 =>
                        mutuallyExclusiveQueryTypes2.Any(type2 =>
                            type1.ToFullName() == type2.ToFullName())).ToArray();

                foreach (var conflict in conflicts)
                    QueryConstructionErrors.SGQC004(systemDescription, location, queryGroup1Name, queryGroup2Name,
                        conflict.ToFullName());

                return conflicts.Length == 0;
            }

            var conflictingQueryTypes = mutuallyExclusiveQueryTypes1.Intersect(mutuallyExclusiveQueryTypes2,
                SymbolEqualityComparer.Default).ToArray();

            foreach (var conflict in conflictingQueryTypes)
                QueryConstructionErrors.SGQC004(systemDescription, location, queryGroup1Name, queryGroup2Name,
                    ((ITypeSymbol)conflict).ToFullName());

            return conflictingQueryTypes.Length == 0;
        }
    }
}
