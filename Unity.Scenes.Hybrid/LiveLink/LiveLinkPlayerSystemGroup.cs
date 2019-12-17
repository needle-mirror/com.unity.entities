using System.IO;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Networking;
using Hash128 = Unity.Entities.Hash128;

namespace Unity.Scenes
{
    
    //@TODO: #ifdefs massively increase iteration time right now when building players (Should be fixed in 20.1)
    //       Until then always have the live link code present.
#if UNITY_EDITOR
    [DisableAutoCreation]
#endif
    [ExecuteAlways]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateBefore(typeof(SceneSystemGroup))]
    class LiveLinkRuntimeSystemGroup : ComponentSystemGroup
    {
        protected override void OnCreate()
        {
#if UNITY_ANDROID
            var uwrFile = new UnityWebRequest(SceneSystem.GetBootStrapPath());
            uwrFile.SendWebRequest();
            while(!uwrFile.isDone) {}

            if (uwrFile.isNetworkError || uwrFile.isHttpError)
            {
                Enabled = false;
            }
            else
            {
                Enabled = true;
            }
#else
            Enabled = File.Exists(SceneSystem.GetBootStrapPath());
#endif
            if (Enabled)
            {
                if (!UnityEngine.Networking.PlayerConnection.PlayerConnection.instance.isConnected)
                    Debug.LogError("Failed to connect to the Editor.\nAn Editor connection is required for LiveLink to work.");
            }
        }
    }
}