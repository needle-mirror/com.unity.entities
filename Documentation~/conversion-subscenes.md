# Subscenes overview

The entity component system (ECS) uses subscenes instead of scenes to manage the content of your application. This is because Unity's core [scene system](xref:CreatingScenes) is incompatible with ECS. 

You can add GameObjects and MonoBehaviour components to a subscene, and [baking](baking.md) then converts the GameObjects and MonoBehaviour components into entities and ECS components.

You can also optionally create your own bakers to attach ECS components to the converted entities. For more information, see the documentation on [Convert data with baking](baking.md).

## Create a subscene

1. Open the scene that you want to add a subscene to.
2. In the Hierarchy window, right-click and select **New Sub Scene** &gt; **Empty Scene**.

To create a subscene from existing GameObjects:

1. Open the scene that contains the GameObjects you want to create a subscene from.
2. In the Hierarchy window, select the GameObjects that you want to move to the new subscene.
3. In the same window, Right-click and select **New Sub Scene** &gt; **From Selection**.

To add an existing subscene to a scene:

1. Open the scene that you want to add a subscene to.
2. Create an empty GameObject.
3. Add the [`SubScene`](xref:Unity.Scenes.SubScene) component.
4. In the [`SubScene`](xref:Unity.Scenes.SubScene) component set the property **Scene Asset** to the scene that you want to use as subscene.

## Subscene component

The [`SubScene`](xref:Unity.Scenes.SubScene) component is a Unity component that triggers baking and streaming for the referenced scene. Unity streams in the referenced scene when the `SubScene` component is enabled, if you set the `AutoLoadScene` field to true. You can also enable the **Auto Load Scene** field in the Editor. To do this, select the subscene in the Hierarchy, and in the Inspector under the Sub Scene script, enable the **Auto Load Scene** checkbox.

The [`SubScene`](xref:Unity.Scenes.SubScene) component has two modes, which depend on whether the subscene is opened or closed. To open or close a subscene you can do one of the following: 

* In the Hierarchy window, enable or disable the checkbox next to the subscene's name. 
* Select a subscene, and the Inspector, under **Open SubScenes** select **Open/Close**.

![](images/SubsceneCheckBox.png)<br/>_Hierarchy window with checkbox next to the subscene's name highlighted_

When a subscene is open, the following happens:

* In the Hierarchy window, Unity displays all the authoring GameObjects from the subscene under the GameObject that has the [`SubScene`](xref:Unity.Scenes.SubScene) component.
* The Scene View will display the runtime data (Entities) or the authoring data (GameObjects) based on the setting _Scene View Mode_ in the Entities section of the [Preferences window](https://docs.unity3d.com/Manual/Preferences.html). 
* An initial baking pass runs on all the authoring components in the subscene.
* Any changes made to the authoring components triggers an incremental baking pass.

When a subscene is closed, Unity streams in the content of the baked scene.

## Additional resources

* [Baking overview](baking.md)
* [Scene streaming](streaming-scenes.md)
