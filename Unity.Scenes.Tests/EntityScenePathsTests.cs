using System.IO;
using NUnit.Framework;
using Unity.Scenes;

public class EntityScenePathsTests
{
    [Test]
    public void TestGetSectionIndexFromPath()
    {
        Assert.AreEqual(0, EntityScenesPaths.GetSectionIndexFromPath(""));
        Assert.AreEqual(5, EntityScenesPaths.GetSectionIndexFromPath("somelongpath.5.entities"));
        Assert.AreEqual(100, EntityScenesPaths.GetSectionIndexFromPath("somelongpath.100.entities"));
        Assert.AreEqual(99, EntityScenesPaths.GetSectionIndexFromPath("somelongpathwith.dots.in.the.name.98.99.entities"));
        Assert.AreEqual(0, EntityScenesPaths.GetSectionIndexFromPath("pathwith.dots.but.no.number.entities"));
    }

#if !NET_DOTS
    [Test]
    public void TestGetTempCachePath()
    {
        var tempCachePath = EntityScenesPaths.GetTempCachePath();

        // Verify we can make and remove this path
        if (!Directory.Exists(tempCachePath))
            Directory.CreateDirectory(tempCachePath);

        Directory.Delete(tempCachePath);
    }
#endif
}
