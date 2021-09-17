---
uid: blobassets
---

# Blob assets
Blob assets are pieces of binary data that are optimized for streaming. Blob is short for Binary Large Object. By writing your data into a blob asset, you are storing it in a format that can be loaded efficiently and referenced from a component stored on an entity. Like struct components, blob assets must not contain any managed data: You cannot use regular arrays, strings, or any other managed object in a blob asset. Blob assets should only contain read-only data that does not change at runtime: they can be accessed from multiple threads at once and (unlike native containers) have no safety checks against concurrent writes.

In order to quickly load blob assets, it is necessary that their data is _relocatable_: The meaning of the data in the blob asset must not change when you copy the whole blob asset to another memory address. This implies that blob assets may not contain absolute references to itself, which precludes the use of internal pointers. Any information you would usually store via a pointer must instead be referenced via an offset relative to the memory address of the blob asset itself. This mostly applies to storing strings and arrays. The details of this indirection using offsets instead of absolute pointers impacts interaction with blob assets in two ways:
1. Blob assets must be created using a [BlobBuilder](xref:Unity.Entities.BlobBuilder). This type takes care of computing the relative offsets for you.
2. Blob assets must always be accessed and passed by reference using the `ref` keyword or using [BlobAssetReference](xref:Unity.Entities.BlobAssetReference`1). This is necessary to ensure that any relative offsets within the blob asset still resolve to the right absolute address. The issue is again relocation: Blob assets can be relocated as a whole in memory, but accessing them by value instead of by reference does not in general guarantee that the whole blob asset is copied.

> [!NOTE]
> You will get a compiler error should you try to use a blob asset containing internal pointers by value.

## Creating blob assets
Creating a blob asset always involves at least four steps:
1. Create a [BlobBuilder](xref:Unity.Entities.BlobBuilder). This needs to allocate some memory internally.
1. Construct the root of the blob asset using [BlobBuilder.ConstructRoot](xref:Unity.Entities.BlobBuilder.ConstructRoot*)
1. Fill the structure with your data.
1. Create a [BlobAssetReference](xref:Unity.Entities.BlobAssetReference`1) using [BlobBuilder.CreateBlobAssetReference](xref:Unity.Entities.BlobBuilder.CreateBlobAssetReference*). This copies the blob asset to the final location.
1. Dispose the blob builder allocated in step 1.

For example, here we are storing a struct with only primitive members as a blob asset:
[!code-cs[blobs](../DocCodeSamples.Tests/BlobAssetExamples.cs#CreateSimpleBlobAsset)]

The role of the blob builder is to construct the data stored in the blob asset, make sure that all internal references are stored as offsets, and finally copy the finished blob asset into a single allocation referenced by the returned `BlobAssetReference<T>`.

### Using `BlobArray`
Arrays within blob assets need special treatment because they are implemented using relative offsets internally. This is implemented using the [BlobArray](xref:Unity.Entities.BlobArray) type. Here is how to allocate an array of blob data and fill it:

[!code-cs[blobs](../DocCodeSamples.Tests/BlobAssetExamples.cs#CreateBlobAssetWithArray)]

### Using `BlobString`
Strings have the same problems as arrays and have custom support using [BlobString](xref:Unity.Entities.BlobString). They are similarly allocated using the `BlobBuilder` API.
[!code-cs[blobs](../DocCodeSamples.Tests/BlobAssetExamples.cs#CreateBlobAssetWithString)]

### Using `BlobPtr`
Should you need to manually set an internal pointer, you can use the [BlobPtr<T>](xref:Unity.Entities.BlobPtr`1) type.
[!code-cs[blobs](../DocCodeSamples.Tests/BlobAssetExamples.cs#CreateBlobAssetWithInternalPointer)]

## Accessing blob assets on a component
Once you have obtained a `BlobAssetReference<T>` to a blob asset, you can store this reference on component and access it. Note that all parts of a blob asset that contain internal pointers must be accessed by reference.
[!code-cs[blobs](../DocCodeSamples.Tests/BlobAssetExamples.cs#BlobAssetOnAComponent)]

## When do I need to dispose a blob asset reference?
All blob asset that were allocated at runtime using [BlobBuilder.CreateBlobAssetReference](xref:Unity.Entities.BlobBuilder.CreateBlobAssetReference`1) need to be manually disposed. This is different for blob assets that were loaded as part of an entity scene loaded from disk: All of these blob assets are reference counted and automatically released once no component references them anymore. They must not be manually disposed.

## Debugging blob asset contents
Blob assets implement internal references using relative offsets. This means that copying a `BlobString` struct (or any other type with these internal references) will copy the relative offset contained, but not what it is pointing to. The result of this is an unusable `BlobString` that will represents an essentially random string of characters. While this is easy to avoid in your own code, debugging utilities will often do exactly that. Therefore the contents of a `BlobString` cannot be shown correctly in a debugger.

However, there is support for displaying the values of a `BlobAssetReference<T>` and all of its contents. If you want to look up the contents of a `BlobString`, navigate to the containing `BlobAssetReference<T>` and start debugging from there.
