using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 負荷測定（仕様 13章「負荷検証」、Phase 5）。通常のテスト実行では動かない（Explicit）。
    /// 合成素材: Unit.prefab（Director 1、Track N）を Group.prefab に 10 個入れ、Scene に Group を G 個置く。
    ///   Small: 100 Director × 10 Track = 1,000 Binding、Large: 500 Director × 20 Track = 10,000 Binding。
    /// 素材は Assets/TBGTestFixtures/Perf/（gitignore 済み、開発用プロジェクトでは監視から除外）に作り、再利用する。
    /// 結果は Temp/TBGPerf/report.txt に書く。
    /// </summary>
    [Explicit("負荷測定。必要なときに個別に実行する")]
    [Category("Performance")]
    public sealed class PerformanceMeasurement
    {
        private const string Folder = TestFixtureBuilder.Root + "/Perf";
        private const int UnitsPerGroup = 10;

        private readonly StringBuilder _report = new StringBuilder();

        [Test]
        public void Measure_Small_100Directors_1000Bindings()
        {
            Measure("Small", 10, 10);
        }

        [Test]
        public void Measure_Large_500Directors_10000Bindings()
        {
            Measure("Large", 20, 50);
        }

        private void Measure(string name, int trackCount, int groupCount)
        {
            string scenePath = EnsureFixture(name, trackCount, groupCount);
            string unitPath = $"{Folder}/{name}/Unit.prefab";
            string groupPath = $"{Folder}/{name}/Group.prefab";
            string historyDirectory = Path.Combine(Path.GetFullPath("Temp"), "TBGPerf", name + "-" + Guid.NewGuid().ToString("N"));
            _report.AppendLine($"== {name}: {groupCount * UnitsPerGroup} directors x {trackCount} tracks = {groupCount * UnitsPerGroup * trackCount} bindings ({DateTime.Now:yyyy-MM-dd HH:mm}) ==");

            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            GuardianHistory history = CreateHistory(historyDirectory);

            ScanResult scan = Time("Scene scan (open scene)", () => new SceneBindingScanner().Scan(scene));
            RecordSummary first = Time("Record scene, first time (empty history)", () => history.Record(new[] { scan }, "perf"));
            Note($"  written {first.WrittenBytes / 1024.0:0.0} KB in {first.WrittenFileCount} file(s)");
            Time("Scene save without changes (scan + record)", () => history.Record(new[] { new SceneBindingScanner().Scan(scene) }, "perf"));
            Time("Scene opened / saved with no change (fingerprint check only, scan skipped)", () => history.IsUnchangedSinceLastRecord(scenePath, ScopeFingerprint.Compute(scenePath)));

            // Prefab 保存時の処理（ScanScheduler は 1 フレーム 1 件ずつ処理するので、それぞれの時間も出す）
            Time("Prefab save: Unit.prefab (scan + record)", () => history.Record(new[] { new SceneBindingScanner().ScanPrefabAsset(unitPath) }, "perf"));
            Time("Prefab save: dependent Group.prefab (scan + record)", () => history.Record(new[] { new SceneBindingScanner().ScanPrefabAsset(groupPath) }, "perf"));
            Time("Prefab save: dependent open scene (scan + record)", () => history.Record(new[] { new SceneBindingScanner().Scan(scene) }, "perf"));

            // 変化があるとき（Unit の 1 Track を NULL に → 全 Director の文脈に影響）
            SetUnitBinding(unitPath, false);
            RecordSummary lostUnit = Time("Change: Unit.prefab binding set to NULL (scan + record)", () => history.Record(new[] { new SceneBindingScanner().ScanPrefabAsset(unitPath) }, "perf"));
            RecordSummary lostScene = Time("Change: open scene after the change (scan + record)", () => history.Record(new[] { new SceneBindingScanner().Scan(scene) }, "perf"));
            Note($"  new events {lostUnit.NewEventCount + lostScene.NewEventCount}, notifications {lostUnit.NewNotifications.Count + lostScene.NewNotifications.Count}");
            Note($"  unit: saved={lostUnit.Saved} error={lostUnit.Error} skipped={string.Join(" / ", lostUnit.SkippedReasons)} recorded={lostUnit.RecordedScopeCount}");
            SetUnitBinding(unitPath, true);
            history.Record(new[] { new SceneBindingScanner().ScanPrefabAsset(unitPath), new SceneBindingScanner().Scan(scene) }, "perf");

            Time("Pre-build check (scene open, prefabs included)", () => PreBuildCheck.Run(new[] { scenePath }, "perf", history));

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Time("Pre-build check (scene closed, prefabs only)", () => PreBuildCheck.Run(new[] { scenePath }, "perf", history));
            BuildSceneScanResult buildScan = Time("Build scene scan (scene closed: open + scan + record + close)",
                () => BuildSceneScan.Run(new[] { scenePath }, history, p => false, (i, n, p) => false));
            Note($"  scanned {buildScan.ScannedSceneCount}, opened {buildScan.OpenedSceneCount}");

            // 起動時の読み込みとメモリ
            long diskBytes = history.Store.GetTotalSizeBytes();
            GC.Collect();
            long gcBefore = GC.GetTotalMemory(true);
            long monoBefore = Profiler.GetMonoUsedSizeLong();
            HistoryStore reloaded = Time("Editor startup: load history (events + notifications only)", () =>
            {
                HistoryStore store = new HistoryStore(historyDirectory, "perf-project");
                store.Load();
                return store;
            });
            long gcStartup = GC.GetTotalMemory(true) - gcBefore;
            Time("Load everything (contexts + owners)", () => reloaded.EnsureAllLoaded());
            long gcAll = GC.GetTotalMemory(true) - gcBefore;
            long monoAll = Profiler.GetMonoUsedSizeLong() - monoBefore;
            Note($"History on disk: {diskBytes / 1024.0:0.0} KB");
            Note($"Managed memory after startup load: +{gcStartup / 1024.0:0.0} KB, after loading everything: +{gcAll / 1024.0:0.0} KB (Mono used +{monoAll / 1024.0:0.0} KB)");
            GC.KeepAlive(reloaded);

            WriteReport();
        }

        private T Time<T>(string label, Func<T> action)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            T result = action();
            stopwatch.Stop();
            Note($"{label}: {stopwatch.Elapsed.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)} ms");
            return result;
        }

        private void Time(string label, Func<bool> action)
        {
            Time<bool>(label, action);
        }

        private void Note(string line)
        {
            _report.AppendLine(line);
            Debug.Log("[TBG Perf] " + line);
        }

        private void WriteReport()
        {
            string directory = Path.Combine(Path.GetFullPath("Temp"), "TBGPerf");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "report.txt"), _report.ToString(), new UTF8Encoding(false));
            _report.Clear();
        }

        private static GuardianHistory CreateHistory(string directory)
        {
            HistoryStore store = new HistoryStore(directory, "perf-project");
            store.Load();
            return new GuardianHistory(store);
        }

        /// <summary>
        /// Unit の最初の Track のバインドを NULL にする / 元の対象に戻す。
        /// </summary>
        private static void SetUnitBinding(string unitPath, bool bound)
        {
            using (PrefabUtility.EditPrefabContentsScope scope = new PrefabUtility.EditPrefabContentsScope(unitPath))
            {
                PlayableDirector director = scope.prefabContentsRoot.GetComponent<PlayableDirector>();
                TrackAsset track = ((TimelineAsset)director.playableAsset).GetOutputTracks().First();
                director.SetGenericBinding(track, bound ? scope.prefabContentsRoot.transform.GetChild(0).gameObject : null);
            }
        }

        /// <summary>
        /// 合成素材を作る（既にあれば再利用）。
        /// </summary>
        private static string EnsureFixture(string name, int trackCount, int groupCount)
        {
            string folder = $"{Folder}/{name}";
            string scenePath = $"{folder}/PerfScene.unity";
            if (File.Exists(scenePath))
            {
                return scenePath;
            }

            EnsureFolder(TestFixtureBuilder.Root);
            EnsureFolder(Folder);
            EnsureFolder(folder);

            TimelineAsset timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, $"{folder}/PerfTimeline.playable");
            List<TrackAsset> tracks = new List<TrackAsset>();
            for (int i = 0; i < trackCount; i++)
            {
                tracks.Add(timeline.CreateTrack<ActivationTrack>(null, "Track" + i));
            }

            AssetDatabase.SaveAssets();

            GameObject unit = new GameObject("Unit");
            PlayableDirector director = unit.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            for (int i = 0; i < trackCount; i++)
            {
                GameObject target = new GameObject("Target" + i);
                target.transform.SetParent(unit.transform);
                director.SetGenericBinding(tracks[i], target);
            }

            GameObject unitAsset = PrefabUtility.SaveAsPrefabAsset(unit, $"{folder}/Unit.prefab");
            Object.DestroyImmediate(unit);

            GameObject group = new GameObject("Group");
            for (int i = 0; i < UnitsPerGroup; i++)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(unitAsset);
                instance.transform.SetParent(group.transform);
            }

            GameObject groupAsset = PrefabUtility.SaveAsPrefabAsset(group, $"{folder}/Group.prefab");
            Object.DestroyImmediate(group);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            for (int i = 0; i < groupCount; i++)
            {
                PrefabUtility.InstantiatePrefab(groupAsset, scene);
            }

            EditorSceneManager.SaveScene(scene, scenePath);
            return scenePath;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
