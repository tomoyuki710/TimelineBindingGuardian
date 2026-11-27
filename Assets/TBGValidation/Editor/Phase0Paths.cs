using System.IO;
using UnityEditor;

namespace TimelineBindingGuardian.Validation
{
    /// <summary>
    /// Phase 0 検証で使用するパスの定義。
    /// </summary>
    public static class Phase0Paths
    {
        public const string ValidationRoot = "Assets/TBGValidation";
        public const string GeneratedRoot = ValidationRoot + "/Generated";

        // "~" で終わるフォルダは Unity がインポートしないため、結果ファイルの出力先に使う
        public static string ResultsDirectory =>
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ValidationRoot, "Results~"));

        // 既存アセットを削除・上書きしないため、生成先を実行名で分ける（-tbgRun 引数、既定 "r1"）
        public static string RunName { get; set; } = "r1";

        public static string GetSetDirectory(string setName)
        {
            return $"{GeneratedRoot}/{RunName}/{setName}";
        }

        public static string GetChildPrefabPath(string setName) => $"{GetSetDirectory(setName)}/Child.prefab";
        public static string GetMiddlePrefabPath(string setName) => $"{GetSetDirectory(setName)}/Middle.prefab";
        public static string GetParentPrefabPath(string setName) => $"{GetSetDirectory(setName)}/Parent.prefab";
        public static string GetVariantPrefabPath(string setName) => $"{GetSetDirectory(setName)}/ParentVariant.prefab";
        public static string GetTimelinePath(string setName) => $"{GetSetDirectory(setName)}/ChildTimeline.playable";
        public static string GetSceneAPath(string setName) => $"{GetSetDirectory(setName)}/SceneA.unity";
        public static string GetSceneBPath(string setName) => $"{GetSetDirectory(setName)}/SceneB.unity";

        public static string[] GetPrefabPaths(string setName)
        {
            return new[]
            {
                GetChildPrefabPath(setName),
                GetMiddlePrefabPath(setName),
                GetParentPrefabPath(setName),
                GetVariantPrefabPath(setName),
            };
        }

        public static string[] GetScenePaths(string setName)
        {
            return new[] { GetSceneAPath(setName), GetSceneBPath(setName) };
        }

        public static bool SetExists(string setName)
        {
            return AssetDatabase.IsValidFolder(GetSetDirectory(setName));
        }
    }
}
