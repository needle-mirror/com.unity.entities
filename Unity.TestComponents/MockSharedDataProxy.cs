using System;

namespace Unity.Entities.Tests
{
    [Serializable]
    public struct MockSharedData : ISharedComponentData
    {
        public int Value;
    }

    public class MockSharedDataProxy : SharedComponentDataProxy<MockSharedData>
    {
    }
}