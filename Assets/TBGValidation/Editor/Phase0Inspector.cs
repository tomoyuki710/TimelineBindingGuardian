using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace TimelineBindingGuardian.Validation
{
    /// <summary>
    /// Director / バインド / Override 情報を読み取り専用でダンプするヘルパー。
    /// ApplyModifiedProperties や SetDirty は呼ばない。
    /// </summary>
    public static class Phase0Inspector
    {
        public const string SceneBindingsProperty = "m_SceneBindings";

        public static string GetIndexPath(Transform transform)
        {
            if (transform == null)
            {
                return "<null>";
            }

            Stack<string> parts = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                parts.Push(current.parent == null ? current.name : $"{current.name}#{current.GetSiblingIndex()}");
                current = current.parent;
            }

            return string.Join("/", parts);
        }

        public static string GetGlobalId(UnityEngine.Object target)
        {
            if (target == null)
            {
                return "<null>";
            }

            return GlobalObjectId.GetGlobalObjectIdSlow(target).ToString();
        }

        public static string Describe(UnityEngine.Object target)
        {
            if (ReferenceEquals(target, null))
            {
                return "NULL";
            }

            if (target == null)
            {
                return $"DESTROYED/MISSING(instanceID={target.GetInstanceID()})";
            }

            string location;
            if (target is Component component)
            {
                int componentIndex = Array.IndexOf(component.GetComponents(component.GetType()), component);
                location = $"{GetIndexPath(component.transform)} [{component.GetType().Name}#{componentIndex}]";
            }
            else if (target is GameObject gameObject)
            {
                location = $"{GetIndexPath(gameObject.transform)} [GameObject]";
            }
            else
            {
                location = $"{target.name} [{target.GetType().Name}]";
            }

            string assetPath = AssetDatabase.GetAssetPath(target);
            string scenePath = GetScenePath(target);
            string where = string.IsNullOrEmpty(assetPath) ? $"scene='{scenePath}'" : $"asset='{assetPath}'";
            return $"{location} {where} gid={GetGlobalId(target)}";
        }

        public static string DescribeSerializedReference(SerializedProperty property)
        {
#pragma warning disable 618
            int instanceId = property.objectReferenceInstanceIDValue;
#pragma warning restore 618
            UnityEngine.Object value = property.objectReferenceValue;
            string missing = (instanceId != 0 && value == null) ? " MISSING_EVIDENCE" : string.Empty;
            return $"{Describe(value)} (storedInstanceID={instanceId}){missing}";
        }

        public static void DumpDirector(PlayableDirector director, Phase0Log log, bool includeSourceChain)
        {
            log.Line($"--- Director {GetIndexPath(director.transform)} ---");
            log.Indent();
            log.Line($"gid={GetGlobalId(director)}");
            log.Line($"isPartOfPrefabAsset={PrefabUtility.IsPartOfPrefabAsset(director)} isPartOfPrefabInstance={PrefabUtility.IsPartOfPrefabInstance(director)} activeInHierarchy={director.gameObject.activeInHierarchy} enabled={director.enabled}");
            GameObject nearest = PrefabUtility.GetNearestPrefabInstanceRoot(director);
            GameObject outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(director);
            log.Line($"nearestInstanceRoot={(nearest != null ? GetIndexPath(nearest.transform) : "<none>")} outermostInstanceRoot={(outermost != null ? GetIndexPath(outermost.transform) : "<none>")}");

            TimelineAsset timeline = director.playableAsset as TimelineAsset;
            log.Line($"playableAsset={(director.playableAsset != null ? director.playableAsset.GetType().Name + " " + AssetDatabase.GetAssetPath(director.playableAsset) : "NULL")}");

            DumpOutputs(director, timeline, log);
            DumpSceneBindingsArray(director, log);
            DumpExposedReferences(director, log);
            DumpModifications(director, log);

            if (includeSourceChain)
            {
                DumpSourceChain(director, timeline, log);
            }

            log.Unindent();
        }

        public static void DumpOutputs(PlayableDirector director, TimelineAsset timeline, Phase0Log log)
        {
            if (timeline == null)
            {
                log.Line("outputs: (not a TimelineAsset)");
                return;
            }

            log.Line("outputs:");
            log.Indent();
            int index = 0;
            foreach (PlayableBinding binding in timeline.outputs)
            {
                UnityEngine.Object source = binding.sourceObject;
                string fileId = "?";
                if (source != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string _, out long localId))
                {
                    fileId = localId.ToString();
                }

                string targetType = binding.outputTargetType != null ? binding.outputTargetType.Name : "<none>";
                string effective = source != null ? Describe(director.GetGenericBinding(source)) : "<no source>";
                log.Line($"[{index}] stream='{binding.streamName}' source={(source != null ? source.GetType().Name + ":" + source.name : "NULL")} trackFileId={fileId} targetType={targetType} effective={effective}");
                index++;
            }

            log.Unindent();
        }

        public static void DumpSceneBindingsArray(PlayableDirector director, Phase0Log log)
        {
            using (SerializedObject serializedObject = new SerializedObject(director))
            {
                SerializedProperty array = serializedObject.FindProperty(SceneBindingsProperty);
                if (array == null)
                {
                    log.Line($"{SceneBindingsProperty}: NOT FOUND");
                    return;
                }

                SerializedProperty size = serializedObject.FindProperty(SceneBindingsProperty + ".Array.size");
                log.Line($"{SceneBindingsProperty}: isArray={array.isArray} arraySize={array.arraySize} type={array.type} arrayElementType={array.arrayElementType} prefabOverride={array.prefabOverride} sizeOverride={(size != null ? size.prefabOverride.ToString() : "?")}");
                log.Indent();
                HashSet<int> seenKeys = new HashSet<int>();
                for (int i = 0; i < array.arraySize; i++)
                {
                    SerializedProperty element = array.GetArrayElementAtIndex(i);
                    StringBuilder line = new StringBuilder($"[{i}] path={element.propertyPath} override={element.prefabOverride}");
                    SerializedProperty iterator = element.Copy();
                    SerializedProperty end = element.GetEndProperty();
                    bool enterChildren = true;
                    while (iterator.Next(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
                    {
                        enterChildren = false;
                        line.Append($" | {iterator.name}({iterator.propertyType},{iterator.type}) override={iterator.prefabOverride} = ");
                        if (iterator.propertyType == SerializedPropertyType.ObjectReference)
                        {
                            line.Append(DescribeSerializedReference(iterator));
                            if (iterator.name == "key" && iterator.objectReferenceValue != null)
                            {
                                int keyId = iterator.objectReferenceValue.GetInstanceID();
                                if (!seenKeys.Add(keyId))
                                {
                                    line.Append(" DUPLICATE_KEY");
                                }
                            }
                        }
                        else
                        {
                            line.Append(iterator.type);
                        }
                    }

                    log.Line(line.ToString());
                }

                log.Unindent();
            }
        }

        public static void DumpExposedReferences(PlayableDirector director, Phase0Log log)
        {
            using (SerializedObject serializedObject = new SerializedObject(director))
            {
                SerializedProperty exposed = serializedObject.FindProperty("m_ExposedReferences.m_References");
                if (exposed == null || !exposed.isArray)
                {
                    log.Line("m_ExposedReferences.m_References: NOT FOUND");
                    return;
                }

                log.Line($"m_ExposedReferences.m_References: size={exposed.arraySize} (ExposedReference, out of v1 scope)");
            }
        }

        public static void DumpModifications(PlayableDirector director, Phase0Log log)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(director))
            {
                log.Line("modifications: (not part of a prefab instance)");
                return;
            }

            UnityEngine.Object source = PrefabUtility.GetCorrespondingObjectFromSource(director);
            PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(director);
            int total = modifications != null ? modifications.Length : 0;
            log.Line($"GetPropertyModifications: total={total} correspondingSource={Describe(source)}");
            log.Indent();
            if (modifications != null)
            {
                foreach (PropertyModification modification in modifications)
                {
                    if (!modification.propertyPath.StartsWith(SceneBindingsProperty))
                    {
                        continue;
                    }

                    string targetMatch = modification.target == source ? "THIS_DIRECTOR" : "OTHER_TARGET";
                    log.Line($"{targetMatch} target={Describe(modification.target)} path={modification.propertyPath} value='{modification.value}' objRef={Describe(modification.objectReference)}");
                }
            }

            log.Unindent();

            GameObject outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(director);
            if (outermost == null)
            {
                return;
            }

            List<UnityEditor.SceneManagement.ObjectOverride> overrides = PrefabUtility.GetObjectOverrides(outermost, true);
            foreach (UnityEditor.SceneManagement.ObjectOverride objectOverride in overrides)
            {
                if (objectOverride.instanceObject == director)
                {
                    log.Line($"GetObjectOverrides(includeDefault): director has object override (assetObject={Describe(objectOverride.GetAssetObject())})");
                }
            }
        }

        public static void DumpSourceChain(PlayableDirector director, TimelineAsset timeline, Phase0Log log)
        {
            log.Line("source chain (level0 = this context):");
            log.Indent();
            UnityEngine.Object original = PrefabUtility.GetCorrespondingObjectFromOriginalSource(director);
            log.Line($"originalSource={Describe(original)}");

            PlayableDirector current = director;
            int level = 0;
            while (current != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(current);
                log.Line($"L{level} {(string.IsNullOrEmpty(assetPath) ? "scene='" + GetScenePath(current) + "'" : "asset='" + assetPath + "'")} path={GetIndexPath(current.transform)} gid={GetGlobalId(current)}");
                if (timeline != null)
                {
                    log.Indent();
                    PlayableDirector next = PrefabUtility.GetCorrespondingObjectFromSource(current);
                    DumpLevelBindings(current, next, timeline, log);
                    log.Unindent();
                }

                current = PrefabUtility.GetCorrespondingObjectFromSource(current);
                level++;
                if (level > 16)
                {
                    log.Line("source chain too deep, aborted");
                    break;
                }
            }

            log.Unindent();
        }

        /// <summary>
        /// 現在レベルと1つ下の Source レベルの有効値を比較し、このレベルで変化したかを示す。
        /// 参照先は GetCorrespondingObjectFromSource で下位レベルへ正規化して比較する。
        /// </summary>
        public static void DumpLevelBindings(PlayableDirector current, PlayableDirector source, TimelineAsset timeline, Phase0Log log)
        {
            Dictionary<UnityEngine.Object, bool> overrideFlags = GetValueOverrideFlags(current);
            foreach (PlayableBinding binding in timeline.outputs)
            {
                if (binding.sourceObject == null || binding.outputTargetType == null)
                {
                    continue;
                }

                UnityEngine.Object value = current.GetGenericBinding(binding.sourceObject);
                string comparison = "BASE";
                if (source != null)
                {
                    UnityEngine.Object sourceValue = source.GetGenericBinding(binding.sourceObject);
                    UnityEngine.Object normalized = value != null ? PrefabUtility.GetCorrespondingObjectFromSource(value) : null;
                    bool bothNull = value == null && sourceValue == null;
                    bool sameObject = value != null && sourceValue != null && normalized == sourceValue;
                    comparison = (bothNull || sameObject) ? "SAME_AS_SOURCE" : "CHANGED_AT_THIS_LEVEL";
                    if (value != null && sourceValue != null && normalized == null)
                    {
                        comparison = "CHANGED_AT_THIS_LEVEL(target has no source: defined in this context)";
                    }
                }

                string overrideText = overrideFlags.TryGetValue(binding.sourceObject, out bool flag) ? flag.ToString() : "NO_ENTRY";
                log.Line($"{binding.sourceObject.name}: {ShortDescribe(value)} valueOverride={overrideText} {comparison}");
            }
        }

        public static Dictionary<UnityEngine.Object, bool> GetValueOverrideFlags(PlayableDirector director)
        {
            Dictionary<UnityEngine.Object, bool> result = new Dictionary<UnityEngine.Object, bool>();
            using (SerializedObject serializedObject = new SerializedObject(director))
            {
                SerializedProperty array = serializedObject.FindProperty(SceneBindingsProperty);
                if (array == null)
                {
                    return result;
                }

                for (int i = 0; i < array.arraySize; i++)
                {
                    SerializedProperty element = array.GetArrayElementAtIndex(i);
                    SerializedProperty key = element.FindPropertyRelative("key");
                    SerializedProperty value = element.FindPropertyRelative("value");
                    if (key == null || value == null || key.objectReferenceValue == null)
                    {
                        continue;
                    }

                    result[key.objectReferenceValue] = value.prefabOverride;
                }
            }

            return result;
        }

        public static string ShortDescribe(UnityEngine.Object target)
        {
            if (ReferenceEquals(target, null))
            {
                return "NULL";
            }

            if (target == null)
            {
                return "MISSING";
            }

            Transform transform = target is Component component ? component.transform : (target as GameObject)?.transform;
            return transform != null ? $"{GetIndexPath(transform)}[{target.GetType().Name}]" : target.name;
        }

        /// <summary>
        /// 比較用のコンパクトな状態表（Director パス -> 各 Track の状態）。
        /// </summary>
        public static string BuildBindingTable(GameObject root)
        {
            StringBuilder builder = new StringBuilder();
            foreach (PlayableDirector director in root.GetComponentsInChildren<PlayableDirector>(true))
            {
                TimelineAsset timeline = director.playableAsset as TimelineAsset;
                builder.AppendLine($"{GetIndexPath(director.transform)} gid={GetGlobalId(director)}");
                if (timeline == null)
                {
                    continue;
                }

                Dictionary<UnityEngine.Object, bool> flags = GetValueOverrideFlags(director);
                foreach (PlayableBinding binding in timeline.outputs)
                {
                    if (binding.sourceObject == null || binding.outputTargetType == null)
                    {
                        continue;
                    }

                    string overrideText = flags.TryGetValue(binding.sourceObject, out bool flag) ? flag.ToString() : "NO_ENTRY";
                    builder.AppendLine($"  {binding.sourceObject.name}: {ShortDescribe(director.GetGenericBinding(binding.sourceObject))} override={overrideText}");
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// m_SceneBindings に関する PropertyModification を文字列化した一覧（比較用）。
        /// </summary>
        public static List<string> BuildModificationList(GameObject root)
        {
            List<string> result = new List<string>();
            HashSet<GameObject> visitedRoots = new HashSet<GameObject>();
            foreach (PlayableDirector director in root.GetComponentsInChildren<PlayableDirector>(true))
            {
                if (!PrefabUtility.IsPartOfPrefabInstance(director))
                {
                    continue;
                }

                GameObject outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(director);
                if (!visitedRoots.Add(outermost))
                {
                    continue;
                }

                PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(outermost);
                if (modifications == null)
                {
                    continue;
                }

                foreach (PropertyModification modification in modifications)
                {
                    result.Add($"{GetIndexPath(outermost.transform)} :: target={GetGlobalId(modification.target)} path={modification.propertyPath} value='{modification.value}' ref={GetGlobalId(modification.objectReference)}");
                }
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }

        public static Dictionary<string, string> HashFolder(string assetFolder)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            string fullFolder = Path.GetFullPath(assetFolder);
            if (!Directory.Exists(fullFolder))
            {
                return result;
            }

            using (SHA256 sha = SHA256.Create())
            {
                foreach (string file in Directory.GetFiles(fullFolder, "*", SearchOption.AllDirectories))
                {
                    byte[] hash = sha.ComputeHash(File.ReadAllBytes(file));
                    result[file.Substring(fullFolder.Length).Replace('\\', '/')] = BitConverter.ToString(hash).Replace("-", string.Empty);
                }
            }

            return result;
        }

        public static void CompareHashes(Dictionary<string, string> before, Dictionary<string, string> after, Phase0Log log, string label)
        {
            List<string> differences = new List<string>();
            foreach (KeyValuePair<string, string> pair in before)
            {
                if (!after.TryGetValue(pair.Key, out string value))
                {
                    differences.Add($"removed {pair.Key}");
                }
                else if (value != pair.Value)
                {
                    differences.Add($"changed {pair.Key}");
                }
            }

            foreach (string key in after.Keys)
            {
                if (!before.ContainsKey(key))
                {
                    differences.Add($"added {key}");
                }
            }

            foreach (string difference in differences)
            {
                log.Line($"  hash diff: {difference}");
            }

            log.Check(differences.Count == 0, $"{label}: files unchanged ({before.Count} files)");
        }

        public static void DumpYamlExcerpts(string assetPath, Phase0Log log, int contextLines)
        {
            string fullPath = Path.GetFullPath(assetPath);
            string[] lines = File.ReadAllLines(fullPath);
            log.Line($"--- YAML excerpts: {assetPath} ---");
            int lastPrinted = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains(SceneBindingsProperty))
                {
                    continue;
                }

                int start = Math.Max(lastPrinted + 1, i - 2);
                int end = Math.Min(lines.Length - 1, i + contextLines);
                if (start > lastPrinted + 1)
                {
                    log.Line("    ...");
                }

                for (int j = start; j <= end; j++)
                {
                    log.Line($"    {j + 1,5}: {lines[j]}");
                }

                lastPrinted = end;
            }
        }

        public static void CopyFileToResults(string assetPath, string resultName)
        {
            Directory.CreateDirectory(Phase0Paths.ResultsDirectory);
            File.Copy(Path.GetFullPath(assetPath), Path.Combine(Phase0Paths.ResultsDirectory, resultName), true);
        }

        private static string GetScenePath(UnityEngine.Object target)
        {
            GameObject gameObject = target as GameObject;
            if (gameObject == null && target is Component component)
            {
                gameObject = component.gameObject;
            }

            if (gameObject == null)
            {
                return string.Empty;
            }

            UnityEngine.SceneManagement.Scene scene = gameObject.scene;
            if (!scene.IsValid())
            {
                return "<no scene>";
            }

            bool isPreview = EditorSceneManager.IsPreviewScene(scene);
            return isPreview ? $"<preview:{scene.name}>" : scene.path;
        }
    }
}
