using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace TimelineBindingGuardian.Validation
{
    /// <summary>
    /// 仕様書 4.2 読み取りの検証。アセットを変更しないことをハッシュで確認する。
    /// </summary>
    public static class Phase0ReadProbe
    {
        public static void Run(string setName, string tag, Phase0Log log)
        {
            string folder = Phase0Paths.GetSetDirectory(setName);
            log.Section($"Read probe set={setName} tag={tag} unity={Application.unityVersion}");
            Dictionary<string, string> before = Phase0Inspector.HashFolder(folder);

            foreach (string prefabPath in Phase0Paths.GetPrefabPaths(setName))
            {
                DumpPrefabAssetDirect(prefabPath, log);
            }

            foreach (string prefabPath in Phase0Paths.GetPrefabPaths(setName))
            {
                DumpPrefabContents(prefabPath, log);
            }

            foreach (string scenePath in Phase0Paths.GetScenePaths(setName))
            {
                DumpScene(scenePath, log);
            }

            log.Section("YAML excerpts");
            foreach (string prefabPath in Phase0Paths.GetPrefabPaths(setName))
            {
                Phase0Inspector.DumpYamlExcerpts(prefabPath, log, 12);
            }

            foreach (string scenePath in Phase0Paths.GetScenePaths(setName))
            {
                Phase0Inspector.DumpYamlExcerpts(scenePath, log, 12);
            }

            log.Section("Dirty / file change check");
            foreach (string prefabPath in Phase0Paths.GetPrefabPaths(setName))
            {
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                log.Check(!EditorUtility.IsDirty(asset), $"asset not dirty: {prefabPath}");
            }

            AssetDatabase.SaveAssets();
            Dictionary<string, string> after = Phase0Inspector.HashFolder(folder);
            Phase0Inspector.CompareHashes(before, after, log, "read probe (after SaveAssets)");
        }

        private static void DumpPrefabAssetDirect(string prefabPath, Phase0Log log)
        {
            log.Section($"Prefab asset (direct load): {prefabPath}");
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            log.Line($"assetType={PrefabUtility.GetPrefabAssetType(asset)} gid={Phase0Inspector.GetGlobalId(asset)}");
            foreach (PlayableDirector director in asset.GetComponentsInChildren<PlayableDirector>(true))
            {
                Phase0Inspector.DumpDirector(director, log, true);
            }
        }

        private static void DumpPrefabContents(string prefabPath, Phase0Log log)
        {
            log.Section($"Prefab contents (LoadPrefabContents): {prefabPath}");
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Dictionary<string, string> assetIds = new Dictionary<string, string>();
            foreach (PlayableDirector director in asset.GetComponentsInChildren<PlayableDirector>(true))
            {
                assetIds[Phase0Inspector.GetIndexPath(director.transform)] = Phase0Inspector.GetGlobalId(director);
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                foreach (PlayableDirector director in contents.GetComponentsInChildren<PlayableDirector>(true))
                {
                    string path = Phase0Inspector.GetIndexPath(director.transform);
                    string contentsId = Phase0Inspector.GetGlobalId(director);
                    UnityEngine.Object source = PrefabUtility.GetCorrespondingObjectFromSource(director);
                    assetIds.TryGetValue(path, out string assetId);
                    log.Line($"{path}: contentsGid={contentsId} assetGid={assetId} sameGid={contentsId == assetId} source={Phase0Inspector.Describe(source)}");
                    log.Indent();
                    Phase0Inspector.DumpOutputs(director, director.playableAsset as UnityEngine.Timeline.TimelineAsset, log);
                    log.Unindent();
                }

                log.Line("binding table (contents):");
                log.Line(Phase0Inspector.BuildBindingTable(contents));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            log.Line("binding table (asset direct):");
            log.Line(Phase0Inspector.BuildBindingTable(asset));
        }

        private static void DumpScene(string scenePath, Phase0Log log)
        {
            log.Section($"Scene: {scenePath}");
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (PlayableDirector director in root.GetComponentsInChildren<PlayableDirector>(true))
                {
                    Phase0Inspector.DumpDirector(director, log, true);
                }
            }

            log.Check(!scene.isDirty, $"scene not dirty after read: {scenePath}");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }
}
