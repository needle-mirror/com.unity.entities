using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.PerformanceTesting;

namespace Unity.Entities.Editor.PerformanceTests
{
	[TestFixture]
	[Category(Categories.Performance)]
    class HierarchyPerformanceTests
    {
	    [Test, Performance]
	    public void ScheduleEntityChanges_WithInitialChanges(
		    [Values(ScenarioId.ScenarioA, ScenarioId.ScenarioB, ScenarioId.ScenarioC, ScenarioId.ScenarioD)]
		    ScenarioId scenarioId)
	    {
		    var scenario = EntityHierarchyScenario.GetScenario(scenarioId, ItemsVisibility.AllExpanded);
		    var generator = new DefaultStrategyWorldGenerator(scenario);
		    var world = generator.Get();
		    
		    var tracker = new HierarchyEntityChangeTracker(world, Allocator.Persistent);
		    var changes = tracker.GetChanges(Allocator.TempJob);

		    var hierarchy = new HierarchyNodeStore(Allocator.Persistent);

		    Measure.Method(() =>
		    {
			    hierarchy.IntegrateEntityChanges(world, changes);
		    }).WarmupCount(1)
			    .SetUp(() =>
			    {
				    hierarchy.Clear();
			    })
			    .IterationsPerMeasurement(10)
			  .Run();

		    changes.Dispose();
		    hierarchy.Dispose();
		    tracker.Dispose();
		    generator.Dispose();
	    }
	    
	    [Test, Performance]
	    public void SchedulePacking_WithInitialChanges(
		    [Values(ScenarioId.ScenarioA, ScenarioId.ScenarioB, ScenarioId.ScenarioC, ScenarioId.ScenarioD)]
		    ScenarioId scenarioId)
	    {
		    var scenario = EntityHierarchyScenario.GetScenario(scenarioId, ItemsVisibility.AllExpanded);
		    var generator = new DefaultStrategyWorldGenerator(scenario);
		    var world = generator.Get();

		    var hierarchy = new HierarchyNodeStore(Allocator.Persistent);
		    var immutable = new HierarchyNodeStore.Immutable(Allocator.Persistent);
		    
		    using (var tracker = new HierarchyEntityChangeTracker(world, Allocator.TempJob))
		    using (var changes = tracker.GetChanges(Allocator.TempJob))
		    {
			    // Integrate the initial change set.
			    hierarchy.IntegrateEntityChanges(world, changes);
		    }
			    
		    Measure.Method(() =>
			    {
				    hierarchy.ExportImmutable(world, immutable);
			    }).WarmupCount(1)
			    .IterationsPerMeasurement(10)
			    .Run();

		    hierarchy.Dispose();
		    immutable.Dispose();
	    }
	    
	    /// <summary>
	    /// This method tests the performance of re-using unchanged branches from previous packs by performing a copy.
	    /// </summary>
	    [Test, Performance]
	    public void SchedulePacking_WithNoChanges_FirstLevelCaching(
		    [Values(ScenarioId.ScenarioA, ScenarioId.ScenarioB, ScenarioId.ScenarioC, ScenarioId.ScenarioD)]
		    ScenarioId scenarioId)
	    {
		    var scenario = EntityHierarchyScenario.GetScenario(scenarioId, ItemsVisibility.AllExpanded);
		    var generator = new DefaultStrategyWorldGenerator(scenario);
		    var world = generator.Get();

		    var hierarchy = new HierarchyNodeStore(Allocator.Persistent);
		    var immutable0 = new HierarchyNodeStore.Immutable(Allocator.Persistent);
		    var immutable1 = new HierarchyNodeStore.Immutable(Allocator.Persistent);
		    
		    using (var tracker = new HierarchyEntityChangeTracker(world, Allocator.TempJob))
		    using (var changes = tracker.GetChanges(Allocator.TempJob))
		    {
			    hierarchy.IntegrateEntityChanges(world, changes);
			    hierarchy.ExportImmutable(world, immutable0);
		    }
			    
		    Measure.Method(() =>
			    {
				    hierarchy.ExportImmutable(world, immutable1, immutable0);
			    }).WarmupCount(1)
			    .IterationsPerMeasurement(10)
			    .Run();

		    hierarchy.Dispose();
		    immutable0.Dispose();
		    immutable1.Dispose();
		    generator.Dispose();
	    }

	    /// <summary>
	    /// This method tests the performance of re-using unchanged memory blocks which already exist in the write buffer from a previous pack.
	    /// </summary>
	    [Test, Performance]
	    public void ScheduleHierarchyItemCollectionRefresh_WithNoItemsExpanded(
		    [Values(ScenarioId.ScenarioA, ScenarioId.ScenarioB, ScenarioId.ScenarioC, ScenarioId.ScenarioD)]
		    ScenarioId scenarioId)
	    {
		    var scenario = EntityHierarchyScenario.GetScenario(scenarioId, ItemsVisibility.AllExpanded);
		    var generator = new DefaultStrategyWorldGenerator(scenario);
		    var world = generator.Get();

		    var hierarchy = new HierarchyNodeStore(Allocator.Persistent);
		    var immutable = new HierarchyNodeStore.Immutable(Allocator.TempJob);
		    
		    using (var tracker = new HierarchyEntityChangeTracker(world, Allocator.TempJob))
		    using (var changes = tracker.GetChanges(Allocator.TempJob))
		    {
			    hierarchy.IntegrateEntityChanges(world, changes);
			    hierarchy.ExportImmutable(world, immutable);
		    }
		    
		    var items = new HierarchyNodes(Allocator.Persistent);

		    Measure.Method(() =>
			    {
				    items.Refresh(immutable);
			    }).WarmupCount(1)
			    .IterationsPerMeasurement(10)
			    .Run();

		    hierarchy.Dispose();
		    immutable.Dispose();
		    items.Dispose();
		    generator.Dispose();
	    }
    }
}