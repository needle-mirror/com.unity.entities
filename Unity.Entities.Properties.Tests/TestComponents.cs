using Unity.Entities;
using Unity.Mathematics;


namespace Unity.Entities.Properties.Tests
{
    public struct TestComponent : IComponentData
    {
        public float x;
    }

    public struct TestComponent2 : IComponentData
    {
        public int x;
        public byte b;
    }
    
    public struct MathComponent : IComponentData
    {
        public float2 v2;
        public float3 v3;
        public float4 v4;
        public float2x2 m2;
        public float3x3 m3;
        public float4x4 m4;
    }

    public struct NestedComponent : IComponentData
    {
        public TestComponent test;
    }

    public struct BlitMe
    {
        public float x;
        public double y;
        public sbyte z;
    }

    public struct BlitComponent : IComponentData
    {
        public BlitMe blit;
        public float flt;
    }

    public struct TestSharedComponent : ISharedComponentData
    {
        public float value;

        public TestSharedComponent(float v) { value = v; }
    }
}
