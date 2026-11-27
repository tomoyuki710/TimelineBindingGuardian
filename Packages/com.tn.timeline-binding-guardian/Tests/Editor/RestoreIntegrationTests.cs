using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 復元の統合テスト（仕様 10 章、13章 T16 / T17 / T20、Scene の Undo）。
    /// 書き換え専用の素材 Assets/TBGTestFixtures/Mutable/ を使い、各テストは「変更 → 復元」の往復で初期状態に戻す。
    /// 復元後のファイルが変更前とバイト単位で同じであることで、他の Track・Override・fileID が変わっていないことを確かめる（T20）。
    /// </summary>
    public sealed class RestoreIntegrationTests
    {
        private string _middlePath;
        private string _parentPath;
        private string _historyDirectory;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            TestFixtureBuilder.BuildMutable();
            _middlePath = TestFixtureBuilder.PathIn(TestFixtureBuilder.MutableFolderName, "Middle.prefab");
            _parentPath = TestFixtureBuilder.PathIn(TestFixtureBuilder.MutableFolderName, "Parent.prefab");
        }

        [SetUp]
        public void SetUp()
        {
            _historyDirectory = Path.Combine(Path.GetFullPath("Temp"), "TBGRestoreTests", Guid.NewGuid().ToString("N"));
            // バックアップと復元後の記録は、実プロジェクトではなくテスト用の場所に書く
            BindingRestoreService.BackupDirectory = Path.Combine(_historyDirectory, "backups");
            AssertBaseline();
        }

        [TearDown]
        public void TearDown()
        {
            BindingRestoreService.BackupDirectory = null;
            BindingRestoreService.History = null;
        }

        [Test]
        public void T16_NullOverrideAtMiddle_RevertToInherited_FileIsIdenticalToBefore()
        {
            GuardianHistory history = CreateHistory();
            byte[] original = File.ReadAllBytes(_middlePath);

            MutateMiddle(0, TestFixtureBuilder.TrackActivation, director => director.SetGenericBinding(Track(director, TestFixtureBuilder.TrackActivation), null));
            ChangeEvent lost = RecordMiddle(history);
            Assert.That(lost.ChangeType, Is.EqualTo(ChangeType.BindingLost));

            RestorePreview preview = BindingRestoreService.CreatePreview(lost);
            RestoreOption option = RestorePlanner.GetRecommended(preview.Plan);
            Assert.That(preview.Plan.CanRestore, Is.True, string.Join(" / ", preview.Plan.Refusals));
            Assert.That(option.Operation, Is.EqualTo(RestoreOperation.RevertToInherited));
            Assert.That(preview.Plan.Warnings, Is.Empty);

            RestoreResult result = BindingRestoreService.Execute(preview, option);

            Assert.That(result.Success, Is.True, result.Message + " " + string.Join(" / ", result.Details));
            Assert.That(File.ReadAllBytes(_middlePath), Is.EqualTo(original));
            Assert.That(File.Exists(result.BackupPath), Is.True);
            Assert.That(EffectiveInParent("Parent#0/MiddleP#0/Child#0/Director#0", TestFixtureBuilder.TrackActivation), Is.EqualTo(ValueState.Bound));
        }

        [Test]
        public void T16_Rollback_PutsBackThePreRestoreFile()
        {
            GuardianHistory history = CreateHistory();
            byte[] original = File.ReadAllBytes(_middlePath);
            MutateMiddle(0, TestFixtureBuilder.TrackActivation, director => director.SetGenericBinding(Track(director, TestFixtureBuilder.TrackActivation), null));
            byte[] mutated = File.ReadAllBytes(_middlePath);
            ChangeEvent lost = RecordMiddle(history);
            RestorePreview preview = BindingRestoreService.CreatePreview(lost);
            RestoreResult restored = BindingRestoreService.Execute(preview, RestorePlanner.GetRecommended(preview.Plan));
            Assert.That(restored.Success, Is.True, restored.Message);

            RestoreResult undone = BindingRestoreService.Rollback(restored);

            Assert.That(undone.Success, Is.True, undone.Message);
            Assert.That(File.ReadAllBytes(_middlePath), Is.EqualTo(mutated));

            // 素材を初期状態に戻す
            RestorePreview again = BindingRestoreService.CreatePreview(lost);
            Assert.That(BindingRestoreService.Execute(again, RestorePlanner.GetRecommended(again.Plan)).Success, Is.True);
            Assert.That(File.ReadAllBytes(_middlePath), Is.EqualTo(original));
        }

        [Test]
        public void T17_ExplicitOverride_SetValueBack_FileIsIdenticalToBefore()
        {
            GuardianHistory history = CreateHistory();
            byte[] original = File.ReadAllBytes(_middlePath);

            MutateMiddle(1, TestFixtureBuilder.TrackAnimA, director =>
            {
                Animator other = director.transform.parent.Find("ActorB").GetComponent<Animator>();
                director.SetGenericBinding(Track(director, TestFixtureBuilder.TrackAnimA), other);
            });
            ChangeEvent changed = RecordMiddle(history);
            Assert.That(changed.ChangeType, Is.EqualTo(ChangeType.TargetChanged));

            RestorePreview preview = BindingRestoreService.CreatePreview(changed);
            RestoreOption option = RestorePlanner.GetRecommended(preview.Plan);
            Assert.That(option.Operation, Is.EqualTo(RestoreOperation.SetValue));
            RestoreResult result = BindingRestoreService.Execute(preview, option);

            Assert.That(result.Success, Is.True, result.Message + " " + string.Join(" / ", result.Details));
            Assert.That(File.ReadAllBytes(_middlePath), Is.EqualTo(original));
        }

        [Test]
        public void T17_ExplicitNull_SetNullBack_FileIsIdenticalToBefore()
        {
            GuardianHistory history = CreateHistory();
            byte[] original = File.ReadAllBytes(_middlePath);

            // Child#1 の Activation は初期状態で明示的な NULL。Override を外して継承（ActTarget）にする
            MutateMiddle(1, TestFixtureBuilder.TrackActivation, director =>
            {
                using (SerializedObject serializedObject = new SerializedObject(director))
                {
                    PrefabUtility.RevertPropertyOverride(FindValue(serializedObject, Track(director, TestFixtureBuilder.TrackActivation)), InteractionMode.AutomatedAction);
                }
            });
            ChangeEvent assigned = RecordMiddle(history);
            Assert.That(assigned.ChangeType, Is.EqualTo(ChangeType.BindingAssigned));

            RestorePreview preview = BindingRestoreService.CreatePreview(assigned);
            RestoreOption option = RestorePlanner.GetRecommended(preview.Plan);
            Assert.That(option.ResultValue.ValueState, Is.EqualTo(ValueState.Null));
            RestoreResult result = BindingRestoreService.Execute(preview, option);

            Assert.That(result.Success, Is.True, result.Message + " " + string.Join(" / ", result.Details));
            Assert.That(File.ReadAllBytes(_middlePath), Is.EqualTo(original));
        }

        [Test]
        public void RemoveOverride_FromCurrentState_WithoutHistoryEvent()
        {
            CreateHistory();
            byte[] original = File.ReadAllBytes(_middlePath);
            ScanResult scan = new SceneBindingScanner().ScanPrefabAsset(_middlePath);
            TrackBindingObservation track = scan.Directors.Single(d => d.Director.IndexPathHint == "Middle#0/Child#1/Director#0")
                .Tracks.Single(t => t.Track.TrackPathHint == TestFixtureBuilder.TrackActivation);
            LevelBindingState middleLevel = track.Levels[0];
            Assert.That(middleLevel.ValueState, Is.EqualTo(ValueState.Null));
            Assert.That(middleLevel.ValueOverridden, Is.True);

            ChangeEvent target = BindingRestoreService.CreateRemoveOverrideTarget(middleLevel, track.Track);
            RestorePreview preview = BindingRestoreService.CreatePreview(target, true);
            RestoreOption option = RestorePlanner.GetRecommended(preview.Plan);
            Assert.That(option.Operation, Is.EqualTo(RestoreOperation.RevertToInherited), string.Join(" / ", preview.Plan.Refusals));
            RestoreResult result = BindingRestoreService.Execute(preview, option);

            Assert.That(result.Success, Is.True, result.Message + " " + string.Join(" / ", result.Details));
            Assert.That(EffectiveInParent("Parent#0/MiddleP#0/Child#1/Director#0", TestFixtureBuilder.TrackActivation), Is.EqualTo(ValueState.Bound));
            Assert.That(BindingRestoreService.CreatePreview(target, true).Plan.IsAlreadyInEarlierState, Is.True);

            // 素材を初期状態に戻す（明示的な NULL を設定し直す）
            LevelValue explicitNull = new LevelValue(ValueState.Null, LocalState.ExplicitOverride, string.Empty, string.Empty, string.Empty);
            ChangeEvent putBack = new ChangeEvent("put-back", "obs", "t", ChangeType.BindingAssigned, string.Empty, middleLevel.Owner, middleLevel.Director,
                track.Track, explicitNull, LevelValue.None, false, string.Empty);
            RestorePreview back = BindingRestoreService.CreatePreview(putBack);
            Assert.That(BindingRestoreService.Execute(back, RestorePlanner.GetRecommended(back.Plan)).Success, Is.True);
            Assert.That(File.ReadAllBytes(_middlePath), Is.EqualTo(original));
        }

        /// <summary>
        /// Timeline のプレビュー中（Animation Mode）でも Prefab の復元ができる（ユーザー報告の不具合の再現テスト）。
        /// </summary>
        [Test]
        public void AnimationMode_PrefabRevertAndRemoveOverride_Succeed()
        {
            GuardianHistory history = CreateHistory();
            byte[] original = File.ReadAllBytes(_middlePath);
            MutateMiddle(0, TestFixtureBuilder.TrackActivation, director => director.SetGenericBinding(Track(director, TestFixtureBuilder.TrackActivation), null));
            ChangeEvent lost = RecordMiddle(history);

            AnimationMode.StartAnimationMode();
            try
            {
                Assert.That(AnimationMode.InAnimationMode(), Is.True);

                // 1) 履歴のイベントから継承に戻す
                RestorePreview preview = BindingRestoreService.CreatePreview(lost);
                RestoreResult restored = BindingRestoreService.Execute(preview, RestorePlanner.GetRecommended(preview.Plan));
                Assert.That(restored.Success, Is.True, restored.Message + " " + string.Join(" / ", restored.Details));
                Assert.That(File.ReadAllBytes(_middlePath), Is.EqualTo(original));

                // 2) 現在の NULL の Override を外す（Child#1 の Activation は初期状態で明示的な NULL）
                ScanResult scan = new SceneBindingScanner().ScanPrefabAsset(_middlePath);
                TrackBindingObservation track = scan.Directors.Single(d => d.Director.IndexPathHint == "Middle#0/Child#1/Director#0")
                    .Tracks.Single(t => t.Track.TrackPathHint == TestFixtureBuilder.TrackActivation);
                RestorePreview remove = BindingRestoreService.CreatePreview(BindingRestoreService.CreateRemoveOverrideTarget(track.Levels[0], track.Track), true);
                RestoreResult removed = BindingRestoreService.Execute(remove, RestorePlanner.GetRecommended(remove.Plan));
                Assert.That(removed.Success, Is.True, removed.Message + " " + string.Join(" / ", removed.Details));

                // 3) 明示的な NULL を設定し直す（値の設定も Animation Mode 中に動く）
                LevelValue explicitNull = new LevelValue(ValueState.Null, LocalState.ExplicitOverride, string.Empty, string.Empty, string.Empty);
                ChangeEvent putBack = new ChangeEvent("put-back", "obs", "t", ChangeType.BindingAssigned, string.Empty, track.Levels[0].Owner,
                    track.Levels[0].Director, track.Track, explicitNull, LevelValue.None, false, string.Empty);
                RestorePreview back = BindingRestoreService.CreatePreview(putBack);
                RestoreResult putBackResult = BindingRestoreService.Execute(back, RestorePlanner.GetRecommended(back.Plan));
                Assert.That(putBackResult.Success, Is.True, putBackResult.Message);
                Assert.That(File.ReadAllBytes(_middlePath), Is.EqualTo(original));
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }
        }

        /// <summary>
        /// Timeline のプレビュー中（Animation Mode）でも Scene の Override を外せ、プレビュー終了後も残り、Undo / Redo できる。
        /// </summary>
        [Test]
        public void AnimationMode_SceneRemoveOverride_PersistsAfterPreviewAndUndoes()
        {
            string scenePath = TestFixtureBuilder.PathIn(TestFixtureBuilder.MutableFolderName, "SceneA.unity");
            byte[] sceneBytes = File.ReadAllBytes(scenePath);
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            try
            {
                ScanResult scan = new SceneBindingScanner().Scan(scene);
                DirectorObservation observed = scan.Directors.Single(d => d.Director.IndexPathHint == "Parent#1/MiddleP#0/Child#0/Director#0");
                TrackBindingObservation track = observed.Tracks.Single(t => t.Track.TrackPathHint == TestFixtureBuilder.TrackActivation);
                PlayableDirector director = (PlayableDirector)GlobalObjectId.GlobalObjectIdentifierToObjectSlow(ParseId(observed.Director.GlobalObjectId));
                TrackAsset activation = Track(director, TestFixtureBuilder.TrackActivation);
                Assert.That(director.GetGenericBinding(activation).name, Is.EqualTo("SceneOnlyTarget"));

                AnimationMode.StartAnimationMode();
                RestorePreview preview = BindingRestoreService.CreatePreview(BindingRestoreService.CreateRemoveOverrideTarget(track.Levels[0], track.Track), true);
                RestoreResult result = BindingRestoreService.Execute(preview, RestorePlanner.GetRecommended(preview.Plan));
                AnimationMode.StopAnimationMode();

                Assert.That(result.Success, Is.True, result.Message + " " + string.Join(" / ", preview.Plan.Refusals));
                Assert.That(director.GetGenericBinding(activation).name, Is.EqualTo("ActTarget"));
                Assert.That(scene.isDirty, Is.True);

                Undo.PerformUndo();
                Assert.That(director.GetGenericBinding(activation).name, Is.EqualTo("SceneOnlyTarget"));
                Undo.PerformRedo();
                Assert.That(director.GetGenericBinding(activation).name, Is.EqualTo("ActTarget"));
            }
            finally
            {
                if (AnimationMode.InAnimationMode())
                {
                    AnimationMode.StopAnimationMode();
                }

                // 変更を保存せずに閉じる
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }

            Assert.That(File.ReadAllBytes(scenePath), Is.EqualTo(sceneBytes));
        }

        [Test]
        public void StalePreview_IsNotExecuted()
        {
            GuardianHistory history = CreateHistory();
            byte[] original = File.ReadAllBytes(_middlePath);
            MutateMiddle(0, TestFixtureBuilder.TrackActivation, director => director.SetGenericBinding(Track(director, TestFixtureBuilder.TrackActivation), null));
            ChangeEvent lost = RecordMiddle(history);
            RestorePreview preview = BindingRestoreService.CreatePreview(lost);

            // プレビュー後に別の編集が入った
            MutateMiddle(0, TestFixtureBuilder.TrackActivation, director =>
            {
                director.SetGenericBinding(Track(director, TestFixtureBuilder.TrackActivation), director.transform.parent.Find("Speaker").gameObject);
            });
            RestoreResult result = BindingRestoreService.Execute(preview, RestorePlanner.GetRecommended(preview.Plan));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("changed since the preview"));

            // 素材を初期状態に戻す
            RestorePreview fresh = BindingRestoreService.CreatePreview(lost);
            Assert.That(BindingRestoreService.Execute(fresh, RestorePlanner.GetRecommended(fresh.Plan)).Success, Is.True);
            Assert.That(File.ReadAllBytes(_middlePath), Is.EqualTo(original));
        }

        [Test]
        public void SceneOverride_RestoreUsesUndoAndMarksDirty_WithoutSaving()
        {
            string scenePath = TestFixtureBuilder.PathIn(TestFixtureBuilder.MutableFolderName, "SceneA.unity");
            byte[] sceneBytes = File.ReadAllBytes(scenePath);
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            try
            {
                ScanResult scan = new SceneBindingScanner().Scan(scene);
                DirectorObservation observed = scan.Directors.Single(d => d.Director.IndexPathHint == "Parent#1/MiddleP#0/Child#0/Director#0");
                TrackBindingObservation track = observed.Tracks.Single(t => t.Track.TrackPathHint == TestFixtureBuilder.TrackActivation);
                LevelValue before = SnapshotConverter.CreateValue(track.Effective);
                Assert.That(before.TargetHint, Is.EqualTo("SceneOnlyTarget"));

                // 未保存の変更として NULL にしたことを、Scene 階層のイベントとして表す
                PlayableDirector director = (PlayableDirector)GlobalObjectId.GlobalObjectIdentifierToObjectSlow(ParseId(observed.Director.GlobalObjectId));
                director.SetGenericBinding(Track(director, TestFixtureBuilder.TrackActivation), null);
                PrefabUtility.RecordPrefabInstancePropertyModifications(director);
                LevelValue after = new LevelValue(ValueState.Null, LocalState.ExplicitOverride, string.Empty, string.Empty, string.Empty);
                ChangeEvent lost = new ChangeEvent("scene-test", "obs", "2026-11-27T00:00:00Z", ChangeType.BindingLost, "key",
                    PrefabContextResolver.CreateSceneLocator(scene), observed.Director, track.Track, before, after, false, string.Empty);

                RestorePreview preview = BindingRestoreService.CreatePreview(lost);
                RestoreOption option = RestorePlanner.GetRecommended(preview.Plan);
                Assert.That(option.Operation, Is.EqualTo(RestoreOperation.SetValue), string.Join(" / ", preview.Plan.Refusals));
                RestoreResult result = BindingRestoreService.Execute(preview, option);

                Assert.That(result.Success, Is.True, result.Message);
                Assert.That(director.GetGenericBinding(Track(director, TestFixtureBuilder.TrackActivation)).name, Is.EqualTo("SceneOnlyTarget"));
                Assert.That(scene.isDirty, Is.True);
                Assert.That(result.CanRollback, Is.False);

                Undo.PerformUndo();
                Assert.That(director.GetGenericBinding(Track(director, TestFixtureBuilder.TrackActivation)), Is.Null);
            }
            finally
            {
                // 変更を保存せずに開き直す
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }

            Assert.That(File.ReadAllBytes(scenePath), Is.EqualTo(sceneBytes));
        }

        private GuardianHistory CreateHistory()
        {
            HistoryStore store = new HistoryStore(_historyDirectory, "test-project");
            store.Load();
            GuardianHistory history = new GuardianHistory(store);
            history.Record(new[] { new SceneBindingScanner().ScanPrefabAsset(_middlePath) }, "baseline");
            BindingRestoreService.History = history;
            return history;
        }

        private ChangeEvent RecordMiddle(GuardianHistory history)
        {
            RecordSummary summary = history.Record(new[] { new SceneBindingScanner().ScanPrefabAsset(_middlePath) }, "test");
            Assert.That(summary.Saved, Is.True, summary.Error);
            return summary.NewEvents.Single(e => e.Owner.AssetPathHint == _middlePath);
        }

        private void MutateMiddle(int childIndex, string trackName, Action<PlayableDirector> mutate)
        {
            using (PrefabUtility.EditPrefabContentsScope scope = new PrefabUtility.EditPrefabContentsScope(_middlePath))
            {
                PlayableDirector director = scope.prefabContentsRoot.transform.GetChild(childIndex).GetComponentInChildren<PlayableDirector>(true);
                mutate(director);
                PrefabUtility.RecordPrefabInstancePropertyModifications(director);
            }
        }

        /// <summary>
        /// 素材が初期状態か確認する（以前のテストが途中で失敗して変更が残っていれば、素材の削除を促す）。
        /// </summary>
        private void AssertBaseline()
        {
            ScanResult scan = new SceneBindingScanner().ScanPrefabAsset(_middlePath);
            ValueState child0 = Effective(scan, "Middle#0/Child#0/Director#0", TestFixtureBuilder.TrackActivation);
            ValueState child1 = Effective(scan, "Middle#0/Child#1/Director#0", TestFixtureBuilder.TrackActivation);
            bool isBaseline = child0 == ValueState.Bound && child1 == ValueState.Null;
            Assert.That(isBaseline, Is.True,
                $"The mutable fixture is not in its initial state. Delete {TestFixtureBuilder.PathIn(TestFixtureBuilder.MutableFolderName, string.Empty)} and run the tests again.");
        }

        private ValueState EffectiveInParent(string directorIndexPath, string trackName)
        {
            return Effective(new SceneBindingScanner().ScanPrefabAsset(_parentPath), directorIndexPath, trackName);
        }

        private static ValueState Effective(ScanResult scan, string directorIndexPath, string trackName)
        {
            return scan.Directors.Single(d => d.Director.IndexPathHint == directorIndexPath)
                .Tracks.Single(t => t.Track.TrackPathHint.Split('/').Last() == trackName).Effective.ValueState;
        }

        private static TrackAsset Track(PlayableDirector director, string trackName)
        {
            return TestFixtureBuilder.FindTrack(AssetDatabase.GetAssetPath(director.playableAsset), trackName);
        }

        private static SerializedProperty FindValue(SerializedObject serializedObject, TrackAsset track)
        {
            SerializedProperty array = serializedObject.FindProperty("m_SceneBindings");
            for (int i = 0; i < array.arraySize; i++)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(i);
                if (element.FindPropertyRelative("key").objectReferenceValue == track)
                {
                    return element.FindPropertyRelative("value");
                }
            }

            throw new InvalidOperationException("entry not found");
        }

        private static GlobalObjectId ParseId(string text)
        {
            GlobalObjectId.TryParse(text, out GlobalObjectId id);
            return id;
        }
    }
}
