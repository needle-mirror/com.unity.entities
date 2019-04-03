using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.TestTools;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Entities.Tests
{
    public class DefaultWorldInitializationTests
    {
        
        [SetUp]
        public void Setup()
        {
        }
// TODO: [case 1040539] Remove this when entering playmode in an editmode test works in 2018.2+
#if UNITY_2018_1_OR_NEWER && !UNITY_2018_2_OR_NEWER
        [UnityTest]
        public IEnumerator Initialize_WhenEnteringPlaymode_ShouldLogNothing()
        {
            EditorApplication.isPlaying = true;
            yield return null;
        
            
            LogAssert.NoUnexpectedReceived();
        }
#endif
        [TearDown]
        public void TearDown()
        {
            EditorApplication.isPlaying = false;

        }
    }
}
