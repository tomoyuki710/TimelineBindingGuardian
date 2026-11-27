using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// ビルド前検査（仕様 11章、T22）。実際のビルドはせず、検査処理を直接呼ぶ。
    /// 書き換え専用の素材 Assets/TBGTestFixtures/Mutable/ を使い、変更した Prefab はファイルの内容を書き戻して元に戻す。
    /// </summary>
    public sealed class PreBuildCheckTests
    {
        private string _middlePath;
        private string _scenePath;
        private string _historyDirectory;
        private byte[] _middleOriginal;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            TestFixtureBuilder.BuildMutable();
            _middlePath = TestFixtureBuilder.PathIn(TestFixtureBuilder.MutableFolderName, "Middle.prefab");
            _scenePath = TestFixtureBuilder.PathIn(TestFixtureBuilder.MutableFolderName, "SceneA.unity");
        }

        [SetUp]
        public void SetUp()
        {
            _historyDirectory = Path.Combine(Path.GetFullPath("Temp"), "TBGPreBuildTests", Guid.NewGuid().ToString("N"));
            _middleOriginal = File.ReadAllBytes(_middlePath);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [TearDown]
        public void TearDown()
        {
            if (!File.ReadAllBytes(_middlePath).SequenceEqual(_middleOriginal))
            {
                File.WriteAllBytes(_middlePath, _middleOriginal);
                AssetDatabase.ImportAsset(_middlePath, ImportAssetOptions.ForceUpdate);
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test]
        public void T22_ClosedScene_PrefabsAreCheckedWithoutOpeningTheScene()
        {
            GuardianHistory history = CreateHistory();
            Scene active = SceneManager.GetActiveScene();

            BuildCheckRecord baseline = PreBuildCheck.Run(new[] { _scenePath }, "test", history);
            SetMiddleActivationNull();
            BuildCheckRecord record = PreBuildCheck.Run(new[] { _scenePath }, "test", history);

            Assert.That(baseline.ObservationIds, Is.Not.Empty, "the prefabs used by the scene are recorded");
            Assert.That(record.NewNotificationCount, Is.EqualTo(1));
            ChangeEvent lost = history.Store.State.Events.Single(e => e.EventId == history.Store.State.Notifications.Single().EventId);
            Assert.That(lost.ChangeType, Is.EqualTo(ChangeType.BindingLost));
            Assert.That(lost.Owner.AssetPathHint, Is.EqualTo(_middlePath));
            Assert.That(record.SkippedReasons, Has.Some.Contains(_scenePath));

            // ユーザーの Scene 構成を変えていない
            Assert.That(SceneManager.sceneCount, Is.EqualTo(1));
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(active));
            Assert.That(SceneManager.GetSceneByPath(_scenePath).isLoaded, Is.False);
        }

        [Test]
        public void T22_DismissedNotification_IsNotNotifiedAgain()
        {
            GuardianHistory history = CreateHistory();
            PreBuildCheck.Run(new[] { _scenePath }, "test", history);
            SetMiddleActivationNull();
            PreBuildCheck.Run(new[] { _scenePath }, "test", history);
            Assert.That(history.Dismiss(history.Store.State.Notifications.Select(n => n.EventId).ToList(), out string error), Is.True, error);

            BuildCheckRecord again = PreBuildCheck.Run(new[] { _scenePath }, "test", history);

            Assert.That(again.NewNotificationCount, Is.EqualTo(0));
            Assert.That(history.GetNotifications(false), Is.Empty);
        }

        [Test]
        public void OpenScene_IsCheckedAndLeftUnchanged()
        {
            GuardianHistory history = CreateHistory();
            Scene scene = EditorSceneManager.OpenScene(_scenePath, OpenSceneMode.Single);

            BuildCheckRecord record = PreBuildCheck.Run(new[] { _scenePath }, "test", history);

            string sceneGuid = AssetDatabase.AssetPathToGUID(_scenePath);
            Assert.That(history.Store.State.Scopes.Any(s => s.ScopeGuid == sceneGuid), Is.True);
            Assert.That(record.SkippedReasons, Has.None.Contains(_scenePath));
            Assert.That(scene.isDirty, Is.False);
            Assert.That(SceneManager.sceneCount, Is.EqualTo(1));
        }

        [Test]
        public void BuildRecord_IsSaved()
        {
            GuardianHistory history = CreateHistory();

            BuildCheckRecord record = PreBuildCheck.Run(new[] { _scenePath }, "StandaloneWindows64", history);

            BuildCheckRecord saved = new HistoryStoreReader(_historyDirectory).LatestBuild;
            Assert.That(saved, Is.Not.Null);
            Assert.That(saved.CheckedAtUtc, Is.EqualTo(record.CheckedAtUtc));
            Assert.That(saved.Platform, Is.EqualTo("StandaloneWindows64"));
            Assert.That(saved.ScenePaths, Is.EqualTo(new[] { _scenePath }));
            Assert.That(saved.ObservationIds, Is.EqualTo(record.ObservationIds));
        }

        [Test]
        public void Failure_DoesNotThrow_AndOnlyWarns()
        {
            GuardianHistory history = CreateHistory();
            LogAssert.Expect(LogType.Warning, new Regex("pre-build check failed.*injected"));

            BuildCheckRecord record = null;
            Assert.DoesNotThrow(() => record = PreBuildCheck.RunSafely(() => throw new InvalidOperationException("injected"), "test", history));

            Assert.That(record, Is.Null);
        }

        private GuardianHistory CreateHistory()
        {
            HistoryStore store = new HistoryStore(_historyDirectory, "test-project");
            store.Load();
            return new GuardianHistory(store);
        }

        private void SetMiddleActivationNull()
        {
            using (PrefabUtility.EditPrefabContentsScope scope = new PrefabUtility.EditPrefabContentsScope(_middlePath))
            {
                PlayableDirector director = scope.prefabContentsRoot.transform.GetChild(0).GetComponentInChildren<PlayableDirector>(true);
                string timelinePath = AssetDatabase.GetAssetPath(director.playableAsset);
                director.SetGenericBinding(TestFixtureBuilder.FindTrack(timelinePath, TestFixtureBuilder.TrackActivation), null);
                PrefabUtility.RecordPrefabInstancePropertyModifications(director);
            }
        }

        /// <summary>
        /// 保存された履歴を読み直す（ビルド記録がファイルに保存されたことの確認用）。
        /// </summary>
        private sealed class HistoryStoreReader
        {
            public HistoryStoreReader(string directory)
            {
                HistoryStore store = new HistoryStore(directory, "test-project");
                store.Load();
                LatestBuild = store.State.LatestBuild;
            }

            public BuildCheckRecord LatestBuild { get; }
        }
    }
}
