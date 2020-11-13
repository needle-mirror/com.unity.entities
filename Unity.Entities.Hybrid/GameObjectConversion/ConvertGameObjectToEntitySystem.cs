using System;
using System.Collections.Generic;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace Unity.Entities
{
    public interface IConvertGameObjectToEntity
    {
        void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem);
    }

    public interface IDeclareReferencedPrefabs
    {
        void DeclareReferencedPrefabs(List<GameObject> referencedPrefabs);
    }

    [AttributeUsage(AttributeTargets.Class)]
    [Obsolete("Not used anymore. You can safely remove this. (RemovedAfter 2020-11-22)")]
    public class RequiresEntityConversionAttribute : Attribute {}
}

namespace Unity.Entities.Conversion
{
    class ConvertGameObjectToEntitySystem : GameObjectConversionSystem
    {
        void Convert(Transform transform, List<IConvertGameObjectToEntity> convertibles)
        {
            try
            {
                transform.GetComponents(convertibles);

                foreach (var c in convertibles)
                {
                    var behaviour = c as Behaviour;
                    if (behaviour != null && !behaviour.enabled) continue;

#if UNITY_EDITOR
                    if (!ShouldRunConversionSystem(c.GetType()))
                        continue;
#endif

                    var entity = GetPrimaryEntity((Component)c);
                    c.Convert(entity, DstEntityManager, this);
                }
            }
            catch (Exception x)
            {
                Debug.LogException(x, transform);
            }
        }

        protected override void OnUpdate()
        {
            var convertibles = new List<IConvertGameObjectToEntity>();

            Entities.ForEach((Transform transform) => Convert(transform, convertibles));
            convertibles.Clear();

            //@TODO: Remove this again once we add support for inheritance in queries
            Entities.ForEach((RectTransform transform) => Convert(transform, convertibles));
        }
    }
}
