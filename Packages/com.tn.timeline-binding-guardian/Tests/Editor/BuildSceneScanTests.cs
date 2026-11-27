using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// Build Scene の一括検査（Phase 5）。閉じた Scene を一時的に開いて記録し、終了後に Scene 構成・Selection を元に戻す。
    /// 素材は書き換えない（Assets/TBGTestFixtures/Mutable/ の SceneA / SceneB を開くだけ）。
    /// </summary>
    public sealed class BuildSceneScanTests
    {
        private string _sceneA;
        private string _sceneB;
        private string _historyDirectory;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            TestFixtureBuilder.BuildMutable();
            _sceneA = TestFixtureBuilder.PathIn(TestFixtureBuilder.MutableFolderName, "SceneA.unity");
            _sceneB = TestFixtureBuilder.PathIn(TestFixtureBuilder.MutableFolderName, "SceneB.unity");
        }

        [SetUp]
        public void SetUp()
        {
            _historyDirectory = Path.Combine(Path.GetFullPath("Temp"), "TBGBuildScanTests", Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test]
        public void ClosedScene_IsOpenedRecordedAndClosed_AndEditorStateIsRestored()
        {
            Scene sceneA = EditorSceneManager.OpenScene(_sceneA, OpenSceneMode.Single);
            GameObject selected = sceneA.GetRootGameObjects()[0];
            Selection.activeGameObject = selected;
            bool suspendedBefore = BindingChangeMonitor.IsSuspended;
            GuardianHistory history = CreateHistory();

            BuildSceneScanResult result = BuildSceneScan.Run(new[] { _sceneA, _sceneB }, history, path => false, NeverCancel);

            Assert.That(result.BlockingReason, Is.Empty);
            Assert.That(result.ScannedSceneCount, Is.EqualTo(2));
            Assert.That(result.OpenedSceneCount, Is.EqualTo(1));
            Assert.That(result.Errors, Is.Empty);
            Assert.That(ScopeGuids(history), Is.SupersetOf(new[] { AssetDatabase.AssetPathToGUID(_sceneA), AssetDatabase.AssetPathToGUID(_sceneB) }));

            Assert.That(SceneManager.sceneCount, Is.EqualTo(1));
            Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(_sceneA));
            Assert.That(sceneA.isDirty, Is.False);
            Assert.That(Selection.activeGameObject, Is.EqualTo(selected));
            Assert.That(BindingChangeMonitor.IsSuspended, Is.EqualTo(suspendedBefore));
        }

        [Test]
        public void UnsavedScene_BlocksTheScan()
        {
            Scene sceneA = EditorSceneManager.OpenScene(_sceneA, OpenSceneMode.Single);
            EditorSceneManager.MarkSceneDirty(sceneA);
            GuardianHistory history = CreateHistory();

            BuildSceneScanResult result = BuildSceneScan.Run(new[] { _sceneA, _sceneB }, history, path => false, NeverCancel);

            Assert.That(result.BlockingReason, Does.Contain(_sceneA));
            Assert.That(result.ScannedSceneCount, Is.EqualTo(0));
            Assert.That(history.Store.State.Scopes, Is.Empty);
            Assert.That(SceneManager.sceneCount, Is.EqualTo(1));
        }

        [Test]
        public void Cancel_KeepsCompletedScenes_AndRestoresSetup()
        {
            EditorSceneManager.OpenScene(_sceneA, OpenSceneMode.Single);
            GuardianHistory history = CreateHistory();

            // 2 件目の前でキャンセルする
            BuildSceneScanResult result = BuildSceneScan.Run(new[] { _sceneB, _sceneA }, history, path => false, (index, total, path) => index >= 1);

            Assert.That(result.Cancelled, Is.True);
            Assert.That(result.ScannedSceneCount, Is.EqualTo(1));
            Assert.That(ScopeGuids(history), Does.Contain(AssetDatabase.AssetPathToGUID(_sceneB)));
            Assert.That(SceneManager.sceneCount, Is.EqualTo(1));
            Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(_sceneA));
        }

        [Test]
        public void ExcludedScene_IsNotOpened()
        {
            EditorSceneManager.OpenScene(_sceneA, OpenSceneMode.Single);
            GuardianHistory history = CreateHistory();

            BuildSceneScanResult result = BuildSceneScan.Run(new[] { _sceneB }, history, path => path == _sceneB, NeverCancel);

            Assert.That(result.OpenedSceneCount, Is.EqualTo(0));
            Assert.That(result.SkippedReasons, Has.Some.Contains(_sceneB));
            Assert.That(history.Store.State.Scopes, Is.Empty);
        }

        private GuardianHistory CreateHistory()
        {
            HistoryStore store = new HistoryStore(_historyDirectory, "test-project");
            store.Load();
            return new GuardianHistory(store);
        }

        private static bool NeverCancel(int index, int total, string scenePath)
        {
            return false;
        }

        private static string[] ScopeGuids(GuardianHistory history)
        {
            return history.Store.State.Scopes.Select(s => s.ScopeGuid).ToArray();
        }
    }
}
