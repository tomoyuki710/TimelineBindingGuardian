using NUnit.Framework;

namespace TimelineBindingGuardian
{
    public sealed class PathExclusionTests
    {
        [Test]
        public void AssetInsideFolder_IsExcluded()
        {
            Assert.That(PathExclusion.IsExcluded("Assets/Samples/Demo/Scene.unity", new[] { "Assets/Samples" }), Is.True);
        }

        [Test]
        public void FolderItself_IsExcluded()
        {
            Assert.That(PathExclusion.IsExcluded("Assets/Samples", new[] { "Assets/Samples" }), Is.True);
        }

        [Test]
        public void FolderWithSamePrefix_IsNotExcluded()
        {
            Assert.That(PathExclusion.IsExcluded("Assets/SamplesExtra/Scene.unity", new[] { "Assets/Samples" }), Is.False);
        }

        [Test]
        public void CaseAndSeparatorDifferences_AreIgnored()
        {
            Assert.That(PathExclusion.IsExcluded("assets/samples/Prefab.prefab", new[] { "Assets\\Samples\\" }), Is.True);
        }

        [Test]
        public void EmptyOrBlankFolders_ExcludeNothing()
        {
            Assert.That(PathExclusion.IsExcluded("Assets/Scene.unity", new string[0]), Is.False);
            Assert.That(PathExclusion.IsExcluded("Assets/Scene.unity", new[] { "", "  " }), Is.False);
            Assert.That(PathExclusion.IsExcluded("", new[] { "Assets" }), Is.False);
        }

        [Test]
        public void NormalizeFolder_RemovesTrailingSlashAndBackslashes()
        {
            Assert.That(PathExclusion.NormalizeFolder(" Assets\\TBGTestFixtures\\v1/ "), Is.EqualTo("Assets/TBGTestFixtures/v1"));
        }
    }
}
