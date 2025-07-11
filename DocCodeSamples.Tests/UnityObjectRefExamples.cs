using Unity.Entities;
using UnityEngine;

namespace DocCodeSamples.Tests
{
#region unityobjectref-example
    public class AnimatorAuthoring : MonoBehaviour
    {

        public GameObject AnimatorPrefab;

        public class AnimatorBaker : Baker<AnimatorAuthoring>
        {
            public override void Bake(AnimatorAuthoring authoring)
            {
                var e = GetEntity(TransformUsageFlags.Renderable);
                AddComponent(e, new AnimatorRefComponent
                {
                    AnimatorAsGO =  authoring.AnimatorPrefab
                });
            }
        }
    }

    public struct AnimatorRefComponent : IComponentData
    {
        public UnityObjectRef<GameObject> AnimatorAsGO;
    }

#endregion

#region unityobjectref-spawn-system-example
    public partial struct SpawnAnimatedCubeSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            var entities = SystemAPI.QueryBuilder().WithAll<AnimatorRefComponent>().WithNone<Animator>().Build().ToEntityArray(state.WorldUpdateAllocator);

            foreach (var entity in entities)
            {
                var animRef = SystemAPI.GetComponent<AnimatorRefComponent>(entity);

                var rotatingCube = (GameObject)Object.Instantiate(animRef.AnimatorAsGO);

                state.EntityManager.AddComponentObject(entity, rotatingCube.GetComponent<Animator>());
            }
        }
    }

#endregion

#if !UNITY_DISABLE_MANAGED_COMPONENTS

#region unityobjectref-anim-system-example
    public partial struct ChangeRotationAnimationSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            //Query and modify the speed of the Animator
            foreach (var anim in SystemAPI.Query<SystemAPI.ManagedAPI.UnityEngineComponent<Animator>>())
            {
                var sineSpeed = 1f + Mathf.Sin(Time.time);
                anim.Value.speed = sineSpeed;
            }
        }
    }
#endregion

#endif
}
