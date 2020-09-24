using Unity.Entities;
using UnityEngine;
using UnityEngine.Serialization;

public class SceneSectionComponent : MonoBehaviour
{
    [FormerlySerializedAs("SectionId")]
    public int         SectionIndex;
}
