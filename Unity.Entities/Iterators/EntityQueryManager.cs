using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine.Assertions;

namespace Unity.Entities
{
    [BurstCompatible]
    internal unsafe struct EntityQueryManager
    {
        private ComponentDependencyManager*    m_DependencyManager;
        private BlockAllocator                 m_GroupDataChunkAllocator;
        private UnsafePtrList<EntityQueryData> m_EntityGroupDatas;

        private UntypedUnsafeParallelHashMap           m_EntityGroupDataCacheUntyped;
        internal int                           m_EntityQueryMasksAllocated;

        public static void Create(EntityQueryManager* queryManager, ComponentDependencyManager* dependencyManager)
        {
            queryManager->m_DependencyManager = dependencyManager;
            queryManager->m_GroupDataChunkAllocator = new BlockAllocator(AllocatorManager.Persistent, 16 * 1024 * 1024); // 16MB should be enough
            ref var groupCache = ref UnsafeUtility.As<UntypedUnsafeParallelHashMap, UnsafeParallelMultiHashMap<int, int>>(ref queryManager->m_EntityGroupDataCacheUntyped);
            groupCache = new UnsafeParallelMultiHashMap<int, int>(1024, Allocator.Persistent);
            queryManager->m_EntityGroupDatas = new UnsafePtrList<EntityQueryData>(0, Allocator.Persistent);
            queryManager->m_EntityQueryMasksAllocated = 0;
        }

        public static void Destroy(EntityQueryManager* manager)
        {
            manager->Dispose();
        }

        void Dispose()
        {
            ref var groupCache = ref UnsafeUtility.As<UntypedUnsafeParallelHashMap, UnsafeParallelMultiHashMap<int, int>>(ref m_EntityGroupDataCacheUntyped);
            groupCache.Dispose();
            for (var g = 0; g < m_EntityGroupDatas.Length; ++g)
            {
                m_EntityGroupDatas.Ptr[g]->Dispose();
            }
            m_EntityGroupDatas.Dispose();
            //@TODO: Need to wait for all job handles to be completed..
            m_GroupDataChunkAllocator.Dispose();
        }

        ArchetypeQuery* CreateQuery(ref UnsafeScratchAllocator unsafeScratchAllocator, ComponentType* requiredTypes, int count)
        {
            var builder = new EntityQueryDescBuilder(Allocator.Temp);

            for (int i = 0; i != count; i++)
            {
                if (requiredTypes[i].AccessModeType == ComponentType.AccessMode.Exclude)
                    builder.AddNone(ComponentType.ReadOnly(requiredTypes[i].TypeIndex));
                else
                    builder.AddAll(requiredTypes[i]);
            }

            builder.FinalizeQuery();

            var result = CreateQuery(ref unsafeScratchAllocator, builder);

            builder.Dispose();

            return result;
        }

        internal void ConstructTypeArray(ref UnsafeScratchAllocator unsafeScratchAllocator, UnsafeList<ComponentType> types, out int* outTypes, out byte* outAccessModes, out int outLength)
        {
            if (types.Length == 0)
            {
                outTypes = null;
                outAccessModes = null;
                outLength = 0;
            }
            else
            {
                outLength = types.Length;
                outTypes = (int*)unsafeScratchAllocator.Allocate<int>(types.Length);
                outAccessModes = (byte*)unsafeScratchAllocator.Allocate<byte>(types.Length);

                var sortedTypes = stackalloc ComponentType[types.Length];
                for (var i = 0; i < types.Length; ++i)
                    SortingUtilities.InsertSorted(sortedTypes, i, types[i]);

                for (int i = 0; i != types.Length; i++)
                {
                    outTypes[i] = sortedTypes[i].TypeIndex;
                    outAccessModes[i] = (byte)sortedTypes[i].AccessModeType;
                }
            }
        }

        void IncludeDependentWriteGroups(ComponentType type, ref UnsafeList<ComponentType> explicitList)
        {
            if (type.AccessModeType != ComponentType.AccessMode.ReadOnly)
                return;

            var typeInfo = TypeManager.GetTypeInfo(type.TypeIndex);
            var writeGroups = TypeManager.GetWriteGroups(typeInfo);
            var writeGroupCount = typeInfo.WriteGroupCount;
            for (int i = 0; i < writeGroupCount; i++)
            {
                var excludedComponentType = GetWriteGroupReadOnlyComponentType(writeGroups, i);
                if (explicitList.Contains(excludedComponentType))
                    continue;

                explicitList.Add(excludedComponentType);
                IncludeDependentWriteGroups(excludedComponentType, ref explicitList);
            }
        }

        private static ComponentType GetWriteGroupReadOnlyComponentType(int* writeGroupTypes, int i)
        {
            // Need to get "Clean" TypeIndex from Type. Since TypeInfo.TypeIndex is not actually the index of the
            // type. (It includes other flags.) What is stored in WriteGroups is the actual index of the type.
            ref readonly var excludedType = ref TypeManager.GetTypeInfo(writeGroupTypes[i]);
            var excludedComponentType = ComponentType.ReadOnly(excludedType.TypeIndex);
            return excludedComponentType;
        }

        void ExcludeWriteGroups(ComponentType type, ref UnsafeList<ComponentType> noneList, UnsafeList<ComponentType> explicitList)
        {
            if (type.AccessModeType == ComponentType.AccessMode.ReadOnly)
                return;

            var typeInfo = TypeManager.GetTypeInfo(type.TypeIndex);
            var writeGroups = TypeManager.GetWriteGroups(typeInfo);
            var writeGroupCount = typeInfo.WriteGroupCount;
            for (int i = 0; i < writeGroupCount; i++)
            {
                var excludedComponentType = GetWriteGroupReadOnlyComponentType(writeGroups, i);
                if (noneList.Contains(excludedComponentType))
                    continue;
                if (explicitList.Contains(excludedComponentType))
                    continue;

                noneList.Add(excludedComponentType);
            }
        }

        // Plan to unmanaged EntityQueryManager
        // [X] Introduce EntityQueryDescBuilder and test it
        // [X] Change internals to take an EntityQueryDescBuilder as an in parameter
        // [X] Validate queryData in CreateQuery
        // [x] Everyone calling this needs to convert managed stuff into unmanaged EQDBuilder
        // [x] Public overloads of APIs to offer an EQDBuilder option
        // [ ] Deprecate EntityQueryDesc
        ArchetypeQuery* CreateQuery(ref UnsafeScratchAllocator unsafeScratchAllocator, in EntityQueryDescBuilder queryBuilder)
        {
            var types = queryBuilder.m_TypeData;
            var queryData = queryBuilder.m_IndexData;
            var outQuery = (ArchetypeQuery*)unsafeScratchAllocator.Allocate(sizeof(ArchetypeQuery) * queryData.Length, UnsafeUtility.AlignOf<ArchetypeQuery>());

            // we need to build out new lists of component types to mutate them for WriteGroups
            var allTypes = new UnsafeList<ComponentType>(32, Allocator.Temp);
            var anyTypes = new UnsafeList<ComponentType>(32, Allocator.Temp);
            var noneTypes = new UnsafeList<ComponentType>(32, Allocator.Temp);

            for (int q = 0; q != queryData.Length; q++)
            {
                {
                    var typesAll = queryData[q].All;
                    for (int i = typesAll.Index; i < typesAll.Index + typesAll.Count; i++)
                    {
                        allTypes.Add(types[i]);
                    }
                }

                {
                    var typesAny = queryData[q].Any;
                    for (int i = typesAny.Index; i < typesAny.Index + typesAny.Count; i++)
                    {
                        anyTypes.Add(types[i]);
                    }
                }

                {
                    var typesNone = queryData[q].None;
                    for (int i = typesNone.Index; i < typesNone.Index + typesNone.Count; i++)
                    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                        var type = types[i];
                        // Can not use Assert.AreEqual here because it uses the (object, object) overload which
                        // boxes the enums being compared, and that can not be burst compiled.
                        Assert.IsTrue(ComponentType.AccessMode.ReadOnly == type.AccessModeType, "EntityQueryDescBuilder.None must convert ComponentType.AccessMode to ReadOnly");
#endif
                        noneTypes.Add(types[i]);
                    }
                }

                // Validate the queryDesc has components declared in a consistent way
                EntityQueryDescBuilder.Validate(allTypes, anyTypes, noneTypes);

                var isFilterWriteGroup = (queryData[q].Options & EntityQueryOptions.FilterWriteGroup) != 0;
                if (isFilterWriteGroup)
                {
                    // Each ReadOnly<type> in any or all
                    //   if has WriteGroup types,
                    //   - Recursively add to any (if not explictly mentioned)

                    var explicitList = new UnsafeList<ComponentType>(allTypes.Length + anyTypes.Length + noneTypes.Length + 16, Allocator.Temp);
                    explicitList.AddRange(allTypes);
                    explicitList.AddRange(anyTypes);
                    explicitList.AddRange(noneTypes);

                    for (int i = 0; i < anyTypes.Length; i++)
                        IncludeDependentWriteGroups(anyTypes[i], ref explicitList);
                    for (int i = 0; i < allTypes.Length; i++)
                        IncludeDependentWriteGroups(allTypes[i], ref explicitList);

                    // Each ReadWrite<type> in any or all
                    //   if has WriteGroup types,
                    //     Add to none (if not exist in any or all or none)

                    for (int i = 0; i < anyTypes.Length; i++)
                        ExcludeWriteGroups(anyTypes[i], ref noneTypes, explicitList);
                    for (int i = 0; i < allTypes.Length; i++)
                        ExcludeWriteGroups(allTypes[i], ref noneTypes, explicitList);
                    explicitList.Dispose();
                }

                ConstructTypeArray(ref unsafeScratchAllocator, noneTypes, out outQuery[q].None,
                    out outQuery[q].NoneAccessMode, out outQuery[q].NoneCount);

                ConstructTypeArray(ref unsafeScratchAllocator, allTypes, out outQuery[q].All,
                    out outQuery[q].AllAccessMode, out outQuery[q].AllCount);

                ConstructTypeArray(ref unsafeScratchAllocator, anyTypes, out outQuery[q].Any,
                    out outQuery[q].AnyAccessMode, out outQuery[q].AnyCount);

                allTypes.Clear();
                anyTypes.Clear();
                noneTypes.Clear();
                outQuery[q].Options = queryData[q].Options;
            }

            allTypes.Dispose();
            anyTypes.Dispose();
            noneTypes.Dispose();
            return outQuery;
        }

        internal static bool CompareQueryArray(in EntityQueryDescBuilder builder, EntityQueryDescBuilder.ComponentIndexArray arr, int* typeArray, byte* accessModeArray, int typeArrayCount)
        {
            int arrCount = arr.Count;
            if (typeArrayCount != arrCount)
                return false;

            var sortedTypes = stackalloc ComponentType[arrCount];
            for (var i = 0; i < arrCount; ++i)
            {
                SortingUtilities.InsertSorted(sortedTypes, i, builder.m_TypeData[arr.Index + i]);
            }

            for (var i = 0; i < arrCount; ++i)
            {
                if (typeArray[i] != sortedTypes[i].TypeIndex || accessModeArray[i] != (byte)sortedTypes[i].AccessModeType)
                    return false;
            }

            return true;
        }


        public static bool CompareQuery(in EntityQueryDescBuilder queryBuilder, EntityQueryData* queryData)
        {
            int count = queryBuilder.m_IndexData.Length;
            if (queryData->ArchetypeQueryCount != count)
                return false;

            for (int i = 0; i != count; i++)
            {
                ref var archetypeQuery = ref queryData->ArchetypeQuery[i];
                var q = queryBuilder.m_IndexData[i];

                if (!CompareQueryArray(queryBuilder, q.All, archetypeQuery.All, archetypeQuery.AllAccessMode, archetypeQuery.AllCount))
                    return false;
                if (!CompareQueryArray(queryBuilder, q.None, archetypeQuery.None, archetypeQuery.NoneAccessMode, archetypeQuery.NoneCount))
                    return false;
                if (!CompareQueryArray(queryBuilder, q.Any, archetypeQuery.Any, archetypeQuery.AnyAccessMode, archetypeQuery.AnyCount))
                    return false;
                if (q.Options != archetypeQuery.Options)
                    return false;
            }

            return true;
        }

        internal struct CompareComponentsQuery
        {
            public FixedList128Bytes<int> includeTypeIndices;
            public FixedList32Bytes<byte> includeAccessModes;
            public FixedList128Bytes<int> excludeTypeIndices;
            public FixedList32Bytes<byte> excludeAccessModes;
        }

        public static void ConvertComponentListToSortedIntListsNoAlloc(ComponentType* sortedTypes, int componentTypesCount, out CompareComponentsQuery query)
        {
            query = new CompareComponentsQuery();

            for (int i = 0; i < componentTypesCount; ++i)
            {
                var type = sortedTypes[i];
                if (type.AccessModeType == ComponentType.AccessMode.Exclude)
                {
                    query.excludeTypeIndices.Add(type.TypeIndex);

                    // None forced to read only
                    query.excludeAccessModes.Add((byte)ComponentType.AccessMode.ReadOnly);
                }
                else
                {
                    query.includeTypeIndices.Add(type.TypeIndex);
                    query.includeAccessModes.Add((byte)type.AccessModeType);
                }
            }
        }

        public static bool CompareComponents(ComponentType* componentTypes, int componentTypesCount, EntityQueryData* queryData)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            for (var k = 0; k < componentTypesCount; ++k)
                if (componentTypes[k].TypeIndex == TypeManager.GetTypeIndex<Entity>())
                    throw new ArgumentException(
                        "EntityQuery.CompareComponents may not include typeof(Entity), it is implicit");
#endif
            if ((queryData->ArchetypeQueryCount != 1) || // This code path only matches one archetypequery
                (queryData->ArchetypeQuery[0].Options != EntityQueryOptions.Default)) // This code path does not allow query options
                return false;

            var sortedTypes = stackalloc ComponentType[componentTypesCount];
            for (var i = 0; i < componentTypesCount; ++i)
            {
                SortingUtilities.InsertSorted(sortedTypes, i, componentTypes[i]);
            }

            ConvertComponentListToSortedIntListsNoAlloc(sortedTypes, componentTypesCount, out var componentCompareQuery);

            var includeCount = componentCompareQuery.includeTypeIndices.Length;
            var excludeCount = componentCompareQuery.excludeTypeIndices.Length;
            var archetypeQuery = queryData->ArchetypeQuery[0];

            if ((includeCount != archetypeQuery.AllCount) ||
                (excludeCount != archetypeQuery.NoneCount))
                return false;

            for (int i = 0; i < includeCount; ++i)
                if (componentCompareQuery.includeTypeIndices[i] != archetypeQuery.All[i] ||
                    componentCompareQuery.includeAccessModes[i] != archetypeQuery.AllAccessMode[i])
                    return false;

            for (int i = 0; i < excludeCount; ++i)
                if (componentCompareQuery.excludeTypeIndices[i] != archetypeQuery.None[i] ||
                    componentCompareQuery.excludeAccessModes[i] != archetypeQuery.NoneAccessMode[i])
                    return false;

            return true;
        }

        private int IntersectSortedComponentIndexArrays(int* arrayA, byte* accessArrayA, int arrayACount, int* arrayB, byte* accessArrayB, int arrayBCount, int* outArray, byte* outAccessArray)
        {
            var intersectionCount = 0;

            var i = 0;
            var j = 0;
            while (i < arrayACount && j < arrayBCount)
            {
                if (arrayA[i] < arrayB[j])
                    i++;
                else if (arrayB[j] < arrayA[i])
                    j++;
                else
                {
                    outArray[intersectionCount] = arrayB[j];
                    outAccessArray[intersectionCount] = accessArrayB[j];
                    intersectionCount++;
                    i++;
                    j++;
                }
            }

            return intersectionCount;
        }

        // Calculates the intersection of "All" queriesDesc
        private ComponentType* CalculateRequiredComponentsFromQuery(ref UnsafeScratchAllocator allocator, ArchetypeQuery* queries, int queryCount, out int outRequiredComponentsCount)
        {
            var maxIntersectionCount = 0;
            for (int queryIndex = 0; queryIndex < queryCount; ++queryIndex)
                maxIntersectionCount = math.max(maxIntersectionCount, queries[queryIndex].AllCount);

            // allocate index array and r/w permissions array
            var intersection = (int*)allocator.Allocate<int>(maxIntersectionCount);
            UnsafeUtility.MemCpy(intersection, queries[0].All, sizeof(int) * queries[0].AllCount);

            var access = (byte*)allocator.Allocate<byte>(maxIntersectionCount);
            UnsafeUtility.MemCpy(access, queries[0].AllAccessMode, sizeof(byte) * queries[0].AllCount);

            var intersectionCount = maxIntersectionCount;
            for (int i = 1; i < queryCount; ++i)
            {
                intersectionCount = IntersectSortedComponentIndexArrays(intersection, access, intersectionCount,
                    queries[i].All, queries[i].AllAccessMode, queries[i].AllCount, intersection, access);
            }

            var outRequiredComponents = (ComponentType*)allocator.Allocate<ComponentType>(intersectionCount + 1);
            outRequiredComponents[0] = ComponentType.ReadWrite<Entity>();
            for (int i = 0; i < intersectionCount; ++i)
            {
                outRequiredComponents[i + 1] = ComponentType.FromTypeIndex(intersection[i]);
                outRequiredComponents[i + 1].AccessModeType = (ComponentType.AccessMode)access[i];
            }

            outRequiredComponentsCount = intersectionCount + 1;
            return outRequiredComponents;
        }

        void CalculateEnableableComponentCounts(ArchetypeQuery* query, out int enableableAllCount, out int enableableNoneCount)
        {
            enableableAllCount = 0;
            for (int i = 0; i < query->AllCount; ++i)
            {
                if (TypeManager.IsEnableable(query->All[i]))
                    enableableAllCount++;
            }

            enableableNoneCount = 0;
            for (int i = 0; i < query->NoneCount; ++i)
            {
                if (TypeManager.IsEnableable(query->None[i]))
                    enableableNoneCount++;
            }

            // todo: Enableable types in the Any clause of an EntityQueryDesc is currently unsupported
            for (int i = 0; i < query->AnyCount; ++i)
            {
                if (TypeManager.IsEnableable(query->Any[i]))
                    throw new ArgumentException(
                        "Using IEnableableComponent in the Any clause of an EntityQueryDesc is currently unsupported.");
            }
        }

        [NotBurstCompatible]
        internal static void ConvertToEntityQueryDescBuilder(ref EntityQueryDescBuilder builder, EntityQueryDesc[] queryDesc)
        {
            for (int q = 0; q != queryDesc.Length; q++)
            {
                for (int i = 0; i < queryDesc[q].All.Length; i++)
                {
                    builder.AddAll(queryDesc[q].All[i]);
                }

                for (int i = 0; i < queryDesc[q].Any.Length; i++)
                {
                    builder.AddAny(queryDesc[q].Any[i]);
                }

                for (int i = 0; i < queryDesc[q].None.Length; i++)
                {
                    builder.AddNone(queryDesc[q].None[i]);
                }

                builder.Options(queryDesc[q].Options);
                builder.FinalizeQuery();
            }
        }

        public EntityQuery CreateEntityQuery(EntityDataAccess* access, in EntityQueryDescBuilder query)
        {
            CheckEmptyBuilder(query);

            var buffer = stackalloc byte[1024];
            var scratchAllocator = new UnsafeScratchAllocator(buffer, 1024);
            var archetypeQuery = CreateQuery(ref scratchAllocator, query);

            var outRequiredComponents = CalculateRequiredComponentsFromQuery(ref scratchAllocator, archetypeQuery, query.m_IndexData.Length, out var outRequiredComponentsCount);
            CalculateEnableableComponentCounts(archetypeQuery, out var outEnableableAllCount, out var outEnableableNoneCount);

            return CreateEntityQuery(access, archetypeQuery, query.m_IndexData.Length, outRequiredComponents, outRequiredComponentsCount, outEnableableAllCount, outEnableableNoneCount);

        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckEmptyBuilder(EntityQueryDescBuilder query)
        {
            if (query.m_IndexData.Length == 0)
            {
                throw new ArgumentException("EntityQueryDescBuilder is empty. Did you forget to call FinalizeQuery()?");
            }
        }

        public EntityQuery CreateEntityQuery(EntityDataAccess* access, ComponentType* inRequiredComponents, int inRequiredComponentsCount)
        {
            var buffer = stackalloc byte[1024];
            var scratchAllocator = new UnsafeScratchAllocator(buffer, 1024);
            var archetypeQuery = CreateQuery(ref scratchAllocator, inRequiredComponents, inRequiredComponentsCount);
            var outRequiredComponents = (ComponentType*)scratchAllocator.Allocate<ComponentType>(inRequiredComponentsCount + 1);
            outRequiredComponents[0] = ComponentType.ReadWrite<Entity>();
            for (int i = 0; i != inRequiredComponentsCount; i++)
                SortingUtilities.InsertSorted(outRequiredComponents + 1, i, inRequiredComponents[i]);
            var outRequiredComponentsCount = inRequiredComponentsCount + 1;
            CalculateEnableableComponentCounts(archetypeQuery, out var outEnableableAllCount, out var outEnableableNoneCount);
            return CreateEntityQuery(access, archetypeQuery, 1, outRequiredComponents, outRequiredComponentsCount, outEnableableAllCount, outEnableableNoneCount);
        }

        bool Matches(EntityQueryData* grp, ArchetypeQuery* archetypeQueries, int archetypeFiltersCount,
            ComponentType* requiredComponents, int requiredComponentsCount)
        {
            if (requiredComponentsCount != grp->RequiredComponentsCount)
                return false;
            if (archetypeFiltersCount != grp->ArchetypeQueryCount)
                return false;
            if (requiredComponentsCount > 0 && UnsafeUtility.MemCmp(requiredComponents, grp->RequiredComponents, sizeof(ComponentType) * requiredComponentsCount) != 0)
                return false;
            for (var i = 0; i < archetypeFiltersCount; ++i)
                if (!archetypeQueries[i].Equals(grp->ArchetypeQuery[i]))
                    return false;
            return true;
        }

        void* ChunkAllocate<T>(int count = 1, void *source = null) where T : struct
        {
            var bytes = count * UnsafeUtility.SizeOf<T>();
            if (bytes == 0)
                return null;
            var pointer = m_GroupDataChunkAllocator.Allocate(bytes, UnsafeUtility.AlignOf<T>());
            if (source != null)
                UnsafeUtility.MemCpy(pointer, source, bytes);
            return pointer;
        }

        public EntityQuery CreateEntityQuery(EntityDataAccess* access,
            ArchetypeQuery* query,
            int queryCount,
            ComponentType* component,
            int componentCount,
            int enableableAllCount,
            int enableableNoneCount)
        {
            //@TODO: Validate that required types is subset of archetype filters all...

            int hash = (int)math.hash(component, componentCount * sizeof(ComponentType));
            for (var i = 0; i < queryCount; ++i)
                hash = hash * 397 ^ query[i].GetHashCode();
            EntityQueryData* cachedQuery = null;
            ref var groupCache = ref UnsafeUtility.As<UntypedUnsafeParallelHashMap, UnsafeParallelMultiHashMap<int, int>>(ref m_EntityGroupDataCacheUntyped);

            if (groupCache.TryGetFirstValue(hash, out var entityGroupDataIndex, out var iterator))
            {
                do
                {
                    var possibleMatch = m_EntityGroupDatas.Ptr[entityGroupDataIndex];
                    if (Matches(possibleMatch, query, queryCount, component, componentCount))
                    {
                        cachedQuery = possibleMatch;
                        break;
                    }
                }
                while (groupCache.TryGetNextValue(out entityGroupDataIndex, ref iterator));
            }

            if (cachedQuery == null)
            {
                cachedQuery = (EntityQueryData*)ChunkAllocate<EntityQueryData>();
                cachedQuery->RequiredComponentsCount = componentCount;
                cachedQuery->RequiredComponents = (ComponentType*)ChunkAllocate<ComponentType>(componentCount, component);
                cachedQuery->EnableableComponentsCountAll = enableableAllCount;
                cachedQuery->EnableableComponentsCountNone = enableableNoneCount;
                InitializeReaderWriter(cachedQuery, component, componentCount);
                cachedQuery->ArchetypeQueryCount = queryCount;
                cachedQuery->ArchetypeQuery = (ArchetypeQuery*)ChunkAllocate<ArchetypeQuery>(queryCount, query);
                for (var i = 0; i < queryCount; ++i)
                {
                    cachedQuery->ArchetypeQuery[i].All = (int*)ChunkAllocate<int>(cachedQuery->ArchetypeQuery[i].AllCount, query[i].All);
                    cachedQuery->ArchetypeQuery[i].Any = (int*)ChunkAllocate<int>(cachedQuery->ArchetypeQuery[i].AnyCount, query[i].Any);
                    cachedQuery->ArchetypeQuery[i].None = (int*)ChunkAllocate<int>(cachedQuery->ArchetypeQuery[i].NoneCount, query[i].None);
                    cachedQuery->ArchetypeQuery[i].AllAccessMode = (byte*)ChunkAllocate<byte>(cachedQuery->ArchetypeQuery[i].AllCount, query[i].AllAccessMode);
                    cachedQuery->ArchetypeQuery[i].AnyAccessMode = (byte*)ChunkAllocate<byte>(cachedQuery->ArchetypeQuery[i].AnyCount, query[i].AnyAccessMode);
                    cachedQuery->ArchetypeQuery[i].NoneAccessMode = (byte*)ChunkAllocate<byte>(cachedQuery->ArchetypeQuery[i].NoneCount, query[i].NoneAccessMode);
                }

                var ecs = access->EntityComponentStore;

                cachedQuery->MatchingArchetypes = new UnsafeMatchingArchetypePtrList(access->EntityComponentStore);
                cachedQuery->MatchingChunkCache = new UnsafeCachedChunkList(access->EntityComponentStore);

                cachedQuery->EntityQueryMask = new EntityQueryMask();

                for (var i = 0; i < ecs->m_Archetypes.Length; ++i)
                {
                    var archetype = ecs->m_Archetypes.Ptr[i];
                    AddArchetypeIfMatching(archetype, cachedQuery);
                }

                groupCache.Add(hash, m_EntityGroupDatas.Length);
                m_EntityGroupDatas.Add(cachedQuery);
                cachedQuery->MatchingChunkCache.InvalidateCache();
            }

            return EntityQuery.Construct(cachedQuery, access);
        }

        void InitializeReaderWriter(EntityQueryData* grp, ComponentType* requiredTypes, int requiredCount)
        {
            Assert.IsTrue(requiredCount > 0);
            Assert.IsTrue(requiredTypes[0] == ComponentType.ReadWrite<Entity>());

            grp->ReaderTypesCount = 0;
            grp->WriterTypesCount = 0;

            for (var i = 1; i != requiredCount; i++)
            {
                // After the first zero sized component the rest are zero sized
                if (requiredTypes[i].IsZeroSized)
                    break;

                switch (requiredTypes[i].AccessModeType)
                {
                    case ComponentType.AccessMode.ReadOnly:
                        grp->ReaderTypesCount++;
                        break;
                    default:
                        grp->WriterTypesCount++;
                        break;
                }
            }

            grp->ReaderTypes = (int*)m_GroupDataChunkAllocator.Allocate(sizeof(int) * grp->ReaderTypesCount, 4);
            grp->WriterTypes = (int*)m_GroupDataChunkAllocator.Allocate(sizeof(int) * grp->WriterTypesCount, 4);

            var curReader = 0;
            var curWriter = 0;
            for (var i = 1; i != requiredCount; i++)
            {
                if (requiredTypes[i].IsZeroSized)
                    break;

                switch (requiredTypes[i].AccessModeType)
                {
                    case ComponentType.AccessMode.ReadOnly:
                        grp->ReaderTypes[curReader++] = requiredTypes[i].TypeIndex;
                        break;
                    default:
                        grp->WriterTypes[curWriter++] = requiredTypes[i].TypeIndex;
                        break;
                }
            }
        }

        public void AddAdditionalArchetypes(UnsafePtrList<Archetype> archetypeList)
        {
            for (int i = 0; i < archetypeList.Length; i++)
            {
                for (var g = 0; g < m_EntityGroupDatas.Length; ++g)
                {
                    var grp = m_EntityGroupDatas.Ptr[g];
                    AddArchetypeIfMatching(archetypeList.Ptr[i], grp);
                }
            }
        }

        void AddArchetypeIfMatching(Archetype* archetype, EntityQueryData* query)
        {
            if (!IsMatchingArchetype(archetype, query))
                return;

            var match = MatchingArchetype.Create(ref m_GroupDataChunkAllocator, archetype, query);
            match->Archetype = archetype;
            var typeIndexInArchetypeArray = match->IndexInArchetype;

            match->Archetype->SetMask(query->EntityQueryMask);

            query->MatchingArchetypes.Add(match);

            // Add back pointer from archetype to query data
            archetype->MatchingQueryData.Add((IntPtr)query);

            var typeComponentIndex = 0;
            for (var component = 0; component < query->RequiredComponentsCount; ++component)
            {
                if (query->RequiredComponents[component].AccessModeType != ComponentType.AccessMode.Exclude)
                {
                    typeComponentIndex = ChunkDataUtility.GetNextIndexInTypeArray(archetype, query->RequiredComponents[component].TypeIndex, typeComponentIndex);
                    Assert.AreNotEqual(-1, typeComponentIndex);
                    typeIndexInArchetypeArray[component] = typeComponentIndex;
                }
                else
                {
                    typeIndexInArchetypeArray[component] = -1;
                }
            }

            var enableableAllCount = 0;
            typeComponentIndex = 0;
            for (var i = 0; i < query->ArchetypeQuery->AllCount; ++i)
            {
                var typeIndex = query->ArchetypeQuery->All[i];
                if (TypeManager.IsEnableable(typeIndex))
                {
                    typeComponentIndex = ChunkDataUtility.GetNextIndexInTypeArray(archetype, typeIndex, typeComponentIndex);
                    Assert.AreNotEqual(-1, typeComponentIndex);
                    match->EnableableIndexInArchetype_All[enableableAllCount++] = typeComponentIndex;
                }
            }

            var enableableNoneCount = 0;
            typeComponentIndex = 0;
            for (var i = 0; i < query->ArchetypeQuery->NoneCount; ++i)
            {
                var typeIndex = query->ArchetypeQuery->None[i];
                if (TypeManager.IsEnableable(typeIndex))
                {
                    var currentTypeComponentIndex = ChunkDataUtility.GetNextIndexInTypeArray(archetype, typeIndex, typeComponentIndex);
                    if (currentTypeComponentIndex != -1) // we skip storing "None" component types for matching archetypes that do not contain the "None" type (there are no bits to check)
                    {
                        match->EnableableIndexInArchetype_None[enableableNoneCount++] = currentTypeComponentIndex;
                        typeComponentIndex = currentTypeComponentIndex;
                    }
                }
            }
        }

        //@TODO: All this could be much faster by having all ComponentType pre-sorted to perform a single search loop instead two nested for loops...
        static bool IsMatchingArchetype(Archetype* archetype, EntityQueryData* query)
        {
            for (int i = 0; i != query->ArchetypeQueryCount; i++)
            {
                if (IsMatchingArchetype(archetype, query->ArchetypeQuery + i))
                    return true;
            }

            return false;
        }

        static bool IsMatchingArchetype(Archetype* archetype, ArchetypeQuery* query)
        {
            if (!TestMatchingArchetypeAll(archetype, query->All, query->AllCount, query->Options))
                return false;
            if (!TestMatchingArchetypeNone(archetype, query->None, query->NoneCount))
                return false;
            if (!TestMatchingArchetypeAny(archetype, query->Any, query->AnyCount))
                return false;

            return true;
        }

        static bool TestMatchingArchetypeAny(Archetype* archetype, int* anyTypes, int anyCount)
        {
            if (anyCount == 0) return true;

            var componentTypes = archetype->Types;
            var componentTypesCount = archetype->TypesCount;
            for (var i = 0; i < componentTypesCount; i++)
            {
                var componentTypeIndex = componentTypes[i].TypeIndex;
                for (var j = 0; j < anyCount; j++)
                {
                    var anyTypeIndex = anyTypes[j];
                    if (componentTypeIndex == anyTypeIndex)
                        return true;
                }
            }

            return false;
        }

        static bool TestMatchingArchetypeNone(Archetype* archetype, int* noneTypes, int noneCount)
        {
            var componentTypes = archetype->Types;
            var componentTypesCount = archetype->TypesCount;
            for (var i = 0; i < componentTypesCount; i++)
            {
                var componentTypeIndex = componentTypes[i].TypeIndex;
                for (var j = 0; j < noneCount; j++)
                {
                    var noneTypeIndex = noneTypes[j];
                    if (componentTypeIndex == noneTypeIndex && !TypeManager.IsEnableable(componentTypeIndex)) return false;
                }
            }

            return true;
        }

        static bool TestMatchingArchetypeAll(Archetype* archetype, int* allTypes, int allCount, EntityQueryOptions options)
        {
            var componentTypes = archetype->Types;
            var componentTypesCount = archetype->TypesCount;
            var foundCount = 0;
            var disabledTypeIndex = TypeManager.GetTypeIndex<Disabled>();
            var prefabTypeIndex = TypeManager.GetTypeIndex<Prefab>();
            var chunkHeaderTypeIndex = TypeManager.GetTypeIndex<ChunkHeader>();
            var includeInactive = (options & EntityQueryOptions.IncludeDisabled) != 0;
            var includePrefab = (options & EntityQueryOptions.IncludePrefab) != 0;
            var includeChunkHeader = false;

            for (var i = 0; i < componentTypesCount; i++)
            {
                var componentTypeIndex = componentTypes[i].TypeIndex;
                for (var j = 0; j < allCount; j++)
                {
                    var allTypeIndex = allTypes[j];
                    if (allTypeIndex == disabledTypeIndex)
                        includeInactive = true;
                    if (allTypeIndex == prefabTypeIndex)
                        includePrefab = true;
                    if (allTypeIndex == chunkHeaderTypeIndex)
                        includeChunkHeader = true;

                    if (componentTypeIndex == allTypeIndex) foundCount++;
                }
            }

            if (archetype->Disabled && (!includeInactive))
                return false;
            if (archetype->Prefab && (!includePrefab))
                return false;
            if (archetype->HasChunkHeader && (!includeChunkHeader))
                return false;

            return foundCount == allCount;
        }

        public static int FindMatchingArchetypeIndexForArchetype(ref UnsafeMatchingArchetypePtrList matchingArchetypes,
            Archetype* archetype)
        {
            int archetypeCount = matchingArchetypes.Length;
            var ptrs = matchingArchetypes.Ptr;
            for (int i = 0; i < archetypeCount; ++i)
            {
                if (archetype == ptrs[i]->Archetype)
                    return i;
            }

            return -1;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        private static void ThrowIfEntityQueryMasksIsGreaterThanLimit(int entityQueryMasksAllocated)
        {
            if (entityQueryMasksAllocated >= 1024)
                throw new Exception("You have reached the limit of 1024 unique EntityQueryMasks, and cannot generate any more.");
        }

        public EntityQueryMask GetEntityQueryMask(EntityQueryData* query, EntityComponentStore* ecStore)
        {
            if (query->EntityQueryMask.IsCreated())
                return query->EntityQueryMask;

            ThrowIfEntityQueryMasksIsGreaterThanLimit(m_EntityQueryMasksAllocated);

            var mask = new EntityQueryMask(
                (byte)(m_EntityQueryMasksAllocated / 8),
                (byte)(1 << (m_EntityQueryMasksAllocated % 8)),
                ecStore);

            m_EntityQueryMasksAllocated++;

            int archetypeCount = query->MatchingArchetypes.Length;
            var ptrs = query->MatchingArchetypes.Ptr;
            for (var i = 0; i < archetypeCount; ++i)
            {
                ptrs[i]->Archetype->QueryMaskArray[mask.Index] |= mask.Mask;
            }

            query->EntityQueryMask = mask;

            return mask;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    unsafe struct MatchingArchetype
    {
        public Archetype* Archetype;
        public int RequiredComponentCount;
        public int EnableableComponentsCount_All;
        public int EnableableComponentsCount_None;

        public fixed int IndexInArchetype[1];

        public int* EnableableIndexInArchetype_All
        {
            get
            {
                fixed (int* basePtr = IndexInArchetype)
                {
                    Assert.IsTrue(EnableableComponentsCount_All > 0);
                    return basePtr + RequiredComponentCount;
                }
            }
        }
        public int* EnableableIndexInArchetype_None
        {
            get
            {
                fixed (int* basePtr = IndexInArchetype)
                {
                    Assert.IsTrue(EnableableComponentsCount_None > 0);
                    return basePtr + RequiredComponentCount + EnableableComponentsCount_All;
                }
            }
        }

        public static int CalculateMatchingArchetypeEnableableTypeIntersectionCount(Archetype* archetype, int* queryComponents, int queryComponentCount)
        {
            var intersectionCount = 0;
            var typeComponentIndex = 0;
            for (int i = 0; i < queryComponentCount; ++i)
            {
                var typeIndex = queryComponents[i];
                if (!TypeManager.IsEnableable(typeIndex))
                    continue;
                var currentTypeComponentIndex = ChunkDataUtility.GetNextIndexInTypeArray(archetype, typeIndex, typeComponentIndex);
                if (currentTypeComponentIndex >= 0)
                {
                    typeComponentIndex = currentTypeComponentIndex;
                    intersectionCount++;
                }
            }

            return intersectionCount;
        }

        public static MatchingArchetype* Create(ref BlockAllocator allocator, Archetype* archetype, EntityQueryData* query)
        {
            var enableableAllCount = CalculateMatchingArchetypeEnableableTypeIntersectionCount(archetype, query->ArchetypeQuery->All, query->ArchetypeQuery->AllCount);
            var enableableNoneCount = CalculateMatchingArchetypeEnableableTypeIntersectionCount(archetype, query->ArchetypeQuery->None, query->ArchetypeQuery->NoneCount);
            var totalEnableableTypeCount = enableableAllCount + enableableNoneCount;
            var match = (MatchingArchetype*)allocator.Allocate(GetAllocationSize(query->RequiredComponentsCount, totalEnableableTypeCount), 8);
            match->Archetype = archetype;

            match->RequiredComponentCount = query->RequiredComponentsCount;
            match->EnableableComponentsCount_All = enableableAllCount;
            match->EnableableComponentsCount_None = enableableNoneCount;

            return match;
        }

        private static int GetAllocationSize(int requiredComponentsCount, int enableableComponentCount)
        {
            return sizeof(MatchingArchetype) +
                   sizeof(int) * (requiredComponentsCount-1) +
                   sizeof(int) * enableableComponentCount;
        }

        public bool ChunkMatchesFilter(int chunkIndex, ref EntityQueryFilter filter)
        {
            var chunks = Archetype->Chunks;

            // Must match ALL shared component data
            for (int i = 0; i < filter.Shared.Count; ++i)
            {
                var indexInEntityQuery = filter.Shared.IndexInEntityQuery[i];
                var sharedComponentIndex = filter.Shared.SharedComponentIndex[i];
                var componentIndexInChunk = IndexInArchetype[indexInEntityQuery] - Archetype->FirstSharedComponent;
                var sharedComponents = chunks.GetSharedComponentValueArrayForType(componentIndexInChunk);

                // if we don't have a match, we can early out
                if (sharedComponents[chunkIndex] != sharedComponentIndex)
                    return false;
            }

            if (filter.Changed.Count == 0 && !filter.UseOrderFiltering)
                return true;

            var orderVersionFilterPassed = filter.UseOrderFiltering && ChangeVersionUtility.DidChange(chunks.GetOrderVersion(chunkIndex), filter.RequiredChangeVersion);

            // Must have AT LEAST ONE type have changed
            var changedVersionFilterPassed = false;
            for (int i = 0; i < filter.Changed.Count; ++i)
            {
                var indexInEntityQuery = filter.Changed.IndexInEntityQuery[i];
                var componentIndexInChunk = IndexInArchetype[indexInEntityQuery];
                var changeVersions = chunks.GetChangeVersionArrayForType(componentIndexInChunk);

                var requiredVersion = filter.RequiredChangeVersion;

                changedVersionFilterPassed |= ChangeVersionUtility.DidChange(changeVersions[chunkIndex], requiredVersion);
            }

            return changedVersionFilterPassed || orderVersionFilterPassed;
        }
    }

    [DebuggerTypeProxy(typeof(UnsafeMatchingArchetypePtrListDebugView))]
    unsafe struct UnsafeMatchingArchetypePtrList
    {
        [NativeDisableUnsafePtrRestriction]
        private UnsafeList<IntPtr>* ListData;

        public MatchingArchetype** Ptr { get => (MatchingArchetype**)ListData->Ptr; }
        public int Length { get => ListData->Length; }

        public void Dispose() { ListData->Dispose(); }
        public void Add(void* t) { ListData->Add((IntPtr)t); }

        [NativeDisableUnsafePtrRestriction]
        public EntityComponentStore* entityComponentStore;

        public UnsafeMatchingArchetypePtrList(EntityComponentStore* entityComponentStore)
        {
            ListData = UnsafeList<IntPtr>.Create(0, Allocator.Persistent);
            this.entityComponentStore = entityComponentStore;
        }
    }

    [BurstCompatible]
    unsafe struct ArchetypeQuery : IEquatable<ArchetypeQuery>
    {
        public int*     Any;
        public byte*    AnyAccessMode;
        public int      AnyCount;

        public int*     All;
        public byte*    AllAccessMode;
        public int      AllCount;

        public int*     None;
        public byte*    NoneAccessMode;
        public int      NoneCount;

        public EntityQueryOptions  Options;

        public bool Equals(ArchetypeQuery other)
        {
            if (AnyCount != other.AnyCount)
                return false;
            if (AllCount != other.AllCount)
                return false;
            if (NoneCount != other.NoneCount)
                return false;
            if (AnyCount > 0 && UnsafeUtility.MemCmp(Any, other.Any, sizeof(int) * AnyCount) != 0 &&
                UnsafeUtility.MemCmp(AnyAccessMode, other.AnyAccessMode, sizeof(byte) * AnyCount) != 0)
                return false;
            if (AllCount > 0 && UnsafeUtility.MemCmp(All, other.All, sizeof(int) * AllCount) != 0 &&
                UnsafeUtility.MemCmp(AllAccessMode, other.AllAccessMode, sizeof(byte) * AllCount) != 0)
                return false;
            if (NoneCount > 0 && UnsafeUtility.MemCmp(None, other.None, sizeof(int) * NoneCount) != 0 &&
                UnsafeUtility.MemCmp(NoneAccessMode, other.NoneAccessMode, sizeof(byte) * NoneCount) != 0)
                return false;
            if (Options != other.Options)
                return false;

            return true;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode =                  (AnyCount + 1);
                hashCode = 397 * hashCode ^ (AllCount + 1);
                hashCode = 397 * hashCode ^ (NoneCount + 1);
                hashCode = (int)math.hash(Any, sizeof(int) * AnyCount, (uint)hashCode);
                hashCode = (int)math.hash(All, sizeof(int) * AllCount, (uint)hashCode);
                hashCode = (int)math.hash(None, sizeof(int) * NoneCount, (uint)hashCode);
                hashCode = (int)math.hash(AnyAccessMode, sizeof(byte) * AnyCount, (uint)hashCode);
                hashCode = (int)math.hash(AllAccessMode, sizeof(byte) * AllCount, (uint)hashCode);
                hashCode = (int)math.hash(NoneAccessMode, sizeof(byte) * NoneCount, (uint)hashCode);
                return hashCode;
            }
        }
    }

    unsafe struct UnsafeCachedChunkList
    {
        [NativeDisableUnsafePtrRestriction]
        internal UnsafePtrList<Chunk>* MatchingChunks;

        [NativeDisableUnsafePtrRestriction]
        internal UnsafeList<int>* PerChunkMatchingArchetypeIndex;

        [NativeDisableUnsafePtrRestriction]
        internal EntityComponentStore* EntityComponentStore;

        internal int CacheValid; // must not be a bool, for Burst compatibility

        public Chunk** Ptr { get => (Chunk**)MatchingChunks->Ptr; }
        public int Length { get => MatchingChunks->Length; }
        public bool IsCacheValid { get => CacheValid != 0; }

        public UnsafeCachedChunkList(EntityComponentStore* entityComponentStore)
        {
            EntityComponentStore = entityComponentStore;
            MatchingChunks = UnsafePtrList<Chunk>.Create(0, Allocator.Persistent);
            PerChunkMatchingArchetypeIndex = UnsafeList<int>.Create(0, Allocator.Persistent);
            CacheValid = 0;
        }

        public void Append(Chunk** t, int addChunkCount, int matchingArchetypeIndex)
        {
            var startIndex = MatchingChunks->Length;
            MatchingChunks->AddRange(new UnsafePtrList<Chunk>(t, addChunkCount));
            for (int i = 0; i < addChunkCount; ++i)
            {
                PerChunkMatchingArchetypeIndex->Add(matchingArchetypeIndex);
            }
        }

        public void Dispose()
        {
            MatchingChunks->Dispose();
            PerChunkMatchingArchetypeIndex->Dispose();
        }

        public void InvalidateCache()
        {
            CacheValid = 0;
        }

        public static bool CheckCacheConsistency(ref UnsafeCachedChunkList cache, EntityQueryData* data)
        {
            var chunkCounter = 0;
            int archetypeCount = data->MatchingArchetypes.Length;
            var ptrs = data->MatchingArchetypes.Ptr;
            for (int archetypeIndex = 0; archetypeIndex < archetypeCount; ++archetypeIndex)
            {
                var archetype = ptrs[archetypeIndex]->Archetype;
                for (int chunkIndex = 0; chunkIndex < archetype->Chunks.Count; ++chunkIndex)
                {
                    if (chunkCounter >= cache.MatchingChunks->Length)
                        return false;
                    if(cache.MatchingChunks->Ptr[chunkCounter++] != archetype->Chunks[chunkIndex])
                        return false;
                }
            }

            // All chunks in cache are accounted for
            if (chunkCounter != cache.Length)
                return false;

            return true;
        }
    }

    /// <summary>
    /// Struct for storing entity query data for multiple queries. Currently used to convert EntityQueryDesc[] into unmanaged
    /// data that EntityQueryManager can use when creating queries.
    /// </summary>
    [BurstCompatible]
    public struct EntityQueryDescBuilder : IDisposable
    {
        internal struct ComponentIndexArray
        {
            public ushort Index;
            public ushort Count;
        }
        internal struct QueryTypes
        {
            public EntityQueryOptions Options;
            public ComponentIndexArray Any;
            public ComponentIndexArray None;
            public ComponentIndexArray All;
        }
        internal UnsafeList<ComponentType> m_TypeData;
        internal UnsafeList<QueryTypes> m_IndexData;
        UnsafeList<ComponentType> m_Any;
        UnsafeList<ComponentType> m_None;
        UnsafeList<ComponentType> m_All;
        EntityQueryOptions m_PendingOptions;

        /// <summary>
        /// Create an entity query description builder
        /// </summary>
        /// <param name="a">The allocator where the builder allocates its arrays. Typically Allocator.Temp.</param>
        public EntityQueryDescBuilder(Allocator a)
        {
            m_Any = new UnsafeList<ComponentType>(6, a);
            m_None = new UnsafeList<ComponentType>(6, a);
            m_All = new UnsafeList<ComponentType>(6, a);
            m_TypeData = new UnsafeList<ComponentType>(2, a);
            m_IndexData = new UnsafeList<QueryTypes>(2, a);
            m_PendingOptions = default;
        }

        /// <summary>
        /// Set options for the current query.
        /// </summary>
        /// <param name="options"></param>
        public void Options(EntityQueryOptions options)
        {
            m_PendingOptions = options;
        }

        /// <summary>
        /// Add an "any" matching type to the current query.
        /// </summary>
        /// <param name="t">The component type</param>
        public void AddAny(ComponentType t)
        {
            m_Any.Add(t);
        }

        /// <summary>
        /// Add a "none" matching type to the current query.
        /// </summary>
        /// <param name="t">The component type</param>
        /// <remarks>Types in the None list are never written to. If the <see cref="AccessModeType"/> field of the
        /// provided component type is <see cref="AccessMode.ReadWrite"/>, will be forced to
        /// <see cref="AccessMode.ReadOnly"/> in the query.</remarks>
        public void AddNone(ComponentType t)
        {
            // The access mode of types in the None list is forced to ReadOnly; the query will not be accessing these
            // types at all, and should not be requesting read/write access to them.
            t.AccessModeType = ComponentType.AccessMode.ReadOnly;
            m_None.Add(t);
        }

        /// <summary>
        /// Add an "all" matching type to the current query.
        /// </summary>
        /// <param name="t">The component type</param>
        public void AddAll(ComponentType t)
        {
            m_All.Add(t);
        }
        /// <summary>
        /// Store the current query into the builder.
        /// </summary>
        /// <remarks>
        /// Components added to any, all, and none will be stored in the builder once this is called.
        /// If this is not called, nothing will be recorded and the query will be empty.
        /// </remarks>
        public void FinalizeQuery()
        {
            QueryTypes qd = default;
            qd.Options = m_PendingOptions;
            TransferArray(ref m_Any, ref qd.Any);
            TransferArray(ref m_None, ref qd.None);
            TransferArray(ref m_All, ref qd.All);
            m_IndexData.Add(qd);
            m_PendingOptions = default;
        }

        private void TransferArray(ref UnsafeList<ComponentType> source, ref ComponentIndexArray result)
        {
            result.Index = (ushort)m_TypeData.Length;
            result.Count = (ushort)source.Length;
            m_TypeData.AddRange(source);
            source.Clear();
        }

        /// <summary>
        /// Dispose the builder and release the memory.
        /// </summary>
        public void Dispose()
        {
            m_IndexData.Dispose();
            m_TypeData.Dispose();
            m_All.Dispose();
            m_None.Dispose();
            m_Any.Dispose();
        }

        /// <summary>
        /// Reset the builder for reuse.
        /// </summary>
        public void Reset()
        {
            m_IndexData.Clear();
            m_TypeData.Clear();
            m_PendingOptions = default;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void ValidateComponentTypes(in UnsafeList<ComponentType> componentTypes, ref UnsafeList<int> allTypeIds)
        {
            // Needs to make sure that AccessModeType is not Exclude
            for (int i = 0; i < componentTypes.Length; i++)
            {
                allTypeIds.Add(componentTypes[i].TypeIndex);
                if (componentTypes[i].AccessModeType == ComponentType.AccessMode.Exclude)
                    throw new ArgumentException("EntityQueryDesc cannot contain Exclude Component types");
            }
        }

#if !NET_DOTS
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        [BurstDiscard]
        internal static void ThrowDuplicateComponentTypeError(int curId)
        {
            var typeName = TypeManager.GetType(curId).Name;
            throw new EntityQueryDescValidationException(
                $"EntityQuery contains a filter with duplicate component type name {typeName}.  Queries can only contain a single component of a given type in a filter.");
        }
#endif

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void Validate(in UnsafeList<ComponentType> allTypes, in UnsafeList<ComponentType> anyTypes, in UnsafeList<ComponentType> noneTypes)
        {
            // Determine the number of ComponentTypes contained in the filters
            var itemCount = allTypes.Length + anyTypes.Length + noneTypes.Length;

            // Project all the ComponentType Ids of None, All, Any queryDesc filters into the same array to identify duplicated later on
            // Also, check that queryDesc doesn't contain any ExcludeComponent...

            var allComponentTypeIds = new UnsafeList<int>(itemCount, Allocator.Temp);
            ValidateComponentTypes(allTypes, ref allComponentTypeIds);
            ValidateComponentTypes(anyTypes, ref allComponentTypeIds);
            ValidateComponentTypes(noneTypes, ref allComponentTypeIds);

            // Check for duplicate, only if necessary
            if (itemCount > 1)
            {
                // Build a new unsafeList for None, All, Any


                // Sort the Ids to have identical value adjacent
                allComponentTypeIds.Sort();

                // Check for identical values
                var refId = allComponentTypeIds[0];
                for (int i = 1; i < allComponentTypeIds.Length; i++)
                {
                    var curId = allComponentTypeIds[i];
                    if (curId == refId)
                    {
#if !NET_DOTS
                        ThrowDuplicateComponentTypeError(curId);
#endif
                        throw new EntityQueryDescValidationException(
                            $"EntityQuery contains a filter with duplicate component type index {curId}.  Queries can only contain a single component of a given type in a filter.");
                    }

                    refId = curId;
                }
            }

            allComponentTypeIds.Dispose();
        }
    }

    unsafe struct EntityQueryData : IDisposable
    {
        //@TODO: better name or remove entirely...
        public ComponentType*       RequiredComponents;
        public int                  RequiredComponentsCount;

        public int*                 ReaderTypes;
        public int                  ReaderTypesCount;

        public int*                 WriterTypes;
        public int                  WriterTypesCount;

        public int                  EnableableComponentsCountAll;
        public int                  EnableableComponentsCountNone;

        public ArchetypeQuery*      ArchetypeQuery;
        public int                  ArchetypeQueryCount;

        public EntityQueryMask      EntityQueryMask;

        public UnsafeMatchingArchetypePtrList MatchingArchetypes;
        internal UnsafeCachedChunkList MatchingChunkCache;

        public bool DoesQueryRequireBatching
        {
            get {return EnableableComponentsCountAll != 0 || EnableableComponentsCountNone != 0;}
        }

        public unsafe UnsafeCachedChunkList GetMatchingChunkCache()
        {
            if(!MatchingChunkCache.IsCacheValid)
                ChunkIterationUtility.RebuildChunkListCache((EntityQueryData*)UnsafeUtility.AddressOf(ref this));

            return MatchingChunkCache;
        }

        public void Dispose()
        {
            MatchingArchetypes.Dispose();
            MatchingChunkCache.Dispose();
        }
    }
}
