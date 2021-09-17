---
uid: ecs-ijobentity
---
# Using IJobEntity jobs

IJobEntity, is a way to iterate across ComponentData, similar to [Entities.ForEach].
Use this, when you have a data transformation that you want in multiple systems, with different invocations.
It creates the [IJobEntityBatch] for you, so all you have to think about is what data you need to transform.

You write a struct using the `IJobEntity` interface, and implement your own custom `Execute` function. 
Remember the `partial` keyword, as source generation will create a struct implementing [IJobEntityBatch]
in a separate file found inside `project/Temp/GeneratedCode/....`.
Here's a simple example, that adds one to every translation component every frame.
[!code-cs[SimpleSample](../DocCodeSamples.Tests/JobEntityExamples.cs#SimpleSample)]

## Specifying a Query
There are two ways to specify a query for `IJobEntity`:
1. Doing it manually, to specify different invocation requirements.
2. Having the implemented IJobEntity do it for you, based on its given `Execute` parameters.

An example of both can be seen here:
[!code-cs[Query](../DocCodeSamples.Tests/JobEntityExamples.cs#Query)]

## Attributes
Since this resembles a job, all attributes that work on a job also work:
* `Unity.Burst.BurstCompile`
* `Unity.Collections.DeallocateOnJobCompletion`
* `Unity.Collections.NativeDisableParallelForRestriction`
* `Unity.Burst.BurstDiscard`
* `Unity.Collections.LowLevel.Unsafe.NativeSetThreadIndex` 
* `Unity.Collections.NativeDisableParallelForRestriction`
* `Unity.Burst.NoAlias`

However, `IJobEntity` also has additional attributes:
* `Unity.Entities.EntityInQueryIndex` Set on `int` parameter in `Execute` to get the current index in query, for the current entity iteration. It is the equivalent to the `entityInQueryIndex` found in [Entities.ForEach].

A sample of `EntityInQueryIndex` can be read as follows:
[!code-cs[EntityInQueryIndex](../DocCodeSamples.Tests/JobEntityExamples.cs#EntityInQueryIndex)]

## IJobEntity vs Entities.ForEach

The core advantage of `IJobEntity` over [Entities.ForEach] is that it enables you to write code once 
which can be used throughout many systems, instead of only once.

Here's an example taken from boids. This is [Entities.ForEach]:
[!code-cs[BoidsForEach](../DocCodeSamples.Tests/JobEntityExamples.cs#BoidsForEach)]

It can be rewritten as (remark, `CopyPositionsJob` can be found above):
[!code-cs[Boids](../DocCodeSamples.Tests/JobEntityExamples.cs#Boids)]

[Entities.ForEach]: xref:Unity.Entities.SystemBase.Entities
[Upgrade Guide]: entities_upgrade_guide.md
[IJobEntityBatch]: ecs_ijobentitybatch.md
