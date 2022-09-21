#if !UNITY_DISABLE_MANAGED_COMPONENTS
using UnityEngine;

namespace Unity.Entities.Tests
{
    [AddComponentMenu("")]
    public class CompanionComponentTestAuthoring : MonoBehaviour
    {
        public int Value;
    }

    class CompanionComponentTestBaker : Baker<CompanionComponentTestAuthoring>
    {
        public override void Bake(CompanionComponentTestAuthoring authoring)
        {
            AddComponentObject(authoring);
        }
    }
}
#endif
