# Prebuilt custom allocators

You can use prebuilt custom allocators to manage allocations in [worlds](concepts-worlds.md), [entity command buffers](systems-entity-command-buffers.md), and [system groups](systems-write-groups.md). All of the following are [rewindable allocators](https://docs.unity3d.com/Packages/com.unity.collections@latest/index.html?subfolder=/manual/allocator-rewindable.html):

* [World update allocator](#world-update-allocator): A double rewindable allocator that a world owns, which is fast and thread safe. The allocations the world update allocator makes are automatically freed every 2 frames, which makes it useful for allocations within a world that last for 2 frames. This allocator has no memory leakage.
* [Entity command buffer allocator](#entity-command-buffer-allocator): A rewindable allocator that an entity command buffer system owns, which is fast and thread safe. An entity command buffer system uses this allocator to create entity command buffers. Allocations are automatically freed after an entity command buffer is played back. This allocator has no memory leakage.
* [System group allocator for rate manager](#system-group-allocator-for-rate-manager): An optional double rewindable allocator that a component system group creates when setting its rate manager. It's useful for allocations in a system of fixed or variable rate system group that tick at different rate from the world update. Allocations last for 2 system group updates and you don't need to manually free the allocations.

These prebuilt allocators are custom allocators. To allocate and deallocate `Native-` collection types and `Unsafe-` collection types see the Collections package documentation on [How to use a custom allocator](https://docs.unity3d.com/Packages/com.unity.collections@latest/index.html?subfolder=/manual/allocator-custom-use.html).

## World update allocator

The world update allocator is a rewindable allocator that gets automatically rewound during every world update. Every world contains double rewindable allocators that are created when the world is initiated. The system [`WorldUpdateAllocatorResetSystem`](xref:Unity.Entities.WorldUpdateAllocatorResetSystem) switches the double rewindable allocators in every world update. After an allocator swaps in, it rewinds the allocator. Because of this, the lifetime of an allocation from a world update allocator spans two frames. You don't need to manually free the allocations, so there isn't any memory leakage.

You can pass allocations from the world update allocator into a job. You can access the world update allocator through:

* [`World.UpdateAllocator`](xref:Unity.Entities.World.UpdateAllocator)
* [`ComponentSystemBase.WorldUpdateAllocator`](xref:Unity.Entities.ComponentSystemBase.WorldUpdateAllocator)
* [`SystemState.WorldUpdateAllocator`](xref:Unity.Entities.SystemState.WorldUpdateAllocator)

The following is an example of accessing the world update allocator through a world:

[!code-cs[Access world update allocator through World.UpdateAllocator](../Unity.Entities.Tests/AllocatorsCustomPrebuiltTests.cs#world-update-allocator-world)]

The following is an example of accessing the world update allocator through SystemBase:

[!code-cs[Access world update allocator through SystemBase.WorldUpdateAllocator](../Unity.Entities.Tests/AllocatorsCustomPrebuiltTests.cs#world-update-allocator-system-base)]

The following is an example of accessing the world update allocator through SystemState:

[!code-cs[Access world update allocator through SystemState.WorldUpdateAllocator](../Unity.Entities.Tests/AllocatorsCustomPrebuiltTests.cs#world-update-allocator-system-state)]


## Entity command buffer allocator

The entity command buffer allocator is a custom rewindable allocator. Each [entity command buffer](systems-entity-command-buffers.md) system creates an entity command buffer allocator when the system is created. The life span of an allocation from an entity command buffer allocator is the same as the entity command buffer.  

If you use [`EntityCommandBufferSystem.CreateCommandBuffer()`](xref:Unity.Entities.EntityCommandBufferSystem.CreateCommandBuffer*) to create an entity command buffer, the entity command buffer allocator allocates memory during the recording of the entity command buffer and deallocates the memory after the buffer is played back.

You register a unmanaged singleton that implements [`IECBSingleton`](xref:Unity.Entities.IECBSingleton) through [`ECBExtensionMethods.RegisterSingleton`](xref:Unity.Entities.ECBExtensionMethods.RegisterSingleton*). During the registration, the entity command buffer allocator of the parent entity command buffer system is set to the singleton's allocator. Therefore, the singleton's entity command buffers are all allocated from this allocator and they're deallocated after the buffers are played back.

The entity command buffer allocator works in the background, and you don't need to make specific code changes to use it.
   
## System group allocator for rate manager

Each [`ComponentSystemGroup`](xref:Unity.Entities.ComponentSystemGroup) has an option to create a system group allocator when setting its rate manager. To create a system group allocator when setting a rate manger, use [`ComponentSystemGroup.SetRateManagerCreateAllocator`](xref:Unity.Entities.ComponentSystemGroup.SetRateManagerCreateAllocator*).  If you use the property [`RateManager`](xref:Unity.Entities.ComponentSystemGroup.RateManager*) to set a rate manager in the system group, no system group allocator is created. The following example uses `ComponentSystemGroup.SetRateManagerCreateAllocator` to set a rate manager and create a system group allocator:

```c#
[WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor | WorldSystemFilterFlags.ThinClientSimulation)]
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup), OrderFirst = true)]
public class FixedStepTestSimulationSystemGroup : ComponentSystemGroup
{
    // Set the timestep use by this group, in seconds. The default value is 1/60 seconds.
    // This value will be clamped to the range [0.0001f ... 10.0f].
    public float Timestep
    {
        get => RateManager != null ? RateManager.Timestep : 0;
        set
        {
            if (RateManager != null)
                RateManager.Timestep = value;
        }
    }
    // Default constructor
    public FixedStepTestSimulationSystemGroup()
    {
        float defaultFixedTimestep = 1.0f / 60.0f;
        // Set FixedRateSimpleManager to be the rate manager and create a system group allocator
        SetRateManagerCreateAllocator(new RateUtils.FixedRateSimpleManager(defaultFixedTimestep));
    }
}
```            

The component system group that creates a system group allocator contains double rewindable allocators. [`World.SetGroupAllocator`](xref:Unity.Entities.World.SetGroupAllocator*) and [`World.RestoreGroupAllocator`](xref:Unity.Entities.World.RestoreGroupAllocator*) are used in [`IRateManager.ShouldGroupUpdate`](xref:Unity.Entities.IRateManager.ShouldGroupUpdate*) to replace the world update allocator with the system group allocator, and later to restore back the world update allocator. 

The example below shows how to use `World.SetGroupAllocator` and `World.RestoreGroupAllocator`:

```c#
public unsafe class FixedRateSimpleManager : IRateManager
{
    float m_FixedTimestep;
    public float Timestep
    {
        get => m_FixedTimestep;
        set => m_FixedTimestep = math.clamp(value, MinFixedDeltaTime, MaxFixedDeltaTime);
    }
    double m_LastFixedUpdateTime;
    bool m_DidPushTime;
    
    DoubleRewindableAllocators* m_OldGroupAllocators = null;
    public FixedRateSimpleManager(float fixedDeltaTime)
    {
        Timestep = fixedDeltaTime;
    }
    public bool ShouldGroupUpdate(ComponentSystemGroup group)
    {
        // if this is true, means we're being called a second or later time in a loop.
        if (m_DidPushTime)
        {
            group.World.PopTime();
            m_DidPushTime = false;
            // Update the group allocators and restore the old allocator
            group.World.RestoreGroupAllocator(m_OldGroupAllocators);
            return false;
        }
        group.World.PushTime(new TimeData(
            elapsedTime: m_LastFixedUpdateTime,
            deltaTime: m_FixedTimestep));
        m_LastFixedUpdateTime += m_FixedTimestep;
        m_DidPushTime = true;
        // Back up current world or group allocator.
        m_OldGroupAllocators = group.World.CurrentGroupAllocators;
        // Replace current world or group allocator with this system group allocator.
        group.World.SetGroupAllocator(group.RateGroupAllocators);
        return true;
    }
}
```

The system group allocator contains double rewindable allocators and works in the same way as the world update allocator. Before a system group proceeds to its update, its system group allocator is put in the world update allocator, and allocations from the world update allocator are allocated from the system group allocator. If the system group skips its update, it switches the double rewindable allocators, rewinds the one that swaps in, and then brings back the world update allocator.  Because this is a double rewindable allocator, the lifetime of an allocation from a system group allocator lasts two system group updates. You don't need to manually free the allocations, so there isn't any memory leakage.

In the example below, the system group allocator is used in `ExampleSystemGroupAllocatorSystem` which is in a fixed rate system group that has a rate manager `FixedRateSimpleManager` as shown above.

[!code-cs[world-update-allocator-worl](../Unity.Entities.Tests/AllocatorsCustomPrebuiltTests.cs#world-update-allocator-system-state)]


## Further information

* [Rewindable allocators](https://docs.unity3d.com/Packages/com.unity.collections@latest/index.html?subfolder=/manual/allocator-rewindable.html)