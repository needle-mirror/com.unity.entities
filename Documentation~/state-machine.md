# Implement state machines

A state machine is a useful design pattern in game development. Basic operations such as enabling and disabling specific behaviors is a two state [finite-state machine (FSM)](https://en.wikipedia.org/wiki/Finite-state_machine). In Entities, you can implement an FSM in several ways, and each has its benefits and caveats. 

## Implementation approaches

The following are approaches for implementing an FSM with Entities:

* **Per-state data clustering**: Group entity data by its state in the same archetypes and iterate through entities in the same state.
* **Per-state data branching**: Group entity data by its state in the same archetypes and filter out entities by the state your want to process.
* **Per-FSM data branching**: Keep entities with different states in the same archetype and branch logic according to their state.


### Per-state data clustering

Group entity data according to its state in distinct [archetypes](concepts-archetypes.md) to efficiently iterate through all the entities in the same state. You can use the following approaches to implement per-state data clustering:

* [**Tag components**](components-tag.md): Add or remove empty components to put entities with the same state in the same archetype.
* [**Shared components**](components-shared.md): Group entities of an archetype into different chunks according to their shared component values.

### Per-state data branching

Keep entities with different states in the same [archetype chunks](concepts-archetypes.md#archetype-chunks), and filter the entities that aren't in the state you want to process. You can use the following approaches to implement per-state data branching:

* [**Enableable components**](components-enableable.md): Enable and disable components that represent the states so that queries don't match components and skip the entity from processing. 
* **Jobs per state**: Using one job per state to iterate over entities and check if they have the required active state, and skip the entities that don’t.

### Per-FSM data branching

Keep entities with different states in the same archetype chunks, and branch logic according to their state. To implement per-FSM data branching, use a value such as an enum to switch between different logic states in a single job.

## Implementation issues

The following issues in systems that schedule entity-iterating jobs might affect how you implement an FSM: 

* **Structural changes**: Using [structural changes](concepts-structural-changes.md) to toggle states, the number of entities that require toggling in the same frame impact performance. For more information about how structural changes impact performance, refer to [Managing structural changes](optimize-structural-changes.md).
* **Data fragmentation**: If you split entities into archetypes or chunks based on their state, having too many states leads to too many archetypes and chunks. A high number of chunks might lead to poor chunk use (more cache misses), and a high number of archetypes might also add overhead to query update and execution. For more information, refer to [Managing chunk allocations](performance-chunk-allocations.md).
* **Unneeded data fetching**: If you iterate over all entities in a chunk, but skip the ones that aren't in the desired state, Unity fetches unnecessary data. This requires fetching more cache lines to process all entities, compared to having all entities in the same state packed together in the same chunk.   
* **Repeated data fetching**: If you iterate over the same entities once per state, there's an overhead of iterating over the same entities several times. This also increases cache misses.  
* **Job overhead**: If you use one job per state, the [job scheduling overhead](job-overhead.md) scales with the number of states.
* **Complex dependencies:** If you use jobs that access most of the entity’s data at the same time, it can prevent other jobs from running in parallel. For more information, refer to [Job dependencies](scheduling-jobs-dependencies.md).  
* **Triggering reactive systems:** Accessing data with write access but not needing it can trigger performance-intensive recalculation costs. For more information about this refer to [Reactive systems](systems-data-granularity.md#reactive-systems). 

The following table compares the implementation issue types against the various implementation approaches, and also provides suggested tools for monitoring the issues:

|**Implementation issue**|**Approaches affected**|**How to monitor issue**|
|---|---|---|
|**Structural changes**|[Per state data clustering](#per-state-data-clustering). Likely to happen if entities change state often.|Use the [CPU Usage Profiler module](xref:um-profiler-cpu) and [Entities Structural Changes profiler module](profiler-module-structural-changes.md) to monitor structural changes.|
|**Data fragmentation**|[Per state data clustering](#per-state-data-clustering). Might happen if there are many states and few entities.|Use the [Archetype window](editor-archetypes-window.md) to measure chunk use. For more information, refer to [Managing chunk allocations](performance-chunk-allocations.md).|
|**Unneeded data fetching**|All approaches:<br/><br/>- [Per state data clustering](#per-state-data-clustering): Might happen if there’s high data fragmentation<br/>- [Per state data branching](#per-state-data-branching): Likely to happen if there are several chunks with few entities in the desired state, or if a high number of entities are in a idle or no-op state.<br/>- [Per-FSM data branching](#per-fsm-data-branching): Likely to happen if a high number of entities are in a idle or no-op state. Unity might fetch data for all states might also, unless you use `IJobChunk`. |Use cache miss native profilers such as VTune or Instruments.|
|**Repeated data fetching**|[Per state data branching](#per-state-data-branching). Likely to happen if there’s more than one state aside from the no-op state.|Use cache miss native profilers such as VTune or Instruments.|
|**Job overhead**|All approaches:<br/><br/>- [Per state data clustering](#per-state-data-clustering) and [Per state data branching](#per-state-data-branching): Might happen if several states aside from the no-op state exist.<br/>- [Per-FSM data branching](#per-fsm-data-branching): Minimal because the approach requires just one job.|Use native profilers to find scheduling methods. For more information, refer to [Job overhead](job-overhead.md).  | 
|**Complex dependencies**|All approaches:<br/><br/>- [Per state data clustering](#per-state-data-clustering) and [Per state data branching](#per-state-data-branching): Might happen if the jobs need access to most entities' components.<br/>- [Per-FSM data branching](#per-fsm-data-branching): Likely to happen if access to most entity’s components is needed in the FSM jobs.|Use the [Profiler](xref:um-profiler) to find job dependencies and idling systems. |
|**Triggering reactive systems**|[Per state data branching](#per-state-data-branching) and [Per-FSM data branching](#per-fsm-data-branching).<br/><br/> If using enableable components, this is likely to happen if there are several chunks with few entities in the desired state, but enableable components can skip entire chunks. For the other approaches, Unity bumps [version numbers](systems-version-numbers.md) for all entities.|Monitor reactive systems entities matching their queries in the [Systems window](editor-systems-window.md) and use entities [journaling](entities-journaling.md).|


You might encounter more issues if you use more than one FSM in the same entity because states might not be mutually exclusive. This might lead to data fragmentation and repeated data collection. You can split entities to simplify these cases. 

The code complexity of each approach might not provide enough performance gains to justify complex code. In this case, single-job branching is the simplest approach. To investigate the best performance approach, [profile your application](xref:um-profiler-collect-data) after implementing a state machine.

## Additional resources

* [Archetypes concepts](concepts-archetypes.md)
* [Manage chunk allocations](performance-chunk-allocations.md)
* [Optimize structural changes](optimize-structural-changes.md)
* [Unity Profiler](xref:um-profiler)