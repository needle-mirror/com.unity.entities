# Entities Hierarchy window reference

The Entities Hierarchy window is an Editor window that you can use to see the entity hierarchy of each [world](concepts-worlds.md) in your project. This window is useful if your project has a lot of entities that you want to search for and visualize in their hierarchy. 

It contains the same operations that you can perform in the [Hierarchy window](https://docs.unity3d.com/Manual/Hierarchy.html) for GameObjects, but you can't select multiple items at once. 

To open the Entities Hierarchy window, go to **Window &gt; Entities &gt; Hierarchy**.

To select the Worlds in your project, use the drop-down in the top left of the window. You can also search for specific entities by their name, ID, or component. 

>[!TIP]
>To view more information about an entity, select it in the list. Unity then displays it in the [Inspector](editor-inspectors.md) window. 

## Data modes

To manually switch between [data modes](editor-authoring-runtime.md), select the circle in the right hand corner to switch between Authoring and Runtime data modes. Unity automatically switches between modes when you enter or exit Play mode.

To see the full entities hierarchy in both Edit mode and Play mode, enable real-time conversion of GameObject data into Entities data. To do this, open the **Preferences** window and go to  **Entities &gt; Baking &gt; Live Baking**. For more information, see [Entities Preferences reference](editor-preferences.md)

### Authoring data mode

In Authoring data mode, the Entities Hierarchy window displays a white ![Authoring data mode - a circle](images/editor-authoring-mode-dark.png) or gray ![Authoring data mode - a circle](images/editor-authoring-mode-light.png) circle in its top right corner. In this mode, the following information is available in the Entities Hierarchy window:

|**Sub Scene status**|**In Edit mode**|**In Play mode**|
|---|---|---|
|**Outside a Sub Scene**| <ul><li>GameObjects</li><li>Prefab instances</li></ul>| Nothing is displayed because the original GameObjects outside Sub Scenes no longer exist and are now in their runtime state.|
|**Inside a Sub Scene, if open**|<ul><li>GameObjects</li><li>Prefab instances</li></ul>| <ul><li>GameObjects</li><li>Prefab instances</li></ul> |
|**Inside a Sub Scene, if closed**| Read-only entities| Read-only entities|

>[!NOTE]
>Even in Play mode, Prefab instances aren't unpacked inside Sub Scenes. The Sub Scene and the GameObjects within act as if they're still in Edit mode and you can make changes to them without losing those changes when exiting Play mode.

![Entities Hierarchy window in Authoring data mode. Edit mode (Left), Play mode (Right).](images/editor-hierarchy-authoring-mode.png)<br/>_Entities Hierarchy window in Authoring data mode. Edit mode (Left), Play mode (Right)._

### Runtime data mode

In Runtime data mode, the Entities Hierarchy window displays an orange ![Runtime data mode icon - an orange circle.](images/editor-runtime-mode-dark.png) or red ![Runtime data mode icon - a red circle.](images/editor-runtime-mode-light.png) circle in its top right corner. In this mode, you can see the following in the Entities Hierarchy window:

|**Sub Scene status**|**In Edit mode and Play mode**|
|---|---|
|**Outside a Sub Scene**| <ul><li>GameObjects as their runtime state.</li><li>Entities created outside the Baking process, and outside Sub Scenes, like WorldTime.</li></ul>|
|**Inside Sub Scenes**| Only entities.|

Everything marked with an orange or red vertical bar relates to runtime data. Furthermore, a <img src="images/entity-prefab-icon.png" width="15"/> indicates that it is an entity prefab, and a ![](images/editor-entity-icon.png) with blue text, indicates that this is an instance of an entity prefab. 

![Entities Hierarchy window in Runtime data mode](images/editor-hierarchy-runtime-mode.png)<br/>_Entities Hierarchy window in Runtime data mode_


### Mixed data mode

In Mixed data mode, the Entities Hierarchy window displays a white and orange ![Mixed data mode icon - an orange circle surrounded by a white circle.](images/editor-mixed-mode-dark.png) or gray and red ![Mixed data mode icon - a red circle surrounded by a grey circle.](images/editor-mixed-mode-light.png) circle in its top right corner. This mode is available only when you enter Play mode. In this mode, the following information is available in the Entities Hierarchy window:

|**Sub Scene status**|**In Play mode**|
|---|---|
|**Outside a Sub Scene**| <ul><li>GameObjects in their runtime state.</li><li>Entities created outside the baking process, and outside Sub Scenes, like WorldTime.</li></ul>|
|**Inside Sub Scenes**| <ul><li>GameObjects in their authoring form</li><li> Prefab instances.</li><li> Entities that Unity created in the Sub Scene at runtime, and have no corresponding authoring GameObjects.</li></ul> |

Everything marked with an orange or red vertical bar relates to runtime data.

![Entities Hierarchy window in Mixed data mode](images/editor-hierarchy-mixed-mode.png)<br/>_Entities Hierarchy window in Mixed data mode_

## Additional resources

* [Entities user manual](concepts-entities.md)
* [Entity Inspector reference](editor-entity-inspector.md)
* [World user manual](concepts-worlds.md)
* [Hierarchy window](https://docs.unity3d.com/Manual/Hierarchy.html)