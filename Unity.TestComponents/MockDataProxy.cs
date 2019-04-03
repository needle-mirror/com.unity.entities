using System;
using UnityEngine;

namespace Unity.Entities.Tests
{
    [Serializable]
    public struct MockData : IComponentData
    {
        public int Value;
    }

    [DisallowMultipleComponent]
    public class MockDataProxy : ComponentDataProxy<MockData>
    {
    }
}