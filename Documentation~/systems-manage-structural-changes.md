# Manage structural changes

Managing structural changes is important to make sure that creating, destroying, and changing entities doesn't affect the performance of your application. You can defer [structural changes](concepts-structural-changes.md), or play back a set of changes multiple times with an [entity command buffer](systems-entity-command-buffers.md).

|**Topic**|**Description**|
|---|---|
|[Manage structural changes](systems-manage-structural-changes-intro.md)|Compare the different ways that you manage structural changes.|
|[Defer data changes](systems-deferring-data.md)|Defer data changes with entity command buffers.|
|[Manage structural changes with EntityManager](systems-entitymanager.md)| Understand how to use `EntityManager` to schedule data changes on the main thread.|

## Additional resources

* [Access data in systems](systems-access-data.md)
* [Query data](systems-entityquery.md)