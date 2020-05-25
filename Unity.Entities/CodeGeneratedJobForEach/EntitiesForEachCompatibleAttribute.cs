using System;

namespace Unity.Entities.CodeGeneratedJobForEach
{
    /// <summary>
    /// This attribute can be applied to a delegate to indicate to the dots compiler that the dots compiler can assume instances of this delegate
    /// will never live longer than the method in which they are created. This allows the dots compiler to in some situations (like Entities.ForEach)
    /// to allocate the captured data for the closure on the stack instead of the heap, saving a GC allocation. There is currently no enforcement
    /// that the promise you make with this attribute it true, so be careful. The only reasonable usecase today for using this attribute
    /// is if you want to use your own delegate for Entities.ForEach that supports more parameters than Entities.ForEach supports out of the box
    /// </summary>
    [AttributeUsage(AttributeTargets.Delegate)]
    public class EntitiesForEachCompatibleAttribute : Attribute
    {
    }
}