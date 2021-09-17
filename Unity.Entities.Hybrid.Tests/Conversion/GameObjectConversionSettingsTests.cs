using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Build;
using Unity.Entities.Conversion;

namespace Unity.Entities.Tests.Conversion
{
    class GameObjectConversionSettingsTests
    {
        [Test]
        public void WithExtraSystems_WithRedundantCall_Throws()
        {
            var settings = new GameObjectConversionSettings();
            settings.WithExtraSystem<int>();

            Assert.That(() => settings.WithExtraSystem<float>(), Throws.Exception
                .With.TypeOf<InvalidOperationException>()
                .With.Message.Contains("already initialized"));
        }

        [UpdateInGroup(typeof(GameObjectBeforeConversionGroup))]
        class TestConversionSystem : GameObjectConversionSystem
        {
            protected override void OnUpdate()
            {
            }
        }

        [Test]
        public void Systems_OnlySystemsFromListAndGameObjectConversionSystemAreAdded()
        {
            using (var world = new World("test world"))
            {
                var settings = new GameObjectConversionSettings
                {
                    DestinationWorld          = world,
                    SceneGUID                 = new Hash128(1, 2, 3, 4),
                    DebugConversionName       = "test name",
                    ConversionFlags           = GameObjectConversionUtility.ConversionFlags.AddEntityGUID,
#if UNITY_EDITOR
                    BuildConfiguration        = BuildConfiguration.CreateInstance(),
#endif
                    Systems                   = new List<Type> {typeof(TestConversionSystem)},
#pragma warning disable 0618
                    NamespaceID               = 123,
#pragma warning restore 0618
                    ConversionWorldCreated    = _ => {},
                    ConversionWorldPreDispose = _ => {},
                };
                using (var conversionWorld = settings.CreateConversionWorld())
                {
                    foreach (var system in conversionWorld.Systems)
                    {
                        if (system is ComponentSystemGroup || system == null)
                            continue;
                        Assert.That(system is TestConversionSystem || system is GameObjectConversionMappingSystem || system is IncrementalChangesSystem, $"System is of unexpected type {system.GetType()}");
                    }
                }
            }
        }
    }
}
