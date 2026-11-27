using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace TimelineBindingGuardian.Validation
{
    /// <summary>
    /// 制御された状態変化（Track 追加/並べ替え/削除、参照先削除）に対する
    /// m_SceneBindings と Override propertyPath の対応を記録する。対象は "Mutation" セットのみ。
    /// </summary>
    public static class Phase0MutationProbe
    {
        public static void Run(string setName, Phase0Log log)
        {
            string timelinePath = Phase0Paths.GetTimelinePath(setName);
            TimelineAsset timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(timelinePath);

            Snapshot(setName, "M0 baseline", log);

            log.Section("M1 add track 'AnimNew' (appended)");
            timeline.CreateTrack<AnimationTrack>(null, "AnimNew");
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            Snapshot(setName, "M1 after add", log);

            log.Section("M2 reorder root tracks: move m_Tracks[0] (AnimA) to index 3");
            using (SerializedObject serializedTimeline = new SerializedObject(timeline))
            {
                SerializedProperty tracks = serializedTimeline.FindProperty("m_Tracks");
                log.Line($"m_Tracks found={tracks != null} size={(tracks != null ? tracks.arraySize : -1)}");
                if (tracks != null)
                {
                    tracks.MoveArrayElement(0, 3);
                    serializedTimeline.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            Snapshot(setName, "M2 after reorder", log);

            log.Section("M3 delete binding target: Child/ActorB GameObject");
            GameObject childContents = PrefabUtility.LoadPrefabContents(Phase0Paths.GetChildPrefabPath(setName));
            try
            {
                Transform actorB = childContents.transform.Find("ActorB");
                log.Line($"ActorB found={actorB != null}");
                if (actorB != null)
                {
                    Object.DestroyImmediate(actorB.gameObject);
                }

                PrefabUtility.SaveAsPrefabAsset(childContents, Phase0Paths.GetChildPrefabPath(setName));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(childContents);
            }

            Snapshot(setName, "M3 after ActorB deleted", log);

            log.Section("M4 delete track 'Audio'");
            timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(timelinePath);
            TrackAsset audio = Phase0AssetBuilder.FindTrack(timeline, Phase0AssetBuilder.TrackAudio);
            bool deleted = timeline.DeleteTrack(audio);
            log.Line($"DeleteTrack returned {deleted}");
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            Snapshot(setName, "M4 after Audio track deleted", log);
        }

        private static void Snapshot(string setName, string label, Phase0Log log)
        {
            log.Section($"Snapshot: {label}");
            TimelineAsset timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(Phase0Paths.GetTimelinePath(setName));
            log.Line("timeline root tracks:");
            log.Indent();
            int index = 0;
            foreach (TrackAsset track in timeline.GetRootTracks())
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(track, out string _, out long localId);
                log.Line($"[{index}] {track.name} ({track.GetType().Name}) fileId={localId}");
                index++;
            }

            log.Unindent();

            string[] paths =
            {
                Phase0Paths.GetChildPrefabPath(setName),
                Phase0Paths.GetMiddlePrefabPath(setName),
                Phase0Paths.GetParentPrefabPath(setName),
            };

            foreach (string path in paths)
            {
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                log.Line($"## {path}");
                log.Indent();
                foreach (UnityEngine.Playables.PlayableDirector director in asset.GetComponentsInChildren<UnityEngine.Playables.PlayableDirector>(true))
                {
                    log.Line($"Director {Phase0Inspector.GetIndexPath(director.transform)}");
                    log.Indent();
                    Phase0Inspector.DumpOutputs(director, timeline, log);
                    Phase0Inspector.DumpSceneBindingsArray(director, log);
                    log.Unindent();
                }

                foreach (string modification in Phase0Inspector.BuildModificationList(asset))
                {
                    if (modification.Contains(Phase0Inspector.SceneBindingsProperty))
                    {
                        log.Line("mod: " + modification);
                    }
                }

                log.Unindent();
                Phase0Inspector.DumpYamlExcerpts(path, log, 12);
            }
        }
    }
}
