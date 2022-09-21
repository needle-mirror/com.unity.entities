using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

// The files in this namespace are used to compile/test the code samples in the documentation.
// Snippets used in entity_command_buffer.md
namespace Doc.CodeSamples.Tests
{
    #region ecb_concurrent

    struct Lifetime : IComponentData
    {
        public byte Value;
    }

    [RequireMatchingQueriesForUpdate]
    partial class LifetimeSystem : SystemBase
    {
        EndSimulationEntityCommandBufferSystem m_EndSimulationEcbSystem;
        protected override void OnCreate()
        {
            base.OnCreate();
            // Find the ECB system once and store it for later usage
            m_EndSimulationEcbSystem = World
                .GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        }

        protected override void OnUpdate()
        {
            // Acquire an ECB and convert it to a concurrent one to be able
            // to use it from a parallel job.
            var ecb = m_EndSimulationEcbSystem.CreateCommandBuffer().AsParallelWriter();
            Entities
                .ForEach((Entity entity, int entityInQueryIndex, ref Lifetime lifetime) =>
            {
                // Track the lifetime of an entity and destroy it once
                // the lifetime reaches zero
                if (lifetime.Value == 0)
                {
                    // pass the entityInQueryIndex to the operation so
                    // the ECB can play back the commands in the right
                    // order
                    ecb.DestroyEntity(entityInQueryIndex, entity);
                }
                else
                {
                    lifetime.Value -= 1;
                }
            }).ScheduleParallel();

            // Make sure that the ECB system knows about our job
            m_EndSimulationEcbSystem.AddJobHandleForProducer(this.Dependency);
        }
    }
    #endregion

    public struct FooComp : IComponentData
    {
        public int Value;
    }

    public struct BarComp : IComponentData
    {
        public Entity TargetEnt;
    }

    [RequireMatchingQueriesForUpdate]
    partial class SingleThreadedSchedule_ECB : SystemBase
    {
        protected override void OnUpdate()
        {
#region ecb_single_threaded
// ... in a system update

// You don't specify a size because the buffer will grow as needed.
EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.TempJob);

// The ECB is captured by the ForEach job.
// Until completed, the job owns the ECB's job safety handle.
Entities
    .ForEach((Entity e, in FooComp foo) =>
    {
        if (foo.Value > 0)
        {
            // Record a command that will later add
            // BarComp to the entity.
            ecb.AddComponent<BarComp>(e);
        }
    }).Schedule();

this.Dependency.Complete();

// Now that the job is completed, you can enact the changes.
// Note that Playback can only be called on the main thread.
ecb.Playback(this.EntityManager);

// You are responsible for disposing of any ECB you create.
ecb.Dispose();
#endregion
        }
    }

    [RequireMatchingQueriesForUpdate]
    partial class MutliThreadedSchedule_ECB : SystemBase
    {
        protected override void OnUpdate()
        {
            {
#region ecb_multi_threaded
// ... in a system update

EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.TempJob);

// We need to write to the ECB concurrently across threads.
EntityCommandBuffer.ParallelWriter ecbParallel = ecb.AsParallelWriter();

// The entityInQueryIndex is unique for each entity and will be
// consistent for each particular entity regardless of scheduling.
Entities
    .ForEach((Entity e, int entityInQueryIndex, in FooComp foo) => {
        if (foo.Value > 0)
        {
            // The first arg is the 'sort key' recorded with the command.
            ecbParallel.AddComponent<BarComp>(entityInQueryIndex, e);
        }
    }).Schedule();

// Playback is single-threaded as normal.
this.Dependency.Complete();

// To ensure deterministic playback order,
// the commands are first sorted by their sort keys.
ecb.Playback(this.EntityManager);

ecb.Dispose();
#endregion
            }

            {
#region ecb_multi_playback
// ... in a system update

EntityCommandBuffer ecb =
        new EntityCommandBuffer(Allocator.TempJob, PlaybackPolicy.MultiPlayback);

// ... record commands

ecb.Playback(this.EntityManager);

// Additional playbacks are OK because this ECB is MultiPlayback.
ecb.Playback(this.EntityManager);

ecb.Dispose();
#endregion
            }

            {
#region ecb_from_ecbsystem
// ... in a system

// Assume an EntityCommandBufferSystem exists named FooECBSystem.
EntityCommandBufferSystem sys =
        this.World.GetExistingSystemManaged<FooECBSystem>();

// Create a command buffer that will be played back
// and disposed by MyECBSystem.
EntityCommandBuffer ecb = sys.CreateCommandBuffer();

// A ForEach with no argument to Schedule implicitly
// assigns its returned JobHandle to this.Dependency
Entities
    .ForEach((Entity e, in FooComp foo) => {
        // ... record to the ECB
    }).Schedule();

// Register the job so that it gets completed by the ECB system.
sys.AddJobHandleForProducer(this.Dependency);
#endregion
            }

            {
#region ecb_deferred_entities
// ... in a system

EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.TempJob);

Entity placeholderEntity = ecb.CreateEntity();

// Valid to use placeholderEntity in later commands of same ECB.
ecb.AddComponent<FooComp>(placeholderEntity);

// The real entity is created, and
// FooComp is added to the real entity.
ecb.Playback(this.EntityManager);

// Exception! The placeholderEntity has no meaning outside
// the ECB which created it, even after playback.
this.EntityManager.AddComponent<BarComp>(placeholderEntity);

ecb.Dispose();
#endregion
            }

            {
#region ecb_deferred_remapping
// ... in a system

EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.TempJob);

// For all entities with a FooComp component...
Entities
    .WithAll<FooComp>()
    .ForEach((Entity e) =>
    {
        // In playback, an actual entity will be created
        // that corresponds to this placeholder entity.
        Entity placeholderEntity = ecb.CreateEntity();

        // (Assume BarComp has an Entity field called TargetEnt.)
        BarComp bar = new BarComp { TargetEnt = placeholderEntity };

        // In playback, TargetEnt will be assigned the
        // actual Entity that corresponds to placeholderEntity.
        ecb.AddComponent(e, bar);
    }).Run();

// After playback, each entity with FooComp now has a
// BarComp component whose TargetEnt references a new entity.
ecb.Playback(this.EntityManager);

ecb.Dispose();
#endregion
            }

        }
    }

    public partial class FooSystem : SystemBase
    {
        protected override void OnUpdate()
        {
        }
    }

    public class FooECBSystem : EntityCommandBufferSystem {}

#region ecb_define_ecbsystem
// You should specify where exactly in the frame
// that the ECB system should update.
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(FooSystem))]
public class MyECBSystem : EntityCommandBufferSystem {
    // This class is intentionally empty. There is generally no
    // reason to put any code in an EntityCommandBufferSystem.
}
#endregion

#region ecb_addjobhandleforproducer
public struct ProcessInfo: IComponentData { public float Value; }
public struct ProcessCompleteTag : IComponentData {}

public partial class AsyncProcessJobSystem : SystemBase
{
    public partial struct ProcessInBackgroundJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ConcurrentCommands;

        public void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndex, in ProcessInfo info)
        {
            // ...hypothetical processing goes here...

            // Remove ProcessInfo and add a ProcessCompleteTag...
            ConcurrentCommands.RemoveComponent<ProcessInfo>(chunkIndex, entity);
            ConcurrentCommands.AddComponent(chunkIndex, entity, new ProcessCompleteTag());
        }
    }

    protected override void OnUpdate()
    {
        // Get a reference to the system that will play back
        var ecbSystem = World.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        var ecb = ecbSystem.CreateCommandBuffer();

        // Pass the command buffer to the writer job, using its ParallelWriter interface
        var job = new ProcessInBackgroundJob
        {
            ConcurrentCommands = ecb.AsParallelWriter(),
        };
        Dependency = job.ScheduleParallel(Dependency);

        // Register the writer job with the playback system as an input dependency
        ecbSystem.AddJobHandleForProducer(Dependency);
    }
}
#endregion
}
