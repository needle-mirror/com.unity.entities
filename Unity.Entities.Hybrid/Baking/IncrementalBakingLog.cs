using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities.Conversion;
using UnityEditor;
using UnityEngine;

namespace Unity.Entities.Baking
{
    [StructLayout(LayoutKind.Explicit)]
    struct BakerJournalingEntry
    {
        [FieldOffset(0)] public int recordTypeInt;
        [FieldOffset(4)] public int intValue;
#if UNITY_EDITOR
        [FieldOffset(4)] public GUID guidValue;
#endif
        [FieldOffset(4)] public ComponentBakeTrigger triggerValue;

        public BakeRecordType RecordType
        {
            get { return (BakeRecordType) recordTypeInt; }
            set { recordTypeInt = (int)value;  }
        }
    }

    struct ComponentBakeTrigger
    {
        public int AuthoringComponentId;
        public ComponentBakeReason BakeReason;
        public int ReasonId;
        public Hash128 ReasonGuid;
        public TypeIndex BakingUnityTypeIndex;
    }

    enum ComponentBakeReason
    {
        NewComponent,
        ComponentChanged,
        GetComponentChanged,
        GetComponentStructuralChange,
        GetComponentsStructuralChange,
        GetHierarchySingleStructuralChange,
        GetHierarchyStructuralChange,
        ObjectExistStructuralChange,
        ReferenceChanged,
        GameObjectPropertyChange,
        GameObjectStaticChange,
        ReferenceChangedOnDisk,
        ActiveChanged,
        UpdatePrefabInstance,
    }

    enum BakeRecordType
    {
        ChangedGameObjects,
        NewGameObjects,
        DestroyedGameObjects,

        NewComponents,
        ChangedComponents,
        DestroyedComponents,

        ChangedAssets,

#if UNITY_EDITOR
        ChangedAssetsOnDisk,
#endif

        ComponentBakeTriggers
    }

    internal static class IncrementalBakingLog
    {
        private static ulong lastJournalingRecordIndex;

        internal sealed class SharedEnabled { internal static readonly SharedStatic<bool> Ref = SharedStatic<bool>.GetOrCreate<SharedEnabled>(); }

        internal static ref bool Enabled => ref SharedEnabled.Ref.Data;

        public static void Begin()
        {
#if UNITY_EDITOR
            Enabled = LiveConversionSettings.IsLiveBakingLoggingEnabled;
#else
            Enabled = false;
#endif

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            lastJournalingRecordIndex = EntitiesJournaling.RecordIndex;
#endif
            if (!Enabled)
                return;
        }

        public static void End()
        {
            if (!Enabled)
                return;
        }

        public static void RecordGameObjectChanged(int instanceId)
        {
            if (!Enabled)
                return;
            AddToJournaling(BakeRecordType.ChangedGameObjects, instanceId);
        }

        public static void RecordGameObjectNew(int instanceId)
        {
            if (!Enabled)
                return;
            AddToJournaling(BakeRecordType.NewGameObjects, instanceId);
        }

        public static void RecordGameObjectDestroyed(int instanceId)
        {
            if (!Enabled)
                return;
            AddToJournaling(BakeRecordType.DestroyedGameObjects, instanceId);
        }

        public static void RecordComponentNew(int instanceId)
        {
            if (!Enabled)
                return;
            AddToJournaling(BakeRecordType.NewComponents, instanceId);
        }

        public static void RecordComponentChanged(int instanceId)
        {
            if (!Enabled)
                return;
            AddToJournaling(BakeRecordType.ChangedComponents, instanceId);
        }

        public static void RecordComponentDestroyed(int instanceId)
        {
            if (!Enabled)
                return;
            AddToJournaling(BakeRecordType.DestroyedComponents, instanceId);
        }

        public static void RecordAssetChanged(int instanceId)
        {
            if (!Enabled)
                return;
            AddToJournaling(BakeRecordType.ChangedAssets, instanceId);
        }

#if UNITY_EDITOR
        public static void RecordAssetChangedOnDisk(GUID guid)
        {
            if (!Enabled)
                return;
            AddToJournaling(BakeRecordType.ChangedAssetsOnDisk, guid);
        }
#endif

        public static void RecordComponentBake(int componentId, ComponentBakeReason reason, int reasonId, TypeIndex unityTypeIndex)
        {
            if (!Enabled)
                return;

            var entry = new BakerJournalingEntry();
            entry.RecordType = BakeRecordType.ComponentBakeTriggers;
            entry.triggerValue = new ComponentBakeTrigger
            {
                AuthoringComponentId = componentId,
                BakeReason = reason,
                ReasonId = reasonId,
                ReasonGuid = default,
                BakingUnityTypeIndex = unityTypeIndex
            };

            AddToJournaling(entry);
        }

#if UNITY_EDITOR
        public static void RecordComponentBake(int componentId, ComponentBakeReason reason, GUID reasonGuid, TypeIndex unityTypeIndex)
        {
            if (!Enabled)
                return;

            var entry = new BakerJournalingEntry();
            entry.RecordType = BakeRecordType.ComponentBakeTriggers;
            entry.triggerValue = new ComponentBakeTrigger
            {
                AuthoringComponentId = componentId,
                BakeReason = reason,
                ReasonId = 0,
                ReasonGuid = reasonGuid,
                BakingUnityTypeIndex = unityTypeIndex
            };
            AddToJournaling(entry);
        }
#endif

        private static void AddToJournaling(BakeRecordType recordType, int value)
        {
            var entry = new BakerJournalingEntry();
            entry.RecordType = recordType;
            entry.intValue = value;

            AddToJournaling(entry);
        }

#if UNITY_EDITOR
        private static void AddToJournaling(BakeRecordType recordType, GUID guid)
        {
            var entry = new BakerJournalingEntry();
            entry.RecordType = recordType;
            entry.guidValue = guid;

            AddToJournaling(entry);
        }
#endif

        private static void AddToJournaling(BakerJournalingEntry entry)
        {
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            unsafe
            {
                int entrySize = UnsafeUtility.SizeOf<BakerJournalingEntry>();
                EntitiesJournaling.AddRecord(
                    recordType: EntitiesJournaling.RecordType.BakingRecord,
                    worldSequenceNumber: default,
                    executingSystem: default,
                    entities: default,
                    entityCount: default,
                    data: UnsafeUtility.AddressOf(ref entry),
                    dataLength: entrySize);
            }
#endif
        }

        public struct JournalBakingInfo : IDisposable
        {
            public UnsafeParallelHashSet<int> ChangedGameObjects;
            public UnsafeParallelHashSet<int> NewGameObjects;
            public UnsafeParallelHashSet<int> DestroyedGameObjects;

            public UnsafeParallelHashSet<int> NewComponents;
            public UnsafeParallelHashSet<int> ChangedComponents;
            public UnsafeParallelHashSet<int> DestroyedComponents;

            public UnsafeParallelHashSet<int> ChangedAssets;

#if UNITY_EDITOR
            public UnsafeParallelHashSet<GUID> ChangedAssetsOnDisk;
#endif

            public UnsafeMultiHashMap<int, ComponentBakeTrigger> ComponentBakeTriggers;

            bool isCreated;

            public JournalBakingInfo(Allocator allocator)
            {
                ChangedGameObjects = new UnsafeParallelHashSet<int>(10, allocator);
                NewGameObjects = new UnsafeParallelHashSet<int>(10, allocator);
                DestroyedGameObjects = new UnsafeParallelHashSet<int>(10, allocator);

                NewComponents = new UnsafeParallelHashSet<int>(10, allocator);
                ChangedComponents = new UnsafeParallelHashSet<int>(10, allocator);
                DestroyedComponents = new UnsafeParallelHashSet<int>(10, allocator);

                ChangedAssets = new UnsafeParallelHashSet<int>(10, allocator);

#if UNITY_EDITOR
                ChangedAssetsOnDisk = new UnsafeParallelHashSet<GUID>(10, allocator);
#endif

                ComponentBakeTriggers = new UnsafeMultiHashMap<int, ComponentBakeTrigger>(1024, allocator);

                isCreated = true;
            }

            public void Dispose()
            {
                if (!isCreated)
                    return;

                ChangedGameObjects.Dispose();
                NewGameObjects.Dispose();
                DestroyedGameObjects.Dispose();

                NewComponents.Dispose();
                ChangedComponents.Dispose();
                DestroyedComponents.Dispose();

                ChangedAssets.Dispose();
#if UNITY_EDITOR
                ChangedAssetsOnDisk.Dispose();
#endif

                ComponentBakeTriggers.Dispose();

                isCreated = false;
            }

            public void Reset()
            {
                ChangedGameObjects.Clear();
                NewGameObjects.Clear();
                DestroyedGameObjects.Clear();

                NewComponents.Clear();
                ChangedComponents.Clear();
                DestroyedComponents.Clear();

                ChangedAssets.Clear();
#if UNITY_EDITOR
                ChangedAssetsOnDisk.Clear();
#endif

                ComponentBakeTriggers.Clear();
            }
        }

        private static JournalBakingInfo ReadRecords()
        {
            JournalBakingInfo info = new JournalBakingInfo(Allocator.Persistent);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            unsafe
            {
                // We want only baking records with an index that's greater than lastJournalingRecordIndex
                var records = EntitiesJournaling.GetRecords(EntitiesJournaling.Ordering.Ascending)
                    .WithRecordType(EntitiesJournaling.RecordType.BakingRecord)
                    .Where(r => r.Index > lastJournalingRecordIndex);

                foreach (var record in records)
                {
                    void* dataPtr = record.DataPtr;

                    // Validate data length to be sure we don't read beyond
                    if (record.DataLength != UnsafeUtility.SizeOf<BakerJournalingEntry>())
                        continue; // This is a silent error, we don't want to spam the console if it ever happens

                    var bakerEntry = UnsafeUtility.AsRef<BakerJournalingEntry>(dataPtr);
                    switch (bakerEntry.RecordType)
                    {
                        case BakeRecordType.ChangedGameObjects:
                            info.ChangedGameObjects.Add(bakerEntry.intValue);
                            break;
                        case BakeRecordType.NewGameObjects:
                            info.NewGameObjects.Add(bakerEntry.intValue);
                            break;
                        case BakeRecordType.DestroyedGameObjects:
                            info.DestroyedGameObjects.Add(bakerEntry.intValue);
                            break;
                        case BakeRecordType.NewComponents:
                            info.NewComponents.Add(bakerEntry.intValue);
                            break;
                        case BakeRecordType.ChangedComponents:
                            info.ChangedComponents.Add(bakerEntry.intValue);
                            break;
                        case BakeRecordType.DestroyedComponents:
                            info.DestroyedComponents.Add(bakerEntry.intValue);
                            break;
                        case BakeRecordType.ChangedAssets:
                            info.ChangedAssets.Add(bakerEntry.intValue);
                            break;
#if UNITY_EDITOR
                        case BakeRecordType.ChangedAssetsOnDisk:
                            info.ChangedAssetsOnDisk.Add(bakerEntry.guidValue);
                            break;
#endif
                        case BakeRecordType.ComponentBakeTriggers:
                            info.ComponentBakeTriggers.Add(bakerEntry.triggerValue.AuthoringComponentId, bakerEntry.triggerValue);
                            break;
                    }
                }
            }
#endif
            return info;
        }

        public static void WriteLog(ref GameObjectComponents gameObjectComponents)
        {
            if (!Enabled)
                return;

            var sb = new StringBuilder();

            using var bakerRecords = ReadRecords();

            sb.AppendLine($"Incremental Baking Log");
            sb.AppendLine($"----------------------");

            // Write all changed GameObjects
            // -----------------------------
            sb.AppendLine($"Changed GameObjects: {bakerRecords.ChangedGameObjects.Count()}");
            sb.AppendLine($"------------------------------");
            foreach (var gameObjectId in bakerRecords.ChangedGameObjects)
            {
                WriteGameObject(ref gameObjectComponents, sb, gameObjectId);
            }
            sb.AppendLine($"------------------------------\n");

            // Write all new GameObjects
            // -------------------------
            sb.AppendLine($"New GameObjects: {bakerRecords.NewGameObjects.Count()}");
            sb.AppendLine($"------------------------------");
            foreach (var gameObjectId in bakerRecords.NewGameObjects)
            {
                WriteGameObject(ref gameObjectComponents, sb, gameObjectId);
            }
            sb.AppendLine($"------------------------------\n");

            // Write all changed Components
            // -------------------------
            sb.AppendLine($"Changed Components: {bakerRecords.ChangedComponents.Count()}");
            sb.AppendLine($"------------------------------");
            foreach (var componentId in bakerRecords.ChangedComponents)
            {
                WriteComponent(sb, componentId);
            }
            sb.AppendLine($"------------------------------\n");

            // Write all new Components
            // -------------------------
            sb.AppendLine($"New Components: {bakerRecords.NewComponents.Count()}");
            sb.AppendLine($"------------------------------");
            foreach (var componentId in bakerRecords.NewComponents)
            {
                WriteComponent(sb, componentId);
            }
            sb.AppendLine($"------------------------------\n");

            // Write dependency triggers
            // -------------------------
            sb.AppendLine($"Bake Reasons");
            sb.AppendLine($"------------------------------");
            using var componentIds = bakerRecords.ComponentBakeTriggers.GetKeyArray(Allocator.Temp);
            foreach (var componentId in componentIds)
            {
                var component = (Component)Resources.InstanceIDToObject(componentId);
                if (component != null)
                {
                    sb.AppendLine($"Type: {component.GetType().Name}");
                    sb.AppendLine($"GameObject: {component.gameObject.name} ({component.gameObject.GetInstanceID()})");
                }
                sb.AppendLine($"InstanceID: {componentId}");
                sb.AppendLine("Why did I bake?:");
                foreach (var trigger in bakerRecords.ComponentBakeTriggers.GetValuesForKey(componentId))
                {
                    var typeInfo = TypeManager.GetTypeInfo(trigger.BakingUnityTypeIndex);
                    switch (trigger.BakeReason)
                    {
                        case ComponentBakeReason.NewComponent:
                        {
                            sb.AppendLine($"\tNew Component - {typeInfo.DebugTypeName} ({trigger.AuthoringComponentId}");
                            break;
                        }
                        case ComponentBakeReason.ComponentChanged:
                        {
                            sb.AppendLine($"\tComponent Changed - {typeInfo.DebugTypeName} ({trigger.AuthoringComponentId}");
                            break;
                        }
                        case ComponentBakeReason.GetComponentChanged:
                        {
                            sb.AppendLine($"\tGetComponent({typeInfo.DebugTypeName}) Changed ({trigger.ReasonId})");
                            break;
                        }
                        case ComponentBakeReason.GetComponentStructuralChange:
                        {
                            sb.AppendLine($"\tGetComponent({typeInfo.DebugTypeName}) Structural Change ({trigger.ReasonId})");
                            break;
                        }
                        case ComponentBakeReason.GetComponentsStructuralChange:
                        {
                            sb.AppendLine($"\tGetComponents({typeInfo.DebugTypeName}) Structural Change");
                            break;
                        }
                        case ComponentBakeReason.GetHierarchySingleStructuralChange:
                        {
                            sb.AppendLine($"\tHierarchy({typeInfo.DebugTypeName}) Structural Change ({trigger.ReasonId})");
                            break;
                        }
                        case ComponentBakeReason.GetHierarchyStructuralChange:
                        {
                            sb.AppendLine($"\tHierarchy({typeInfo.DebugTypeName}) Structural Change");
                            break;
                        }
                        case ComponentBakeReason.ObjectExistStructuralChange:
                        {
                            sb.AppendLine($"\tObjectExist({typeInfo.DebugTypeName}) Structural Change ({trigger.ReasonId})");
                            break;
                        }
                        case ComponentBakeReason.ReferenceChanged:
                        {
                            sb.AppendLine($"\tReference() Changed ({trigger.ReasonId})");
                            break;
                        }
                        case ComponentBakeReason.GameObjectPropertyChange:
                        {
                            sb.AppendLine($"\tGameObject Property() Changed ({trigger.ReasonId})");
                            break;
                        }

                        case ComponentBakeReason.GameObjectStaticChange:
                        {
                            sb.AppendLine($"\tGameObject IsStatic() Changed ({trigger.ReasonId})");
                            break;
                        }
#if UNITY_EDITOR
                        case ComponentBakeReason.ReferenceChangedOnDisk:
                        {
                            var path = AssetDatabase.GUIDToAssetPath(trigger.ReasonGuid);
                            sb.AppendLine($"\tReference({path}) Changed On Disk ({trigger.ReasonGuid})");
                            break;
                        }
#endif
                        case ComponentBakeReason.ActiveChanged:
                        {
                            var gameObject = (GameObject)Resources.InstanceIDToObject(trigger.ReasonId);
                            if (gameObject != null)
                            {
                                sb.AppendLine($"\tIsActive() Changed ({gameObject.name}, {trigger.ReasonId})");
                            }
                            else
                            {
                                sb.AppendLine($"\tIsActive() Changed ({trigger.ReasonId})");
                            }
                            break;
                        }
                        case ComponentBakeReason.UpdatePrefabInstance:
                        {
                            var gameObject = (GameObject)Resources.InstanceIDToObject(trigger.ReasonId);
                            if (gameObject != null)
                            {
                                sb.AppendLine($"\tUpdatePrefabInstance ({gameObject.name}, {trigger.ReasonId}) - Caused when a prefab asset is instance in a SubScene and the original asset is modified, thus updating the non-overridden properties in the scene instance.");
                            }
                            else
                            {
                                sb.AppendLine($"\tUpdatePrefabInstance ({trigger.ReasonId}) - Caused when a prefab asset is instance in a SubScene and the original asset is modified, thus updating the non-overridden properties in the scene instance.");
                            }
                            break;
                        }
                    }
                }

                sb.AppendLine();
            }
            sb.AppendLine($"------------------------------\n");

            Debug.Log(sb.ToString());
        }

        static void WriteComponent(StringBuilder sb, int componentId)
        {
            var component = (Component) Resources.InstanceIDToObject(componentId);
            sb.AppendLine($"Type: {component.GetType().Name}");
            sb.AppendLine($"InstanceID: {componentId}");
            sb.AppendLine($"GameObject: {component.gameObject.name} ({component.gameObject.GetInstanceID()})");
        }

        static void WriteGameObject(ref GameObjectComponents gameObjectComponents, StringBuilder sb, int gameObjectId)
        {
            var gameObject = (GameObject)Resources.InstanceIDToObject(gameObjectId);
            sb.AppendLine($"Name: {gameObject.name}");
            sb.AppendLine($"InstanceID: {gameObjectId}");
            sb.AppendLine($"Scene: {gameObject.scene.name}");

            sb.AppendLine($"Components: ");
            foreach (var componentData in gameObjectComponents.GetComponents(gameObjectId))
            {
                var typeInfo = TypeManager.GetTypeInfo(componentData.TypeIndex);
                sb.AppendLine($"\t{typeInfo.Type.Name} ({componentData.InstanceID})");
            }
        }

    }
}
