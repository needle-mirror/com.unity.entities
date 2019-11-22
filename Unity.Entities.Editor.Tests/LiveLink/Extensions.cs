using System.IO;

namespace Unity.Entities.Editor.Tests.LiveLink
{
    static class StringExtensions
    {
        public static string ToForwardSlash(this string value)
        {
            return value.Replace('\\', '/');
        }

        public static string SingleQuotes(this string value)
        {
            return "'" + value.Trim('\'') + "'";
        }

        public static string DoubleQuotes(this string value)
        {
            return '"' + value.Trim('"') + '"';
        }

        public static string ToHyperLink(this string value, string key = null)
        {
            return string.IsNullOrEmpty(key) ? $"<a>{value}</a>" : $"<a {key}={value.DoubleQuotes()}>{value}</a>";
        }
    }

    static class DirectoryInfoExtensions
    {
        public static string GetRelativePath(this DirectoryInfo directoryInfo)
        {
            var path = directoryInfo.FullName.ToForwardSlash();
            var relativePath = new DirectoryInfo(".").FullName.ToForwardSlash();
            return path.StartsWith(relativePath) ? path.Substring(relativePath.Length).TrimStart('/') : path;
        }
    }
}
