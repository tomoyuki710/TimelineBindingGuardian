using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace TimelineBindingGuardian.Validation
{
    /// <summary>
    /// 仕様書 4.3 復元の検証。対象は "Restore" セットのみ。
    /// 各ステップで Child/Middle/Parent/Variant のバインド表・Override 一覧の差分を記録し、
    /// 想定した行以外が変化しないことを確認する。
    /// </summary>
    public static class Phase0RestoreProbe
    {
        private const string TableSuffix = ".table";

        public static void Run(string setName, Phase0Log log)
        {
            TimelineAsset timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(Phase0Paths.GetTimelinePath(setName));
            TrackAsset audio = Phase0AssetBuilder.FindTrack(timeline, Phase0AssetBuilder.TrackAudio);
            TrackAsset activation = Phase0AssetBuilder.FindTrack(timeline, Phase0AssetBuilder.TrackActivation);
            TrackAsset animA = Phase0AssetBuilder.FindTrack(timeline, Phase0AssetBuilder.TrackAnimA);
            TrackAsset signal = Phase0AssetBuilder.FindTrack(timeline, Phase0AssetBuilder.TrackSignal);
            string middlePath = Phase0Paths.GetMiddlePrefabPath(setName);
            string parentPath = Phase0Paths.GetParentPrefabPath(setName);
            string variantPath = Phase0Paths.GetVariantPrefabPath(setName);

            Dictionary<string, List<string>> original = CaptureState(setName);
            CopyFiles(setName, "R0");

            // R1: Parent 階層の NULL Override を取り除き継承へ戻す（RevertPropertyOverride）
            RunStep(setName, log, "R1 Parent MiddleQ/Child#0 Audio: revert NULL override -> inherited",
                () => RevertValueOverride(parentPath, "MiddleQ#1/Child#0/Director", audio, log),
                new Dictionary<string, int> { { "Parent", 1 }, { "Variant", 1 }, { "Middle", 0 }, { "Child", 0 } });

            // R2a: Middle 階層の NULL Override を取り除く
            Dictionary<string, List<string>> beforeR2 = CaptureState(setName);
            RunStep(setName, log, "R2a Middle Child#1 Activation: revert NULL override -> inherited",
                () => RevertValueOverride(middlePath, "Child#1/Director", activation, log),
                new Dictionary<string, int> { { "Middle", 1 }, { "Parent", 2 }, { "Variant", 2 }, { "Child", 0 } });

            // R2b: 明示的 NULL Override を SetGenericBinding + RecordPrefabInstancePropertyModifications で復元
            RunStep(setName, log, "R2b Middle Child#1 Activation: restore explicit NULL (SetGenericBinding+Record)",
                () => SetBindingWithApi(middlePath, "Child#1/Director", activation, null, log),
                new Dictionary<string, int> { { "Middle", 1 }, { "Parent", 2 }, { "Variant", 2 }, { "Child", 0 } });
            CheckStateEquals(beforeR2, CaptureState(setName), log, "R2b state equals state before R2a");

            // R3a: Middle の別対象 Override を別の値に変更（SerializedProperty）
            Dictionary<string, List<string>> beforeR3 = CaptureState(setName);
            RunStep(setName, log, "R3a Middle Child#1 AnimA: change AltActor -> Child#1/ActorB (SerializedProperty)",
                () => SetBindingWithSerializedProperty(middlePath, "Child#1/Director", animA, "Child#1/ActorB", log),
                new Dictionary<string, int> { { "Middle", 1 }, { "Parent", 2 }, { "Variant", 2 }, { "Child", 0 } });

            // R3b: 元の Override 値（AltActor）へ復元（SerializedProperty）
            RunStep(setName, log, "R3b Middle Child#1 AnimA: restore AltActor (SerializedProperty)",
                () => SetBindingWithSerializedProperty(middlePath, "Child#1/Director", animA, "AltActor", log),
                new Dictionary<string, int> { { "Middle", 1 }, { "Parent", 2 }, { "Variant", 2 }, { "Child", 0 } });
            CheckStateEquals(beforeR3, CaptureState(setName), log, "R3b state equals state before R3a");

            // R5: ClearGenericBinding による NoEntry の表現と、配列単位 Revert の影響を観察（採用候補ではない）
            log.Section("R5 Variant MiddleP/Child#1 Signal (ClearGenericBinding) representation");
            DumpOverriddenBindingProperties(variantPath, "MiddleP#0/Child#1/Director", log);
            RunStep(setName, log, "R5 Variant MiddleP/Child#1: RevertPropertyOverride on whole m_SceneBindings (observation only)",
                () => RevertWholeArray(variantPath, "MiddleP#0/Child#1/Director", signal, log),
                new Dictionary<string, int> { { "Variant", 1 }, { "Parent", 0 }, { "Middle", 0 }, { "Child", 0 } });

            CopyFiles(setName, "R9");

            // R4: Scene の Override を Undo 付きで Revert し、Undo で戻せるか
            RunSceneUndo(setName, activation, log);

            log.Section("Summary vs original");
            Dictionary<string, List<string>> final = CaptureState(setName);
            foreach (string key in original.Keys)
            {
                int changed = CountChangedLines(original[key], final[key], log, key, false);
                log.Line($"{key}: {changed} lines differ from original (expected: R1 + R5 effects only)");
            }
        }

        private static void RunStep(string setName, Phase0Log log, string label, Action action, Dictionary<string, int> expectedChangedLines)
        {
            log.Section(label);
            Dictionary<string, List<string>> before = CaptureState(setName);
            action();
            AssetDatabase.SaveAssets();
            Dictionary<string, List<string>> after = CaptureState(setName);

            foreach (KeyValuePair<string, int> pair in expectedChangedLines)
            {
                string tableKey = pair.Key + TableSuffix;
                int changed = CountChangedLines(before[tableKey], after[tableKey], log, pair.Key, true);
                log.Check(changed == pair.Value, $"{pair.Key}: binding table changed lines={changed} expected={pair.Value}");
            }

            foreach (string key in before.Keys.Where(k => k.EndsWith(".mods")))
            {
                List<string> removed = before[key].Except(after[key]).ToList();
                List<string> added = after[key].Except(before[key]).ToList();
                foreach (string line in removed)
                {
                    log.Line($"  {key} - {line}");
                }

                foreach (string line in added)
                {
                    log.Line($"  {key} + {line}");
                }
            }

            string fileTag = label.Substring(0, label.IndexOf(' '));
            CopyFiles(setName, fileTag);
        }

        private static void RevertValueOverride(string prefabPath, string directorPath, TrackAsset track, Phase0Log log)
        {
            using (PrefabUtility.EditPrefabContentsScope scope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
            {
                PlayableDirector director = Phase0AssetBuilder.FindDirector(scope.prefabContentsRoot, directorPath);
                using (SerializedObject serializedObject = new SerializedObject(director))
                {
                    SerializedProperty value = FindValueProperty(serializedObject, track, log);
                    log.Line($"target property={value.propertyPath} prefabOverride(before)={value.prefabOverride} value={Phase0Inspector.DescribeSerializedReference(value)}");
                    PrefabUtility.RevertPropertyOverride(value, InteractionMode.AutomatedAction);
                    serializedObject.Update();
                    SerializedProperty afterValue = FindValueProperty(serializedObject, track, log);
                    log.Line($"after revert (in contents): prefabOverride={afterValue.prefabOverride} value={Phase0Inspector.DescribeSerializedReference(afterValue)}");
                }
            }
        }

        private static void SetBindingWithApi(string prefabPath, string directorPath, TrackAsset track, UnityEngine.Object target, Phase0Log log)
        {
            using (PrefabUtility.EditPrefabContentsScope scope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
            {
                PlayableDirector director = Phase0AssetBuilder.FindDirector(scope.prefabContentsRoot, directorPath);
                director.SetGenericBinding(track, target);
                PrefabUtility.RecordPrefabInstancePropertyModifications(director);
                using (SerializedObject serializedObject = new SerializedObject(director))
                {
                    SerializedProperty value = FindValueProperty(serializedObject, track, log);
                    log.Line($"after SetGenericBinding: prefabOverride={value.prefabOverride} value={Phase0Inspector.DescribeSerializedReference(value)}");
                }
            }
        }

        private static void SetBindingWithSerializedProperty(string prefabPath, string directorPath, TrackAsset track, string targetPath, Phase0Log log)
        {
            using (PrefabUtility.EditPrefabContentsScope scope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
            {
                PlayableDirector director = Phase0AssetBuilder.FindDirector(scope.prefabContentsRoot, directorPath);
                Animator target = Phase0AssetBuilder.FindByIndexPath(scope.prefabContentsRoot, targetPath).GetComponent<Animator>();
                using (SerializedObject serializedObject = new SerializedObject(director))
                {
                    SerializedProperty value = FindValueProperty(serializedObject, track, log);
                    value.objectReferenceValue = target;
                    serializedObject.ApplyModifiedPropertiesWithoutUndo();
                    serializedObject.Update();
                    SerializedProperty afterValue = FindValueProperty(serializedObject, track, log);
                    log.Line($"after SerializedProperty set: prefabOverride={afterValue.prefabOverride} value={Phase0Inspector.DescribeSerializedReference(afterValue)}");
                }
            }
        }

        private static void RevertWholeArray(string prefabPath, string directorPath, TrackAsset track, Phase0Log log)
        {
            using (PrefabUtility.EditPrefabContentsScope scope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
            {
                PlayableDirector director = Phase0AssetBuilder.FindDirector(scope.prefabContentsRoot, directorPath);
                log.Line($"before: Signal binding={Phase0Inspector.ShortDescribe(director.GetGenericBinding(track))}");
                using (SerializedObject serializedObject = new SerializedObject(director))
                {
                    SerializedProperty array = serializedObject.FindProperty(Phase0Inspector.SceneBindingsProperty);
                    PrefabUtility.RevertPropertyOverride(array, InteractionMode.AutomatedAction);
                }

                log.Line($"after: Signal binding={Phase0Inspector.ShortDescribe(director.GetGenericBinding(track))}");
            }
        }

        private static void DumpOverriddenBindingProperties(string prefabPath, string directorPath, Phase0Log log)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            PlayableDirector director = Phase0AssetBuilder.FindDirector(asset, directorPath);
            Phase0Inspector.DumpDirector(director, log, true);
            using (SerializedObject serializedObject = new SerializedObject(director))
            {
                SerializedProperty iterator = serializedObject.FindProperty(Phase0Inspector.SceneBindingsProperty);
                SerializedProperty end = iterator.GetEndProperty();
                bool enterChildren = true;
                while (iterator.Next(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
                {
                    if (iterator.prefabOverride)
                    {
                        log.Line($"overridden property: {iterator.propertyPath} ({iterator.propertyType})");
                    }
                }
            }
        }

        private static void RunSceneUndo(string setName, TrackAsset track, Phase0Log log)
        {
            log.Section("R4 SceneA Parent#1/MiddleP/Child#0 Activation: RevertPropertyOverride with Undo, then Undo");
            string scenePath = Phase0Paths.GetSceneAPath(setName);
            Dictionary<string, string> before = Phase0Inspector.HashFolder(Phase0Paths.GetSetDirectory(setName));
            string trackName = track.name;
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            // OpenScene で UnloadUnusedAssets が走るため Track を再解決する
            track = Phase0AssetBuilder.FindTrack(AssetDatabase.LoadAssetAtPath<TimelineAsset>(Phase0Paths.GetTimelinePath(setName)), trackName);
            GameObject[] roots = scene.GetRootGameObjects();
            PlayableDirector director = Phase0AssetBuilder.FindDirector(roots[1], "MiddleP#0/Child#0/Director");
            log.Line($"director={Phase0Inspector.GetIndexPath(director.transform)} binding(before)={Phase0Inspector.ShortDescribe(director.GetGenericBinding(track))}");
            string siblingBefore = Phase0Inspector.BuildBindingTable(roots[0]);

            using (SerializedObject serializedObject = new SerializedObject(director))
            {
                SerializedProperty value = FindValueProperty(serializedObject, track, log);
                log.Line($"prefabOverride(before)={value.prefabOverride}");
                PrefabUtility.RevertPropertyOverride(value, InteractionMode.UserAction);
            }

            string afterRevert = Phase0Inspector.ShortDescribe(director.GetGenericBinding(track));
            log.Line($"binding(after revert)={afterRevert} sceneDirty={scene.isDirty}");
            log.Check(afterRevert.Contains("ActTarget"), "scene revert -> inherited ActTarget");
            log.Check(scene.isDirty, "scene marked dirty (user saves normally)");

            Undo.PerformUndo();
            string afterUndo = Phase0Inspector.ShortDescribe(director.GetGenericBinding(track));
            log.Line($"binding(after undo)={afterUndo}");
            log.Check(afterUndo.Contains("SceneOnlyTarget"), "undo restores scene override");
            log.Check(siblingBefore == Phase0Inspector.BuildBindingTable(roots[0]), "sibling Parent#0 instance unchanged");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Dictionary<string, string> after = Phase0Inspector.HashFolder(Phase0Paths.GetSetDirectory(setName));
            Phase0Inspector.CompareHashes(before, after, log, "R4 (scene not saved)");
        }

        private static SerializedProperty FindValueProperty(SerializedObject serializedObject, TrackAsset track, Phase0Log log)
        {
            SerializedProperty array = serializedObject.FindProperty(Phase0Inspector.SceneBindingsProperty);
            List<int> matches = new List<int>();
            for (int i = 0; i < array.arraySize; i++)
            {
                SerializedProperty key = array.GetArrayElementAtIndex(i).FindPropertyRelative("key");
                if (key != null && key.objectReferenceValue == track)
                {
                    matches.Add(i);
                }
            }

            if (matches.Count != 1)
            {
                throw new InvalidOperationException($"Expected exactly one m_SceneBindings entry for track '{track.name}', found {matches.Count}.");
            }

            return array.GetArrayElementAtIndex(matches[0]).FindPropertyRelative("value");
        }

        private static Dictionary<string, List<string>> CaptureState(string setName)
        {
            Dictionary<string, List<string>> state = new Dictionary<string, List<string>>();
            AddState(state, "Child", Phase0Paths.GetChildPrefabPath(setName));
            AddState(state, "Middle", Phase0Paths.GetMiddlePrefabPath(setName));
            AddState(state, "Parent", Phase0Paths.GetParentPrefabPath(setName));
            AddState(state, "Variant", Phase0Paths.GetVariantPrefabPath(setName));
            return state;
        }

        private static void AddState(Dictionary<string, List<string>> state, string key, string path)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            string table = Phase0Inspector.BuildBindingTable(asset);
            state[key + TableSuffix] = table.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
            state[key + ".mods"] = Phase0Inspector.BuildModificationList(asset);
        }

        private static int CountChangedLines(List<string> before, List<string> after, Phase0Log log, string label, bool print)
        {
            int count = Math.Abs(before.Count - after.Count);
            int length = Math.Min(before.Count, after.Count);
            for (int i = 0; i < length; i++)
            {
                if (before[i] == after[i])
                {
                    continue;
                }

                count++;
                if (print)
                {
                    log.Line($"  {label} line {i}: '{before[i].Trim()}' -> '{after[i].Trim()}'");
                }
            }

            return count;
        }

        private static void CheckStateEquals(Dictionary<string, List<string>> expected, Dictionary<string, List<string>> actual, Phase0Log log, string label)
        {
            foreach (string key in expected.Keys)
            {
                bool equal = expected[key].SequenceEqual(actual[key]);
                log.Check(equal, $"{label}: {key}");
                if (equal)
                {
                    continue;
                }

                foreach (string line in expected[key].Except(actual[key]))
                {
                    log.Line($"  expected only: {line}");
                }

                foreach (string line in actual[key].Except(expected[key]))
                {
                    log.Line($"  actual only: {line}");
                }
            }
        }

        private static void CopyFiles(string setName, string tag)
        {
            Phase0Inspector.CopyFileToResults(Phase0Paths.GetMiddlePrefabPath(setName), $"restore_{tag}_Middle.prefab.txt");
            Phase0Inspector.CopyFileToResults(Phase0Paths.GetParentPrefabPath(setName), $"restore_{tag}_Parent.prefab.txt");
            Phase0Inspector.CopyFileToResults(Phase0Paths.GetVariantPrefabPath(setName), $"restore_{tag}_ParentVariant.prefab.txt");
        }
    }
}
