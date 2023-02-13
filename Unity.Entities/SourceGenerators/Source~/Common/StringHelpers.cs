using System;
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


        // Todo: This is temporary until we have a unified SourceGen printer (as .AppendLine will do the proper thing for us, the problem is from using Verbatim strings)
        public static string ReplaceLineEndings(this string value)
        {
            return value.Replace("\r", "").Replace("\n", Environment.NewLine);
        }
    }
}
