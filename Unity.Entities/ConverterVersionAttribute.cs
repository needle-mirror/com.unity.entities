using System;
using System.ComponentModel;

namespace Unity.Entities
{
    /// <summary>
    /// By declaring a version number a ComponentSystem can ensure that any cached data by the asset pipeline was prepared using the active code.
    /// If the version number of any conversion system or optimization system changes or a new conversion system is added, then the scene will be re-converted.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class ConverterVersionAttribute : Attribute
    {
        public string UserName;
        public int    Version;

        [EditorBrowsable(EditorBrowsableState.Never), Obsolete("It is recommended to use the string userName & int version override (RemovedAfter 2020-02-18)")]
        public ConverterVersionAttribute(int version)
        {
            Version = version;
        }

        public ConverterVersionAttribute(string userName, int version)
        {
            UserName = userName;
            Version = version;
        }
    }
}