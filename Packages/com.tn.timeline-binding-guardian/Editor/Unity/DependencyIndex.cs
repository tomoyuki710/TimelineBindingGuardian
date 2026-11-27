using System.Collections.Generic;
using UnityEditor;
using UnityEngine.SceneManagement;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 履歴にある Scope（Scene / Prefab）→ 依存アセット（推移的）の逆引き。
    /// Prefab や Timeline が変わったときに、再検査すべき Scope と「影響の可能性があるが未検査の Scene」を求める。
    /// 依存一覧は AssetDatabase から取り、メモリにキャッシュする（指紋の計算でも共有する）。
    /// アセットがインポート・移動・削除されたら、そのアセットと、それを依存に含む一覧だけを捨てる。
    /// </summary>
    public static class DependencyIndex
    {
        private static readonly DependencyCache _cache = new DependencyCache(path => AssetDatabase.GetDependencies(path, true));

        /// <summary>
        /// すべて捨てる（テスト用・確実に取り直したいとき）。
        /// </summary>
        public static void Invalidate()
        {
            _cache.Clear();
        }

        /// <summary>
        /// 変わったアセットと、それを依存に含む一覧だけを捨てる。
        /// </summary>
        public static void Invalidate(IEnumerable<string> changedPaths)
        {
            _cache.Invalidate(changedPaths);
        }

        /// <summary>
        /// 依存一覧（推移的、自分自身を含む）を序数順に並べたもの。
        /// </summary>
        public static IReadOnlyList<string> GetSortedDependencies(string path)
        {
            return _cache.GetSorted(path);
        }

        /// <summary>
        /// assetPath に依存している Scope の現在のパス（自分自身は除く）。Scope は GUID から現在のパスを引くので、移動にも追従する。
        /// </summary>
        public static List<string> GetDependentScopePaths(string assetPath, IEnumerable<ScopeRecord> scopes)
        {
            List<string> result = new List<string>();
            foreach (ScopeRecord scope in scopes)
            {
                string scopePath = AssetDatabase.GUIDToAssetPath(scope.ScopeGuid);
                bool isOther = !string.IsNullOrEmpty(scopePath) && scopePath != assetPath;
                if (isOther && _cache.DependsOn(scopePath, assetPath))
                {
                    result.Add(scopePath);
                }
            }

            return result;
        }

        /// <summary>
        /// 読み込まれている保存済み Scene のうち、assetPath に依存しているもの。
        /// </summary>
        public static List<string> GetLoadedScenePathsDependingOn(string assetPath)
        {
            List<string> result = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                bool isSavedAndLoaded = scene.isLoaded && !string.IsNullOrEmpty(scene.path);
                if (isSavedAndLoaded && _cache.DependsOn(scene.path, assetPath))
                {
                    result.Add(scene.path);
                }
            }

            return result;
        }

        public static bool IsSceneLoaded(string scenePath)
        {
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            return scene.IsValid() && scene.isLoaded;
        }
    }
}
