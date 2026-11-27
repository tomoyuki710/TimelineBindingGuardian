using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace TimelineBindingGuardian.Validation
{
    /// <summary>
    /// 仕様書 4.1 の検証アセットを生成する。
    /// 既存のセットを上書き・削除しないため、同名セットが存在する場合は失敗する。
    ///
    /// 構成:
    ///   Child.prefab   : Child/Director(PlayableDirector, SignalReceiver), ActorA, ActorB(Animator),
    ///                    Speaker(AudioSource x2: 2番目にバインド), ActTarget
    ///                    Timeline: AnimA, AnimB, Activation, Audio, Signal, Group(GroupedActivation), Control
    ///   Middle.prefab  : Child#0(ChildX), Child#1(ChildY, 同名), AltActor(Animator)
    ///                    ChildY: Activation=NULL Override, AnimA=AltActor Override
    ///   Parent.prefab  : MiddleP#0, MiddleQ#1
    ///                    MiddleQ/Child#0: Audio=NULL Override
    ///                    MiddleQ/Child#1: AnimB=MiddleP/Child#0/ActorB（別Prefabインスタンスへのバインド）
    ///   ParentVariant  : Parent の Variant
    ///                    MiddleP/Child#0: AnimA=NULL (SetGenericBinding null)
    ///                    MiddleP/Child#1: Signal=ClearGenericBinding
    ///   SceneA.unity   : Parent#0, Parent#1(Scene Override: MiddleP/Child#0 Activation=SceneOnlyTarget),
    ///                    SceneOnlyTarget, SceneActor(Animator), SceneDirector(単独、AnimAのみバインド)
    ///   SceneB.unity   : Parent, ParentVariant
    /// </summary>
    public static class Phase0AssetBuilder
    {
        public const string TrackAnimA = "AnimA";
        public const string TrackAnimB = "AnimB";
        public const string TrackActivation = "Activation";
        public const string TrackAudio = "Audio";
        public const string TrackSignal = "Signal";
        public const string TrackGroup = "Group";
        public const string TrackGroupedActivation = "GroupedActivation";
        public const string TrackControl = "Control";

        public static void Build(string setName, Phase0Log log)
        {
            if (Phase0Paths.SetExists(setName))
            {
                throw new InvalidOperationException(
                    $"Set '{setName}' already exists. Delete {Phase0Paths.GetSetDirectory(setName)} manually or use another set name.");
            }

            EnsureFolder(Phase0Paths.ValidationRoot, "Generated");
            EnsureFolder(Phase0Paths.GeneratedRoot, Phase0Paths.RunName);
            EnsureFolder(Phase0Paths.GeneratedRoot + "/" + Phase0Paths.RunName, setName);

            log.Section($"Build set: {setName}");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            TimelineAsset timeline = CreateTimeline(Phase0Paths.GetTimelinePath(setName));
            GameObject child = CreateChildPrefab(Phase0Paths.GetChildPrefabPath(setName), timeline, log);
            GameObject middle = CreateMiddlePrefab(Phase0Paths.GetMiddlePrefabPath(setName), child, timeline, log);
            GameObject parent = CreateParentPrefab(Phase0Paths.GetParentPrefabPath(setName), middle, timeline, log);
            GameObject variant = CreateParentVariant(Phase0Paths.GetVariantPrefabPath(setName), parent, timeline, log);
            CreateSceneA(Phase0Paths.GetSceneAPath(setName), parent, timeline, log);
            CreateSceneB(Phase0Paths.GetSceneBPath(setName), parent, variant, log);

            AssetDatabase.SaveAssets();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            log.Line("Build completed.");
        }

        /// <summary>
        /// Track をアセットパスから再解決する。NewScene 等で UnloadUnusedAssets が走ると
        /// 保持していた Track の C# 参照が破棄済みになるため、毎回ロードし直す。
        /// </summary>
        public static TrackAsset FindTrack(TimelineAsset timeline, string trackName)
        {
            string path = AssetDatabase.GetAssetPath(timeline);
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is TrackAsset track && track.name == trackName)
                {
                    return track;
                }
            }

            return null;
        }

        /// <summary>
        /// "Parent/MiddleQ#1/Child#0/Director" のように「名前#兄弟インデックス」で辿る（検証コード専用）。
        /// ルート要素は名前のみ。
        /// </summary>
        public static Transform FindByIndexPath(GameObject root, string relativeIndexPath)
        {
            Transform current = root.transform;
            if (string.IsNullOrEmpty(relativeIndexPath))
            {
                return current;
            }

            foreach (string token in relativeIndexPath.Split('/'))
            {
                int hash = token.LastIndexOf('#');
                if (hash < 0)
                {
                    current = current.Find(token);
                }
                else
                {
                    int index = int.Parse(token.Substring(hash + 1));
                    current = index < current.childCount ? current.GetChild(index) : null;
                }

                if (current == null)
                {
                    return null;
                }
            }

            return current;
        }

        public static PlayableDirector FindDirector(GameObject root, string relativeIndexPath)
        {
            Transform transform = FindByIndexPath(root, relativeIndexPath);
            return transform != null ? transform.GetComponent<PlayableDirector>() : null;
        }

        private static void EnsureFolder(string parent, string name)
        {
            string path = $"{parent}/{name}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }

        private static TimelineAsset CreateTimeline(string path)
        {
            TimelineAsset timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, path);

            timeline.CreateTrack<AnimationTrack>(null, TrackAnimA);
            timeline.CreateTrack<AnimationTrack>(null, TrackAnimB);
            timeline.CreateTrack<ActivationTrack>(null, TrackActivation);
            timeline.CreateTrack<AudioTrack>(null, TrackAudio);
            timeline.CreateTrack<SignalTrack>(null, TrackSignal);
            GroupTrack group = timeline.CreateTrack<GroupTrack>(null, TrackGroup);
            timeline.CreateTrack<ActivationTrack>(group, TrackGroupedActivation);
            ControlTrack control = timeline.CreateTrack<ControlTrack>(null, TrackControl);
            TimelineClip clip = control.CreateClip<ControlPlayableAsset>();
            ControlPlayableAsset controlAsset = (ControlPlayableAsset)clip.asset;
            controlAsset.sourceGameObject.exposedName = "TBG_ControlSource";

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            return timeline;
        }

        private static GameObject CreateChildPrefab(string path, TimelineAsset timeline, Phase0Log log)
        {
            GameObject root = new GameObject("Child");
            GameObject directorObject = CreateChild(root, "Director");
            PlayableDirector director = directorObject.AddComponent<PlayableDirector>();
            SignalReceiver receiver = directorObject.AddComponent<SignalReceiver>();
            director.playOnAwake = false;
            director.playableAsset = timeline;

            Animator actorA = CreateChild(root, "ActorA").AddComponent<Animator>();
            Animator actorB = CreateChild(root, "ActorB").AddComponent<Animator>();
            GameObject speaker = CreateChild(root, "Speaker");
            speaker.AddComponent<AudioSource>();
            AudioSource secondAudio = speaker.AddComponent<AudioSource>();
            GameObject actTarget = CreateChild(root, "ActTarget");

            director.SetGenericBinding(FindTrack(timeline, TrackAnimA), actorA);
            director.SetGenericBinding(FindTrack(timeline, TrackAnimB), actorB);
            director.SetGenericBinding(FindTrack(timeline, TrackActivation), actTarget);
            director.SetGenericBinding(FindTrack(timeline, TrackAudio), secondAudio);
            director.SetGenericBinding(FindTrack(timeline, TrackSignal), receiver);
            director.SetGenericBinding(FindTrack(timeline, TrackGroupedActivation), actTarget);
            director.SetReferenceValue("TBG_ControlSource", actTarget);

            GameObject asset = SavePrefab(root, path, log);
            UnityEngine.Object.DestroyImmediate(root);
            return asset;
        }

        private static GameObject CreateMiddlePrefab(string path, GameObject child, TimelineAsset timeline, Phase0Log log)
        {
            GameObject root = new GameObject("Middle");
            GameObject childX = InstantiateUnder(child, root);
            GameObject childY = InstantiateUnder(child, root);
            Animator altActor = CreateChild(root, "AltActor").AddComponent<Animator>();

            log.Line($"Middle: ChildX name='{childX.name}', ChildY name='{childY.name}' (same name expected)");

            PlayableDirector directorY = FindDirector(childY, "Director");
            directorY.SetGenericBinding(FindTrack(timeline, TrackActivation), null);
            directorY.SetGenericBinding(FindTrack(timeline, TrackAnimA), altActor);
            PrefabUtility.RecordPrefabInstancePropertyModifications(directorY);
            LogModificationCount("Middle/ChildY", directorY, log);

            GameObject asset = SavePrefab(root, path, log);
            UnityEngine.Object.DestroyImmediate(root);
            return asset;
        }

        private static GameObject CreateParentPrefab(string path, GameObject middle, TimelineAsset timeline, Phase0Log log)
        {
            GameObject root = new GameObject("Parent");
            GameObject middleP = InstantiateUnder(middle, root);
            middleP.name = "MiddleP";
            GameObject middleQ = InstantiateUnder(middle, root);
            middleQ.name = "MiddleQ";

            PlayableDirector qChildX = FindDirector(middleQ, "Child#0/Director");
            qChildX.SetGenericBinding(FindTrack(timeline, TrackAudio), null);
            PrefabUtility.RecordPrefabInstancePropertyModifications(qChildX);
            LogModificationCount("Parent/MiddleQ/Child#0", qChildX, log);

            PlayableDirector qChildY = FindDirector(middleQ, "Child#1/Director");
            Animator crossTarget = FindByIndexPath(middleP, "Child#0/ActorB").GetComponent<Animator>();
            qChildY.SetGenericBinding(FindTrack(timeline, TrackAnimB), crossTarget);
            PrefabUtility.RecordPrefabInstancePropertyModifications(qChildY);
            LogModificationCount("Parent/MiddleQ/Child#1", qChildY, log);

            GameObject asset = SavePrefab(root, path, log);
            UnityEngine.Object.DestroyImmediate(root);
            return asset;
        }

        private static GameObject CreateParentVariant(string path, GameObject parent, TimelineAsset timeline, Phase0Log log)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(parent);

            PlayableDirector pChildX = FindDirector(instance, "MiddleP#0/Child#0/Director");
            pChildX.SetGenericBinding(FindTrack(timeline, TrackAnimA), null);
            PrefabUtility.RecordPrefabInstancePropertyModifications(pChildX);
            LogModificationCount("Variant/MiddleP/Child#0", pChildX, log);

            PlayableDirector pChildY = FindDirector(instance, "MiddleP#0/Child#1/Director");
            pChildY.ClearGenericBinding(FindTrack(timeline, TrackSignal));
            PrefabUtility.RecordPrefabInstancePropertyModifications(pChildY);
            LogModificationCount("Variant/MiddleP/Child#1", pChildY, log);

            GameObject asset = SavePrefab(instance, path, log);
            log.Line($"Variant asset type: {PrefabUtility.GetPrefabAssetType(asset)}");
            UnityEngine.Object.DestroyImmediate(instance);
            return asset;
        }

        private static void CreateSceneA(string path, GameObject parent, TimelineAsset timeline, Phase0Log log)
        {
            string timelinePath = AssetDatabase.GetAssetPath(timeline);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(timelinePath);
            PrefabUtility.InstantiatePrefab(parent, scene);
            GameObject parent2 = (GameObject)PrefabUtility.InstantiatePrefab(parent, scene);
            GameObject sceneOnlyTarget = new GameObject("SceneOnlyTarget");
            Animator sceneActor = new GameObject("SceneActor").AddComponent<Animator>();

            PlayableDirector overridden = FindDirector(parent2, "MiddleP#0/Child#0/Director");
            overridden.SetGenericBinding(FindTrack(timeline, TrackActivation), sceneOnlyTarget);
            PrefabUtility.RecordPrefabInstancePropertyModifications(overridden);
            LogModificationCount("SceneA/Parent#1/MiddleP/Child#0", overridden, log);

            GameObject standalone = new GameObject("SceneDirector");
            PlayableDirector director = standalone.AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.playableAsset = timeline;
            director.SetGenericBinding(FindTrack(timeline, TrackAnimA), sceneActor);

            bool saved = EditorSceneManager.SaveScene(scene, path);
            log.Line($"Saved scene {path}: {saved}");
        }

        private static void CreateSceneB(string path, GameObject parent, GameObject variant, Phase0Log log)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PrefabUtility.InstantiatePrefab(parent, scene);
            PrefabUtility.InstantiatePrefab(variant, scene);
            bool saved = EditorSceneManager.SaveScene(scene, path);
            log.Line($"Saved scene {path}: {saved}");
        }

        private static GameObject CreateChild(GameObject parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        private static GameObject InstantiateUnder(GameObject prefab, GameObject parent)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetParent(parent.transform, false);
            return instance;
        }

        private static GameObject SavePrefab(GameObject root, string path, Phase0Log log)
        {
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            log.Line($"Saved prefab {path}: {success}");
            if (!success)
            {
                throw new InvalidOperationException($"Failed to save prefab: {path}");
            }

            return asset;
        }

        private static void LogModificationCount(string label, PlayableDirector director, Phase0Log log)
        {
            PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(director);
            int bindingCount = 0;
            if (modifications != null)
            {
                foreach (PropertyModification modification in modifications)
                {
                    if (modification.propertyPath.StartsWith("m_SceneBindings"))
                    {
                        bindingCount++;
                        log.Line($"  {label}: mod {modification.propertyPath} value='{modification.value}' ref={DescribeShort(modification.objectReference)}");
                    }
                }
            }

            log.Line($"{label}: m_SceneBindings modifications={bindingCount}");
        }

        private static string DescribeShort(UnityEngine.Object target)
        {
            return target == null ? "null" : $"{target.GetType().Name}:{target.name}";
        }
    }
}
