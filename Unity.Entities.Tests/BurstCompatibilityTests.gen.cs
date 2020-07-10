// auto-generated
#if !NET_DOTS
using System;
using NUnit.Framework;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using System.ComponentModel;
namespace Unity.Entities.BurstTests
{
[BurstCompile]
public unsafe class BurstCompatibilityTests_Generated
{
    private delegate void TestFunc(IntPtr p);
    public static readonly string Guid = "de88b99578b2977946e15cda75b2e13c";
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
    public static void Burst_Unity__Entities__EntityDataAccess_AddComponentsDuringStructuralChange(IntPtr p)
    {
var v0 = default(UnsafeMatchingArchetypePtrList);
var v1 = default(EntityQueryFilter);
var v2 = default(ComponentTypes);
        var instance = (EntityDataAccess*)p;
        instance->AddComponentsDuringStructuralChange(v0 ,v1 ,v2);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_AddComponentsDuringStructuralChange()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_AddComponentsDuringStructuralChange);
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
    public static void Burst_Unity__Entities__EntityDataAccess_PlaybackManagedChanges(IntPtr p)
    {
        var instance = (EntityDataAccess*)p;
        instance->PlaybackManagedChanges();
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_PlaybackManagedChanges()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_PlaybackManagedChanges);
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
    public static void Burst_Unity__Entities__EntityDataAccess_RemoveMultipleComponentsDuringStructuralChange(IntPtr p)
    {
var v0 = default(UnsafeMatchingArchetypePtrList);
var v1 = default(EntityQueryFilter);
var v2 = default(ComponentTypes);
        var instance = (EntityDataAccess*)p;
        instance->RemoveMultipleComponentsDuringStructuralChange(v0 ,v1 ,v2);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityDataAccess_RemoveMultipleComponentsDuringStructuralChange()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityDataAccess_RemoveMultipleComponentsDuringStructuralChange);
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
    public static void Burst_Unity__Entities__EntityManager_AddComponent(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(ComponentType);
        var instance = (EntityManager*)p;
        instance->AddComponent(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_AddComponent()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_AddComponent);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_AddComponent_overload0(IntPtr p)
    {
var v0 = default(EntityQuery);
var v1 = default(ComponentType);
        var instance = (EntityManager*)p;
        instance->AddComponent(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_AddComponent_overload0()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_AddComponent_overload0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_AddComponent_overload1(IntPtr p)
    {
var v0 = default(NativeArray<Entity>);
var v1 = default(ComponentType);
        var instance = (EntityManager*)p;
        instance->AddComponent(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_AddComponent_overload1()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_AddComponent_overload1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_AddComponentRaw(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(int);
        var instance = (EntityManager*)p;
        instance->AddComponentRaw(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_AddComponentRaw()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_AddComponentRaw);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_AddComponents(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(ComponentTypes);
        var instance = (EntityManager*)p;
        instance->AddComponents(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_AddComponents()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_AddComponents);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_AllocateConsecutiveEntitiesForLoading(IntPtr p)
    {
var v0 = default(int);
        var instance = (EntityManager*)p;
        instance->AllocateConsecutiveEntitiesForLoading(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_AllocateConsecutiveEntitiesForLoading()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_AllocateConsecutiveEntitiesForLoading);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_BeforeStructuralChange(IntPtr p)
    {
        var instance = (EntityManager*)p;
        instance->BeforeStructuralChange();
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_BeforeStructuralChange()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_BeforeStructuralChange);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_BeginExclusiveEntityTransaction(IntPtr p)
    {
        var instance = (EntityManager*)p;
        instance->BeginExclusiveEntityTransaction();
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_BeginExclusiveEntityTransaction()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_BeginExclusiveEntityTransaction);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_CompleteAllJobs(IntPtr p)
    {
        var instance = (EntityManager*)p;
        instance->CompleteAllJobs();
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_CompleteAllJobs()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_CompleteAllJobs);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_CopyEntities(IntPtr p)
    {
var v0 = default(NativeArray<Entity>);
var v1 = default(NativeArray<Entity>);
        var instance = (EntityManager*)p;
        instance->CopyEntities(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_CopyEntities()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_CopyEntities);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_CreateArchetype(IntPtr p)
    {
ComponentType* v0 = (ComponentType*) ((byte*)p + 0);
var v1 = default(int);
        var instance = (EntityManager*)p;
        instance->CreateArchetype(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_CreateArchetype()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_CreateArchetype);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_CreateEntity(IntPtr p)
    {
        var instance = (EntityManager*)p;
        instance->CreateEntity();
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_CreateEntity()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_CreateEntity);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_CreateEntity_overload0(IntPtr p)
    {
var v0 = default(EntityArchetype);
        var instance = (EntityManager*)p;
        instance->CreateEntity(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_CreateEntity_overload0()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_CreateEntity_overload0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_CreateEntity_overload1(IntPtr p)
    {
var v0 = default(EntityArchetype);
var v1 = default(NativeArray<Entity>);
        var instance = (EntityManager*)p;
        instance->CreateEntity(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_CreateEntity_overload1()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_CreateEntity_overload1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_CreateEntity_overload2(IntPtr p)
    {
var v0 = default(EntityArchetype);
var v1 = default(int);
var v2 = default(Allocator);
        var instance = (EntityManager*)p;
        instance->CreateEntity(v0 ,v1 ,v2);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_CreateEntity_overload2()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_CreateEntity_overload2);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_CreateEntityManagerInUninitializedState(IntPtr p)
    {
EntityManager.CreateEntityManagerInUninitializedState();
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_CreateEntityManagerInUninitializedState()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_CreateEntityManagerInUninitializedState);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_CreateEntityRemapArray(IntPtr p)
    {
var v0 = default(Allocator);
        var instance = (EntityManager*)p;
        instance->CreateEntityRemapArray(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_CreateEntityRemapArray()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_CreateEntityRemapArray);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_DestroyEntity(IntPtr p)
    {
var v0 = default(Entity);
        var instance = (EntityManager*)p;
        instance->DestroyEntity(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_DestroyEntity()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_DestroyEntity);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_DestroyEntity_overload0(IntPtr p)
    {
var v0 = default(EntityQuery);
        var instance = (EntityManager*)p;
        instance->DestroyEntity(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_DestroyEntity_overload0()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_DestroyEntity_overload0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_DestroyEntity_overload1(IntPtr p)
    {
var v0 = default(NativeArray<Entity>);
        var instance = (EntityManager*)p;
        instance->DestroyEntity(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_DestroyEntity_overload1()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_DestroyEntity_overload1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_DestroyEntity_overload2(IntPtr p)
    {
var v0 = default(NativeSlice<Entity>);
        var instance = (EntityManager*)p;
        instance->DestroyEntity(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_DestroyEntity_overload2()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_DestroyEntity_overload2);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_DestroyEntityInternal(IntPtr p)
    {
Entity* v0 = (Entity*) ((byte*)p + 0);
var v1 = default(int);
        var instance = (EntityManager*)p;
        instance->DestroyEntityInternal(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_DestroyEntityInternal()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_DestroyEntityInternal);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_EndExclusiveEntityTransaction(IntPtr p)
    {
        var instance = (EntityManager*)p;
        instance->EndExclusiveEntityTransaction();
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_EndExclusiveEntityTransaction()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_EndExclusiveEntityTransaction);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_Equals(IntPtr p)
    {
var v0 = default(EntityManager);
        var instance = (EntityManager*)p;
        instance->Equals(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_Equals()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_Equals);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_Exists(IntPtr p)
    {
var v0 = default(Entity);
        var instance = (EntityManager*)p;
        instance->Exists(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_Exists()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_Exists);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_GetAllArchetypes(IntPtr p)
    {
var v0 = default(NativeList<EntityArchetype>);
        var instance = (EntityManager*)p;
        instance->GetAllArchetypes(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_GetAllArchetypes()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_GetAllArchetypes);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_GetAllChunks(IntPtr p)
    {
var v0 = default(Allocator);
        var instance = (EntityManager*)p;
        instance->GetAllChunks(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_GetAllChunks()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_GetAllChunks);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_GetAllEntities(IntPtr p)
    {
var v0 = default(Allocator);
        var instance = (EntityManager*)p;
        instance->GetAllEntities(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_GetAllEntities()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_GetAllEntities);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_GetBufferLength(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(int);
        var instance = (EntityManager*)p;
        instance->GetBufferLength(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_GetBufferLength()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_GetBufferLength);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_GetBufferRawRO(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(int);
        var instance = (EntityManager*)p;
        instance->GetBufferRawRO(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_GetBufferRawRO()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_GetBufferRawRO);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_GetBufferRawRW(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(int);
        var instance = (EntityManager*)p;
        instance->GetBufferRawRW(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_GetBufferRawRW()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_GetBufferRawRW);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_GetCheckedEntityDataAccess(IntPtr p)
    {
        var instance = (EntityManager*)p;
        instance->GetCheckedEntityDataAccess();
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_GetCheckedEntityDataAccess()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_GetCheckedEntityDataAccess);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_GetChunk(IntPtr p)
    {
var v0 = default(Entity);
        var instance = (EntityManager*)p;
        instance->GetChunk(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_GetChunk()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_GetChunk);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_GetChunkVersionHash(IntPtr p)
    {
var v0 = default(Entity);
        var instance = (EntityManager*)p;
        instance->GetChunkVersionHash(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_GetChunkVersionHash()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_GetChunkVersionHash);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_GetComponentCount(IntPtr p)
    {
var v0 = default(Entity);
        var instance = (EntityManager*)p;
        instance->GetComponentCount(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_GetComponentCount()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_GetComponentCount);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_GetComponentDataRawRO(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(int);
        var instance = (EntityManager*)p;
        instance->GetComponentDataRawRO(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_GetComponentDataRawRO()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_GetComponentDataRawRO);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_GetComponentDataRawRW(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(int);
        var instance = (EntityManager*)p;
        instance->GetComponentDataRawRW(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_GetComponentDataRawRW()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_GetComponentDataRawRW);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_GetComponentTypeIndex(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(int);
        var instance = (EntityManager*)p;
        instance->GetComponentTypeIndex(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_GetComponentTypeIndex()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_GetComponentTypeIndex);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_GetComponentTypes(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(Allocator);
        var instance = (EntityManager*)p;
        instance->GetComponentTypes(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_GetComponentTypes()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_GetComponentTypes);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_GetCreatedAndDestroyedEntities(IntPtr p)
    {
var v0 = default(NativeList<int>);
var v1 = default(NativeList<Entity>);
var v2 = default(NativeList<Entity>);
        var instance = (EntityManager*)p;
        instance->GetCreatedAndDestroyedEntities(v0 ,v1 ,v2);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_GetCreatedAndDestroyedEntities()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_GetCreatedAndDestroyedEntities);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_GetCreatedAndDestroyedEntitiesAsync(IntPtr p)
    {
var v0 = default(NativeList<int>);
var v1 = default(NativeList<Entity>);
var v2 = default(NativeList<Entity>);
        var instance = (EntityManager*)p;
        instance->GetCreatedAndDestroyedEntitiesAsync(v0 ,v1 ,v2);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_GetCreatedAndDestroyedEntitiesAsync()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_GetCreatedAndDestroyedEntitiesAsync);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_GetDynamicComponentTypeHandle(IntPtr p)
    {
var v0 = default(ComponentType);
        var instance = (EntityManager*)p;
        instance->GetDynamicComponentTypeHandle(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_GetDynamicComponentTypeHandle()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_GetDynamicComponentTypeHandle);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_GetEnabled(IntPtr p)
    {
var v0 = default(Entity);
        var instance = (EntityManager*)p;
        instance->GetEnabled(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_GetEnabled()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_GetEnabled);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_GetEntityQueryMask(IntPtr p)
    {
var v0 = default(EntityQuery);
        var instance = (EntityManager*)p;
        instance->GetEntityQueryMask(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_GetEntityQueryMask()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_GetEntityQueryMask);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_GetEntityTypeHandle(IntPtr p)
    {
        var instance = (EntityManager*)p;
        instance->GetEntityTypeHandle();
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_GetEntityTypeHandle()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_GetEntityTypeHandle);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_GetHashCode(IntPtr p)
    {
        var instance = (EntityManager*)p;
        instance->GetHashCode();
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_GetHashCode()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_GetHashCode);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_HasComponent(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(ComponentType);
        var instance = (EntityManager*)p;
        instance->HasComponent(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_HasComponent()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_HasComponent);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_HasComponentRaw(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(int);
        var instance = (EntityManager*)p;
        instance->HasComponentRaw(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_HasComponentRaw()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_HasComponentRaw);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_Instantiate(IntPtr p)
    {
var v0 = default(Entity);
        var instance = (EntityManager*)p;
        instance->Instantiate(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_Instantiate()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_Instantiate);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_Instantiate_overload0(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(NativeArray<Entity>);
        var instance = (EntityManager*)p;
        instance->Instantiate(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_Instantiate_overload0()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_Instantiate_overload0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_Instantiate_overload1(IntPtr p)
    {
var v0 = default(NativeArray<Entity>);
var v1 = default(NativeArray<Entity>);
        var instance = (EntityManager*)p;
        instance->Instantiate(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_Instantiate_overload1()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_Instantiate_overload1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_Instantiate_overload2(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(int);
var v2 = default(Allocator);
        var instance = (EntityManager*)p;
        instance->Instantiate(v0 ,v1 ,v2);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_Instantiate_overload2()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_Instantiate_overload2);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_IsQueryValid(IntPtr p)
    {
var v0 = default(EntityQuery);
        var instance = (EntityManager*)p;
        instance->IsQueryValid(v0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_IsQueryValid()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_IsQueryValid);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_PreDisposeCheck(IntPtr p)
    {
        var instance = (EntityManager*)p;
        instance->PreDisposeCheck();
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_PreDisposeCheck()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_PreDisposeCheck);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_RemoveComponent(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(ComponentType);
        var instance = (EntityManager*)p;
        instance->RemoveComponent(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_RemoveComponent()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_RemoveComponent);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_RemoveComponent_overload0(IntPtr p)
    {
var v0 = default(EntityQuery);
var v1 = default(ComponentType);
        var instance = (EntityManager*)p;
        instance->RemoveComponent(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_RemoveComponent_overload0()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_RemoveComponent_overload0);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_RemoveComponent_overload1(IntPtr p)
    {
var v0 = default(EntityQuery);
var v1 = default(ComponentTypes);
        var instance = (EntityManager*)p;
        instance->RemoveComponent(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_RemoveComponent_overload1()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_RemoveComponent_overload1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_RemoveComponent_overload2(IntPtr p)
    {
var v0 = default(NativeArray<Entity>);
var v1 = default(ComponentType);
        var instance = (EntityManager*)p;
        instance->RemoveComponent(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_RemoveComponent_overload2()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_RemoveComponent_overload2);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_RemoveComponentRaw(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(int);
        var instance = (EntityManager*)p;
        instance->RemoveComponentRaw(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_RemoveComponentRaw()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_RemoveComponentRaw);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_SetArchetype(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(EntityArchetype);
        var instance = (EntityManager*)p;
        instance->SetArchetype(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_SetArchetype()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_SetArchetype);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_SetComponentDataRaw(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(int);
void* v2 = (void*) ((byte*)p + 2048);
var v3 = default(int);
        var instance = (EntityManager*)p;
        instance->SetComponentDataRaw(v0 ,v1 ,v2 ,v3);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_SetComponentDataRaw()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_SetComponentDataRaw);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_SetEnabled(IntPtr p)
    {
var v0 = default(Entity);
var v1 = default(bool);
        var instance = (EntityManager*)p;
        instance->SetEnabled(v0 ,v1);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_SetEnabled()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_SetEnabled);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [BurstCompile(CompileSynchronously = true)]
    public static void Burst_Unity__Entities__EntityManager_SwapComponents(IntPtr p)
    {
var v0 = default(ArchetypeChunk);
var v1 = default(int);
var v2 = default(ArchetypeChunk);
var v3 = default(int);
        var instance = (EntityManager*)p;
        instance->SwapComponents(v0 ,v1 ,v2 ,v3);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Test]
    public void BurstCompile_Unity__Entities__EntityManager_SwapComponents()
    {
        BurstCompiler.CompileFunctionPointer<TestFunc>(Burst_Unity__Entities__EntityManager_SwapComponents);
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
