using System;

namespace Unity.Entities
{
    /// <summary>
    /// By default if no baking version are declared in an assembly, the scene will be re-imported if the assembly has changed.
    /// If you only want to re-trigger scene import if a baker, a baking system or an optimization system has changed and not something else in the assembly, use this attribute and bump its version or username everytime you want to re-trigger the scene import.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class BakingVersionAttribute : Attribute
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

        /// <summary>
        /// Initializes and returns an instance of BakingVersionAttribute
        /// </summary>
        /// <param name="userName">The identifier of the user that made the last change</param>
        /// <param name="version">The version number of the converter</param>
        public BakingVersionAttribute(string userName, int version)
        {
            UserName = userName;
            Version = version;
        }
    }
}
