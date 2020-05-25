// auto-generated
#if !NET_DOTS
using System;
using NUnit.Framework;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using System.ComponentModel;
namespace Unity.Entities.Tests
{
[BurstCompile]
public unsafe class BurstCompatibilityTests_Generated
{
    private delegate void TestFunc(IntPtr p);
    public static readonly string Guid = "fbc0f35cd3fbe7dd682e63c298e126f4";
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_AddComponent(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(ComponentType);
        var instance = (EntityDataAccess*)p;
        instance->AddComponent(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_AddComponent()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_AddComponent);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_AddComponent_overload0(IntPtr p)
    {
var v0 = default(UnsafeMatchingArchetypePtrList);
var v1 = default(EntityQueryFilter);
var v2 = default(ComponentType);
        var instance = (EntityDataAccess*)p;
        instance->AddComponent(v0 ,v1 ,v2);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_AddComponent_overload0()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_AddComponent_overload0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_AddComponentDuringStructuralChange(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(ComponentType);
        var instance = (EntityDataAccess*)p;
        instance->AddComponentDuringStructuralChange(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_AddComponentDuringStructuralChange()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_AddComponentDuringStructuralChange);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_AddComponentDuringStructuralChange_overload0(IntPtr p)
    {
var v0 = default(UnsafeMatchingArchetypePtrList);
var v1 = default(EntityQueryFilter);
var v2 = default(ComponentType);
        var instance = (EntityDataAccess*)p;
        instance->AddComponentDuringStructuralChange(v0 ,v1 ,v2);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_AddComponentDuringStructuralChange_overload0()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_AddComponentDuringStructuralChange_overload0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_AddSharedComponentData(IntPtr p)
    {
var v0 = default(NativeArray<ArchetypeChunk>);
var v1 = default(int);
var v2 = default(ComponentType);
        var instance = (EntityDataAccess*)p;
        instance->AddSharedComponentData(v0 ,v1 ,v2);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_AddSharedComponentData()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_AddSharedComponentData);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_AddSharedComponentDataDuringStructuralChange(IntPtr p)
    {
var v0 = default(NativeArray<ArchetypeChunk>);
var v1 = default(int);
var v2 = default(ComponentType);
        var instance = (EntityDataAccess*)p;
        instance->AddSharedComponentDataDuringStructuralChange(v0 ,v1 ,v2);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_AddSharedComponentDataDuringStructuralChange()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_AddSharedComponentDataDuringStructuralChange);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_BeforeStructuralChange(IntPtr p)
    {
        var instance = (EntityDataAccess*)p;
        instance->BeforeStructuralChange();
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_BeforeStructuralChange()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_BeforeStructuralChange);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_CreateArchetype(IntPtr p)
    {
ComponentType* v0 = (ComponentType*) ((byte*)p + 0);
var v1 = default(int);
        var instance = (EntityDataAccess*)p;
        instance->CreateArchetype(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_CreateArchetype()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_CreateArchetype);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_CreateEntity(IntPtr p)
    {
var v0 = default(EntityArchetype);
        var instance = (EntityDataAccess*)p;
        instance->CreateEntity(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_CreateEntity()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_CreateEntity);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_CreateEntity_overload0(IntPtr p)
    {
var v0 = default(EntityArchetype);
var v1 = default(NativeArray<Entity>);
        var instance = (EntityDataAccess*)p;
        instance->CreateEntity(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_CreateEntity_overload0()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_CreateEntity_overload0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_CreateEntity_overload1(IntPtr p)
    {
var v0 = default(EntityArchetype);
Entity* v1 = (Entity*) ((byte*)p + 1024);
var v2 = default(int);
        var instance = (EntityDataAccess*)p;
        instance->CreateEntity(v0 ,v1 ,v2);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_CreateEntity_overload1()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_CreateEntity_overload1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_CreateEntityDuringStructuralChange(IntPtr p)
    {
var v0 = default(EntityArchetype);
        var instance = (EntityDataAccess*)p;
        instance->CreateEntityDuringStructuralChange(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_CreateEntityDuringStructuralChange()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_CreateEntityDuringStructuralChange);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_CreateEntityDuringStructuralChange_overload0(IntPtr p)
    {
var v0 = default(EntityArchetype);
var v1 = default(NativeArray<Entity>);
        var instance = (EntityDataAccess*)p;
        instance->CreateEntityDuringStructuralChange(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_CreateEntityDuringStructuralChange_overload0()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_CreateEntityDuringStructuralChange_overload0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_CreateEntityDuringStructuralChange_overload1(IntPtr p)
    {
var v0 = default(EntityArchetype);
Entity* v1 = (Entity*) ((byte*)p + 1024);
var v2 = default(int);
        var instance = (EntityDataAccess*)p;
        instance->CreateEntityDuringStructuralChange(v0 ,v1 ,v2);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_CreateEntityDuringStructuralChange_overload1()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_CreateEntityDuringStructuralChange_overload1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_DestroyEntity(IntPtr p)
    {
var v0 = default(Entity);
        var instance = (EntityDataAccess*)p;
        instance->DestroyEntity(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_DestroyEntity()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_DestroyEntity);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_DestroyEntity_overload0(IntPtr p)
    {
var v0 = default(UnsafeMatchingArchetypePtrList);
var v1 = default(EntityQueryFilter);
        var instance = (EntityDataAccess*)p;
        instance->DestroyEntity(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_DestroyEntity_overload0()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_DestroyEntity_overload0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_DestroyEntityDuringStructuralChange(IntPtr p)
    {
var v0 = default(UnsafeMatchingArchetypePtrList);
var v1 = default(EntityQueryFilter);
        var instance = (EntityDataAccess*)p;
        instance->DestroyEntityDuringStructuralChange(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_DestroyEntityDuringStructuralChange()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_DestroyEntityDuringStructuralChange);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_DestroyEntityInternal(IntPtr p)
    {
Entity* v0 = (Entity*) ((byte*)p + 0);
var v1 = default(int);
        var instance = (EntityDataAccess*)p;
        instance->DestroyEntityInternal(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_DestroyEntityInternal()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_DestroyEntityInternal);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_DestroyEntityInternalDuringStructuralChange(IntPtr p)
    {
Entity* v0 = (Entity*) ((byte*)p + 0);
var v1 = default(int);
        var instance = (EntityDataAccess*)p;
        instance->DestroyEntityInternalDuringStructuralChange(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_DestroyEntityInternalDuringStructuralChange()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_DestroyEntityInternalDuringStructuralChange);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_Exists(IntPtr p)
    {
var v0 = default(Entity);
        var instance = (EntityDataAccess*)p;
        instance->Exists(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_Exists()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_Exists);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_FillSortedArchetypeArray(IntPtr p)
    {
ComponentTypeInArchetype* v0 = (ComponentTypeInArchetype*) ((byte*)p + 0);
ComponentType* v1 = (ComponentType*) ((byte*)p + 1024);
var v2 = default(int);
EntityDataAccess.FillSortedArchetypeArray(v0 ,v1 ,v2);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_FillSortedArchetypeArray()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_FillSortedArchetypeArray);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_GetComponentDataRawRW(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(int);
        var instance = (EntityDataAccess*)p;
        instance->GetComponentDataRawRW(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_GetComponentDataRawRW()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_GetComponentDataRawRW);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_GetComponentDataRawRWEntityHasComponent(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(int);
        var instance = (EntityDataAccess*)p;
        instance->GetComponentDataRawRWEntityHasComponent(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_GetComponentDataRawRWEntityHasComponent()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_GetComponentDataRawRWEntityHasComponent);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_GetEntityOnlyArchetype(IntPtr p)
    {
        var instance = (EntityDataAccess*)p;
        instance->GetEntityOnlyArchetype();
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_GetEntityOnlyArchetype()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_GetEntityOnlyArchetype);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_HasComponent(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(ComponentType);
        var instance = (EntityDataAccess*)p;
        instance->HasComponent(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_HasComponent()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_HasComponent);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_InstantiateInternal(IntPtr p)
    {
var v0 = default(Entity);
Entity* v1 = (Entity*) ((byte*)p + 1024);
var v2 = default(int);
        var instance = (EntityDataAccess*)p;
        instance->InstantiateInternal(v0 ,v1 ,v2);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_InstantiateInternal()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_InstantiateInternal);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_InstantiateInternal_overload0(IntPtr p)
    {
Entity* v0 = (Entity*) ((byte*)p + 0);
Entity* v1 = (Entity*) ((byte*)p + 1024);
var v2 = default(int);
var v3 = default(int);
var v4 = default(bool);
        var instance = (EntityDataAccess*)p;
        instance->InstantiateInternal(v0 ,v1 ,v2 ,v3 ,v4);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_InstantiateInternal_overload0()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_InstantiateInternal_overload0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_InstantiateInternalDuringStructuralChange(IntPtr p)
    {
var v0 = default(Entity);
Entity* v1 = (Entity*) ((byte*)p + 1024);
var v2 = default(int);
        var instance = (EntityDataAccess*)p;
        instance->InstantiateInternalDuringStructuralChange(v0 ,v1 ,v2);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_InstantiateInternalDuringStructuralChange()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_InstantiateInternalDuringStructuralChange);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_RemoveComponent(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(ComponentType);
        var instance = (EntityDataAccess*)p;
        instance->RemoveComponent(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_RemoveComponent()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_RemoveComponent);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_RemoveComponent_overload0(IntPtr p)
    {
var v0 = default(NativeArray<ArchetypeChunk>);
var v1 = default(ComponentType);
        var instance = (EntityDataAccess*)p;
        instance->RemoveComponent(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_RemoveComponent_overload0()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_RemoveComponent_overload0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_RemoveComponent_overload1(IntPtr p)
    {
var v0 = default(UnsafeMatchingArchetypePtrList);
var v1 = default(EntityQueryFilter);
var v2 = default(ComponentType);
        var instance = (EntityDataAccess*)p;
        instance->RemoveComponent(v0 ,v1 ,v2);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_RemoveComponent_overload1()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_RemoveComponent_overload1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_RemoveComponentDuringStructuralChange(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(ComponentType);
        var instance = (EntityDataAccess*)p;
        instance->RemoveComponentDuringStructuralChange(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_RemoveComponentDuringStructuralChange()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_RemoveComponentDuringStructuralChange);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_RemoveComponentDuringStructuralChange_overload0(IntPtr p)
    {
var v0 = default(NativeArray<ArchetypeChunk>);
var v1 = default(ComponentType);
        var instance = (EntityDataAccess*)p;
        instance->RemoveComponentDuringStructuralChange(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_RemoveComponentDuringStructuralChange_overload0()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_RemoveComponentDuringStructuralChange_overload0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_RemoveComponentDuringStructuralChange_overload1(IntPtr p)
    {
var v0 = default(UnsafeMatchingArchetypePtrList);
var v1 = default(EntityQueryFilter);
var v2 = default(ComponentType);
        var instance = (EntityDataAccess*)p;
        instance->RemoveComponentDuringStructuralChange(v0 ,v1 ,v2);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_RemoveComponentDuringStructuralChange_overload1()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_RemoveComponentDuringStructuralChange_overload1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_SetBufferRaw(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(int);
BufferHeader* v2 = (BufferHeader*) ((byte*)p + 2048);
var v3 = default(int);
        var instance = (EntityDataAccess*)p;
        instance->SetBufferRaw(v0 ,v1 ,v2 ,v3);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_SetBufferRaw()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_SetBufferRaw);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_SetComponentDataRaw(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(int);
void* v2 = (void*) ((byte*)p + 2048);
var v3 = default(int);
        var instance = (EntityDataAccess*)p;
        instance->SetComponentDataRaw(v0 ,v1 ,v2 ,v3);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_SetComponentDataRaw()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_SetComponentDataRaw);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityDataAccess_SwapComponents(IntPtr p)
    {
var v0 = default(ArchetypeChunk);
var v1 = default(int);
var v2 = default(ArchetypeChunk);
var v3 = default(int);
        var instance = (EntityDataAccess*)p;
        instance->SwapComponents(v0 ,v1 ,v2 ,v3);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_SwapComponents()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_SwapComponents);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityQueryImpl_GetBufferSafetyHandle(IntPtr p)
    {
var v0 = default(int);
        var instance = (EntityQueryImpl*)p;
        instance->GetBufferSafetyHandle(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityQueryImpl_GetBufferSafetyHandle()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityQueryImpl_GetBufferSafetyHandle);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityQueryImpl_GetSafetyHandle(IntPtr p)
    {
var v0 = default(int);
        var instance = (EntityQueryImpl*)p;
        instance->GetSafetyHandle(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityQueryImpl_GetSafetyHandle()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityQueryImpl_GetSafetyHandle);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__SystemBaseRegistry_CallForwardingFunction(IntPtr p)
    {
SystemState* v0 = (SystemState*) ((byte*)p + 0);
var v1 = default(int);
SystemBaseRegistry.CallForwardingFunction(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__SystemBaseRegistry_CallForwardingFunction()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__SystemBaseRegistry_CallForwardingFunction);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__SystemBaseRegistry_CallOnCreate(IntPtr p)
    {
SystemState* v0 = (SystemState*) ((byte*)p + 0);
SystemBaseRegistry.CallOnCreate(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__SystemBaseRegistry_CallOnCreate()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__SystemBaseRegistry_CallOnCreate);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__SystemBaseRegistry_CallOnDestroy(IntPtr p)
    {
SystemState* v0 = (SystemState*) ((byte*)p + 0);
SystemBaseRegistry.CallOnDestroy(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__SystemBaseRegistry_CallOnDestroy()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__SystemBaseRegistry_CallOnDestroy);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__SystemBaseRegistry_CallOnUpdate(IntPtr p)
    {
SystemState* v0 = (SystemState*) ((byte*)p + 0);
SystemBaseRegistry.CallOnUpdate(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__SystemBaseRegistry_CallOnUpdate()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__SystemBaseRegistry_CallOnUpdate);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__SystemBaseRegistry_GetDebugName(IntPtr p)
    {
var v0 = default(int);
SystemBaseRegistry.GetDebugName(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__SystemBaseRegistry_GetDebugName()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__SystemBaseRegistry_GetDebugName);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__SystemBaseRegistry_GetSystemTypeMetaIndex(IntPtr p)
    {
var v0 = default(long);
SystemBaseRegistry.GetSystemTypeMetaIndex(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__SystemBaseRegistry_GetSystemTypeMetaIndex()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__SystemBaseRegistry_GetSystemTypeMetaIndex);
    }
}
}
#endif
