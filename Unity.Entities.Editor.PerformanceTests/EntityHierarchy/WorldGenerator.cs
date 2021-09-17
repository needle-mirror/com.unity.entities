using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Profiling;

namespace Unity.Entities.Editor.PerformanceTests
{
    abstract class WorldGenerator : IDisposable
    {
        static readonly ProfilerMarker k_InitializeWorldMarker = new ProfilerMarker($"{nameof(WorldGenerator)}: Initialize World");
        static readonly ProfilerMarker k_CloneWorldMarker = new ProfilerMarker($"{nameof(WorldGenerator)}: Clone World");
        static readonly ProfilerMarker k_GenerateArchetypeMarker = new ProfilerMarker($"{nameof(WorldGenerator)}: Generate Archetype");

        static readonly ComponentType[][] k_ArchetypeVariants =
        {
            new ComponentType[] {typeof(SegmentId)},

            new ComponentType[] {typeof(SegmentId), typeof(ArchetypeMarker1)},
            new ComponentType[] {typeof(SegmentId), typeof(ArchetypeMarker2)},
            new ComponentType[] {typeof(SegmentId), typeof(ArchetypeMarker3)},
            new ComponentType[] {typeof(SegmentId), typeof(ArchetypeMarker4)},

            new ComponentType[] {typeof(SegmentId), typeof(ArchetypeMarker1), typeof(ArchetypeMarker2)},
            new ComponentType[] {typeof(SegmentId), typeof(ArchetypeMarker1), typeof(ArchetypeMarker3)},
            new ComponentType[] {typeof(SegmentId), typeof(ArchetypeMarker1), typeof(ArchetypeMarker4)},
            new ComponentType[] {typeof(SegmentId), typeof(ArchetypeMarker2), typeof(ArchetypeMarker3)},
            new ComponentType[] {typeof(SegmentId), typeof(ArchetypeMarker2), typeof(ArchetypeMarker4)},
            new ComponentType[] {typeof(SegmentId), typeof(ArchetypeMarker3), typeof(ArchetypeMarker4)},

            new ComponentType[] {typeof(SegmentId), typeof(ArchetypeMarker1), typeof(ArchetypeMarker2), typeof(ArchetypeMarker3)},
            new ComponentType[] {typeof(SegmentId), typeof(ArchetypeMarker1), typeof(ArchetypeMarker2), typeof(ArchetypeMarker4)},
            new ComponentType[] {typeof(SegmentId), typeof(ArchetypeMarker1), typeof(ArchetypeMarker3), typeof(ArchetypeMarker4)},
            new ComponentType[] {typeof(SegmentId), typeof(ArchetypeMarker2), typeof(ArchetypeMarker3), typeof(ArchetypeMarker4)},

            new ComponentType[] {typeof(SegmentId), typeof(ArchetypeMarker1), typeof(ArchetypeMarker2), typeof(ArchetypeMarker3), typeof(ArchetypeMarker4)}
        };

        World m_World;
        List<World> m_Clones = new List<World>();

        int m_ArchetypeSequenceNumber;

        // TODO: Useful?
        // Probably useful to test cloning
        public World Original
        {
            get
            {
                if (m_World == null || !m_World.IsCreated)
                    InitializeWorld();
                return m_World;
            }
        }

        public World Get()
        {
            if (m_World == null || !m_World.IsCreated)
                InitializeWorld();

            var clone = new World($"{m_World.Name} (Clone {m_Clones.Count.ToString()})");

            using (k_CloneWorldMarker.Auto())
            {
                clone.EntityManager.CopyAndReplaceEntitiesFrom(m_World.EntityManager);
            }

            m_Clones.Add(clone);

            return clone;
        }

        void InitializeWorld()
        {
            using (k_InitializeWorldMarker.Auto())
            {
                m_World = GenerateWorld();
            }
        }

        public virtual void Dispose()
        {
            if (m_World != null && m_World.IsCreated)
                m_World.Dispose();

            foreach (var clone in m_Clones)
            {
                if (clone != null && clone.IsCreated)
                    clone.Dispose();
            }
        }

        protected abstract World GenerateWorld();

        protected ComponentType[] GetNextArchetypeVariant(int maximumVariantsCount, params ComponentType[] componentTypes)
        {
            using (k_GenerateArchetypeMarker.Auto())
            {
                m_ArchetypeSequenceNumber %= math.clamp(maximumVariantsCount, 1, k_ArchetypeVariants.Length);
                var variantTypes = k_ArchetypeVariants[m_ArchetypeSequenceNumber++];

                var originalCount = componentTypes.Length;
                var variantComponentsCount = variantTypes.Length;
                var result = new ComponentType[originalCount + variantComponentsCount];
                Array.Copy(componentTypes, 0, result, 0, originalCount);
                Array.Copy(variantTypes, 0, result, originalCount, variantComponentsCount);

                return result;
            }
        }
    }

    public struct ArchetypeMarker1 : IComponentData { }
    public struct ArchetypeMarker2 : IComponentData { }
    public struct ArchetypeMarker3 : IComponentData { }
    public struct ArchetypeMarker4 : IComponentData { }

    public struct SegmentId : ISharedComponentData
    {
        // ReSharper disable once NotAccessedField.Global
        public int Value;
    }
}
