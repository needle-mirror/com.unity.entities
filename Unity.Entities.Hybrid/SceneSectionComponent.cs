using UnityEngine;
using UnityEngine.Serialization;
using Hash128 = Unity.Entities.Hash128;

public class SceneSectionComponent : MonoBehaviour
{
    [FormerlySerializedAs("SectionId")] 
    public int         SectionIndex;
}
