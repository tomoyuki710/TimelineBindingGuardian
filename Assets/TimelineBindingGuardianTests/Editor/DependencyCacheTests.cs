using System.Collections.Generic;
using NUnit.Framework;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 依存一覧のキャッシュ: 変わったアセットと、それを依存に含む一覧だけを捨てる（Unity 非依存）。
    /// </summary>
    public sealed class DependencyCacheTests
    {
        // Scene → Outer.prefab → Inner.prefab → Material、Other.prefab は独立
        private static readonly Dictionary<string, string[]> Graph = new Dictionary<string, string[]>
        {
            ["Scene.unity"] = new[] { "Scene.unity", "Outer.prefab", "Inner.prefab", "Mat.mat" },
            ["Outer.prefab"] = new[] { "Outer.prefab", "Inner.prefab", "Mat.mat" },
            ["Inner.prefab"] = new[] { "Inner.prefab", "Mat.mat" },
            ["Other.prefab"] = new[] { "Other.prefab" },
        };

        private int _providerCalls;

        [Test]
        public void SecondLookup_UsesTheCache()
        {
            DependencyCache cache = CreateCache();

            Assert.That(cache.DependsOn("Scene.unity", "Inner.prefab"), Is.True);
            Assert.That(cache.DependsOn("Scene.unity", "Other.prefab"), Is.False);

            Assert.That(_providerCalls, Is.EqualTo(1));
        }

        [Test]
        public void Sorted_IsInOrdinalOrder()
        {
            DependencyCache cache = CreateCache();

            Assert.That(cache.GetSorted("Scene.unity"), Is.EqualTo(new[] { "Inner.prefab", "Mat.mat", "Outer.prefab", "Scene.unity" }));
        }

        [Test]
        public void Invalidate_RemovesTheAssetAndEntriesContainingIt_AndKeepsOthers()
        {
            DependencyCache cache = CreateCache();
            foreach (string path in Graph.Keys)
            {
                cache.GetSorted(path);
            }

            cache.Invalidate(new[] { "Inner.prefab" });

            Assert.That(cache.Contains("Inner.prefab"), Is.False, "the changed asset itself");
            Assert.That(cache.Contains("Outer.prefab"), Is.False, "contains Inner directly");
            Assert.That(cache.Contains("Scene.unity"), Is.False, "contains Inner indirectly");
            Assert.That(cache.Contains("Other.prefab"), Is.True, "does not depend on Inner");
        }

        [Test]
        public void Invalidate_ByANonScopeAsset_RemovesEveryEntryUsingIt()
        {
            DependencyCache cache = CreateCache();
            foreach (string path in Graph.Keys)
            {
                cache.GetSorted(path);
            }

            // マテリアルの参照先が変わった場合など、Prefab・Scene 以外の変更でも一覧が古くなりうる
            cache.Invalidate(new[] { "Mat.mat" });

            Assert.That(cache.Count, Is.EqualTo(1));
            Assert.That(cache.Contains("Other.prefab"), Is.True);
        }

        [Test]
        public void Invalidate_WithUnrelatedOrEmptyPaths_KeepsEverything()
        {
            DependencyCache cache = CreateCache();
            cache.GetSorted("Scene.unity");

            cache.Invalidate(new[] { "Unrelated.png", string.Empty, null });

            Assert.That(cache.Contains("Scene.unity"), Is.True);
        }

        private DependencyCache CreateCache()
        {
            _providerCalls = 0;
            return new DependencyCache(path =>
            {
                _providerCalls++;
                return Graph.TryGetValue(path, out string[] dependencies) ? dependencies : new[] { path };
            });
        }
    }
}
