using System;
using System.IO;
using System.Collections.Generic;
using NUnit.Framework;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 履歴ファイルの読み書きのテスト（仕様 8章、13章 T03, T15, T23）。
    /// ファイルはプロジェクトの Temp/ 配下（Unity 終了時に Unity が削除する）に作る。
    /// </summary>
    public sealed class HistoryStoreTests
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetFullPath("Temp"), "TBGHistoryTests", Guid.NewGuid().ToString("N"));
        }

        [Test]
        public void MissingFile_StartsEmptyAndWritable()
        {
            HistoryStore store = new HistoryStore(_directory, "project");

            store.Load();

            Assert.That(store.Status, Is.EqualTo(HistoryStoreStatus.Ready));
            Assert.That(store.IsWritable, Is.True);
            Assert.That(store.State.Events, Is.Empty);
        }

        [Test]
        public void Commit_ThenReload_RestoresState()
        {
            HistoryStore store = new HistoryStore(_directory, "project");
            store.Load();
            HistoryState next = store.State.Clone();
            BindingDiffEngine.Apply(next, Observation("obs1"));

            Assert.That(store.TryCommit(next, out string error), Is.True, error);

            HistoryStore reloaded = new HistoryStore(_directory, "project");
            reloaded.Load();
            Assert.That(reloaded.Status, Is.EqualTo(HistoryStoreStatus.Ready));
            Assert.That(reloaded.State.Events.Count, Is.EqualTo(1));
            Assert.That(reloaded.State.Events[0].EventId, Is.EqualTo(next.Events[0].EventId));
            Assert.That(reloaded.State.Scopes.Count, Is.EqualTo(1));
        }

        [Test]
        public void SecondCommit_KeepsPreviousGenerationAsBackup()
        {
            HistoryStore store = new HistoryStore(_directory, "project");
            store.Load();
            HistoryState first = store.State.Clone();
            BindingDiffEngine.Apply(first, Observation("obs1"));
            store.TryCommit(first, out string _);
            string firstJson = File.ReadAllText(store.FilePath);

            HistoryState second = store.State.Clone();
            BindingDiffEngine.Apply(second, Observation("obs2"));
            store.TryCommit(second, out string _);

            Assert.That(File.Exists(store.BackupPath), Is.True);
            Assert.That(File.ReadAllText(store.BackupPath), Is.EqualTo(firstJson));
        }

        [Test]
        public void CorruptedFile_IsReadOnlyAndNotOverwritten()
        {
            Directory.CreateDirectory(_directory);
            string path = Path.Combine(_directory, HistoryStore.FileName);
            File.WriteAllText(path, "{ this is not json");
            HistoryStore store = new HistoryStore(_directory, "project");

            store.Load();
            bool committed = store.TryCommit(new HistoryState("project"), out string error);

            Assert.That(store.Status, Is.EqualTo(HistoryStoreStatus.Corrupted));
            Assert.That(store.IsWritable, Is.False);
            Assert.That(committed, Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(File.ReadAllText(path), Is.EqualTo("{ this is not json"));
        }

        [Test]
        public void UnsupportedSchema_IsReadOnly()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, HistoryStore.FileName), "{\"schemaVersion\":999,\"projectGuid\":\"project\"}");
            HistoryStore store = new HistoryStore(_directory, "project");

            store.Load();

            Assert.That(store.Status, Is.EqualTo(HistoryStoreStatus.UnsupportedSchema));
            Assert.That(store.IsWritable, Is.False);
        }

        [Test]
        public void T23_FileFromAnotherProject_IsReadOnly()
        {
            HistoryStore original = new HistoryStore(_directory, "projectA");
            original.Load();
            original.TryCommit(original.State.Clone(), out string _);

            HistoryStore other = new HistoryStore(_directory, "projectB");
            other.Load();

            Assert.That(other.Status, Is.EqualTo(HistoryStoreStatus.ProjectMismatch));
            Assert.That(other.IsWritable, Is.False);
        }

        [Test]
        public void T03_ReloadedState_ProducesNoNewEventsForSameObservation()
        {
            HistoryStore store = new HistoryStore(_directory, "project");
            store.Load();
            HistoryState first = store.State.Clone();
            BindingDiffEngine.Apply(first, Observation("obs1"));
            store.TryCommit(first, out string _);

            HistoryStore restarted = new HistoryStore(_directory, "project");
            restarted.Load();
            HistoryState next = restarted.State.Clone();
            DiffResult result = BindingDiffEngine.Apply(next, Observation("obs2"));

            Assert.That(result.NewEvents, Is.Empty);
        }

        private static ScopeObservation Observation(string id)
        {
            LevelValue bound = new LevelValue(ValueState.Bound, LocalState.DefinedHere, "target", "Target", "Animator");
            LevelSnapshot level = new LevelSnapshot("owner|director|timeline|track", new AssetLocator("scene", "Assets/S.unity", AssetKind.Scene),
                new DirectorLocator("director", "Director", "Director#0", 0), bound, ContributionKind.Defined);
            ContextSnapshot context = new ContextSnapshot("key", "scene", "Assets/S.unity", new DirectorLocator("director", "Director", "Director#0", 0),
                new TrackLocator("timeline", "Assets/T.playable", "1", "AnimationTrack", "AnimA", "Animator"), bound, 0, false,
                new List<LevelSnapshot> { level });
            return new ScopeObservation(id, "2026-01-01T00:00:00Z", "test", "scene", "Assets/S.unity", new List<ContextSnapshot> { context });
        }
    }
}
