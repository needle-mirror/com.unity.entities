using System;
using System.Diagnostics;
using Unity.Assertions;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{
    public unsafe partial struct EntityManager
    {
        // ----------------------------------------------------------------------------------------------------------
        // PUBLIC
        // ----------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Gets the name assigned to an entity.
        /// </summary>
        /// <remarks>For performance, entity names only exist when running in the Unity Editor.</remarks>
        /// <param name="entity">The Entity object of the entity of interest.</param>
        /// <returns>The entity name.</returns>
        [NotBurstCompatible]
        public string GetName(Entity entity)
        {
#if !DOTS_DISABLE_DEBUG_NAMES
            return GetCheckedEntityDataAccess()->GetName(entity);
#else
            return "";
#endif
        }

        [BurstCompatible]
        public void GetName(Entity entity, out FixedString64Bytes name)
        {
#if !DOTS_DISABLE_DEBUG_NAMES
            GetCheckedEntityDataAccess()->GetName(entity, out name);
#else
            name = default;
#endif
        }

        /// <summary>
        /// Sets the name of an entity, truncating the name if needed
        /// </summary>
        /// <remarks>For performance, entity names only exist when running in the Unity Editor.</remarks>
        /// <param name="entity">The Entity object of the entity to name.</param>
        /// <param name="name">The name to assign.The maximum length of an EntityName is 61 characters. if a string longer than 61
        /// characters is used a, the name will be truncated.</param>
        [NotBurstCompatible]
        public void SetName(Entity entity, string name)
        {
#if !DOTS_DISABLE_DEBUG_NAMES
            GetCheckedEntityDataAccess()->SetName(entity, name);
#endif
        }

        [BurstCompatible]
        public void SetName(Entity entity, FixedString64Bytes name)
        {
#if !DOTS_DISABLE_DEBUG_NAMES
            GetCheckedEntityDataAccess()->SetName(entity, name);
#endif
        }

        public enum GetAllEntitiesOptions
        {
            ExcludeMeta,
            IncludeMeta,
        }

        /// <summary>
        /// Gets all the entities managed by this EntityManager.
        /// </summary>
        /// <remarks>
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before getting the entities and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="allocator">The type of allocation for creating the NativeArray to hold the Entity objects.</param>
        /// <returns>An array of Entity objects referring to all the entities in the World.</returns>
        public NativeArray<Entity> GetAllEntities(Allocator allocator = Allocator.Temp)
            => GetAllEntities(allocator, GetAllEntitiesOptions.ExcludeMeta);

        /// <summary>
        /// Gets all the entities managed by this EntityManager.
        /// </summary>
        /// <remarks>
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before getting the entities and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="allocator">The type of allocation for creating the NativeArray to hold the Entity objects.</param>
        /// <param name="options">Specifies whether entities from chunk components should be included.</param>
        /// <returns>An array of Entity objects referring to all the entities in the World.</returns>
        public NativeArray<Entity> GetAllEntities(Allocator allocator, GetAllEntitiesOptions options)
        {
            BeforeStructuralChange();

            NativeArray<ArchetypeChunk> chunks = default;
            switch (options)
            {
                case GetAllEntitiesOptions.ExcludeMeta:
                    chunks = GetAllChunks();
                    break;
                case GetAllEntitiesOptions.IncludeMeta:
                    chunks = GetAllChunksAndMetaChunks();
                    break;
                default:
                    throw new ArgumentException($"Invalid enum value", nameof(options));
            }

            var count = ArchetypeChunkArray.CalculateEntityCount(chunks);
            var array = CollectionHelper.CreateNativeArray<Entity>(count, allocator);
            var entityType = GetEntityTypeHandle();
            var offset = 0;

            for (int i = 0; i < chunks.Length; i++)
            {
                var chunk = chunks[i];
                var entities = chunk.GetNativeArray(entityType);
                array.Slice(offset, entities.Length).CopyFrom(entities);
                offset += entities.Length;
            }

            chunks.Dispose();
            return array;
        }

        // @TODO document EntityManagerDebug
        /// <summary>
        /// Provides information and utility functions for debugging.
        /// </summary>
        public struct EntityManagerDebug
        {
            private readonly EntityManager m_Manager;

            public EntityManagerDebug(EntityManager entityManager)
            {
                m_Manager = entityManager;
            }

            public void PoisonUnusedDataInAllChunks(EntityArchetype archetype, byte value)
            {
                Unity.Entities.EntityComponentStore.AssertValidArchetype(m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore, archetype);

                for (var i = 0; i < archetype.Archetype->Chunks.Count; ++i)
                {
                    var chunk = archetype.Archetype->Chunks[i];
                    ChunkDataUtility.MemsetUnusedChunkData(chunk, value);
                }
            }

            public void SetGlobalSystemVersion(uint version)
            {
                m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore->SetGlobalSystemVersion(version);
            }

            public bool IsSharedComponentManagerEmpty()
            {
                return m_Manager.GetCheckedEntityDataAccess()->ManagedComponentStore.IsEmpty();
            }

#if !NET_DOTS
            internal static string GetArchetypeDebugString(Archetype* a)
            {
                var buf = new System.Text.StringBuilder();
                buf.Append("(");

                for (var i = 0; i < a->TypesCount; i++)
                {
                    var componentTypeInArchetype = a->Types[i];
                    if (i > 0)
                        buf.Append(", ");
                    buf.Append(componentTypeInArchetype.ToString());
                }

                buf.Append(")");
                return buf.ToString();
            }

#endif

            public int EntityCount
            {
                get
                {
                    var allEntities = m_Manager.GetAllEntities();
                    var count = allEntities.Length;
                    allEntities.Dispose();
                    return count;
                }
            }

            public bool UseMemoryInitPattern
            {
                get => m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore->useMemoryInitPattern != 0;
                set => m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore->useMemoryInitPattern = value ? (byte)1 : (byte)0;
            }

            public byte MemoryInitPattern
            {
                get => m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore->memoryInitPattern;
                set => m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore->memoryInitPattern = value;
            }

            internal Entity GetMetaChunkEntity(Entity entity)
            {
                return m_Manager.GetChunk(entity).m_Chunk->metaChunkEntity;
            }

            internal Entity GetMetaChunkEntity(ArchetypeChunk chunk)
            {
                return chunk.m_Chunk->metaChunkEntity;
            }

            public void LogEntityInfo(Entity entity) => Unity.Debug.Log(GetEntityInfo(entity));

#if UNITY_EDITOR
            internal string GetAllEntityInfo(bool includeComponentValues=false)
            {
                var str = new System.Text.StringBuilder();
                using (var arr = m_Manager.UniversalQuery.ToEntityArray(Allocator.Persistent))
                {
                    for (int i = 0; i < arr.Length; i++)
                    {
                        GetEntityInfo(arr[i], includeComponentValues, str);
                        str.AppendLine();
                    }
                }

                return str.ToString();
            }
#endif

            public string GetEntityInfo(Entity entity)
            {
                var entityComponentStore = m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore;

                if (entity.Index < 0 || entity.Index > entityComponentStore->EntitiesCapacity)
                {
                    return "Entity.Invalid";
                }
#if !NET_DOTS
                var str = new System.Text.StringBuilder();
                GetEntityInfo(entity, false, str);
                return str.ToString();
#else
                // @TODO Tiny really needs a proper string/stringutils implementation
                var archetype = entityComponentStore->GetArchetype(entity);
                string str = $"Entity {entity.Index}.{entity.Version}";
                for (var i = 0; i < archetype->TypesCount; i++)
                {
                    var componentTypeInArchetype = archetype->Types[i];
                    str += "  - {0}" + componentTypeInArchetype.ToString();
                }

                return str;
#endif
            }

#if !NET_DOTS
            internal void GetEntityInfo(Entity entity, bool includeComponentValues, System.Text.StringBuilder str)
            {
                var entityComponentStore = m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore;
                var archetype = entityComponentStore->GetArchetype(entity);
                str.Append(entity.ToString());
#if UNITY_EDITOR && !DOTS_DISABLE_DEBUG_NAMES
                {
                    var name = m_Manager.GetName(entity);
                    if (!string.IsNullOrEmpty(name))
                        str.Append($" (name '{name}')");
                }
#endif
                for (var i = 0; i < archetype->TypesCount; i++)
                {
                    var componentTypeInArchetype = archetype->Types[i];
                    str.AppendFormat("  - {0}", componentTypeInArchetype.ToString());
                }

#if UNITY_EDITOR
                if (includeComponentValues)
                {
                    for (var i = 0; i < archetype->TypesCount; i++)
                    {
                        var componentType = archetype->Types[i].ToComponentType();
                        if (componentType.IsBuffer || componentType.IsZeroSized || TypeManager.GetTypeInfo(componentType.TypeIndex).Category == TypeManager.TypeCategory.EntityData)
                            continue;
                        var comp = GetComponentBoxed(entity, componentType);
                        if (comp is UnityEngine.Object)
                            continue;
                        str.AppendLine();
                        str.AppendLine(componentType.ToString());
                        var json = UnityEngine.JsonUtility.ToJson(comp, true);
                        str.AppendLine(json);
                    }
                }
#endif
            }
#endif

            internal string Debugger_GetName(Entity entity)
            {
#if !DOTS_DISABLE_DEBUG_NAMES
                if (m_Manager.m_EntityDataAccess == null)
                    return null;

                return EntityComponentStore.Debugger_GetName(m_Manager.m_EntityDataAccess->EntityComponentStore, entity);
#else
                return "";
#endif
            }

            internal bool Debugger_Exists(Entity entity)
            {
                if (m_Manager.m_EntityDataAccess == null)
                    return false;
                return EntityComponentStore.Debugger_Exists(m_Manager.m_EntityDataAccess->EntityComponentStore, entity);
            }

            internal object[] Debugger_GetComponents(Entity entity)
            {
                var access = m_Manager.m_EntityDataAccess;
                if (access == null || !EntityComponentStore.Debugger_Exists(access->EntityComponentStore, entity))
                    return null;

                var archetype = access->EntityComponentStore->GetArchetype(entity);
                if (archetype == null || archetype->TypesCount <= 0 || archetype->TypesCount > 4096)
                    return null;

                // NOTE: First component is the entity itself
                var objects = new object[archetype->TypesCount-1];
                for (int i = 1; i < archetype->TypesCount; i++)
                    objects[i-1] = GetComponentBoxedUnchecked(access, entity, ComponentType.FromTypeIndex(archetype->Types[i].TypeIndex));

                return objects;
            }

            static object GetComponentBoxedUnchecked(EntityDataAccess* access, Entity entity, ComponentType type)
            {
                var typeInfo = TypeManager.GetTypeInfo(type.TypeIndex);
                if (typeInfo.Category == TypeManager.TypeCategory.ComponentData)
                {
                    if (TypeManager.IsManagedComponent(typeInfo.TypeIndex))
                    {
                        return access->Debugger_GetComponentObject(entity, type);
                    }

                    var src = EntityComponentStore.Debugger_GetComponentDataWithTypeRO(access->EntityComponentStore, entity, type.TypeIndex);
                    var obj = TypeManager.ConstructComponentFromBuffer(type.TypeIndex, src);

                    return obj;
                }
                else if (typeInfo.Category == TypeManager.TypeCategory.ISharedComponentData)
                {
                    var sharedComponentIndex = access->EntityComponentStore->Debugger_GetSharedComponentDataIndex(entity, type.TypeIndex);
                    if (sharedComponentIndex == -1)
                        return null;
                    return access->ManagedComponentStore.GetSharedComponentDataBoxed(sharedComponentIndex, type.TypeIndex);
                }
                else if (typeInfo.Category == TypeManager.TypeCategory.UnityEngineObject)
                {
                    return access->Debugger_GetComponentObject(entity, type);
                }
                else if (typeInfo.Category == TypeManager.TypeCategory.BufferData)
                {
                    var src = EntityComponentStore.Debugger_GetComponentDataWithTypeRO(access->EntityComponentStore, entity, type.TypeIndex);
                    var header = (BufferHeader*) src;
                    if (header == null || header->Length < 0)
                        return null;

                    int length = header->Length;

#if !NET_DOTS
                    System.Array array = Array.CreateInstance(TypeManager.GetType(type.TypeIndex), length);
#else
                    // no Array.CreateInstance in Tiny BCL
                    // This unfortunately means that the debugger display for this will be object[], because we can't
                    // create an array of the right type.  But better than nothing, since the values are still viewable.
                    var array = new object[length];
#endif

                    var elementSize = TypeManager.GetTypeInfo(type.TypeIndex).ElementSize;
                    byte* basePtr = BufferHeader.GetElementPointer(header);

#if !UNITY_DOTSRUNTIME
                    var dstPtr = UnsafeUtility.PinGCArrayAndGetDataAddress(array, out var handle);
                    UnsafeUtility.MemCpy(dstPtr, basePtr, elementSize * length);
                    UnsafeUtility.ReleaseGCObject(handle);
#else
                    // DOTS Runtime doesn't have PinGCArrayAndGetDataAddress, because that's in Unity's Mono impl only
                    for (int i = 0; i < length; i++)
                    {
                        var item = TypeManager.ConstructComponentFromBuffer(type.TypeIndex, basePtr + elementSize * i);
                        #if !NET_DOTS
                        array.SetValue(item, i);
                        #else
                        array[i] = item;
                        #endif
                    }
#endif
                    return array;
                }
                else
                {
                    return null;
                }
            }

            public object GetComponentBoxed(Entity entity, ComponentType type)
            {
                var access = m_Manager.GetCheckedEntityDataAccess();

                access->EntityComponentStore->AssertEntitiesExist(&entity, 1);

                if (!access->HasComponent(entity, type))
                    throw new ArgumentException($"Component of type {type} does not exist on the entity.");

                return GetComponentBoxedUnchecked(access, entity, type);
            }

            public object GetComponentBoxed(Entity entity, Type type)
            {
                var access = m_Manager.GetCheckedEntityDataAccess();

                access->EntityComponentStore->AssertEntitiesExist(&entity, 1);

                var archetype = access->EntityComponentStore->GetArchetype(entity);
                var typeIndex = ChunkDataUtility.GetTypeIndexFromType(archetype, type);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (typeIndex == -1)
                    throw new ArgumentException($"A component with type:{type} has not been added to the entity.");
#endif

                return GetComponentBoxedUnchecked(access, entity, ComponentType.FromTypeIndex(typeIndex));
            }

#if UNITY_EDITOR
            /// <summary>
            /// Returns the Authoring object for the entity. Returns null if the authoring object is not available.
            /// For example closed subscenes will always return null.
            /// </summary>
            public UnityEngine.Object GetAuthoringObjectForEntity(Entity entity)
            {
                if (m_Manager.HasComponent<EntityGuid>(entity))
                    return UnityEditor.EditorUtility.InstanceIDToObject(m_Manager.GetComponentData<EntityGuid>(entity).OriginatingId);

                return null;
            }

            [BurstCompile]
            struct BuildInstanceIDToEntityIndex : IJobEntityBatch
            {
                public UnsafeParallelMultiHashMap<int, Entity>.ParallelWriter EntityLookup;
                [ReadOnly]
                public ComponentTypeHandle<EntityGuid>                GuidType;
                [ReadOnly]
                public EntityTypeHandle                               EntityType;

                public void Execute(ArchetypeChunk chunk, int batchIndex)
                {
                    var entities = chunk.GetNativeArray(EntityType);
                    var guids = chunk.GetNativeArray(GuidType);

                    for (int i = 0; i != entities.Length; i++)
                        EntityLookup.Add(guids[i].OriginatingId, entities[i]);
                }
            }

            /// <summary>
            /// Lists all entities in this world that were converted from or are associated with the game object.
            /// </summary>
            public void GetEntitiesForAuthoringObject(UnityEngine.GameObject gameObject, NativeList<Entity> entities)
            {
                GetEntitiesForAuthoringObject((UnityEngine.Object)gameObject, entities);
            }

            /// <summary>
            /// Lists all entities in this world that were converted from or are associated with the game object.
            /// </summary>
            public void GetEntitiesForAuthoringObject(UnityEngine.Component component, NativeList<Entity> entities)
            {
                GetEntitiesForAuthoringObject((UnityEngine.Object)component.gameObject, entities);
            }
            /// <summary>
            /// Lists all entities in this world that were converted from or are associated with the given object.
            /// </summary>
            public void GetEntitiesForAuthoringObject(UnityEngine.Object obj, NativeList<Entity> entities)
            {
                var instanceID = obj.GetInstanceID();
                var lookup = GetCachedEntityGUIDToEntityIndexLookup();

                entities.Clear();
                foreach (var e in lookup.GetValuesForKey(instanceID))
                    entities.Add(e);
            }

            internal UnsafeParallelMultiHashMap<int, Entity> GetCachedEntityGUIDToEntityIndexLookup()
            {
                var access = m_Manager.GetCheckedEntityDataAccess();
                var newVersion = m_Manager.GetComponentOrderVersion<EntityGuid>();
                if (access->m_CachedEntityGUIDToEntityIndexVersion != newVersion)
                {
                    access->CachedEntityGUIDToEntityIndex.Clear();
                    var count = access->m_EntityGuidQuery.CalculateEntityCount();
                    if (access->CachedEntityGUIDToEntityIndex.Capacity < count)
                        access->CachedEntityGUIDToEntityIndex.Capacity = count;

                    new BuildInstanceIDToEntityIndex
                    {
                        EntityLookup = access->CachedEntityGUIDToEntityIndex.AsParallelWriter(),
                        GuidType = m_Manager.GetComponentTypeHandle<EntityGuid>(true),
                        EntityType = m_Manager.GetEntityTypeHandle()
                    }.ScheduleParallel(access->m_EntityGuidQuery).Complete();
                    access->m_CachedEntityGUIDToEntityIndexVersion = newVersion;
                }

                return access->CachedEntityGUIDToEntityIndex;
            }
#endif

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            public void CheckInternalConsistency()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                // Note that this is awkwardly written to avoid all safety checks except "we were created".
                // This is so unit tests can run out of the test body with jobs running and exclusive transactions still opened.
                AtomicSafetyHandle.CheckExistsAndThrow(m_Manager.m_Safety);

                var eda = m_Manager.m_EntityDataAccess;
                var mcs = eda->ManagedComponentStore;

                //@TODO: Validate from perspective of chunkquery...
                if (false == eda->EntityComponentStore->IsIntentionallyInconsistent)
                    eda->EntityComponentStore->CheckInternalConsistency(mcs.m_ManagedComponentData);

                Assert.IsTrue(mcs.AllSharedComponentReferencesAreFromChunks(eda->EntityComponentStore));
                mcs.CheckInternalConsistency();

                var chunkHeaderType = new ComponentType(typeof(ChunkHeader));
                var chunkQuery = eda->EntityQueryManager->CreateEntityQuery(eda, &chunkHeaderType, 1);

                int totalEntitiesFromQuery = eda->m_UniversalQuery.CalculateEntityCount() + chunkQuery.CalculateEntityCount();
                Assert.AreEqual(eda->EntityComponentStore->CountEntities(), totalEntitiesFromQuery);

                chunkQuery.Dispose();
#endif
            }
        }

        internal Entity GetEntityByEntityIndex(int index)
        {
            return GetCheckedEntityDataAccess()->GetEntityByEntityIndex(index);
        }

        internal int GetNameIndexByEntityIndex(int index)
        {
            return GetCheckedEntityDataAccess()->GetNameIndexByEntityIndex(index);
        }
    }
}
