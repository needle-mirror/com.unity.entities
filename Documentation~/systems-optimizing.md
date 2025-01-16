# Optimize systems

Every system has a fixed performance overhead. When you create a system it always has the following default behaviors:

* Each system accesses a structure called [`EntityTypeHandle`](xref:Unity.Entities.EntityManager.GetEntityTypeHandle) to iterate over the chunks that match an `EntityQuery`. Because `EntityTypeHandle` structs are invalidated by [structural changes](concepts-structural-changes.md), each system gets its own copy before each `OnUpdate`, so the CPU overhead of these accesses grows linearly with the number of active systems.  
* When scheduling or running a job, your code might need to get a [`ComponentLookup`](xref:Unity.Entities.ComponentLookup`1) or a [`BufferLookup`](xref:Unity.Entities.BufferLookup`1) to pass to the job. An application with a lot of systems might end up creating the same lookup structures in different systems. This means that those copies need to be updated every frame the system uses the lookup, which is necessary to account for possible [structural changes](concepts-structural-changes.md).
* Every system contains one [`JobHandle`](https://docs.unity3d.com/Documentation/ScriptReference/Unity.Jobs.JobHandle.html) named `Dependency`, either in the [`SystemState`](xref:Unity.Entities.SystemState.Dependency) object or the [`SystemBase`](xref:Unity.Entities.SystemBase.Dependency) class. There's overhead involved in calculating before executing the system, and in considering the system's scheduled jobs for the next system's `Dependency` handle. More systems in an application means more jobs and a more complex chain of `JobHandle` dependencies.

## Managing OnUpdate calls

You can add the [`[RequireMatchingQueriesForUpdate]`](xref:Unity.Entities.RequireMatchingQueriesForUpdateAttribute) attribute to the system to instruct it to only execute its `OnUpdate` method when there's data to process. However, the system still performs a check every frame to find if any entities match any of the queries the system uses. 

Systems that have matching entities run their `Update` methods, and systems with none don't update. The test is fast, but the time adds up in applications with many systems. As an alternative, you can use an `if` check at the top of `OnUpdate`, which can be faster than `[RequireMatchingQueriesForUpdate]`, depending on what you put in the check.

Also, avoid using the `[RequireMatchingQueriesForUpdate]` attribute on systems that don't run all or most of the time. For example, a system related to a player character doesn't need to check for matching queries because the player character exists all the time. However, a system related to a specific level of a game can use the `[RequireMatchingQueriesForUpdate]` attribute if it only needs to run at a certain point in your application. 

## Burst compiler behavior

If you use the [Burst compiler](https://docs.unity3d.com/Packages/com.unity.burst@latest), the overhead is lower on systems created with `ISystem`, than those created with `SystemBase`. To get the best performance while using Burst, use [`ISystem` based systems](systems-isystem.md).

## Additional resources

* [Organize system data](systems-data.md)
* [`[RequireMatchingQueriesForUpdate]` API documentation](xref:Unity.Entities.RequireMatchingQueriesForUpdateAttribute)