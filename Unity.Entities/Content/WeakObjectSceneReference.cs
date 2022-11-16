#if !UNITY_DOTSRUNTIME
using System;
using Unity.Entities.Serialization;
using UnityEngine.SceneManagement;

namespace Unity.Entities.Content
{
    /// <summary>
    /// Weak reference to a scene. The Result field can be accessed after the scene has completed loading.
    /// </summary>
    [Serializable]
    public struct WeakObjectSceneReference : IEquatable<WeakObjectSceneReference>
    {
        /// <summary>
        /// The reference Id.
        /// </summary>
        public UntypedWeakReferenceId Id;

        /// <summary>
        /// Returns if the id of the referenced scene is valid.
        /// </summary>
        public bool IsReferenceValid => Id.IsValid;

        /// <summary>
        /// True if the object is either being loaded or already finished loading.
        /// </summary>
        public Unity.Loading.SceneLoadingStatus LoadingStatus => RuntimeContentManager.GetSceneLoadingStatus(Id);

        /// <summary>
        /// The loaded scene object.
        /// </summary>
        public Scene SceneResult
        {
            get
            {
                return RuntimeContentManager.GetSceneValue(Id);
            }
        }

        /// <summary>
        /// The ContentSceneFile that was loaded.  This can be used to manually integrate the loaded scene at the end of the frame.
        /// </summary>
        public Unity.Loading.ContentSceneFile SceneFileResult
        {
            get
            {
                return RuntimeContentManager.GetSceneFileValue(Id);
            }
        }

        /// <summary>
        /// Directs the object to begin loading.  This will increase the reference count for each call to the same id.  Release must be called for each Load call to properly release resources.
        /// </summary>
        /// <param name="loadParams">The scene loading parameters.</param>
        public void LoadAsync(Unity.Loading.ContentSceneParameters loadParams)
        {
            RuntimeContentManager.LoadSceneAsync(Id, loadParams);
        }

        /// <summary>
        /// Releases the object.  This will decrement the reference count of this object.  When an objects reference count reaches 0, the archive file is released.  The archive file is only
        /// unloaded when its reference count reaches zero, which will then release the archive it was loaded from.  Archives will be unmounted when their reference count reaches 0.
        /// </summary>
        public void Release()
        {
            RuntimeContentManager.ReleaseScene(Id);
        }

        /// <summary>
        /// String conversion override.
        /// </summary>
        /// <returns>String representation of reference which includes type, guid and local id.</returns>
        public override string ToString() => $"WeakSceneReference -> {Id}";

        /// <inheritdoc/>
        public bool Equals(WeakObjectSceneReference other)
        {
            return Id.Equals(other.Id);
        }

        /// <summary>
        /// Returns the hash code of this reference.
        /// </summary>
        /// <returns>The hash code of this reference.</returns>
        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }
    }
}
#endif

