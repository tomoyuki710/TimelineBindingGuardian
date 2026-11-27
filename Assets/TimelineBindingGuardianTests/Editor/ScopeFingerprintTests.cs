using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 変化が無ければ自動検査を省くための指紋（Phase 5）。
    /// 書き換え専用の素材 Assets/TBGTestFixtures/Mutable/ を使い、ファイルは必ず元の内容に書き戻す。
    /// </summary>
    public sealed class ScopeFingerprintTests
    {
        private string _scenePath;
        private string _middlePath;
        private string _historyDirectory;
        private byte[] _sceneOriginal;
        private byte[] _middleOriginal;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            TestFixtureBuilder.BuildMutable();
            _scenePath = TestFixtureBuilder.PathIn(TestFixtureBuilder.MutableFolderName, "SceneA.unity");
            _middlePath = TestFixtureBuilder.PathIn(TestFixtureBuilder.MutableFolderName, "Middle.prefab");
        }

        [SetUp]
        public void SetUp()
        {
            _historyDirectory = Path.Combine(Path.GetFullPath("Temp"), "TBGFingerprintTests", Guid.NewGuid().ToString("N"));
            _sceneOriginal = File.ReadAllBytes(_scenePath);
            _middleOriginal = File.ReadAllBytes(_middlePath);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [TearDown]
        public void TearDown()
        {
            RestoreIfChanged(_scenePath, _sceneOriginal);
            RestoreIfChanged(_middlePath, _middleOriginal);
        }

        [Test]
        public void SameContent_GivesSameFingerprint()
        {
            string first = ScopeFingerprint.Compute(_scenePath);
            string second = ScopeFingerprint.Compute(_scenePath);

            Assert.That(first, Is.Not.Empty);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(ScopeFingerprint.Compute(_middlePath), Is.Not.EqualTo(first));
            Assert.That(ScopeFingerprint.Compute("Assets/NoSuchFile.unity"), Is.Empty);
        }

        [Test]
        public void ChangeInSceneFile_ChangesFingerprint()
        {
            string before = ScopeFingerprint.Compute(_scenePath);

            File.AppendAllText(_scenePath, "\n");

            Assert.That(ScopeFingerprint.Compute(_scenePath), Is.Not.EqualTo(before));
        }

        [Test]
        public void ChangeInDependentPrefab_ChangesSceneFingerprint()
        {
            string sceneBefore = ScopeFingerprint.Compute(_scenePath);
            string middleBefore = ScopeFingerprint.Compute(_middlePath);
            Assert.That(AssetDatabase.GetDependencies(_scenePath, true), Does.Contain(_middlePath));

            File.AppendAllText(_middlePath, "\n");

            Assert.That(ScopeFingerprint.Compute(_middlePath), Is.Not.EqualTo(middleBefore));
            Assert.That(ScopeFingerprint.Compute(_scenePath), Is.Not.EqualTo(sceneBefore));
        }

        [Test]
        public void RecordedFingerprint_IsUnchangedUntilTheFileChanges()
        {
            GuardianHistory history = CreateHistory();
            ScanResult scan = new SceneBindingScanner().ScanPrefabAsset(_middlePath);
            Assert.That(scan.SourceFingerprint, Is.Not.Empty);
            Assert.That(history.IsUnchangedSinceLastRecord(_middlePath, scan.SourceFingerprint), Is.False, "never recorded");

            Assert.That(history.Record(new[] { scan }, "test").Saved, Is.True);
            Assert.That(history.IsUnchangedSinceLastRecord(_middlePath, ScopeFingerprint.Compute(_middlePath)), Is.True);
            Assert.That(history.IsUnchangedSinceLastRecord(_middlePath, string.Empty), Is.False);

            // 保存後に読み直しても指紋は残る（manifest に保存される）
            HistoryStore reloaded = new HistoryStore(_historyDirectory, "test-project");
            reloaded.Load();
            Assert.That(new GuardianHistory(reloaded).IsUnchangedSinceLastRecord(_middlePath, ScopeFingerprint.Compute(_middlePath)), Is.True);

            File.AppendAllText(_middlePath, "\n");
            Assert.That(history.IsUnchangedSinceLastRecord(_middlePath, ScopeFingerprint.Compute(_middlePath)), Is.False);
        }

        [Test]
        public void UnsavedScene_HasNoFingerprint()
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.OpenScene(_scenePath, OpenSceneMode.Single);
            Assert.That(new SceneBindingScanner().Scan(scene).SourceFingerprint, Is.Not.Empty);

            EditorSceneManager.MarkSceneDirty(scene);

            Assert.That(new SceneBindingScanner().Scan(scene).SourceFingerprint, Is.Empty);
        }

        [Test]
        public void PreBuildCheck_SkipsUnchangedScopes()
        {
            GuardianHistory history = CreateHistory();
            EditorSceneManager.OpenScene(_scenePath, OpenSceneMode.Single);

            BuildCheckRecord first = PreBuildCheck.Run(new[] { _scenePath }, "test", history);
            int observationsAfterFirst = history.Store.State.Observations.Count;
            BuildCheckRecord second = PreBuildCheck.Run(new[] { _scenePath }, "test", history);

            Assert.That(first.ObservationIds, Is.Not.Empty);
            Assert.That(second.ObservationIds, Is.Empty, "nothing changed, so nothing is scanned or recorded");
            Assert.That(history.Store.State.Observations.Count, Is.EqualTo(observationsAfterFirst));
        }

        private GuardianHistory CreateHistory()
        {
            HistoryStore store = new HistoryStore(_historyDirectory, "test-project");
            store.Load();
            return new GuardianHistory(store);
        }

        private static void RestoreIfChanged(string path, byte[] original)
        {
            if (File.ReadAllBytes(path).SequenceEqual(original))
            {
                return;
            }

            File.WriteAllBytes(path, original);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
    }
}
