using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 自動監視の入口。保存・Scene を開く・Prefab Mode での保存・起動（Domain Reload 後）を受けて検査を予約する。
    /// アセットのインポート（外部変更・Git Pull を含む）は BindingAssetPostprocessor が受ける。
    /// コールバックでは予約だけを行い、重い検査はしない。
    /// ObjectChangeEvents は未保存の変更が主で、保存済みの状態だけを履歴の正とする方針のため使わない。
    /// </summary>
    [InitializeOnLoad]
    public static class BindingChangeMonitor
    {
        private static bool _registered;

        static BindingChangeMonitor()
        {
            Register();
            // 起動時・Domain Reload 後に、開いている Scene を前回の記録と比較する
            EditorApplication.delayCall += OnStartup;
        }

        /// <summary>
        /// true の間は予約も処理もしない（テスト実行中に、テスト用の Scene や Prefab が実プロジェクトの履歴に記録されないようにする）。
        /// </summary>
        public static bool IsSuspended { get; set; }

        private static void Register()
        {
            if (_registered)
            {
                return;
            }

            EditorSceneManager.sceneSaved += OnSceneSaved;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            PrefabStage.prefabSaved += OnPrefabSaved;
            PrefabStage.prefabStageClosing += OnPrefabStageClosing;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            ScanScheduler.Initialize();
            _registered = true;
        }

        private static void Unregister()
        {
            EditorSceneManager.sceneSaved -= OnSceneSaved;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            PrefabStage.prefabSaved -= OnPrefabSaved;
            PrefabStage.prefabStageClosing -= OnPrefabStageClosing;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            ScanScheduler.Shutdown();
            _registered = false;
        }

        private static void OnStartup()
        {
            ScanScheduler.EnqueueLoadedScenes(Lang.T("engine.reason.editorStartup"));
            ScanScheduler.Release();
        }

        private static void OnBeforeAssemblyReload()
        {
            Unregister();
        }

        private static void OnSceneSaved(Scene scene)
        {
            ScanScheduler.Enqueue(scene.path, ScanTargetKind.Scene, Lang.T("engine.reason.sceneSaved"));
            ScanScheduler.Release();
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            ScanScheduler.Enqueue(scene.path, ScanTargetKind.Scene, Lang.T("engine.reason.sceneOpened"));
            ScanScheduler.Release();
        }

        /// <summary>
        /// Prefab Mode を閉じる区切り。閉じ終わるまでは ScanScheduler が Prefab Mode 中として待つ。
        /// </summary>
        private static void OnPrefabStageClosing(PrefabStage stage)
        {
            ScanScheduler.Release();
        }

        private static void OnPrefabSaved(GameObject prefabContentsRoot)
        {
            PrefabStage stage = PrefabStageUtility.GetPrefabStage(prefabContentsRoot);
            if (stage != null)
            {
                ScanScheduler.Enqueue(stage.assetPath, ScanTargetKind.Prefab, Lang.T("engine.reason.prefabSaved"));
            }
        }
    }

    /// <summary>
    /// インポート・移動された Prefab / Scene / Timeline を検査に回す（外部変更や Git Pull による変化も含む）。
    /// </summary>
    public sealed class BindingAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            // 依存一覧のキャッシュは、変わったアセットとそれを含む一覧だけを捨てる（全部捨てると、毎回すべてを取り直すことになり重い）
            DependencyIndex.Invalidate(importedAssets.Concat(deletedAssets).Concat(movedAssets).Concat(movedFromAssetPaths));

            foreach (string path in importedAssets)
            {
                EnqueueIfRelevant(path, Lang.T("engine.reason.assetImported"));
            }

            foreach (string path in movedAssets)
            {
                EnqueueIfRelevant(path, Lang.T("engine.reason.assetMoved"));
            }
        }

        private static void EnqueueIfRelevant(string path, string reason)
        {
            if (path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
            {
                ScanScheduler.Enqueue(path, ScanTargetKind.Prefab, reason);
            }
            else if (path.EndsWith(".unity", System.StringComparison.OrdinalIgnoreCase))
            {
                // 読み込まれていない Scene は検査時に読み飛ばされる（開いたときに検査される）
                ScanScheduler.Enqueue(path, ScanTargetKind.Scene, reason);
            }
            else if (path.EndsWith(".playable", System.StringComparison.OrdinalIgnoreCase))
            {
                ScanScheduler.Enqueue(path, ScanTargetKind.Timeline, reason);
            }
        }
    }
}
