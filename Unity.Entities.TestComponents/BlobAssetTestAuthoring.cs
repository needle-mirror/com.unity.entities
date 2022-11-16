using UnityEngine;

namespace Unity.Entities.TestComponents
{
    public class BlobAssetAddTestAuthoring : MonoBehaviour
    {
        public int blobValue = 0;
    }

    public struct BlobAssetReference : IComponentData
    {
        public int blobValue;
        public Hash128 blobHash;
        public BlobAssetReference<int> blobReference;
    }

    public class AddBlobAssetWithDefaultHashBaker : Baker<BlobAssetAddTestAuthoring>
    {
        public override void Bake(BlobAssetAddTestAuthoring authoring)
        {
            var blobReference = BlobAssetUtility.CreateBlobAsset(authoring.blobValue);
            AddBlobAsset(ref blobReference, out Hash128 objectHash);

            AddComponent(new BlobAssetReference()
            {
                blobValue = blobReference.Value,
                blobHash = objectHash,
                blobReference = blobReference
            });
        }
    }


    [DisableAutoCreation]
    public class AddBlobAssetWithCustomHashBaker : Baker<BlobAssetAddTestAuthoring>
    {
        public override void Bake(BlobAssetAddTestAuthoring authoring)
        {
            var customHash = authoring.blobValue.GetHashCode();
            var customCustomHash = new Hash128((uint) customHash, 1, 2, 3);

            var blobReference = BlobAssetUtility.CreateBlobAsset(authoring.blobValue);
            AddBlobAssetWithCustomHash(ref blobReference, customCustomHash);

            AddComponent(new BlobAssetReference()
            {
                blobValue = blobReference.Value,
                blobHash = customCustomHash,
                blobReference = blobReference
            });
        }
    }

    public struct BlobAssetGetReference : IComponentData
    {
        public int blobValue;
        public Hash128 blobHash;
        public BlobAssetReference<int> blobReference;
    }

    [DisableAutoCreation]
    public class GetBlobAssetWithCustomHashBaker : Baker<BlobAssetAddTestAuthoring>
    {
        public override void Bake(BlobAssetAddTestAuthoring authoring)
        {
            var customHash = authoring.blobValue.GetHashCode();
            var customCustomHash = new Hash128((uint) customHash, 1, 2, 3);

            if (TryGetBlobAssetReference(customCustomHash, out BlobAssetReference<int> blobReference))
            {
                AddComponent(new BlobAssetReference()
                {
                    blobValue = blobReference.Value,
                    blobHash = customCustomHash,
                    blobReference = blobReference
                });
            }
            else
            {
                blobReference = BlobAssetUtility.CreateBlobAsset(authoring.blobValue);
                AddBlobAssetWithCustomHash(ref blobReference, customCustomHash);
                AddComponent(new BlobAssetGetReference()
                {
                    blobValue = blobReference.Value,
                    blobHash = customCustomHash,
                    blobReference = blobReference
                });
            }
        }
    }
}
