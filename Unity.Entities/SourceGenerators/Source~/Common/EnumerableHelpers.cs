using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unity.Entities.SourceGen.Common
{
    public static class EnumerableHelpers
    {
        public static string SeparateByDot(this IEnumerable<string> lines) => string.Join(".", lines.Where(s => s != null));
        public static string SeparateByComma(this IEnumerable<string> lines) => string.Join(",", lines.Where(s => s != null));
        public static string SeparateByBinaryOr(this IEnumerable<string> lines) => string.Join("|", lines.Where(s => s != null));
        public static string SeparateByCommaAndNewLine(this IEnumerable<string> lines) => string.Join(",\r\n", lines.Where(s => s != null));
        public static string SeparateByNewLine(this IEnumerable<string> lines) => string.Join("\r\n", lines.Where(s => s != null));
    }
}
