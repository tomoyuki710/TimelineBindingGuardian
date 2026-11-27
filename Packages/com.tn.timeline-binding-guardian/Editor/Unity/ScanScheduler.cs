using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 自動検査のスケジューラ。イベントのコールバックではパスを待ち行列に入れるだけにし、
    /// 最後の追加から 1 秒後に EditorApplication.update で 1 フレーム 1 件ずつ検査・記録する。
    /// コンパイル中・アセット更新中・Play Mode 中・監視 OFF・テスト実行中は処理を延期する。
    /// </summary>
    public static class ScanScheduler
    {
        public const double DebounceSeconds = 1.0;

        private static readonly ScanQueue _queue = new ScanQueue(DebounceSeconds);
        private static bool _initialized;

        /// <summary>
        /// 1 件の検査・記録が終わったとき（Scene の検査結果があれば渡す）。
        /// </summary>
        public static event Action<ScanResult, RecordSummary> Processed;

        public static int PendingCount => _queue.Count;
        public static string DeferReason { get; private set; } = string.Empty;
        public static DateTime LastProcessedLocal { get; private set; }

        public static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            EditorApplication.update += OnEditorUpdate;
            _initialized = true;
        }

        public static void Shutdown()
        {
            EditorApplication.update -= OnEditorUpdate;
            _initialized = false;
        }

        public static void Enqueue(string path, ScanTargetKind kind, string reason, bool isDerived = false)
        {
            bool accept = !string.IsNullOrEmpty(path) && GuardianSettings.Instance.MonitoringEnabled;
            if (accept && !BindingChangeMonitor.IsSuspended)
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
                DeferReason = string.Empty;
                return;
            }

            bool canProcess = CanProcess(out string reason);
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
                reason = "Waiting for compilation or asset import to finish.";
            }
            else if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                reason = "Deferred during Play Mode.";
            }
            else if (BindingChangeMonitor.IsSuspended)
            {
                reason = "Monitoring is suspended.";
            }

            return reason.Length == 0;
        }

        private static void ProcessSafely(ScanRequest request)
        {
            try
            {
                Process(request);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Timeline Binding Guardian] Automatic scan of '{request.Path}' failed and was skipped: {exception.Message}");
            }
        }

        private static void Process(ScanRequest request)
        {
            switch (request.Kind)
            {
                case ScanTargetKind.Scene:
                    ProcessScene(request);
                    break;
                case ScanTargetKind.Prefab:
                    ProcessPrefab(request);
                    break;
                case ScanTargetKind.Timeline:
                    EnqueueDependents(request.Path, request.Reason);
                    break;
            }
        }

        private static void ProcessScene(ScanRequest request)
        {
            Scene scene = SceneManager.GetSceneByPath(request.Path);
            bool isRecordable = scene.IsValid() && scene.isLoaded && !scene.isDirty;
            if (!isRecordable)
            {
                // 閉じている・未保存の変更がある Scene は記録しない（保存時・開いたときに改めて検査される）
                return;
            }

            ScanResult result = new SceneBindingScanner().Scan(scene);
            Record(result, request);
        }

        private static void ProcessPrefab(ScanRequest request)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(request.Path);
            if (asset == null)
            {
                return;
            }

            if (asset.GetComponentInChildren<PlayableDirector>(true) != null)
            {
                Record(new SceneBindingScanner().ScanPrefabAsset(request.Path), request);
            }

            if (!request.IsDerived)
            {
                EnqueueDependents(request.Path, request.Reason);
            }
        }

        /// <summary>
        /// 変更されたアセットに依存する、履歴にある Prefab と読み込み済みの Scene を再検査に回す。
        /// 閉じている Scene は検査できないので、通知の表示で「未検査」として示す。
        /// </summary>
        private static void EnqueueDependents(string assetPath, string reason)
        {
            string derivedReason = $"{reason} (depends on {System.IO.Path.GetFileName(assetPath)})";
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

        private static void Record(ScanResult result, ScanRequest request)
        {
            RecordSummary summary = GuardianHistory.Shared.Record(new[] { result }, request.Reason);
            LastProcessedLocal = DateTime.Now;
            if (!string.IsNullOrEmpty(summary.Error))
            {
                Debug.LogWarning($"[Timeline Binding Guardian] History was not saved: {summary.Error}");
            }

            NotificationService.ReportNewNotifications(summary, request.Reason);
            Processed?.Invoke(result, summary);
        }
    }
}
