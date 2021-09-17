namespace Unity.Properties.UI
{
    public abstract partial class ContentProvider
    {
        /// <summary>
        /// Context object used when a change to the content is detected from the UI.
        /// </summary>
        public readonly struct ChangeContext
        {
            readonly PropertyElement m_Binding;

            internal ChangeContext(PropertyElement binding)
            {
                m_Binding = binding;
            }

            /// <summary>
            /// Tries to get the target of the content as an instance of type <see cref="T"/>.
            /// </summary>
            /// <param name="content">The content, if it succeeds; <see langword="default"/> otherwise.</param>
            /// <typeparam name="T">The type of the content.</typeparam>
            /// <returns><see langword="true"/> if content was of the correct type; <see langword="false"/> otherwise.</returns>
            public bool TryGetDisplayContent<T>(out T content)
            {
                return m_Binding.TryGetTarget(out content);
            }
        }
    }
}
