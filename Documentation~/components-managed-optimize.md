# Optimize managed components

Unlike unmanaged components, Unity doesn't store managed components directly in chunks. Instead, Unity stores them in one big array for the whole `World`. Chunks then store the array indices of the relevant managed components. This means when you access a managed component of an entity, Unity processes an extra index lookup. This makes managed components less optimal then unmanaged components.

The performance implications of managed components mean that you should use unmanaged components instead where possible.