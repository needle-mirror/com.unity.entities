using System.Collections;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.NotBurstCompatible;

// ReSharper disable StringLiteralTypo
namespace Unity.Entities.Editor.Tests
{
    // NOTE: For `GetMatches` tests, see: EntityWindowIntegrationTests.cs (Specifically `Search_NameSearch()`)
    class EntityItemSearchUtilsTests
    {
        public static IEnumerable GetTestCases()
        {
            yield return new TestCaseData(new [] { "Token" }, new[] { "token" }).SetName("Tokens are converted to lowercase");
            yield return new TestCaseData(new [] { "BBBB", "D", "AAA", "CC", "AAAAA" }, new[] { "aaaaa", "bbbb", "cc", "d" }).SetName("Tokens are sorted by length and redundancies are removed");
            yield return new TestCaseData(new [] { "GameObject", "c:Rotation", "C:Translation" }, new[] { "gameobject" }).SetName("Component tokens are excluded");
            yield return new TestCaseData(new [] { "GameObject", "\"quoted\"" }, new[] { "gameobject", "quoted" }).SetName("Quote pairs are stripped");
            yield return new TestCaseData(new [] { "\"Game Object (22)\"" }, new[] { "game object (22)" }).SetName("Quoted sentences are preserved");
            yield return new TestCaseData(new [] { "GameObject", "\"\"" }, new[] { "gameobject" }).SetName("Empty quotes are stripped");
            yield return new TestCaseData(new [] { "GameObject", "\"" }, new[] { "gameobject" }).SetName("Unmatched quote characters are stripped");
            yield return new TestCaseData(new [] { "GameObject", "\"Hi", "Hello\"" }, new[] { "gameobject", "hello\"", "\"hi" }).SetName("Quote characters are not stripped when part of a valid token");
            yield return new TestCaseData(new [] { "c:Rotation", "\"", "\"\"" }, new string[0]).SetName("Returns an empty list if necessary");
        }

        [TestCaseSource(nameof(GetTestCases))]
        public void PreProcessTokensTestRunner(string[] input, string[] expected)
        {
            using var output = new NativeList<FixedString64Bytes>(Allocator.Temp);
            HierarchyItemSearchUtils.PreProcessTokens(input, output);

            // NOTE: NativeList does not implement GetEnumerator in a way that is understandable by the Assertion engine,
            // so we convert them to plain C# arrays before comparing them.
            Assert.That(output.ToArrayNBC(), Is.EqualTo(expected));
        }

        [Test]
        public void PreProcessTokens_ClearsInitialList()
        {
            var input = new [] { "GameObject" };
            var expectedOutput = new[] { "gameobject" };

            // Pre-fill the output with garbage
            using var output = new NativeList<FixedString64Bytes>(Allocator.Temp){ "Some Garbage", "Some more, why not" };

            HierarchyItemSearchUtils.PreProcessTokens(input, output);

            Assert.That(output.ToArrayNBC(), Is.EqualTo(expectedOutput));
        }

        [Test]
        public void PreProcessTokens_DoesNotThrow_WhenTruncatingTokens()
        {
            // FixedString will throw an error if truncation occurs while copying data from a string.
            // No matter what happens, we don't want to throw when a search token is above the limit of characters.
            // If this is a problem, it should be handled somewhere else during validation (ideally in `SearchElement`).

            var longToken = new string('s', FixedString64Bytes.utf8MaxLengthInBytes + 1);
            var truncatedToken = new string('s', FixedString64Bytes.utf8MaxLengthInBytes);

            var input = new [] { longToken };
            var expectedOutput = new[] { truncatedToken };

            using var output = new NativeList<FixedString64Bytes>(Allocator.Temp);

            Assert.DoesNotThrow(() => HierarchyItemSearchUtils.PreProcessTokens(input, output));

            Assert.That(output.ToArrayNBC(), Is.EqualTo(expectedOutput));
        }

        [Test]
        public void PreProcessTokens_DoesNotThrow_WhenTruncatingQuotedTokens()
        {
            // Quoted tokens go through a different path; we want to ensure we hit both for this case.

            var longToken = new string('s', FixedString64Bytes.utf8MaxLengthInBytes + 1);
            var truncatedToken = new string('s', FixedString64Bytes.utf8MaxLengthInBytes);

            var input = new [] { $"\"{longToken}\"" };
            var expectedOutput = new[] { $"{truncatedToken}" };

            using var output = new NativeList<FixedString64Bytes>(Allocator.Temp);

            Assert.DoesNotThrow(() => HierarchyItemSearchUtils.PreProcessTokens(input, output));

            Assert.That(output.ToArrayNBC(), Is.EqualTo(expectedOutput));
        }
    }
}
