using System.Linq;

namespace Unity.Entities.SourceGen.Common
{
    public static class StringHelpers
    {
        public static string EmitIfTrue(this string emitString, bool someCondition) => someCondition ? emitString : string.Empty;

        public static bool IsValidLambdaName(this string jobName)
        {
            if (jobName.Length == 0)
                return false;
            if (char.IsDigit(jobName[0]))
                return false;
            if (jobName.Any(t => t != '_' && !char.IsLetterOrDigit(t)))
                return false;

            // names with __ are reserved for the compiler by convention
            return !jobName.Contains("__");
        }
    }
}
