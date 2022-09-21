# Introducing unmanaged components

Unmanaged components store the most common data types which means they are useful in the majority of use-cases.

Unmanaged components can store properties of the following types:

* [Blittable types](https://docs.microsoft.com/en-us/dotnet/framework/interop/blittable-and-non-blittable-types)
* `bool`
* `char`
* `BlobAssetReference<T>` (a reference to a Blob data structure)
* `Collections.FixedString` (a fixed-sized character buffer)
* `Collections.FixedList`
* [Fixed array](https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/fixed-statement) (only allowed in an [unsafe](https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/unsafe) context)
* Other structs that conform to these same restrictions