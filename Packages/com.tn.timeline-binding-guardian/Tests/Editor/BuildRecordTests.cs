using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// ビルド前検査の記録（Phase 5、仕様 11章）の保存と「前回ビルド以降」の判定。
    /// </summary>
    public sealed class BuildRecordTests
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetFullPath("Temp"), "TBGBuildRecordTests", Guid.NewGuid().ToString("N"));
        }

        [Test]
        public void BuildRecord_IsSavedInManifest_AndReloaded()
        {
            HistoryStore store = CreateStore();
            store.State.AddBuild(Build("2026-11-27T03:00:00.0000000Z", "scene-a"));
            Assert.That(store.TryCommit(store.State, new HistoryChangeSet(), out string error), Is.True, error);

            HistoryStore reloaded = CreateStore();

            Assert.That(reloaded.Status, Is.EqualTo(HistoryStoreStatus.Ready));
            BuildCheckRecord build = reloaded.State.LatestBuild;
            Assert.That(build, Is.Not.Null);
            Assert.That(build.CheckedAtUtc, Is.EqualTo("2026-11-27T03:00:00.0000000Z"));
            Assert.That(build.ScenePaths, Is.EqualTo(new[] { "Assets/scene-a.unity" }));
            Assert.That(build.ObservationIds, Is.EqualTo(new[] { "obs-scene-a" }));
            Assert.That(build.SkippedReasons, Is.EqualTo(new[] { "closed" }));
        }

        [Test]
        public void ManifestWithoutBuilds_IsReadable()
        {
            HistoryStore store = CreateStore();
            store.State.AddBuild(Build("2026-11-27T03:00:00.0000000Z", "scene-a"));
            Assert.That(store.TryCommit(store.State, new HistoryChangeSet(), out string error), Is.True, error);

            // Phase 5 より前の manifest（builds 項目なし）を再現する
            string manifestPath = Path.Combine(_directory, "manifest.json");
            string text = File.ReadAllText(manifestPath);
            string withoutBuilds = Regex.Replace(text, @",\s*""builds""\s*:\s*\[[\s\S]*\]\s*(?=\}\s*$)", string.Empty);
            Assert.That(withoutBuilds, Does.Not.Contain("\"builds\""));
            File.WriteAllText(manifestPath, withoutBuilds);

            HistoryStore reloaded = CreateStore();

            Assert.That(reloaded.Status, Is.EqualTo(HistoryStoreStatus.Ready), reloaded.ErrorMessage);
            Assert.That(reloaded.IsWritable, Is.True);
            Assert.That(reloaded.State.LatestBuild, Is.Null);
        }

        [Test]
        public void BuildRecords_KeepOnlyLatest()
        {
            HistoryState state = new HistoryState("project");
            for (int i = 0; i < HistoryState.MaxBuildRecords + 5; i++)
            {
                state.AddBuild(Build($"2026-11-27T03:{i:00}:00.0000000Z", "scene-" + i));
            }

            Assert.That(state.Builds.Count, Is.EqualTo(HistoryState.MaxBuildRecords));
            Assert.That(state.Builds.First().ScenePaths.Single(), Is.EqualTo("Assets/scene-5.unity"));
            Assert.That(state.LatestBuild.ScenePaths.Single(), Is.EqualTo($"Assets/scene-{HistoryState.MaxBuildRecords + 4}.unity"));
        }

        [Test]
        public void IsObservedAfter_ComparesWithCheckTime()
        {
            BuildCheckRecord build = Build("2026-11-27T03:00:00.0000000Z", "scene-a");

            Assert.That(build.IsObservedAfter("2026-11-27T03:00:01.0000000Z"), Is.True);
            Assert.That(build.IsObservedAfter("2026-11-27T02:59:59.0000000Z"), Is.False);
            // 検査で見つかった変化（検査の時刻と同じ）はビルドより前の変化として扱う
            Assert.That(build.IsObservedAfter("2026-11-27T03:00:00.0000000Z"), Is.False);
            Assert.That(build.IsObservedAfter("not a date"), Is.True);
        }

        private HistoryStore CreateStore()
        {
            HistoryStore store = new HistoryStore(_directory, "project");
            store.Load();
            return store;
        }

        private static BuildCheckRecord Build(string checkedAtUtc, string scene)
        {
            return new BuildCheckRecord(checkedAtUtc, "StandaloneWindows64", new List<string> { $"Assets/{scene}.unity" },
                new List<string> { "obs-" + scene }, 1, 1, new List<string> { "closed" }, string.Empty);
        }
    }
}
