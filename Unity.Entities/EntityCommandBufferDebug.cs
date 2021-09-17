using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{
    [DebuggerTypeProxy(typeof(EntityCommandBufferDebugView))]
    public unsafe partial struct EntityCommandBuffer
    {
        internal const int INITIAL_COMMANDS_CAPACITY = 8;
        internal struct DebugViewProcessor : IEcbProcessor
        {
            public UnsafePtrList<BasicCommand> commands;

            public void Init(Allocator allocator)
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

            public unsafe void AddComponentForMultipleEntities(BasicCommand* header)
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

            public unsafe void RemoveMultipleComponentsForMultipleEntities(BasicCommand* header)
            {
                commands.Add(header);
            }

            public unsafe void DestroyMultipleEntities(BasicCommand* header)
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

            public unsafe void SetSharedComponentValueForMultipleEntities(BasicCommand* header)
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

        internal class MultipleEntitiesCommandView : BasicCommandView
        {
            public EntityNode Entities;
            public int EntitiesCount;
            public bool SkipDeferredEntityLookup;
            public Allocator Allocator;

            public MultipleEntitiesCommandView()
            {
                Entities = new EntityNode();
                EntitiesCount = 0;
                Allocator = Allocator.Invalid;
                SkipDeferredEntityLookup = false;
            }

            public MultipleEntitiesCommandView(ECBCommand commandType, int sortKey, int totalSize, EntityNode entities, int entitiesCount,
                bool skipDeferredEntityLookup, Allocator allocator)
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
            public int ComponentTypeIndex;
            public int ComponentSize;
            public object ComponentValue;

            public MultipleEntitiesComponentCommandView(ECBCommand commandType, int sortKey, int totalSize,
                EntityNode entities, int entitiesCount, bool skipDeferredEntityLookup, Allocator allocator, int componentTypeIndex, int componentSize,
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
#if !NET_DOTS
                    ComponentValue = Activator.CreateInstance(TypeManager.GetType(componentTypeIndex));
                    var handle = GCHandle.Alloc(ComponentValue, GCHandleType.Pinned);
                    UnsafeUtility.MemCpy(handle.AddrOfPinnedObject().ToPointer(), componentValue, componentSize);
                    handle.Free();
#else
                    ComponentValue = default; // NET_DOTS does not support CreateInstance()
#endif
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
            public int ComponentTypeIndex;
            public int HashCode;
            public EntityComponentGCNode GCNode;

            internal object GetBoxedObject()
            {
                if (GCNode.BoxedObject.IsAllocated)
                    return GCNode.BoxedObject.Target;
                return null;
            }

            public MultipleEntitiesComponentCommandWithObjectView(ECBCommand commandType, int sortKey, int totalSize, EntityNode entities, int entitiesCount,
                bool skipDeferredEntityLookup, Allocator allocator, int componentTypeIndex, int hashCode, EntityComponentGCNode gcNode)
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

        internal class MultipleEntitiesAndComponentsCommandView : MultipleEntitiesCommandView
        {
            public ComponentTypes Types;

            public MultipleEntitiesAndComponentsCommandView(ECBCommand commandType, int sortKey, int totalSize, EntityNode entities, int entitiesCount,
                bool skipDeferredEntityLookup, Allocator allocator, ComponentTypes types)
            {
                CommandType = commandType;
                SortKey = sortKey;
                TotalSizeInBytes = totalSize;
                Entities = entities;
                EntitiesCount = entitiesCount;
                SkipDeferredEntityLookup = skipDeferredEntityLookup;
                Allocator = allocator;
                Types = types;
            }

            public override string ToString()
            {
                return CommandType == ECBCommand.AddMultipleComponentsForMultipleEntities ? $"Add {Types.Length} " +
                    $"Components to {EntitiesCount} Entities": $"Remove {Types.Length} Components from {EntitiesCount} Entities";
            }
        }

        internal class EntityComponentCommandView : EntityCommandView
        {
            public int ComponentTypeIndex;
            public int ComponentSize;
            public object ComponentValue;

            public EntityComponentCommandView()
            {
                ComponentTypeIndex = Int32.MinValue;
                ComponentSize = 0;
                ComponentValue = default;
            }

            public EntityComponentCommandView(ECBCommand commandType, int sortKey, int totalSize, Entity entity,
                int identityIndex, int batchCount, int componentTypeIndex, int componentSize, byte* componentValue)
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
#if !NET_DOTS
                    ComponentValue = Activator.CreateInstance(TypeManager.GetType(componentTypeIndex));
                    var handle = GCHandle.Alloc(ComponentValue, GCHandleType.Pinned);
                    UnsafeUtility.MemCpy(handle.AddrOfPinnedObject().ToPointer(), componentValue, componentSize);
                    handle.Free();
#else
                    ComponentValue = default; // NET_DOTS does not support CreateInstance()
#endif
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
                    default: throw new ArgumentException("Unknown Command");
                }
            }
        }

        internal class EntityComponentEnabledCommandView : EntityCommandView
        {
            public int ComponentTypeIndex;
            public byte IsEnabled;

            public EntityComponentEnabledCommandView(ECBCommand commandType, int sortKey, int totalSize, Entity entity, int identityIndex, int batchCount, int componentTypeIndex, byte isEnabled)
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
            public ComponentTypes Types;

            public EntityMultipleComponentsCommandView(ECBCommand commandType, int sortKey, int totalSize, Entity entity,
                int identityIndex, int batchCount, ComponentTypes types)
            {
                CommandType = commandType;
                SortKey = sortKey;
                TotalSizeInBytes = totalSize;
                Entity = entity;
                IdentityIndex = identityIndex;
                BatchCount = batchCount;
                Types = types;
            }

            public override string ToString()
            {
                return CommandType ==  ECBCommand.AddMultipleComponents ?  $"Add {Types.Length} Components" : $"Remove {Types.Length} Components";
            }
        }

        internal unsafe class EntityBufferCommandView : EntityCommandView
        {
            public int ComponentTypeIndex;
            public int ComponentSize;
            // Must point to original buffer node in ECB, so that we can find the buffer data embedded after it.
            public BufferHeaderNode* BufferNode;

            public EntityBufferCommandView(ECBCommand commandType, int sortKey, int totalSize, Entity entity,
                int identityIndex, int batchCount, int componentTypeIndex, int componentSize, BufferHeaderNode* bufferNode)
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
            public int ComponentTypeIndex;
            public EntityComponentGCNode GCNode;

            internal object GetBoxedObject()
            {
                if (GCNode.BoxedObject.IsAllocated)
                    return GCNode.BoxedObject.Target;
                return null;
            }

            public EntityManagedComponentCommandView(ECBCommand commandType, int sortKey, int totalSize, Entity entity,
                int identityIndex, int batchCount, int componentTypeIndex, EntityComponentGCNode gcNode)
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

        internal class EntitySharedComponentCommandView : EntityCommandView
        {
            public int ComponentTypeIndex;
            public int HashCode;
            public EntityComponentGCNode GCNode;

            internal object GetBoxedObject()
            {
                if (GCNode.BoxedObject.IsAllocated)
                    return GCNode.BoxedObject.Target;
                return null;
            }

            public EntitySharedComponentCommandView(ECBCommand commandType, int sortKey, int totalSize, Entity entity,
                int identityIndex, int batchCount, int componentTypeIndex, int hashCode, EntityComponentGCNode gcNode)
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
                            entityMultipleComponentsCommand->Header.BatchCount, entityMultipleComponentsCommand->Types);

                    case ECBCommand.RemoveComponent:
                    case ECBCommand.AddComponent:
                    case ECBCommand.AddComponentWithEntityFixUp:
                    case ECBCommand.SetComponent:
                    case ECBCommand.SetComponentWithEntityFixUp:
                    case ECBCommand.AppendToBuffer:
                    case ECBCommand.AppendToBufferWithEntityFixUp:
                        var entityComponentCommand = (EntityComponentCommand*) header;
                        var data = header->CommandType != ECBCommand.RemoveComponent ? (byte*)(entityComponentCommand+1) : null;
                        return new EntityComponentCommandView(header->CommandType, header->SortKey,
                            header->TotalSize, entityComponentCommand->Header.Entity,
                            entityComponentCommand->Header.IdentityIndex, entityComponentCommand->Header.BatchCount,
                            entityComponentCommand->ComponentTypeIndex, entityComponentCommand->ComponentSize,
                            data);

                    case ECBCommand.SetComponentEnabled:
                        var setComponentEnabledCommand = (EntityComponentEnabledCommand*) header;
                        return new EntityComponentEnabledCommandView(header->CommandType, header->SortKey,
                            header->TotalSize, setComponentEnabledCommand->Header.Entity,
                            setComponentEnabledCommand->Header.IdentityIndex, setComponentEnabledCommand->Header.BatchCount,
                            setComponentEnabledCommand->ComponentTypeIndex, setComponentEnabledCommand->IsEnabled);

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

                    case ECBCommand.AddComponentForMultipleEntities:
                    case ECBCommand.RemoveComponentForMultipleEntities:
                        var multipleEntitiesComponentCommand = (MultipleEntitiesComponentCommand*) header;
                        var dataMultiple = header->CommandType != ECBCommand.RemoveComponentForMultipleEntities ? (byte*)(multipleEntitiesComponentCommand+1) : null;
                        return new MultipleEntitiesComponentCommandView(header->CommandType,
                            header->SortKey, header->TotalSize, multipleEntitiesComponentCommand->Header.Entities,
                            multipleEntitiesComponentCommand->Header.EntitiesCount,
                            multipleEntitiesComponentCommand->Header.SkipDeferredEntityLookup != 0 ? true : false,
                            multipleEntitiesComponentCommand->Header.Allocator,
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
                            multipleEntitiesAndComponentsCommand->Header.Allocator,
                            multipleEntitiesAndComponentsCommand->Types);

                    case ECBCommand.DestroyMultipleEntities:
                        var destroyMultipleEntitiesCommand = (MultipleEntitiesCommand*) header;
                        return new MultipleEntitiesCommandView(header->CommandType,
                            header->SortKey, header->TotalSize, destroyMultipleEntitiesCommand->Entities,
                            destroyMultipleEntitiesCommand->EntitiesCount,
                            destroyMultipleEntitiesCommand->SkipDeferredEntityLookup != 0 ? true : false,
                            destroyMultipleEntitiesCommand->Allocator);

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
                            multipleEntitiesComponentCommandWithObject->Header.Allocator,
                            multipleEntitiesComponentCommandWithObject->ComponentTypeIndex,
                            multipleEntitiesComponentCommandWithObject->HashCode, multipleEntitiesComponentCommandWithObject->GCNode);

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
                    walker.processor.Init(m_ecb.m_Data->m_Allocator);
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
