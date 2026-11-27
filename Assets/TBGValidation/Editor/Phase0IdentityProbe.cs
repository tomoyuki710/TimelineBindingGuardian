using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace TimelineBindingGuardian.Validation
{
    /// <summary>
    /// 名前変更・兄弟の並べ替え・非アクティブ化の前後で、GlobalObjectId を鍵にした
    /// Director/Track/Target の対応が変化しないことを確認する（仕様 3.2）。"Identity" セットを新規生成して使う。
    /// </summary>
    public static class Phase0IdentityProbe
    {
        public const string IdentitySet = "Identity";

        public static void Run(Phase0Log log)
        {
            if (!Phase0Paths.SetExists(IdentitySet))
            {
                Phase0AssetBuilder.Build(IdentitySet, log);
            }

            string middlePath = Phase0Paths.GetMiddlePrefabPath(IdentitySet);
            string[] prefabPaths =
            {
                middlePath,
                Phase0Paths.GetParentPrefabPath(IdentitySet),
                Phase0Paths.GetVariantPrefabPath(IdentitySet),
            };

            Dictionary<string, Dictionary<string, string>> before = Capture(prefabPaths);

            log.Section("I1 Middle: rename Child#1 -> 'Renamed', move it to sibling index 0, deactivate Child#0's Director GameObject");
            using (PrefabUtility.EditPrefabContentsScope scope = new PrefabUtility.EditPrefabContentsScope(middlePath))
            {
                Transform childX = scope.prefabContentsRoot.transform.GetChild(0);
                Transform childY = scope.prefabContentsRoot.transform.GetChild(1);
                childY.name = "Renamed";
                childY.SetSiblingIndex(0);
                childX.Find("Director").gameObject.SetActive(false);
            }

            AssetDatabase.SaveAssets();
            Dictionary<string, Dictionary<string, string>> after = Capture(prefabPaths);

            foreach (string path in prefabPaths)
            {
                Dictionary<string, string> expected = before[path];
                Dictionary<string, string> actual = after[path];
                log.Line($"## {path}: directors before={expected.Count} after={actual.Count}");
                bool sameKeys = expected.Keys.OrderBy(k => k).SequenceEqual(actual.Keys.OrderBy(k => k));
                log.Check(sameKeys, $"{path}: same director GlobalObjectIds");
                foreach (KeyValuePair<string, string> pair in expected)
                {
                    if (!actual.TryGetValue(pair.Key, out string value))
                    {
                        log.Line($"  missing director {pair.Key}");
                        continue;
                    }

                    if (value != pair.Value)
                    {
                        log.Line($"  changed {pair.Key}:\n    before: {pair.Value}\n    after : {value}");
                    }
                }

                bool sameBindings = sameKeys && expected.All(p => actual[p.Key] == p.Value);
                log.Check(sameBindings, $"{path}: bindings keyed by GlobalObjectId unchanged");
            }

            log.Section("Paths after change (display only)");
            foreach (string path in prefabPaths)
            {
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (PlayableDirector director in asset.GetComponentsInChildren<PlayableDirector>(true))
                {
                    log.Line($"{Phase0Inspector.GetIndexPath(director.transform)} gid={Phase0Inspector.GetGlobalId(director)} activeSelf={director.gameObject.activeSelf}");
                }
            }
        }

        /// <summary>
        /// assetPath -> (Director gid -> "Track=TargetGid(override)" を連結した文字列)。パスや名前を含めない。
        /// </summary>
        private static Dictionary<string, Dictionary<string, string>> Capture(string[] prefabPaths)
        {
            Dictionary<string, Dictionary<string, string>> result = new Dictionary<string, Dictionary<string, string>>();
            foreach (string path in prefabPaths)
            {
                Dictionary<string, string> directors = new Dictionary<string, string>();
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (PlayableDirector director in asset.GetComponentsInChildren<PlayableDirector>(true))
                {
                    TimelineAsset timeline = director.playableAsset as TimelineAsset;
                    Dictionary<Object, bool> flags = Phase0Inspector.GetValueOverrideFlags(director);
                    List<string> parts = new List<string>();
                    foreach (PlayableBinding binding in timeline.outputs)
                    {
                        if (binding.sourceObject == null || binding.outputTargetType == null)
                        {
                            continue;
                        }

                        string overrideText = flags.TryGetValue(binding.sourceObject, out bool flag) ? flag.ToString() : "NO_ENTRY";
                        parts.Add($"{Phase0Inspector.GetGlobalId(binding.sourceObject)}={Phase0Inspector.GetGlobalId(director.GetGenericBinding(binding.sourceObject))}({overrideText})");
                    }

                    directors[Phase0Inspector.GetGlobalId(director)] = string.Join("; ", parts);
                }

                result[path] = directors;
            }

            return result;
        }
    }
}
