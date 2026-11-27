using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 履歴の保存形式 v2（manifest ＋ gzip 区画）のテスト（仕様 8章、13章 T03, T15, T23）。
    /// ファイルはプロジェクトの Temp/ 配下（Unity 終了時に Unity が削除する）に作る。
    /// </summary>
    public sealed class HistoryStoreTests
    {
        private string _directory;
        private int _counter;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetFullPath("Temp"), "TBGHistoryTests", Guid.NewGuid().ToString("N"));
        }

        [Test]
        public void MissingFolder_StartsEmptyAndWritable()
        {
            HistoryStore store = CreateStore();

            Assert.That(store.Status, Is.EqualTo(HistoryStoreStatus.Ready));
            Assert.That(store.IsWritable, Is.True);
            Assert.That(store.State.Events, Is.Empty);
        }

        [Test]
        public void Commit_ThenReload_RestoresState()
        {
            HistoryStore store = CreateStore();
            Record(store, "sceneA", "target");

            HistoryStore reloaded = CreateStore();

            Assert.That(reloaded.Status, Is.EqualTo(HistoryStoreStatus.Ready));
            Assert.That(reloaded.State.Events.Count, Is.EqualTo(1));
            Assert.That(reloaded.State.Events[0].EventId, Is.EqualTo(store.State.Events[0].EventId));
            Assert.That(reloaded.State.Scopes.Single().ScopeGuid, Is.EqualTo("sceneA"));
            Assert.That(reloaded.EnsureAllLoaded(), Is.True);
            Assert.That(reloaded.State.Contexts.Count, Is.EqualTo(1));
            Assert.That(reloaded.State.OwnerLevels.Count, Is.EqualTo(1));
            Assert.That(File.Exists(Path.Combine(_directory, "contexts", "sceneA.json.gz")), Is.True);
        }

        [Test]
        public void Load_ReadsOnlyEventsAndNotifications_ContextsAndOwnersOnDemand()
        {
            HistoryStore store = CreateStore();
            Record(store, "sceneA", "target");
            Record(store, "sceneB", "target");

            HistoryStore reloaded = CreateStore();

            Assert.That(reloaded.State.Events.Count, Is.EqualTo(2));
            Assert.That(reloaded.State.Contexts, Is.Empty);
            Assert.That(reloaded.State.OwnerLevels, Is.Empty);

            Assert.That(reloaded.EnsureScopeLoaded("sceneA"), Is.True);
            Assert.That(reloaded.State.Contexts.Select(c => c.ScopeGuid), Is.EqualTo(new[] { "sceneA" }));
            Assert.That(reloaded.IsPartitionLoaded("contexts", "sceneB"), Is.False);

            // 読み込んだ Scope だけを記録し直しても、読み込んでいない Scope の区画は書き直さない
            long sceneBGeneration = ReadPartitionGeneration("contexts", "sceneB");
            Record(reloaded, "sceneA", "otherTarget");
            Assert.That(ReadPartitionGeneration("contexts", "sceneB"), Is.EqualTo(sceneBGeneration));
        }

        [Test]
        public void RewritingUnloadedPartition_IsRejected()
        {
            HistoryStore store = CreateStore();
            Record(store, "sceneA", "target");
            HistoryStore reloaded = CreateStore();
            HistoryChangeSet changes = new HistoryChangeSet();
            changes.ScopeGuids.Add("sceneA");

            bool committed = reloaded.TryCommit(reloaded.State, changes, out string error);

            Assert.That(committed, Is.False);
            Assert.That(error, Does.Contain("must be loaded"));
            Assert.That(CreateStore().EnsureAllLoaded(), Is.True);
        }

        [Test]
        public void Notifications_ArePersistedSeparately()
        {
            HistoryStore store = CreateStore();
            Record(store, "sceneA", "target");
            DiffResult lost = RecordValue(store, "sceneA", new LevelValue(ValueState.Null, LocalState.DefinedHere, string.Empty, string.Empty, string.Empty));
            List<NotificationRecord> created = NotificationRules.CreateForNewEvents(store.State, lost.NewEvents, "2026-11-27T00:00:00Z");
            Assert.That(store.TryCommit(store.State, new HistoryChangeSet { NotificationsChanged = true }, out string error), Is.True, error);

            HistoryStore reloaded = CreateStore();
            Assert.That(NotificationRules.GetUnread(reloaded.State).Select(n => n.EventId), Is.EqualTo(created.Select(n => n.EventId)));

            NotificationRules.Dismiss(reloaded.State, created.Select(n => n.EventId), "2026-11-27T00:00:01Z");
            reloaded.TryCommit(reloaded.State, new HistoryChangeSet { NotificationsChanged = true }, out error);

            Assert.That(NotificationRules.GetUnread(CreateStore().State), Is.Empty);
            Assert.That(File.Exists(Path.Combine(_directory, "notifications", "all.json.gz")), Is.True);
        }

        [Test]
        public void RecordingAnotherScene_DoesNotRewriteFirstSceneContexts()
        {
            HistoryStore store = CreateStore();
            Record(store, "sceneA", "target");
            long before = ReadPartitionGeneration("contexts", "sceneA");

            Record(store, "sceneB", "target");

            Assert.That(ReadPartitionGeneration("contexts", "sceneA"), Is.EqualTo(before));
            Assert.That(ReadPartitionGeneration("contexts", "sceneB"), Is.EqualTo(store.Generation));
        }

        [Test]
        public void UnchangedOwner_IsNotRewritten()
        {
            HistoryStore store = CreateStore();
            Record(store, "sceneA", "target");
            long ownerBefore = ReadPartitionGeneration("owners", "owner-sceneA");

            DiffResult result = Record(store, "sceneA", "target");

            Assert.That(result.ChangedOwnerGuids, Is.Empty);
            Assert.That(ReadPartitionGeneration("owners", "owner-sceneA"), Is.EqualTo(ownerBefore));
        }

        [Test]
        public void ChangedOwner_IsRewritten()
        {
            HistoryStore store = CreateStore();
            Record(store, "sceneA", "target");
            long ownerBefore = ReadPartitionGeneration("owners", "owner-sceneA");

            Record(store, "sceneA", "otherTarget");

            Assert.That(ReadPartitionGeneration("owners", "owner-sceneA"), Is.GreaterThan(ownerBefore));
            Assert.That(CreateStore().State.Events.Last().ChangeType, Is.EqualTo(ChangeType.TargetChanged));
        }

        [Test]
        public void T15_InterruptedWrite_UsesBackupAndKeepsValidGeneration()
        {
            HistoryStore store = CreateStore();
            Record(store, "sceneA", "target");
            Record(store, "sceneA", "otherTarget");

            // 区画は書けたが manifest の置換前に落ちた状態を再現（manifest だけ 1 世代前に戻す）
            File.Copy(Path.Combine(_directory, HistoryStore.ManifestFileName + ".bak"), Path.Combine(_directory, HistoryStore.ManifestFileName), true);

            HistoryStore recovered = CreateStore();
            Assert.That(recovered.Status, Is.EqualTo(HistoryStoreStatus.Ready), recovered.ErrorMessage);
            Assert.That(recovered.RecoveredPartitionCount, Is.GreaterThan(0));
            Assert.That(recovered.State.Events.Count, Is.EqualTo(1));

            // 続けて記録し、さらにもう一度同じ中断が起きても有効な世代が失われない
            Record(recovered, "sceneA", "thirdTarget");
            File.Copy(Path.Combine(_directory, HistoryStore.ManifestFileName + ".bak"), Path.Combine(_directory, HistoryStore.ManifestFileName), true);
            HistoryStore again = CreateStore();
            Assert.That(again.Status, Is.EqualTo(HistoryStoreStatus.Ready), again.ErrorMessage);
            Assert.That(again.State.Events.Count, Is.EqualTo(1));
        }

        [Test]
        public void CorruptedManifest_IsReadOnlyAndNotOverwritten()
        {
            Directory.CreateDirectory(_directory);
            string path = Path.Combine(_directory, HistoryStore.ManifestFileName);
            File.WriteAllText(path, "{ this is not json");

            HistoryStore store = CreateStore();
            bool committed = store.TryCommit(new HistoryState("project"), HistoryChangeSet.All, out string error);

            Assert.That(store.Status, Is.EqualTo(HistoryStoreStatus.Corrupted));
            Assert.That(committed, Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(File.ReadAllText(path), Is.EqualTo("{ this is not json"));
        }

        [Test]
        public void CorruptedPartition_IsReadOnly()
        {
            HistoryStore store = CreateStore();
            Record(store, "sceneA", "target");
            string partition = Path.Combine(_directory, "contexts", "sceneA.json.gz");
            File.WriteAllText(partition, "broken");
            File.WriteAllText(partition + ".bak", "broken");

            // 文脈の区画は遅延読み込みなので、読もうとした時点で検出し、読み取り専用になる
            HistoryStore reloaded = CreateStore();
            Assert.That(reloaded.Status, Is.EqualTo(HistoryStoreStatus.Ready));

            bool loaded = reloaded.EnsureScopeLoaded("sceneA");

            Assert.That(loaded, Is.False);
            Assert.That(reloaded.Status, Is.EqualTo(HistoryStoreStatus.Corrupted));
            Assert.That(reloaded.IsWritable, Is.False);
            Assert.That(reloaded.ErrorMessage, Does.Contain("contexts/sceneA"));
            Assert.That(File.ReadAllText(partition), Is.EqualTo("broken"));
        }

        [Test]
        public void UnsupportedSchema_IsReadOnly()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, HistoryStore.ManifestFileName),
                "{\"schemaVersion\":999,\"projectGuid\":\"project\",\"generation\":1,\"scopes\":[],\"partitions\":[]}");

            HistoryStore store = CreateStore();

            Assert.That(store.Status, Is.EqualTo(HistoryStoreStatus.UnsupportedSchema));
            Assert.That(store.IsWritable, Is.False);
        }

        [Test]
        public void T23_HistoryFromAnotherProject_IsReadOnly()
        {
            HistoryStore original = CreateStore("projectA");
            Record(original, "sceneA", "target");

            HistoryStore other = CreateStore("projectB");

            Assert.That(other.Status, Is.EqualTo(HistoryStoreStatus.ProjectMismatch));
            Assert.That(other.IsWritable, Is.False);
        }

        [Test]
        public void LegacyStateJson_IsMigratedAndKept()
        {
            HistoryState legacy = new HistoryState("project");
            BindingDiffEngine.Apply(legacy, Observation("sceneA", new LevelValue(ValueState.Bound, LocalState.DefinedHere, "target", "target", "Animator")));
            string json = JsonUtility.ToJson(legacy).Replace("\"schemaVersion\":2", "\"schemaVersion\":1");
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, HistoryStore.LegacyFileName), json);

            HistoryStore store = CreateStore();

            Assert.That(store.Status, Is.EqualTo(HistoryStoreStatus.Ready), store.ErrorMessage);
            Assert.That(store.State.Events.Single().EventId, Is.EqualTo(legacy.Events.Single().EventId));
            Assert.That(File.Exists(Path.Combine(_directory, HistoryStore.ManifestFileName)), Is.True);
            Assert.That(File.Exists(Path.Combine(_directory, HistoryStore.LegacyFileName)), Is.False);
            Assert.That(File.Exists(Path.Combine(_directory, HistoryStore.MigratedLegacyFileName)), Is.True);
            HistoryStore again = CreateStore();
            Assert.That(again.EnsureAllLoaded(), Is.True);
            Assert.That(again.State.Contexts.Count, Is.EqualTo(1));
        }

        [Test]
        public void T03_ReloadedState_ProducesNoNewEventsForSameObservation()
        {
            HistoryStore store = CreateStore();
            Record(store, "sceneA", "target");

            DiffResult result = Record(CreateStore(), "sceneA", "target");

            Assert.That(result.NewEvents, Is.Empty);
        }

        [Test]
        public void LoadedStrings_AreShared()
        {
            HistoryStore store = CreateStore();
            Record(store, "sceneA", "target");

            HistoryStore reloaded = CreateStore();
            reloaded.EnsureAllLoaded();
            HistoryState state = reloaded.State;

            // 別々の区画から読んだ同じ内容の文字列が、同じインスタンスにまとめられている
            ContextSnapshot context = state.Contexts.Single();
            Assert.That(ReferenceEquals(context.Levels[0].OwnerLevelKey, state.OwnerLevels.Single().OwnerLevelKey), Is.True);
        }

        /// <summary>
        /// 規模の確認（合否は付けず実測値を出力）。10,000 バインディングの Scene を記録した後、
        /// 小さな Scene の記録で書き出す量が全体に比例しないことを確認する。
        /// </summary>
        [Test]
        public void Scale_LargeSceneThenSmallScene_WritesOnlyChangedPartitions()
        {
            HistoryStore store = CreateStore();
            Stopwatch watch = Stopwatch.StartNew();
            HistoryState state = store.State;
            DiffResult big = BindingDiffEngine.Apply(state, LargeObservation("bigScene", 10000, 50));
            HistoryChangeSet bigChanges = new HistoryChangeSet();
            bigChanges.Add(big);
            Assert.That(store.TryCommit(state, bigChanges, out string error), Is.True, error);
            long bigMs = watch.ElapsedMilliseconds;
            long bigBytes = store.LastCommitBytes;

            watch.Restart();
            DiffResult small = BindingDiffEngine.Apply(state, LargeObservation("smallScene", 10, 2));
            HistoryChangeSet smallChanges = new HistoryChangeSet();
            smallChanges.Add(small);
            Assert.That(store.TryCommit(state, smallChanges, out error), Is.True, error);
            long smallMs = watch.ElapsedMilliseconds;
            long smallBytes = store.LastCommitBytes;

            // 起動時（遅延読み込み: イベントと通知だけ）
            watch.Restart();
            HistoryStore reloaded = CreateStore();
            long startupLoadMs = watch.ElapsedMilliseconds;

            // 再起動後に大きな Scene を記録し直す（自動検査で起きる処理: 必要区画の読み込み＋差分＋保存）
            watch.Restart();
            ScopeObservation again = LargeObservation("bigScene", 10000, 50);
            reloaded.EnsureScopeLoaded("bigScene");
            reloaded.EnsureOwnersLoaded(again.Contexts.SelectMany(c => c.Levels).Select(l => l.Owner.AssetGuid));
            DiffResult rerecord = BindingDiffEngine.Apply(reloaded.State, again);
            HistoryChangeSet rerecordChanges = new HistoryChangeSet();
            rerecordChanges.Add(rerecord);
            Assert.That(reloaded.TryCommit(reloaded.State, rerecordChanges, out error), Is.True, error);
            long rerecordMs = watch.ElapsedMilliseconds;

            watch.Restart();
            HistoryStore full = CreateStore();
            full.EnsureAllLoaded();
            long fullLoadMs = watch.ElapsedMilliseconds;

            string report = $"[TBG scale] 10,000 bindings: first record+save {bigMs} ms, wrote {bigBytes / 1024.0:0.0} KB | " +
                $"next small scene {smallMs} ms, wrote {smallBytes / 1024.0:0.0} KB | startup load (lazy) {startupLoadMs} ms | " +
                $"re-record big scene after restart {rerecordMs} ms ({rerecord.NewEvents.Count} new events) | load everything {fullLoadMs} ms | " +
                $"total on disk {store.GetTotalSizeBytes() / 1024.0:0.0} KB";
            UnityEngine.Debug.Log(report);
            TestContext.WriteLine(report);

            Assert.That(full.State.Contexts.Count, Is.EqualTo(10010));
            Assert.That(rerecord.NewEvents, Is.Empty);
            Assert.That(smallBytes, Is.LessThan(bigBytes / 10));
        }

        private HistoryStore CreateStore(string project = "project")
        {
            HistoryStore store = new HistoryStore(_directory, project);
            store.Load();
            return store;
        }

        private DiffResult Record(HistoryStore store, string scope, string target)
        {
            return RecordValue(store, scope, new LevelValue(ValueState.Bound, LocalState.DefinedHere, target, target, "Animator"));
        }

        /// <summary>
        /// 製品の GuardianHistory と同じく、記録の前にその Scope と Owner の区画を読み込んでから差分を取り保存する。
        /// </summary>
        private DiffResult RecordValue(HistoryStore store, string scope, LevelValue value)
        {
            Assert.That(store.EnsureScopeLoaded(scope) && store.EnsureOwnersLoaded(new[] { $"owner-{scope}" }), Is.True, store.ErrorMessage);
            HistoryState state = store.State;
            DiffResult result = BindingDiffEngine.Apply(state, Observation(scope, value));
            HistoryChangeSet changes = new HistoryChangeSet();
            changes.Add(result);
            Assert.That(store.TryCommit(state, changes, out string error), Is.True, error);
            return result;
        }

        private long ReadPartitionGeneration(string kind, string id)
        {
            string path = Path.Combine(_directory, kind, id + ".json.gz");
            using (FileStream file = File.OpenRead(path))
            using (GZipStream gzip = new GZipStream(file, CompressionMode.Decompress))
            using (StreamReader reader = new StreamReader(gzip, Encoding.UTF8))
            {
                Match match = Regex.Match(reader.ReadToEnd(), "\"generation\":(\\d+)");
                return long.Parse(match.Groups[1].Value);
            }
        }

        private ScopeObservation Observation(string scope, LevelValue bound)
        {
            _counter++;
            LevelSnapshot level = new LevelSnapshot($"owner-{scope}|director|timeline|track", new AssetLocator($"owner-{scope}", $"Assets/{scope}.unity", AssetKind.Scene),
                new DirectorLocator("director", "Director", "Director#0", 0), bound, ContributionKind.Defined);
            ContextSnapshot context = new ContextSnapshot($"key-{scope}", scope, $"Assets/{scope}.unity", new DirectorLocator("director", "Director", "Director#0", 0),
                new TrackLocator("timeline", "Assets/T.playable", "1", "AnimationTrack", "AnimA", "Animator"), bound, 0, false,
                new List<LevelSnapshot> { level });
            return new ScopeObservation($"obs{_counter}", $"2026-11-27T00:00:{_counter:00}Z", "test", scope, $"Assets/{scope}.unity", new List<ContextSnapshot> { context });
        }

        /// <summary>
        /// Scene(L0, 文脈ごとに固有) → Parent → Middle → Child の 4 階層。Prefab 側は prefabCount 種類を共有する。
        /// </summary>
        private ScopeObservation LargeObservation(string scope, int contextCount, int prefabCount)
        {
            _counter++;
            List<ContextSnapshot> contexts = new List<ContextSnapshot>(contextCount);
            for (int i = 0; i < contextCount; i++)
            {
                int prefab = i % prefabCount;
                string director = $"GlobalObjectId_V1-2-{scope}-{i * 7919L}-{i}";
                string track = (i % 6).ToString();
                LevelValue bound = new LevelValue(ValueState.Bound, LocalState.Inherited, $"GlobalObjectId_V1-1-target-{prefab}-{track}-0", $"Parent/Middle/Child/Actor{track}", "Animator");
                List<LevelSnapshot> levels = new List<LevelSnapshot>
                {
                    Level($"{scope}|{director}|timeline|{track}", scope, $"Assets/{scope}.unity", director, bound, ContributionKind.Inherited),
                    Level($"parent{prefab}|parentDir{i % 4}|timeline|{track}", $"parent{prefab}", $"Assets/Parent{prefab}.prefab", $"parentDir{i % 4}", bound, ContributionKind.Inherited),
                    Level($"middle{prefab}|middleDir{i % 2}|timeline|{track}", $"middle{prefab}", $"Assets/Middle{prefab}.prefab", $"middleDir{i % 2}", bound, ContributionKind.Inherited),
                    Level($"child{prefab}|childDir|timeline|{track}", $"child{prefab}", $"Assets/Child{prefab}.prefab", "childDir", bound, ContributionKind.Defined),
                };
                DirectorLocator directorLocator = new DirectorLocator(director, "Parent/Middle/Child/Director", $"Parent#{i}/Middle#0/Child#0/Director#0", 0);
                TrackLocator trackLocator = new TrackLocator("timeline", "Assets/Timeline.playable", track, "AnimationTrack", $"Track{track}", "Animator");
                contexts.Add(new ContextSnapshot($"{director}|timeline|{track}", scope, $"Assets/{scope}.unity", directorLocator, trackLocator, bound, 3, false, levels));
            }

            return new ScopeObservation($"obs{_counter}", $"2026-11-27T01:00:{_counter:00}Z", "test", scope, $"Assets/{scope}.unity", contexts);
        }

        private static LevelSnapshot Level(string key, string ownerGuid, string ownerPath, string director, LevelValue value, ContributionKind contribution)
        {
            return new LevelSnapshot(key, new AssetLocator(ownerGuid, ownerPath, AssetKind.Prefab), new DirectorLocator(director, "Director", "Director#0", 0), value, contribution);
        }
    }
}
