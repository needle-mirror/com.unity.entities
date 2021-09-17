using System.Collections.Generic;

namespace Unity.Entities.SourceGen.Common
{
    public static class DictionaryHelpers
    {
        public static void Add<T1, T2>(this Dictionary<T1, List<T2>> dictionary, T1 key, T2 value)
        {
            if (dictionary.TryGetValue(key, out var values))
            {
                values.Add(value);
            }
            else
            {
                dictionary.Add(key, new List<T2>{value});
            }
        }
    }
}
