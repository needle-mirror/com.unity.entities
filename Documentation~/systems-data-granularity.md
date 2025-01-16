# Data granularity

The best way to get fine-grained queries is to construct entities from many small components, rather than using a small number of large components. Small components make efficient use of the CPU cache, given that cache lines only contain the data you need. Larger components might fetch unnecessary fields. 

However, excessive component granularity might introduce significant overhead when dealing with [entity queries](systems-entityquery.md), [archetypes](concepts-archetypes.md), and other internal processes because there's a larger number of components to consider.

## Read-only data

Declare any data that's used as input but not modified during a job as [`ReadOnly`](xref:Unity.Collections.ReadOnlyAttribute). This allows you to safely parallelize your system’s jobs that read that data. It also gives the job scheduler more options to figure out how to arrange scheduled jobs from different systems and scripts, which gets the most efficient usage of the available CPU threads. 

If a given piece of data is permanently immutable for the lifetime of your application, you can store it in a [blob asset](blob-assets-intro.md). A blob asset is an immutable data structure stored in unmanaged memory which can contain structs and arrays of blittable data, and strings in the form of [`BlobString`](xref:Unity.Entities.BlobString). 

Unity stores blob assets on disk in the exact way that they're stored in memory, so systems can deserialize them much more quickly than assets such as ScriptableObjects. Blob assets are not stored in chunks which means that they don’t contribute to chunk fragmentation, so they don’t get in the way of processing mutable component data.

For more information on blob assets, refer to [Blob assets concepts](blob-assets-concept.md).

## Reactive systems

Declaring data as read-only is important in projects which contain reactive systems. These are systems whose queries uses [change filters](systems-entityquery-filters.md).

Iterating component data with read/write access marks the iterated chunks and all their entities as changed. This makes the reactive systems run, even if the data wasn't modified. You should separate data that's read-only into different components from data which is read/write to avoid running reactive systems needlessly.

You can use the [`IJobEntityChunkBeginEnd`](xref:Unity.Entities.IJobEntityChunkBeginEnd) interface in `IJobEntity` jobs to pre-evaluate the [chunk](concepts-archetypes.md#archetype-chunks) and prevent triggering reactive systems, even if you need write access to the components. This allows you to skip the chunk before requesting write access and mark the data as changed by increasing the internal [version number](systems-version-numbers.md).  

To understand the best approach for your application, use the [Profiler](xref:um-profiler) to capture data that uses reactive systems and data that doesn't use reactive systems, and then compare them.

## Additional resources

* [Organize system data](systems-data.md)
* [Blob assets concepts](blob-assets-concept.md)