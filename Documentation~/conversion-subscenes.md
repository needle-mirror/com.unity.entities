# Subscenes

The entity component system uses subscenes to manage the content of your application. You can add GameObjects and MonoBehaviour components to a subscene, and [baking](baking.md) then converts the GameObjects and MonoBehaviour components into entities and ECS components.

You can also optionally create your own bakers to attach ECS components to the converted entities. For more information, see the documentation on [Convert data with baking](baking.md).

To create a subscene:

1. In the Editor, open a [scene](xref:CreatingScenes) that has been saved as a file.
2. In the Inspector, right-click and select **New Sub Scene** &gt; **Empty Scene**.

You can then add GameObjects to the subscene and attach components to them. At runtime, Unity bakes the subscene and converts the GameObjects to entities.