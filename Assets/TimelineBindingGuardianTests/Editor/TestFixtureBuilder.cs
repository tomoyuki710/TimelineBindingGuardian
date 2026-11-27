using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// テスト用の Nested Prefab / Variant / Scene を Assets/TBGTestFixtures/v{Version}/ に生成する。
    /// 生成済み（完了印あり）なら再利用し、削除・上書きはしない。構成を変えたら Version を上げる。
    ///
    /// 構成（Phase 0 検証アセットと同じ + 追加ケース）:
    ///   Child   : Director(PlayableDirector, SignalReceiver), ActorA, ActorB, Speaker(AudioSource x2: 2番目にバインド), ActTarget
    ///             Timeline: AnimA, AnimB, Activation, Audio, Signal, Group > GroupedActivation, Control
    ///   Middle  : Child#0, Child#1（同名）, AltActor。Child#1: Activation=NULL, AnimA=AltActor
    ///   Parent  : MiddleP#0, MiddleQ#1。MiddleQ/Child#0: Audio=NULL、MiddleQ/Child#1: AnimB=MiddleP/Child#0/ActorB
    ///   Variant : MiddleP/Child#0: AnimA=SetGenericBinding(null)、MiddleP/Child#1: Signal=ClearGenericBinding
    ///   SceneA  : Parent#0, Parent#1（同名）, SceneOnlyTarget, SceneActor, SceneDirector, InactiveDirector, DisabledDirector
    ///             Parent#1: MiddleP/Child#0 Activation=SceneOnlyTarget、MiddleP/Child#1 Activation=SceneOnlyTarget（Middle の NULL を上書き）
    ///   SceneB  : Parent, ParentVariant
    ///   Stale   : StaleTimeline（KeepTrack, RemoveTrack）を使う Director。生成後に RemoveTrack を削除して stale key を作る
    /// </summary>
    internal static class TestFixtureBuilder
    {
        public const int Version = 1;
        public const string Root = "Assets/TBGTestFixtures";

        public const string TrackAnimA = "AnimA";
        public const string TrackAnimB = "AnimB";
        public const string TrackActivation = "Activation";
        public const string TrackAudio = "Audio";
        public const string TrackSignal = "Signal";
        public const string TrackGroup = "Group";
        public const string TrackGroupedActivation = "GroupedActivation";
        public const string TrackControl = "Control";
        public const string TrackKeep = "KeepTrack";
        public const string TrackRemove = "RemoveTrack";

        // 手動の画面確認用（自由に編集してよい）。テストはこのフォルダを使わない
        public const string PlaygroundFolderName = "Playground";

        private const string CompleteMarker = "fixture_complete.txt";

        private static string _folderNameOverride;

        public static string Folder => $"{Root}/{FolderName}";

        private static string FolderName => _folderNameOverride ?? $"v{Version}";
        public static string TimelinePath => $"{Folder}/ChildTimeline.playable";
        public static string StaleTimelinePath => $"{Folder}/StaleTimeline.playable";
        public static string ChildPath => $"{Folder}/Child.prefab";
        public static string MiddlePath => $"{Folder}/Middle.prefab";
        public static string ParentPath => $"{Folder}/Parent.prefab";
        public static string VariantPath => $"{Folder}/ParentVariant.prefab";
        public static string StalePath => $"{Folder}/Stale.prefab";
        public static string SceneAPath => $"{Folder}/SceneA.unity";
        public static string SceneBPath => $"{Folder}/SceneB.unity";

        public static void EnsureBuilt()
        {
            string markerPath = Path.GetFullPath($"{Folder}/{CompleteMarker}");
            if (File.Exists(markerPath))
            {
                return;
            }

            if (AssetDatabase.IsValidFolder(Folder))
            {
                throw new InvalidOperationException(
                    $"Test fixture folder '{Folder}' exists but is incomplete. Delete it manually and run the tests again.");
            }

            Build();
            File.WriteAllText(markerPath, $"Timeline Binding Guardian test fixtures v{Version}. Safe to delete.");
            AssetDatabase.ImportAsset($"{Folder}/{CompleteMarker}");
        }

        /// <summary>
        /// 手動確認用の素材を Assets/TBGTestFixtures/Playground/ に生成する（既にあれば何もしない）。生成先のパスを返す。
        /// </summary>
        public static string BuildPlayground()
        {
            return BuildNamedSet(PlaygroundFolderName);
        }

        /// <summary>
        /// 書き換えを伴うテスト（復元）用の素材を Assets/TBGTestFixtures/Mutable/ に生成する（既にあれば何もしない）。
        /// テストは「変更して元に戻す」往復で初期状態を保つ。
        /// </summary>
        public static string BuildMutable()
        {
            return BuildNamedSet(MutableFolderName);
        }

        public const string MutableFolderName = "Mutable";

        public static string PathIn(string folderName, string fileName)
        {
            return $"{Root}/{folderName}/{fileName}";
        }

        private static string BuildNamedSet(string folderName)
        {
            string folder = $"{Root}/{folderName}";
            if (AssetDatabase.IsValidFolder(folder))
            {
                return folder;
            }

            _folderNameOverride = folderName;
            try
            {
                Build();
            }
            finally
            {
                _folderNameOverride = null;
            }

            return folder;
        }

        public static TrackAsset FindTrack(string timelinePath, string trackName)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(timelinePath))
            {
                if (asset is TrackAsset track && track.name == trackName)
                {
                    return track;
                }
            }

            return null;
        }

        private static void Build()
        {
            if (!AssetDatabase.IsValidFolder(Root))
            {
                AssetDatabase.CreateFolder("Assets", "TBGTestFixtures");
            }

            AssetDatabase.CreateFolder(Root, FolderName);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateTimeline();
            GameObject child = CreateChildPrefab();
            GameObject middle = CreateMiddlePrefab(child);
            GameObject parent = CreateParentPrefab(middle);
            GameObject variant = CreateVariant(parent);
            CreateSceneA(parent);
            CreateSceneB(parent, variant);
            CreateStalePrefab();

            AssetDatabase.SaveAssets();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static void CreateTimeline()
        {
            TimelineAsset timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);
            timeline.CreateTrack<AnimationTrack>(null, TrackAnimA);
            timeline.CreateTrack<AnimationTrack>(null, TrackAnimB);
            timeline.CreateTrack<ActivationTrack>(null, TrackActivation);
            timeline.CreateTrack<AudioTrack>(null, TrackAudio);
            timeline.CreateTrack<SignalTrack>(null, TrackSignal);
            GroupTrack group = timeline.CreateTrack<GroupTrack>(null, TrackGroup);
            timeline.CreateTrack<ActivationTrack>(group, TrackGroupedActivation);
            ControlTrack control = timeline.CreateTrack<ControlTrack>(null, TrackControl);
            ControlPlayableAsset controlAsset = (ControlPlayableAsset)control.CreateClip<ControlPlayableAsset>().asset;
            controlAsset.sourceGameObject.exposedName = "TBG_ControlSource";
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
        }

        private static GameObject CreateChildPrefab()
        {
            GameObject root = new GameObject("Child");
            GameObject directorObject = CreateChild(root, "Director");
            PlayableDirector director = directorObject.AddComponent<PlayableDirector>();
            SignalReceiver receiver = directorObject.AddComponent<SignalReceiver>();
            director.playOnAwake = false;
            director.playableAsset = LoadTimeline();

            Animator actorA = CreateChild(root, "ActorA").AddComponent<Animator>();
            Animator actorB = CreateChild(root, "ActorB").AddComponent<Animator>();
            GameObject speaker = CreateChild(root, "Speaker");
            speaker.AddComponent<AudioSource>();
            AudioSource secondAudio = speaker.AddComponent<AudioSource>();
            GameObject actTarget = CreateChild(root, "ActTarget");

            Bind(director, TrackAnimA, actorA);
            Bind(director, TrackAnimB, actorB);
            Bind(director, TrackActivation, actTarget);
            Bind(director, TrackAudio, secondAudio);
            Bind(director, TrackSignal, receiver);
            Bind(director, TrackGroupedActivation, actTarget);
            director.SetReferenceValue("TBG_ControlSource", actTarget);

            return SaveAndDestroy(root, ChildPath);
        }

        private static GameObject CreateMiddlePrefab(GameObject child)
        {
            GameObject root = new GameObject("Middle");
            InstantiateUnder(child, root);
            GameObject childY = InstantiateUnder(child, root);
            Animator altActor = CreateChild(root, "AltActor").AddComponent<Animator>();

            PlayableDirector director = FindDirector(childY, "Director");
            Bind(director, TrackActivation, null);
            Bind(director, TrackAnimA, altActor);
            PrefabUtility.RecordPrefabInstancePropertyModifications(director);

            return SaveAndDestroy(root, MiddlePath);
        }

        private static GameObject CreateParentPrefab(GameObject middle)
        {
            GameObject root = new GameObject("Parent");
            GameObject middleP = InstantiateUnder(middle, root);
            middleP.name = "MiddleP";
            GameObject middleQ = InstantiateUnder(middle, root);
            middleQ.name = "MiddleQ";

            PlayableDirector qChildX = FindDirector(middleQ, "Child#0/Director");
            Bind(qChildX, TrackAudio, null);
            PrefabUtility.RecordPrefabInstancePropertyModifications(qChildX);

            PlayableDirector qChildY = FindDirector(middleQ, "Child#1/Director");
            Animator crossTarget = FindByIndexPath(middleP, "Child#0/ActorB").GetComponent<Animator>();
            Bind(qChildY, TrackAnimB, crossTarget);
            PrefabUtility.RecordPrefabInstancePropertyModifications(qChildY);

            return SaveAndDestroy(root, ParentPath);
        }

        private static GameObject CreateVariant(GameObject parent)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(parent);

            PlayableDirector pChildX = FindDirector(instance, "MiddleP#0/Child#0/Director");
            Bind(pChildX, TrackAnimA, null);
            PrefabUtility.RecordPrefabInstancePropertyModifications(pChildX);

            PlayableDirector pChildY = FindDirector(instance, "MiddleP#0/Child#1/Director");
            pChildY.ClearGenericBinding(LoadTrack(TrackSignal));
            PrefabUtility.RecordPrefabInstancePropertyModifications(pChildY);

            return SaveAndDestroy(instance, VariantPath);
        }

        private static void CreateSceneA(GameObject parent)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PrefabUtility.InstantiatePrefab(parent, scene);
            GameObject parent2 = (GameObject)PrefabUtility.InstantiatePrefab(parent, scene);
            GameObject sceneOnlyTarget = new GameObject("SceneOnlyTarget");
            Animator sceneActor = new GameObject("SceneActor").AddComponent<Animator>();

            PlayableDirector overridden = FindDirector(parent2, "MiddleP#0/Child#0/Director");
            Bind(overridden, TrackActivation, sceneOnlyTarget);
            PrefabUtility.RecordPrefabInstancePropertyModifications(overridden);

            PlayableDirector shadowing = FindDirector(parent2, "MiddleP#0/Child#1/Director");
            Bind(shadowing, TrackActivation, sceneOnlyTarget);
            PrefabUtility.RecordPrefabInstancePropertyModifications(shadowing);

            CreateSceneDirector("SceneDirector", sceneActor);
            CreateSceneDirector("InactiveDirector", sceneActor).gameObject.SetActive(false);
            CreateSceneDirector("DisabledDirector", sceneActor).enabled = false;

            EditorSceneManager.SaveScene(scene, SceneAPath);
        }

        private static void CreateSceneB(GameObject parent, GameObject variant)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PrefabUtility.InstantiatePrefab(parent, scene);
            PrefabUtility.InstantiatePrefab(variant, scene);
            EditorSceneManager.SaveScene(scene, SceneBPath);
        }

        private static void CreateStalePrefab()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            TimelineAsset timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, StaleTimelinePath);
            timeline.CreateTrack<AnimationTrack>(null, TrackKeep);
            timeline.CreateTrack<AnimationTrack>(null, TrackRemove);
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();

            GameObject root = new GameObject("Stale");
            PlayableDirector director = root.AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.playableAsset = timeline;
            Animator actor = CreateChild(root, "Actor").AddComponent<Animator>();
            director.SetGenericBinding(FindTrack(StaleTimelinePath, TrackKeep), actor);
            director.SetGenericBinding(FindTrack(StaleTimelinePath, TrackRemove), actor);
            SaveAndDestroy(root, StalePath);

            // Prefab 保存後に Track を削除し、Prefab 側に stale key を残す（Prefab は再保存しない）
            TimelineAsset reloaded = AssetDatabase.LoadAssetAtPath<TimelineAsset>(StaleTimelinePath);
            reloaded.DeleteTrack(FindTrack(StaleTimelinePath, TrackRemove));
            EditorUtility.SetDirty(reloaded);
            AssetDatabase.SaveAssets();
        }

        private static PlayableDirector CreateSceneDirector(string name, Animator target)
        {
            PlayableDirector director = new GameObject(name).AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.playableAsset = LoadTimeline();
            Bind(director, TrackAnimA, target);
            return director;
        }

        private static void Bind(PlayableDirector director, string trackName, Object target)
        {
            director.SetGenericBinding(LoadTrack(trackName), target);
        }

        // NewScene で UnloadUnusedAssets が走ると保持していた参照が破棄されるため、毎回パスから解決する
        private static TimelineAsset LoadTimeline()
        {
            return AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
        }

        private static TrackAsset LoadTrack(string trackName)
        {
            return FindTrack(TimelinePath, trackName);
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

        private static GameObject SaveAndDestroy(GameObject root, string path)
        {
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            Object.DestroyImmediate(root);
            if (!success)
            {
                throw new InvalidOperationException($"Failed to save prefab: {path}");
            }

            return asset;
        }

        private static PlayableDirector FindDirector(GameObject root, string indexPath)
        {
            return FindByIndexPath(root, indexPath).GetComponent<PlayableDirector>();
        }

        /// <summary>
        /// "MiddleP#0/Child#1/Director" のように「名前#兄弟インデックス」で辿る（#が無い要素は名前検索）。生成専用。
        /// </summary>
        private static Transform FindByIndexPath(GameObject root, string indexPath)
        {
            Transform current = root.transform;
            foreach (string token in indexPath.Split('/'))
            {
                int hash = token.LastIndexOf('#');
                current = hash < 0 ? current.Find(token) : current.GetChild(int.Parse(token.Substring(hash + 1)));
                if (current == null)
                {
                    throw new InvalidOperationException($"Path not found: {indexPath}");
                }
            }

            return current;
        }
    }
}
