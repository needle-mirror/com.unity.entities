using System;
using System.Linq;
using NUnit.Framework;
using Unity.Entities.Conversion;

namespace Unity.Entities.Tests.Conversion
{
    class MultiListTests
    {
        MultiList<string, MultiListArrayData<string>> m_MultiList;

        [SetUp]
        public void SetUp()
        {
            m_MultiList.Init();
            m_MultiList.EnsureCapacity(10);
            m_MultiList.SetHeadIdsCapacity(10);
        }

        [TearDown]
        public void TearDown()
        {
            MultiListDebugUtility.ValidateIntegrity(ref m_MultiList);
            m_MultiList.Dispose();
        }

        [Test]
        public void AddHead_AfterRelease_ReusesCapacity()
        {
            m_MultiList.Dispose();
            m_MultiList.Init();
            m_MultiList.EnsureCapacity(2);
            m_MultiList.SetHeadIdsCapacity(2);

            Assert.That(m_MultiList.Data.Data.Length, Is.GreaterThanOrEqualTo(2));
            var oldHeadIds = m_MultiList.HeadIds;
            var oldNext = m_MultiList.Next;
            var oldData = m_MultiList.Data;

            m_MultiList.AddHead(0, "0");
            m_MultiList.AddHead(1, "1");

            m_MultiList.ReleaseList(1);

            Assert.That(m_MultiList.HeadIds, Is.EqualTo(oldHeadIds));
            Assert.That(m_MultiList.Next, Is.EqualTo(oldNext));
            Assert.That(m_MultiList.Data, Is.EqualTo(oldData));

            m_MultiList.AddHead(1, "2");

            Assert.That(m_MultiList.HeadIds, Is.EqualTo(oldHeadIds));
            Assert.That(m_MultiList.Next, Is.EqualTo(oldNext));
            Assert.That(m_MultiList.Data, Is.EqualTo(oldData));
        }

        [Test]
        public void AddHead_WithReusedHeadId_Throws()
        {
            m_MultiList.AddHead(0, "0");
            Assert.Throws<ArgumentException>(() => m_MultiList.AddHead(0, "0a"));
            m_MultiList.AddHead(1, "1");
            Assert.Throws<ArgumentException>(() => m_MultiList.AddHead(0, "0b"));
            Assert.Throws<ArgumentException>(() => m_MultiList.AddHead(1, "1a"));
        }

        [Test]
        public void AddVarious_WithInvalidId_Throws()
        {
            const int invalid = -1;
            var outOfRange = m_MultiList.HeadIds.Length;

            Assert.Throws<IndexOutOfRangeException>(() => m_MultiList.AddHead(invalid, "0"));
            Assert.Throws<IndexOutOfRangeException>(() => m_MultiList.AddHead(outOfRange, "0"));

            Assert.Throws<IndexOutOfRangeException>(() => m_MultiList.Add(invalid, "0"));
            Assert.Throws<IndexOutOfRangeException>(() => m_MultiList.Add(outOfRange, "0"));

            Assert.Throws<IndexOutOfRangeException>(() => m_MultiList.AddTail(invalid));
            Assert.Throws<IndexOutOfRangeException>(() => m_MultiList.AddTail(outOfRange));

            Assert.Throws<IndexOutOfRangeException>(() => m_MultiList.AddTail(invalid, "0"));
            Assert.Throws<IndexOutOfRangeException>(() => m_MultiList.AddTail(outOfRange, "0"));

            Assert.Throws<IndexOutOfRangeException>(() => m_MultiList.ReleaseList(invalid));
            Assert.Throws<IndexOutOfRangeException>(() => m_MultiList.ReleaseList(outOfRange));

            Assert.Throws<IndexOutOfRangeException>(() => m_MultiList.ReleaseListKeepHead(invalid));
            Assert.Throws<IndexOutOfRangeException>(() => m_MultiList.ReleaseListKeepHead(outOfRange));

            Assert.Throws<IndexOutOfRangeException>(() => m_MultiList.SelectList(invalid));
            Assert.Throws<IndexOutOfRangeException>(() => m_MultiList.SelectList(outOfRange));
        }

        [Test]
        public unsafe void AddTailMultiple_WithoutHead_Throws()
        {
            Assert.Throws<IndexOutOfRangeException>(() => m_MultiList.AddTail(0, null, 0));
        }

        [Test]
        public unsafe void AddTailMultiple_WithZeroCount_DoesNotAdd()
        {
            m_MultiList.AddHead(0, "0a");
            m_MultiList.AddTail(0, null, 0);
            MultiListDebugUtility.ValidateIntegrity(ref m_MultiList);
            var data = MultiListDebugUtility.SelectAllData(m_MultiList);
            Assert.That(data, Is.EqualTo(new[] {new[] {"0a"}}));
        }

        [Test]
        public unsafe void AddTailMultiple_WithSingle_AddsSingle()
        {
            m_MultiList.AddHead(0, "0a");
            int id;
            m_MultiList.AddTail(0, &id, 1);
            m_MultiList.Data.Data[id] = "0b";
            MultiListDebugUtility.ValidateIntegrity(ref m_MultiList);
            var data = MultiListDebugUtility.SelectAllData(m_MultiList);
            Assert.That(data, Is.EqualTo(new[] {new[] {"0a", "0b"}}));
        }

        [Test]
        public unsafe void AddTailMultiple_WithMultiple_AddsMultiple()
        {
            m_MultiList.AddHead(0, "0a");
            int* ids = stackalloc int[5];
            m_MultiList.AddTail(0, ids, 5);
            m_MultiList.Data.Data[ids[0]] = "0b";
            m_MultiList.Data.Data[ids[1]] = "0c";
            m_MultiList.Data.Data[ids[2]] = "0d";
            m_MultiList.Data.Data[ids[3]] = "0e";
            m_MultiList.Data.Data[ids[4]] = "0f";
            MultiListDebugUtility.ValidateIntegrity(ref m_MultiList);
            var data = MultiListDebugUtility.SelectAllData(m_MultiList);
            Assert.That(data, Is.EqualTo(new[] {new[] {"0a", "0b", "0c", "0d", "0e", "0f"}}));
        }

        [Test]
        public void Add_BuildsNewList()
        {
            m_MultiList.Add(0, "0a"); // add head
            m_MultiList.Add(0, "0b"); // add after head
            m_MultiList.Add(0, "0c"); // add after head
            m_MultiList.Add(0, "0d"); // add after head

            Assert.That(m_MultiList.HeadIds.Take(1), Is.EqualTo(new[] { 0 }));
            Assert.That(m_MultiList.Next.Take(4), Is.EqualTo(new[] { 3, -1, 1, 2 }));

            var data = MultiListDebugUtility.SelectAllData(m_MultiList);
            Assert.That(data, Is.EqualTo(new[] { new[] { "0a", "0d", "0c", "0b" }, }));
        }

        [Test]
        public void Add_WithInterleavingAddHead_ProperlyLinksLists()
        {
            m_MultiList.AddHead(0, "0");
            m_MultiList.Add(0, "0a");
            m_MultiList.AddHead(1, "1");
            m_MultiList.Add(0, "0b");
            m_MultiList.Add(2, "2");

            var data = MultiListDebugUtility.SelectAllData(m_MultiList).ToList();

            Assert.That(data.Count, Is.EqualTo(3));
            Assert.That(data[0], Is.EquivalentTo(new[] { "0", "0b", "0a" }));
            Assert.That(data[1], Is.EquivalentTo(new[] { "1" }));
            Assert.That(data[2], Is.EquivalentTo(new[] { "2" }));
        }

        [Test]
        public void AddTail_WithNoHead_Throws()
        {
            Assert.Throws<IndexOutOfRangeException>(() => m_MultiList.AddTail(0, "0"));

            m_MultiList.AddHead(0, "0");
            Assert.DoesNotThrow(() => m_MultiList.AddTail(0, "0"));

            Assert.Throws<IndexOutOfRangeException>(() => m_MultiList.AddTail(1, "1"));
        }

        [Test]
        public void AddTail_WithExistingHead_AddsEnd()
        {
            m_MultiList.AddHead(0, "0");
            m_MultiList.AddTail(0, "0a");
            m_MultiList.AddHead(1, "1");
            m_MultiList.AddTail(0, "0b");
            var serial = m_MultiList.AddTail(0, "0c").serial;

            Assert.That(serial, Is.EqualTo(3));
            Assert.That(MultiListDebugUtility.SelectAllData(m_MultiList), Is.EqualTo(new[]
            {
                new[] { "0", "0a", "0b", "0c" },
                new[] { "1" }
            }));
        }

        [Test]
        public void AddTail_WithDeferredDataSet_Matches()
        {
            m_MultiList.AddHead(0, "0");
            m_MultiList.AddTail(0, "0a");
            var added = m_MultiList.AddTail(0);
            m_MultiList.AddTail(0, "0c");

            Assert.That(MultiListDebugUtility.SelectAllData(m_MultiList), Is.EqualTo(
                new[] { new[] { "0", "0a", null, "0c" } }));

            m_MultiList.Data.Data[added.id] = "0b";

            Assert.That(MultiListDebugUtility.SelectAllData(m_MultiList), Is.EqualTo(
                new[] { new[] { "0", "0a", "0b", "0c" } }));
        }

        [Test]
        public void ReleaseList_WithHead_ReleasesEntireList()
        {
            m_MultiList.AddHead(0, "0a");
            m_MultiList.AddTail(0, "0b");
            m_MultiList.AddHead(1, "1a");
            m_MultiList.AddTail(0, "0c");
            m_MultiList.AddTail(1, "1b");

            var released = m_MultiList.ReleaseList(0);

            Assert.That(released, Is.EqualTo(3));

            Assert.That(m_MultiList.HeadIds.Take(2), Is.EqualTo(new[] { -1, 2 }));

            Assert.That(MultiListDebugUtility.SelectAllData(m_MultiList), Is.EqualTo(
                new[] { new[] { "1a", "1b" } }));
        }

        [Test]
        public void ReleaseList_WithNoHead_DoesNothing()
        {
            m_MultiList.ReleaseList(0);
            Assert.That(m_MultiList.HeadIds[0], Is.EqualTo(-1));

            m_MultiList.Add(0, "0");
            Assert.That(m_MultiList.HeadIds[0], Is.EqualTo(0));

            m_MultiList.ReleaseList(0);
            Assert.That(m_MultiList.HeadIds[0], Is.EqualTo(-1));

            m_MultiList.ReleaseList(0);
            Assert.That(m_MultiList.HeadIds[0], Is.EqualTo(-1));
        }

        [Test]
        public void ReleaseListKeepHead_WithNoHead_Throws()
        {
            Assert.Throws<IndexOutOfRangeException>(() => m_MultiList.ReleaseListKeepHead(0));
        }

        [Test]
        public void ReleaseListKeepHead_WithHead_ReleasesEntireListButKeepsHead()
        {
            m_MultiList.AddHead(0, "0a");
            m_MultiList.AddTail(0, "0b");
            m_MultiList.AddHead(1, "1a");
            m_MultiList.AddTail(0, "0c");
            m_MultiList.AddTail(1, "1b");

            var released = m_MultiList.ReleaseListKeepHead(0);

            Assert.That(released, Is.EqualTo(2));

            Assert.That(m_MultiList.HeadIds.Take(2), Is.EqualTo(new[] { 0, 2 }));

            Assert.That(MultiListDebugUtility.SelectAllData(m_MultiList), Is.EqualTo(
                new[] { new[] { "0a" }, new[] { "1a", "1b" } }));
        }

        [Test]
        public void EnumeratorMoveNext_WithDefault_ReturnsEmpty()
        {
            using (var e = m_MultiList.SelectListAt(-1))
            {
                Assert.That(e.MoveNext(), Is.False);
            }

            using (var e = MultiListEnumerator<string, MultiListArrayData<string>>.Empty)
            {
                Assert.That(e.MoveNext(), Is.False);
            }
        }

        [Test]
        public void EnumeratorIsEmpty_WithEmpty_ReturnsTrue()
        {
            Assert.IsTrue(m_MultiList.SelectListAt(-1).IsEmpty);
            Assert.IsFalse(m_MultiList.SelectListAt(-1).Any);
            Assert.That(m_MultiList.SelectListAt(-1), Is.Empty);
        }

        [Test]
        public void EnumeratorIsEmpty_WithNonEmpty_ReturnsFalse()
        {
            Assert.IsFalse(m_MultiList.SelectListAt(0).IsEmpty);
            Assert.IsTrue(m_MultiList.SelectListAt(0).Any);
            Assert.That(m_MultiList.SelectListAt(0), Is.Not.Empty);
        }

        [Test]
        public void EnumeratorCount()
        {
            Assert.That(m_MultiList.SelectListAt(-1).Count(), Is.EqualTo(0));

            m_MultiList.Add(0, "0");
            Assert.That(m_MultiList.SelectListAt(0).Count(), Is.EqualTo(1));

            m_MultiList.Add(0, "1");
            Assert.That(m_MultiList.SelectListAt(0).Count(), Is.EqualTo(2));

            m_MultiList.Add(0, "2");
            Assert.That(m_MultiList.SelectListAt(0).Count(), Is.EqualTo(3));
        }

        [Test]
        public void EnumeratorCurrent_WithDefault_Throws()
        {
            using (var e = MultiListEnumerator<string, MultiListArrayData<string>>.Empty)
            {
                // ReSharper disable once NotAccessedVariable
                string s;
                Assert.Throws<NullReferenceException>(() => s = e.Current);
            }
        }

        [Test]
        public void EnumeratorIteration_WithItems_ReturnsItems()
        {
            m_MultiList.Add(0, "0");

            // double test to ensure no reuse of state in collection
            Assert.That(m_MultiList.SelectListAt(0).Count(), Is.EqualTo(1));
            Assert.That(m_MultiList.SelectListAt(0).Count(), Is.EqualTo(1));

            m_MultiList.Add(0, "0");

            Assert.That(m_MultiList.SelectListAt(0).Count(), Is.EqualTo(2));
            Assert.That(m_MultiList.SelectListAt(0).Count(), Is.EqualTo(2));
        }

        [Test]
        public void EnumeratorMoveNext_WithAttemptToMovePastEnd_Throws()
        {
            m_MultiList.Add(0, "0");

            using (var e = m_MultiList.SelectListAt(0))
            {
                Assert.That(e.MoveNext(), Is.True);
                Assert.That(e.MoveNext(), Is.False);
                Assert.Throws<IndexOutOfRangeException>(() => e.MoveNext());
            }
        }

        [Test]
        public void EnumeratorCurrent_WithInvalidState_Throws()
        {
            m_MultiList.Add(0, "0");

            using (var e = m_MultiList.SelectListAt(0))
            {
                string s = null;
                Assert.Throws<IndexOutOfRangeException>(() => s = e.Current);

                e.MoveNext();

                Assert.DoesNotThrow(() => s = e.Current);
                Assert.That(s, Is.EqualTo("0"));

                e.MoveNext();

                Assert.Throws<IndexOutOfRangeException>(() => s = e.Current);
            }
        }

        [Test]
        public void Enumerator_WithReset_RestartsIteration()
        {
            m_MultiList.Add(0, "0a");
            m_MultiList.Add(0, "0b");

            using (var e = m_MultiList.SelectListAt(0))
            {
                Assert.That(e.MoveNext(), Is.True);
                Assert.That(e.Current, Is.EqualTo("0a"));

                e.Reset();

                Assert.That(e.MoveNext(), Is.True);
                Assert.That(e.Current, Is.EqualTo("0a"));
                Assert.That(e.MoveNext(), Is.True);
                Assert.That(e.Current, Is.EqualTo("0b"));

                e.Reset();

                Assert.That(e.MoveNext(), Is.True);
                Assert.That(e.Current, Is.EqualTo("0a"));
                Assert.That(e.MoveNext(), Is.True);
                Assert.That(e.Current, Is.EqualTo("0b"));

                Assert.That(e.MoveNext(), Is.False);
            }
        }

        [Test]
        public void EnumeratorIterationFromHeadIdIndices_WithItems_ReturnsItems()
        {
            m_MultiList.Add(0, "0a");
            m_MultiList.Add(0, "0b");
            m_MultiList.Add(1, "1a");
            m_MultiList.Add(0, "0c");
            m_MultiList.Add(1, "1b");

            using (var enumerator = m_MultiList.SelectList(0))
            {
                Assert.That(enumerator, Is.EqualTo(new[] { "0a", "0c", "0b" }));
            }

            using (var enumerator = m_MultiList.SelectListAt(m_MultiList.HeadIds[0]))
            {
                Assert.That(enumerator, Is.EqualTo(new[] { "0a", "0c", "0b" }));
            }

            using (var enumerator = m_MultiList.SelectList(1))
            {
                Assert.That(enumerator, Is.EqualTo(new[] { "1a", "1b" }));
            }

            using (var enumerator = m_MultiList.SelectListAt(m_MultiList.HeadIds[1]))
            {
                Assert.That(enumerator, Is.EqualTo(new[] { "1a", "1b" }));
            }
        }
    }
}
