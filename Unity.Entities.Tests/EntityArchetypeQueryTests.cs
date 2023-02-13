using System;
using NUnit.Framework;
using Unity.Collections;

namespace Unity.Entities.Tests
{
    class EntityArchetypeQueryTests : ECSTestsFixture
    {
        [Test]
        [TestRequiresDotsDebugOrCollectionChecks("Test requires entity query safety checks")]
        public void EntityQueryFilter_IdenticalIds_InDifferentFilters_Throws()
        {
            Assert.Throws<EntityQueryDescValidationException>(() =>
            {
                new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<EcsTestData>()
                    .WithNone<EcsTestData>()
                    .Build(m_Manager);
            });
        }

        [Test]
        [TestRequiresDotsDebugOrCollectionChecks("Test requires entity query safety checks")]
        public void EntityQueryFilter_IdenticalIds_InSameFilter_Throws()
        {
            Assert.Throws<EntityQueryDescValidationException>(() =>
            {
                new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<EcsTestData,EcsTestData>()
                    .Build(m_Manager);
            });
        }

        [Test]
        [TestRequiresDotsDebugOrCollectionChecks("Test requires entity query safety checks")]
        public void EntityQueryFilter_MultipleIdenticalIds_Throws()
        {
            Assert.Throws<EntityQueryDescValidationException>(() =>
            {
                new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<EcsTestData,EcsTestData2>()
                    .WithNone<EcsTestData3,EcsTestData4>()
                    .WithAny<EcsTestData,EcsTestData4>()
                    .Build(m_Manager);
            });
        }

        [Test]
        public void EntityQueryFilter_SeparatedIds()
        {
            Assert.DoesNotThrow(() =>
            {
                new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<EcsTestData,EcsTestData2>()
                    .WithNone<EcsTestData3,EcsTestData4>()
                    .WithAny<EcsTestData5>()
                    .Build(m_Manager);
            });
        }

        [Test]
        [TestRequiresDotsDebugOrCollectionChecks("Test requires entity query safety checks")]
        public void EntityQueryFilter_CannotContainExcludeComponentType_All_Throws()
        {
            var queryDesc = new EntityQueryDesc
            {
                All = new ComponentType[] {typeof(EcsTestData), ComponentType.Exclude<EcsTestData2>() },
            };

            Assert.Throws<ArgumentException>(() =>
            {
                queryDesc.Validate();
            });
        }

        [Test]
        [TestRequiresDotsDebugOrCollectionChecks("Test requires entity query safety checks")]
        public void EntityQueryFilterCannotContainExcludeComponentType_Any_Throws()
        {
            var queryDesc = new EntityQueryDesc
            {
                Any = new ComponentType[] {typeof(EcsTestData), ComponentType.Exclude<EcsTestData2>() },
            };

            Assert.Throws<ArgumentException>(() =>
            {
                queryDesc.Validate();
            });
        }

        [Test]
        [TestRequiresDotsDebugOrCollectionChecks("Test requires entity query safety checks")]
        public void EntityQueryFilterCannotContainExcludeComponentType_None_Throws()
        {
            var queryDesc = new EntityQueryDesc
            {
                All = new ComponentType[] {typeof(EcsTestData) },
                None = new ComponentType[] {typeof(EcsTestData3), ComponentType.Exclude<EcsTestData4>() },
            };

            Assert.Throws<ArgumentException>(() =>
            {
                queryDesc.Validate();
            });
        }

        [Test]
        [TestRequiresDotsDebugOrCollectionChecks("Test requires entity query safety checks")]
        public void EntityQueryFilterCannotContainExcludeComponentType_Disabled_Throws()
        {
            var queryDesc = new EntityQueryDesc
            {
                Disabled = new ComponentType[] {typeof(EcsTestDataEnableable), ComponentType.Exclude<EcsTestDataEnableable2>() },
            };

            Assert.Throws<ArgumentException>(() =>
            {
                queryDesc.Validate();
            });
        }

        [Test]
        [TestRequiresDotsDebugOrCollectionChecks("Test requires entity query safety checks")]
        public void EntityQueryFilterCannotContainExcludeComponentType_Absent_Throws()
        {
            var queryDesc = new EntityQueryDesc
            {
                All = new ComponentType[] {typeof(EcsTestData) },
                Absent = new ComponentType[] {typeof(EcsTestData3), ComponentType.Exclude<EcsTestData4>() },
            };

            Assert.Throws<ArgumentException>(() =>
            {
                queryDesc.Validate();
            });
        }
    }
}
