using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Entities.Hybrid.Baking;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.SocialPlatforms;
using Object = UnityEngine.Object;

namespace Unity.Entities.Tests.Conversion
{
    class TransformBakingTests
    {
        #region Setup

        private Scene _scene;
        private World _destinationWorld;
        private EntityManager _entityManager;
        private TestWithObjects _objects = default;

        internal enum ConversionType
        {
            ConvertHierarchy,
            ConvertScene
        }

        internal enum EntityType
        {
            PrimaryEntity,
            AdditionalEntity
        }

        [OneTimeSetUp]
        public void SetUpOnce()
        {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            _destinationWorld = new World("TestWorld");
            _entityManager = _destinationWorld.EntityManager;
        }

        [OneTimeTearDown]
        public void TearDownOnce()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            _destinationWorld.Dispose();
            _destinationWorld = null;
        }

        [SetUp]
        public void SetUp()
        {
            _objects.SetUp();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene.GetRootGameObjects())
                Object.DestroyImmediate(go);
            _objects.TearDown();
            _entityManager.DestroyEntity(_entityManager.UniversalQuery);

            AssignTransformUsageBaker.Flags.Clear();
            AssignTransformUsageBaker.AdditionalFlags.Clear();
        }

        BakingSettings MakeDefaultSettings() => new BakingSettings
        {
            BakingFlags = BakingUtility.BakingFlags.AssignName
        };

        private void Convert(GameObject root, ConversionType conversion, BakingSettings settings, params Type[] additionalBakers)
        {
            // These tests fully replace existing bakers, this means we also replace the StaticAndActiveBaker which currently forces all transforms to be dynamic so that nothing is stripped by default.
            var replaceExistingBakers = true;
            using var overrideBakers = new BakerDataUtility.OverrideBakers(replaceExistingBakers, additionalBakers);

            if (settings == null)
            {
                settings = MakeDefaultSettings();
                if (conversion == ConversionType.ConvertScene)
                    settings.BakingFlags |= BakingUtility.BakingFlags.AddEntityGUID;
            }

            bool localBlobAssetStore = !settings.BlobAssetStore.IsCreated;
            if (localBlobAssetStore)
            {
                settings.BlobAssetStore = new BlobAssetStore(128);
            }

            if (conversion == ConversionType.ConvertScene)
            {
                SceneManager.MoveGameObjectToScene(root, _scene);
                BakingUtility.BakeScene(_destinationWorld, _scene, settings, false, null);
            }
            else
                BakingUtility.BakeGameObjects(_destinationWorld, new[]{root}, settings);

            if (localBlobAssetStore)
            {
                settings.BlobAssetStore.Dispose();
            }
        }

        private void Convert(GameObject root, ConversionType conversion, params Type[] additionalBakers)
        {
            Convert(root, conversion, null, additionalBakers);
        }

        #endregion setup

        static EntityMatch Exact(ConversionType conversionType, EntityType entityType = EntityType.PrimaryEntity, params object[] matchData) =>
            ExactWith(conversionType, Enumerable.Empty<Type>(), entityType, matchData);
        static EntityMatch ExactUnreferenced(ConversionType conversionType, EntityType entityType = EntityType.PrimaryEntity, params object[] matchData)
        {
            matchData = matchData.Append(typeof(RemoveUnusedEntityInBake)).ToArray();
            return ExactWith(conversionType, Enumerable.Empty<Type>(), entityType, matchData);
        }

        static EntityMatch ExactWith(ConversionType conversionType, IEnumerable<Type> types, EntityType entityType = EntityType.PrimaryEntity, params object[] matchData)
        {
            if (entityType == EntityType.PrimaryEntity)
                types = types.Append(typeof(AdditionalEntitiesBakingData));
            else if (entityType == EntityType.AdditionalEntity)
                types = types.Append(typeof(AdditionalEntityParent));

            if (conversionType == ConversionType.ConvertHierarchy)
                return EntityMatch.Exact(types.Append(typeof(TransformAuthoring)).Append(typeof(Simulate)), matchData);
            else
                return EntityMatch.Exact(types.Append(typeof(TransformAuthoring)).Append(typeof(EntityGuid)).Append(typeof(Simulate)), matchData);
        }

        static EntityMatch ExactPrefabWithComponents(IEnumerable<Type> types, params object[] matchData)
            => EntityMatch.Exact(types.Append(typeof(LinkedEntityGroup)).Append(typeof(Prefab)), matchData);
        static EntityMatch ExactPrefab(params object[] matchData)
            => ExactPrefabWithComponents(Enumerable.Empty<Type>(), matchData);

        private static object[] GlobalData(float3 pos, quaternion rotation, float3 scale)
        {
#if !ENABLE_TRANSFORM_V1
            // TransformData does not support per-object non-uniform scale
            Unity.Assertions.Assert.AreApproximatelyEqual(scale.x, scale.y);
            Unity.Assertions.Assert.AreApproximatelyEqual(scale.x, scale.z);
            var transform = LocalTransform.FromPositionRotationScale(pos, rotation, scale.x);
            return new object[]
            {
                transform,
                (WorldTransform)transform,
                new LocalToWorld {Value = transform.ToMatrix()}
            };
#else
            if (scale.Equals(1))
                return new object[]
                {
                    new Translation() {Value = pos},
                    new Rotation {Value = rotation},
                    new LocalToWorld() {Value = float4x4.TRS(pos, rotation, scale)}
                };
            else
                return new object[]
                {
                    new Translation() {Value = pos},
                    new Rotation {Value = rotation},
                    new NonUniformScale {Value = scale},
                    new LocalToWorld() {Value = float4x4.TRS(pos, rotation, scale)}
                };
#endif
        }

        private static IEnumerable ProduceTestCases(IEnumerable<(TransformUsageFlags, object[])> testCaseData)
        {
            foreach (var entry in testCaseData)
            {
                yield return new TestCaseData(ConversionType.ConvertHierarchy, entry.Item1, entry.Item2).SetName($"ConvertHierarchy_{entry.Item1}");
                yield return new TestCaseData(ConversionType.ConvertScene, entry.Item1, entry.Item2).SetName($"ConvertScene_{entry.Item1}");
            }
        }

        private static IEnumerable<(TransformUsageFlags, object[])> BlankObjectWithUniformScaleData(float3 t, quaternion r, float s)
        {
            yield return (TransformUsageFlags.None, Array.Empty<object>());
            yield return (TransformUsageFlags.ManualOverride, Array.Empty<object>());
            yield return (TransformUsageFlags.Default, GlobalData(t, r, s));
            yield return (TransformUsageFlags.ReadGlobalTransform, GlobalData(t, r, s));
            yield return (TransformUsageFlags.WriteGlobalTransform, GlobalData(t, r, s));
            yield return (TransformUsageFlags.ReadResidualTransform, new object[] {new LocalToWorld {Value = float4x4.TRS(t, r, s)}}); // no residual transform
            yield return (TransformUsageFlags.ReadLocalToWorld, new object[] {new LocalToWorld {Value = float4x4.TRS(t, r, s)}});
        }

        public static IEnumerable BlankObjectTestCases => ProduceTestCases(BlankObjectWithUniformScaleData(float3.zero, quaternion.identity, 1));



        public static IEnumerable BlankObjectWithOffsetTestCases => ProduceTestCases(BlankObjectWithUniformScaleData(
            new float3(4, 5, 6), quaternion.Euler(1, 2, 3), 4)
        );

        [DisableAutoCreation]
        class TransformUsageBaker : Baker<UnityEngine.Transform>
        {
            internal static TransformUsageFlags Flags;

            public override void Bake(UnityEngine.Transform authoring)
            {
                GetEntity(authoring, Flags);
            }
        }

        [DisableAutoCreation]
        class AssignTransformUsageBaker : Baker<UnityEngine.Transform>
        {
            internal static readonly Dictionary<GameObject, TransformUsageFlags> Flags = new Dictionary<GameObject, TransformUsageFlags>();
            internal static readonly Dictionary<GameObject, TransformUsageFlags> AdditionalFlags = new Dictionary<GameObject, TransformUsageFlags>();
            public override void Bake(UnityEngine.Transform authoring)
            {
                if (Flags.TryGetValue(authoring.gameObject, out var flags))
                    GetEntity(authoring, flags);

                if (AdditionalFlags.TryGetValue(authoring.gameObject, out flags))
                    CreateAdditionalEntity(flags);
            }
        }

        [DisableAutoCreation]
        class ManualTransformBaker : Baker<UnityEngine.Transform>
        {
            internal static TransformUsageFlags Flags;

            public override void Bake(UnityEngine.Transform authoring)
            {
                // Pollute the entity with random TransformUsageFlags that should get ignored due to the ManualOverride
                GetEntity(authoring, TransformUsageFlags.ReadGlobalTransform);
                GetEntity(authoring, TransformUsageFlags.ReadLocalToWorld);
                var entity = GetEntity(authoring, TransformUsageFlags.ManualOverride);

#if !ENABLE_TRANSFORM_V1
                AddComponent(entity, LocalTransform.Identity);
                AddComponent(entity, WorldTransform.Identity);
#else
                AddComponent(entity, new Translation());
#endif
            }
        }
        [DisableAutoCreation]
        class ManualBakeAdditional : Baker<UnityEngine.Transform>
        {
            public override void Bake(UnityEngine.Transform authoring)
            {
                // Pollute the entity with random TransformUsageFlags that should get ignored due to the ManualOverride
                GetEntity(authoring, TransformUsageFlags.ReadLocalToWorld);

                var entity = CreateAdditionalEntity(TransformUsageFlags.ManualOverride);
#if !ENABLE_TRANSFORM_V1
                AddComponent(entity, LocalTransform.Identity);
                AddComponent(entity, WorldTransform.Identity);
#else
                AddComponent(entity, new Translation());
#endif
            }
        }

        [TestCaseSource(typeof(TransformBakingTests), nameof(BlankObjectTestCases))]
        public void ConvertBlankGameObject(ConversionType type, TransformUsageFlags flag, object[] matchers)
        {
            var go = _objects.CreateGameObject();
            TransformUsageBaker.Flags = flag;
            Convert(go, type, typeof(TransformUsageBaker));
            EntitiesAssert.ContainsOnly(_entityManager, Exact(type, EntityType.PrimaryEntity, matchers));

            var entity = _destinationWorld.GetExistingSystemManaged<BakingSystem>().GetEntity(go);

            var authoring = _entityManager.GetComponentData<TransformAuthoring>(entity);
            var transform = go.transform;
            Assert.AreEqual((float3)transform.localPosition, authoring.LocalPosition);
            Assert.AreEqual((quaternion)transform.localRotation, authoring.LocalRotation);
            Assert.AreEqual((float3)transform.localScale, authoring.LocalScale);
            Assert.AreEqual((float3)transform.position, authoring.Position);
            Assert.AreEqual((quaternion)transform.rotation, authoring.Rotation);
            Assert.AreEqual((float4x4)transform.localToWorldMatrix, authoring.LocalToWorld);
            Assert.AreEqual(default(Entity), authoring.AuthoringParent);
            Assert.AreEqual(default(Entity), authoring.RuntimeParent);

            Assert.AreEqual(flag, _entityManager.GetComponentData<TransformAuthoring>(entity).RuntimeTransformUsage);
        }

        [Test]
        public void ConvertGameObject_WithIConvertGameObjectToEntity_HasDefaultFlags([Values]ConversionType type)
        {
            var root = _objects.CreateGameObject("Parent");
            var child = _objects.CreateGameObject("Child");
            child.transform.SetParent(root.transform);
            TransformUsageBaker.Flags = TransformUsageFlags.Default;
            Convert(root, type, typeof(TransformUsageBaker));

            EntitiesAssert.ContainsOnly(_entityManager,
#if !ENABLE_TRANSFORM_V1
                Exact(type, EntityType.PrimaryEntity, typeof(LocalTransform), typeof(WorldTransform), typeof(LocalToWorld)),
                Exact(type, EntityType.PrimaryEntity, typeof(LocalTransform), typeof(WorldTransform), typeof(LocalToWorld), typeof(Parent)));
#else
                Exact(type, EntityType.PrimaryEntity, typeof(Translation), typeof(Rotation), typeof(LocalToWorld)),
                Exact(type, EntityType.PrimaryEntity, typeof(Translation), typeof(Rotation), typeof(LocalToWorld), typeof(Parent), typeof(LocalToParent)));
#endif
        }

        [Test]
        public void BakeManualTransform([Values]ConversionType type)
        {
            var go = _objects.CreateGameObject();
            Convert(go, type, typeof(ManualTransformBaker));
#if !ENABLE_TRANSFORM_V1
            EntitiesAssert.ContainsOnly(_entityManager, Exact(type, EntityType.PrimaryEntity, typeof(LocalTransform), typeof(WorldTransform)));
#else
            EntitiesAssert.ContainsOnly(_entityManager, Exact(type, EntityType.PrimaryEntity, typeof(Translation)));
#endif
        }

        [Test]
        public void BakeManualAdditionalTransform([Values]ConversionType type)
        {
            var go = _objects.CreateGameObject();
            Convert(go, type, typeof(ManualBakeAdditional));
            EntitiesAssert.ContainsOnly(_entityManager,
#if !ENABLE_TRANSFORM_V1
                Exact(type, EntityType.AdditionalEntity, typeof(LocalTransform), typeof(WorldTransform)),
                Exact(type, EntityType.PrimaryEntity, typeof(LocalToWorld)));
#else
                Exact(type, EntityType.AdditionalEntity, typeof(Translation)),
                Exact(type, EntityType.PrimaryEntity, typeof(LocalToWorld)));
#endif
        }

        [Test]
        public void ConvertBlankGameObject_InScene_DoesNotCreateEntities()
        {
            var go = _objects.CreateGameObject();
            Convert(go, ConversionType.ConvertScene);
            EntitiesAssert.ContainsOnly(_entityManager, ExactUnreferenced(ConversionType.ConvertScene));
        }

        [Test]
        public void ConvertBlankGameObject_AsHierarchy_CreatesEntities()
        {
            var go = _objects.CreateGameObject();
            Convert(go, ConversionType.ConvertHierarchy);
            EntitiesAssert.ContainsOnly(_entityManager, ExactUnreferenced(ConversionType.ConvertHierarchy));
        }

        [TestCaseSource(typeof(TransformBakingTests), nameof(BlankObjectWithOffsetTestCases))]
        public void ConvertBlankGameObject_WithOffset(ConversionType type, TransformUsageFlags flag, object[] matchers)
        {
            var go = _objects.CreateGameObject();
            go.transform.SetPositionAndRotation(new Vector3(4, 5, 6), quaternion.Euler(1, 2, 3));
            go.transform.localScale = (float3)4;
            TransformUsageBaker.Flags = flag;
            Convert(go, type, typeof(TransformUsageBaker));
            EntitiesAssert.ContainsOnly(_entityManager, Exact(type, EntityType.PrimaryEntity, matchers));
        }

        private static readonly quaternion[] ResidualRotations = {
            quaternion.identity,
            quaternion.Euler(1,2,3),
            quaternion.Euler(-16, 0.1f, 2),
        };

        private static readonly float3[] ResidualTranslations =
        {
            float3.zero,
            new float3(0, 0, 1500),
            new float3(-10, 15, 0),
            new float3(0.001f, -1000, 17),
            new float3(1),
        };

        private static readonly float3[] ResidualScale =
        {
            float3.zero,
            new float3(1, 2, 3),
            new float3(1),
            new float3(-1, 1, 0),
            new float3(1, 1, 2)
        };

        static IEnumerable ResidualTransformTestCases
        {
            get
            {
                for (int t = 0; t < ResidualTranslations.Length; t++)
                {
                    for (int r = 0; r < ResidualRotations.Length; r++)
                    {
                        for (int s = 0; s < ResidualScale.Length; s++)
                        {
                            yield return new TestCaseData(ConversionType.ConvertScene, ResidualTranslations[t], ResidualRotations[r], ResidualScale[s]).SetName($"ConvertScene_{t}_{r}_{s}");
                            yield return new TestCaseData(ConversionType.ConvertHierarchy, ResidualTranslations[t], ResidualRotations[r], ResidualScale[s]).SetName($"ConvertHierarchy_{t}_{r}_{s}");
                        }
                    }
                }
            }
        }

        private float4x4 ComputeLocalToWorldFromGlobalAndResidual(Entity e)
        {
            #if TRANSFORM_V2
            var t = _entityManager.GetComponentData<GlobalTranslation>(e).Value;
            var r = _entityManager.GetComponentData<GlobalRotation>(e).Value;
            var s = _entityManager.GetComponentData<GlobalScale>(e).Value;
            float3x3 residual = float3x3.identity;
            if (_entityManager.HasComponent<ResidualTransformation>(e))
                residual = _entityManager.GetComponentData<ResidualTransformation>(e).Value;
            var ltw = float4x4.TRS(t, r, s);
            return math.mul(ltw, new float4x4(residual, 0));
            #else
            return _entityManager.GetComponentData<LocalToWorld>(e).Value;
            #endif
        }

        private static bool IsAlmostZero(float4x4 a)
        {
            const float epsilon = 0.001f;
            return ((a < epsilon) & (a > -epsilon)).Equals(true);
        }

        private static bool IsAlmostZero(float3x3 a)
        {
            const float epsilon = 0.001f;
            return ((a < epsilon) & (a > -epsilon)).Equals(true);
        }

        private static bool IsAlmostZero(float4 a)
        {
            const float epsilon = 0.001f;
            return ((a < epsilon) & (a > -epsilon)).Equals(true);
        }

        private static bool IsAlmostZero(float3 a)
        {
            const float epsilon = 0.001f;
            return ((a < epsilon) & (a > -epsilon)).Equals(true);
        }

        private static bool IsAlmostZero(float a)
        {
            const float epsilon = 0.001f;
            return ((a < epsilon) & (a > -epsilon)).Equals(true);
        }

        [TestCaseSource(typeof(TransformBakingTests), nameof(ResidualTransformTestCases))]
        public void ConvertBlankGameObject_WithTransform_HasCorrectResidual(ConversionType type, float3 t, quaternion r, float3 scale)
        {
            var go = _objects.CreateGameObject();
            go.transform.SetPositionAndRotation(t, r);
            go.transform.localScale = scale;
            TransformUsageBaker.Flags = TransformUsageFlags.ReadGlobalTransform | TransformUsageFlags.ReadResidualTransform;
            Convert(go, type, typeof(TransformUsageBaker));
#if TRANSFORM_V2
            EntitiesAssert.ContainsOnly(_entityManager, EntityMatch.Partial<GlobalRotation, GlobalScale, GlobalTranslation>());
#else
            EntitiesAssert.ContainsOnly(_entityManager, EntityMatch.Partial<LocalToWorld>());
#endif
            var ltw = ComputeLocalToWorldFromGlobalAndResidual(_entityManager.UniversalQuery.GetSingletonEntity());
            Assert.IsTrue(IsAlmostZero(ltw - go.transform.localToWorldMatrix));
        }

        //#define TRANSFORM_V2
#if TRANSFORM_V2
        [Test]
        public void ConvertBlankGameObject_WithResidualTransform_HasCorrectResidual([Values] ConversionType type, [Values] TransformUsageFlags flag)
        {
            var go = _objects.CreateGameObject();
            go.transform.localScale = new float3(1, 2, 3);
            TransformUsageBaker.Flags = flag;
            Convert(go, type, typeof(TransformUsageBaker));
            if (flag == TransformUsageFlags.ReadResidualTransform)
                EntitiesAssert.ContainsOnly(_entityManager, ExactRootWithComponents(type, new[] {typeof(ResidualTransformation)}));
            else
                Assert.IsFalse(_entityManager.HasComponent<ResidualTransformation>(_entityManager.UniversalQuery.GetSingletonEntity()));
        }
#endif

// PREFABS ARE NOT YET SUPPORTED BY BAKING
#if false

        [Test]
        public void ConvertGameObject_WithReferencedPrefab_ButWithoutCallingGetPrimaryEntity_ConvertsPrefabWithoutTransformData([Values]ConversionType type)
        {
            var prefab = ConversionTestHelpers.LoadPrefab("EmptyPrefab");
            var go = _objects.CreateGameObject("GO");
            var authoring = go.AddComponent<PrefabReferenceAuthoring>();
            authoring.Prefab = prefab;
            authoring.CallGetPrimaryEntity = false;

            // Only force the main entity into existence explicitly
            AssignUsageFlagSystem.Flags.Clear();
            AssignUsageFlagSystem.Flags[go.GetInstanceID()] = TransformUsageFlags.None;
            Convert(go, type, typeof(AssignUsageFlagSystem));

            EntitiesAssert.Contains(_entityManager, ExactPrefab());
        }
        [Test]
        [TestCaseSource(typeof(TransformBakingTests), nameof(BlankObjectTestCases))]
        public void ConvertGameObject_WithReferencedPrefab_ConvertsPrefab(ConversionType type, TransformUsageFlags flag, object[] matchers)
        {
            var prefab = ConversionTestHelpers.LoadPrefab("EmptyPrefab");
            var go = _objects.CreateGameObject("GO");
            var authoring = go.AddComponent<PrefabReferenceAuthoring>();
            authoring.Prefab = prefab;
            authoring.TransformUsageFlags = flag;

            // Only force the main entity into existence
            AssignUsageFlagSystem.Flags.Clear();
            AssignUsageFlagSystem.Flags[go.GetInstanceID()] = TransformUsageFlags.None;
            Convert(go, type, typeof(AssignUsageFlagSystem));

            EntitiesAssert.Contains(_entityManager, ExactPrefab(matchers));
        }
#endif

        [Test]
        public void ConvertGameObject_WithAdditionalEntity_IsAttachedToPrimaryEntity_WhenItHasATransform([Values]ConversionType type)
        {
            var root = _objects.CreateGameObject("GameObject");
            AssignTransformUsageBaker.Flags[root] = TransformUsageFlags.Default;
            AssignTransformUsageBaker.AdditionalFlags[root] = TransformUsageFlags.Default;
            Convert(root, type, typeof(AssignTransformUsageBaker));
            EntitiesAssert.ContainsOnly(_entityManager,
#if !ENABLE_TRANSFORM_V1
                Exact(type, EntityType.PrimaryEntity, typeof(LocalTransform), typeof(WorldTransform), typeof(LocalToWorld)),
                Exact(type, EntityType.AdditionalEntity, typeof(LocalTransform), typeof(WorldTransform), typeof(LocalToWorld), typeof(Parent)));
#else
                Exact(type, EntityType.PrimaryEntity, typeof(Translation), typeof(Rotation), typeof(LocalToWorld)),
                Exact(type, EntityType.AdditionalEntity, typeof(Translation), typeof(Rotation), typeof(LocalToWorld), typeof(LocalToParent), typeof(Parent)));
#endif
        }

        [Test]
        public void ConvertGameObject_WithAdditionalEntity_IsNotAttachedToPrimaryEntity_WhenItHasNoTransform([Values]ConversionType type)
        {
            var root = _objects.CreateGameObject("GameObject");
            AssignTransformUsageBaker.Flags[root] = TransformUsageFlags.Default;
            AssignTransformUsageBaker.AdditionalFlags[root] = TransformUsageFlags.None;
            Convert(root, type, typeof(AssignTransformUsageBaker));
            EntitiesAssert.ContainsOnly(_entityManager,
#if !ENABLE_TRANSFORM_V1
                Exact(type, EntityType.PrimaryEntity, typeof(LocalTransform), typeof(WorldTransform), typeof(LocalToWorld)),
                Exact(type, EntityType.AdditionalEntity));
#else
                Exact(type, EntityType.PrimaryEntity, typeof(Translation), typeof(Rotation), typeof(LocalToWorld)),
                Exact(type, EntityType.AdditionalEntity));
#endif
        }

        [Test]
        public void ConvertGameObject_WithAdditionalEntity_IsNotAttachedToPrimaryEntity_WhenPrimaryHasNoTransform([Values]ConversionType type)
        {
            // in this case, there will not be a primary entity!
            var root = _objects.CreateGameObject("GameObject");
            AssignTransformUsageBaker.AdditionalFlags[root] = TransformUsageFlags.Default;

            Convert(root, type, typeof(AssignTransformUsageBaker));
#if !ENABLE_TRANSFORM_V1
            EntitiesAssert.ContainsOnly(_entityManager, ExactUnreferenced(type), Exact(type, EntityType.AdditionalEntity, typeof(LocalTransform), typeof(WorldTransform), typeof(LocalToWorld)));
#else
            EntitiesAssert.ContainsOnly(_entityManager, ExactUnreferenced(type), Exact(type, EntityType.AdditionalEntity, typeof(Rotation), typeof(Translation), typeof(LocalToWorld)));
#endif
        }

        [Test]
        public void ConvertGameObject_WithAdditionalEntity_IsNotAttachedToPrimaryEntity_WhenPrimaryHasManualOverride([Values]ConversionType type)
        {
            var root = _objects.CreateGameObject("GameObject");
            AssignTransformUsageBaker.AdditionalFlags[root] = TransformUsageFlags.Default;
            AssignTransformUsageBaker.Flags[root] = TransformUsageFlags.ManualOverride;

            Convert(root, type, typeof(AssignTransformUsageBaker));
#if !ENABLE_TRANSFORM_V1
            EntitiesAssert.ContainsOnly(_entityManager, Exact(type), Exact(type, EntityType.AdditionalEntity, typeof(LocalTransform), typeof(WorldTransform), typeof(LocalToWorld)));
#else
            EntitiesAssert.ContainsOnly(_entityManager, Exact(type), Exact(type, EntityType.AdditionalEntity, typeof(Translation), typeof(Rotation), typeof(LocalToWorld)));
#endif
        }

        [Test]
        public void ConvertGameObject_WithAdditionalEntity_IsAttachedToFirstParentWithTransformData([Values]ConversionType type)
        {
            // The primary entity of the object that we create the additional entity for will not have transform data,
            // but its parent has. So we expect to attach to that. (In fact, the primary entity does not exist.)
            var root = _objects.CreateGameObject("Root");
            var child = _objects.CreateGameObject("Child");
            child.transform.SetParent(root.transform);

            AssignTransformUsageBaker.Flags[root] = TransformUsageFlags.Default;
            AssignTransformUsageBaker.AdditionalFlags[child] = TransformUsageFlags.Default;

            Convert(root, type, typeof(AssignTransformUsageBaker));

#if !ENABLE_TRANSFORM_V1
            var rootTypes = Exact(type, EntityType.PrimaryEntity, typeof(LocalTransform), typeof(WorldTransform), typeof(LocalToWorld));
            var additionalChildTypes = Exact(type, EntityType.AdditionalEntity, typeof(LocalTransform), typeof(WorldTransform), typeof(LocalToWorld), typeof(Parent));
#else
            var rootTypes = Exact(type, EntityType.PrimaryEntity, typeof(Translation), typeof(Rotation), typeof(LocalToWorld));
            var additionalChildTypes = Exact(type, EntityType.AdditionalEntity, typeof(Translation), typeof(Rotation), typeof(LocalToWorld), typeof(Parent), typeof(LocalToParent));
#endif
            EntitiesAssert.ContainsOnly(_entityManager, rootTypes, additionalChildTypes, ExactUnreferenced(type));

            foreach (var de in DebugEntity.GetAllEntitiesWithSystems(_entityManager))
            {
                if (de.TryGetComponent<Parent>(out var parent))
                {
                    Assert.AreNotEqual(Entity.Null, parent.Value);
                    Assert.IsTrue(_entityManager.Exists(parent.Value));
                    Assert.IsFalse(_entityManager.HasComponent<RemoveUnusedEntityInBake>(parent.Value));
                }
            }
        }

        [Test]
        public void ConvertGameObjectHierarchy_WithWriteGlobalOnChild_MovesChildToRoot([Values] ConversionType type)
        {
            var root = _objects.CreateGameObject("Parent");
            var child = _objects.CreateGameObject("Child");
            child.transform.SetParent(root.transform);
            AssignTransformUsageBaker.Flags[root] = TransformUsageFlags.Default;
            AssignTransformUsageBaker.Flags[child] = TransformUsageFlags.WriteGlobalTransform | TransformUsageFlags.Default;
            Convert(root, type, typeof(AssignTransformUsageBaker));
            EntitiesAssert.ContainsOnly(
                _entityManager,
                EntityMatch.Where(() => "no parent", de => !de.HasComponent<Parent>()),
                EntityMatch.Where(() => "no parent", de => !de.HasComponent<Parent>())
            );
        }


        [Test]
        public void ConvertGameObjectHierarchy_WithStaticRoot_MovesChildToRoot([Values] ConversionType type, [Values(TransformUsageFlags.None, TransformUsageFlags.ReadLocalToWorld, TransformUsageFlags.ReadGlobalTransform)] TransformUsageFlags rootFlags)
        {
            var root = _objects.CreateGameObject("Parent");
            var child = _objects.CreateGameObject("Child");
            child.transform.SetParent(root.transform);
            AssignTransformUsageBaker.Flags[root] = rootFlags;
            AssignTransformUsageBaker.Flags[child] = TransformUsageFlags.Default;
            Convert(root, type, typeof(AssignTransformUsageBaker));
            #if TRANSFORM_V2
            EntitiesAssert.ContainsOnly(
                _entityManager,
                EntityMatch.Where(() => "no parent", de => !de.HasComponent<Parent>()),
                EntityMatch.Where(() => "no parent", de => !de.HasComponent<Parent>())
            );
            #else

            EntityMatch rootMatch;
#if !ENABLE_TRANSFORM_V1
            if (rootFlags == TransformUsageFlags.None)
                rootMatch = Exact(type);
            else if (rootFlags == TransformUsageFlags.ReadGlobalTransform)
                rootMatch = Exact(type, EntityType.PrimaryEntity, typeof(LocalToWorld), typeof(LocalTransform), typeof(WorldTransform));
            else
                rootMatch = Exact(type, EntityType.PrimaryEntity, typeof(LocalToWorld));

            var childMatch = Exact(type, EntityType.PrimaryEntity, typeof(LocalToWorld), typeof(LocalTransform), typeof(WorldTransform));
#else
            if (rootFlags == TransformUsageFlags.None)
                rootMatch = Exact(type);
            else if (rootFlags == TransformUsageFlags.ReadGlobalTransform)
                rootMatch = Exact(type, EntityType.PrimaryEntity, typeof(LocalToWorld), typeof(Rotation), typeof(Translation));
            else
                rootMatch = Exact(type, EntityType.PrimaryEntity, typeof(LocalToWorld));

            var childMatch = Exact(type, EntityType.PrimaryEntity, typeof(LocalToWorld), typeof(Rotation), typeof(Translation));
#endif
            EntitiesAssert.ContainsOnly(_entityManager, rootMatch, childMatch);

            #endif
        }

        [Test]
        public void ConvertGameObjectHierarchy_WithManualOverrideRoot_MovesChildToRoot([Values] ConversionType type)
        {
            var root = _objects.CreateGameObject("Parent");
            var child = _objects.CreateGameObject("Child");
            child.transform.SetParent(root.transform);
            AssignTransformUsageBaker.Flags[root] = TransformUsageFlags.ManualOverride;
            AssignTransformUsageBaker.Flags[child] = TransformUsageFlags.Default;
            Convert(root, type, typeof(AssignTransformUsageBaker));
            EntitiesAssert.ContainsOnly(
                _entityManager,
                EntityMatch.Where(() => "no parent", de => !de.HasComponent<Parent>()),
                EntityMatch.Where(() => "no parent", de => !de.HasComponent<Parent>())
            );
        }

        [Test]
        public void ConvertGameObjectHierarchy_WithManualOverrideChild_ChildHasNoParent([Values] ConversionType type)
        {
            var root = _objects.CreateGameObject("Parent");
            var child = _objects.CreateGameObject("Child");
            child.transform.SetParent(root.transform);
            AssignTransformUsageBaker.Flags[root] = TransformUsageFlags.Default;
            AssignTransformUsageBaker.Flags[child] = TransformUsageFlags.ManualOverride;
            Convert(root, type, typeof(AssignTransformUsageBaker));
            EntitiesAssert.ContainsOnly(
                _entityManager,
                EntityMatch.Where(() => "no parent", de => !de.HasComponent<Parent>()),
                EntityMatch.Where(() => "no parent", de => !de.HasComponent<Parent>())
            );
        }

#if !DOTS_DISABLE_DEBUG_NAMES
        [Test]
#endif
        public void ConvertGameObjectHierarchy_WithIntermediateStatic_IsRemoved([Values] ConversionType type)
        {
            var root = _objects.CreateGameObject("Root");
            var child = _objects.CreateGameObject("Child");
            child.transform.SetParent(root.transform);
            var childChild = _objects.CreateGameObject("ChildChild");
            childChild.transform.SetParent(child.transform);
            AssignTransformUsageBaker.Flags[root] = TransformUsageFlags.Default;
            AssignTransformUsageBaker.Flags[child] = TransformUsageFlags.ReadGlobalTransform;
            AssignTransformUsageBaker.Flags[childChild] = TransformUsageFlags.Default;
            Convert(root, type, typeof(AssignTransformUsageBaker));

            EntitiesAssert.ContainsOnly(
                _entityManager,
                EntityMatch.Where(() => "no parent for root", de => !de.HasComponent<Parent>() && de.Name == "Root"),
                EntityMatch.Where(() => "child has parent", de => de.HasComponent<Parent>() && de.Name == "Child"),
                EntityMatch.Where(() => "child child is parented to root", de => de.HasComponent<Parent>() && de.Name == "ChildChild" && _entityManager.GetName(de.GetComponent<Parent>().Value) == "Root")
            );
        }

#if TRANSFORM_V2
        static IEnumerable<uint> FuzzTestingSeeds()
        {
            for (int i = 0; i < 300; i++)
                yield return (uint) i;
        }

        [Test, Explicit]
        public void ConvertHierarchy_HasExpectedData([ValueSource(nameof(FuzzTestingSeeds))]uint seed)
        {
            Console.WriteLine("Running with seed " + seed);
            // set up a random hierarchy of game objects with a single root
            const int allFlags = (int) (TransformUsageFlags.Default | TransformUsageFlags.ReadGlobalTransform |
                                        TransformUsageFlags.WriteGlobalTransform | TransformUsageFlags.ReadLocalToWorld |
                                        TransformUsageFlags.ReadResidualTransform | TransformUsageFlags.ManualOverride);
            var rng = Mathematics.Random.CreateFromIndex(seed);
            var objs = new List<GameObject>();

            var usageFlags = AssignTransformUsageBaker.Flags;
            GameObject root = null;
            int i = 0;
            do
            {
                var go = _objects.CreateGameObject(i.ToString());
                i++;
                if (objs.Count > 0)
                {
                    var parent = objs[rng.NextInt(objs.Count)];
                    go.transform.SetParent(parent.transform);
                }
                else
                    root = go;

                objs.Add(go);
                go.transform.SetPositionAndRotation(rng.NextFloat3(), rng.NextQuaternionRotation());
                go.transform.localScale = rng.NextFloat3();
                if (rng.NextFloat() < 0.95f)
                    usageFlags.Add(go, (TransformUsageFlags) rng.NextInt(allFlags + 1));
            } while (rng.NextFloat() < 0.95f);

            // convert the hierarchy and get GUIDs
            var settings = MakeDefaultSettings();
            settings.ConversionFlags |= GameObjectConversionUtility.ConversionFlags.AddEntityGUID;
            settings.ExtraSystems = new[] {typeof(AssignTransformUsageBaker)};
            Convert(root, ConversionType.ConvertScene, settings);

            var entities = DebugEntity.GetAllEntities(_entityManager);
            var entitiesByInstanceId = new Dictionary<int, DebugEntity>();
            foreach (var de in entities)
                entitiesByInstanceId.Add(de.GetComponent<EntityGuid>().OriginatingId, de);

            // validate that the hierarchy looks as expected
            foreach (var gameObject in objs)
            {
                var instanceId = gameObject.GetInstanceID();
                bool hasEntity = entitiesByInstanceId.TryGetValue(instanceId, out var de);
                bool hasFlag = usageFlags.TryGetValue(gameObject, out var flags);
                Assert.AreEqual(hasEntity, hasFlag);
                if (!hasFlag)
                    continue;
                Assert.IsTrue(hasEntity, $"Failed to find entity for {gameObject}");

                if (flags == TransformUsageFlags.None || (flags & TransformUsageFlags.ManualOverride) != 0)
                {
                    if (de.Components.Count != 1)
                        _entityManager.Debug.LogEntityInfo(de.Entity);
                    Assert.AreEqual(1, de.Components.Count);
                    AssertComponent<EntityGuid>();
                    continue;
                }

                if ((flags & (TransformUsageFlags.Default | TransformUsageFlags.ReadGlobalTransform | TransformUsageFlags.WriteGlobalTransform)) != 0)
                {
                    AssertComponent<GlobalTranslation>();
                    AssertComponent<GlobalRotation>();
                    AssertComponent<GlobalScale>();
                }

                if ((flags & TransformUsageFlags.ReadLocalToWorld) != 0)
                    AssertComponent<LocalToWorld>();
                else
                    AssertNoComponent<LocalToWorld>();

                if ((flags & (TransformUsageFlags.Default | TransformUsageFlags.ReadLocalToWorld)) == 0)
                {
                    AssertNoComponent<LocalTranslation>();
                    AssertNoComponent<LocalRotation>();
                    AssertNoComponent<LocalScale>();
                }

                if ((flags & TransformUsageFlags.WriteGlobalTransform) != 0)
                {
                    AssertNoComponent<Parent>();
                    AssertNoComponent<ParentTransform>();
                }

                if (de.HasComponent<ResidualTransformation>())
                {
                    Assert.AreEqual(TransformUsageFlags.ReadResidualTransform,
                        flags & TransformUsageFlags.ReadResidualTransform);
                    var rt = de.GetComponent<ResidualTransformation>();
                    Assert.IsFalse(IsAlmostZero(rt.Value - float3x3.identity));
                }

                if ((flags & TransformUsageFlags.ReadLocalToWorld) != 0 &&
                    (flags & TransformUsageFlags.ReadResidualTransform) != 0 &&
                    (flags & TransformUsageFlags.ReadGlobalTransform) != 0)
                {
                    // make sure that multiplying out LTW is the same as global TRS * Residual Transform
                    var ltw = de.GetComponent<LocalToWorld>().Value;
                    var globalT = de.GetComponent<GlobalTranslation>().Value;
                    var globalR = de.GetComponent<GlobalRotation>().Value;
                    var globalS = de.GetComponent<GlobalScale>().Value;
                    var residual = de.GetComponent<ResidualTransformation>().Value;

                    var computedLtw = math.mul(float4x4.TRS(globalT, globalR, globalS),
                        new float4x4(residual, float3.zero));
                    Assert.IsTrue(IsAlmostZero(computedLtw - ltw));
                }

                if (de.HasComponent<Parent>())
                {
                    var parent = de.GetComponent<Parent>().Value;
                    Assert.IsTrue(_entityManager.Exists(parent), "If there is a parent, it must be valid.");
                    var parentDe = new DebugEntity(_entityManager, parent);

                    if ((flags & (TransformUsageFlags.Default | TransformUsageFlags.ReadLocalToWorld)) != 0)
                    {
                        AssertComponent<LocalTranslation>();
                        AssertComponent<LocalRotation>();
                        AssertComponent<LocalScale>();
                        AssertComponent<ParentTransform>();
                        Assert.IsTrue(parentDe.HasComponent<GlobalTranslation>());
                        Assert.IsTrue(parentDe.HasComponent<GlobalRotation>());
                        Assert.IsTrue(parentDe.HasComponent<GlobalScale>());

                        // check that parent transform matches
                        var parentTransformData = de.GetComponent<ParentTransform>();
                        Assert.AreEqual(parentDe.GetComponent<GlobalTranslation>().Value, parentTransformData.Translation);
                        Assert.AreEqual(parentDe.GetComponent<GlobalRotation>().Value.value, parentTransformData.Rotation.value);
                        Assert.AreEqual(parentDe.GetComponent<GlobalScale>().Value, parentTransformData.Scale);

                        // check that multiplying the data together (local + parent TRS) yields global TRS
                        var matrix = float4x4.TRS(parentTransformData.Translation, parentTransformData.Rotation,
                            parentTransformData.Scale);
                        Assert.IsTrue(IsAlmostZero(math.transform(matrix, de.GetComponent<LocalTranslation>().Value) - de.GetComponent<GlobalTranslation>().Value));
                        Assert.IsTrue(IsAlmostZero(
                            math.mul(parentTransformData.Rotation, de.GetComponent<LocalRotation>().Value).value -
                            de.GetComponent<GlobalRotation>().Value.value));
                        Assert.IsTrue(IsAlmostZero(parentTransformData.Scale * de.GetComponent<LocalScale>().Value - de.GetComponent<GlobalScale>().Value));
                    }

                    // check that the game object that the parent comes from is actually writable
                    var entityParent = de.GetComponent<Parent>().Value;
                    var entityParentInstanceId = _entityManager.GetComponentData<EntityGuid>(entityParent).OriginatingId;
                    Assert.AreNotEqual(TransformUsageFlags.None, usageFlags[entityParentInstanceId] & TransformUsageFlags.WriteFlags, $"An object should only have children if it is writable. Found {usageFlags[entityParentInstanceId]}");

                    // check that between this game object and the game object of the parent there is no other writable
                    // game object
                    var parentTransform = gameObject.transform.parent;
                    var goParentInstanceId = 0;
                    while ((goParentInstanceId = parentTransform.gameObject.GetInstanceID()) != entityParentInstanceId)
                    {
                        bool parentHasFlags = usageFlags.TryGetValue(goParentInstanceId, out var goParentFlags);
                        if (parentHasFlags && (goParentFlags & TransformUsageFlags.ManualOverride) == 0 && (goParentFlags & TransformUsageFlags.WriteFlags) != 0)
                            Assert.Fail($"No intermediate parent should be a writable. Found {usageFlags[goParentInstanceId]}");
                        parentTransform = parentTransform.parent;
                    }
                }

                void AssertNoComponent<T>() => Assert.IsFalse(de.HasComponent<T>(),
                    $"Entity for {gameObject} should not have component {typeof(T).Name} - flags are {flags}");

                void AssertComponent<T>() => Assert.IsTrue(de.HasComponent<T>(),
                    $"Entity for {gameObject} should have component {typeof(T).Name} - flags are {flags}");
            }
        }
#endif
    }
}
