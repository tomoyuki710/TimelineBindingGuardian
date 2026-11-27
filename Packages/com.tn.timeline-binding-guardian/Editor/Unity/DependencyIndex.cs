using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine.SceneManagement;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 履歴にある Scope（Scene / Prefab）→ 依存アセット（推移的）の逆引き。
    /// Prefab や Timeline が変わったときに、再検査すべき Scope と「影響の可能性があるが未検査の Scene」を求める。
    /// 依存関係は AssetDatabase から取り、結果はメモリにキャッシュする（Scene・Prefab のインポートで破棄）。
    /// </summary>
    public static class DependencyIndex
    {
        private static readonly Dictionary<string, HashSet<string>> _dependencies = new Dictionary<string, HashSet<string>>();

        public static void Invalidate()
        {
            _dependencies.Clear();
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
                if (isOther && GetDependencies(scopePath).Contains(assetPath))
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
                if (isSavedAndLoaded && GetDependencies(scene.path).Contains(assetPath))
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

        private static HashSet<string> GetDependencies(string path)
        {
            if (!_dependencies.TryGetValue(path, out HashSet<string> dependencies))
            {
                dependencies = new HashSet<string>(AssetDatabase.GetDependencies(path, true), StringComparer.Ordinal);
                _dependencies.Add(path, dependencies);
            }

            return dependencies;
        }
    }
}
