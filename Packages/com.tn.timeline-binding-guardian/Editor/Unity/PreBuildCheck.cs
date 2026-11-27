using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// ビルド前検査（仕様 11章、T22）。Build Settings の有効な Scene を対象に、
    /// - 読み込み済みで保存済みの Scene はそのまま検査して記録する（Scene は開かない・変更しない）。
    /// - 対象 Scene が依存する Prefab（Director を持つもの）を検査して記録する（閉じた Scene でも Prefab 側の変化は分かる）。
    /// - 新しい Lost は既存の通知と Console の集約 Warning で知らせる。Dismiss 済みは再通知しない。
    /// - どんな失敗でもビルドを止めない（Warning を出すだけ）。ダイアログ・進捗バーは出さない（CI でも同じ）。
    /// </summary>
    public static class PreBuildCheck
    {
        /// <summary>
        /// Build Settings の有効な Scene のパス（存在するものだけ、Build Settings の順）。
        /// </summary>
        public static List<string> GetBuildScenePaths()
        {
            return EditorBuildSettings.scenes
                .Where(s => s.enabled && !string.IsNullOrEmpty(s.path))
                .Select(s => s.path)
                .Where(p => !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(p)))
                .ToList();
        }

        /// <summary>
        /// 例外を外に出さずに検査する（ビルドを止めないため）。失敗したら Warning を出して null を返す。
        /// </summary>
        public static BuildCheckRecord RunSafely(Func<IReadOnlyList<string>> getScenePaths, string platform, GuardianHistory history)
        {
            try
            {
                return Run(getScenePaths(), platform, history);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(Lang.T("engine.warning.preBuildFailed", exception.Message));
                return null;
            }
        }

        public static BuildCheckRecord Run(IReadOnlyList<string> scenePaths, string platform, GuardianHistory history)
        {
            List<string> skipped = new List<string>();
            List<ScanResult> results = new List<ScanResult>();
            SceneBindingScanner scanner = new SceneBindingScanner();
            string reason = Lang.T("engine.reason.preBuild");

            foreach (string scenePath in scenePaths)
            {
                Scene scene = SceneManager.GetSceneByPath(scenePath);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    // 閉じている Scene は開かない（ユーザーの Scene 構成を変えない）。依存する Prefab は下で検査する
                    skipped.Add(Lang.T("engine.preBuild.sceneClosed", scenePath));
                    continue;
                }

                // 前回の記録から変わっていなければ検査を省く（dirty・除外フォルダは GuardianHistory.Record が理由付きで記録しない）
                if (!scene.isDirty && IsUnchanged(history, scenePath))
                {
                    continue;
                }

                results.Add(scanner.Scan(scene));
            }

            foreach (string prefabPath in GetDependentPrefabPaths(scenePaths))
            {
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                bool hasDirector = asset != null && asset.GetComponentInChildren<PlayableDirector>(true) != null;
                if (hasDirector && !IsUnchanged(history, prefabPath))
                {
                    results.Add(scanner.ScanPrefabAsset(prefabPath));
                }
            }

            RecordSummary summary = history.Record(results, reason);
            if (!string.IsNullOrEmpty(summary.Error))
            {
                Debug.LogWarning(Lang.T("engine.warning.historyNotSaved", summary.Error));
            }

            NotificationService.ReportNewNotifications(summary, reason);

            skipped.AddRange(summary.SkippedReasons);
            string checkedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            BuildCheckRecord record = new BuildCheckRecord(checkedAtUtc, platform, scenePaths.ToList(), summary.ObservationIds.ToList(),
                summary.NewEventCount, summary.NewNotifications.Count, skipped, summary.Error);
            if (!history.RecordBuild(record, out string error))
            {
                Debug.LogWarning(Lang.T("engine.warning.historyNotSaved", error));
            }

            return record;
        }

        private static bool IsUnchanged(GuardianHistory history, string assetPath)
        {
            return history.IsUnchangedSinceLastRecord(assetPath, ScopeFingerprint.Compute(assetPath));
        }

        /// <summary>
        /// Scene が（推移的に）依存する Prefab のパス。重複なし・パス順。
        /// </summary>
        private static List<string> GetDependentPrefabPaths(IEnumerable<string> scenePaths)
        {
            return scenePaths
                .SelectMany(p => AssetDatabase.GetDependencies(p, true))
                .Where(p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <summary>
    /// ビルドの開始時に PreBuildCheck を呼ぶ。設定でオフにできる。ビルドは止めない。
    /// </summary>
    internal sealed class PreBuildCheckHook : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!GuardianSettings.Instance.PreBuildCheckEnabled)
            {
                return;
            }

            PreBuildCheck.RunSafely(PreBuildCheck.GetBuildScenePaths, report.summary.platform.ToString(), GuardianHistory.Shared);
        }
    }
}
