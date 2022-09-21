using System.Globalization;
using UnityEngine;

namespace Unity.Entities.TestComponents
{
    public class SetNameOnAdditionalEntityTestAuthoring : MonoBehaviour
    {
        public int number;
    }

    public class SetNameOnAdditionalEntityTestAuthoringBaker : Baker<SetNameOnAdditionalEntityTestAuthoring>
    {
        public override void Bake(SetNameOnAdditionalEntityTestAuthoring authoring)
        {
            for (int i = 0; i < authoring.number; i++)
            {
                CreateAdditionalEntity(TransformUsageFlags.Default, false, $"additionalEntity - {i}");
            }
        }
    }
}
