using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// スキャン → 変換 → 差分 → 保存の統合テスト（13章 T01, T03, T14）。履歴ファイルは Temp/ 配下に作る。
    /// </summary>
    public sealed class HistoryIntegrationTests
    {
        private string _directory;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            TestFixtureBuilder.EnsureBuilt();
        }

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetFullPath("Temp"), "TBGHistoryIntegration", Guid.NewGuid().ToString("N"));
        }

        [Test]
        public void T01_FirstRecord_IsInitialOnly_SecondRecordHasNoEvents()
        {
            GuardianHistory history = CreateHistory();

            RecordSummary first = history.Record(ScanScene(TestFixtureBuilder.SceneAPath), "test");
            RecordSummary second = history.Record(ScanScene(TestFixtureBuilder.SceneAPath), "test");

            Assert.That(first.Saved, Is.True, first.Error);
            Assert.That(first.NewEvents.Select(e => e.ChangeType), Is.EqualTo(new[] { ChangeType.Initial }));
            Assert.That(second.Saved, Is.True, second.Error);
            Assert.That(second.NewEventCount, Is.EqualTo(0));
            Assert.That(second.UpdatedEventCount, Is.EqualTo(0));
        }

        [Test]
        public void T03_AfterReload_SameScene_HasNoEvents()
        {
            GuardianHistory history = CreateHistory();
            history.Record(ScanScene(TestFixtureBuilder.SceneAPath), "test");
            history.Record(ScanScene(TestFixtureBuilder.SceneBPath), "test");

            GuardianHistory restarted = CreateHistory();
            RecordSummary summary = restarted.Record(ScanScene(TestFixtureBuilder.SceneAPath), "test");

            Assert.That(restarted.Store.State.Scopes.Count, Is.EqualTo(2));
            Assert.That(summary.NewEventCount, Is.EqualTo(0));
        }

        [Test]
        public void SharedPrefabLevels_AreRecordedOnceAcrossScenes()
        {
            GuardianHistory history = CreateHistory();
            history.Record(ScanScene(TestFixtureBuilder.SceneAPath), "test");
            int afterA = history.Store.State.OwnerLevels.Count;

            RecordSummary summary = history.Record(ScanScene(TestFixtureBuilder.SceneBPath), "test");

            // SceneB の Parent インスタンスは SceneA と同じ Middle / Child / Parent の階層を共有する
            Assert.That(summary.NewEvents.Select(e => e.ChangeType), Is.EqualTo(new[] { ChangeType.Initial }));
            Assert.That(history.Store.State.OwnerLevels.Count, Is.GreaterThan(afterA));
            Assert.That(history.Store.State.OwnerLevels.Select(o => o.OwnerLevelKey).Distinct().Count(),
                Is.EqualTo(history.Store.State.OwnerLevels.Count));
        }

        [Test]
        public void T14_DirtyScene_IsNotRecorded()
        {
            GuardianHistory history = CreateHistory();
            Scene scene = EditorSceneManager.OpenScene(TestFixtureBuilder.SceneAPath, OpenSceneMode.Single);
            EditorSceneManager.MarkSceneDirty(scene);

            RecordSummary summary = history.Record(new[] { new SceneBindingScanner().Scan(scene) }, "test");

            Assert.That(summary.Saved, Is.False);
            Assert.That(summary.RecordedScopeCount, Is.EqualTo(0));
            Assert.That(summary.SkippedReasons.Single(), Does.Contain("unsaved changes"));
            Assert.That(File.Exists(history.Store.FilePath), Is.False);

            // 変更を破棄して開き直す（フィクスチャを保存しない）
            EditorSceneManager.OpenScene(TestFixtureBuilder.SceneAPath, OpenSceneMode.Single);
        }

        [Test]
        public void UnsavedScene_IsNotRecorded()
        {
            GuardianHistory history = CreateHistory();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RecordSummary summary = history.Record(new[] { new SceneBindingScanner().Scan(scene) }, "test");

            Assert.That(summary.RecordedScopeCount, Is.EqualTo(0));
            Assert.That(summary.SkippedReasons.Single(), Does.Contain("never been saved"));
        }

        [Test]
        public void Converter_ClassifiesContributionsPerLevel()
        {
            ScanResult scan = ScanScene(TestFixtureBuilder.SceneBPath)[0];
            List<ContextSnapshot> contexts = SnapshotConverter.Convert(scan);

            // Variant で ClearGenericBinding した Signal: Variant 階層が「要素を削除」として値を定めている
            ContextSnapshot signal = Find(contexts, "ParentVariant#1/MiddleP#0/Child#1/Director#0", TestFixtureBuilder.TrackSignal);
            LevelSnapshot variantLevel = signal.Levels.Single(l => l.Owner.AssetPathHint == TestFixtureBuilder.VariantPath);
            Assert.That(variantLevel.Contribution, Is.EqualTo(ContributionKind.Defined));
            Assert.That(variantLevel.Value.ValueState, Is.EqualTo(ValueState.NoEntry));
            Assert.That(signal.Levels[0].Contribution, Is.EqualTo(ContributionKind.Inherited));

            // Middle の NULL Override: Middle 階層が定め、Parent・Scene は継承
            ContextSnapshot activation = Find(contexts, "Parent#0/MiddleP#0/Child#1/Director#0", TestFixtureBuilder.TrackActivation);
            Assert.That(activation.Levels.Select(l => l.Contribution),
                Is.EqualTo(new[] { ContributionKind.Inherited, ContributionKind.Inherited, ContributionKind.Defined, ContributionKind.Defined }));
            Assert.That(activation.IsUncertain, Is.False);
        }

        private GuardianHistory CreateHistory()
        {
            HistoryStore store = new HistoryStore(_directory, "test-project");
            store.Load();
            return new GuardianHistory(store);
        }

        private static ScanResult[] ScanScene(string path)
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            return new[] { new SceneBindingScanner().Scan(scene) };
        }

        private static ContextSnapshot Find(List<ContextSnapshot> contexts, string directorIndexPath, string trackName)
        {
            return contexts.Single(c => c.Director.IndexPathHint == directorIndexPath && c.Track.TrackPathHint.Split('/').Last() == trackName);
        }
    }
}
