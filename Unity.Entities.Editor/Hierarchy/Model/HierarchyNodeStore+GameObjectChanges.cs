namespace Unity.Entities.Editor
{
    partial struct HierarchyNodeStore
    {
        public void IntegrateGameObjectChanges(HierarchyGameObjectChanges changes)
        {
            foreach (var scene in changes.UnloadedScenes)
                RemoveNode(HierarchyNodeHandle.FromScene(scene));

            foreach (var scene in changes.LoadedScenes)
                AddNode(HierarchyNodeHandle.FromScene(scene));
        }
    }
}