using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityLogType = UnityEngine.LogType;
using UnityObject = UnityEngine.Object;
using GameObject = UnityEngine.GameObject;

namespace Unity.Entities.Conversion
{
    interface IConversionEventData {}

    struct LogEventData : IConversionEventData
    {
        public UnityLogType Type;
        public string Message;
        public string Stacktrace;
    }

    /// <summary>
    /// Exposes the entities that belong to a GameObject with a given instance ID.
    ///
    /// ATTENTION: Future public API.
    /// </summary>
    internal struct ConvertedEntitiesAccessor
    {
        NativeHashMap<int, int> m_HeadIdIndices; // object instanceId -> front index
        MultiList<Entity, MultiListNativeArrayData<Entity>> m_Entities;

        internal ConvertedEntitiesAccessor(NativeHashMap<int, int> headIndices,
            MultiList<Entity, MultiListNativeArrayData<Entity>> entities)
        {
            m_HeadIdIndices = headIndices;
            m_Entities = entities;
        }


        /// <summary>
        /// Returns an enumerator for the entities associated with a given instance ID. The first entity returned by
        /// the enumerator is the primary entity associated with the instance ID.
        /// </summary>
        /// <param name="instanceId">The instance ID of the GameObject that you want to get the entities of.</param>
        /// <returns>An enumerator for the entities associated with the instance ID.</returns>
        public MultiListEnumerator<Entity> GetEntities(int instanceId)
        {
            if (!m_HeadIdIndices.TryGetValue(instanceId, out var headIdIndex))
                return MultiListEnumerator<Entity>.Empty;
            return new MultiListEnumerator<Entity>(m_Entities.SelectList(headIdIndex));
        }
    }

    partial struct ConversionJournalData : IDisposable
    {
        NativeHashMap<int, int> m_HeadIdIndices; // object instanceId -> front index
        NativeList<int> m_FreeHeadIds;
        private int m_HeadIdCount;


        // Only for UnityEngine component types to be stored in a companion GameObject
        // maps GameObject to MultiList m_HybridTypes
        // for 2020.2, this could be based on instance IDs instead
        Dictionary<GameObject, int> m_NewHybridHeadIdIndices;
        internal Dictionary<GameObject, int> NewHybridHeadIdIndices => m_NewHybridHeadIdIndices;


        public void Dispose()
        {
            m_HeadIdIndices.Dispose();
            m_FreeHeadIds.Dispose();
            m_Entities.Dispose();
            m_LogEvents.Dispose();
            m_HybridTypes.Dispose();
        }

        // ** keep this block in sync ** (begin)

        MultiList<Entity, MultiListNativeArrayData<Entity>> m_Entities;
        MultiList<LogEventData, MultiListArrayData<LogEventData>> m_LogEvents;
        MultiList<Type, MultiListArrayData<Type>> m_HybridTypes;

        public void Init()
        {
            m_HeadIdIndices = new NativeHashMap<int, int>(1000, Allocator.Persistent);
            m_FreeHeadIds = new NativeList<int>(Allocator.Persistent);
            m_NewHybridHeadIdIndices = new Dictionary<GameObject, int>();

            m_Entities.Init();
            m_LogEvents.Init();
            m_HybridTypes.Init();
        }

        internal ConvertedEntitiesAccessor GetConvertedEntitiesAccessor()
        {
            return new ConvertedEntitiesAccessor(m_HeadIdIndices, m_Entities);
        }

        public void RemoveForIncremental(int instanceId, GameObject go)
        {
            if (m_HeadIdIndices.TryGetValue(instanceId, out var headIdIndex))
            {
                m_Entities.ReleaseListKeepHead(headIdIndex);
                m_LogEvents.ReleaseList(headIdIndex);
            }

            if (go != null && m_NewHybridHeadIdIndices.TryGetValue(go, out headIdIndex))
            {
                m_HybridTypes.ReleaseList(headIdIndex);
            }
        }

        // for debug/test
        IEnumerable<IJournalDataDebug> SelectJournalDataDebug(int objectInstanceId, int headIdIndex)
        {
            foreach (var e in SelectJournalDataDebug(objectInstanceId, headIdIndex, ref m_Entities)) yield return e;
            foreach (var e in SelectJournalDataDebug(objectInstanceId, headIdIndex, ref m_LogEvents)) yield return e;
        }

        // ** keep this block in sync ** (end)

        int GetOrAddHeadIdIndex(int objectInstanceId)
        {
            if (!m_HeadIdIndices.TryGetValue(objectInstanceId, out var headIdIndex))
            {
                if (!m_FreeHeadIds.IsEmpty)
                {
                    int end = m_FreeHeadIds.Length - 1;
                    headIdIndex = m_FreeHeadIds[end];
                    m_FreeHeadIds.Length -= 1;
                }
                else
                    headIdIndex = m_HeadIdCount++;;
                m_HeadIdIndices.Add(objectInstanceId, headIdIndex);

                var headIdsCapacity = headIdIndex + 1;
                if (MultiList.CalcExpandCapacity(m_Entities.HeadIds.Length, ref headIdsCapacity))
                {
                    m_Entities.SetHeadIdsCapacity(headIdsCapacity);
                    m_LogEvents.SetHeadIdsCapacity(headIdsCapacity);
                }
            }

            return headIdIndex;
        }

        int GetOrAddHybridHeadIdIndex(GameObject gameObject)
        {
            if (!m_NewHybridHeadIdIndices.TryGetValue(gameObject, out var headIdIndex))
            {
                headIdIndex = m_NewHybridHeadIdIndices.Count;
                m_NewHybridHeadIdIndices.Add(gameObject, headIdIndex);

                var headIdsCapacity = headIdIndex + 1;
                if (MultiList.CalcExpandCapacity(m_HybridTypes.HeadIds.Length, ref headIdsCapacity))
                {
                    m_HybridTypes.SetHeadIdsCapacity(headIdsCapacity);
                }
            }

            return headIdIndex;
        }

        // creates new head, returns false if already had one
        void AddHead<T, I>(int objectInstanceId, ref MultiList<T, I> store, in T data) where I : IMultiListDataImpl<T> =>
            store.AddHead(GetOrAddHeadIdIndex(objectInstanceId), data);

        // creates new head or adds a new entry
        void Add<T, I>(int objectInstanceId, ref MultiList<T, I> store, in T data) where I : IMultiListDataImpl<T> =>
            store.Add(GetOrAddHeadIdIndex(objectInstanceId), data);

        // requires existing sublist, walks to end and adds, returns count (can be slow with large count)
        (int id, int serial) AddTail<T, I>(int objectInstanceId, ref MultiList<T, I> store) where I : IMultiListDataImpl<T> =>
            m_HeadIdIndices.TryGetValue(objectInstanceId, out var headIdIndex)
            ? store.AddTail(headIdIndex) : (-1, 0);

        unsafe int AddTail<T, I>(int objectInstanceId, ref MultiList<T, I> store, int* outIds, int count) where I : IMultiListDataImpl<T>
        {
            if (!m_HeadIdIndices.TryGetValue(objectInstanceId, out var headIdIndex))
                return 0;
            return store.AddTail(headIdIndex, outIds, count);
        }

        int GetHeadId<T, I>(int objectInstanceId, ref MultiList<T, I> store) where I : IMultiListDataImpl<T>
        {
            if (!m_HeadIdIndices.TryGetValue(objectInstanceId, out var headIdIndex))
                return -1;

            return store.HeadIds[headIdIndex];
        }

        bool HasHead<T, I>(int objectInstanceId, ref MultiList<T, I> store) where I : IMultiListDataImpl<T> =>
            GetHeadId(objectInstanceId, ref store) >= 0;

        bool GetHeadData<T, I>(int objectInstanceId, ref MultiList<T, I> store, ref T data) where I : IMultiListDataImpl<T>
        {
            var headId = GetHeadId(objectInstanceId, ref store);
            if (headId < 0)
                return false;

            data = store.Data.Get(headId);
            return true;
        }

        public void RecordPrimaryEntity(int objectInstanceId, Entity entity) =>
            AddHead(objectInstanceId, ref m_Entities, entity);

        public bool HasPrimaryEntity(int objectInstanceId) =>
            HasHead(objectInstanceId, ref m_Entities);

        public bool TryGetPrimaryEntity(int objectInstanceId, out Entity entity)
        {
            entity = Entity.Null;
            return GetHeadData(objectInstanceId, ref m_Entities, ref entity);
        }

        public Entity RemovePrimaryEntity(int objectInstanceId)
        {
            int head = GetHeadId(objectInstanceId, ref m_Entities);
            if (head == -1)
                return Entity.Null;
            var entity = m_Entities.Data.Data[head];
            m_FreeHeadIds.Add(head);
            m_HeadIdIndices.Remove(objectInstanceId);
            m_Entities.ReleaseList(head);
            return entity;
        }

        public (int id, int serial) ReserveAdditionalEntity(int objectInstanceId) =>
            AddTail(objectInstanceId, ref m_Entities);

        public unsafe int ReserveAdditionalEntities(int objectInstanceId, int* outIds, int count) =>
            AddTail(objectInstanceId, ref m_Entities, outIds, count);

        public void RecordAdditionalEntityAt(int atId, Entity entity) =>
            m_Entities.Data.Data[atId] = entity;

        // returns false if the object is unknown to the conversion system
        public bool GetEntities(int objectInstanceId, out MultiListEnumerator<Entity> iter)
        {
            var headId = GetHeadId(objectInstanceId, ref m_Entities);
            iter = new MultiListEnumerator<Entity>(m_Entities.SelectListAt(headId));
            return headId >= 0;
        }

        bool RecordEvent(UnityObject context, ref MultiList<LogEventData, MultiListArrayData<LogEventData>> eventStore, in LogEventData eventData)
        {
            var instanceId = 0;
            if (context != null)
            {
                // ignore if conversion system does not know about this context
                instanceId = context.GetInstanceID();
                if (!HasHead(instanceId, ref m_Entities))
                    return false;
            }

            Add(instanceId, ref eventStore, eventData);
            return true;
        }

        public bool RecordLogEvent(UnityObject context, UnityLogType logType, string message, string stacktrace = default) =>
            RecordEvent(context, ref m_LogEvents, new LogEventData { Type = logType, Message = message, Stacktrace = stacktrace });

        public bool RecordExceptionEvent(UnityObject context, Exception exception) =>
            RecordLogEvent(context, UnityLogType.Exception, $"{exception.GetType().Name}: {exception.Message}", exception.StackTrace);

        MultiListEnumerator<T, I> SelectJournalData<T, I>(UnityObject context, ref MultiList<T, I> store) where I : IMultiListDataImpl<T>
        {
            var iter = store.SelectListAt(GetHeadId(context.GetInstanceID(), ref store));
            if (!iter.IsValid)
                context.CheckObjectIsNotComponent();

            return iter;
        }

        IEnumerable<(int objectInstanceId, T eventData)> SelectJournalData<T>(MultiList<T, MultiListArrayData<T>> store)
        {
            //@TODO: make custom enumerator for this

            using (var headIdIndices = m_HeadIdIndices.GetKeyValueArrays(Allocator.Temp))
            {
                for (var i = 0; i < headIdIndices.Keys.Length; ++i)
                {
                    var objectInstanceId = headIdIndices.Keys[i];
                    if (store.TrySelectList(headIdIndices.Values[i], out var iter))
                    {
                        foreach (var e in iter)
                            yield return (objectInstanceId, e);
                    }
                }
            }
        }

        public MultiListEnumerator<Entity> SelectEntities(UnityObject context) =>
            new MultiListEnumerator<Entity>(SelectJournalData(context, ref m_Entities));

        public MultiListEnumerator<LogEventData, MultiListArrayData<LogEventData>> SelectLogEventsFast(UnityObject context) =>
            SelectJournalData(context, ref m_LogEvents);

        public LogEventData[] SelectLogEventsOrdered(UnityObject context)
        {
            using (var iter = SelectLogEventsFast(context))
            {
                var count = iter.Count();
                if (count == 0)
                    return Array.Empty<LogEventData>();

                var events = new LogEventData[count];

                iter.Reset();
                iter.MoveNext();

                // head
                events[0] = iter.Current;

                // rest of list in reverse order
                while (iter.MoveNext())
                    events[--count] = iter.Current;

                return events;
            }
        }

        public void AddHybridComponent(GameObject gameObject, Type type)
        {
            int index = GetOrAddHybridHeadIdIndex(gameObject);
            m_HybridTypes.Add(index, type);
        }

        public MultiListEnumerator<Type, MultiListArrayData<Type>> HybridTypes(int headIdIndex) =>
            m_HybridTypes.SelectList(headIdIndex);

        internal void ClearNewHybridComponents()
        {
            m_NewHybridHeadIdIndices.Clear();
        }

        public IEnumerable<(int objectInstanceId, LogEventData eventData)> SelectLogEventsFast() =>
            SelectJournalData(m_LogEvents);

        public IEnumerable<(int objectInstanceId, LogEventData)> SelectLogEventsOrdered()
        {
            using (var headIdIndices = m_HeadIdIndices.GetKeyValueArrays(Allocator.Temp))
            {
                var events = Array.Empty<LogEventData>();

                for (var i = 0; i < headIdIndices.Keys.Length; ++i)
                {
                    var objectInstanceId = headIdIndices.Keys[i];
                    if (m_LogEvents.TrySelectList(headIdIndices.Values[i], out var iter))
                    {
                        using (iter)
                        {
                            var count = iter.Count();
                            if (count == 0)
                                continue;

                            var newCount = count;
                            if (MultiList.CalcExpandCapacity(events.Length, ref newCount))
                                events = new LogEventData[newCount];

                            iter.Reset();
                            iter.MoveNext();

                            // head
                            events[0] = iter.Current;

                            // rest of list in reverse order
                            for (var e = count - 1; iter.MoveNext(); --e)
                                events[e] = iter.Current;

                            // yield in forward order
                            for (var e = 0; e < count; ++e)
                                yield return (objectInstanceId, events[e]);
                        }
                    }
                }
            }
        }
    }
}
