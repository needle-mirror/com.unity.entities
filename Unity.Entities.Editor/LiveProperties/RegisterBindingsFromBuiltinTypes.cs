using System;
using UnityEngine;
using UnityEditor;
using Unity.Entities;
using Unity.Transforms;

[InitializeOnLoad]
static class RegisterBindingsFromBuiltinTypes
{
    static RegisterBindingsFromBuiltinTypes()
    {
#if !ENABLE_TRANSFORM_V1

#if false // TODO: Look at this with Xian on Monday
        BindingRegistry.Register(typeof(LocalToWorldTransform), "Value.Position.x", typeof(Transform), "m_LocalPosition.x");
        BindingRegistry.Register(typeof(LocalToWorldTransform), "Value.Position.y", typeof(Transform), "m_LocalPosition.y");
        BindingRegistry.Register(typeof(LocalToWorldTransform), "Value.Position.z", typeof(Transform), "m_LocalPosition.z");

        BindingRegistry.Register(typeof(LocalToWorldTransform), "Value.Scale", typeof(Transform), "m_LocalScale.x");
        BindingRegistry.Register(typeof(LocalToWorldTransform), "Value.Scale", typeof(Transform), "m_LocalScale.y");
        BindingRegistry.Register(typeof(LocalToWorldTransform), "Value.Scale", typeof(Transform), "m_LocalScale.z");

        BindingRegistry.Register(typeof(LocalToWorldTransform), "Value.Rotation", typeof(Transform), "m_LocalRotation");
#endif

#else
        BindingRegistry.Register(typeof(Translation), "Value.x", typeof(Transform), "m_LocalPosition.x");
        BindingRegistry.Register(typeof(Translation), "Value.y", typeof(Transform), "m_LocalPosition.y");
        BindingRegistry.Register(typeof(Translation), "Value.z", typeof(Transform), "m_LocalPosition.z");

        BindingRegistry.Register(typeof(NonUniformScale), "Value.x", typeof(Transform), "m_LocalScale.x");
        BindingRegistry.Register(typeof(NonUniformScale), "Value.y", typeof(Transform), "m_LocalScale.y");
        BindingRegistry.Register(typeof(NonUniformScale), "Value.z", typeof(Transform), "m_LocalScale.z");

        BindingRegistry.Register(typeof(Rotation), nameof(Rotation.Value), typeof(Transform), "m_LocalRotation");
#endif
    }
}
