using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine.Assertions;

namespace Unity.Entities
{
    internal unsafe class EntityGroupManager : IDisposable
    {
        private readonly ComponentJobSafetyManager m_JobSafetyManager;
        private ChunkAllocator m_GroupDataChunkAllocator;
        private EntityGroupDataList m_EntityGroupDatas;
        private NativeMultiHashMap<int, int> m_EntityGroupDataCache;

        ref UnsafePtrList m_EntityGroupDatasUnsafePtrList
        {
            get { return ref *(UnsafePtrList*)UnsafeUtility.AddressOf(ref m_EntityGroupDatas); }
        }
        
        public EntityGroupManager(ComponentJobSafetyManager safetyManager)
        {
            m_JobSafetyManager = safetyManager;
            m_GroupDataChunkAllocator = new ChunkAllocator();
            m_EntityGroupDataCache = new NativeMultiHashMap<int, int>(1024, Allocator.Persistent); 
        }

        public void Dispose()
        {
            m_EntityGroupDataCache.Dispose();
            for(var g = m_EntityGroupDatas.Count - 1; g >= 0; --g)
                m_EntityGroupDatas.p[g]->Dispose();
            m_EntityGroupDatasUnsafePtrList.Dispose();
            //@TODO: Need to wait for all job handles to be completed..
            m_GroupDataChunkAllocator.Dispose();
        }

        ArchetypeQuery* CreateQuery(ref ScratchAllocator scratchAllocator, ComponentType* requiredTypes, int count)
        {
            var allList = new NativeList<ComponentType>(Allocator.Temp);
            var noneList = new NativeList<ComponentType>(Allocator.Temp);
            for (int i = 0; i != count; i++)
            {
                if (requiredTypes[i].AccessModeType == ComponentType.AccessMode.Subtractive)
                    noneList.Add(ComponentType.ReadOnly(requiredTypes[i].TypeIndex));
                else
                    allList.Add(requiredTypes[i]);
            }
            
            // NativeList.ToArray requires GC Pinning, not supported in Tiny
            var allCount = allList.Length;
            var noneCount = noneList.Length;
            var allTypes = new ComponentType[allCount];
            var noneTypes = new ComponentType[noneCount];
            for (int i = 0; i < allCount; i++)
                allTypes[i] = allList[i];
            for (int i = 0; i < noneCount; i++)
                noneTypes[i] = noneList[i];

            var query = new EntityArchetypeQuery
            {
                All = allTypes,
                None = noneTypes
            };
            
            allList.Dispose();
            noneList.Dispose();

            return CreateQuery(ref scratchAllocator, new EntityArchetypeQuery[] { query });
        }

        void ConstructTypeArray(ref ScratchAllocator scratchAllocator, ComponentType[] types, out int* outTypes, out int outLength)
        {
            if (types == null || types.Length == 0)
            {
                outTypes = null;
                outLength = 0;
            }
            else
            {
                outLength = types.Length;
                outTypes = (int*)scratchAllocator.Allocate<int>(types.Length);
                for (int i = 0; i != types.Length; i++)
                    outTypes[i] = types[i].TypeIndex;
            }
        }

        void IncludeDependentWriteGroups(ComponentType type, NativeList<ComponentType> anyList, NativeList<ComponentType> explicitList)
        {
            if (type.AccessModeType != ComponentType.AccessMode.ReadOnly)
                return;
            
            var writeGroupTypes = TypeManager.GetWriteGroupTypes(type.TypeIndex);
            for (int i = 0; i < writeGroupTypes.Length; i++)
            {
                var excludedComponentType = GetWriteGroupReadOnlyComponentType(writeGroupTypes, i);
                if (anyList.Contains(excludedComponentType))
                    continue;
                if (explicitList.Contains(excludedComponentType))
                    continue;
                
                anyList.Add(excludedComponentType);
                IncludeDependentWriteGroups(excludedComponentType, anyList, explicitList);
            }
        }

        private static ComponentType GetWriteGroupReadOnlyComponentType(NativeArray<int> writeGroupTypes, int i)
        {
            // Need to get "Clean" TypeIndex from Type. Since TypeInfo.TypeIndex is not actually the index of the
            // type. (It includes other flags.) What is stored in WriteGroups is the actual index of the type.
            var excludedType = TypeManager.GetTypeInfo(writeGroupTypes[i]);
            var excludedComponentType = ComponentType.ReadOnly(excludedType.TypeIndex);
            return excludedComponentType;
        }

        void ExcludeWriteGroups(ComponentType type, NativeList<ComponentType> noneList, NativeList<ComponentType> explicitList)
        {
            if (type.AccessModeType == ComponentType.AccessMode.ReadOnly)
                return;
            
            var writeGroupTypes = TypeManager.GetWriteGroupTypes(type.TypeIndex);
            for (int i = 0; i < writeGroupTypes.Length; i++)
            {
                var excludedComponentType = GetWriteGroupReadOnlyComponentType(writeGroupTypes, i);
                if (noneList.Contains(excludedComponentType))
                    continue;
                if (explicitList.Contains(excludedComponentType))
                    continue;
                
                noneList.Add(excludedComponentType);
            }
        }

        NativeList<ComponentType> CreateExplicitTypeList(ComponentType[] typesNone, ComponentType[] typesAll, ComponentType[] typesAny)
        {
            var explicitList = new NativeList<ComponentType>(Allocator.Temp);
            for (int i=0;i<typesAny.Length;i++)
                explicitList.Add(typesAny[i]);
            for (int i=0;i<typesAll.Length;i++)
                explicitList.Add(typesAll[i]);
            for (int i=0;i<typesNone.Length;i++)
                explicitList.Add(typesNone[i]);
            return explicitList;
        }

        ArchetypeQuery* CreateQuery(ref ScratchAllocator scratchAllocator, EntityArchetypeQuery[] query)
        {
            var outQuery = (ArchetypeQuery*)scratchAllocator.Allocate(sizeof(ArchetypeQuery) * query.Length, UnsafeUtility.AlignOf<ArchetypeQuery>());
            for (int q = 0; q != query.Length; q++)
            {
                var typesNone = query[q].None;
                var typesAll = query[q].All;
                var typesAny = query[q].Any;
                
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                // Check that query doesn't contain any SubtractiveComponent...
                {
                    for (int i=0;i<typesNone.Length;i++)
                        if (typesNone[i].AccessModeType == ComponentType.AccessMode.Subtractive)
                            throw new ArgumentException("EntityArchetypeQuery cannot contain Subtractive Component types");
                    for (int i=0;i<typesAll.Length;i++)
                        if (typesAll[i].AccessModeType == ComponentType.AccessMode.Subtractive)
                            throw new ArgumentException("EntityArchetypeQuery cannot contain Subtractive Component types");
                    for (int i=0;i<typesAny.Length;i++)
                        if (typesAny[i].AccessModeType == ComponentType.AccessMode.Subtractive)
                            throw new ArgumentException("EntityArchetypeQuery cannot contain Subtractive Component types");
                }
#endif        
                
                // None forced to read only
                {
                    for (int i=0;i<typesNone.Length;i++)
                        if (typesNone[i].AccessModeType != ComponentType.AccessMode.ReadOnly)
                            typesNone[i] = ComponentType.ReadOnly(typesNone[i].TypeIndex);
                }
                
                // Each ReadOnly<type> in any or all
                //   if has WriteGroup types,
                //   - Recursively add to any (if not explictly mentioned)
                {
                    var explicitList = CreateExplicitTypeList(typesNone, typesAll, typesAny);
                    var anyList = new NativeList<ComponentType>( typesAny.Length, Allocator.Temp);
                    for (int i=0;i<typesAny.Length;i++)
                        anyList.Add(typesAny[i]);
                    for (int i = 0; i < typesAny.Length; i++)
                        IncludeDependentWriteGroups(typesAny[i], anyList, explicitList);
                    for (int i = 0; i < typesAll.Length; i++)
                        IncludeDependentWriteGroups(typesAll[i], anyList, explicitList);
                    typesAny = new ComponentType[anyList.Length];
                    for (int i = 0; i < anyList.Length; i++)
                        typesAny[i] = anyList[i];
                    anyList.Dispose();
                    explicitList.Dispose();
                }

                // Each ReadWrite<type> in any or all
                //   if has WriteGroup types,
                //     Add to none (if not exist in any or all or none) 
                {
                    var explicitList = CreateExplicitTypeList(typesNone, typesAll, typesAny);
                    var noneList = new NativeList<ComponentType>( typesNone.Length, Allocator.Temp);
                    for (int i=0;i<typesNone.Length;i++)
                        noneList.Add(typesNone[i]);
                    for (int i = 0; i < typesAny.Length; i++)
                        ExcludeWriteGroups(typesAny[i], noneList, explicitList);
                    for (int i = 0; i < typesAll.Length; i++)
                        ExcludeWriteGroups(typesAll[i], noneList, explicitList);
                    typesNone = new ComponentType[noneList.Length];
                    for (int i = 0; i < noneList.Length; i++)
                        typesNone[i] = noneList[i];
                    noneList.Dispose();
                    explicitList.Dispose();
                }                
            
                ConstructTypeArray(ref scratchAllocator, typesNone, out outQuery[q].None, out outQuery[q].NoneCount);
                ConstructTypeArray(ref scratchAllocator, typesAll,  out outQuery[q].All,  out outQuery[q].AllCount);
                ConstructTypeArray(ref scratchAllocator, typesAny,  out outQuery[q].Any,  out outQuery[q].AnyCount);
            }

            return outQuery;
        }

        public static bool CompareQueryArray(ComponentType[] filter, int* typeArray, int typeArrayCount)
        {
            int filterLength = filter != null ? filter.Length : 0;
            if (typeArrayCount != filterLength)
                return false;

            for (var i = 0; i < filterLength; ++i)
            {
                if (typeArray[i] != filter[i].TypeIndex)
                    return false;
            }

            return true;
        }

        public static bool CompareQuery(EntityArchetypeQuery[] query, EntityGroupData* groupData)
        {
            if (groupData->RequiredComponents != null)
                return false;

            if (groupData->ArchetypeQueryCount != query.Length)
                return false;

            for (int i = 0; i != query.Length; i++)
            {
                if (!CompareQueryArray(query[i].All, groupData->ArchetypeQuery[i].All, groupData->ArchetypeQuery[i].AllCount))
                    return false;
                if (!CompareQueryArray(query[i].None, groupData->ArchetypeQuery[i].None, groupData->ArchetypeQuery[i].NoneCount))
                    return false;
                if (!CompareQueryArray(query[i].Any, groupData->ArchetypeQuery[i].Any, groupData->ArchetypeQuery[i].AnyCount))
                    return false;
            }

            return true;
        }

        public static bool CompareComponents(ComponentType* componentTypes, int componentTypesCount, EntityGroupData* groupData)
        {
            if (groupData->RequiredComponents == null)
                return false;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            for (var k = 0; k < componentTypesCount; ++k)
                if (componentTypes[k].TypeIndex == TypeManager.GetTypeIndex<Entity>())
                    throw new ArgumentException(
                        "ComponentGroup.CompareComponents may not include typeof(Entity), it is implicit");
#endif

            // ComponentGroups are constructed including the Entity ID
            if (componentTypesCount + 1 != groupData->RequiredComponentsCount)
                return false;

            for (var i = 0; i < componentTypesCount; ++i)
            {
                if (groupData->RequiredComponents[i + 1] != componentTypes[i])
                    return false;
            }

            return true;
        }

        struct ScratchAllocator
        {
            public void* m_pointer;
            public int m_lengthInBytes;
            public int m_capacityInBytes;
            public ScratchAllocator(void* pointer, int capacityInBytes)
            {
                m_pointer = pointer;
                m_lengthInBytes = 0;
                m_capacityInBytes = capacityInBytes;
            }
            public void* Allocate(int sizeInBytes, int alignmentInBytes)
            {
                if (sizeInBytes == 0)
                    return null;
                var alignmentMask = (ulong)(alignmentInBytes - 1);
                var end = (ulong) (IntPtr) m_pointer + (ulong) m_lengthInBytes;
                end = (end + alignmentMask) & ~alignmentMask;
                var lengthInBytes = (byte*) (IntPtr) end - (byte*)m_pointer;
                lengthInBytes += sizeInBytes;
                Assert.IsTrue(lengthInBytes <= m_capacityInBytes);
                m_lengthInBytes = (int)lengthInBytes;
                return (void*)(IntPtr)end;
            }
            public void* Allocate<T>(int count = 1) where T : struct
            {
                return Allocate(UnsafeUtility.SizeOf<T>() * count, UnsafeUtility.AlignOf<T>());
            }
        }

        public ComponentGroup CreateEntityGroup(ArchetypeManager typeMan, EntityDataManager* entityDataManager, EntityArchetypeQuery[] query)
        {
            //@TODO: Support for CreateEntityGroup with query but using ComponentDataArray etc
            var buffer = stackalloc byte[1024];
            var scratchAllocator = new ScratchAllocator(buffer, 1024);
            var archetypeQuery = CreateQuery(ref scratchAllocator, query);
            return CreateEntityGroup(typeMan, entityDataManager, archetypeQuery, query.Length, null, 0);
        }

        public ComponentGroup CreateEntityGroup(ArchetypeManager typeMan, EntityDataManager* entityDataManager, ComponentType* inRequiredComponents, int inRequiredComponentsCount)
        {
            var buffer = stackalloc byte[1024];
            var scratchAllocator = new ScratchAllocator(buffer, 1024);
            var archetypeQuery = CreateQuery(ref scratchAllocator, inRequiredComponents, inRequiredComponentsCount);
            var outRequiredComponents = (ComponentType*)scratchAllocator.Allocate<ComponentType>(inRequiredComponentsCount + 1);
            outRequiredComponents[0] = ComponentType.Create<Entity>();
            for (int i = 0; i != inRequiredComponentsCount; i++)
                outRequiredComponents[i + 1] = inRequiredComponents[i];
            var outRequiredComponentsCount = inRequiredComponentsCount + 1;
            return CreateEntityGroup(typeMan, entityDataManager, archetypeQuery, 1, outRequiredComponents, outRequiredComponentsCount);
        }

        bool Matches(EntityGroupData* grp, ArchetypeQuery* archetypeQueries, int archetypeFiltersCount,
            ComponentType* requiredComponents, int requiredComponentsCount)
        {
            if (requiredComponentsCount != grp->RequiredComponentsCount)
                return false;
            if(archetypeFiltersCount != grp->ArchetypeQueryCount)
                return false;
            if (requiredComponentsCount > 0 && UnsafeUtility.MemCmp(requiredComponents, grp->RequiredComponents,sizeof(ComponentType) * requiredComponentsCount) != 0)
                return false;
            for(var i = 0; i < archetypeFiltersCount; ++i)
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
            if(source != null)
                UnsafeUtility.MemCpy(pointer, source, bytes);
            return pointer;
        }
        
        public ComponentGroup CreateEntityGroup(ArchetypeManager typeMan, EntityDataManager* entityDataManager,
            ArchetypeQuery* query, int queryCount, ComponentType* component, int componentCount)
        {           
            //@TODO: Validate that required types is subset of archetype filters all...

            int hash = (int)math.hash(component, componentCount * sizeof(ComponentType));
            for (var i = 0; i < queryCount; ++i)
                hash = hash * 397 ^ query[i].GetHashCode();
            EntityGroupData* cachedGroup = null;
            if(m_EntityGroupDataCache.TryGetFirstValue(hash, out var entityGroupDataIndex, out var iterator))
            {
                do
                {
                    var possibleMatch = m_EntityGroupDatas.p[entityGroupDataIndex];
                    if(Matches(possibleMatch, query, queryCount, component, componentCount))
                    {
                        cachedGroup = possibleMatch;
                        break;
                    }
                } while (m_EntityGroupDataCache.TryGetNextValue(out entityGroupDataIndex, ref iterator));
            }

            if (cachedGroup == null)
            {
                cachedGroup = (EntityGroupData*) ChunkAllocate<EntityGroupData>();
                cachedGroup->RequiredComponentsCount = componentCount;
                cachedGroup->RequiredComponents = (ComponentType*) ChunkAllocate<ComponentType>(componentCount, component);
                InitializeReaderWriter(cachedGroup, component, componentCount);
                cachedGroup->ArchetypeQueryCount = queryCount;
                cachedGroup->ArchetypeQuery = (ArchetypeQuery*) ChunkAllocate<ArchetypeQuery>(queryCount, query);
                for (var i = 0; i < queryCount; ++i)
                {
                    cachedGroup->ArchetypeQuery[i].All = (int*)ChunkAllocate<int>(cachedGroup->ArchetypeQuery[i].AllCount,query[i].All);
                    cachedGroup->ArchetypeQuery[i].Any = (int*)ChunkAllocate<int>(cachedGroup->ArchetypeQuery[i].AnyCount,query[i].Any);
                    cachedGroup->ArchetypeQuery[i].None = (int*)ChunkAllocate<int>(cachedGroup->ArchetypeQuery[i].NoneCount,query[i].None);
                }
                cachedGroup->MatchingArchetypes = new MatchingArchetypeList();
                for (var i = typeMan.m_Archetypes.Count - 1; i >= 0; --i)
                {
                    var archetype = typeMan.m_Archetypes.p[i];
                    AddArchetypeIfMatching(archetype, cachedGroup);
                }

               m_EntityGroupDataCache.Add(hash, m_EntityGroupDatas.Count);
               m_EntityGroupDatasUnsafePtrList.Add(cachedGroup);
            }

            return new ComponentGroup(cachedGroup, m_JobSafetyManager, typeMan, entityDataManager);
        }

        void InitializeReaderWriter(EntityGroupData* grp, ComponentType* requiredTypes, int requiredCount)
        {
            grp->ReaderTypesCount = 0;
            grp->WriterTypesCount = 0;

            for (var i = 0; i != requiredCount; i++)
            {
                //@TODO: Investigate why Entity is not early out on this one...
                if (!requiredTypes[i].RequiresJobDependency)
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

            grp->ReaderTypes = (int*) m_GroupDataChunkAllocator.Allocate(sizeof(int) * grp->ReaderTypesCount, 4);
            grp->WriterTypes = (int*) m_GroupDataChunkAllocator.Allocate(sizeof(int) * grp->WriterTypesCount, 4);

            var curReader = 0;
            var curWriter = 0;
            for (var i = 0; i != requiredCount; i++)
            {
                if (!requiredTypes[i].RequiresJobDependency)
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

        public void AddArchetypeIfMatching(Archetype* type)
        {
            for (var g = m_EntityGroupDatas.Count - 1; g >= 0; --g)
            {
                var grp = m_EntityGroupDatas.p[g];
                AddArchetypeIfMatching(type, grp);
            }
        }

        void AddArchetypeIfMatching(Archetype* archetype, EntityGroupData* group)
        {
            if (!IsMatchingArchetype(archetype, group))
                return;

            var match = (MatchingArchetype*) m_GroupDataChunkAllocator.Allocate(
                MatchingArchetype.GetAllocationSize(group->RequiredComponentsCount), 8);
            match->Archetype = archetype;
            var typeIndexInArchetypeArray = match->IndexInArchetype;

            group->MatchingArchetypesUnsafePtrList.Add(match);

            for (var component = 0; component < group->RequiredComponentsCount; ++component)
            {
                var typeComponentIndex = -1;
                if (group->RequiredComponents[component].AccessModeType != ComponentType.AccessMode.Subtractive)
                {
                    typeComponentIndex = ChunkDataUtility.GetIndexInTypeArray(archetype, group->RequiredComponents[component].TypeIndex);
                    Assert.AreNotEqual(-1, typeComponentIndex);
                }

                typeIndexInArchetypeArray[component] = typeComponentIndex;
            }
        }


        //@TODO: All this could be much faster by having all ComponentType pre-sorted to perform a single search loop instead two nested for loops...
        static bool IsMatchingArchetype(Archetype* archetype, EntityGroupData* group)
        {
            for (int i = 0; i != group->ArchetypeQueryCount; i++)
            {
                if (IsMatchingArchetype(archetype, group->ArchetypeQuery + i))
                    return true;
            }

            return false;
        }

        static bool IsMatchingArchetype(Archetype* archetype, ArchetypeQuery* query)
        {
            if (!TestMatchingArchetypeAll(archetype, query->All, query->AllCount))
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
                    if (componentTypeIndex == noneTypeIndex) return false;
                }
            }

            return true;
        }

        static bool TestMatchingArchetypeAll(Archetype* archetype, int* allTypes, int allCount)
        {
            var componentTypes = archetype->Types;
            var componentTypesCount = archetype->TypesCount;
            var foundCount = 0;
            var disabledTypeIndex = TypeManager.GetTypeIndex<Disabled>();
            var prefabTypeIndex = TypeManager.GetTypeIndex<Prefab>();
            var chunkHeaderTypeIndex = TypeManager.GetTypeIndex<ChunkHeader>();
            var requestedDisabled = false;
            var requestedPrefab = false;
            var requestedChunkHeader = false;

            for (var i = 0; i < componentTypesCount; i++)
            {
                var componentTypeIndex = componentTypes[i].TypeIndex;
                for (var j = 0; j < allCount; j++)
                {
                    var allTypeIndex = allTypes[j];
                    if (allTypeIndex == disabledTypeIndex)
                        requestedDisabled = true;
                    if (allTypeIndex == prefabTypeIndex)
                        requestedPrefab = true;
                    if (allTypeIndex == chunkHeaderTypeIndex)
                        requestedChunkHeader = true;
                    if (componentTypeIndex == allTypeIndex) foundCount++;
                }
            }

            if (archetype->Disabled && (!requestedDisabled))
                return false;
            if (archetype->Prefab && (!requestedPrefab))
                return false;
            if (archetype->HasChunkHeader && (!requestedChunkHeader))
                return false;

            return foundCount == allCount;
        }
    }


    [StructLayout(LayoutKind.Sequential)]
    unsafe struct MatchingArchetype
    {
        public Archetype* Archetype;

        public fixed int IndexInArchetype[1];

        public static int GetAllocationSize(int requiredComponentsCount)
        {
            return sizeof(MatchingArchetype) + sizeof(int) * (requiredComponentsCount - 1);
        }
    }

    [DebuggerTypeProxy(typeof(MatchingArchetypeListDebugView))]
    unsafe struct MatchingArchetypeList
    {
        [NativeDisableUnsafePtrRestriction] public MatchingArchetype** p;
        public int Count;
        public int Capacity;
    }
    
    unsafe struct ArchetypeQuery : IEquatable<ArchetypeQuery>
    {
        public int*     Any;
        public int      AnyCount;

        public int*     All;
        public int      AllCount;

        public int*     None;
        public int      NoneCount;

        public bool Equals(ArchetypeQuery other)
        {
            if (AnyCount != other.AnyCount)
                return false;
            if (AllCount != other.AllCount)
                return false;
            if (NoneCount != other.NoneCount)
                return false;
            if (AnyCount > 0 && UnsafeUtility.MemCmp(Any, other.Any, sizeof(int) * AnyCount) != 0)
                return false;
            if (AllCount > 0 && UnsafeUtility.MemCmp(All, other.All, sizeof(int) * AllCount) != 0)
                return false;
            if (NoneCount > 0 && UnsafeUtility.MemCmp(None, other.None, sizeof(int) * NoneCount) != 0)
                return false;
            return true;
        }        
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode =                  (AnyCount + 1);
                    hashCode = 397 * hashCode ^ (AllCount + 1);
                    hashCode = 397 * hashCode ^ (NoneCount+ 1);
                    hashCode = (int)math.hash(Any, sizeof(int) * AnyCount, (uint)hashCode);
                    hashCode = (int)math.hash(All, sizeof(int) * AllCount, (uint)hashCode);
                    hashCode = (int)math.hash(None, sizeof(int) * NoneCount, (uint)hashCode);
                return hashCode;
            }
        }
    }

    [DebuggerTypeProxy(typeof(EntityGroupDataListDebugView))]
    unsafe struct EntityGroupDataList
    {
        [NativeDisableUnsafePtrRestriction] public EntityGroupData** p;
        public int Count;
        public int Capacity;
    }
    
    unsafe struct EntityGroupData : IDisposable
    {
        //@TODO: better name or remove entirely...
        public ComponentType*       RequiredComponents;
        public int                  RequiredComponentsCount;

        public int*                 ReaderTypes;
        public int                  ReaderTypesCount;

        public int*                 WriterTypes;
        public int                  WriterTypesCount;

        public ArchetypeQuery*      ArchetypeQuery;
        public int                  ArchetypeQueryCount;

        public MatchingArchetypeList MatchingArchetypes;

        public ref UnsafePtrList MatchingArchetypesUnsafePtrList
        {
            get { return ref *(UnsafePtrList*)UnsafeUtility.AddressOf(ref MatchingArchetypes); }
        }
                
        public void Dispose()
        {
            MatchingArchetypesUnsafePtrList.Dispose();
        }
    }
}
