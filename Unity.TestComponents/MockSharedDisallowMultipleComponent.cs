using System;
using UnityEngine;

namespace Unity.Entities.Tests
{
    [Serializable]
    public struct MockSharedDisallowMultiple : ISharedComponentData
    {
        public int Value;
    }

    [DisallowMultipleComponent]
    public class MockSharedDisallowMultipleComponent : SharedComponentDataWrapper<MockSharedDisallowMultiple>
    {

    }
}
