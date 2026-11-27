using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// Build Scene の一括検査の結果。
    /// </summary>
    public sealed class BuildSceneScanResult
    {
        public string BlockingReason { get; set; } = string.Empty;
        public bool Cancelled { get; set; }
        public int ScannedSceneCount { get; set; }
        public int OpenedSceneCount { get; set; }
        public List<string> SkippedReasons { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();
        // 記録の結果をまとめたもの（Console の集約 Warning 用）
        public RecordSummary Combined { get; } = new RecordSummary();
    }

    /// <summary>
    /// Build Settings の Scene の一括検査（ユーザーが明示的に実行する、Phase 5）。
    /// - 閉じている Scene を 1 つずつ追加で開いて検査し、すぐ閉じる。読み込み済みの Scene はそのまま検査する。
    /// - Scene ごとに完全なスナップショットとして記録する（キャンセルしたら、完了した Scene までを記録する）。
    /// - 終了時（キャンセル・例外を含む）に Scene の構成・アクティブ Scene・Selection を元に戻し、自動監視を再開する。
    /// - 読み込み中の Scene に未保存の変更があるとき、Prefab Mode・Play Mode・コンパイル中は実行しない。
    /// </summary>
    public static class BuildSceneScan
    {
        /// <summary>
        /// 実行できない理由（実行できるなら空文字）。
        /// </summary>
        public static string GetBlockingReason()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return Lang.T("engine.buildScan.blocked.playMode");
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return Lang.T("engine.buildScan.blocked.compiling");
            }

            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                return Lang.T("engine.buildScan.blocked.prefabMode");
            }

            List<string> dirty = GetLoadedScenes().Where(s => s.isDirty).Select(s => string.IsNullOrEmpty(s.path) ? s.name : s.path).ToList();
            return dirty.Count > 0 ? Lang.T("engine.buildScan.blocked.unsaved", string.Join(", ", dirty)) : string.Empty;
        }

        /// <param name="isExcluded">除外フォルダの判定（除外された Scene は開かない）。</param>
        /// <param name="isCancelRequested">進捗の表示とキャンセルの確認（index, total, scenePath）。true を返すと中止する。</param>
        public static BuildSceneScanResult Run(IReadOnlyList<string> scenePaths, GuardianHistory history, Func<string, bool> isExcluded,
            Func<int, int, string, bool> isCancelRequested)
        {
            BuildSceneScanResult result = new BuildSceneScanResult { BlockingReason = GetBlockingReason() };
            if (result.BlockingReason.Length > 0)
            {
                return result;
            }

            SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
            Object[] selection = Selection.objects;
            bool wasSuspended = BindingChangeMonitor.IsSuspended;
            // 一時的に開く・閉じる操作を自動監視に拾わせない
            BindingChangeMonitor.IsSuspended = true;
            string reason = Lang.T("engine.reason.buildScan");
            try
            {
                for (int i = 0; i < scenePaths.Count; i++)
                {
                    string scenePath = scenePaths[i];
                    if (isCancelRequested(i, scenePaths.Count, scenePath))
                    {
                        result.Cancelled = true;
                        break;
                    }

                    if (isExcluded(scenePath))
                    {
                        result.SkippedReasons.Add($"{scenePath}: {Lang.T("engine.skip.excludedFolder")}");
                        continue;
                    }

                    ScanAndRecord(scenePath, history, reason, result);
                }
            }
            finally
            {
                RestoreEditorState(setup, selection);
                ScanScheduler.ClearPending();
                BindingChangeMonitor.IsSuspended = wasSuspended;
            }

            return result;
        }

        private static void ScanAndRecord(string scenePath, GuardianHistory history, string reason, BuildSceneScanResult result)
        {
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool wasLoaded = scene.IsValid() && scene.isLoaded;
            ScanResult scan;
            if (wasLoaded)
            {
                scan = new SceneBindingScanner().Scan(scene);
            }
            else
            {
                Scene opened = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                result.OpenedSceneCount++;
                try
                {
                    scan = new SceneBindingScanner().Scan(opened);
                }
                finally
                {
                    EditorSceneManager.CloseScene(opened, true);
                }
            }

            RecordSummary summary = history.Record(new[] { scan }, reason);
            if (!string.IsNullOrEmpty(summary.Error))
            {
                result.Errors.Add($"{scenePath}: {summary.Error}");
                return;
            }

            result.ScannedSceneCount++;
            result.SkippedReasons.AddRange(summary.SkippedReasons);
            result.Combined.NewEvents.AddRange(summary.NewEvents);
            result.Combined.NewNotifications.AddRange(summary.NewNotifications);
            result.Combined.NewEventCount += summary.NewEventCount;
            result.Combined.ObservationIds.AddRange(summary.ObservationIds);
        }

        /// <summary>
        /// Scene の構成が変わっていれば元に戻す（開いた Scene はその場で閉じているので、通常は変わらない）。Selection も戻す。
        /// </summary>
        private static void RestoreEditorState(SceneSetup[] setup, Object[] selection)
        {
            SceneSetup[] current = EditorSceneManager.GetSceneManagerSetup();
            if (!IsSameSetup(setup, current))
            {
                EditorSceneManager.RestoreSceneManagerSetup(setup);
            }

            Selection.objects = selection;
        }

        private static bool IsSameSetup(SceneSetup[] a, SceneSetup[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                bool samePath = a[i].path == b[i].path;
                bool sameState = a[i].isLoaded == b[i].isLoaded && a[i].isActive == b[i].isActive;
                if (!samePath || !sameState)
                {
                    return false;
                }
            }

            return true;
        }

        private static IEnumerable<Scene> GetLoadedScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                {
                    yield return scene;
                }
            }
        }
    }
}
