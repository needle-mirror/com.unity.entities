using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;
#if HDRP_7_0_0_OR_NEWER
using UnityEngine.Rendering.HighDefinition;
#endif
#if URP_7_0_0_OR_NEWER
using UnityEngine.Rendering.Universal;
#endif

using Unity.Entities;

[assembly: RegisterUnityEngineComponentType(typeof(Light))]
[assembly: RegisterUnityEngineComponentType(typeof(LightProbeProxyVolume))]
[assembly: RegisterUnityEngineComponentType(typeof(ReflectionProbe))]
[assembly: RegisterUnityEngineComponentType(typeof(TextMesh))]
[assembly: RegisterUnityEngineComponentType(typeof(MeshRenderer))]
[assembly: RegisterUnityEngineComponentType(typeof(SpriteRenderer))]
[assembly: RegisterUnityEngineComponentType(typeof(VisualEffect))]
[assembly: RegisterUnityEngineComponentType(typeof(AudioSource))]
[assembly: RegisterUnityEngineComponentType(typeof(LODGroup))]
[assembly: RegisterUnityEngineComponentType(typeof(Rigidbody))]
[assembly: RegisterUnityEngineComponentType(typeof(Collider))]
[assembly: RegisterUnityEngineComponentType(typeof(GameObject))]
[assembly: RegisterUnityEngineComponentType(typeof(Transform))]
[assembly: RegisterUnityEngineComponentType(typeof(SphereCollider))]
[assembly: RegisterUnityEngineComponentType(typeof(BoxCollider))]
[assembly: RegisterUnityEngineComponentType(typeof(CapsuleCollider))]
[assembly: RegisterUnityEngineComponentType(typeof(MeshCollider))]
#if PARTICLE_SYSTEM_MODULE
[assembly: RegisterUnityEngineComponentType(typeof(ParticleSystem))]
[assembly: RegisterUnityEngineComponentType(typeof(ParticleSystemRenderer))]
#endif
#if SRP_7_0_0_OR_NEWER
[assembly: RegisterUnityEngineComponentType(typeof(Volume))]
#endif
#if URP_7_0_0_OR_NEWER
[assembly: RegisterUnityEngineComponentType(typeof(UnityEngine.Rendering.Universal.DecalProjector))]
#endif
#if HDRP_7_0_0_OR_NEWER
[assembly: RegisterUnityEngineComponentType(typeof(UnityEngine.Rendering.HighDefinition.DecalProjector))]
[assembly: RegisterUnityEngineComponentType(typeof(HDAdditionalLightData))]
[assembly: RegisterUnityEngineComponentType(typeof(HDAdditionalReflectionData))]
[assembly: RegisterUnityEngineComponentType(typeof(PlanarReflectionProbe))]
[assembly: RegisterUnityEngineComponentType(typeof(LocalVolumetricFog))]
#endif
#if URP_7_0_0_OR_NEWER
[assembly: RegisterUnityEngineComponentType(typeof(UniversalAdditionalLightData))]
#endif
#if HYBRID_ENTITIES_CAMERA_CONVERSION
[assembly: RegisterUnityEngineComponentType(typeof(Camera))]
#if HDRP_7_0_0_OR_NEWER
[assembly: RegisterUnityEngineComponentType(typeof(HDAdditionalCameraData))]
#endif
#if URP_7_0_0_OR_NEWER
[assembly: RegisterUnityEngineComponentType(typeof(UniversalAdditionalCameraData))]
#endif
#endif
#if SRP_17_0_0_OR_NEWER
[assembly: RegisterUnityEngineComponentType(typeof(ProbeVolume))]
[assembly: RegisterUnityEngineComponentType(typeof(ProbeVolumePerSceneData))]
#endif

[assembly: InternalsVisibleTo("Unity.Entities.Hybrid")]
namespace Unity.Entities.Conversion
{
    internal class CompanionComponentSupportedTypes
    {
        public static ComponentType[] Types =
        {
            typeof(Light),
            typeof(LightProbeProxyVolume),
            typeof(ReflectionProbe),
            typeof(TextMesh),
            typeof(MeshRenderer),
            typeof(SpriteRenderer),
            typeof(VisualEffect),
            typeof(AudioSource),
            typeof(SphereCollider),
            typeof(BoxCollider),
            typeof(CapsuleCollider),
            typeof(MeshCollider),
#if PARTICLE_SYSTEM_MODULE
            typeof(ParticleSystem),
            typeof(ParticleSystemRenderer),
#endif
#if SRP_7_0_0_OR_NEWER
            typeof(Volume),

#endif
#if URP_7_0_0_OR_NEWER
            typeof(UnityEngine.Rendering.Universal.DecalProjector),
#endif
#if HDRP_7_0_0_OR_NEWER
            typeof(UnityEngine.Rendering.HighDefinition.DecalProjector),
            typeof(HDAdditionalLightData),
            typeof(HDAdditionalReflectionData),
            typeof(PlanarReflectionProbe),
            typeof(LocalVolumetricFog),
#endif
#if URP_7_0_0_OR_NEWER
            typeof(UniversalAdditionalLightData),
#endif
#if HYBRID_ENTITIES_CAMERA_CONVERSION
            typeof(Camera),
#if HDRP_7_0_0_OR_NEWER
            typeof(HDAdditionalCameraData),
#endif
#if URP_7_0_0_OR_NEWER
            typeof(UniversalAdditionalCameraData),
#endif
#endif
#if SRP_17_0_0_OR_NEWER
            typeof(ProbeVolume),
            typeof(ProbeVolumePerSceneData),
#endif
        };
    }
}
