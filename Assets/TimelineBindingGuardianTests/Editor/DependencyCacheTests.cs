using System.Collections.Generic;
using NUnit.Framework;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 依存関係のキャッシュ（Unity 非依存）: 1 段の依存をキャッシュし、Prefab・Scene・Timeline の間だけをたどる。
    /// 変わったアセットの 1 段の依存だけを取り直す。
    /// </summary>
    public sealed class DependencyCacheTests
    {
        // Scene → Outer.prefab → Inner.prefab → Timeline.playable → Track.cs
        //                       Inner.prefab → Mat.mat → Tex.png（Mat の先はたどらない）
        // Other.prefab は独立
        private Dictionary<string, string[]> _graph;
        private Dictionary<string, int> _providerCalls;

        [SetUp]
        public void SetUp()
        {
            _graph = new Dictionary<string, string[]>
            {
                ["Scene.unity"] = new[] { "Outer.prefab" },
                ["Outer.prefab"] = new[] { "Inner.prefab" },
                ["Inner.prefab"] = new[] { "Timeline.playable", "Mat.mat" },
                ["Timeline.playable"] = new[] { "Track.cs" },
                ["Mat.mat"] = new[] { "Tex.png" },
                ["Other.prefab"] = new[] { "Other.fbx" },
            };
            _providerCalls = new Dictionary<string, int>();
        }

        [Test]
        public void Closure_FollowsPrefabsScenesAndTimelines_AndIncludesTheirDirectLeaves()
        {
            DependencyCache cache = CreateCache();

            Assert.That(cache.GetSorted("Scene.unity"), Is.EqualTo(new[]
            {
                "Inner.prefab", "Mat.mat", "Outer.prefab", "Scene.unity", "Timeline.playable", "Track.cs",
            }));
            Assert.That(cache.DependsOn("Scene.unity", "Tex.png"), Is.False, "the material is a leaf and is not followed");
            Assert.That(_providerCalls.ContainsKey("Mat.mat"), Is.False);
        }

        [Test]
        public void DirectDependencies_AreFetchedOncePerPath()
        {
            DependencyCache cache = CreateCache();

            cache.GetSorted("Scene.unity");
            cache.GetSorted("Outer.prefab");
            cache.DependsOn("Inner.prefab", "Mat.mat");

            Assert.That(_providerCalls.Values, Is.All.EqualTo(1));
        }

        [Test]
        public void Invalidate_RefetchesOnlyTheChangedAsset_AndClosuresSeeTheNewReference()
        {
            DependencyCache cache = CreateCache();
            Assert.That(cache.DependsOn("Scene.unity", "Other.prefab"), Is.False);

            // Inner が Other を入れ子にするよう変更された
            _graph["Inner.prefab"] = new[] { "Timeline.playable", "Mat.mat", "Other.prefab" };
            cache.Invalidate(new[] { "Inner.prefab" });

            Assert.That(cache.DependsOn("Scene.unity", "Other.prefab"), Is.True);
            Assert.That(cache.DependsOn("Scene.unity", "Other.fbx"), Is.True);
            Assert.That(_providerCalls["Inner.prefab"], Is.EqualTo(2));
            Assert.That(_providerCalls["Outer.prefab"], Is.EqualTo(1), "unchanged assets are not fetched again");
            Assert.That(_providerCalls["Scene.unity"], Is.EqualTo(1));
        }

        [Test]
        public void Invalidate_WithUnrelatedOrEmptyPaths_KeepsTheCache()
        {
            DependencyCache cache = CreateCache();
            cache.GetSorted("Scene.unity");

            cache.Invalidate(new[] { "Unrelated.png", string.Empty, null });

            Assert.That(cache.Contains("Scene.unity"), Is.True);
            Assert.That(cache.Contains("Inner.prefab"), Is.True);
            cache.GetSorted("Scene.unity");
            Assert.That(_providerCalls.Values, Is.All.EqualTo(1));
        }

        private DependencyCache CreateCache()
        {
            return new DependencyCache(path =>
            {
                _providerCalls[path] = _providerCalls.TryGetValue(path, out int count) ? count + 1 : 1;
                return _graph.TryGetValue(path, out string[] dependencies) ? dependencies : new string[0];
            });
        }
    }
}
