using System;
using System.Linq;
using Mono.Cecil;
using NUnit.Framework;
using Unity.Entities.CodeGen.Tests.LambdaJobs.Infrastructure;
using Unity.Collections;
using Unity.Entities.CodeGeneratedJobForEach;
using Unity.Jobs;

namespace Unity.Entities.CodeGen.Tests
{
    [TestFixture]
    public class LambdaJobDescriptionConstructionTests : LambdaJobsPostProcessorTestBase
    {
        [Test]
        public void EntitiesForEachTest()
        {
            var methodToAnalyze = MethodDefinitionForOnlyMethodOf(typeof(WithForEachSystem));

            var forEachDescriptionConstruction = LambdaJobDescriptionConstruction.FindIn(methodToAnalyze).Single();

            var icm = forEachDescriptionConstruction.InvokedConstructionMethods;

            CollectionAssert.AreEqual(new[]
            {
                nameof(LambdaJobQueryConstructionMethods.WithEntityQueryOptions),
                nameof(LambdaJobDescriptionConstructionMethods.WithBurst),
                nameof(LambdaJobQueryConstructionMethods.WithNone),
                nameof(LambdaJobQueryConstructionMethods.WithChangeFilter),
                nameof(LambdaJobDescriptionConstructionMethods.WithName),
                nameof(LambdaForEachDescriptionConstructionMethods.ForEach),
            }, icm.Select(i => i.MethodName));


            CollectionAssert.AreEqual(new[]
            {
                1,
                3,
                0,
                0,
                1,
                1,
            }, icm.Select(i => i.Arguments.Length));

            Assert.AreEqual("MyJobName", icm[4].Arguments[0]);
            CollectionAssert.AreEqual(new[] {nameof(Translation), nameof(Velocity)}, icm[3].TypeArguments.Select(t => t.Name));

            Assert.AreEqual(EntityQueryOptions.IncludePrefab, (EntityQueryOptions)icm[0].Arguments.Single());
        }

        class WithForEachSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                float dt = 2.34f;

                Entities
                    .WithEntityQueryOptions(EntityQueryOptions.IncludePrefab)
                    .WithBurst(synchronousCompilation: true)
                    .WithNone<Boid>()
                    .WithChangeFilter<Translation, Velocity>()
                    .WithName("MyJobName")
                    .ForEach(
                        (ref Translation translation, ref Boid boid, in Velocity velocity) =>
                        {
                            translation.Value += velocity.Value * dt;
                        })
                    .Schedule();
            }
        }

        [Test]
        public void SingleJobTest()
        {
            var methodToAnalyze = MethodDefinitionForOnlyMethodOf(typeof(SingleJobTestSystem));

            var forEachDescriptionConstruction = LambdaJobDescriptionConstruction.FindIn(methodToAnalyze).Single();

            var icm = forEachDescriptionConstruction.InvokedConstructionMethods;

            Assert.AreEqual(LambdaJobDescriptionKind.Job, forEachDescriptionConstruction.Kind);

            CollectionAssert.AreEqual(new[]
            {
                nameof(LambdaJobDescriptionConstructionMethods.WithBurst),
                nameof(LambdaJobDescriptionConstructionMethods.WithName),
                nameof(LambdaSingleJobDescriptionConstructionMethods.WithCode),
            }, icm.Select(i => i.MethodName));

            CollectionAssert.AreEqual(new[]
            {
                3,
                1,
                1,
            }, icm.Select(i => i.Arguments.Length));

            Assert.AreEqual("MyJobName", icm[1].Arguments[0]);
        }

        class SingleJobTestSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                Job
                    .WithBurst(synchronousCompilation: true)
                    .WithName("MyJobName")
                    .WithCode(()
                        =>
                        {
                        })
                    .Schedule();
            }
        }

#if ENABLE_DOTS_COMPILER_CHUNKS
        [Test]
        public void JobChunkTest()
        {
            var methodToAnalyze = MethodDefinitionForOnlyMethodOf(typeof(JobChunkTestSystem));

            var forEachDescriptionConstruction = LambdaJobDescriptionConstruction.FindIn(methodToAnalyze).Single();

            var icm = forEachDescriptionConstruction.InvokedConstructionMethods;
            Assert.AreEqual(LambdaJobDescriptionKind.Chunk, forEachDescriptionConstruction.Kind);
            CollectionAssert.AreEqual(new[]
            {
                nameof(LambdaJobDescriptionConstructionMethods.WithBurst),
                nameof(LambdaJobDescriptionConstructionMethods.WithName),
                nameof(LambdaJobChunkDescriptionConstructionMethods.ForEach),
            }, icm.Select(i => i.MethodName));

            CollectionAssert.AreEqual(new[]
            {
                4,
                1,
                1,
            }, icm.Select(i => i.Arguments.Length));

            Assert.AreEqual("MyJobName", icm[1].Arguments[0]);
        }

        class JobChunkTestSystem : SystemBase
        {
            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                return Chunks
                    .WithName("MyJobName")
                    .ForEach(delegate(ArchetypeChunk chunk, int index, int query) {})
                    .Schedule(inputDeps);
            }
        }
#endif

        [Test]
        public void DoesNotCaptureTest()
        {
            var methodToAnalyze = MethodDefinitionForOnlyMethodOf(typeof(WithCodeThatDoesNotCaptureSystem));

            var icm = LambdaJobDescriptionConstruction.FindIn(methodToAnalyze).Single().InvokedConstructionMethods;

            CollectionAssert.AreEqual(new[]
            {
                nameof(LambdaForEachDescriptionConstructionMethods.ForEach),
            }, icm.Select(i => i.MethodName));
        }

        class WithCodeThatDoesNotCaptureSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities
                    .ForEach((ref Velocity e1) => { e1.Value += 1f;})
                    .Schedule();
            }
        }

        [Test]
        public void WithReadOnlyCapturedVariableTest()
        {
            var methodToAnalyze = MethodDefinitionForOnlyMethodOf(typeof(WithReadOnlyCapturedVariable));
            var description = LambdaJobDescriptionConstruction.FindIn(methodToAnalyze).Single();
            var withReadOnly = description.InvokedConstructionMethods.Single(m => m.MethodName == nameof(LambdaJobDescriptionConstructionMethods.WithReadOnly));
            Assert.IsInstanceOf<FieldDefinition>(withReadOnly.Arguments.Single());
        }

        class WithReadOnlyCapturedVariable : TestSystemBase
        {
            void Test()
            {
                NativeArray<int> myarray = new NativeArray<int>();

                Entities
                    .WithReadOnly(myarray)
                    .ForEach((ref Translation translation) => translation.Value += myarray[0])
                    .Schedule();
            }
        }

        [Test]
        public void WithReadOnlyCapturedVariableFromTwoScopesTest()
        {
            var methodToAnalyze = MethodDefinitionForOnlyMethodOf(typeof(WithReadOnlyCapturedVariableFromTwoScopes));
            var description = LambdaJobDescriptionConstruction.FindIn(methodToAnalyze).Single();

            var withReadOnlyMethods = description.InvokedConstructionMethods.Where(m => m.MethodName == nameof(LambdaJobDescriptionConstructionMethods.WithReadOnly));
            Assert.AreEqual(2, withReadOnlyMethods.Count());
            foreach (var withReadOnly in withReadOnlyMethods)
                Assert.IsInstanceOf<FieldDefinition>(withReadOnly.Arguments.Single());
        }

        class WithReadOnlyCapturedVariableFromTwoScopes : TestSystemBase
        {
            void Test()
            {
                NativeArray<int> outerScopeArray = new NativeArray<int>();
                {
                    NativeArray<int> innerScopeArray = new NativeArray<int>();
                    Entities
                        .WithReadOnly(outerScopeArray)
                        .WithReadOnly(innerScopeArray)
                        .ForEach((ref Translation translation) => translation.Value += outerScopeArray[0] + innerScopeArray[0])
                        .Schedule();
                }
            }
        }

        [Test]
        public void RunInsideLoopCapturingLoopConditionTest()
        {
            var methodToAnalyze = MethodDefinitionForOnlyMethodOf(typeof(RunInsideLoopCapturingLoopCondition));

            LambdaJobDescriptionConstruction.FindIn(methodToAnalyze).Single();
        }

        public class RunInsideLoopCapturingLoopCondition : SystemBase
        {
            protected override void OnUpdate()
            {
                int variable = 10;
                for (int i = 0; i != variable; i++)
                {
                    Entities
                        .ForEach((ref Translation e1) => { e1.Value += variable; })
                        .Run();
                }
            }
        }
    }
}
