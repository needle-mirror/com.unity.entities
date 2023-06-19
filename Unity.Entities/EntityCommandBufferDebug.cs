using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{

    [DebuggerTypeProxy(typeof(EntityCommandBufferDebugView))]
    public unsafe partial struct EntityCommandBuffer
    {
        internal const int INITIAL_COMMANDS_CAPACITY = 8;

        private struct ECBPlaybackWithTrace
        {
            public static readonly SharedStatic<int> Ref = SharedStatic<int>.GetOrCreate<EntityCommandBuffer, ECBPlaybackWithTrace>();
        }

        static readonly SharedStatic<int> _PLAYBACK_WITH_TRACE = ECBPlaybackWithTrace.Ref;

        /// <summary>
        /// A static field for logging details during EntityCommandBuffer playback.
        /// When set to true, each EntityCommandBuffer will log its commands as they are processed during playback.
        /// </summary>
        public static bool PLAYBACK_WITH_TRACE
        {
            get => _PLAYBACK_WITH_TRACE.Data != 0;
            set => _PLAYBACK_WITH_TRACE.Data = value ? 1 : 0;
        }

        private struct ECBPrePlaybackValidation
        {
            public static readonly SharedStatic<int> Ref = SharedStatic<int>.GetOrCreate<EntityCommandBuffer, ECBPrePlaybackValidation>();
        }

        static readonly SharedStatic<int> _ENABLE_PRE_PLAYBACK_VALIDATION = ECBPrePlaybackValidation.Ref;

        internal static bool ENABLE_PRE_PLAYBACK_VALIDATION
        {
            get => _ENABLE_PRE_PLAYBACK_VALIDATION.Data != 0;
            set => _ENABLE_PRE_PLAYBACK_VALIDATION.Data = value ? 1 : 0;
        }

        internal struct PlaybackWithTraceProcessor : IEcbProcessor
        {
            private const string kMsgUnknownSystem = "unknown system type";

            public PlaybackProcessor playbackProcessor;
            public EntityDataAccess* mgr;
            public SystemHandle originSystem;
            public FixedString128Bytes originSystemDebugName;
            public FixedString128Bytes currentSystemDebugName;

            public void Init(EntityDataAccess* entityDataAccess, EntityCommandBufferData* data, in SystemHandle originSystemHandle)
            {
                playbackProcessor.Init(entityDataAccess, data, originSystemHandle);

                mgr = entityDataAccess;
                originSystem = originSystemHandle;
                var systemState = mgr->m_WorldUnmanaged.ResolveSystemState(originSystem);
                ExtractDebugName(systemState, out originSystemDebugName);
                ExtractDebugName(mgr->m_WorldUnmanaged.ResolveSystemState(mgr->m_WorldUnmanaged.ExecutingSystem), out currentSystemDebugName);

                Debug.Log($"Starting EntityCommandBuffer playback in {currentSystemDebugName}; recorded from {originSystemDebugName}.");
            }

            public void Cleanup()
            {
                playbackProcessor.Cleanup();
                Debug.Log($"Ending EntityCommandBuffer playback in {currentSystemDebugName}; recorded from {originSystemDebugName}.");
            }

            internal static void ExtractDebugName(SystemState* systemState, out FixedString128Bytes fixedString)
            {
                if (systemState != null)
                    fixedString = new FixedString128Bytes(systemState->DebugName);
                else
                    fixedString = kMsgUnknownSystem;
            }

            public void LogEntitiesAndComponentsCommand(Entity* entities, int count, in ComponentTypeSet typeSet,
                in FixedString64Bytes commandAction)
            {
                for (var i = 0; i < count; i++)
                {
                    LogEntityAndComponentsCommand(entities[i], typeSet, commandAction);
                }
            }

            public void LogEntitiesAndComponentCommand(Entity* entities, int count, TypeIndex typeIndex,
                in FixedString64Bytes commandAction)
            {
                for (var i = 0; i < count; i++)
                {
                    LogEntityAndComponentCommand(entities[i], typeIndex, commandAction);
                }
            }

            public void LogEntityAndComponentsCommand(Entity entity, ComponentTypeInArchetype* types, int count,
                in FixedString64Bytes commandAction)
            {
                for (var i = 0; i < count; i++)
                {
                    LogEntityAndComponentCommand(entity, types[i].TypeIndex, commandAction);
                }
            }

            public void LogEntityAndComponentsCommand(Entity entity, in ComponentTypeSet typeSet,
                in FixedString64Bytes commandAction)
            {
                for (var i = 0; i < typeSet.Length; i++)
                {
                    LogEntityAndComponentCommand(entity, typeSet.UnsafeTypesPtrRO[i], commandAction);
                }
            }

            public void LogEntityAndComponentCommand(Entity cmdEntity, TypeIndex typeIndex, in FixedString64Bytes commandAction)
            {
                Entity entity = SelectEntity(cmdEntity, playbackProcessor.playbackState);
                mgr->GetName(entity, out var entityName);

                Debug.Log($"{commandAction} component on entity {entityName}({entity.Index},{entity.Version}) for component index {typeIndex.ToFixedString()}; recorded from {originSystemDebugName}.");
            }

            public void LogEntitiesOnlyCommand(Entity* entities, int count, in FixedString64Bytes commandAction)
            {
                for (var i = 0; i < count; i++)
                {
                    LogEntityOnlyCommand(entities[i], commandAction);
                }
            }

            public void LogEntityOnlyCommand(Entity cmdEntity, in FixedString64Bytes commandAction)
            {
                Entity entity = SelectEntity(cmdEntity, playbackProcessor.playbackState);
                mgr->GetName(entity, out var entityName);

                Debug.Log($"{commandAction} entity {entityName}({entity.Index},{entity.Version}); recorded from {originSystemDebugName}.");
            }

            public void LogLinkedEntityGroupCommand(Entity cmdEntity, TypeIndex typeIndex, in EntityQueryMask mask, in FixedString64Bytes commandAction)
            {
                Entity entity = SelectEntity(cmdEntity, playbackProcessor.playbackState);
                mgr->GetName(entity, out var entityName);
                var linkedTypeIndex = TypeManager.GetTypeIndex<LinkedEntityGroup>();
                using var linkedEntities = mgr->GetBuffer<LinkedEntityGroup>(entity
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                        , mgr->DependencyManager->Safety.GetSafetyHandle(linkedTypeIndex, false),
                        mgr->DependencyManager->Safety.GetBufferSafetyHandle(linkedTypeIndex)
#endif
                    ).Reinterpret<Entity>()
                    .ToNativeArray(mgr->m_WorldUnmanaged.UpdateAllocator.ToAllocator);

                // Filter the linked entities based on the mask
                foreach (var e in linkedEntities)
                {
                    if (mask.MatchesIgnoreFilter(e))
                    {
                        Debug.Log($"{commandAction} component to {entityName}({entity.Index},{entity.Version})'s linked entity ({e.Index},{e.Version}) for component index {typeIndex.ToFixedString()}; recorded from {originSystemDebugName}.");
                    }
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void DestroyEntity(BasicCommand* header)
            {
                var cmd = (EntityCommand*)header;
                FixedString64Bytes commandAction = "Destroying";
                LogEntityOnlyCommand(cmd->Entity, in commandAction);
                playbackProcessor.DestroyEntity(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void RemoveComponent(BasicCommand* header)
            {
                var cmd = (EntityComponentCommand*)header;
                FixedString64Bytes commandAction = "Removing";
                LogEntityAndComponentCommand(cmd->Header.Entity, cmd->ComponentTypeIndex, in commandAction);
                playbackProcessor.RemoveComponent(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void RemoveMultipleComponents(BasicCommand* header)
            {
                var cmd = (EntityMultipleComponentsCommand*)header;
                FixedString64Bytes commandAction = "Removing";
                LogEntityAndComponentsCommand(cmd->Header.Entity, cmd->TypeSet, in commandAction);
                playbackProcessor.RemoveMultipleComponents(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void CreateEntity(BasicCommand* header)
            {
                var cmd = (CreateCommand*)header;

                Debug.Log($"Creating {cmd->BatchCount} entity; recorded from {originSystemDebugName}.");
                playbackProcessor.CreateEntity(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void InstantiateEntity(BasicCommand* header)
            {
                var cmd = (EntityCommand*)header;
                Entity entity = SelectEntity(cmd->Entity, playbackProcessor.playbackState);
                mgr->GetName(entity, out var entityName);

                Debug.Log($"Instantiating {cmd->BatchCount} instance(s) of entity {entityName}({entity.Index},{entity.Version}); recorded from {originSystemDebugName}.");
                playbackProcessor.InstantiateEntity(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddComponent(BasicCommand* header)
            {
                var cmd = (EntityComponentCommand*)header;
                FixedString64Bytes commandAction = "Adding";
                LogEntityAndComponentCommand(cmd->Header.Entity, cmd->ComponentTypeIndex, in commandAction);
                playbackProcessor.AddComponent(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddMultipleComponents(BasicCommand* header)
            {
                var cmd = (EntityMultipleComponentsCommand*)header;
                FixedString64Bytes commandAction = "Adding";
                LogEntityAndComponentsCommand(cmd->Header.Entity, cmd->TypeSet, in commandAction);
                playbackProcessor.AddMultipleComponents(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddComponentWithEntityFixUp(BasicCommand* header)
            {
                var cmd = (EntityComponentCommand*)header;
                FixedString64Bytes commandAction = "Adding";
                LogEntityAndComponentCommand(cmd->Header.Entity, cmd->ComponentTypeIndex, in commandAction);
                playbackProcessor.AddComponentWithEntityFixUp(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetComponent(BasicCommand* header)
            {
                var cmd = (EntityComponentCommand*)header;
                FixedString64Bytes commandAction = "Setting";
                LogEntityAndComponentCommand(cmd->Header.Entity, cmd->ComponentTypeIndex, in commandAction);
                playbackProcessor.SetComponent(header);
            }

            public void SetEnabled(BasicCommand* header)
            {
                var cmd = (EntityEnabledCommand*)header;
                Entity entity = SelectEntity(cmd->Header.Entity, playbackProcessor.playbackState);
                mgr->GetName(entity, out var entityName);
                FixedString32Bytes enabled = cmd->IsEnabled == 0 ? "DISABLED" : "ENABLED";

                Debug.Log($"Setting entity {entityName}({entity.Index},{entity.Version}) to {enabled}; recorded from {originSystemDebugName}.");
                playbackProcessor.SetEnabled(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetComponentEnabled(BasicCommand* header)
            {
                var cmd = (EntityComponentEnabledCommand*)header;
                Entity entity = SelectEntity(cmd->Header.Header.Entity, playbackProcessor.playbackState);
                mgr->GetName(entity, out var entityName);
                FixedString32Bytes enabled = cmd->Header.IsEnabled == 0 ? "FALSE" : "TRUE";

                Debug.Log($"Setting component enableable on entity {entityName}({entity.Index},{entity.Version}) for component index {cmd->ComponentTypeIndex.ToFixedString()} to {enabled}; recorded from {originSystemDebugName}.");
                playbackProcessor.SetComponentEnabled(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetName(BasicCommand* header)
            {
                var cmd = (EntityNameCommand*)header;
                Entity entity = SelectEntity(cmd->Header.Entity, playbackProcessor.playbackState);
                mgr->GetName(entity, out var entityName);

                Debug.Log($"Setting name on entity {entityName}({entity.Index},{entity.Version}) with name {cmd->Name}; recorded from {originSystemDebugName}.");
                playbackProcessor.SetName(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetComponentWithEntityFixUp(BasicCommand* header)
            {
                var cmd = (EntityComponentCommand*)header;
                FixedString64Bytes commandAction = "Setting";
                LogEntityAndComponentCommand(cmd->Header.Entity, cmd->ComponentTypeIndex, in commandAction);
                playbackProcessor.SetComponentWithEntityFixUp(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddBuffer(BasicCommand* header)
            {
                var cmd = (EntityBufferCommand*)header;
                FixedString64Bytes commandAction = "Adding dynamic buffer";
                LogEntityAndComponentCommand(cmd->Header.Entity, cmd->ComponentTypeIndex, in commandAction);
                playbackProcessor.AddBuffer(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddBufferWithEntityFixUp(BasicCommand* header)
            {
                var cmd = (EntityBufferCommand*)header;
                FixedString64Bytes commandAction = "Adding dynamic buffer";
                LogEntityAndComponentCommand(cmd->Header.Entity, cmd->ComponentTypeIndex, in commandAction);
                playbackProcessor.AddBufferWithEntityFixUp(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetBuffer(BasicCommand* header)
            {
                var cmd = (EntityBufferCommand*)header;
                FixedString64Bytes commandAction = "Setting dynamic buffer";
                LogEntityAndComponentCommand(cmd->Header.Entity, cmd->ComponentTypeIndex, in commandAction);
                playbackProcessor.SetBuffer(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetBufferWithEntityFixUp(BasicCommand* header)
            {
                var cmd = (EntityBufferCommand*)header;
                FixedString64Bytes commandAction = "Setting dynamic buffer";
                LogEntityAndComponentCommand(cmd->Header.Entity, cmd->ComponentTypeIndex, in commandAction);
                playbackProcessor.SetBufferWithEntityFixUp(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AppendToBuffer(BasicCommand* header)
            {
                var cmd = (EntityComponentCommand*)header;
                FixedString64Bytes commandAction = "Appending element to dynamic buffer";
                LogEntityAndComponentCommand(cmd->Header.Entity, cmd->ComponentTypeIndex, in commandAction);
                playbackProcessor.AppendToBuffer(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AppendToBufferWithEntityFixUp(BasicCommand* header)
            {
                var cmd = (EntityComponentCommand*)header;
                FixedString64Bytes commandAction = "Appending element to dynamic buffer";
                LogEntityAndComponentCommand(cmd->Header.Entity, cmd->ComponentTypeIndex, in commandAction);
                playbackProcessor.AppendToBufferWithEntityFixUp(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddComponentForEntityQuery(BasicCommand* header)
            {
                var cmd = (EntityQueryComponentCommand*)header;
                FixedString64Bytes commandAction = "Adding";
                Debug.Log($"{commandAction} component on entity query for component index {cmd->ComponentTypeIndex.ToFixedString()}; recorded from {originSystemDebugName}.");
                playbackProcessor.AddComponentForEntityQuery(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddComponentForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesComponentCommand*)header;
                FixedString64Bytes commandAction = "Adding";
                LogEntitiesAndComponentCommand(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->ComponentTypeIndex, in commandAction);
                playbackProcessor.AddComponentForMultipleEntities(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void RemoveComponentForEntityQuery(BasicCommand* header)
            {
                var cmd = (EntityQueryComponentCommand*)header;
                FixedString64Bytes commandAction = "Removing";
                Debug.Log($"{commandAction} component on entity query for component index {cmd->ComponentTypeIndex.ToFixedString()}; recorded from {originSystemDebugName}.");
                playbackProcessor.RemoveComponentForEntityQuery(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void RemoveComponentForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesComponentCommand*)header;
                FixedString64Bytes commandAction = "Removing";
                LogEntitiesAndComponentCommand(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->ComponentTypeIndex, in commandAction);
                playbackProcessor.RemoveComponentForMultipleEntities(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddMultipleComponentsForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesAndComponentsCommand*)header;
                FixedString64Bytes commandAction = "Adding";
                LogEntitiesAndComponentsCommand(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->TypeSet, in commandAction);
                playbackProcessor.AddMultipleComponentsForMultipleEntities(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddMultipleComponentsForEntityQuery(BasicCommand* header)
            {
                var cmd = (EntityQueryComponentTypeSetCommand*)header;
                FixedString64Bytes commandAction = "Adding";
                Debug.Log($"{commandAction} component on entity query for component set {cmd->TypeSet}; recorded from {originSystemDebugName}.");
                playbackProcessor.AddMultipleComponentsForEntityQuery(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void RemoveMultipleComponentsForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesAndComponentsCommand*)header;
                FixedString64Bytes commandAction = "Removing";
                LogEntitiesAndComponentsCommand(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->TypeSet, in commandAction);
                playbackProcessor.RemoveMultipleComponentsForMultipleEntities(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void RemoveMultipleComponentsForEntityQuery(BasicCommand* header)
            {
                var cmd = (EntityQueryComponentTypeSetCommand*)header;
                FixedString64Bytes commandAction = "Removing";
                Debug.Log($"{commandAction} component on entity query for component set {cmd->TypeSet}; recorded from {originSystemDebugName}.");
                playbackProcessor.RemoveMultipleComponentsForEntityQuery(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void DestroyMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesCommand*)header;
                FixedString64Bytes commandAction = "Destroying";
                LogEntitiesOnlyCommand(cmd->Entities.Ptr, cmd->EntitiesCount, in commandAction);
                playbackProcessor.DestroyMultipleEntities(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void DestroyForEntityQuery(BasicCommand* header)
            {
                var cmd = (EntityQueryCommand*)header;
                Debug.Log($"Destroying entities in EntityQuery recorded from {originSystemDebugName}.");
                playbackProcessor.DestroyMultipleEntities(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddComponentLinkedEntityGroup(BasicCommand* header)
            {
                var cmd = (EntityQueryMaskCommand*) header;
                FixedString64Bytes commandAction = "Adding";
                LogLinkedEntityGroupCommand(cmd->Header.Header.Entity, cmd->Header.ComponentTypeIndex, cmd->Mask,
                    commandAction);

                playbackProcessor.AddComponentLinkedEntityGroup(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetComponentLinkedEntityGroup(BasicCommand* header)
            {
                var cmd = (EntityQueryMaskCommand*) header;
                FixedString64Bytes commandAction = "Setting";
                LogLinkedEntityGroupCommand(cmd->Header.Header.Entity, cmd->Header.ComponentTypeIndex, cmd->Mask,
                    commandAction);

                playbackProcessor.SetComponentLinkedEntityGroup(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void ReplaceComponentLinkedEntityGroup(BasicCommand* header)
            {
                var cmd = (EntityComponentCommand*) header;
                FixedString64Bytes commandAction = "Replacing";
                Entity entity = SelectEntity(cmd->Header.Entity, playbackProcessor.playbackState);
                mgr->GetName(entity, out var entityName);
                var linkedTypeIndex = TypeManager.GetTypeIndex<LinkedEntityGroup>();
                using var linkedEntities = mgr->GetBuffer<LinkedEntityGroup>(entity
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                        , mgr->DependencyManager->Safety.GetSafetyHandle(linkedTypeIndex, false),
                        mgr->DependencyManager->Safety.GetBufferSafetyHandle(linkedTypeIndex)
#endif
                    ).Reinterpret<Entity>()
                    .ToNativeArray(mgr->m_WorldUnmanaged.UpdateAllocator.ToAllocator);

                // Filter the linked entities based on the mask
                foreach (var e in linkedEntities)
                {
                    if (mgr->HasComponent(e, ComponentType.FromTypeIndex(cmd->ComponentTypeIndex)))
                    {
                        Debug.Log($"{commandAction} component to {entityName}({entity.Index},{entity.Version})'s linked entity ({e.Index},{e.Version}) for component index {cmd->ComponentTypeIndex.ToFixedString()}; recorded from {originSystemDebugName}.");
                    }
                }

                playbackProcessor.ReplaceComponentLinkedEntityGroup(header);
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddManagedComponentData(BasicCommand* header)
            {
                var cmd = (EntityManagedComponentCommand*)header;
                FixedString64Bytes commandAction = "Adding managed";
                LogEntityAndComponentCommand(cmd->Header.Entity, cmd->ComponentTypeIndex, in commandAction);
                playbackProcessor.AddManagedComponentData(header);
            }


            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddUnmanagedSharedComponentData(BasicCommand* header)
            {
                var cmd = (EntityUnmanagedSharedComponentCommand*)header;
                FixedString64Bytes commandAction = "Adding unmanaged shared";
                LogEntityAndComponentCommand(cmd->Header.Entity, cmd->ComponentTypeIndex, in commandAction);
                playbackProcessor.AddUnmanagedSharedComponentData(header);
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddSharedComponentData(BasicCommand* header)
            {
                var cmd = (EntitySharedComponentCommand*) header;
                FixedString64Bytes commandAction = "Adding shared";
                LogEntityAndComponentCommand(cmd->Header.Entity, cmd->ComponentTypeIndex, in commandAction);
                playbackProcessor.AddSharedComponentData(header);
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddComponentObjectForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesComponentCommandWithObject*)header;
                FixedString64Bytes commandAction = "Adding";
                LogEntitiesAndComponentCommand(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->ComponentTypeIndex, in commandAction);
                playbackProcessor.AddComponentObjectForMultipleEntities(header);
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetComponentObjectForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesComponentCommandWithObject*)header;
                FixedString64Bytes commandAction = "Setting";
                LogEntitiesAndComponentCommand(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->ComponentTypeIndex, in commandAction);
                playbackProcessor.SetComponentObjectForMultipleEntities(header);
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddSharedComponentWithValueForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesComponentCommandWithObject*)header;
                FixedString64Bytes commandAction = "Adding shared";
                LogEntitiesAndComponentCommand(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->ComponentTypeIndex, in commandAction);
                playbackProcessor.AddSharedComponentWithValueForMultipleEntities(header);
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddSharedComponentWithValueForEntityQuery(BasicCommand* header)
            {
                var cmd = (EntityQueryComponentCommandWithObject*)header;
                FixedString64Bytes commandAction = "Adding shared";
                Debug.Log($"{commandAction} component on entity query for component index {cmd->Header.ComponentTypeIndex.ToFixedString()}; recorded from {originSystemDebugName}.");
                playbackProcessor.AddSharedComponentWithValueForEntityQuery(header);
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetSharedComponentValueForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesComponentCommandWithObject*)header;
                FixedString64Bytes commandAction = "Setting shared";
                LogEntitiesAndComponentCommand(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->ComponentTypeIndex, in commandAction);
                playbackProcessor.SetSharedComponentValueForMultipleEntities(header);
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetSharedComponentValueForEntityQuery(BasicCommand* header)
            {
                var cmd = (EntityQueryComponentCommandWithObject*)header;
                FixedString64Bytes commandAction = "Setting shared";
                Debug.Log($"{commandAction} component on entity query for component index {cmd->Header.ComponentTypeIndex.ToFixedString()}; recorded from {originSystemDebugName}.");
                playbackProcessor.SetSharedComponentValueForEntityQuery(header);
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetManagedComponentData(BasicCommand* header)
            {
                var cmd = (EntityManagedComponentCommand*)header;
                FixedString64Bytes commandAction = "Setting managed";
                LogEntityAndComponentCommand(cmd->Header.Entity, cmd->ComponentTypeIndex, in commandAction);
                playbackProcessor.SetManagedComponentData(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetUnmanagedSharedComponentData(BasicCommand* header)
            {
                var cmd = (EntityUnmanagedSharedComponentCommand*)header;
                FixedString64Bytes commandAction = "Setting unmanaged shared";
                LogEntityAndComponentCommand(cmd->Header.Entity, cmd->ComponentTypeIndex, in commandAction);
                playbackProcessor.SetUnmanagedSharedComponentData(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddUnmanagedSharedComponentValueForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesCommand_WithUnmanagedSharedComponent*)header;
                FixedString64Bytes commandAction = "Adding unmanaged shared";
                LogEntitiesAndComponentCommand(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->ComponentTypeIndex, in commandAction);
                playbackProcessor.AddUnmanagedSharedComponentValueForMultipleEntities(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddUnmanagedSharedComponentValueForEntityQuery(BasicCommand* header)
            {
                var cmd = (EntityQueryComponentCommandWithUnmanagedSharedComponent*)header;
                FixedString64Bytes commandAction = "Adding unmanaged shared";
                Debug.Log($"{commandAction} component on entity query for component index {cmd->Header.ComponentTypeIndex.ToFixedString()}; recorded from {originSystemDebugName}.");
                playbackProcessor.AddUnmanagedSharedComponentValueForEntityQuery(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetUnmanagedSharedComponentValueForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesCommand_WithUnmanagedSharedComponent*)header;
                FixedString64Bytes commandAction = "Setting unmanaged shared";
                LogEntitiesAndComponentCommand(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->ComponentTypeIndex, in commandAction);
                playbackProcessor.SetUnmanagedSharedComponentValueForMultipleEntities(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetUnmanagedSharedComponentValueForEntityQuery(BasicCommand* header)
            {
                var cmd = (EntityQueryComponentCommandWithUnmanagedSharedComponent*)header;
                FixedString64Bytes commandAction = "Setting unmanaged shared";
                Debug.Log($"{commandAction} component on entity query for component index {cmd->Header.ComponentTypeIndex.ToFixedString()}; recorded from {originSystemDebugName}.");
                playbackProcessor.SetUnmanagedSharedComponentValueForEntityQuery(header);
            }

            public ECBProcessorType ProcessorType => ECBProcessorType.PlaybackWithTraceProcessor;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetSharedComponentData(BasicCommand* header)
            {
                var cmd = (EntitySharedComponentCommand*)header;
                FixedString64Bytes commandAction = "Setting shared";
                LogEntityAndComponentCommand(cmd->Header.Entity, cmd->ComponentTypeIndex, in commandAction);
                playbackProcessor.SetSharedComponentData(header);
            }
        }

        internal struct PrePlaybackValidationProcessor : IEcbProcessor
        {
            private const string kMsgUnknownSystem = "unknown system type";

            public PlaybackProcessor playbackProcessor;
            public EntityDataAccess* mgr;

            public void Init(EntityDataAccess* entityDataAccess, EntityCommandBufferData* data, in SystemHandle originSystemHandle)
            {
                playbackProcessor.Init(entityDataAccess, data, originSystemHandle);

                mgr = entityDataAccess;
            }

            public void Cleanup()
            {
                playbackProcessor.Cleanup();
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
            private void ThrowIfPrefabComponentInArchetype(EntityArchetype archetype)
            {
                //The Prefab component can not be added or removed in an ECB.
                for(int i = 0; i < archetype.TypesCount; i++)
                {
                    if( archetype.Types[i].ToComponentType() == ComponentType.ReadWrite<Prefab>())
                        throw new InvalidOperationException($"Cannot create an entity with a Prefab component in an EntityCommandBuffer.");
                }
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
            private void ThrowIfPrefabComponent(ComponentType type)
            {
                //The Prefab component can not be added or removed in an ECB.
                if (type == ComponentType.ReadWrite<Prefab>())
                {
                    throw new InvalidOperationException($"Cannot add or remove the Prefab component to an entity within an EntityCommandBuffer.");
                }
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
            private void ThrowIfPrefabComponentInSet(in ComponentTypeSet typeSet)
            {
                for (var i = 0; i < typeSet.Length; i++)
                {
                    ThrowIfPrefabComponent(typeSet.GetComponentType(i));
                }
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
            private void ThrowIfPrefab(Entity e)
            {
                //Entities with the Prefab component can not be created or destroyed in an ECB.
                if (mgr->HasComponent(e, ComponentType.ReadWrite<Prefab>()))
                {
                    throw new InvalidOperationException($"Cannot create, destroy, add, or remove components an entity that has the Prefab tag within an EntityCommandBuffer.");
                }
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
            private void ThrowIfPrefab(Entity* entities, int entityCount, bool skipDeferredEntityLookup)
            {
                if (skipDeferredEntityLookup)
                {
                    for (int len = entityCount, i = 0; i < len; ++i)
                    {
                        ThrowIfPrefab(entities[i]);
                    }
                }
                else
                {
                    for (int len = entityCount, i = 0; i < len; ++i)
                    {
                        var ent = SelectEntity(entities[i], playbackProcessor.playbackState);
                        ThrowIfPrefab(ent);
                    }
                }
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
            private void ThrowIfPrefabInSetOfEntities(NativeArray<Entity> entities)
            {
                for (var i = 0; i < entities.Length; i++)
                {
                    ThrowIfPrefab(entities[i]);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void DestroyEntity(BasicCommand* header)
            {
                var cmd = (EntityCommand*)header;

                Entity entity = SelectEntity(cmd->Entity, playbackProcessor.playbackState);
                ThrowIfPrefab(entity);

                playbackProcessor.DestroyEntity(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void RemoveComponent(BasicCommand* header)
            {
                var cmd = (EntityComponentCommand*)header;

                var entity = SelectEntity(cmd->Header.Entity, playbackProcessor.playbackState);
                ThrowIfPrefab(entity);
                ThrowIfPrefabComponent(ComponentType.FromTypeIndex(cmd->ComponentTypeIndex));

                playbackProcessor.RemoveComponent(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void RemoveMultipleComponents(BasicCommand* header)
            {
                var cmd = (EntityMultipleComponentsCommand*)header;

                var entity = SelectEntity(cmd->Header.Entity, playbackProcessor.playbackState);
                var componentTypes = cmd->TypeSet;
                ThrowIfPrefab(entity);
                ThrowIfPrefabComponentInSet(in componentTypes);

                playbackProcessor.RemoveMultipleComponents(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void CreateEntity(BasicCommand* header)
            {
                var cmd = (CreateCommand*)header;

                EntityArchetype at = cmd->Archetype;
                if (!at.Valid)
                    at = mgr->GetEntityAndSimulateArchetype();
                ThrowIfPrefabComponentInArchetype(at);

                playbackProcessor.CreateEntity(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void InstantiateEntity(BasicCommand* header)
            {
                var cmd = (EntityCommand*)header;
                playbackProcessor.InstantiateEntity(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddComponent(BasicCommand* header)
            {
                var cmd = (EntityComponentCommand*)header;

                var componentType = ComponentType.FromTypeIndex(cmd->ComponentTypeIndex);
                var entity = SelectEntity(cmd->Header.Entity, playbackProcessor.playbackState);
                ThrowIfPrefab(entity);
                ThrowIfPrefabComponent(componentType);

                playbackProcessor.AddComponent(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddMultipleComponents(BasicCommand* header)
            {
                var cmd = (EntityMultipleComponentsCommand*)header;

                var entity = SelectEntity(cmd->Header.Entity, playbackProcessor.playbackState);
                ThrowIfPrefab(entity);
                ThrowIfPrefabComponentInSet(in cmd->TypeSet);

                playbackProcessor.AddMultipleComponents(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddComponentWithEntityFixUp(BasicCommand* header)
            {
                var cmd = (EntityComponentCommand*)header;

                var entity = SelectEntity(cmd->Header.Entity, playbackProcessor.playbackState);
                ThrowIfPrefab(entity);

                playbackProcessor.AddComponentWithEntityFixUp(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetComponent(BasicCommand* header)
            {
                var cmd = (EntityComponentCommand*)header;

                var entity = SelectEntity(cmd->Header.Entity, playbackProcessor.playbackState);
                ThrowIfPrefab(entity);

                playbackProcessor.SetComponent(header);
            }

            public void SetEnabled(BasicCommand* header)
            {
                var cmd = (EntityEnabledCommand*)header;

                var entity = SelectEntity(cmd->Header.Entity, playbackProcessor.playbackState);
                ThrowIfPrefab(entity);

                playbackProcessor.SetEnabled(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetComponentEnabled(BasicCommand* header)
            {
                var cmd = (EntityComponentEnabledCommand*)header;

                var entity = SelectEntity(cmd->Header.Header.Entity, playbackProcessor.playbackState);
                ThrowIfPrefab(entity);

                playbackProcessor.SetComponentEnabled(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetName(BasicCommand* header)
            {
                var cmd = (EntityNameCommand*)header;

                var entity = SelectEntity(cmd->Header.Entity, playbackProcessor.playbackState);
                ThrowIfPrefab(entity);

                playbackProcessor.SetName(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetComponentWithEntityFixUp(BasicCommand* header)
            {
                var cmd = (EntityComponentCommand*)header;

                var entity = SelectEntity(cmd->Header.Entity, playbackProcessor.playbackState);
                ThrowIfPrefab(entity);

                playbackProcessor.SetComponentWithEntityFixUp(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddBuffer(BasicCommand* header)
            {
                var cmd = (EntityBufferCommand*)header;

                var entity = SelectEntity(cmd->Header.Entity, playbackProcessor.playbackState);
                ThrowIfPrefab(entity);

                playbackProcessor.AddBuffer(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddBufferWithEntityFixUp(BasicCommand* header)
            {
                var cmd = (EntityBufferCommand*)header;

                var entity = SelectEntity(cmd->Header.Entity, playbackProcessor.playbackState);
                ThrowIfPrefab(entity);

                playbackProcessor.AddBufferWithEntityFixUp(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetBuffer(BasicCommand* header)
            {
                var cmd = (EntityBufferCommand*)header;

                var entity = SelectEntity(cmd->Header.Entity, playbackProcessor.playbackState);
                ThrowIfPrefab(entity);

                playbackProcessor.SetBuffer(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetBufferWithEntityFixUp(BasicCommand* header)
            {
                var cmd = (EntityBufferCommand*)header;

                var entity = SelectEntity(cmd->Header.Entity, playbackProcessor.playbackState);
                ThrowIfPrefab(entity);

                playbackProcessor.SetBufferWithEntityFixUp(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AppendToBuffer(BasicCommand* header)
            {
                var cmd = (EntityComponentCommand*)header;

                var entity = SelectEntity(cmd->Header.Entity, playbackProcessor.playbackState);
                ThrowIfPrefab(entity);

                playbackProcessor.AppendToBuffer(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AppendToBufferWithEntityFixUp(BasicCommand* header)
            {
                var cmd = (EntityComponentCommand*)header;

                var entity = SelectEntity(cmd->Header.Entity, playbackProcessor.playbackState);
                ThrowIfPrefab(entity);

                playbackProcessor.AppendToBufferWithEntityFixUp(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddComponentForEntityQuery(BasicCommand* header)
            {
                var cmd = (EntityQueryComponentCommand*)header;

                var componentType = ComponentType.FromTypeIndex(cmd->ComponentTypeIndex);
                ThrowIfPrefabComponent(componentType);

                // TODO: throw if any matching archetypes have the Prefab component
                //ThrowIfPrefab(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->Header.SkipDeferredEntityLookup != 0);

                playbackProcessor.AddComponentForEntityQuery(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddComponentForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesComponentCommand*)header;

                var componentType = ComponentType.FromTypeIndex(cmd->ComponentTypeIndex);
                ThrowIfPrefabComponent(componentType);

                ThrowIfPrefab(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->Header.SkipDeferredEntityLookup != 0);

                playbackProcessor.AddComponentForMultipleEntities(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void RemoveComponentForEntityQuery(BasicCommand* header)
            {
                var cmd = (EntityQueryComponentCommand*)header;

                var componentType = ComponentType.FromTypeIndex(cmd->ComponentTypeIndex);
                ThrowIfPrefabComponent(componentType);

                // TODO: throw if any matching archetypes have the Prefab component
                //ThrowIfPrefab(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->Header.SkipDeferredEntityLookup != 0);

                playbackProcessor.RemoveComponentForEntityQuery(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void RemoveComponentForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesComponentCommand*)header;

                var componentType = ComponentType.FromTypeIndex(cmd->ComponentTypeIndex);
                ThrowIfPrefabComponent(componentType);

                ThrowIfPrefab(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->Header.SkipDeferredEntityLookup != 0);

                playbackProcessor.RemoveComponentForMultipleEntities(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddMultipleComponentsForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesAndComponentsCommand*)header;

                ThrowIfPrefabComponentInSet(in cmd->TypeSet);

                ThrowIfPrefab(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->Header.SkipDeferredEntityLookup != 0);

                playbackProcessor.AddMultipleComponentsForMultipleEntities(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddMultipleComponentsForEntityQuery(BasicCommand* header)
            {
                var cmd = (EntityQueryComponentTypeSetCommand*)header;

                ThrowIfPrefabComponentInSet(in cmd->TypeSet);

                // TODO: throw if any matching archetypes have the Prefab component
                //ThrowIfPrefab(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->Header.SkipDeferredEntityLookup != 0);

                playbackProcessor.AddMultipleComponentsForEntityQuery(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void RemoveMultipleComponentsForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesAndComponentsCommand*)header;

                ThrowIfPrefabComponentInSet(in cmd->TypeSet);

                ThrowIfPrefab(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->Header.SkipDeferredEntityLookup != 0);

                playbackProcessor.RemoveMultipleComponentsForMultipleEntities(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void RemoveMultipleComponentsForEntityQuery(BasicCommand* header)
            {
                var cmd = (EntityQueryComponentTypeSetCommand*)header;

                ThrowIfPrefabComponentInSet(in cmd->TypeSet);

                // TODO: throw if any matching archetypes have the Prefab component
                //ThrowIfPrefab(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->Header.SkipDeferredEntityLookup != 0);

                playbackProcessor.RemoveMultipleComponentsForEntityQuery(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void DestroyMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesCommand*)header;

                ThrowIfPrefab(cmd->Entities.Ptr, cmd->EntitiesCount, cmd->SkipDeferredEntityLookup != 0);

                playbackProcessor.DestroyMultipleEntities(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void DestroyForEntityQuery(BasicCommand* header)
            {
                var cmd = (EntityQueryCommand*)header;

                // TODO: throw if any matching archetypes have the Prefab component
                //ThrowIfPrefab(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->Header.SkipDeferredEntityLookup != 0);

                playbackProcessor.DestroyForEntityQuery(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddComponentLinkedEntityGroup(BasicCommand* header)
            {
                var cmd = (EntityQueryMaskCommand*) header;

                var entity = SelectEntity(cmd->Header.Header.Entity, playbackProcessor.playbackState);
                ThrowIfPrefab(entity);

                ThrowIfPrefabComponent(ComponentType.FromTypeIndex(cmd->Header.ComponentTypeIndex));

                playbackProcessor.AddComponentLinkedEntityGroup(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetComponentLinkedEntityGroup(BasicCommand* header)
            {
                var cmd = (EntityQueryMaskCommand*) header;

                var entity = SelectEntity(cmd->Header.Header.Entity, playbackProcessor.playbackState);
                ThrowIfPrefab(entity);

                playbackProcessor.SetComponentLinkedEntityGroup(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void ReplaceComponentLinkedEntityGroup(BasicCommand* header)
            {
                var cmd = (EntityComponentCommand*) header;

                var entity = SelectEntity(cmd->Header.Entity, playbackProcessor.playbackState);
                ThrowIfPrefab(entity);

                playbackProcessor.ReplaceComponentLinkedEntityGroup(header);
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddManagedComponentData(BasicCommand* header)
            {
                var cmd = (EntityManagedComponentCommand*)header;

                var entity = SelectEntity(cmd->Header.Entity, playbackProcessor.playbackState);
                ThrowIfPrefab(entity);

                playbackProcessor.AddManagedComponentData(header);
            }


            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddUnmanagedSharedComponentData(BasicCommand* header)
            {
                var cmd = (EntityUnmanagedSharedComponentCommand*)header;

                var entity = SelectEntity(cmd->Header.Entity, playbackProcessor.playbackState);
                ThrowIfPrefab(entity);

                playbackProcessor.AddUnmanagedSharedComponentData(header);
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddSharedComponentData(BasicCommand* header)
            {
                var cmd = (EntitySharedComponentCommand*) header;

                var entity = SelectEntity(cmd->Header.Entity, playbackProcessor.playbackState);
                ThrowIfPrefab(entity);

                playbackProcessor.AddSharedComponentData(header);
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddComponentObjectForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesComponentCommandWithObject*)header;

                var componentType = ComponentType.FromTypeIndex(cmd->ComponentTypeIndex);
                ThrowIfPrefabComponent(componentType);

                ThrowIfPrefab(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->Header.SkipDeferredEntityLookup != 0);

                playbackProcessor.AddComponentObjectForMultipleEntities(header);
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetComponentObjectForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesComponentCommandWithObject*)header;

                ThrowIfPrefab(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->Header.SkipDeferredEntityLookup != 0);

                playbackProcessor.SetComponentObjectForMultipleEntities(header);
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddSharedComponentWithValueForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesComponentCommandWithObject*)header;

                ThrowIfPrefab(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->Header.SkipDeferredEntityLookup != 0);

                playbackProcessor.AddSharedComponentWithValueForMultipleEntities(header);
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddSharedComponentWithValueForEntityQuery(BasicCommand* header)
            {
                var cmd = (EntityQueryComponentCommandWithObject*)header;

                // TODO: throw if any matching archetypes have the Prefab component
                //ThrowIfPrefab(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->Header.SkipDeferredEntityLookup != 0);

                playbackProcessor.AddSharedComponentWithValueForEntityQuery(header);
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetSharedComponentValueForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesComponentCommandWithObject*)header;

                ThrowIfPrefab(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->Header.SkipDeferredEntityLookup != 0);

                playbackProcessor.SetSharedComponentValueForMultipleEntities(header);
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetSharedComponentValueForEntityQuery(BasicCommand* header)
            {
                var cmd = (EntityQueryComponentCommandWithObject*)header;

                // TODO: throw if any matching archetypes have the Prefab component
                //ThrowIfPrefab(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->Header.SkipDeferredEntityLookup != 0);

                playbackProcessor.SetSharedComponentValueForEntityQuery(header);
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetManagedComponentData(BasicCommand* header)
            {
                var cmd = (EntityManagedComponentCommand*)header;

                var entity = SelectEntity(cmd->Header.Entity, playbackProcessor.playbackState);
                ThrowIfPrefab(entity);

                playbackProcessor.SetManagedComponentData(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetUnmanagedSharedComponentData(BasicCommand* header)
            {
                var cmd = (EntityUnmanagedSharedComponentCommand*)header;

                var entity = SelectEntity(cmd->Header.Entity, playbackProcessor.playbackState);
                ThrowIfPrefab(entity);

                playbackProcessor.SetUnmanagedSharedComponentData(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddUnmanagedSharedComponentValueForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesCommand_WithUnmanagedSharedComponent*)header;

                ThrowIfPrefab(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->Header.SkipDeferredEntityLookup != 0);

                playbackProcessor.AddUnmanagedSharedComponentValueForMultipleEntities(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddUnmanagedSharedComponentValueForEntityQuery(BasicCommand* header)
            {
                var cmd = (EntityQueryComponentCommandWithUnmanagedSharedComponent*)header;

                // TODO: throw if any matching archetypes have the Prefab component
                //ThrowIfPrefab(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->Header.SkipDeferredEntityLookup != 0);

                playbackProcessor.AddUnmanagedSharedComponentValueForEntityQuery(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetUnmanagedSharedComponentValueForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesCommand_WithUnmanagedSharedComponent*)header;

                ThrowIfPrefab(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->Header.SkipDeferredEntityLookup != 0);

                playbackProcessor.SetUnmanagedSharedComponentValueForMultipleEntities(header);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetUnmanagedSharedComponentValueForEntityQuery(BasicCommand* header)
            {
                var cmd = (EntityQueryComponentCommandWithUnmanagedSharedComponent*)header;

                // TODO: throw if any matching archetypes have the Prefab component
                //ThrowIfPrefab(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, cmd->Header.SkipDeferredEntityLookup != 0);

                playbackProcessor.SetUnmanagedSharedComponentValueForEntityQuery(header);
            }

            public ECBProcessorType ProcessorType => ECBProcessorType.PrePlaybackValidationProcessor;

            public void SetSharedComponentData(BasicCommand* header)
            {
                var cmd = (EntitySharedComponentCommand*)header;

                var entity = SelectEntity(cmd->Header.Entity, playbackProcessor.playbackState);
                ThrowIfPrefab(entity);

                playbackProcessor.SetSharedComponentData(header);
            }
        }

        internal struct DebugViewProcessor : IEcbProcessor
        {
            public UnsafePtrList<BasicCommand> commands;

            public void Init(AllocatorManager.AllocatorHandle allocator)
            {
                commands = new UnsafePtrList<BasicCommand>(INITIAL_COMMANDS_CAPACITY, allocator);
            }

            public void Cleanup()
            {
                commands.Dispose();
            }

            public unsafe void DestroyEntity(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void RemoveComponent(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void RemoveMultipleComponents(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void CreateEntity(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void InstantiateEntity(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void AddComponent(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void AddMultipleComponents(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void AddComponentWithEntityFixUp(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void SetComponent(BasicCommand* header)
            {
                commands.Add(header);
            }

            public void SetEnabled(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void SetComponentEnabled(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void SetName(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void SetComponentWithEntityFixUp(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void AddBuffer(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void AddBufferWithEntityFixUp(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void SetBuffer(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void SetBufferWithEntityFixUp(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void AppendToBuffer(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void AppendToBufferWithEntityFixUp(BasicCommand* header)
            {
                commands.Add(header);
            }

            public void AddComponentForEntityQuery(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void AddComponentForMultipleEntities(BasicCommand* header)
            {
                commands.Add(header);
            }

            public void RemoveComponentForEntityQuery(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void RemoveComponentForMultipleEntities(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void AddMultipleComponentsForMultipleEntities(BasicCommand* header)
            {
                commands.Add(header);
            }

            public void AddMultipleComponentsForEntityQuery(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void RemoveMultipleComponentsForMultipleEntities(BasicCommand* header)
            {
                commands.Add(header);
            }

            public void RemoveMultipleComponentsForEntityQuery(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void DestroyMultipleEntities(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void DestroyForEntityQuery(BasicCommand* header)
            {
                commands.Add(header);
            }

            public void AddComponentLinkedEntityGroup(BasicCommand* header)
            {
                commands.Add(header);
            }

            public void SetComponentLinkedEntityGroup(BasicCommand* header)
            {
                commands.Add(header);
            }

            public void ReplaceComponentLinkedEntityGroup(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void AddManagedComponentData(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void AddSharedComponentData(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void AddComponentObjectForMultipleEntities(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void SetComponentObjectForMultipleEntities(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void AddSharedComponentWithValueForMultipleEntities(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void AddSharedComponentWithValueForEntityQuery(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void SetSharedComponentValueForMultipleEntities(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void SetSharedComponentValueForEntityQuery(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void SetManagedComponentData(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void SetSharedComponentData(BasicCommand* header)
            {
                commands.Add(header);
            }

            public void AddUnmanagedSharedComponentData(BasicCommand* header)
            {
                commands.Add(header);
            }

            public void SetUnmanagedSharedComponentData(BasicCommand* header)
            {
                commands.Add(header);
            }

            public void AddUnmanagedSharedComponentValueForMultipleEntities(BasicCommand* header)
            {
                commands.Add(header);
            }

            public void AddUnmanagedSharedComponentValueForEntityQuery(BasicCommand* header)
            {
                commands.Add(header);
            }

            public void SetUnmanagedSharedComponentValueForMultipleEntities(BasicCommand* header)
            {
                commands.Add(header);
            }

            public void SetUnmanagedSharedComponentValueForEntityQuery(BasicCommand* header)
            {
                commands.Add(header);
            }

            public ECBProcessorType ProcessorType => ECBProcessorType.DebugViewProcessor;
        }

        internal class BasicCommandView
        {
            public ECBCommand CommandType;
            public int SortKey;
            public int TotalSizeInBytes;

            public BasicCommandView()
            {
                CommandType = default;
                SortKey = Int32.MinValue;
                TotalSizeInBytes = 0;
            }

            public BasicCommandView(ECBCommand commandType, int sortKey, int totalSize)
            {
                CommandType = commandType;
                SortKey = sortKey;
                TotalSizeInBytes = totalSize;
            }
        }

        internal class CreateCommandView : BasicCommandView
        {
            public EntityArchetype EntityArchetype;
            public int EntityIdentityIndex;
            public int BatchCount;

            public CreateCommandView(ECBCommand commandType, int sortKey, int totalSize, EntityArchetype archetype, int identityIndex, int batchCount)
            {
                CommandType = commandType;
                SortKey = sortKey;
                TotalSizeInBytes = totalSize;
                EntityArchetype = archetype;
                EntityIdentityIndex = identityIndex;
                BatchCount = batchCount;
            }

            public override string ToString()
            {
                return "Create Entity";
            }
        }

        internal class EntityCommandView : BasicCommandView
        {
            public Entity Entity;
            public int IdentityIndex;
            public int BatchCount;

            public EntityCommandView()
            {
                Entity = Entity.Null;
                IdentityIndex = Int32.MinValue;
                BatchCount = 0;
            }

            public EntityCommandView(ECBCommand commandType, int sortKey, int totalSize, Entity entity, int identityIndex, int batchCount)
            {
                CommandType = commandType;
                SortKey = sortKey;
                TotalSizeInBytes = totalSize;
                Entity = entity;
                IdentityIndex = identityIndex;
                BatchCount = batchCount;
            }

            public override string ToString()
            {
                return (CommandType == ECBCommand.InstantiateEntity) ? $"Instantiate Entity (count={BatchCount})" : "Destroy Entity";
            }
        }

        internal class EntityQueryCommandView : BasicCommandView
        {
            public EntityQueryImpl* Query;

            public EntityQueryCommandView()
            {
                CommandType = 0;
                SortKey = 0;
                TotalSizeInBytes = 0;
                Query = null;
            }

            public EntityQueryCommandView(ECBCommand commandType, int sortKey, int totalSize, EntityQueryImpl* queryImpl)
            {
                CommandType = commandType;
                SortKey = sortKey;
                TotalSizeInBytes = totalSize;
                Query = queryImpl;
            }
        }

        internal class EntityQueryComponentCommandView : EntityQueryCommandView
        {
            public TypeIndex ComponentTypeIndex;

            public EntityQueryComponentCommandView()
            {
                CommandType = 0;
                SortKey = 0;
                TotalSizeInBytes = 0;
                Query = null;
                ComponentTypeIndex = 0;
            }

            public EntityQueryComponentCommandView(ECBCommand commandType, int sortKey, int totalSize,
                EntityQueryImpl* queryImpl, TypeIndex componentTypeIndex)
            {
                CommandType = commandType;
                SortKey = sortKey;
                TotalSizeInBytes = totalSize;
                Query = queryImpl;
                ComponentTypeIndex = componentTypeIndex;
            }

            public override string ToString()
            {
#if !NET_DOTS
                var type = TypeManager.GetType(ComponentTypeIndex);
                var typeName = type.Name + " ";
#else
                var typeName = "";
#endif
                return (CommandType == ECBCommand.AddComponentForEntityQuery) ? $"Add {typeName}Component to EntityQuery" : $"Remove {typeName}Component from EntityQuery";
            }
        }

        internal class EntityQueryComponentTypeSetCommandView : EntityQueryCommandView
        {
            public ComponentTypeSet TypeSet;

            public EntityQueryComponentTypeSetCommandView(ECBCommand commandType, int sortKey, int totalSize,
                EntityQueryImpl* queryImpl, in ComponentTypeSet typeSet)
            {
                CommandType = commandType;
                SortKey = sortKey;
                TotalSizeInBytes = totalSize;
                Query = queryImpl;
                TypeSet = typeSet;
            }

            public override string ToString()
            {
                return (CommandType == ECBCommand.AddComponentForEntityQuery) ? $"Add {TypeSet} to EntityQuery" : $"Remove {TypeSet} from EntityQuery";
            }
        }

        internal class MultipleEntitiesCommandView : BasicCommandView
        {
            public EntityNode Entities;
            public int EntitiesCount;
            public bool SkipDeferredEntityLookup;
            public AllocatorManager.AllocatorHandle Allocator;

            public MultipleEntitiesCommandView()
            {
                Entities = new EntityNode();
                EntitiesCount = 0;
                Allocator = Unity.Collections.Allocator.Invalid;
                SkipDeferredEntityLookup = false;
            }

            public MultipleEntitiesCommandView(ECBCommand commandType, int sortKey, int totalSize, EntityNode entities, int entitiesCount,
                bool skipDeferredEntityLookup, AllocatorManager.AllocatorHandle allocator)
            {
                CommandType = commandType;
                SortKey = sortKey;
                TotalSizeInBytes = totalSize;
                Entities = entities;
                EntitiesCount = entitiesCount;
                SkipDeferredEntityLookup = skipDeferredEntityLookup;
                Allocator = allocator;
            }

            public override string ToString()
            {
                return (CommandType == ECBCommand.CreateEntity) ? $"Instantiate {EntitiesCount} Entities" : $"Destroy {EntitiesCount} Entities";
            }
        }

        internal class MultipleEntitiesComponentCommandView : MultipleEntitiesCommandView
        {
            public TypeIndex ComponentTypeIndex;
            public int ComponentSize;
            public object ComponentValue;

            public MultipleEntitiesComponentCommandView(ECBCommand commandType, int sortKey, int totalSize,
                EntityNode entities, int entitiesCount, bool skipDeferredEntityLookup, AllocatorManager.AllocatorHandle allocator, TypeIndex componentTypeIndex, int componentSize,
                byte* componentValue)
            {
                CommandType = commandType;
                SortKey = sortKey;
                TotalSizeInBytes = totalSize;
                Entities = entities;
                EntitiesCount = entitiesCount;
                SkipDeferredEntityLookup = skipDeferredEntityLookup;
                Allocator = allocator;
                ComponentTypeIndex = componentTypeIndex;
                ComponentSize = componentSize;
                if (ComponentSize > 0 && componentValue != null)
                {
                    ComponentValue = TypeManager.ConstructComponentFromBuffer(componentTypeIndex, componentValue);
                }
                else
                {
                    ComponentValue = default;
                }
            }

            public override string ToString()
            {
#if !NET_DOTS
                var type = TypeManager.GetType(ComponentTypeIndex);
                var typeName = type.Name + " ";
#else
                var typeName = "";
#endif
                return (CommandType == ECBCommand.AddComponentForMultipleEntities) ? $"Add {typeName}Component to {EntitiesCount} Entities" : $"Remove {typeName}Component from {EntitiesCount} Entities";
            }
        }

        internal class MultipleEntitiesComponentCommandWithObjectView : MultipleEntitiesCommandView
        {
            public TypeIndex ComponentTypeIndex;
            public int HashCode;
            public EntityComponentGCNode GCNode;

            internal object GetBoxedObject()
            {
                if (GCNode.BoxedObject.IsAllocated)
                    return GCNode.BoxedObject.Target;
                return null;
            }

            public MultipleEntitiesComponentCommandWithObjectView(ECBCommand commandType, int sortKey, int totalSize, EntityNode entities, int entitiesCount,
                bool skipDeferredEntityLookup, AllocatorManager.AllocatorHandle allocator, TypeIndex componentTypeIndex, int hashCode, EntityComponentGCNode gcNode)
            {
                CommandType = commandType;
                SortKey = sortKey;
                TotalSizeInBytes = totalSize;
                Entities = entities;
                EntitiesCount = entitiesCount;
                SkipDeferredEntityLookup = skipDeferredEntityLookup;
                Allocator = allocator;
                ComponentTypeIndex = componentTypeIndex;
                HashCode = hashCode;
                GCNode = gcNode;
            }

            public override string ToString()
            {
#if  !NET_DOTS
                var type = TypeManager.GetType(ComponentTypeIndex);
                var typeName = type.Name + " ";
#else
                var typeName = "";
#endif
                return CommandType == ECBCommand.AddComponentObjectForMultipleEntities ||
                       CommandType == ECBCommand.AddSharedComponentWithValueForMultipleEntities ?
                 $"Add {typeName}Component to {EntitiesCount} Entities" :
                 $"Set {typeName}Component to {EntitiesCount} Entities";
            }
        }

        internal class EntityQueryComponentCommandWithObjectView : EntityQueryComponentCommandView
        {
            public int HashCode;
            public EntityComponentGCNode GCNode;

            internal object GetBoxedObject()
            {
                if (GCNode.BoxedObject.IsAllocated)
                    return GCNode.BoxedObject.Target;
                return null;
            }

            public EntityQueryComponentCommandWithObjectView(ECBCommand commandType, int sortKey, int totalSize,
                EntityQueryImpl* queryImpl, TypeIndex componentTypeIndex, int hashCode, EntityComponentGCNode gcNode)
            {
                CommandType = commandType;
                SortKey = sortKey;
                TotalSizeInBytes = totalSize;
                Query = queryImpl;
                ComponentTypeIndex = componentTypeIndex;
                HashCode = hashCode;
                GCNode = gcNode;
            }

            public override string ToString()
            {
#if  !NET_DOTS
                var type = TypeManager.GetType(ComponentTypeIndex);
                var typeName = type.Name + " ";
#else
                var typeName = "";
#endif
                return CommandType == ECBCommand.AddSharedComponentWithValueForEntityQuery ||
                        CommandType == ECBCommand.AddComponentObjectForEntityQuery ?
                    $"Add {typeName}Component to EntityQuery" :
                    $"Set {typeName}Component to EntityQuery";
            }
        }

        internal class MultipleEntitiesAndComponentsCommandView : MultipleEntitiesCommandView
        {
            public ComponentTypeSet TypeSet;

            public MultipleEntitiesAndComponentsCommandView(ECBCommand commandType, int sortKey, int totalSize, EntityNode entities, int entitiesCount,
                bool skipDeferredEntityLookup, AllocatorManager.AllocatorHandle allocator, in ComponentTypeSet typeSet)
            {
                CommandType = commandType;
                SortKey = sortKey;
                TotalSizeInBytes = totalSize;
                Entities = entities;
                EntitiesCount = entitiesCount;
                SkipDeferredEntityLookup = skipDeferredEntityLookup;
                Allocator = allocator;
                TypeSet = typeSet;
            }

            public override string ToString()
            {
                return CommandType == ECBCommand.AddMultipleComponentsForMultipleEntities ? $"Add {TypeSet.Length} " +
                    $"Components to {EntitiesCount} Entities": $"Remove {TypeSet.Length} Components from {EntitiesCount} Entities";
            }
        }

        internal class EntityComponentCommandView : EntityCommandView
        {
            public TypeIndex ComponentTypeIndex;
            public int ComponentSize;
            public object ComponentValue;

            public EntityComponentCommandView()
            {
                ComponentTypeIndex = TypeIndex.Null;
                ComponentSize = 0;
                ComponentValue = default;
            }

            public EntityComponentCommandView(ECBCommand commandType, int sortKey, int totalSize, Entity entity,
                int identityIndex, int batchCount, TypeIndex componentTypeIndex, int componentSize, byte* componentValue)
            {
                CommandType = commandType;
                SortKey = sortKey;
                TotalSizeInBytes = totalSize;
                Entity = entity;
                IdentityIndex = identityIndex;
                BatchCount = batchCount;
                ComponentTypeIndex = componentTypeIndex;
                ComponentSize = componentSize;
                if (ComponentSize > 0 && componentValue != null)
                {
                    ComponentValue = TypeManager.ConstructComponentFromBuffer(componentTypeIndex, componentValue);
                }
                else
                {
                    ComponentValue = default;
                }
            }

            public override string ToString()
            {
#if !NET_DOTS
                var type = TypeManager.GetType(ComponentTypeIndex);
                var typeName = type.Name + " ";
                #else
                var typeName = "";
#endif
                switch (CommandType)
                {
                    case ECBCommand.RemoveComponent: return $"Remove {typeName}Component";
                    case ECBCommand.AddComponent: return $"Add {typeName}Component";
                    case ECBCommand.AddComponentWithEntityFixUp: return $"Add {typeName}Component";
                    case ECBCommand.SetComponent: return $"Set {typeName}Component";
                    case ECBCommand.SetComponentWithEntityFixUp: return $"Set {typeName}Component";
                    case ECBCommand.AppendToBuffer: return $"Append {typeName}BufferElementData";
                    case ECBCommand.AppendToBufferWithEntityFixUp: return $"Append {typeName}BufferElementData";
                    case ECBCommand.ReplaceComponentLinkedEntityGroup:
                        return $"Replace {typeName}Component for LinkedEntityGroup";
                    default: throw new ArgumentException("Unknown Command");
                }
            }
        }

        internal class EntityQueryMaskCommandView : EntityComponentCommandView
        {
            public EntityQueryMask Mask;

            public EntityQueryMaskCommandView()
            {
                ComponentTypeIndex = TypeIndex.Null;
                ComponentSize = 0;
                ComponentValue = default;
            }

            public EntityQueryMaskCommandView(ECBCommand commandType, int sortKey, int totalSize, Entity entity,
                int identityIndex, int batchCount, EntityQueryMask mask, TypeIndex componentTypeIndex, int componentSize, byte* componentValue)
            {
                CommandType = commandType;
                SortKey = sortKey;
                TotalSizeInBytes = totalSize;
                Entity = entity;
                IdentityIndex = identityIndex;
                BatchCount = batchCount;
                Mask = mask;
                ComponentTypeIndex = componentTypeIndex;
                ComponentSize = componentSize;
                if (ComponentSize > 0 && componentValue != null)
                {
                    ComponentValue = TypeManager.ConstructComponentFromBuffer(componentTypeIndex, componentValue);
                }
                else
                {
                    ComponentValue = default;
                }
            }

            public override string ToString()
            {
#if !NET_DOTS
                var type = TypeManager.GetType(ComponentTypeIndex);
                var typeName = type.Name + " ";
                #else
                var typeName = "";
#endif
                switch (CommandType)
                {
                    case ECBCommand.AddComponentLinkedEntityGroup: return $"Add {typeName}Component for LinkedEntityGroup";
                    case ECBCommand.SetComponentLinkedEntityGroup: return $"Set {typeName}Component for LinkedEntityGroup";
                    default: throw new ArgumentException("Unknown Command");
                }
            }
        }

        internal class EntityEnabledCommandView : EntityCommandView
        {
            public int ComponentTypeIndex;
            public byte IsEnabled;

            public EntityEnabledCommandView(ECBCommand commandType, int sortKey, int totalSize, Entity entity, int identityIndex, int batchCount, byte isEnabled)
            {
                CommandType = commandType;
                SortKey = sortKey;
                TotalSizeInBytes = totalSize;
                Entity = entity;
                IdentityIndex = identityIndex;
                BatchCount = batchCount;
                IsEnabled = isEnabled;
            }

            public override string ToString()
            {
                return IsEnabled == 1 ? $"Set Entity ({Entity.Index},{Entity.Version}) to Enabled" : $"Set Entity ({Entity.Index},{Entity.Version}) to Disabled";
            }

        }

        internal class EntityComponentEnabledCommandView : EntityCommandView
        {
            public TypeIndex ComponentTypeIndex;
            public byte IsEnabled;

            public EntityComponentEnabledCommandView(ECBCommand commandType, int sortKey, int totalSize, Entity entity, int identityIndex, int batchCount, TypeIndex componentTypeIndex, byte isEnabled)
            {
                CommandType = commandType;
                SortKey = sortKey;
                TotalSizeInBytes = totalSize;
                Entity = entity;
                IdentityIndex = identityIndex;
                BatchCount = batchCount;
                ComponentTypeIndex = componentTypeIndex;
                IsEnabled = isEnabled;
            }

            public override string ToString()
            {
#if !NET_DOTS
                var type = TypeManager.GetType(ComponentTypeIndex);
                var typeName = type.Name + " ";
#else
                var typeName = "";
#endif

                return IsEnabled != 1 ? $"{typeName}Component Enabled" : $"{typeName}Component Disabled";
            }

        }

        internal class EntityNameCommandView : EntityCommandView
        {
            public FixedString64Bytes Name;

            public EntityNameCommandView(ECBCommand commandType, int sortKey, int totalSize, Entity entity,
                int identityIndex, int batchCount, FixedString64Bytes name)
            {
                CommandType = commandType;
                SortKey = sortKey;
                TotalSizeInBytes = totalSize;
                Entity = entity;
                IdentityIndex = identityIndex;
                BatchCount = batchCount;
                Name = name;
            }

            public override string ToString()
            {
                return $"Set EntityName: {Name.ToString()}";
            }
        }

        internal class EntityMultipleComponentsCommandView : EntityCommandView
        {
            public ComponentTypeSet TypeSet;

            public EntityMultipleComponentsCommandView(ECBCommand commandType, int sortKey, int totalSize, Entity entity,
                int identityIndex, int batchCount, in ComponentTypeSet typeSet)
            {
                CommandType = commandType;
                SortKey = sortKey;
                TotalSizeInBytes = totalSize;
                Entity = entity;
                IdentityIndex = identityIndex;
                BatchCount = batchCount;
                TypeSet = typeSet;
            }

            public override string ToString()
            {
                switch (CommandType)
                {
                    case ECBCommand.AddMultipleComponents: return $"Add {TypeSet.Length} Components";
                    default: return $"Remove {TypeSet.Length} Components";
                }
            }
        }

        internal unsafe class EntityBufferCommandView : EntityCommandView
        {
            public TypeIndex ComponentTypeIndex;
            public int ComponentSize;
            // Must point to original buffer node in ECB, so that we can find the buffer data embedded after it.
            public BufferHeaderNode* BufferNode;

            public EntityBufferCommandView(ECBCommand commandType, int sortKey, int totalSize, Entity entity,
                int identityIndex, int batchCount, TypeIndex componentTypeIndex, int componentSize, BufferHeaderNode* bufferNode)
            {
                CommandType = commandType;
                SortKey = sortKey;
                TotalSizeInBytes = totalSize;
                Entity = entity;
                IdentityIndex = identityIndex;
                BatchCount = batchCount;
                ComponentTypeIndex = componentTypeIndex;
                ComponentSize = componentSize;
                BufferNode = bufferNode;
            }

            public override string ToString()
            {
#if !NET_DOTS
                var type = TypeManager.GetType(ComponentTypeIndex);
                var typeName = " " + type.Name;
#else
                var typeName = "";
#endif
                return CommandType == ECBCommand.AddBuffer || CommandType == ECBCommand.AddBufferWithEntityFixUp ? $"Add Entity Buffer{typeName}" : $"Set Entity Buffer{typeName}";
            }
        }

        internal class EntityManagedComponentCommandView : EntityCommandView
        {
            public TypeIndex ComponentTypeIndex;
            public EntityComponentGCNode GCNode;

            internal object GetBoxedObject()
            {
                if (GCNode.BoxedObject.IsAllocated)
                    return GCNode.BoxedObject.Target;
                return null;
            }

            public EntityManagedComponentCommandView(ECBCommand commandType, int sortKey, int totalSize, Entity entity,
                int identityIndex, int batchCount, TypeIndex componentTypeIndex, EntityComponentGCNode gcNode)
            {
                CommandType = commandType;
                SortKey = sortKey;
                TotalSizeInBytes = totalSize;
                Entity = entity;
                IdentityIndex = identityIndex;
                BatchCount = batchCount;
                ComponentTypeIndex = componentTypeIndex;
                GCNode = gcNode;
            }

            public override string ToString()
            {
#if !NET_DOTS
                var type = TypeManager.GetType(ComponentTypeIndex);
                var typeName = type.Name + " ";
#else
                var typeName = "";
#endif

                return CommandType  == ECBCommand.AddManagedComponentData ? $"Add {typeName}Component (Managed)" : $"Set {typeName}Component (Managed)";
            }
        }

        internal class EntityUnmanagedSharedComponentCommandView : EntityCommandView
        {
            public TypeIndex ComponentTypeIndex;
            public int HashCode;
            public object ComponentValue;


            public EntityUnmanagedSharedComponentCommandView(
                ECBCommand commandType,
                int sortKey,
                int totalSize,
                Entity entity,
                int identityIndex,
                int batchCount,
                TypeIndex componentTypeIndex,
                int hashCode,
                int componentSize,
                void* componentValue)
            {
                CommandType = commandType;
                SortKey = sortKey;
                TotalSizeInBytes = totalSize;
                Entity = entity;
                IdentityIndex = identityIndex;
                BatchCount = batchCount;
                ComponentTypeIndex = componentTypeIndex;
                HashCode = hashCode;

                ComponentValue = TypeManager.ConstructComponentFromBuffer(componentTypeIndex, componentValue);
            }

            public override string ToString()
            {
#if !NET_DOTS
                var type = TypeManager.GetType(ComponentTypeIndex);
                var typeName = type.Name + " ";
#else
                var typeName = "";
#endif

                return CommandType  == ECBCommand.AddUnmanagedSharedComponentData ? $"Add {typeName}UnmanagedSharedComponentData" : $"Set {typeName}UnmanagedSharedComponentData";
            }
        }

        internal class MultipleEntitiesComponentCommandView_WithUnmanagedSharedValue : MultipleEntitiesCommandView
        {
            public TypeIndex ComponentTypeIndex;
            public int ComponentSize;
            public object ComponentValue;

            public MultipleEntitiesComponentCommandView_WithUnmanagedSharedValue(
                ECBCommand commandType,
                int sortKey,
                int totalSize,
                EntityNode entities,
                bool skipDeferredEntityLookup,
                int entitiesCount,
                AllocatorManager.AllocatorHandle allocator,
                TypeIndex componentTypeIndex,
                int componentSize,
                byte* componentValue)
            {
                CommandType = commandType;
                SortKey = sortKey;
                TotalSizeInBytes = totalSize;
                Entities = entities;
                EntitiesCount = entitiesCount;
                Allocator = allocator;
                ComponentTypeIndex = componentTypeIndex;
                ComponentSize = componentSize;
                SkipDeferredEntityLookup = skipDeferredEntityLookup;
                if (ComponentSize > 0 && componentValue != null)
                    ComponentValue = TypeManager.ConstructComponentFromBuffer(componentTypeIndex, componentValue);
                else
                    ComponentValue = default;
            }

            public override string ToString()
            {
#if !NET_DOTS
                var type = TypeManager.GetType(ComponentTypeIndex);
                var typeName = type.Name + " ";
#else
                var typeName = "";
#endif
                return (CommandType == ECBCommand.AddUnmanagedSharedComponentValueForMultipleEntities)
                    ? $"Add {typeName}Unmanaged Shared Component to {EntitiesCount} Entities"
                    : $"Set {typeName}Unmanaged Shared Component from {EntitiesCount} Entities";
            }
        }

        internal class EntityQueryComponentCommandView_WithUnmanagedSharedValue : EntityQueryComponentCommandView
        {
            public int ComponentSize;
            public object ComponentValue;

            public EntityQueryComponentCommandView_WithUnmanagedSharedValue(
                ECBCommand commandType,
                int sortKey,
                int totalSize,
                EntityQueryImpl *queryImpl,
                TypeIndex componentTypeIndex,
                int componentSize,
                byte* componentValue)
            {
                CommandType = commandType;
                SortKey = sortKey;
                TotalSizeInBytes = totalSize;
                Query = queryImpl;
                ComponentTypeIndex = componentTypeIndex;
                ComponentSize = componentSize;
                if (ComponentSize > 0 && componentValue != null)
                    ComponentValue = TypeManager.ConstructComponentFromBuffer(componentTypeIndex, componentValue);
                else
                    ComponentValue = default;
            }

            public override string ToString()
            {
#if !NET_DOTS
                var type = TypeManager.GetType(ComponentTypeIndex);
                var typeName = type.Name + " ";
#else
                var typeName = "";
#endif
                return (CommandType == ECBCommand.AddUnmanagedSharedComponentValueForEntityQuery)
                    ? $"Add {typeName}Unmanaged Shared Component to EntityQuery"
                    : $"Set {typeName}Unmanaged Shared Component from EntityQuery";
            }
        }

        internal class EntitySharedComponentCommandView : EntityCommandView
        {
            public TypeIndex ComponentTypeIndex;
            public int HashCode;
            public EntityComponentGCNode GCNode;

            internal object GetBoxedObject()
            {
                if (GCNode.BoxedObject.IsAllocated)
                    return GCNode.BoxedObject.Target;
                return null;
            }

            public EntitySharedComponentCommandView(ECBCommand commandType, int sortKey, int totalSize, Entity entity,
                int identityIndex, int batchCount, TypeIndex componentTypeIndex, int hashCode, EntityComponentGCNode gcNode)
            {
                CommandType = commandType;
                SortKey = sortKey;
                TotalSizeInBytes = totalSize;
                Entity = entity;
                IdentityIndex = identityIndex;
                BatchCount = batchCount;
                ComponentTypeIndex = componentTypeIndex;
                HashCode = hashCode;
                GCNode = gcNode;
            }

            public override string ToString()
            {
#if !NET_DOTS
                var type = TypeManager.GetType(ComponentTypeIndex);
                var typeName = type.Name + " ";
#else
                var typeName = "";
#endif

                return CommandType  == ECBCommand.AddSharedComponentData ? $"Add {typeName}SharedComponentData" : $"Set {typeName}SharedComponentData";
            }
        }

        internal sealed class EntityCommandBufferDebugView
        {
#if !NET_DOTS
            private EntityCommandBuffer m_ecb;
            public EntityCommandBufferDebugView(EntityCommandBuffer ecb)
            {
                m_ecb = ecb;
            }

            public BasicCommandView ProcessDebugViewCommand(BasicCommand* header)
            {
                switch (header->CommandType)
                {
                    case ECBCommand.DestroyEntity:
                    case ECBCommand.InstantiateEntity:
                        var entityCommand = (EntityCommand*) header;
                        return new EntityCommandView(header->CommandType, header->SortKey, header->TotalSize,
                            entityCommand->Entity, entityCommand->IdentityIndex, entityCommand->BatchCount);

                    case ECBCommand.CreateEntity:
                        var createCommand = (CreateCommand*)header;
                        return new CreateCommandView(header->CommandType, header->SortKey, header->TotalSize,
                            createCommand->Archetype, createCommand->IdentityIndex, createCommand->BatchCount);

                    case ECBCommand.RemoveMultipleComponents:
                    case ECBCommand.AddMultipleComponents:
                        var entityMultipleComponentsCommand = (EntityMultipleComponentsCommand*) header;
                        return new EntityMultipleComponentsCommandView(header->CommandType, header->SortKey,
                            header->TotalSize, entityMultipleComponentsCommand->Header.Entity,
                            entityMultipleComponentsCommand->Header.IdentityIndex,
                            entityMultipleComponentsCommand->Header.BatchCount, entityMultipleComponentsCommand->TypeSet);

                    case ECBCommand.RemoveComponent:
                    case ECBCommand.AddComponent:
                    case ECBCommand.AddComponentWithEntityFixUp:
                    case ECBCommand.SetComponent:
                    case ECBCommand.SetComponentWithEntityFixUp:
                    case ECBCommand.AppendToBuffer:
                    case ECBCommand.AppendToBufferWithEntityFixUp:
                    case ECBCommand.ReplaceComponentLinkedEntityGroup:
                        var entityComponentCommand = (EntityComponentCommand*) header;
                        var data = header->CommandType != ECBCommand.RemoveComponent ? (byte*)(entityComponentCommand+1) : null;
                        return new EntityComponentCommandView(header->CommandType, header->SortKey,
                            header->TotalSize, entityComponentCommand->Header.Entity,
                            entityComponentCommand->Header.IdentityIndex, entityComponentCommand->Header.BatchCount,
                            entityComponentCommand->ComponentTypeIndex, entityComponentCommand->ComponentSize,
                            data);

                    case ECBCommand.SetEntityEnabled:
                        var setEntityEnabledCommand = (EntityEnabledCommand*) header;
                        return new EntityEnabledCommandView(header->CommandType, header->SortKey,
                            header->TotalSize, setEntityEnabledCommand->Header.Entity,
                            setEntityEnabledCommand->Header.IdentityIndex, setEntityEnabledCommand->Header.BatchCount,
                            setEntityEnabledCommand->IsEnabled);

                    case ECBCommand.SetComponentEnabled:
                        var setComponentEnabledCommand = (EntityComponentEnabledCommand*) header;
                        return new EntityComponentEnabledCommandView(header->CommandType, header->SortKey,
                            header->TotalSize, setComponentEnabledCommand->Header.Header.Entity,
                            setComponentEnabledCommand->Header.Header.IdentityIndex, setComponentEnabledCommand->Header.Header.BatchCount,
                            setComponentEnabledCommand->ComponentTypeIndex, setComponentEnabledCommand->Header.IsEnabled);

                    case ECBCommand.SetName:
                        var setNameCommand = (EntityNameCommand*) header;
                        return new EntityNameCommandView(header->CommandType, header->SortKey,
                            header->TotalSize, setNameCommand->Header.Entity, setNameCommand->Header.IdentityIndex,
                            setNameCommand->Header.BatchCount, setNameCommand->Name);

                    case ECBCommand.AddBuffer:
                    case ECBCommand.AddBufferWithEntityFixUp:
                    case ECBCommand.SetBuffer:
                    case ECBCommand.SetBufferWithEntityFixUp:
                        var entityBufferCommand = (EntityBufferCommand*) header;
                        return new EntityBufferCommandView(header->CommandType, header->SortKey,
                            header->TotalSize, entityBufferCommand->Header.Entity, entityBufferCommand->Header.IdentityIndex,
                            entityBufferCommand->Header.BatchCount, entityBufferCommand->ComponentTypeIndex,
                            entityBufferCommand->ComponentSize, &entityBufferCommand->BufferNode);

                    case ECBCommand.AddComponentForEntityQuery:
                    case ECBCommand.RemoveComponentForEntityQuery:
                        var entityQueryComponentCommand = (EntityQueryComponentCommand*)header;
                        return new EntityQueryComponentCommandView(
                            header->CommandType, header->SortKey, header->TotalSize,
                            entityQueryComponentCommand->Header.QueryImpl,
                            entityQueryComponentCommand->ComponentTypeIndex);

                    case ECBCommand.AddComponentForMultipleEntities:
                    case ECBCommand.RemoveComponentForMultipleEntities:
                        var multipleEntitiesComponentCommand = (MultipleEntitiesComponentCommand*) header;
                        var dataMultiple = header->CommandType != ECBCommand.RemoveComponentForMultipleEntities ? (byte*)(multipleEntitiesComponentCommand+1) : null;
                        return new MultipleEntitiesComponentCommandView(header->CommandType,
                            header->SortKey, header->TotalSize, multipleEntitiesComponentCommand->Header.Entities,
                            multipleEntitiesComponentCommand->Header.EntitiesCount,
                            multipleEntitiesComponentCommand->Header.SkipDeferredEntityLookup != 0 ? true : false,
                            multipleEntitiesComponentCommand->Header.Allocator.ToAllocator,
                            multipleEntitiesComponentCommand->ComponentTypeIndex,
                            multipleEntitiesComponentCommand->ComponentSize, dataMultiple);

                    case ECBCommand.AddMultipleComponentsForMultipleEntities:
                    case ECBCommand.RemoveMultipleComponentsForMultipleEntities:
                        var multipleEntitiesAndComponentsCommand = (MultipleEntitiesAndComponentsCommand*) header;
                        return new MultipleEntitiesAndComponentsCommandView(
                            header->CommandType, header->SortKey, header->TotalSize,
                            multipleEntitiesAndComponentsCommand->Header.Entities,
                            multipleEntitiesAndComponentsCommand->Header.EntitiesCount,
                            multipleEntitiesAndComponentsCommand->Header.SkipDeferredEntityLookup != 0 ? true : false,
                            multipleEntitiesAndComponentsCommand->Header.Allocator.ToAllocator,
                            multipleEntitiesAndComponentsCommand->TypeSet);

                    case ECBCommand.AddMultipleComponentsForEntityQuery:
                    case ECBCommand.RemoveMultipleComponentsForEntityQuery:
                        var entityQueryComponentTypeSetCommand = (EntityQueryComponentTypeSetCommand*)header;
                        return new EntityQueryComponentTypeSetCommandView(
                            header->CommandType, header->SortKey, header->TotalSize,
                            entityQueryComponentTypeSetCommand->Header.QueryImpl,
                            entityQueryComponentTypeSetCommand->TypeSet);

                    case ECBCommand.DestroyMultipleEntities:
                        var destroyMultipleEntitiesCommand = (MultipleEntitiesCommand*) header;
                        return new MultipleEntitiesCommandView(header->CommandType,
                            header->SortKey, header->TotalSize, destroyMultipleEntitiesCommand->Entities,
                            destroyMultipleEntitiesCommand->EntitiesCount,
                            destroyMultipleEntitiesCommand->SkipDeferredEntityLookup != 0 ? true : false,
                            destroyMultipleEntitiesCommand->Allocator.ToAllocator);

                    case ECBCommand.DestroyForEntityQuery:
                        var destroyEntityQueryCommand = (EntityQueryCommand*) header;
                        return new EntityQueryCommandView(header->CommandType, header->SortKey,
                            header->TotalSize, destroyEntityQueryCommand->QueryImpl);

                    case ECBCommand.AddComponentObjectForMultipleEntities:
                    case ECBCommand.SetComponentObjectForMultipleEntities:
                    case ECBCommand.AddSharedComponentWithValueForMultipleEntities:
                    case ECBCommand.SetSharedComponentValueForMultipleEntities:
                        var multipleEntitiesComponentCommandWithObject = (MultipleEntitiesComponentCommandWithObject*) header;
                        return new MultipleEntitiesComponentCommandWithObjectView(
                            header->CommandType, header->SortKey, header->TotalSize,
                            multipleEntitiesComponentCommandWithObject->Header.Entities,
                            multipleEntitiesComponentCommandWithObject->Header.EntitiesCount,
                            multipleEntitiesComponentCommandWithObject->Header.SkipDeferredEntityLookup != 0 ? true : false,
                            multipleEntitiesComponentCommandWithObject->Header.Allocator.ToAllocator,
                            multipleEntitiesComponentCommandWithObject->ComponentTypeIndex,
                            multipleEntitiesComponentCommandWithObject->HashCode, multipleEntitiesComponentCommandWithObject->GCNode);

                    case ECBCommand.AddSharedComponentWithValueForEntityQuery:
                    case ECBCommand.SetSharedComponentValueForEntityQuery:
                        var entityQueryComponentCommandWithObject = (EntityQueryComponentCommandWithObject*) header;
                        return new EntityQueryComponentCommandWithObjectView(
                            header->CommandType, header->SortKey, header->TotalSize,
                            entityQueryComponentCommandWithObject->Header.Header.QueryImpl,
                            entityQueryComponentCommandWithObject->Header.ComponentTypeIndex,
                            entityQueryComponentCommandWithObject->HashCode,
                            entityQueryComponentCommandWithObject->GCNode);

                    case ECBCommand.AddManagedComponentData:
                    case ECBCommand.SetManagedComponentData:
                        var entityManagedComponentCommand = (EntityManagedComponentCommand*) header;
                        return new EntityManagedComponentCommandView(header->CommandType,
                            header->SortKey, header->TotalSize, entityManagedComponentCommand->Header.Entity,
                            entityManagedComponentCommand->Header.IdentityIndex,
                            entityManagedComponentCommand->Header.BatchCount,
                            entityManagedComponentCommand->ComponentTypeIndex, entityManagedComponentCommand->GCNode);

                    case ECBCommand.AddSharedComponentData:
                    case ECBCommand.SetSharedComponentData:

                        var entitySharedComponentCommand = (EntitySharedComponentCommand*) header;
                        return new EntitySharedComponentCommandView(header->CommandType, header->SortKey,
                            header->TotalSize, entitySharedComponentCommand->Header.Entity,
                            entitySharedComponentCommand->Header.IdentityIndex,
                            entitySharedComponentCommand->Header.BatchCount, entitySharedComponentCommand->ComponentTypeIndex,
                            entitySharedComponentCommand->HashCode, entitySharedComponentCommand->GCNode);
                    case ECBCommand.AddUnmanagedSharedComponentData:
                    case ECBCommand.SetUnmanagedSharedComponentData:
                        var cmd = (EntityUnmanagedSharedComponentCommand*) header;
                        return new EntityUnmanagedSharedComponentCommandView(
                            header->CommandType,
                            header->SortKey,
                            header->TotalSize,
                            cmd->Header.Entity,
                            cmd->Header.IdentityIndex,
                            cmd->Header.BatchCount,
                            cmd->ComponentTypeIndex,
                            cmd->HashCode,
                            TypeManager.GetTypeInfo(cmd->ComponentTypeIndex).TypeSize,
                            cmd + 1);

                    case ECBCommand.AddUnmanagedSharedComponentValueForMultipleEntities:
                    case ECBCommand.SetUnmanagedSharedComponentValueForMultipleEntities:
                        var cmd2 = (MultipleEntitiesCommand_WithUnmanagedSharedComponent*) header;
                        var data2 = (byte*)(cmd2+1);
                        return new MultipleEntitiesComponentCommandView_WithUnmanagedSharedValue(
                            header->CommandType,
                            header->SortKey,
                            header->TotalSize,
                            cmd2->Header.Entities,
                            cmd2->Header.SkipDeferredEntityLookup != 0,
                            cmd2->Header.EntitiesCount,
                            cmd2->Header.Allocator.ToAllocator,
                            cmd2->ComponentTypeIndex,
                            cmd2->ComponentSize,
                            data2);

                    case ECBCommand.AddUnmanagedSharedComponentValueForEntityQuery:
                    case ECBCommand.SetUnmanagedSharedComponentValueForEntityQuery:
                        var entityQueryComponentCommandWithUnmanagedSharedComponent = (EntityQueryComponentCommandWithUnmanagedSharedComponent*) header;
                        var data3 = (byte*)(entityQueryComponentCommandWithUnmanagedSharedComponent+1);
                        return new EntityQueryComponentCommandView_WithUnmanagedSharedValue(
                            header->CommandType,
                            header->SortKey,
                            header->TotalSize,
                            entityQueryComponentCommandWithUnmanagedSharedComponent->Header.Header.QueryImpl,
                            entityQueryComponentCommandWithUnmanagedSharedComponent->Header.ComponentTypeIndex,
                            entityQueryComponentCommandWithUnmanagedSharedComponent->ComponentSize,
                            data3);

                    case ECBCommand.AddComponentLinkedEntityGroup:
                    case ECBCommand.SetComponentLinkedEntityGroup:
                        var maskCommand = (EntityQueryMaskCommand*) header;
                        var componentData = (byte*)(maskCommand+1);
                        return new EntityQueryMaskCommandView(header->CommandType,
                            header->SortKey,
                            header->TotalSize,
                            maskCommand->Header.Header.Entity,
                            maskCommand->Header.Header.IdentityIndex,
                            maskCommand->Header.Header.BatchCount,
                            maskCommand->Mask,
                            maskCommand->Header.ComponentTypeIndex,
                            maskCommand->Header.ComponentSize,
                            componentData);

                    default:
                    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                        throw new InvalidOperationException(
                            $"Invalid command type {(ECBCommand)header->CommandType} not recognized.");
#else
                        return default;
#endif
                    }
                }
            }

            public BasicCommandView[] Commands
            {
                get {
                    var walker = new EcbWalker<DebugViewProcessor>(default, ECBProcessorType.DebugViewProcessor);
                    walker.processor.Init(m_ecb.m_Data->m_Allocator.ToAllocator);
                    walker.WalkChains(m_ecb);

                    //Convert the unsafe native list of pointers to the commands to an array of command views
                    var commandViewArray = new BasicCommandView[walker.processor.commands.Length];

                    for (var i = 0; i < walker.processor.commands.Length; i++)
                    {
                        commandViewArray[i] = ProcessDebugViewCommand(walker.processor.commands[i]);
                    }

                    walker.processor.Cleanup();
                    return commandViewArray;
                }
            }
#endif
        }
    }
}
