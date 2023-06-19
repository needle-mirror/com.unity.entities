using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;

namespace Unity.Entities
{
#if ENABLE_UNITY_COLLECTIONS_CHECKS
    // A bundle of component safety handles implicitly referenced by an EntityQuery. This allows a query to
    // register its component dependencies when the query is used by a job.
    [NativeContainer]
    internal struct EntityQuerySafetyHandles
    {
        // All IJobChunk jobs have a EntityManager safety handle to ensure that BeforeStructuralChange throws an error if
        // jobs without any other safety handles are still running (haven't been synced).
        internal AtomicSafetyHandle m_Safety0;
        // TODO(DOTS-6573): Enable this path in DOTSRT once it supports AtomicSafetyHandle.SetExclusiveWeak()
#if !UNITY_DOTSRUNTIME
        // Enableable components from query used to schedule job. To add more handles here, you must also increase
        // EntityQueryManager.MAX_ENABLEABLE_COMPONENTS_PER_QUERY.
        internal AtomicSafetyHandle m_SafetyEnableable1;
        internal AtomicSafetyHandle m_SafetyEnableable2;
        internal AtomicSafetyHandle m_SafetyEnableable3;
        internal AtomicSafetyHandle m_SafetyEnableable4;
        internal AtomicSafetyHandle m_SafetyEnableable5;
        internal AtomicSafetyHandle m_SafetyEnableable6;
        internal AtomicSafetyHandle m_SafetyEnableable7;
        internal AtomicSafetyHandle m_SafetyEnableable8;
#endif
        internal int m_SafetyReadOnlyCount;
        internal int m_SafetyReadWriteCount;

        internal unsafe EntityQuerySafetyHandles(EntityQueryImpl* queryImpl)
        {
            this = default; // workaround for CS0171 error (all fields must be fully assigned before control is returned)
            var queryData = queryImpl->_QueryData;
            m_Safety0 = queryImpl->SafetyHandles->GetEntityManagerSafetyHandle();
#if UNITY_DOTSRUNTIME
            // TODO(DOTS-6573): DOTSRT can use the main code path once it supports AtomicSafetyHandle.SetExclusiveWeak()
            m_SafetyReadOnlyCount = 1; // for EntityManager handle
            m_SafetyReadWriteCount = 0;
#else
            m_SafetyReadOnlyCount = 1 + queryData->EnableableComponentTypeIndexCount; // +1 for EntityManager handle
            m_SafetyReadWriteCount = 0;
            fixed (AtomicSafetyHandle* pEnableableHandles = &m_SafetyEnableable1)
            {
                for (int i = 0; i < EntityQueryManager.MAX_ENABLEABLE_COMPONENTS_PER_QUERY; ++i)
                {
                    if (i < queryData->EnableableComponentTypeIndexCount)
                    {
                        pEnableableHandles[i] =
                            queryImpl->SafetyHandles->GetSafetyHandle(queryData->EnableableComponentTypeIndices[i],
                                true);
                        AtomicSafetyHandle.SetExclusiveWeak(ref pEnableableHandles[i], true);
                    }
                }
            }
#endif
        }
    }
#endif

    [GenerateTestsForBurstCompatibility]
    internal unsafe struct EntityQueryManager
    {
        private ComponentDependencyManager*    m_DependencyManager;
        private BlockAllocator                 m_EntityQueryDataChunkAllocator;
        private UnsafePtrList<EntityQueryData> m_EntityQueryDatas;
        internal const int MAX_ENABLEABLE_COMPONENTS_PER_QUERY = 8;

        private UntypedUnsafeParallelHashMap           m_EntityQueryDataCacheUntyped;
        internal int                           m_EntityQueryMasksAllocated;

        public static void Create(EntityQueryManager* queryManager, ComponentDependencyManager* dependencyManager)
        {
            queryManager->m_DependencyManager = dependencyManager;
            queryManager->m_EntityQueryDataChunkAllocator = new BlockAllocator(AllocatorManager.Persistent, 16 * 1024 * 1024); // 16MB should be enough
            ref var entityQueryCache = ref UnsafeUtility.As<UntypedUnsafeParallelHashMap, UnsafeParallelMultiHashMap<int, int>>(ref queryManager->m_EntityQueryDataCacheUntyped);
            entityQueryCache = new UnsafeParallelMultiHashMap<int, int>(1024, Allocator.Persistent);
            queryManager->m_EntityQueryDatas = new UnsafePtrList<EntityQueryData>(0, Allocator.Persistent);
            queryManager->m_EntityQueryMasksAllocated = 0;
        }

        public static void Destroy(EntityQueryManager* manager)
        {
            manager->Dispose();
        }

        void Dispose()
        {
            ref var entityQueryCache = ref UnsafeUtility.As<UntypedUnsafeParallelHashMap, UnsafeParallelMultiHashMap<int, int>>(ref m_EntityQueryDataCacheUntyped);
            entityQueryCache.Dispose();
            for (var g = 0; g < m_EntityQueryDatas.Length; ++g)
            {
                m_EntityQueryDatas.Ptr[g]->Dispose();
            }
            m_EntityQueryDatas.Dispose();
            //@TODO: Need to wait for all job handles to be completed..
            m_EntityQueryDataChunkAllocator.Dispose();
        }

        ArchetypeQuery* CreateArchetypeQueries(ref UnsafeScratchAllocator unsafeScratchAllocator, ComponentType* requiredTypes, int count)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp);

            for (int i = 0; i != count; i++)
            {
                if (requiredTypes[i].AccessModeType == ComponentType.AccessMode.Exclude)
                {
                    var noneType = ComponentType.ReadOnly(requiredTypes[i].TypeIndex);
                    builder.WithNone(&noneType, 1);
                }
                else
                {
                    builder.WithAll(&requiredTypes[i], 1);
                }
            }

            builder.FinalizeQueryInternal();

            var result = CreateArchetypeQueries(ref unsafeScratchAllocator, builder);

            builder.Dispose();

            return result;
        }

        internal void ConstructTypeArray(ref UnsafeScratchAllocator unsafeScratchAllocator, UnsafeList<ComponentType> types, out TypeIndex* outTypes, out byte* outAccessModes, out int outLength)
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
                outTypes = (TypeIndex*)unsafeScratchAllocator.Allocate<TypeIndex>(types.Length);
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

        private static ComponentType GetWriteGroupReadOnlyComponentType(TypeIndex* writeGroupTypes, int i)
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
        // [X] Introduce EntityQueryBuilder and test it
        // [X] Change internals to take an EntityQueryBuilder as an in parameter
        // [X] Validate queryData in CreateArchetypeQueries
        // [x] Everyone calling this needs to convert managed stuff into unmanaged EQDBuilder
        // [x] Public overloads of APIs to offer an EQDBuilder option
        // [ ] Deprecate EntityQueryDesc
        ArchetypeQuery* CreateArchetypeQueries(ref UnsafeScratchAllocator unsafeScratchAllocator, in EntityQueryBuilder queryBuilder)
        {
            var types = queryBuilder._builderDataPtr->_typeData;
            var queryData = queryBuilder._builderDataPtr->_indexData;
            var outQuery = (ArchetypeQuery*)unsafeScratchAllocator.Allocate(sizeof(ArchetypeQuery) * queryData.Length, UnsafeUtility.AlignOf<ArchetypeQuery>());

            // we need to build out new lists of component types to mutate them for WriteGroups
            var allTypes = new UnsafeList<ComponentType>(32, Allocator.Temp);
            var anyTypes = new UnsafeList<ComponentType>(32, Allocator.Temp);
            var noneTypes = new UnsafeList<ComponentType>(32, Allocator.Temp);
            var disabledTypes = new UnsafeList<ComponentType>(32, Allocator.Temp);
            var absentTypes = new UnsafeList<ComponentType>(32, Allocator.Temp);
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            var entityTypeIndex = TypeManager.GetTypeIndex<Entity>();
#endif

            for (int q = 0; q != queryData.Length; q++)
            {
                {
                    var typesAll = queryData[q].All;
                    for (int i = typesAll.Index; i < typesAll.Index + typesAll.Count; i++)
                    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                        if (types[i].TypeIndex == entityTypeIndex)
                        {
                            throw new ArgumentException("Entity is not allowed in list of component types for EntityQuery");
                        }
#endif
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
                        Assert.IsTrue(ComponentType.AccessMode.ReadOnly == type.AccessModeType, "EntityQueryBuilder.None must convert ComponentType.AccessMode to ReadOnly");
#endif
                        noneTypes.Add(types[i]);
                    }
                }

                {
                    var typesDisabled = queryData[q].Disabled;
                    for (int i = typesDisabled.Index; i < typesDisabled.Index + typesDisabled.Count; i++)
                    {
                        disabledTypes.Add(types[i]);
                    }
                }

                {
                    var typesAbsent = queryData[q].Absent;
                    for (int i = typesAbsent.Index; i < typesAbsent.Index + typesAbsent.Count; i++)
                    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                        var type = types[i];
                        // Can not use Assert.AreEqual here because it uses the (object, object) overload which
                        // boxes the enums being compared, and that can not be burst compiled.
                        Assert.IsTrue(ComponentType.AccessMode.ReadOnly == type.AccessModeType, "EntityQueryBuilder.Absent must convert ComponentType.AccessMode to ReadOnly");
#endif
                        absentTypes.Add(types[i]);
                    }
                }


                // Validate the queryBuilder has components declared in a consistent way
                EntityQueryBuilder.Validate(allTypes, anyTypes, noneTypes, disabledTypes, absentTypes);

                var isFilterWriteGroup = (queryData[q].Options & EntityQueryOptions.FilterWriteGroup) != 0;
                if (isFilterWriteGroup)
                {
                    // Each ReadOnly<type> in any or all
                    //   if has WriteGroup types,
                    //   - Recursively add to any (if not explictly mentioned)
                    var explicitList = new UnsafeList<ComponentType>(allTypes.Length + anyTypes.Length +
                                                                     noneTypes.Length + disabledTypes.Length +
                                                                     absentTypes.Length + 16, Allocator.Temp);
                    explicitList.AddRange(allTypes);
                    explicitList.AddRange(anyTypes);
                    explicitList.AddRange(noneTypes);
                    explicitList.AddRange(disabledTypes);
                    explicitList.AddRange(absentTypes);

                    for (int i = 0; i < anyTypes.Length; i++)
                        IncludeDependentWriteGroups(anyTypes[i], ref explicitList);
                    for (int i = 0; i < allTypes.Length; i++)
                        IncludeDependentWriteGroups(allTypes[i], ref explicitList);
                    for (int i = 0; i < disabledTypes.Length; i++)
                        IncludeDependentWriteGroups(disabledTypes[i], ref explicitList);

                    // Each ReadWrite<type> in any or all
                    //   if has WriteGroup types,
                    //     Add to none (if not exist in any or all or none)

                    for (int i = 0; i < anyTypes.Length; i++)
                        ExcludeWriteGroups(anyTypes[i], ref noneTypes, explicitList);
                    for (int i = 0; i < allTypes.Length; i++)
                        ExcludeWriteGroups(allTypes[i], ref noneTypes, explicitList);
                    for (int i = 0; i < disabledTypes.Length; i++)
                        ExcludeWriteGroups(disabledTypes[i], ref noneTypes, explicitList);
                    explicitList.Dispose();
                }

                ConstructTypeArray(ref unsafeScratchAllocator, noneTypes, out outQuery[q].None,
                    out outQuery[q].NoneAccessMode, out outQuery[q].NoneCount);
                ConstructTypeArray(ref unsafeScratchAllocator, allTypes, out outQuery[q].All,
                    out outQuery[q].AllAccessMode, out outQuery[q].AllCount);
                ConstructTypeArray(ref unsafeScratchAllocator, anyTypes, out outQuery[q].Any,
                    out outQuery[q].AnyAccessMode, out outQuery[q].AnyCount);
                ConstructTypeArray(ref unsafeScratchAllocator, disabledTypes, out outQuery[q].Disabled,
                    out outQuery[q].DisabledAccessMode, out outQuery[q].DisabledCount);
                ConstructTypeArray(ref unsafeScratchAllocator, absentTypes, out outQuery[q].Absent,
                    out outQuery[q].AbsentAccessMode, out outQuery[q].AbsentCount);

                allTypes.Clear();
                anyTypes.Clear();
                noneTypes.Clear();
                disabledTypes.Clear();
                absentTypes.Clear();
                outQuery[q].Options = queryData[q].Options;
            }

            allTypes.Dispose();
            anyTypes.Dispose();
            noneTypes.Dispose();
            disabledTypes.Dispose();
            absentTypes.Dispose();
            return outQuery;
        }

        internal static bool CompareQueryArray(in EntityQueryBuilder builder, EntityQueryBuilder.ComponentIndexArray arr, TypeIndex* typeArray, byte* accessModeArray, int typeArrayCount)
        {
            int arrCount = arr.Count;
            if (typeArrayCount != arrCount)
                return false;

            var sortedTypes = stackalloc ComponentType[arrCount];
            var types = builder._builderDataPtr->_typeData;
            for (var i = 0; i < arrCount; ++i)
            {
                SortingUtilities.InsertSorted(sortedTypes, i, types[arr.Index + i]);
            }

            for (var i = 0; i < arrCount; ++i)
            {
                if (typeArray[i] != sortedTypes[i].TypeIndex || accessModeArray[i] != (byte)sortedTypes[i].AccessModeType)
                    return false;
            }

            return true;
        }


        public static bool CompareQuery(in EntityQueryBuilder queryBuilder, EntityQueryData* queryData)
        {
            queryBuilder.FinalizeQueryInternal();
            var indexData = queryBuilder._builderDataPtr->_indexData;
            int count = indexData.Length;
            if (queryData->ArchetypeQueryCount != count)
                return false;

            for (int i = 0; i != count; i++)
            {
                ref var archetypeQuery = ref queryData->ArchetypeQueries[i];
                var q = indexData[i];

                if (!CompareQueryArray(queryBuilder, q.All, archetypeQuery.All, archetypeQuery.AllAccessMode, archetypeQuery.AllCount))
                    return false;
                if (!CompareQueryArray(queryBuilder, q.None, archetypeQuery.None, archetypeQuery.NoneAccessMode, archetypeQuery.NoneCount))
                    return false;
                if (!CompareQueryArray(queryBuilder, q.Any, archetypeQuery.Any, archetypeQuery.AnyAccessMode, archetypeQuery.AnyCount))
                    return false;
                if (!CompareQueryArray(queryBuilder, q.Disabled, archetypeQuery.Disabled, archetypeQuery.DisabledAccessMode, archetypeQuery.DisabledCount))
                    return false;
                if (!CompareQueryArray(queryBuilder, q.Absent, archetypeQuery.Absent, archetypeQuery.AbsentAccessMode, archetypeQuery.AbsentCount))
                    return false;
                if (q.Options != archetypeQuery.Options)
                    return false;
            }

            return true;
        }

        private int IntersectSortedComponentIndexArrays(ComponentType* arrayA, int arrayACount, ComponentType* arrayB, int arrayBCount, ComponentType* outArray)
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
                    intersectionCount++;
                    i++;
                    j++;
                }
            }

            return intersectionCount;
        }

        // Calculates the intersection of "All" and "Disabled" arrays from the provided ArchetypeQuery objects
        private ComponentType* CalculateRequiredComponentsFromQuery(ref UnsafeScratchAllocator allocator, ArchetypeQuery* queries, int queryCount, out int outRequiredComponentsCount)
        {
            var maxIntersectionCount = 0;
            for (int queryIndex = 0; queryIndex < queryCount; ++queryIndex)
                maxIntersectionCount = math.max(maxIntersectionCount, queries[queryIndex].AllCount + queries[queryIndex].DisabledCount);

            // Populate and sort a combined array of all+disabled component types and their access modes from the first ArchetypeQuery
            var outRequiredComponents = (ComponentType*)allocator.Allocate<ComponentType>(maxIntersectionCount+1);
            // The first required component is always Entity.
            outRequiredComponents[0] = ComponentType.ReadWrite<Entity>();
            var intersection = outRequiredComponents + 1;
            for (int j = 0; j < queries[0].AllCount; ++j)
            {
                intersection[j] = new ComponentType
                {
                    TypeIndex = queries[0].All[j],
                    AccessModeType = (ComponentType.AccessMode)queries[0].AllAccessMode[j],
                };
            }
            for (int j = 0; j < queries[0].DisabledCount; ++j)
            {
                intersection[j+queries[0].AllCount] = new ComponentType
                {
                    TypeIndex = queries[0].Disabled[j],
                    AccessModeType = (ComponentType.AccessMode)queries[0].DisabledAccessMode[j],
                };
            }
            NativeSortExtension.Sort(intersection, maxIntersectionCount);

            // For each additional ArchetypeQuery, create the same sorted array of component types, and reduce the
            // original array to the intersection of these two arrays.
            var intersectionCount = maxIntersectionCount;
            var queryRequiredTypes = (ComponentType*)allocator.Allocate<ComponentType>(maxIntersectionCount);
            for (int i = 1; i < queryCount; ++i)
            {
                int queryRequiredCount = queries[i].AllCount + queries[i].DisabledCount;
                for (int j = 0; j < queries[i].AllCount; ++j)
                {
                    queryRequiredTypes[j] = new ComponentType
                    {
                        TypeIndex = queries[i].All[j],
                        AccessModeType = (ComponentType.AccessMode)queries[i].AllAccessMode[j],
                    };
                }
                for (int j = 0; j < queries[i].DisabledCount; ++j)
                {
                    queryRequiredTypes[j+queries[i].AllCount] = new ComponentType
                    {
                        TypeIndex = queries[i].Disabled[j],
                        AccessModeType = (ComponentType.AccessMode)queries[i].DisabledAccessMode[j],
                    };
                }
                NativeSortExtension.Sort(queryRequiredTypes, queryRequiredCount);
                intersectionCount = IntersectSortedComponentIndexArrays(intersection, intersectionCount,
                    queryRequiredTypes, queryRequiredCount, intersection);
            }
            outRequiredComponentsCount = intersectionCount + 1;
            return outRequiredComponents;
        }

        [ExcludeFromBurstCompatTesting("Takes managed array")]
        internal static void ConvertToEntityQueryBuilder(ref EntityQueryBuilder builder, EntityQueryDesc[] queryDesc)
        {
            for (int q = 0; q != queryDesc.Length; q++)
            {
                ref var desc = ref queryDesc[q];
                fixed (ComponentType* allTypes = desc.All)
                {
                    builder.WithAll(allTypes, desc.All.Length);
                }
                fixed (ComponentType* anyTypes = desc.Any)
                {
                    builder.WithAny(anyTypes, desc.Any.Length);
                }
                fixed (ComponentType* noneTypes = desc.None)
                {
                    builder.WithNone(noneTypes, desc.None.Length);
                }
                fixed (ComponentType* disabledTypes = desc.Disabled)
                {
                    builder.WithDisabled(disabledTypes, desc.Disabled.Length);
                }
                fixed (ComponentType* absentTypes = desc.Absent)
                {
                    builder.WithAbsent(absentTypes, desc.Absent.Length);
                }

                builder.WithOptions(desc.Options);
                builder.FinalizeQueryInternal();
            }
        }

        public EntityQuery CreateEntityQuery(EntityDataAccess* access, EntityQueryBuilder query)
        {
            query.FinalizeQueryInternal();

            var buffer = stackalloc byte[2048];
            var scratchAllocator = new UnsafeScratchAllocator(buffer, 2048);
            var archetypeQuery = CreateArchetypeQueries(ref scratchAllocator, query);

            var indexData = query._builderDataPtr->_indexData;
            var outRequiredComponents = CalculateRequiredComponentsFromQuery(ref scratchAllocator, archetypeQuery, indexData.Length, out var outRequiredComponentsCount);
            return CreateEntityQuery(access, archetypeQuery, indexData.Length, outRequiredComponents, outRequiredComponentsCount);
        }

        public EntityQuery CreateEntityQuery(EntityDataAccess* access, ComponentType* inRequiredComponents, int inRequiredComponentsCount)
        {
            var buffer = stackalloc byte[1024];
            var scratchAllocator = new UnsafeScratchAllocator(buffer, 1024);
            var archetypeQueries = CreateArchetypeQueries(ref scratchAllocator, inRequiredComponents, inRequiredComponentsCount);
            var outRequiredComponents = (ComponentType*)scratchAllocator.Allocate<ComponentType>(inRequiredComponentsCount + 1);
            outRequiredComponents[0] = ComponentType.ReadWrite<Entity>();
            for (int i = 0; i != inRequiredComponentsCount; i++)
                SortingUtilities.InsertSorted(outRequiredComponents + 1, i, inRequiredComponents[i]);
            var outRequiredComponentsCount = inRequiredComponentsCount + 1;
            return CreateEntityQuery(access, archetypeQueries, 1, outRequiredComponents, outRequiredComponentsCount);
        }

        bool Matches(EntityQueryData* grp, ArchetypeQuery* archetypeQueries, int archetypeQueryCount,
            ComponentType* requiredComponents, int requiredComponentsCount)
        {
            if (requiredComponentsCount != grp->RequiredComponentsCount)
                return false;
            if (archetypeQueryCount != grp->ArchetypeQueryCount)
                return false;
            if (requiredComponentsCount > 0 && UnsafeUtility.MemCmp(requiredComponents, grp->RequiredComponents, sizeof(ComponentType) * requiredComponentsCount) != 0)
                return false;
            for (var i = 0; i < archetypeQueryCount; ++i)
                if (!archetypeQueries[i].Equals(grp->ArchetypeQueries[i]))
                    return false;
            return true;
        }

        void* ChunkAllocate<T>(int count = 1, void *source = null) where T : struct
        {
            var bytes = count * UnsafeUtility.SizeOf<T>();
            if (bytes == 0)
                return null;
            var pointer = m_EntityQueryDataChunkAllocator.Allocate(bytes, UnsafeUtility.AlignOf<T>());
            if (source != null)
                UnsafeUtility.MemCpy(pointer, source, bytes);
            return pointer;
        }

        public EntityQuery CreateEntityQuery(EntityDataAccess* access,
            ArchetypeQuery* archetypeQueries,
            int archetypeQueryCount,
            ComponentType* requiredComponents,
            int requiredComponentCount)
        {
            //@TODO: Validate that required types is subset of archetype filters all...

            int hash = (int)math.hash(requiredComponents, requiredComponentCount * sizeof(ComponentType));
            for (var i = 0; i < archetypeQueryCount; ++i)
                hash = hash * 397 ^ archetypeQueries[i].GetHashCode();
            EntityQueryData* queryData = null;
            ref var entityQueryCache = ref UnsafeUtility.As<UntypedUnsafeParallelHashMap, UnsafeParallelMultiHashMap<int, int>>(ref m_EntityQueryDataCacheUntyped);

            if (entityQueryCache.TryGetFirstValue(hash, out var entityQueryIndex, out var iterator))
            {
                do
                {
                    var possibleMatch = m_EntityQueryDatas.Ptr[entityQueryIndex];
                    if (Matches(possibleMatch, archetypeQueries, archetypeQueryCount, requiredComponents, requiredComponentCount))
                    {
                        queryData = possibleMatch;
                        break;
                    }
                }
                while (entityQueryCache.TryGetNextValue(out entityQueryIndex, ref iterator));
            }

            if (queryData == null)
            {
                // Validate input archetype queries
                bool queryIgnoresEnabledBits =
                    (archetypeQueries[0].Options & EntityQueryOptions.IgnoreComponentEnabledState) != 0;
                for (int iAQ = 1; iAQ < archetypeQueryCount; ++iAQ)
                {
                    bool hasIgnoreEnabledBitsFlag = (archetypeQueries[iAQ].Options & EntityQueryOptions.IgnoreComponentEnabledState) != 0;
                    if (hasIgnoreEnabledBitsFlag != queryIgnoresEnabledBits)
                        throw new ArgumentException(
                            $"All EntityQueryOptions passed to CreateEntityQuery() must have the same value for the IgnoreComponentEnabledState flag");
                }

                // count & identify enableable component types
                int totalComponentCount = 0;
                for (int iAQ = 0; iAQ < archetypeQueryCount; ++iAQ)
                {
                    totalComponentCount += archetypeQueries[iAQ].AllCount + archetypeQueries[iAQ].AnyCount +
                                           archetypeQueries[iAQ].NoneCount + archetypeQueries[iAQ].DisabledCount +
                                           archetypeQueries[iAQ].AbsentCount;
                }

                var allEnableableTypeIndices = new NativeList<TypeIndex>(totalComponentCount, Allocator.Temp);
                for (int iAQ = 0; iAQ < archetypeQueryCount; ++iAQ)
                {
                    ref ArchetypeQuery aq = ref archetypeQueries[iAQ];
                    for (int i = 0; i < aq.AllCount; ++i)
                    {
                        if (aq.All[i].IsEnableable)
                            allEnableableTypeIndices.Add(aq.All[i]);
                    }
                    for (int i = 0; i < aq.NoneCount; ++i)
                    {
                        if (aq.None[i].IsEnableable)
                            allEnableableTypeIndices.Add(aq.None[i]);
                    }
                    for (int i = 0; i < aq.AnyCount; ++i)
                    {
                        if (aq.Any[i].IsEnableable)
                            allEnableableTypeIndices.Add(aq.Any[i]);
                    }
                    for (int i = 0; i < aq.DisabledCount; ++i)
                    {
                        // All "Disabled" types must be enableable. We'll enforce that elsewhere.
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                        if (!aq.Disabled[i].IsEnableable)
                            throw new InvalidOperationException($"Component type {aq.Disabled[i]} passed with WithDisabled() is not an enableable component");
#endif
                        allEnableableTypeIndices.Add(aq.Disabled[i]);
                    }
                    // absent components are never present in a matching archetype, so it doesn't matter if they're
                    // enableable or not.
                }
                // eliminate duplicate type indices
                if (allEnableableTypeIndices.Length > 0)
                {
                    allEnableableTypeIndices.Sort();
                    int lastUniqueIndex = 0;
                    for (int i = 1; i < allEnableableTypeIndices.Length; ++i)
                    {
                        if (allEnableableTypeIndices[i] != allEnableableTypeIndices[lastUniqueIndex])
                        {
                            allEnableableTypeIndices[++lastUniqueIndex] = allEnableableTypeIndices[i];
                        }
                    }
                    allEnableableTypeIndices.Length = lastUniqueIndex + 1;
                    // This limit matches the number of safety handles for enableable types we store in job structs.
                    if (allEnableableTypeIndices.Length > MAX_ENABLEABLE_COMPONENTS_PER_QUERY)
                        throw new ArgumentException(
                            $"EntityQuery objects may not reference more than {MAX_ENABLEABLE_COMPONENTS_PER_QUERY} enableable components");
                }
                // Allocate and populate query data
                queryData = (EntityQueryData*)ChunkAllocate<EntityQueryData>();
                queryData->RequiredComponentsCount = requiredComponentCount;
                queryData->RequiredComponents = (ComponentType*)ChunkAllocate<ComponentType>(requiredComponentCount, requiredComponents);
                queryData->EnableableComponentTypeIndexCount = queryIgnoresEnabledBits ? 0 : allEnableableTypeIndices.Length;
                queryData->EnableableComponentTypeIndices = (TypeIndex*)ChunkAllocate<TypeIndex>(allEnableableTypeIndices.Length, allEnableableTypeIndices.GetUnsafeReadOnlyPtr());
                queryData->HasEnableableComponents = (allEnableableTypeIndices.Length > 0 && !queryIgnoresEnabledBits) ? (byte)1 : (byte)0;

                InitializeReaderWriter(queryData, requiredComponents, requiredComponentCount);
                queryData->ArchetypeQueryCount = archetypeQueryCount;
                queryData->ArchetypeQueries = (ArchetypeQuery*)ChunkAllocate<ArchetypeQuery>(archetypeQueryCount, archetypeQueries);
                for (var i = 0; i < archetypeQueryCount; ++i)
                {
                    queryData->ArchetypeQueries[i].All = (TypeIndex*)ChunkAllocate<TypeIndex>(queryData->ArchetypeQueries[i].AllCount, archetypeQueries[i].All);
                    queryData->ArchetypeQueries[i].Any = (TypeIndex*)ChunkAllocate<TypeIndex>(queryData->ArchetypeQueries[i].AnyCount, archetypeQueries[i].Any);
                    queryData->ArchetypeQueries[i].None = (TypeIndex*)ChunkAllocate<TypeIndex>(queryData->ArchetypeQueries[i].NoneCount, archetypeQueries[i].None);
                    queryData->ArchetypeQueries[i].Disabled = (TypeIndex*)ChunkAllocate<TypeIndex>(queryData->ArchetypeQueries[i].DisabledCount, archetypeQueries[i].Disabled);
                    queryData->ArchetypeQueries[i].Absent = (TypeIndex*)ChunkAllocate<TypeIndex>(queryData->ArchetypeQueries[i].AbsentCount, archetypeQueries[i].Absent);
                    queryData->ArchetypeQueries[i].AllAccessMode = (byte*)ChunkAllocate<byte>(queryData->ArchetypeQueries[i].AllCount, archetypeQueries[i].AllAccessMode);
                    queryData->ArchetypeQueries[i].AnyAccessMode = (byte*)ChunkAllocate<byte>(queryData->ArchetypeQueries[i].AnyCount, archetypeQueries[i].AnyAccessMode);
                    queryData->ArchetypeQueries[i].NoneAccessMode = (byte*)ChunkAllocate<byte>(queryData->ArchetypeQueries[i].NoneCount, archetypeQueries[i].NoneAccessMode);
                    queryData->ArchetypeQueries[i].DisabledAccessMode = (byte*)ChunkAllocate<byte>(queryData->ArchetypeQueries[i].DisabledCount, archetypeQueries[i].DisabledAccessMode);
                    queryData->ArchetypeQueries[i].AbsentAccessMode = (byte*)ChunkAllocate<byte>(queryData->ArchetypeQueries[i].AbsentCount, archetypeQueries[i].AbsentAccessMode);
                }

                var ecs = access->EntityComponentStore;

                queryData->MatchingArchetypes = new UnsafeMatchingArchetypePtrList(access->EntityComponentStore);
                queryData->CreateMatchingChunkCache(access->EntityComponentStore);

                queryData->EntityQueryMask = new EntityQueryMask();

                for (var i = 0; i < ecs->m_Archetypes.Length; ++i)
                {
                    var archetype = ecs->m_Archetypes.Ptr[i];
                    AddArchetypeIfMatching(archetype, queryData);
                }

                entityQueryCache.Add(hash, m_EntityQueryDatas.Length);
                m_EntityQueryDatas.Add(queryData);
                queryData->InvalidateChunkCache();
            }

            return EntityQuery.Construct(queryData, access);
        }

        void InitializeReaderWriter(EntityQueryData* grp, ComponentType* requiredTypes, int requiredCount)
        {
            Assert.IsTrue(requiredCount > 0);
            Assert.IsTrue(requiredTypes[0] == ComponentType.ReadWrite<Entity>());

            grp->ReaderTypesCount = 0;
            grp->WriterTypesCount = 0;

            for (var i = 1; i != requiredCount; i++)
            {
                if (requiredTypes[i].IsZeroSized && !requiredTypes[i].IsEnableable)
                    continue;

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

            grp->ReaderTypes = (TypeIndex*)m_EntityQueryDataChunkAllocator.Allocate(sizeof(TypeIndex) * grp->ReaderTypesCount, 4);
            grp->WriterTypes = (TypeIndex*)m_EntityQueryDataChunkAllocator.Allocate(sizeof(TypeIndex) * grp->WriterTypesCount, 4);

            var curReader = 0;
            var curWriter = 0;
            for (var i = 1; i != requiredCount; i++)
            {
                if (requiredTypes[i].IsZeroSized && !requiredTypes[i].IsEnableable)
                    continue;

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
                for (var g = 0; g < m_EntityQueryDatas.Length; ++g)
                {
                    var grp = m_EntityQueryDatas.Ptr[g];
                    AddArchetypeIfMatching(archetypeList.Ptr[i], grp);
                }
            }
        }

        void AddArchetypeIfMatching(Archetype* archetype, EntityQueryData* query)
        {
            if (!IsMatchingArchetype(archetype, query))
                return;

            var match = MatchingArchetype.Create(ref m_EntityQueryDataChunkAllocator, archetype, query);
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

            // TODO(DOTS-5638): this assumes that query only contains one ArchetypeQuery
            var enableableAllCount = 0;
            typeComponentIndex = 0;
            for (var i = 0; i < query->ArchetypeQueries->AllCount; ++i)
            {
                var typeIndex = query->ArchetypeQueries->All[i];
                if (TypeManager.IsEnableable(typeIndex))
                {
                    typeComponentIndex = ChunkDataUtility.GetNextIndexInTypeArray(archetype, typeIndex, typeComponentIndex);
                    Assert.AreNotEqual(-1, typeComponentIndex);
                    match->EnableableTypeMemoryOrderInArchetype_All[enableableAllCount++] = archetype->TypeIndexInArchetypeToMemoryOrderIndex[typeComponentIndex];
                }
            }

            var enableableNoneCount = 0;
            typeComponentIndex = 0;
            for (var i = 0; i < query->ArchetypeQueries->NoneCount; ++i)
            {
                var typeIndex = query->ArchetypeQueries->None[i];
                if (TypeManager.IsEnableable(typeIndex))
                {
                    var currentTypeComponentIndex = ChunkDataUtility.GetNextIndexInTypeArray(archetype, typeIndex, typeComponentIndex);
                    if (currentTypeComponentIndex != -1) // we skip storing "None" component types for matching archetypes that do not contain the "None" type (there are no bits to check)
                    {
                        match->EnableableTypeMemoryOrderInArchetype_None[enableableNoneCount++] = archetype->TypeIndexInArchetypeToMemoryOrderIndex[currentTypeComponentIndex];
                        typeComponentIndex = currentTypeComponentIndex;
                    }
                }
            }

            var enableableAnyCount = 0;
            typeComponentIndex = 0;
            for (var i = 0; i < query->ArchetypeQueries->AnyCount; ++i)
            {
                var typeIndex = query->ArchetypeQueries->Any[i];
                if (TypeManager.IsEnableable(typeIndex))
                {
                    var currentTypeComponentIndex = ChunkDataUtility.GetNextIndexInTypeArray(archetype, typeIndex, typeComponentIndex);
                    // The archetype may not contain all the Any types (by definition; this is the whole point of Any).
                    // Skip storing the missing types.
                    if (currentTypeComponentIndex != -1)
                    {
                        match->EnableableTypeMemoryOrderInArchetype_Any[enableableAnyCount++] = archetype->TypeIndexInArchetypeToMemoryOrderIndex[currentTypeComponentIndex];
                        typeComponentIndex = currentTypeComponentIndex;
                    }
                }
            }

            var enableableDisabledCount = 0;
            typeComponentIndex = 0;
            for (var i = 0; i < query->ArchetypeQueries->DisabledCount; ++i)
            {
                var typeIndex = query->ArchetypeQueries->Disabled[i];
                if (TypeManager.IsEnableable(typeIndex))
                {
                    typeComponentIndex = ChunkDataUtility.GetNextIndexInTypeArray(archetype, typeIndex, typeComponentIndex);
                    Assert.AreNotEqual(-1, typeComponentIndex);
                    match->EnableableTypeMemoryOrderInArchetype_Disabled[enableableDisabledCount++] = archetype->TypeIndexInArchetypeToMemoryOrderIndex[typeComponentIndex];
                }
            }
        }

        static bool IsMatchingArchetype(Archetype* archetype, EntityQueryData* query)
        {
            for (int i = 0; i != query->ArchetypeQueryCount; i++)
            {
                if (IsMatchingArchetype(archetype, query->ArchetypeQueries + i))
                    return true;
            }

            return false;
        }

        static bool IsMatchingArchetype(Archetype* archetype, ArchetypeQuery* query)
        {
            //TODO(DOTS-7717): The nested for loops in these methods can be rewritten to take advantage of the fact that the component type arrays are sorted.
            return
                TestMatchingArchetypeAll(archetype, query->All, query->AllCount, query->Options)
                && TestMatchingArchetypeNone(archetype, query->None, query->NoneCount)
                && TestMatchingArchetypeAny(archetype, query->Any, query->AnyCount)
                // TODO: can we reuse existing methods for the two new arrays? (None for Absent, and All for Disabled)?
                && TestMatchingArchetypeAbsent(archetype, query->Absent, query->AbsentCount)
                && TestMatchingArchetypeDisabled(archetype, query->Disabled, query->DisabledCount);
        }

        static bool TestMatchingArchetypeAny(Archetype* archetype, TypeIndex* anyTypes, int anyCount)
        {
            if (anyCount == 0)
                return true;

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

        static bool TestMatchingArchetypeNone(Archetype* archetype, TypeIndex* noneTypes, int noneCount)
        {
            var componentTypes = archetype->Types;
            var componentTypesCount = archetype->TypesCount;
            for (var i = 0; i < componentTypesCount; i++)
            {
                var componentTypeIndex = componentTypes[i].TypeIndex;
                for (var j = 0; j < noneCount; j++)
                {
                    var noneTypeIndex = noneTypes[j];

                    if (componentTypeIndex == noneTypeIndex)
                    {
                        if (!TypeManager.IsEnableable(componentTypeIndex))
                            return false;
                    }
                }
            }

            return true;
        }

        static bool TestMatchingArchetypeAbsent(Archetype* archetype, TypeIndex* absentTypes, int absentTypeCount)
        {
            var types = archetype->Types;
            var typeCount = archetype->TypesCount;

            for (var i = 0; i < typeCount; i++)
            {
                var typeIndex = types[i].TypeIndex;
                for (var j = 0; j < absentTypeCount; j++)
                {
                    var absentTypeIndex = absentTypes[j];
                    if (typeIndex == absentTypeIndex) // The archetype contains at least 1 component that ought to be absent.
                        return false;
                }
            }
            return true;
        }

        // Disabled components MUST be present in the archetype.
        static bool TestMatchingArchetypeDisabled(Archetype* archetype, TypeIndex* disabledTypes, int disabledCount)
        {
            var types = archetype->Types;
            var typeCount = archetype->TypesCount;

            int presentButDisabledTypeCount = 0;

            for (var i = 0; i < typeCount; i++)
            {
                var typeIndex = types[i].TypeIndex;

                for (var j = 0; j < disabledCount; j++)
                {
                    var disabledComponentTypeIndex = disabledTypes[j];
                    if (disabledComponentTypeIndex == typeIndex)
                        presentButDisabledTypeCount++;
                }
            }
            return presentButDisabledTypeCount == disabledCount;
        }

        static bool TestMatchingArchetypeAll(Archetype* archetype, TypeIndex* allTypes, int allCount, EntityQueryOptions options)
        {
            var componentTypes = archetype->Types;
            var componentTypesCount = archetype->TypesCount;
            var foundCount = 0;
            var disabledTypeIndex = TypeManager.GetTypeIndex<Disabled>();
            var prefabTypeIndex = TypeManager.GetTypeIndex<Prefab>();
            var systemInstanceTypeIndex = TypeManager.GetTypeIndex<SystemInstance>();
            var chunkHeaderTypeIndex = TypeManager.GetTypeIndex<ChunkHeader>();
            var includeDisabledEntities = (options & EntityQueryOptions.IncludeDisabledEntities) != 0;
            var includePrefab = (options & EntityQueryOptions.IncludePrefab) != 0;
            var includeSystems = (options & EntityQueryOptions.IncludeSystems) != 0;
            var includeChunkHeader = (options & EntityQueryOptions.IncludeMetaChunks) != 0;

            for (var i = 0; i < componentTypesCount; i++)
            {
                var componentTypeIndex = componentTypes[i].TypeIndex;
                for (var j = 0; j < allCount; j++)
                {
                    var allTypeIndex = allTypes[j];
                    if (allTypeIndex == disabledTypeIndex)
                        includeDisabledEntities = true;
                    if (allTypeIndex == prefabTypeIndex)
                        includePrefab = true;
                    if (allTypeIndex == chunkHeaderTypeIndex)
                        includeChunkHeader = true;
                    if (allTypeIndex == systemInstanceTypeIndex)
                        includeSystems = true;

                    if (componentTypeIndex == allTypeIndex) foundCount++;
                }
            }

            if (archetype->Disabled && !includeDisabledEntities)
                return false;
            if (archetype->Prefab && !includePrefab)
                return false;
            if (archetype->HasSystemInstanceComponents && !includeSystems)
                return false;
            if (archetype->HasChunkHeader && !includeChunkHeader)
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
        public int EnableableComponentsCount_Any;
        public int EnableableComponentsCount_Disabled; // TODO(DOTS-7809): the None and Disabled lists can be combined here, as they're treated identically in all cases.
        // No need to count enableable absent components, since they're not in the archetype (by definition)

        public fixed int IndexInArchetype[1];

        public int* EnableableTypeMemoryOrderInArchetype_All
        {
            get
            {
                fixed (int* basePtr = IndexInArchetype)
                {
                    return basePtr + RequiredComponentCount;
                }
            }
        }
        public int* EnableableTypeMemoryOrderInArchetype_None
        {
            get
            {
                fixed (int* basePtr = IndexInArchetype)
                {
                    return basePtr + RequiredComponentCount + EnableableComponentsCount_All;
                }
            }
        }
        public int* EnableableTypeMemoryOrderInArchetype_Any
        {
            get
            {
                fixed (int* basePtr = IndexInArchetype)
                {
                    return basePtr + RequiredComponentCount + EnableableComponentsCount_All + EnableableComponentsCount_None;
                }
            }
        }
        public int* EnableableTypeMemoryOrderInArchetype_Disabled
        {
            get
            {
                fixed (int* basePtr = IndexInArchetype)
                {
                    return basePtr + RequiredComponentCount + EnableableComponentsCount_All + EnableableComponentsCount_None + EnableableComponentsCount_Any;
                }
            }
        }
        public static int CalculateMatchingArchetypeEnableableTypeIntersectionCount(Archetype* archetype, TypeIndex* queryComponents, int queryComponentCount)
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
            // TODO(DOTS-5638): this assumes that query only contains one ArchetypeQuery
            var enableableAllCount = CalculateMatchingArchetypeEnableableTypeIntersectionCount(archetype, query->ArchetypeQueries->All, query->ArchetypeQueries->AllCount);
            var enableableNoneCount = CalculateMatchingArchetypeEnableableTypeIntersectionCount(archetype, query->ArchetypeQueries->None, query->ArchetypeQueries->NoneCount);
            var enableableAnyCount = CalculateMatchingArchetypeEnableableTypeIntersectionCount(archetype, query->ArchetypeQueries->Any, query->ArchetypeQueries->AnyCount);
            var enableableDisabledCount = CalculateMatchingArchetypeEnableableTypeIntersectionCount(archetype, query->ArchetypeQueries->Disabled, query->ArchetypeQueries->DisabledCount);
            var totalEnableableTypeCount = enableableAllCount + enableableNoneCount + enableableAnyCount + enableableDisabledCount;
            var match = (MatchingArchetype*)allocator.Allocate(GetAllocationSize(query->RequiredComponentsCount, totalEnableableTypeCount), 8);
            match->Archetype = archetype;

            match->RequiredComponentCount = query->RequiredComponentsCount;
            match->EnableableComponentsCount_All = enableableAllCount;
            match->EnableableComponentsCount_None = enableableNoneCount;
            match->EnableableComponentsCount_Any = enableableAnyCount;
            match->EnableableComponentsCount_Disabled = enableableDisabledCount;

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

        public void Dispose() { UnsafeList<IntPtr>.Destroy(ListData); }
        public void Add(void* t) { ListData->Add((IntPtr)t); }

        [NativeDisableUnsafePtrRestriction]
        public EntityComponentStore* entityComponentStore;

        public UnsafeMatchingArchetypePtrList(EntityComponentStore* entityComponentStore)
        {
            ListData = UnsafeList<IntPtr>.Create(0, Allocator.Persistent);
            this.entityComponentStore = entityComponentStore;
        }
    }

    [GenerateTestsForBurstCompatibility]
    unsafe struct ArchetypeQuery : IEquatable<ArchetypeQuery>
    {
        public TypeIndex*   Any;
        public byte*        AnyAccessMode;
        public int          AnyCount;

        public TypeIndex*   All;
        public byte*        AllAccessMode;
        public int          AllCount;

        public TypeIndex*   None;
        public byte*        NoneAccessMode;
        public int          NoneCount;

        public TypeIndex*   Disabled;
        public byte*        DisabledAccessMode;
        public int          DisabledCount;

        public TypeIndex*   Absent;
        public byte*        AbsentAccessMode;
        public int          AbsentCount;

        public EntityQueryOptions  Options;

        public bool Equals(ArchetypeQuery other)
        {
            if (AnyCount != other.AnyCount)
                return false;
            if (AllCount != other.AllCount)
                return false;
            if (NoneCount != other.NoneCount)
                return false;
            if (DisabledCount != other.DisabledCount)
                return false;
            if (AbsentCount != other.AbsentCount)
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
            if (DisabledCount > 0 && UnsafeUtility.MemCmp(Disabled, other.Disabled, sizeof(int) * DisabledCount) != 0 &&
                UnsafeUtility.MemCmp(DisabledAccessMode, other.DisabledAccessMode, sizeof(byte) * DisabledCount) != 0)
                return false;
            if (AbsentCount > 0 && UnsafeUtility.MemCmp(Absent, other.Absent, sizeof(int) * AbsentCount) != 0 &&
                UnsafeUtility.MemCmp(AbsentAccessMode, other.AbsentAccessMode, sizeof(byte) * AbsentCount) != 0)
                return false;
            if (Options != other.Options)
                return false;

            return true;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = AnyCount + 1;
                hashCode = 397 * hashCode ^ (AllCount + 1);
                hashCode = 397 * hashCode ^ (NoneCount + 1);
                hashCode = 397 * hashCode ^ (DisabledCount + 1);
                hashCode = 397 * hashCode ^ (AbsentCount + 1);
                hashCode = 397 * hashCode ^ (int)Options;
                hashCode = (int)math.hash(Any, sizeof(int) * AnyCount, (uint)hashCode);
                hashCode = (int)math.hash(All, sizeof(int) * AllCount, (uint)hashCode);
                hashCode = (int)math.hash(None, sizeof(int) * NoneCount, (uint)hashCode);
                hashCode = (int)math.hash(Disabled, sizeof(int) * DisabledCount, (uint)hashCode);
                hashCode = (int)math.hash(Absent, sizeof(int) * AbsentCount, (uint)hashCode);
                hashCode = (int)math.hash(AnyAccessMode, sizeof(byte) * AnyCount, (uint)hashCode);
                hashCode = (int)math.hash(AllAccessMode, sizeof(byte) * AllCount, (uint)hashCode);
                hashCode = (int)math.hash(NoneAccessMode, sizeof(byte) * NoneCount, (uint)hashCode);
                hashCode = (int)math.hash(DisabledAccessMode, sizeof(byte) * DisabledCount, (uint)hashCode);
                hashCode = (int)math.hash(AbsentAccessMode, sizeof(byte) * AbsentCount, (uint)hashCode);
                return hashCode;
            }
        }
    }

    [NoAlias]
    [BurstCompile]
    unsafe struct UnsafeCachedChunkList
    {
        [NoAlias,NativeDisableUnsafePtrRestriction]
        internal UnsafePtrList<Chunk>* MatchingChunks;

        [NoAlias,NativeDisableUnsafePtrRestriction]
        internal UnsafeList<int>* PerChunkMatchingArchetypeIndex;

        [NoAlias,NativeDisableUnsafePtrRestriction]
        internal UnsafeList<int>* ChunkIndexInArchetype;

        [NoAlias,NativeDisableUnsafePtrRestriction]
        internal EntityComponentStore* EntityComponentStore;

        internal int CacheValid; // must not be a bool, for Burst compatibility

        internal Chunk** Ptr { get => (Chunk**)MatchingChunks->Ptr; }
        public int Length { get => MatchingChunks->Length; }
        public bool IsCacheValid { get => CacheValid != 0; }

        internal UnsafeCachedChunkList(EntityComponentStore* entityComponentStore)
        {
            EntityComponentStore = entityComponentStore;
            MatchingChunks = UnsafePtrList<Chunk>.Create(0, Allocator.Persistent);
            PerChunkMatchingArchetypeIndex = UnsafeList<int>.Create(0, Allocator.Persistent);
            ChunkIndexInArchetype = UnsafeList<int>.Create(0, Allocator.Persistent);
            CacheValid = 0;
        }

        internal void Append(Chunk** t, int addChunkCount, int matchingArchetypeIndex)
        {
            MatchingChunks->AddRange(new UnsafePtrList<Chunk>(t, addChunkCount));
            for (int i = 0; i < addChunkCount; ++i)
            {
                PerChunkMatchingArchetypeIndex->Add(matchingArchetypeIndex);
                ChunkIndexInArchetype->Add(i);
            }
        }

        internal void Dispose()
        {
            if (MatchingChunks != null)
                UnsafePtrList<Chunk>.Destroy(MatchingChunks);
            if (PerChunkMatchingArchetypeIndex != null)
                UnsafeList<int>.Destroy(PerChunkMatchingArchetypeIndex);
            if (ChunkIndexInArchetype != null)
                UnsafeList<int>.Destroy(ChunkIndexInArchetype);
        }

        internal void Invalidate()
        {
            CacheValid = 0;
        }

        [BurstCompile]
        internal static void Rebuild(ref UnsafeCachedChunkList cache, in EntityQueryData queryData)
        {
            cache.MatchingChunks->Clear();
            cache.PerChunkMatchingArchetypeIndex->Clear();
            cache.ChunkIndexInArchetype->Clear();

            int archetypeCount = queryData.MatchingArchetypes.Length;
            var ptrs = queryData.MatchingArchetypes.Ptr;
            for (int matchingArchetypeIndex = 0; matchingArchetypeIndex < archetypeCount; ++matchingArchetypeIndex)
            {
                var archetype = ptrs[matchingArchetypeIndex]->Archetype;
                if (archetype->EntityCount > 0)
                    archetype->Chunks.AddToCachedChunkList(ref cache, matchingArchetypeIndex);
            }

            cache.CacheValid = 1;
        }

        // Expensive debug validation, to make sure cached data is consistent with the associated query.
        // Throws an ArgumentException if anything's wrong, including if the cache.IsCacheValid flag is clear.
        // The forceCheckInvalidCache allows the validity flag check to skipped, e.g. by unit tests that want to
        // make sure this method actually detects the expected inconsistencies in a known invalid cache.
        internal static void AssertIsConsistent(in UnsafeCachedChunkList cache, in EntityQueryData queryData, bool forceCheckInvalidCache)
        {
            // If the input cache doesn't even think it's valid, its contents are definitely not guaranteed to be consistent.
            // Allow the caller to skip this check and check a known invalid cache any, for unit testing purposes
            if (!forceCheckInvalidCache && !cache.IsCacheValid)
            {
                throw new ArgumentException($"Can't check an invalid cache for consistency. Did you call queryData.GetMatchingChunkCache() to access this cache?");
            }
            // The cache's lists must all be the same length
            if (cache.ChunkIndexInArchetype->Length != cache.Length)
            {
                // This indicates an error in the cache update logic; every chunk in the cache must have a valid entry in the ChunkIndexInArchetype list.
                throw new ArgumentException($"query chunk cache inconsistency: ChunkIndexInArchetypeLength={cache.ChunkIndexInArchetype->Length}, cache length={cache.Length}");
            }
            if (cache.PerChunkMatchingArchetypeIndex->Length != cache.Length)
            {
                // This indicates an error in the cache update logic; every chunk in the cache must have a valid entry in the PerChunkMatchingArchetypeIndex list.
                throw new ArgumentException($"query chunk cache inconsistency: PerChunkMatchingArchetypeIndex={cache.PerChunkMatchingArchetypeIndex->Length}, cache length={cache.Length}");
            }

            // count the expected number of chunks in all matching archetypes
            int archetypeCount = queryData.MatchingArchetypes.Length;
            var matchingArchetypes = queryData.MatchingArchetypes.Ptr;
            int matchingArchetypeChunkCount = 0;
            for (int archetypeIndex = 0; archetypeIndex < archetypeCount; ++archetypeIndex)
            {
                var archetype = matchingArchetypes[archetypeIndex]->Archetype;
                matchingArchetypeChunkCount += archetype->Chunks.Count;
            }
            // If this fails, a cache that thinks it's valid definitely contains the wrong number of chunks.
            // This would indicate an error in the cache update logic.
            // However, instead of failing immediately, let's keep going and see if we can log more data about the missing/extra
            // chunk in the loop below. This same check appears at the end of the function, in case nothing else fires.
            //if (matchingArchetypeChunkCount != cache.Length)
            //{
            //    //throw new ArgumentException($"query chunk cache inconsistency: {archetypeCount} matching archetypes contain {matchingArchetypeChunkCount} chunks, but cache only contains {cache.MatchingChunks->Length} chunks");
            //}

            // Iterate over chunks in matching archetypes, and make sure they're at the correct location in the cache
            var chunkCounter = 0;
            for (int archetypeIndex = 0; archetypeIndex < archetypeCount; ++archetypeIndex)
            {
                var archetype = matchingArchetypes[archetypeIndex]->Archetype;
                int archetypeChunkCount = archetype->Chunks.Count;
                for (int chunkIndex = 0; chunkIndex < archetypeChunkCount; ++chunkIndex)
                {
                    if (chunkCounter >= cache.Length)
                    {
                        // This should *never* happen; it should have been caught in the check just above this loop.
                        // This would indicate that either the matching archetypes or the chunk cache are being modified
                        // concurrently with this consistency check.
                        throw new ArgumentException($"query chunk cache inconsistency: matching archetype chunk {chunkCounter} exceeds chunk cache length {cache.Length}");
                    }

                    var chunk = cache.MatchingChunks->Ptr[chunkCounter];
                    if (chunk != archetype->Chunks[chunkIndex])
                    {
                        // This means the chunk may actually be in the cache, but not at the index we expect it to be.
                        // This may indicate that the chunk cache update logic is iterating over matching archetypes/chunks in a different
                        // order than this consistency check.
                        throw new ArgumentException(@$"query chunk cache inconsistency: cached chunk at index {chunkCounter} should be at chunkIndex={chunkIndex} of archetype {archetypeIndex}.
Archetype has {archetypeChunkCount} chunks and {archetype->EntityCount} entities, with types {archetype->ToString()}.
Query matches {archetypeCount} archetypes.");
                    }
                    if (chunk->SequenceNumber != archetype->Chunks[chunkIndex]->SequenceNumber)
                    {
                        // Chunk* comparisons are not sufficient, because chunk allocations are pooled. The chunk may have
                        // been freed and reallocated into a new archetype.
                        // This would indicate that some operation is not invalidating the appropriate query caches.
                        throw new ArgumentException(@$"query chunk cache inconsistency: cached chunk at index {chunkCounter} has a different sequence number {chunk->SequenceNumber} than chunk in matching archetype with sequence number {archetype->Chunks[chunkIndex]->SequenceNumber}.
Archetype has {archetypeChunkCount} chunks and {archetype->EntityCount} entities, with types {archetype->ToString()}.
Query matches {archetypeCount} archetypes.");
                    }
                    if (cache.ChunkIndexInArchetype->Ptr[chunkCounter] != chunk->ListIndex)
                    {
                        // This chunk in the cache corresponds to the correct chunk in the archetype, but
                        // its entry in ChunkIndexInArchetype is incorrect.
                        // This would indicate that the cache's ChunkIndexInArchetype list is not being updated correctly when the
                        // cache is updated.
                        throw new ArgumentException(@$"query chunk cache inconsistency: cached chunk at index {chunkCounter} thinks it should be at index {cache.ChunkIndexInArchetype->Ptr[chunkCounter]} in archetype {archetypeIndex}'s chunk list, but it's actually at index {chunk->ListIndex}.
Archetype has {archetypeChunkCount} chunks and {archetype->EntityCount} entities, with types {archetype->ToString()}.
Query matches {archetypeCount} archetypes.");
                    }
                    if (cache.PerChunkMatchingArchetypeIndex->Ptr[chunkCounter] != archetypeIndex)
                    {
                        // This chunk in the cache corresponds to the correct chunk in the archetype, but
                        // its entry in PerChunkMatchingArchetypeIndex is incorrect.
                        // This would indicate that the cache's PerChunkMatchingArchetypeIndex list is not being updated correctly when the
                        // cache is updated.
                        throw new ArgumentException(@$"query chunk cache inconsistency: cached chunk at index {chunkCounter} thinks it should be in matching archetype {cache.PerChunkMatchingArchetypeIndex->Ptr[chunkCounter]}, but it's actually in archetype {archetypeIndex}.
Archetype has {archetypeChunkCount} chunks and {archetype->EntityCount} entities, with types {archetype->ToString()}.
Query matches {archetypeCount} archetypes.");
                    }
                    chunkCounter += 1;
                }
            }
            // Iterate over chunks in the cache, and make sure they're in the correct location in the matching archetypes.
            for (int cacheChunkIndex = 0; cacheChunkIndex < cache.Length; ++cacheChunkIndex)
            {
                var chunk = cache.MatchingChunks->Ptr[cacheChunkIndex];
                int expectedArchetypeIndex = cache.PerChunkMatchingArchetypeIndex->Ptr[cacheChunkIndex];
                int expectedChunkIndexInArchetype = cache.ChunkIndexInArchetype->Ptr[cacheChunkIndex];
                if (expectedArchetypeIndex < 0 || expectedArchetypeIndex >= archetypeCount)
                {
                    // This indicates objectively invalid data in the cache's PerChunkMatchingArchetypeIndex array.
                    // Be especially concerned about negative numbers; that would be a pretty clear sign of a memory stomp.
                    throw new ArgumentException(@$"query chunk cache inconsistency: matching archetype index {expectedArchetypeIndex} for cached chunk {cacheChunkIndex} is out of range.
Query matches {archetypeCount} archetypes.");
                }
                var archetype = matchingArchetypes[expectedArchetypeIndex]->Archetype;
                if (expectedChunkIndexInArchetype < 0 || expectedChunkIndexInArchetype >= archetype->Chunks.Count)
                {
                    // This indicates objectively invalid data in the cache's ChunkIndexInArchetype array.
                    // Be especially concerned about negative numbers; that would be a pretty clear sign of a memory stomp.
                    throw new ArgumentException(@$"query chunk cache inconsistency: chunk index in archetype {expectedChunkIndexInArchetype} for cached chunk {cacheChunkIndex} is out of range. Archetype {expectedArchetypeIndex} contains {archetype->Chunks.Count} chunks.
Archetype has {archetype->Chunks.Count} chunks and {archetype->EntityCount} entities, with types {archetype->ToString()}.
Query matches {archetypeCount} archetypes.");
                }
                if (archetype->Chunks[expectedChunkIndexInArchetype] != chunk)
                {
                    // This means there's a chunk in the cache that didn't come from the query's matching archetypes.
                    // That would either be due to an error in the cache update logic, or the matching archetypes changing
                    // without invalidating the cache.
                    throw new ArgumentException(@$"query chunk cache inconsistency: cached chunk {cacheChunkIndex} not found at expected chunk index {expectedChunkIndexInArchetype} of expected matching archetype {expectedArchetypeIndex}.
Archetype has {archetype->Chunks.Count} chunks and {archetype->EntityCount} entities, with types {archetype->ToString()}.
Query matches {archetypeCount} archetypes.");
                }
                if (archetype->Chunks[expectedChunkIndexInArchetype]->SequenceNumber != chunk->SequenceNumber)
                {
                    // Chunk* comparisons are not sufficient, because chunk allocations are pooled. The chunk may have
                    // been freed and reallocated into a new archetype.
                    // This would indicate that some operation is not invalidating the appropriate query caches.
                    throw new ArgumentException(@$"query chunk cache inconsistency: cached chunk {cacheChunkIndex} has a different sequence number {chunk->SequenceNumber} than chunk in matching archetype with sequence number {archetype->Chunks[expectedChunkIndexInArchetype]->SequenceNumber}.
Archetype has {archetype->Chunks.Count} chunks and {archetype->EntityCount} entities, with types {archetype->ToString()}.
Query matches {archetypeCount} archetypes.");
                }
            }

            // All chunks in cache are accounted for.
            if (chunkCounter != cache.Length)
            {
                // This should *never* happen; we've already checked twice that these counts match.
                // This would indicate that the chunk cache is being modified concurrently with this consistency check.
                throw new ArgumentException($"query chunk cache inconsistency: we checked {chunkCounter} chunks from matching archetypes, but chunk cache length is {cache.Length}.");
            }
        }
    }

    unsafe struct EntityQueryData : IDisposable
    {
        //@TODO: better name or remove entirely...
        public ComponentType*       RequiredComponents;
        public int                  RequiredComponentsCount;

        public TypeIndex*           ReaderTypes;
        public int                  ReaderTypesCount;

        public TypeIndex*           WriterTypes;
        public int                  WriterTypesCount;

        public TypeIndex*           EnableableComponentTypeIndices;
        public int                  EnableableComponentTypeIndexCount; // number of elements in EnableableComponentTypeIndices

        public ArchetypeQuery*      ArchetypeQueries;
        public int                  ArchetypeQueryCount;

        public EntityQueryMask      EntityQueryMask;

        public UnsafeMatchingArchetypePtrList MatchingArchetypes;
        private UnsafeCachedChunkList MatchingChunkCache; // Direct access to this field is not thread-safe; use the helper methods on EntityQueryData instead.

        public byte HasEnableableComponents; // 0 = no, 1 = yes

        internal void CreateMatchingChunkCache(EntityComponentStore* ecs)
        {
            MatchingChunkCache = new UnsafeCachedChunkList(ecs);
        }

        internal void RebuildMatchingChunkCache()
        {
            UnsafeCachedChunkList.Rebuild(ref MatchingChunkCache, this);
        }

        // This method should *only* be called by EntityQueryImpl.GetMatchingChunkCache(), which includes some critical
        // thread-safety and cache validity checks. Using this return value directly in any other context is not guaranteed
        // to return a valid, up-to-date cache!
        internal UnsafeCachedChunkList UnsafeGetMatchingChunkCache()
        {
            return MatchingChunkCache;
        }

        internal void CheckChunkListCacheConsistency(bool forceCheckInvalidCache)
        {
            UnsafeCachedChunkList.AssertIsConsistent(MatchingChunkCache, this, forceCheckInvalidCache);
        }

        internal void InvalidateChunkCache()
        {
            MatchingChunkCache.Invalidate();
        }

        internal bool IsChunkCacheValid()
        {
            return MatchingChunkCache.IsCacheValid;
        }

        public void Dispose()
        {
            MatchingArchetypes.Dispose();
            MatchingChunkCache.Dispose();
        }
    }
}
