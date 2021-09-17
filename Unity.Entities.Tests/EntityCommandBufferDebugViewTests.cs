using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities.Tests
{
#if !NET_DOTS // NET_DOTS does not support debug views
    unsafe class EntityCommandBufferDebugViewTests : ECSTestsFixture
    {
        [SetUp]
        public override void Setup()
        {
            base.Setup();
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
        }

        [Test]
        public void EntityCommandBufferDebugView_CreateEntity_ContainsExpectedData()
        {
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            var ent = ecb.CreateEntity();
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.CreateEntity, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(40, cmdView.TotalSizeInBytes);

            var createCmdView = cmdView as EntityCommandBuffer.CreateCommandView;
            Assert.IsFalse(createCmdView.EntityArchetype.Valid);
            Assert.AreEqual(ent.Index, createCmdView.EntityIdentityIndex);
            Assert.AreEqual(1, createCmdView.BatchCount);
            Assert.AreEqual("Create Entity",createCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_CreateEntityFromArchetype_ContainsExpectedData()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            var ent = ecb.CreateEntity(archetype);
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.CreateEntity, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(40, cmdView.TotalSizeInBytes);

            var createCmdView = cmdView as EntityCommandBuffer.CreateCommandView;
            Assert.AreEqual(archetype, createCmdView.EntityArchetype);
            Assert.AreEqual(ent.Index, createCmdView.EntityIdentityIndex);
            Assert.AreEqual(1, createCmdView.BatchCount);
            Assert.AreEqual("Create Entity",createCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_Instantiate_ContainsExpectedData()
        {
            var prefab = m_Manager.CreateEntity(typeof(EcsTestData), typeof(Prefab));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            var ent = ecb.Instantiate(prefab);
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.InstantiateEntity, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(28, cmdView.TotalSizeInBytes);

            var entityCmdView = cmdView as EntityCommandBuffer.EntityCommandView;
            Assert.AreEqual(prefab, entityCmdView.Entity);
            Assert.AreEqual(ent.Index, entityCmdView.IdentityIndex);
            Assert.AreEqual(1, entityCmdView.BatchCount);
            Assert.AreEqual("Instantiate Entity (count=1)",entityCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_InstantiateToArray_ContainsExpectedData()
        {
            var prefab = m_Manager.CreateEntity(typeof(EcsTestData), typeof(Prefab));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            using var entities = CollectionHelper.CreateNativeArray<Entity>(5, World.UpdateAllocator.ToAllocator);
            ecb.Instantiate(prefab, entities);
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.InstantiateEntity, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(28, cmdView.TotalSizeInBytes);

            var entityCmdView = cmdView as EntityCommandBuffer.EntityCommandView;
            Assert.AreEqual(prefab, entityCmdView.Entity);
            Assert.AreEqual(entities[0].Index, entityCmdView.IdentityIndex);
            Assert.AreEqual(entities.Length, entityCmdView.BatchCount);
            Assert.AreEqual("Instantiate Entity (count=5)",entityCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_DestroyEntity_ContainsExpectedData()
        {
            var ent = m_Manager.CreateEntity(typeof(EcsTestData));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            ecb.DestroyEntity(ent);
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.DestroyEntity, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(28, cmdView.TotalSizeInBytes);

            var entityCmdView = cmdView as EntityCommandBuffer.EntityCommandView;
            Assert.AreEqual(ent, entityCmdView.Entity);
            Assert.AreEqual(0, entityCmdView.IdentityIndex);
            Assert.AreEqual(1, entityCmdView.BatchCount);
            Assert.AreEqual("Destroy Entity",entityCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_AddBuffer_ContainsExpectedData()
        {
            var ent = m_Manager.CreateEntity(typeof(EcsTestData));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            var buf = ecb.AddBuffer<EcsIntElement>(ent);
            buf.Add(17);
            buf.Add(23);
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.AddBuffer, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(112, cmdView.TotalSizeInBytes);

            var bufferCmdView = cmdView as EntityCommandBuffer.EntityBufferCommandView;
            var typeInfo = TypeManager.GetTypeInfo<EcsIntElement>();
            Assert.AreEqual(typeInfo.TypeIndex, bufferCmdView.ComponentTypeIndex);
            Assert.AreEqual(typeInfo.SizeInChunk, bufferCmdView.ComponentSize);
            Assert.AreEqual(2, bufferCmdView.BufferNode->TempBuffer.Length);
            Assert.AreEqual(typeInfo.BufferCapacity, bufferCmdView.BufferNode->TempBuffer.Capacity);
            Assert.AreEqual("Add Entity Buffer EcsIntElement",bufferCmdView.ToString());


            var contents = (int*)BufferHeader.GetElementPointer(&bufferCmdView.BufferNode->TempBuffer);
            Assert.AreEqual(17, contents[0]);
            Assert.AreEqual(23, contents[1]);
        }

        [Test]
        public void EntityCommandBufferDebugView_AddBufferWithEntityFixup_ContainsExpectedData()
        {
            var ent = m_Manager.CreateEntity(typeof(EcsTestData));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            var ent2 = ecb.CreateEntity(); // need deferred entity to force fix-up
            var ent3 = ecb.CreateEntity(); // need deferred entity to force fix-up
            var buf = ecb.AddBuffer<EcsComplexEntityRefElement>(ent);
            buf.Add(new EcsComplexEntityRefElement { Dummy = 17, Entity = ent2 });
            buf.Add(new EcsComplexEntityRefElement { Dummy = 23, Entity = ent3 });
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(2, commands.Length); // the two create commands are batched

            var cmdView = commands[1];
            Assert.AreEqual(ECBCommand.AddBufferWithEntityFixUp, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(128, cmdView.TotalSizeInBytes);

            var bufferCmdView = cmdView as EntityCommandBuffer.EntityBufferCommandView;
            var typeInfo = TypeManager.GetTypeInfo<EcsComplexEntityRefElement>();
            Assert.AreEqual(typeInfo.TypeIndex, bufferCmdView.ComponentTypeIndex);
            Assert.AreEqual(typeInfo.SizeInChunk, bufferCmdView.ComponentSize);
            Assert.AreEqual(2, bufferCmdView.BufferNode->TempBuffer.Length);
            Assert.AreEqual(typeInfo.BufferCapacity, bufferCmdView.BufferNode->TempBuffer.Capacity);
            Assert.AreEqual("Add Entity Buffer EcsComplexEntityRefElement",bufferCmdView.ToString());

            var contents =
                (EcsComplexEntityRefElement*)BufferHeader.GetElementPointer(&bufferCmdView.BufferNode->TempBuffer);
            Assert.AreEqual(17, contents[0].Dummy);
            Assert.AreEqual(ent2, contents[0].Entity);
            Assert.AreEqual(23, contents[1].Dummy);
            Assert.AreEqual(ent3, contents[1].Entity);
        }

        [Test]
        public void EntityCommandBufferDebugView_SetBuffer_ContainsExpectedData()
        {
            var ent = m_Manager.CreateEntity(typeof(EcsIntElement));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            var buf = ecb.SetBuffer<EcsIntElement>(ent);
            buf.Add(17);
            buf.Add(23);
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.SetBuffer, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(112, cmdView.TotalSizeInBytes);

            var bufferCmdView = cmdView as EntityCommandBuffer.EntityBufferCommandView;
            var typeInfo = TypeManager.GetTypeInfo<EcsIntElement>();
            Assert.AreEqual(typeInfo.TypeIndex, bufferCmdView.ComponentTypeIndex);
            Assert.AreEqual(typeInfo.SizeInChunk, bufferCmdView.ComponentSize);
            Assert.AreEqual(2, bufferCmdView.BufferNode->TempBuffer.Length);
            Assert.AreEqual(typeInfo.BufferCapacity, bufferCmdView.BufferNode->TempBuffer.Capacity);
            Assert.AreEqual("Set Entity Buffer EcsIntElement",bufferCmdView.ToString());

            var contents = (int*)BufferHeader.GetElementPointer(&bufferCmdView.BufferNode->TempBuffer);
            Assert.AreEqual(17, contents[0]);
            Assert.AreEqual(23, contents[1]);
        }

        [Test]
        public void EntityCommandBufferDebugView_SetBufferWithEntityFixup_ContainsExpectedData()
        {
            var ent = m_Manager.CreateEntity(typeof(EcsComplexEntityRefElement));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            var ent2 = ecb.CreateEntity(); // need deferred entity to force fix-up
            var ent3 = ecb.CreateEntity(); // need deferred entity to force fix-up
            var buf = ecb.SetBuffer<EcsComplexEntityRefElement>(ent);
            buf.Add(new EcsComplexEntityRefElement { Dummy = 17, Entity = ent2 });
            buf.Add(new EcsComplexEntityRefElement { Dummy = 23, Entity = ent3 });
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(2, commands.Length); // the two create commands are batched

            var cmdView = commands[1];
            Assert.AreEqual(ECBCommand.SetBufferWithEntityFixUp, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(128, cmdView.TotalSizeInBytes);

            var bufferCmdView = cmdView as EntityCommandBuffer.EntityBufferCommandView;
            var typeInfo = TypeManager.GetTypeInfo<EcsComplexEntityRefElement>();
            Assert.AreEqual(typeInfo.TypeIndex, bufferCmdView.ComponentTypeIndex);
            Assert.AreEqual(typeInfo.SizeInChunk, bufferCmdView.ComponentSize);
            Assert.AreEqual(2, bufferCmdView.BufferNode->TempBuffer.Length);
            Assert.AreEqual(typeInfo.BufferCapacity, bufferCmdView.BufferNode->TempBuffer.Capacity);
            Assert.AreEqual("Set Entity Buffer EcsComplexEntityRefElement",bufferCmdView.ToString());

            var contents =
                (EcsComplexEntityRefElement*)BufferHeader.GetElementPointer(&bufferCmdView.BufferNode->TempBuffer);
            Assert.AreEqual(17, contents[0].Dummy);
            Assert.AreEqual(ent2, contents[0].Entity);
            Assert.AreEqual(23, contents[1].Dummy);
            Assert.AreEqual(ent3, contents[1].Entity);
        }

        [Test]
        public void EntityCommandBufferDebugView_AppendToBuffer_ContainsExpectedData()
        {
            var ent = m_Manager.CreateEntity(typeof(EcsIntElement));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            ecb.AppendToBuffer(ent, new EcsIntElement { Value = 17 });
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.AppendToBuffer, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(40, cmdView.TotalSizeInBytes);

            var entityCmdView = cmdView as EntityCommandBuffer.EntityCommandView;
            Assert.AreEqual(ent, entityCmdView.Entity);
            Assert.AreEqual(0, entityCmdView.IdentityIndex);
            Assert.AreEqual(1, entityCmdView.BatchCount);

            var componentCmdView = cmdView as EntityCommandBuffer.EntityComponentCommandView;
            var typeInfo = TypeManager.GetTypeInfo<EcsIntElement>();
            Assert.AreEqual(typeInfo.TypeIndex, componentCmdView.ComponentTypeIndex);
            Assert.AreEqual(sizeof(EcsIntElement), componentCmdView.ComponentSize);
            var actualValue = (EcsIntElement)componentCmdView.ComponentValue;
            Assert.AreEqual(17, actualValue.Value);
            Assert.AreEqual("Append EcsIntElement BufferElementData",componentCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_AppendToBufferWithEntityFixup_ContainsExpectedData()
        {
            var ent = m_Manager.CreateEntity(typeof(EcsComplexEntityRefElement));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            var ent2 = ecb.CreateEntity(); // need deferred entity to force fix-up
            ecb.AppendToBuffer(ent, new EcsComplexEntityRefElement { Dummy = 17, Entity = ent2 });
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(2, commands.Length);

            var cmdView = commands[1];
            Assert.AreEqual(ECBCommand.AppendToBufferWithEntityFixUp, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(48, cmdView.TotalSizeInBytes);

            var entityCmdView = cmdView as EntityCommandBuffer.EntityCommandView;
            Assert.AreEqual(ent, entityCmdView.Entity);
            Assert.AreEqual(0, entityCmdView.IdentityIndex);
            Assert.AreEqual(1, entityCmdView.BatchCount);

            var componentCmdView = cmdView as EntityCommandBuffer.EntityComponentCommandView;
            var typeInfo = TypeManager.GetTypeInfo<EcsComplexEntityRefElement>();
            Assert.AreEqual(typeInfo.TypeIndex, componentCmdView.ComponentTypeIndex);
            Assert.AreEqual(sizeof(EcsComplexEntityRefElement), componentCmdView.ComponentSize);
            var actualValue = (EcsComplexEntityRefElement)componentCmdView.ComponentValue;
            Assert.AreEqual(17, actualValue.Dummy);
            Assert.AreEqual(ent2, actualValue.Entity);
            Assert.AreEqual("Append EcsComplexEntityRefElement BufferElementData",componentCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_AddComponentWithValue_ContainsExpectedData()
        {
            var ent = m_Manager.CreateEntity();
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            var value = new EcsTestData { value = 17 };
            ecb.AddComponent(ent, value);
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.AddComponent, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(40, cmdView.TotalSizeInBytes);

            var entityCmdView = cmdView as EntityCommandBuffer.EntityCommandView;
            Assert.AreEqual(ent, entityCmdView.Entity);
            Assert.AreEqual(0, entityCmdView.IdentityIndex);
            Assert.AreEqual(1, entityCmdView.BatchCount);

            var componentCmdView = cmdView as EntityCommandBuffer.EntityComponentCommandView;
            Assert.AreEqual(TypeManager.GetTypeIndex<EcsTestData>(), componentCmdView.ComponentTypeIndex);
            Assert.AreEqual(sizeof(EcsTestData), componentCmdView.ComponentSize);
            Assert.AreEqual(value, (EcsTestData)componentCmdView.ComponentValue);
            Assert.AreEqual("Add EcsTestData Component",componentCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_AddComponentWithEntityFixup_ContainsExpectedData()
        {
            var ent = m_Manager.CreateEntity();
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            var ent2 = ecb.CreateEntity(); // need deferred entity to force fix-up
            var value = new EcsTestDataEntity { value0 = 17, value1 = ent2 };
            ecb.AddComponent(ent, value);
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(2, commands.Length);

            var cmdView = commands[1];
            Assert.AreEqual(ECBCommand.AddComponentWithEntityFixUp, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(48, cmdView.TotalSizeInBytes);

            var entityCmdView = cmdView as EntityCommandBuffer.EntityCommandView;
            Assert.AreEqual(ent, entityCmdView.Entity);
            Assert.AreEqual(0, entityCmdView.IdentityIndex);
            Assert.AreEqual(1, entityCmdView.BatchCount);

            var componentCmdView = cmdView as EntityCommandBuffer.EntityComponentCommandView;
            Assert.AreEqual(TypeManager.GetTypeIndex<EcsTestDataEntity>(), componentCmdView.ComponentTypeIndex);
            Assert.AreEqual(sizeof(EcsTestDataEntity), componentCmdView.ComponentSize);
            var actualValue = (EcsTestDataEntity)componentCmdView.ComponentValue;
            Assert.AreEqual(value.value0, actualValue.value0);
            Assert.AreEqual(value.value1, actualValue.value1);
            Assert.AreEqual("Add EcsTestDataEntity Component",componentCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_AddComponent_ContainsExpectedData()
        {
            var ent = m_Manager.CreateEntity();
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            ecb.AddComponent<EcsTestData>(ent);
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.AddComponent, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(40, cmdView.TotalSizeInBytes);

            var entityCmdView = cmdView as EntityCommandBuffer.EntityCommandView;
            Assert.AreEqual(ent, entityCmdView.Entity);
            Assert.AreEqual(0, entityCmdView.IdentityIndex);
            Assert.AreEqual(1, entityCmdView.BatchCount);

            var componentCmdView = cmdView as EntityCommandBuffer.EntityComponentCommandView;
            Assert.AreEqual(TypeManager.GetTypeIndex<EcsTestData>(), componentCmdView.ComponentTypeIndex);
            Assert.AreEqual(0, componentCmdView.ComponentSize);
            Assert.IsNull(componentCmdView.ComponentValue);
            Assert.AreEqual("Add EcsTestData Component",componentCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_AddComponentType_ContainsExpectedData()
        {
            var ent = m_Manager.CreateEntity();
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            ecb.AddComponent(ent, ComponentType.ReadWrite<EcsTestData>());
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.AddComponent, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(40, cmdView.TotalSizeInBytes);

            var entityCmdView = cmdView as EntityCommandBuffer.EntityCommandView;
            Assert.AreEqual(ent, entityCmdView.Entity);
            Assert.AreEqual(0, entityCmdView.IdentityIndex);
            Assert.AreEqual(1, entityCmdView.BatchCount);

            var componentCmdView = cmdView as EntityCommandBuffer.EntityComponentCommandView;
            Assert.AreEqual(TypeManager.GetTypeIndex<EcsTestData>(), componentCmdView.ComponentTypeIndex);
            Assert.AreEqual(0, componentCmdView.ComponentSize);
            Assert.IsNull(componentCmdView.ComponentValue);
            Assert.AreEqual("Add EcsTestData Component",componentCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_AddComponentTypes_ContainsExpectedData()
        {
            var ent = m_Manager.CreateEntity();
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            var componentTypes = new ComponentTypes(typeof(EcsTestData),
                typeof(EcsTestData2), typeof(EcsTestData3));
            ecb.AddComponent(ent, componentTypes);
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.AddMultipleComponents, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(104, cmdView.TotalSizeInBytes);

            var entityCmdView = cmdView as EntityCommandBuffer.EntityCommandView;
            Assert.AreEqual(ent, entityCmdView.Entity);
            Assert.AreEqual(0, entityCmdView.IdentityIndex);
            Assert.AreEqual(1, entityCmdView.BatchCount);

            var multiComponentCmdView = cmdView as EntityCommandBuffer.EntityMultipleComponentsCommandView;
            Assert.AreEqual(componentTypes.Length, multiComponentCmdView.Types.Length);
            for (int i = 0; i < componentTypes.Length; ++i)
            {
                Assert.AreEqual(componentTypes.GetTypeIndex(i), multiComponentCmdView.Types.GetTypeIndex(i));
            }

            Assert.AreEqual("Add 3 Components",multiComponentCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_SetComponentWithValue_ContainsExpectedData()
        {
            var ent = m_Manager.CreateEntity(typeof(EcsTestData));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            var value = new EcsTestData { value = 17 };
            ecb.SetComponent(ent, value);
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.SetComponent, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(40, cmdView.TotalSizeInBytes);

            var entityCmdView = cmdView as EntityCommandBuffer.EntityCommandView;
            Assert.AreEqual(ent, entityCmdView.Entity);
            Assert.AreEqual(0, entityCmdView.IdentityIndex);
            Assert.AreEqual(1, entityCmdView.BatchCount);

            var componentCmdView = cmdView as EntityCommandBuffer.EntityComponentCommandView;
            Assert.AreEqual(TypeManager.GetTypeIndex<EcsTestData>(), componentCmdView.ComponentTypeIndex);
            Assert.AreEqual(sizeof(EcsTestData), componentCmdView.ComponentSize);
            Assert.AreEqual(value, (EcsTestData)componentCmdView.ComponentValue);
            Assert.AreEqual("Set EcsTestData Component",componentCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_SetComponentWithEntityFixup_ContainsExpectedData()
        {
            var ent = m_Manager.CreateEntity(typeof(EcsTestDataEntity));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            var ent2 = ecb.CreateEntity(); // need deferred entity to force fix-up
            var value = new EcsTestDataEntity { value0 = 17, value1 = ent2 };
            ecb.SetComponent(ent, value);
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(2, commands.Length);

            var cmdView = commands[1];
            Assert.AreEqual(ECBCommand.SetComponentWithEntityFixUp, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(48, cmdView.TotalSizeInBytes);

            var entityCmdView = cmdView as EntityCommandBuffer.EntityCommandView;
            Assert.AreEqual(ent, entityCmdView.Entity);
            Assert.AreEqual(0, entityCmdView.IdentityIndex);
            Assert.AreEqual(1, entityCmdView.BatchCount);

            var componentCmdView = cmdView as EntityCommandBuffer.EntityComponentCommandView;
            Assert.AreEqual(TypeManager.GetTypeIndex<EcsTestDataEntity>(), componentCmdView.ComponentTypeIndex);
            Assert.AreEqual(sizeof(EcsTestDataEntity), componentCmdView.ComponentSize);
            var actualValue = (EcsTestDataEntity)componentCmdView.ComponentValue;
            Assert.AreEqual(value.value0, actualValue.value0);
            Assert.AreEqual(value.value1, actualValue.value1);
            Assert.AreEqual("Set EcsTestDataEntity Component",componentCmdView.ToString());
        }

#if !DOTS_DISABLE_DEBUG_NAMES
        [Test]
        public void EntityCommandBufferDebugView_SetName_ContainsExpectedData()
        {
            var ent = m_Manager.CreateEntity(typeof(EcsTestData));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            var name = new FixedString64Bytes("Test Name");
            ecb.SetName(ent, name);
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.SetName, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(96, cmdView.TotalSizeInBytes);

            var entityCmdView = cmdView as EntityCommandBuffer.EntityCommandView;
            Assert.AreEqual(ent, entityCmdView.Entity);
            Assert.AreEqual(0, entityCmdView.IdentityIndex);
            Assert.AreEqual(1, entityCmdView.BatchCount);

            var nameCmdView = cmdView as EntityCommandBuffer.EntityNameCommandView;
            Assert.AreEqual(name, nameCmdView.Name);
            Assert.AreEqual("Set EntityName: Test Name",nameCmdView.ToString());
        }
#endif

        [Test]
        public void EntityCommandBufferDebugView_RemoveComponent_ContainsExpectedData()
        {
            var ent = m_Manager.CreateEntity(typeof(EcsTestData));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            ecb.RemoveComponent<EcsTestData>(ent);
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.RemoveComponent, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(40, cmdView.TotalSizeInBytes);

            var entityCmdView = cmdView as EntityCommandBuffer.EntityCommandView;
            Assert.AreEqual(ent, entityCmdView.Entity);
            Assert.AreEqual(0, entityCmdView.IdentityIndex);
            Assert.AreEqual(1, entityCmdView.BatchCount);

            var componentCmdView = cmdView as EntityCommandBuffer.EntityComponentCommandView;
            Assert.AreEqual(TypeManager.GetTypeIndex<EcsTestData>(), componentCmdView.ComponentTypeIndex);
            Assert.AreEqual(0, componentCmdView.ComponentSize);
            Assert.IsNull(componentCmdView.ComponentValue);
            Assert.AreEqual("Remove EcsTestData Component",componentCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_RemoveComponentType_ContainsExpectedData()
        {
            var ent = m_Manager.CreateEntity(typeof(EcsTestData));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            ecb.RemoveComponent(ent, ComponentType.ReadWrite<EcsTestData>());
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.RemoveComponent, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(40, cmdView.TotalSizeInBytes);

            var entityCmdView = cmdView as EntityCommandBuffer.EntityCommandView;
            Assert.AreEqual(ent, entityCmdView.Entity);
            Assert.AreEqual(0, entityCmdView.IdentityIndex);
            Assert.AreEqual(1, entityCmdView.BatchCount);

            var componentCmdView = cmdView as EntityCommandBuffer.EntityComponentCommandView;
            Assert.AreEqual(TypeManager.GetTypeIndex<EcsTestData>(), componentCmdView.ComponentTypeIndex);
            Assert.AreEqual(0, componentCmdView.ComponentSize);
            Assert.IsNull(componentCmdView.ComponentValue);
            Assert.AreEqual("Remove EcsTestData Component",componentCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_RemoveComponentTypes_ContainsExpectedData()
        {
            var componentTypes = new ComponentTypes(typeof(EcsTestData),
                typeof(EcsTestData2), typeof(EcsTestData3));
            var ent = m_Manager.CreateEntity();
            m_Manager.AddComponents(ent, componentTypes);
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            ecb.RemoveComponent(ent, componentTypes);
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.RemoveMultipleComponents, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(104, cmdView.TotalSizeInBytes);

            var entityCmdView = cmdView as EntityCommandBuffer.EntityCommandView;
            Assert.AreEqual(ent, entityCmdView.Entity);
            Assert.AreEqual(0, entityCmdView.IdentityIndex);
            Assert.AreEqual(1, entityCmdView.BatchCount);

            var multiComponentCmdView = cmdView as EntityCommandBuffer.EntityMultipleComponentsCommandView;
            Assert.AreEqual(componentTypes.Length, multiComponentCmdView.Types.Length);
            for (int i = 0; i < componentTypes.Length; ++i)
            {
                Assert.AreEqual(componentTypes.GetTypeIndex(i), multiComponentCmdView.Types.GetTypeIndex(i));
            }
            Assert.AreEqual("Remove 3 Components",multiComponentCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_AddComponentForEntityQuery_ContainsExpectedData()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            int entityCount = 10;
            using var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.TempJob);

            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            ecb.AddComponentForEntityQuery<EcsTestData2>(query);
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.AddComponentForMultipleEntities, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(56, cmdView.TotalSizeInBytes);

            var multiEntityCmdView = cmdView as EntityCommandBuffer.MultipleEntitiesCommandView;
            Assert.AreEqual(ecb.m_Data->m_Allocator, multiEntityCmdView.Allocator);
            Assert.AreEqual(entityCount, multiEntityCmdView.EntitiesCount);
            Assert.IsTrue(multiEntityCmdView.SkipDeferredEntityLookup);
            var actualEntities =
                NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Entity>(multiEntityCmdView.Entities.Ptr,
                    entityCount, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref actualEntities,
                AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            CollectionAssert.AreEquivalent(entities.ToArray(), actualEntities.ToArray());

            var multiEntityComponentCmdView = cmdView as EntityCommandBuffer.MultipleEntitiesComponentCommandView;
            Assert.AreEqual(TypeManager.GetTypeIndex<EcsTestData2>(), multiEntityComponentCmdView.ComponentTypeIndex);
            Assert.AreEqual(0, multiEntityComponentCmdView.ComponentSize);
            Assertions.Assert.IsNull(multiEntityComponentCmdView.ComponentValue);
            Assert.AreEqual("Add EcsTestData2 Component to 10 Entities",multiEntityCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_AddComponentTypeForEntityQuery_ContainsExpectedData()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            int entityCount = 10;
            using var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.TempJob);

            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            ecb.AddComponentForEntityQuery(query, ComponentType.ReadWrite<EcsTestData2>());
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.AddComponentForMultipleEntities, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(56, cmdView.TotalSizeInBytes);

            var multiEntityCmdView = cmdView as EntityCommandBuffer.MultipleEntitiesCommandView;
            Assert.AreEqual(ecb.m_Data->m_Allocator, multiEntityCmdView.Allocator);
            Assert.AreEqual(entityCount, multiEntityCmdView.EntitiesCount);
            Assert.IsTrue(multiEntityCmdView.SkipDeferredEntityLookup);
            var actualEntities =
                NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Entity>(multiEntityCmdView.Entities.Ptr,
                    entityCount, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref actualEntities,
                AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            CollectionAssert.AreEquivalent(entities.ToArray(), actualEntities.ToArray());

            var multiEntityComponentCmdView = cmdView as EntityCommandBuffer.MultipleEntitiesComponentCommandView;
            Assert.AreEqual(TypeManager.GetTypeIndex<EcsTestData2>(), multiEntityComponentCmdView.ComponentTypeIndex);
            Assert.AreEqual(0, multiEntityComponentCmdView.ComponentSize);
            Assertions.Assert.IsNull(multiEntityComponentCmdView.ComponentValue);
            Assert.AreEqual("Add EcsTestData2 Component to 10 Entities",multiEntityCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_AddComponentForEntityQueryWithValue_ContainsExpectedData()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            int entityCount = 10;
            using var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.TempJob);

            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            ecb.AddComponentForEntityQuery(query, new EcsTestData2 { value0 = 17, value1 = 23 });
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.AddComponentForMultipleEntities, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(64, cmdView.TotalSizeInBytes);

            var multiEntityCmdView = cmdView as EntityCommandBuffer.MultipleEntitiesComponentCommandView;
            Assert.AreEqual(ecb.m_Data->m_Allocator, multiEntityCmdView.Allocator);
            Assert.AreEqual(entityCount, multiEntityCmdView.EntitiesCount);
            Assert.IsTrue(multiEntityCmdView.SkipDeferredEntityLookup);
            var actualEntities =
                NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Entity>(multiEntityCmdView.Entities.Ptr,
                    entityCount, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref actualEntities,
                AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            CollectionAssert.AreEquivalent(entities.ToArray(), actualEntities.ToArray());

            var multiEntityComponentCmdView = cmdView as EntityCommandBuffer.MultipleEntitiesComponentCommandView;
            Assert.AreEqual(TypeManager.GetTypeIndex<EcsTestData2>(), multiEntityComponentCmdView.ComponentTypeIndex);
            Assert.AreEqual(sizeof(EcsTestData2), multiEntityComponentCmdView.ComponentSize);
            var actualValue = (EcsTestData2)multiEntityComponentCmdView.ComponentValue;
            Assert.AreEqual(17, actualValue.value0);
            Assert.AreEqual(23, actualValue.value1);
            Assert.AreEqual("Add EcsTestData2 Component to 10 Entities",multiEntityCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_AddComponentTypesForEntityQuery_ContainsExpectedData()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var componentTypes = new ComponentTypes(typeof(EcsTestData2),
                typeof(EcsTestData3), typeof(EcsTestData4));
            int entityCount = 10;
            using var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.TempJob);

            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            ecb.AddComponentForEntityQuery(query, componentTypes);
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.AddMultipleComponentsForMultipleEntities, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(120, cmdView.TotalSizeInBytes);

            var multiEntityCmdView = cmdView as EntityCommandBuffer.MultipleEntitiesCommandView;
            Assert.AreEqual(ecb.m_Data->m_Allocator, multiEntityCmdView.Allocator);
            Assert.AreEqual(entityCount, multiEntityCmdView.EntitiesCount);
            Assert.IsTrue(multiEntityCmdView.SkipDeferredEntityLookup);
            var actualEntities =
                NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Entity>(multiEntityCmdView.Entities.Ptr,
                    entityCount, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref actualEntities,
                AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            CollectionAssert.AreEquivalent(entities.ToArray(), actualEntities.ToArray());

            var multiEntityAndComponentsCmdView =
                cmdView as EntityCommandBuffer.MultipleEntitiesAndComponentsCommandView;
            Assert.AreEqual(componentTypes.Length, multiEntityAndComponentsCmdView.Types.Length);
            for (int i = 0; i < componentTypes.Length; ++i)
            {
                Assert.AreEqual(componentTypes.GetTypeIndex(i), multiEntityAndComponentsCmdView.Types.GetTypeIndex(i));
            }

            Assert.AreEqual("Add 3 Components to 10 Entities",multiEntityCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_AddSharedComponentForEntityQuery_ContainsExpectedData()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            int entityCount = 10;
            using var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.TempJob);

            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            ecb.AddSharedComponentForEntityQuery(query, new EcsTestSharedComp { value = 17 });
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.AddSharedComponentWithValueForMultipleEntities, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(72, cmdView.TotalSizeInBytes);

            var multiEntityCmdView = cmdView as EntityCommandBuffer.MultipleEntitiesCommandView;
            Assert.AreEqual(ecb.m_Data->m_Allocator, multiEntityCmdView.Allocator);
            Assert.AreEqual(entityCount, multiEntityCmdView.EntitiesCount);
            Assert.IsTrue(multiEntityCmdView.SkipDeferredEntityLookup);
            var actualEntities =
                NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Entity>(multiEntityCmdView.Entities.Ptr,
                    entityCount, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref actualEntities,
                AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            CollectionAssert.AreEquivalent(entities.ToArray(), actualEntities.ToArray());

            var multiEntityComponentObjectCmdView =
                cmdView as EntityCommandBuffer.MultipleEntitiesComponentCommandWithObjectView;
            Assert.AreEqual(TypeManager.GetTypeIndex<EcsTestSharedComp>(),
                multiEntityComponentObjectCmdView.ComponentTypeIndex);
            //Assert.AreEqual(760342523, multiEntityComponentObjectCmdView.HashCode); // hash value not consistent
            var actualValue = (EcsTestSharedComp)multiEntityComponentObjectCmdView.GetBoxedObject();
            Assert.AreEqual(17, actualValue.value);

            Assert.AreEqual("Add EcsTestSharedComp Component to 10 Entities",multiEntityCmdView.ToString());
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        [Test]
        public void EntityCommandBufferDebugView_AddComponentObjectForEntityQuery_ContainsExpectedData()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            int entityCount = 10;
            using var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.TempJob);

            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            const string stringValue = "TestValue";
            ecb.AddComponentObjectForEntityQuery(query, new EcsTestManagedComponent { value = stringValue });
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.AddComponentObjectForMultipleEntities, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(72, cmdView.TotalSizeInBytes);

            var multiEntityCmdView = cmdView as EntityCommandBuffer.MultipleEntitiesCommandView;
            Assert.AreEqual(ecb.m_Data->m_Allocator, multiEntityCmdView.Allocator);
            Assert.AreEqual(entityCount, multiEntityCmdView.EntitiesCount);
            Assert.IsTrue(multiEntityCmdView.SkipDeferredEntityLookup);
            var actualEntities =
                NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Entity>(multiEntityCmdView.Entities.Ptr,
                    entityCount, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref actualEntities,
                AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            CollectionAssert.AreEquivalent(entities.ToArray(), actualEntities.ToArray());

            var multiEntityComponentObjectCmdView =
                cmdView as EntityCommandBuffer.MultipleEntitiesComponentCommandWithObjectView;
            Assert.AreEqual(TypeManager.GetTypeIndex<EcsTestManagedComponent>(),
                multiEntityComponentObjectCmdView.ComponentTypeIndex);
            //Assert.AreEqual(1029733109, multiEntityComponentObjectCmdView.HashCode); // hash value not consistent
            var actualValue = (EcsTestManagedComponent)multiEntityComponentObjectCmdView.GetBoxedObject();
            Assert.AreEqual(stringValue, actualValue.value);
            Assert.AreEqual("Add EcsTestManagedComponent Component to 10 Entities",multiEntityCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_SetComponentObjectForEntityQuery_ContainsExpectedData()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestManagedComponent));
            int entityCount = 10;
            using var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.TempJob);

            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            const string stringValue = "TestValue";
            ecb.SetComponentObjectForEntityQuery(query, new EcsTestManagedComponent { value = stringValue });
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.SetComponentObjectForMultipleEntities, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(72, cmdView.TotalSizeInBytes);

            var multiEntityCmdView = cmdView as EntityCommandBuffer.MultipleEntitiesCommandView;
            Assert.AreEqual(ecb.m_Data->m_Allocator, multiEntityCmdView.Allocator);
            Assert.AreEqual(entityCount, multiEntityCmdView.EntitiesCount);
            Assert.IsTrue(multiEntityCmdView.SkipDeferredEntityLookup);
            var actualEntities =
                NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Entity>(multiEntityCmdView.Entities.Ptr,
                    entityCount, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref actualEntities,
                AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            CollectionAssert.AreEquivalent(entities.ToArray(), actualEntities.ToArray());

            var multiEntityComponentObjectCmdView =
                cmdView as EntityCommandBuffer.MultipleEntitiesComponentCommandWithObjectView;
            Assert.AreEqual(TypeManager.GetTypeIndex<EcsTestManagedComponent>(),
                multiEntityComponentObjectCmdView.ComponentTypeIndex);
            //Assert.AreEqual(1029733109, multiEntityComponentObjectCmdView.HashCode); // hash value not consistent
            var actualValue = (EcsTestManagedComponent)multiEntityComponentObjectCmdView.GetBoxedObject();
            Assert.AreEqual(stringValue, actualValue.value);
            Assert.AreEqual("Set EcsTestManagedComponent Component to 10 Entities",multiEntityCmdView.ToString());
        }
#endif

        [Test]
        public void EntityCommandBufferDebugView_SetSharedComponentForEntityQuery_ContainsExpectedData()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            int entityCount = 10;
            using var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.TempJob);

            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            ecb.SetSharedComponentForEntityQuery(query, new EcsTestSharedComp { value = 17 });
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.SetSharedComponentValueForMultipleEntities, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(72, cmdView.TotalSizeInBytes);

            var multiEntityCmdView = cmdView as EntityCommandBuffer.MultipleEntitiesCommandView;
            Assert.AreEqual(ecb.m_Data->m_Allocator, multiEntityCmdView.Allocator);
            Assert.AreEqual(entityCount, multiEntityCmdView.EntitiesCount);
            Assert.IsTrue(multiEntityCmdView.SkipDeferredEntityLookup);
            var actualEntities =
                NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Entity>(multiEntityCmdView.Entities.Ptr,
                    entityCount, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref actualEntities,
                AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            CollectionAssert.AreEquivalent(entities.ToArray(), actualEntities.ToArray());

            var multiEntityComponentObjectCmdView =
                cmdView as EntityCommandBuffer.MultipleEntitiesComponentCommandWithObjectView;
            Assert.AreEqual(TypeManager.GetTypeIndex<EcsTestSharedComp>(),
                multiEntityComponentObjectCmdView.ComponentTypeIndex);
            //Assert.AreEqual(760342523, multiEntityComponentObjectCmdView.HashCode); // hash value not consistent
            var actualValue = (EcsTestSharedComp)multiEntityComponentObjectCmdView.GetBoxedObject();
            Assert.AreEqual(17, actualValue.value);
            Assert.AreEqual("Set EcsTestSharedComp Component to 10 Entities",multiEntityCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_RemoveComponentTypeForEntityQuery_ContainsExpectedData()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            int entityCount = 10;
            using var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.TempJob);

            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            ecb.RemoveComponentForEntityQuery(query, ComponentType.ReadWrite<EcsTestData>());
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.RemoveComponentForMultipleEntities, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(56, cmdView.TotalSizeInBytes);

            var multiEntityCmdView = cmdView as EntityCommandBuffer.MultipleEntitiesCommandView;
            Assert.AreEqual(ecb.m_Data->m_Allocator, multiEntityCmdView.Allocator);
            Assert.AreEqual(entityCount, multiEntityCmdView.EntitiesCount);
            Assert.IsTrue(multiEntityCmdView.SkipDeferredEntityLookup);
            var actualEntities =
                NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Entity>(multiEntityCmdView.Entities.Ptr,
                    entityCount, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref actualEntities,
                AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            CollectionAssert.AreEquivalent(entities.ToArray(), actualEntities.ToArray());

            var multiEntityComponentCmdView = cmdView as EntityCommandBuffer.MultipleEntitiesComponentCommandView;
            Assert.AreEqual(TypeManager.GetTypeIndex<EcsTestData>(), multiEntityComponentCmdView.ComponentTypeIndex);
            Assert.AreEqual(0, multiEntityComponentCmdView.ComponentSize);
            Assertions.Assert.IsNull(multiEntityComponentCmdView.ComponentValue);
            Assert.AreEqual("Remove EcsTestData Component from 10 Entities",multiEntityCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_RemoveComponentForEntityQuery_ContainsExpectedData()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            int entityCount = 10;
            using var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.TempJob);

            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            ecb.RemoveComponentForEntityQuery<EcsTestData>(query);
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.RemoveComponentForMultipleEntities, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(56, cmdView.TotalSizeInBytes);

            var multiEntityCmdView = cmdView as EntityCommandBuffer.MultipleEntitiesCommandView;
            Assert.AreEqual(ecb.m_Data->m_Allocator, multiEntityCmdView.Allocator);
            Assert.AreEqual(entityCount, multiEntityCmdView.EntitiesCount);
            Assert.IsTrue(multiEntityCmdView.SkipDeferredEntityLookup);
            var actualEntities =
                NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Entity>(multiEntityCmdView.Entities.Ptr,
                    entityCount, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref actualEntities,
                AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            CollectionAssert.AreEquivalent(entities.ToArray(), actualEntities.ToArray());

            var multiEntityComponentCmdView = cmdView as EntityCommandBuffer.MultipleEntitiesComponentCommandView;
            Assert.AreEqual(TypeManager.GetTypeIndex<EcsTestData>(), multiEntityComponentCmdView.ComponentTypeIndex);
            Assert.AreEqual(0, multiEntityComponentCmdView.ComponentSize);
            Assertions.Assert.IsNull(multiEntityComponentCmdView.ComponentValue);
            Assert.AreEqual("Remove EcsTestData Component from 10 Entities",multiEntityCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_RemoveComponentTypesForEntityQuery_ContainsExpectedData()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData),
                typeof(EcsTestData2), typeof(EcsTestData3), typeof(EcsTestData4));
            var componentTypes = new ComponentTypes(typeof(EcsTestData2),
                typeof(EcsTestData3), typeof(EcsTestData4));
            int entityCount = 10;
            using var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.TempJob);

            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            ecb.RemoveComponentForEntityQuery(query, componentTypes);
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.RemoveMultipleComponentsForMultipleEntities, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(120, cmdView.TotalSizeInBytes);

            var multiEntityCmdView = cmdView as EntityCommandBuffer.MultipleEntitiesCommandView;
            Assert.AreEqual(ecb.m_Data->m_Allocator, multiEntityCmdView.Allocator);
            Assert.AreEqual(entityCount, multiEntityCmdView.EntitiesCount);
            Assert.IsTrue(multiEntityCmdView.SkipDeferredEntityLookup);
            var actualEntities =
                NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Entity>(multiEntityCmdView.Entities.Ptr,
                    entityCount, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref actualEntities,
                AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            CollectionAssert.AreEquivalent(entities.ToArray(), actualEntities.ToArray());

            var multiEntityAndComponentsCmdView =
                cmdView as EntityCommandBuffer.MultipleEntitiesAndComponentsCommandView;
            Assert.AreEqual(componentTypes.Length, multiEntityAndComponentsCmdView.Types.Length);
            for (int i = 0; i < componentTypes.Length; ++i)
            {
                Assert.AreEqual(componentTypes.GetTypeIndex(i), multiEntityAndComponentsCmdView.Types.GetTypeIndex(i));
            }

            Assert.AreEqual("Remove 3 Components from 10 Entities",multiEntityCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_DestroyEntitiesForEntityQuery_ContainsExpectedData()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            int entityCount = 10;
            using var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.TempJob);

            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            ecb.DestroyEntitiesForEntityQuery(query);
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.DestroyMultipleEntities, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(48, cmdView.TotalSizeInBytes);

            var multiEntityCmdView = cmdView as EntityCommandBuffer.MultipleEntitiesCommandView;
            Assert.AreEqual(ecb.m_Data->m_Allocator, multiEntityCmdView.Allocator);
            Assert.AreEqual(entityCount, multiEntityCmdView.EntitiesCount);
            Assert.IsTrue(multiEntityCmdView.SkipDeferredEntityLookup);
            var actualEntities =
                NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Entity>(multiEntityCmdView.Entities.Ptr,
                    entityCount, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref actualEntities,
                AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            CollectionAssert.AreEquivalent(entities.ToArray(), actualEntities.ToArray());
            Assert.AreEqual("Destroy 10 Entities",multiEntityCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_AddSharedComponent_ContainsExpectedData()
        {
            var ent = m_Manager.CreateEntity();
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            var value = new EcsTestSharedComp { value = 17 };
            ecb.AddSharedComponent(ent, value);
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.AddSharedComponentData, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(56, cmdView.TotalSizeInBytes);

            var entityCmdView = cmdView as EntityCommandBuffer.EntityCommandView;
            Assert.AreEqual(ent, entityCmdView.Entity);
            Assert.AreEqual(0, entityCmdView.IdentityIndex);
            Assert.AreEqual(1, entityCmdView.BatchCount);

            var sharedComponentCmdView = cmdView as EntityCommandBuffer.EntitySharedComponentCommandView;
            Assert.AreEqual(TypeManager.GetTypeIndex<EcsTestSharedComp>(), sharedComponentCmdView.ComponentTypeIndex);
            //Assert.AreEqual(760342523, sharedComponentCmdView.HashCode); // hash value not consistent
            var actualValue = (EcsTestSharedComp)sharedComponentCmdView.GetBoxedObject();
            Assert.AreEqual(17, actualValue.value);
            Assert.AreEqual("Add EcsTestSharedComp SharedComponentData",sharedComponentCmdView.ToString());
        }

        [Test]
        public void EntityCommandBufferDebugView_SetSharedComponent_ContainsExpectedData()
        {
            var ent = m_Manager.CreateEntity(typeof(EcsTestSharedComp));
            using var ecb = new EntityCommandBuffer(Allocator.TempJob);
            var value = new EcsTestSharedComp { value = 17 };
            ecb.SetSharedComponent(ent, value);
            var ecbView = new EntityCommandBuffer.EntityCommandBufferDebugView(ecb);

            var commands = ecbView.Commands;
            Assert.AreEqual(1, commands.Length);

            var cmdView = commands[0];
            Assert.AreEqual(ECBCommand.SetSharedComponentData, cmdView.CommandType);
            Assert.AreEqual(ecb.MainThreadSortKey, cmdView.SortKey);
            Assert.AreEqual(56, cmdView.TotalSizeInBytes);

            var entityCmdView = cmdView as EntityCommandBuffer.EntityCommandView;
            Assert.AreEqual(ent, entityCmdView.Entity);
            Assert.AreEqual(0, entityCmdView.IdentityIndex);
            Assert.AreEqual(1, entityCmdView.BatchCount);

            var sharedComponentCmdView = cmdView as EntityCommandBuffer.EntitySharedComponentCommandView;
            Assert.AreEqual(TypeManager.GetTypeIndex<EcsTestSharedComp>(), sharedComponentCmdView.ComponentTypeIndex);
            //Assert.AreEqual(760342523, sharedComponentCmdView.HashCode); // hash value not consistent
            var actualValue = (EcsTestSharedComp)sharedComponentCmdView.GetBoxedObject();
            Assert.AreEqual(17, actualValue.value);
            Assert.AreEqual("Set EcsTestSharedComp SharedComponentData",sharedComponentCmdView.ToString());
        }
    }
#endif
}
