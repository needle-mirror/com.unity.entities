using NUnit.Framework;

namespace Unity.Entities.Tests
{
    class ArchetypeTests : ECSTestsFixture
    {
        [Test]
        unsafe public void DiffArchetype_AddRemove()
        {
            var before = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData3), typeof(EcsTestData4));
            var after = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3), typeof(EcsTestData5));

            var added = stackalloc int[after.TypesCount];
            var removed = stackalloc int[before.TypesCount];

            EntityArchetype.CalculateDifference(before, after, added, out var addedTypesCount, removed, out var removedTypesCount);

            Assert.AreEqual(2, addedTypesCount);
            Assert.AreEqual(TypeManager.GetTypeIndex(typeof(EcsTestData2)), added[0]);
            Assert.AreEqual(TypeManager.GetTypeIndex(typeof(EcsTestData5)), added[1]);
            Assert.AreEqual(1, removedTypesCount);
            Assert.AreEqual(TypeManager.GetTypeIndex(typeof(EcsTestData4)), removed[0]);
        }

        [Test]
        unsafe public void DiffArchetype_AddEmpty()
        {
            var before = m_Manager.CreateArchetype();
            var after = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2));

            var added = stackalloc int[after.TypesCount];
            var removed = stackalloc int[before.TypesCount];

            EntityArchetype.CalculateDifference(before, after, added, out var addedTypesCount, removed, out var removedTypesCount);

            Assert.AreEqual(2, addedTypesCount);
            Assert.AreEqual(TypeManager.GetTypeIndex(typeof(EcsTestData)), added[0]);
            Assert.AreEqual(TypeManager.GetTypeIndex(typeof(EcsTestData2)), added[1]);
            Assert.AreEqual(0, removedTypesCount);
        }

        [Test]
        unsafe public void DiffArchetype_RemoveEmpty()
        {
            var before = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2));
            var after = m_Manager.CreateArchetype();

            var added = stackalloc int[after.TypesCount];
            var removed = stackalloc int[before.TypesCount];

            EntityArchetype.CalculateDifference(before, after, added, out var addedTypesCount, removed, out var removedTypesCount);

            Assert.AreEqual(2, removedTypesCount);
            Assert.AreEqual(TypeManager.GetTypeIndex(typeof(EcsTestData)), removed[0]);
            Assert.AreEqual(TypeManager.GetTypeIndex(typeof(EcsTestData2)), removed[1]);
            Assert.AreEqual(0, addedTypesCount);
        }

        [Test]
        unsafe public void DiffArchetype_NoChange()
        {
            var before = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2));
            var after = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2));

            var added = stackalloc int[after.TypesCount];
            var removed = stackalloc int[before.TypesCount];

            EntityArchetype.CalculateDifference(before, after, added, out var addedTypesCount, removed, out var removedTypesCount);

            Assert.AreEqual(0, removedTypesCount);
            Assert.AreEqual(0, addedTypesCount);
        }

        [Test]
        unsafe public void DiffArchetype_EmptyEmpty()
        {
            var before = m_Manager.CreateArchetype();
            var after = m_Manager.CreateArchetype();

            var added = stackalloc int[after.TypesCount];
            var removed = stackalloc int[before.TypesCount];

            EntityArchetype.CalculateDifference(before, after, added, out var addedTypesCount, removed, out var removedTypesCount);

            Assert.AreEqual(0, removedTypesCount);
            Assert.AreEqual(0, addedTypesCount);
        }
    }
}
