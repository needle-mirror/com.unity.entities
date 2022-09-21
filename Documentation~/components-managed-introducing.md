# Introducing managed components

Unlike [unmanaged components](components-unmanaged.md), managed components can store properties of any type. However, they're more resource intensive to store and access, and have the following restrictions:

* You can't access them in [jobs](https://docs.unity3d.com/Manual/JobSystem.html).
* You can't use them in [Burst](https://docs.unity3d.com/Packages/com.unity.burst@latest) compiled code.
* They require garbage collection.
* They must include a constructor with no parameters for serialization purposes.

## Managed type properties

If a property in a managed component uses a managed type, you might need to manually add the ability to clone, compare, and serialize the property.