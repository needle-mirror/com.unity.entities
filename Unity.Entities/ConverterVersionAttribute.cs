using System;

namespace Unity.Entities
{
    /// <summary>
    /// By declaring a version number a ComponentSystem can ensure that any cached data by the asset pipeline was prepared using the active code.
    /// If the version number of any conversion system or optimization system changes or a new conversion system is added, then the scene will be re-converted.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class ConverterVersionAttribute : Attribute
    {
        /// <summary>
        /// An identifier for the user that made the last change. Use this to enforce a merge conflict when two different
        /// users both try to bump the version at the same time.
        /// </summary>
        public string UserName;

        /// <summary>
        /// The version number of the converter. Increase this to invalidate the cached versions of entity scenes that
        /// use the converter that is tagged with this attribute.
        /// </summary>
        public int    Version;

        public ConverterVersionAttribute(string userName, int version)
        {
            UserName = userName;
            Version = version;
        }
    }
}
