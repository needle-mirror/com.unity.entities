using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Unity.Entities.Tests
{
    public class GetComponentTransformBaker : Baker<TestComponentAuthoring>
    {
        public struct Vector3Element : IComponentData
        {
            public static implicit operator float3(Vector3Element e)
            {
                return e.Value;
            }

            public static implicit operator Vector3Element(float3 e)
            {
                return new Vector3Element {Value = e};
            }

            public float3 Value;
        }

        public override void Bake(TestComponentAuthoring authoring)
        {
            var position = GetComponent<Transform>().position;

            AddComponent(new Vector3Element { Value =  position });
        }
    }

    public class GetParentBaker : Baker<TestComponentAuthoring>
	{
        public struct IntElement : IBufferElementData
        {
            public static implicit operator int(IntElement e)
            {
                return e.Value;
            }

            public static implicit operator IntElement(int e)
            {
                return new IntElement {Value = e};
            }

            public int Value;
        }

		public override void Bake(TestComponentAuthoring authoring)
		{
            var parent = GetParent();

			DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            if (parent)
                buffer.Add(parent.GetInstanceID());
        }
	}

    public class GetParentsBaker : Baker<TestComponentAuthoring>
    {
        public struct IntElement : IBufferElementData
        {
            public static implicit operator int(IntElement e)
            {
                return e.Value;
            }

            public static implicit operator IntElement(int e)
            {
                return new IntElement {Value = e};
            }

            public int Value;
        }

        public override void Bake(TestComponentAuthoring authoring)
        {
            var parents = GetParents();

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            if (parents != null)
            {
                foreach (var parent in parents)
                {
                    buffer.Add(parent.GetInstanceID());
                }
            }
        }
    }

    public class GetChildCountBaker : Baker<TestComponentAuthoring>
    {
        public struct IntComponent : IComponentData
        {
            public int Value;
        }

        public override void Bake(TestComponentAuthoring authoring)
        {
            AddComponent(new IntComponent() { Value = GetChildCount() });
        }
    }

    public class GetChildBaker : Baker<TestComponentAuthoring>
    {
        public struct IntElement : IBufferElementData
        {
            public static implicit operator int(IntElement e)
            {
                return e.Value;
            }

            public static implicit operator IntElement(int e)
            {
                return new IntElement {Value = e};
            }

            public int Value;
        }

        public override void Bake(TestComponentAuthoring authoring)
        {
            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            if (authoring.transform.childCount > 0)
            {
                var child = GetChild(0);
                buffer.Add(child.GetInstanceID());
            }
        }
    }

    public class GetChildrenBaker : Baker<TestComponentAuthoring>
    {
        public static bool Recursive;

        public struct IntElement : IBufferElementData
        {
            public static implicit operator int(IntElement e)
            {
                return e.Value;
            }

            public static implicit operator IntElement(int e)
            {
                return new IntElement {Value = e};
            }

            public int Value;
        }

        public override void Bake(TestComponentAuthoring authoring)
        {
            var children = GetChildren(Recursive);

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            if (children != null)
            {
                foreach (var child in children)
                {
                    buffer.Add(child.GetInstanceID());
                }
            }
        }
    }
}
