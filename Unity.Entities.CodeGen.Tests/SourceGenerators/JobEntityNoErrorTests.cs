using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities.CodeGen.Tests;

namespace Unity.Entities.CodeGen.SourceGenerators.Tests
{
    [TestFixture]
    public class JobEntityNoErrorTests : SourceGenTests
    {
        protected override Type[] DefaultCompilationReferenceTypes { get; } =
        {
            typeof(Entity),
            typeof(Translation),
            typeof(NativeArray<>)
        };

        protected override string[] DefaultUsings { get; } =
        {
            "System", "Unity.Entities", "Unity.Collections", "Unity.Entities.CodeGen.Tests"
        };

        [Test]
        public void NoError_WithIJobEntityExtensions()
        {
            const string source =
                @"public partial struct WithIJobEntityExtensions : IJobEntity
                {
                    void Execute(ref Translation translation)
                    {
                        translation.Value /= 2f;
                    }
                }
                public partial class TestSystem : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        var job = new WithIJobEntityExtensions();
                        Dependency = IJobEntityExtensions.Schedule(job, Dependency);
                    }
                }";

            AssertProducesNoError(source);
        }

        [Test]
        public void NoError_WithIJobEntityWithIndexExtensions()
        {
            const string source =
                @"public partial struct WithIJobEntityWithIndexExtensions : IJobEntity
                {
                    void Execute([Unity.Entities.EntityInQueryIndex] int index, ref Translation translation)
                    {
                    }
                }
                public partial class TestSystem : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        var job = new WithIJobEntityWithIndexExtensions();
                        Dependency = IJobEntityExtensions.Schedule(job, Dependency);
                    }
                }";

            AssertProducesNoError(source);
        }


        [Test]
        public void NoError_ArgumentsOutOfOrder_WithIJobEntityExtensions()
        {
            const string source =
                @"public partial struct WithIJobEntityExtensions : IJobEntity
                {
                    void Execute(ref Translation translation)
                    {
                        translation.Value /= 2f;
                    }
                }
                public partial class TestSystem : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        var job = new WithIJobEntityExtensions();
                        Dependency = IJobEntityExtensions.Schedule(dependsOn: Dependency, jobData: job);
                    }
                }";

            AssertProducesNoError(source);
        }

        [Test]
        public void NoError_ClassNestedStruct()
        {
            const string source =
                @"public partial class NoError_ClassNestedStruct : SystemBase
                {
                    partial class NestedClass
                    {
                        public partial struct ThrustJob : IJobEntity
                        {
                            public float DeltaTime;

                            public void Execute(ref Translation translation)
                            {
                                translation.Value *= 2f;
                            }
                        }
                    }

                    protected override void OnUpdate()
                    {
                        var job = new NestedClass.ThrustJob{DeltaTime = Time.DeltaTime};
                        var query = EntityManager.UniversalQuery;
                        job.Schedule(query, Dependency);
                    }
                }";

            AssertProducesNoError(source);
        }

        [Test]
        public void NoError_AllScheduleAndRunInvocations()
        {
            const string source =
                @"public partial class NoError_AllScheduleAndRunInvocations : SystemBase
                {

                    protected override void OnUpdate()
                    {
                        var job = new ThrustJob{DeltaTime = Time.DeltaTime};
                        var query = EntityManager.UniversalQuery;

                        job.Schedule();
                        job.Schedule(Dependency);
                        job.Schedule(query, Dependency);

                        job.ScheduleByRef();
                        job.ScheduleByRef(Dependency);
                        job.ScheduleByRef(query, Dependency);

                        job.ScheduleParallel();
                        job.ScheduleParallel(Dependency);
                        job.ScheduleParallel(query, Dependency);

                        job.ScheduleParallelByRef();
                        job.ScheduleParallelByRef(Dependency);
                        job.ScheduleParallelByRef(query, Dependency);

                        job.Run();
                        job.Run(query);

                        job.RunByRef();
                        job.RunByRef(query);
                    }

                    partial struct ThrustJob : IJobEntity
                    {
                        public float DeltaTime;

                        public void Execute(ref Translation translation)
                        {
                            translation.Value *= 2f;
                        }
                    }
                }";

            AssertProducesNoError(source);
        }

        [Test]
        public void NoError_WithNestedPrivateJob()
        {
            const string source = @"
                public partial class PlayerVehicleControlSystem : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        var job = new ThrustJob { DeltaTime = Time.DeltaTime };
                        job.ScheduleParallel(Dependency);
                    }

                    partial struct ThrustJob : IJobEntity
                    {
                        public float DeltaTime;

                        public void Execute(ref Translation translation)
                        {
                            translation.Value *= 2f;
                        }
                    }
                }";

            AssertProducesNoError(source, DefaultUsings, true);
        }

        [Test]
        public void NoError_LotsOfInterfaces()
        {
            const string source = @"
                public partial class NoError_LotsOfInterfaces : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        var job = new ThrustJob();
                        job.ScheduleParallel(Dependency);
                    }

                    interface IInterfaceA {}
                    interface IInterfaceB {}
                    partial struct ThrustJob : IInterfaceA, IJobEntity, IInterfaceB
                    {
                        public void Execute(ref Translation translation) {}
                    }
                }";

            AssertProducesNoError(source, DefaultUsings, true);
        }

        [Test]
        public void NoError_AllScheduleVariations()
        {
            const string source = @"
                public partial class NoError_AllScheduleVariations : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        var job = new ThrustJob{DeltaTime = Time.DeltaTime};
                        var query = EntityManager.UniversalQuery;
                        Dependency = job.ScheduleParallel(query, Dependency);

                        job.Schedule();
                        job.Schedule(Dependency);
                        job.Schedule(query, Dependency);

                        job.ScheduleByRef();
                        job.ScheduleByRef(Dependency);
                        job.ScheduleByRef(query, Dependency);

                        job.ScheduleParallel();
                        job.ScheduleParallel(Dependency);
                        job.ScheduleParallel(query, Dependency);

                        job.ScheduleParallelByRef();
                        job.ScheduleParallelByRef(Dependency);
                        job.ScheduleParallelByRef(query, Dependency);

                        job.Run();
                        job.Run(query);

                        job.RunByRef();
                        job.RunByRef(query);
                    }

                    partial struct ThrustJob : IJobEntity
                    {
                        public float DeltaTime;

                        public void Execute(ref Translation translation)
                        {
                            translation.Value *= 2f;
                        }
                    }
                }
            ";

            AssertProducesNoError(source, DefaultUsings, true);
        }

        [Test]
        public void InnerNamespaceUsing()
        {
            var source = @"
            namespace SomeNameSpace {
                using Unity.Entities;
                public partial struct SomeJob : IJobEntity {
                    public void Execute() {}
                }
            }";
            AssertProducesNoError(source, new string[]{}, true);
        }

        [Test]
        public void JobInStruct()
        {
            var source = @"
            using Unity.Entities;
            public partial struct SomeOuter {
                public partial struct SomeJob : IJobEntity {
                    public void Execute() {}
                }
            }";
            AssertProducesNoError(source, new string[]{}, true);

        }

        [Test]
        public void JobInClass()
        {
            var source = @"
            using Unity.Entities;
            public partial class SomeOuter {
                public partial struct SomeJob : IJobEntity {
                    public void Execute() {}
                }
            }";
            AssertProducesNoError(source, new string[]{}, true);
        }

        [Test]
        public void TwoJobs()
        {
            var source = @"
            using Unity.Entities;
            public partial struct SomeOuter {
                public partial struct SomeJobA : IJobEntity {
                    public void Execute() {}
                }
                public partial struct SomeJobB : IJobEntity {
                    public void Execute() {}
                }
            }";
            AssertProducesNoError(source,new string[]{}, true);

        }
    }
}
