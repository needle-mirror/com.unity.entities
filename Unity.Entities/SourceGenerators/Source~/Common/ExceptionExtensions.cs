#nullable enable
using System;
using System.Globalization;
using System.Text;

namespace Unity.Entities.SourceGen.Common
{
    public static class ExceptionExtensions
    {
        public static string ToUnityPrintableString(this Exception exception) => exception.ToString().Replace(Environment.NewLine, " |--| ");
    }
}
