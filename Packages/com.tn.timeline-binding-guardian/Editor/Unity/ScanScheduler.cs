using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 自動検査のスケジューラ。イベントのコールバックではパスを待ち行列に入れるだけにする。
    /// 作業中（Hierarchy の編集、Override の Apply、Prefab Mode での編集など）は検査せず、
    /// 区切り（Scene の保存・Scene を開く・Prefab Mode を閉じる・起動時・「今すぐ検査」）で Release() されたときに、
    /// 溜まった要求を EditorApplication.update で 1 フレーム 1 件ずつ検査・記録する（最後の追加から 1 秒待つ）。
    /// コンパイル中・アセット更新中・Play Mode 中・監視 OFF・テスト実行中・Prefab Mode 中は、Release() 後でも延期する。
    /// 溜まった要求は Domain Reload で失わないよう SessionState に退避する。
    /// </summary>
    public static class ScanScheduler
    {
        public const double DebounceSeconds = 1.0;

        private const string PendingStateKey = "TimelineBindingGuardian.PendingScans";

        private static readonly ScanQueue _queue = new ScanQueue(DebounceSeconds);
        private static bool _initialized;
        // 区切りが来て、溜まった要求を処理してよい状態（待ち行列が空になったら戻す）
        private static bool _released;

        /// <summary>
        /// 1 件の検査・記録が終わったとき（Scene の検査結果があれば渡す）。
        /// </summary>
        public static event Action<ScanResult, RecordSummary> Processed;

        public static int PendingCount => _queue.Count;
        public static string DeferReason { get; private set; } = string.Empty;
        public static DateTime LastProcessedLocal { get; private set; }

        // 変化が無いため省いた自動検査の数（確認・テスト用）
        public static int SkippedUnchangedCount { get; private set; }

        public static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            RestorePending();
            EditorApplication.update += OnEditorUpdate;
            _initialized = true;
        }

        /// <summary>
        /// Domain Reload の前に呼ばれる。溜まった要求を退避する（Initialize で戻す）。
        /// </summary>
        public static void Shutdown()
        {
            SavePending();
            EditorApplication.update -= OnEditorUpdate;
            _initialized = false;
        }

        /// <summary>
        /// 区切り（Scene の保存・Scene を開く・Prefab Mode を閉じる・起動時・「今すぐ検査」）が来た。溜まった要求を処理してよい。
        /// </summary>
        public static void Release()
        {
            _released = true;
        }

        public static void Enqueue(string path, ScanTargetKind kind, string reason, bool isDerived = false)
        {
            bool accept = !string.IsNullOrEmpty(path) && GuardianSettings.Instance.MonitoringEnabled;
            bool excluded = GuardianSettings.Instance.IsExcluded(path) || BindingChangeMonitor.IsSuspended;
            if (accept && !excluded)
            {
                _queue.Enqueue(new ScanRequest(path, kind, reason, isDerived), EditorApplication.timeSinceStartup);
            }
        }

        /// <summary>
        /// 待機中の予約を捨てる（テスト終了時など、記録したくない変化が予約されている場合）。
        /// </summary>
        public static void ClearPending()
        {
            _queue.Clear();
        }

        public static void EnqueueLoadedScenes(string reason)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                {
                    Enqueue(scene.path, ScanTargetKind.Scene, reason);
                }
            }
        }

        private static void OnEditorUpdate()
        {
            if (_queue.Count == 0)
            {
                // 処理し終えたら、次の区切りまでは作業中の変更を検査しない
                DeferReason = string.Empty;
                _released = false;
                return;
            }

            bool canProcess = CanProcess(out string reason);
            if (canProcess && !_released)
            {
                canProcess = false;
                reason = Lang.T("engine.defer.waitingForSave");
            }

            DeferReason = reason;
            if (_queue.TryDequeue(EditorApplication.timeSinceStartup, canProcess, out ScanRequest request))
            {
                ProcessSafely(request);
            }
        }

        private static bool CanProcess(out string reason)
        {
            reason = string.Empty;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                reason = Lang.T("engine.defer.compiling");
            }
            else if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                reason = Lang.T("engine.defer.playMode");
            }
            else if (BindingChangeMonitor.IsSuspended)
            {
                reason = Lang.T("engine.defer.suspended");
            }
            else if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                reason = Lang.T("engine.defer.prefabMode");
            }

            return reason.Length == 0;
        }

        private static void ProcessSafely(ScanRequest request)
        {
            ScanTiming timing = new ScanTiming();
            try
            {
                Process(request, timing);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning(Lang.T("engine.warning.autoScanFailed", request.Path, exception.Message));
            }

            if (GuardianSettings.Instance.TimingLogEnabled)
            {
                UnityEngine.Debug.Log(timing.Format(request));
            }
        }

        private static void Process(ScanRequest request, ScanTiming timing)
        {
            switch (request.Kind)
            {
                case ScanTargetKind.Scene:
                    ProcessScene(request, timing);
                    break;
                case ScanTargetKind.Prefab:
                    ProcessPrefab(request, timing);
                    break;
                case ScanTargetKind.Timeline:
                    timing.Measure(ScanTiming.Phase.Dependents, () => EnqueueDependents(request.Path, request.Reason));
                    break;
            }
        }

        private static void ProcessScene(ScanRequest request, ScanTiming timing)
        {
            Scene scene = SceneManager.GetSceneByPath(request.Path);
            bool isRecordable = scene.IsValid() && scene.isLoaded && !scene.isDirty;
            if (!isRecordable)
            {
                // 閉じている・未保存の変更がある Scene は記録しない（保存時・開いたときに改めて検査される）
                timing.Outcome = "not loaded or unsaved";
                return;
            }

            // 指紋は 1 回だけ計算し、変化の判定と記録の両方に使う
            string fingerprint = timing.Measure(ScanTiming.Phase.Fingerprint, () => ScopeFingerprint.Compute(request.Path));
            if (IsUnchanged(request.Path, fingerprint))
            {
                timing.Outcome = "unchanged";
                return;
            }

            ScanResult result = timing.Measure(ScanTiming.Phase.Scan, () => new SceneBindingScanner().Scan(scene, fingerprint));
            timing.Measure(ScanTiming.Phase.Record, () => Record(result, request));
        }

        private static void ProcessPrefab(ScanRequest request, ScanTiming timing)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(request.Path);
            if (asset == null)
            {
                timing.Outcome = "not found";
                return;
            }

            // 前回の記録から内容（依存を含む）が変わっていなければ、この Prefab も依存先も検査し直す必要はない
            string fingerprint = timing.Measure(ScanTiming.Phase.Fingerprint, () => ScopeFingerprint.Compute(request.Path));
            if (IsUnchanged(request.Path, fingerprint))
            {
                timing.Outcome = "unchanged";
                return;
            }

            if (asset.GetComponentInChildren<PlayableDirector>(true) != null)
            {
                ScanResult result = timing.Measure(ScanTiming.Phase.Scan, () => new SceneBindingScanner().ScanPrefabAsset(request.Path, fingerprint));
                timing.Measure(ScanTiming.Phase.Record, () => Record(result, request));
            }

            if (!request.IsDerived)
            {
                timing.Measure(ScanTiming.Phase.Dependents, () => EnqueueDependents(request.Path, request.Reason));
            }
        }

        /// <summary>
        /// 前回記録したときからファイル内容（依存を含む）が変わっていないか（Phase 5）。変わっていなければ自動検査を省く。
        /// </summary>
        internal static bool IsUnchanged(string assetPath)
        {
            return IsUnchanged(assetPath, ScopeFingerprint.Compute(assetPath));
        }

        private static bool IsUnchanged(string assetPath, string fingerprint)
        {
            bool unchanged = GuardianHistory.Shared.IsUnchangedSinceLastRecord(assetPath, fingerprint);
            if (unchanged)
            {
                SkippedUnchangedCount++;
            }

            return unchanged;
        }

        /// <summary>
        /// 変更されたアセットに依存する、履歴にある Prefab と読み込み済みの Scene を再検査に回す。
        /// 閉じている Scene は検査できないので、通知の表示で「未検査」として示す。
        /// </summary>
        private static void EnqueueDependents(string assetPath, string reason)
        {
            string derivedReason = Lang.T("engine.reason.dependsOn", reason, System.IO.Path.GetFileName(assetPath));
            List<string> scopes = DependencyIndex.GetDependentScopePaths(assetPath, GuardianHistory.Shared.Store.State.Scopes);
            foreach (string scope in scopes.Where(p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)))
            {
                Enqueue(scope, ScanTargetKind.Prefab, derivedReason, true);
            }

            foreach (string scene in DependencyIndex.GetLoadedScenePathsDependingOn(assetPath))
            {
                Enqueue(scene, ScanTargetKind.Scene, derivedReason, true);
            }
        }

        /// <summary>
        /// 溜まった要求を SessionState に退避する（Domain Reload で static な待ち行列は消えるため）。
        /// </summary>
        internal static void SavePending()
        {
            PendingScanList list = new PendingScanList();
            foreach (ScanRequest request in _queue.Items)
            {
                list.items.Add(new PendingScan
                {
                    path = request.Path,
                    kind = (int)request.Kind,
                    reason = request.Reason,
                    isDerived = request.IsDerived,
                });
            }

            if (list.items.Count == 0)
            {
                SessionState.EraseString(PendingStateKey);
                return;
            }

            SessionState.SetString(PendingStateKey, JsonUtility.ToJson(list));
        }

        /// <summary>
        /// 退避した要求を待ち行列に戻す。処理は次の区切り（起動時の Release など）で行う。
        /// </summary>
        internal static void RestorePending()
        {
            string json = SessionState.GetString(PendingStateKey, string.Empty);
            SessionState.EraseString(PendingStateKey);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            PendingScanList list = JsonUtility.FromJson<PendingScanList>(json);
            foreach (PendingScan item in list?.items ?? new List<PendingScan>())
            {
                if (!string.IsNullOrEmpty(item.path))
                {
                    _queue.Enqueue(new ScanRequest(item.path, (ScanTargetKind)item.kind, item.reason, item.isDerived), EditorApplication.timeSinceStartup);
                }
            }
        }

        private static void Record(ScanResult result, ScanRequest request)
        {
            RecordSummary summary = GuardianHistory.Shared.Record(new[] { result }, request.Reason);
            LastProcessedLocal = DateTime.Now;
            if (!string.IsNullOrEmpty(summary.Error))
            {
                UnityEngine.Debug.LogWarning(Lang.T("engine.warning.historyNotSaved", summary.Error));
            }

            NotificationService.ReportNewNotifications(summary, request.Reason);
            Processed?.Invoke(result, summary);
        }
    }

    [Serializable]
    internal sealed class PendingScanList
    {
        public List<PendingScan> items = new List<PendingScan>();
    }

    [Serializable]
    internal sealed class PendingScan
    {
        public string path;
        public int kind;
        public string reason;
        public bool isDerived;
    }

    /// <summary>
    /// 自動検査 1 件の処理時間の内訳（設定「処理時間を Console に出す」が ON のときに出力する、調査用）。
    /// </summary>
    internal sealed class ScanTiming
    {
        public enum Phase
        {
            Fingerprint,
            Scan,
            Record,
            Dependents,
        }

        private readonly Stopwatch _total = Stopwatch.StartNew();
        private readonly long[] _elapsed = new long[4];

        public string Outcome { get; set; } = string.Empty;

        public T Measure<T>(Phase phase, Func<T> action)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                return action();
            }
            finally
            {
                _elapsed[(int)phase] += stopwatch.ElapsedMilliseconds;
            }
        }

        public void Measure(Phase phase, Action action)
        {
            Measure(phase, () =>
            {
                action();
                return true;
            });
        }

        public string Format(ScanRequest request)
        {
            string outcome = Outcome.Length > 0 ? $" ({Outcome})" : string.Empty;
            return $"[Timeline Binding Guardian] {request.Path}{outcome}: {_total.ElapsedMilliseconds} ms"
                + $" = fingerprint {_elapsed[(int)Phase.Fingerprint]} + scan {_elapsed[(int)Phase.Scan]}"
                + $" + record {_elapsed[(int)Phase.Record]} + dependents {_elapsed[(int)Phase.Dependents]} ms [{request.Reason}]";
        }
    }
}
