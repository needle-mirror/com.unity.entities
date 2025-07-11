using System;
using System.Collections.Generic;
using System.IO;
using System.ComponentModel;

namespace Unity.Entities.Content
{
    public static partial class RemoteContentCatalogBuildUtility
    {
        /// <summary>
        /// Publish a folder of files.  This will copy or move files from the build folder to the target folder and rename them to the content hash.  Remote catalogs will also be created.
        /// </summary>
        /// <param name="files">The collection of files to publish.  This should be sorted before calling this method to ensure deterministic data.</param>
        /// <param name="targetFolder">The target folder for the published data.  The structure is the same as the local cache, so this data can be directly installed on device to preload the cache.</param>
        /// <param name="contentSetFunc">This will be called for each file as it is published.  The returned strings will define the content sets that the file will be a part of.  If null is returned, the content will stay in the source folder and will not be published.</param>
        /// <param name="deleteSrcContent">If true, the src content files will be deleted from the build folder.  Ensure that a build is properly backed up before enabling this.</param>
        /// <returns>True if the publish process succeeds.</returns>
        //MethodSignatureChangeConfig("RemoteContentCatalogBuildUtility::PublishContent(IEnumerable<string>, string)", "!0,!2")
        [Obsolete("Use the PublishContent method that has the source folder as a parameter.  This version will fail if it unable to determine the source folder from the passed in files.")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static bool PublishContent(IEnumerable<string> files, string targetFolder, Func<string, IEnumerable<string>> contentSetFunc, bool deleteSrcContent = false)
        {
            var sourceFolder = DetermineSourceFolder(files);
            if (string.IsNullOrEmpty(sourceFolder))
            {
                Debug.LogError($"Unable to determine sourceFolder to use for publishing files.  Please use the version of PublishContent that has sourceFolder as a parameter.");
                return false;
            }
            return PublishContent(files, sourceFolder, targetFolder, contentSetFunc, deleteSrcContent);
        }

        //This code attempts to determine the sourceFolder from passed in files.
        //This should work for normal ECS content builds builds that contain EntityScenes or ContentArchives folders but for those that don't, it will fail.
        //The source folder is necessary to create correct RemoteContentIds in the catalog.
        //Without it, any content delivery that is initiated using only a relataive path to create the RemoteContentId at runtim will fail.
        internal static string DetermineSourceFolder(IEnumerable<string> files)
        {
            string sourceFolder = null;
            foreach (var file in files)
            {
                if (sourceFolder == null)
                {
                    var filePath = Path.GetDirectoryName(file);
                    var fileDir = Path.GetFileName(filePath);
                    if (fileDir == "EntityScenes" || fileDir == "ContentArchives")
                        return Path.GetDirectoryName(filePath);
                }
            }
            return null;
        }
    }
}
