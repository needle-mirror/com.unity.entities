namespace Doc.CodeSamples.Tests
{
    #region example
    using System;
    using Unity.Entities.Content;
    using UnityEngine;

    public class GameStarter : MonoBehaviour
    {
        public string remoteUrlRoot;
        public string initialContentSet;

        void Start()
        {
#if ENABLE_CONTENT_DELIVERY
        ContentDeliverySystem.Instance.UpdateContent(remoteUrlRoot, initialContentSet);
        ContentDeliverySystem.Instance.RegisterForContentUpdateCompletion(s =>
        {
            LoadMainScene();
        });
#else
            LoadMainScene();
#endif
        }

        void LoadMainScene()
        {
            //content is ready here...
        }
    }
    #endregion
}
