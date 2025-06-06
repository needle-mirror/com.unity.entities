# Entities Profiler modules 

You can use the [Unity Profiler](xref:um-profiler) to profile your application and understand more about the performance of your application. There are two Entities Profiler modules that you can use to track and collect data on the performance of your Entities code:

* [**Entities Structural Changes**](profiler-module-structural-changes.md): Displays information about when the ECS framework created and destroyed Entities and Components.  
* [**Entities Memory**](profiler-module-memory.md): Displays how much memory the [Archetypes](concepts-archetypes.md) in your project used on a per-frame basis. 

For information on how to use the Profiler, and its other modules, refer to the Unity User Manual documentation on the [Profiler window](xref:um-profiler). 

To open the Profiler window, go to **Window &gt; Analysis &gt; Profiler**. By default, the Entities Profiler modules appear in the Profiler window. To enable or disable them, use the **Profiler Modules** dropdown. 

>[!IMPORTANT]
>The Profiler doesn’t collect any data for modules that aren’t enabled. If you enable a module after profiling your application, the newly enabled Profiler modules won’t display any data.

You can [profile your application](xref:um-profiler-profiling-applications) in Play mode, or a development build of your application on a target device. You can also profile the Unity Editor itself, to discover how much overhead it has, and to profile Conversion code, which runs in Edit mode. 

![Profiler window with the Entities Profiler modules displayed](images/profiler-entities-structural.png)<br/>_Profiler window with the Entities Profiler modules displayed_

## Additional resources

* [Profiler window](xref:um-profiler)
* [Profiling your application](xref:um-profiler-profiling-applications)