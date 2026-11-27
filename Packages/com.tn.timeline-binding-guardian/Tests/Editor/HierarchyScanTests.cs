using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 階層検査の統合テスト（仕様 13章 T07, T08, T09, T11, T25 ほか）。
    /// テスト素材は TestFixtureBuilder が Assets/TBGTestFixtures/ に生成・再利用する。
    /// </summary>
    public sealed class HierarchyScanTests
    {
        private static readonly string[] BindableTrackNames =
        {
            TestFixtureBuilder.TrackAnimA,
            TestFixtureBuilder.TrackAnimB,
            TestFixtureBuilder.TrackActivation,
            TestFixtureBuilder.TrackAudio,
            TestFixtureBuilder.TrackSignal,
            TestFixtureBuilder.TrackGroupedActivation,
        };

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            TestFixtureBuilder.EnsureBuilt();
        }

        [Test]
        public void T07_ParentPrefab_MiddleNullOverride_OriginIsMiddle()
        {
            ScanResult result = new SceneBindingScanner().ScanPrefabAsset(TestFixtureBuilder.ParentPath);

            TrackBindingObservation track = FindTrack(result, "Parent#0/MiddleP#0/Child#1/Director#0", TestFixtureBuilder.TrackActivation);

            Assert.That(track.Levels.Count, Is.EqualTo(3));
            Assert.That(track.Effective.ValueState, Is.EqualTo(ValueState.Null));
            Assert.That(track.Effective.LocalState, Is.EqualTo(LocalState.Inherited));
            Assert.That(track.OriginKind, Is.EqualTo(OriginKind.Determined));
            Assert.That(track.Origin.Owner.AssetPathHint, Is.EqualTo(TestFixtureBuilder.MiddlePath));
            Assert.That(track.Origin.LocalState, Is.EqualTo(LocalState.ExplicitOverride));
            Assert.That(track.Origin.Evidence.Any(e => e.PropertyPath.EndsWith(".value")), Is.True);
            Assert.That(track.Levels[2].ValueState, Is.EqualTo(ValueState.Bound));
            Assert.That(track.Levels[2].LocalState, Is.EqualTo(LocalState.DefinedHere));
        }

        [Test]
        public void T07_Scene_MiddleNullOverride_OriginIsMiddleThroughParent()
        {
            ScanResult result = ScanScene(TestFixtureBuilder.SceneAPath);

            TrackBindingObservation track = FindTrack(result, "Parent#0/MiddleP#0/Child#1/Director#0", TestFixtureBuilder.TrackActivation);

            Assert.That(track.Levels.Count, Is.EqualTo(4));
            Assert.That(track.Levels[0].ContextKind, Is.EqualTo(ContextKind.SceneInstance));
            Assert.That(track.Levels[0].LocalState, Is.EqualTo(LocalState.Inherited));
            Assert.That(track.Levels[1].LocalState, Is.EqualTo(LocalState.Inherited));
            Assert.That(track.Origin.Owner.AssetPathHint, Is.EqualTo(TestFixtureBuilder.MiddlePath));
        }

        [Test]
        public void ParentOverrides_OriginIsParent()
        {
            ScanResult result = new SceneBindingScanner().ScanPrefabAsset(TestFixtureBuilder.ParentPath);

            TrackBindingObservation audio = FindTrack(result, "Parent#0/MiddleQ#1/Child#0/Director#0", TestFixtureBuilder.TrackAudio);
            Assert.That(audio.Effective.ValueState, Is.EqualTo(ValueState.Null));
            Assert.That(audio.OriginLevel, Is.EqualTo(0));
            Assert.That(audio.Origin.Owner.AssetPathHint, Is.EqualTo(TestFixtureBuilder.ParentPath));

            TrackBindingObservation cross = FindTrack(result, "Parent#0/MiddleQ#1/Child#1/Director#0", TestFixtureBuilder.TrackAnimB);
            Assert.That(cross.Effective.ValueState, Is.EqualTo(ValueState.Bound));
            Assert.That(cross.OriginLevel, Is.EqualTo(0));
            Assert.That(cross.Effective.Target.IndexPathHint, Is.EqualTo("Parent#0/MiddleP#0/Child#0/ActorB#2"));
        }

        [Test]
        public void T08_SceneOverride_LowerMiddleChangeIsShadowed()
        {
            ScanResult result = ScanScene(TestFixtureBuilder.SceneAPath);

            TrackBindingObservation track = FindTrack(result, "Parent#1/MiddleP#0/Child#1/Director#0", TestFixtureBuilder.TrackActivation);

            Assert.That(track.Effective.ValueState, Is.EqualTo(ValueState.Bound));
            Assert.That(track.Effective.Target.HierarchyPathHint, Is.EqualTo("SceneOnlyTarget"));
            Assert.That(track.OriginLevel, Is.EqualTo(0));
            Assert.That(track.Origin.LocalState, Is.EqualTo(LocalState.ExplicitOverride));
            LevelBindingState middle = track.Levels.Single(l => l.Owner.AssetPathHint == TestFixtureBuilder.MiddlePath);
            Assert.That(middle.Role, Is.EqualTo(LevelRole.Shadowed));
        }

        [Test]
        public void T09_SameNameInstances_AreDistinctAndResolveToOwnTargets()
        {
            ScanResult middle = new SceneBindingScanner().ScanPrefabAsset(TestFixtureBuilder.MiddlePath);
            TrackBindingObservation first = FindTrack(middle, "Middle#0/Child#0/Director#0", TestFixtureBuilder.TrackAnimB);
            TrackBindingObservation second = FindTrack(middle, "Middle#0/Child#1/Director#0", TestFixtureBuilder.TrackAnimB);

            Assert.That(first.BindingKey, Is.Not.EqualTo(second.BindingKey));
            Assert.That(first.Effective.Target.IndexPathHint, Is.EqualTo("Middle#0/Child#0/ActorB#2"));
            Assert.That(second.Effective.Target.IndexPathHint, Is.EqualTo("Middle#0/Child#1/ActorB#2"));

            ScanResult scene = ScanScene(TestFixtureBuilder.SceneAPath);
            List<string> keys = scene.Directors.SelectMany(d => d.Tracks).Select(t => t.BindingKey).ToList();
            Assert.That(keys.Distinct().Count(), Is.EqualTo(keys.Count));
            DirectorObservation parent0 = FindDirector(scene, "Parent#0/MiddleP#0/Child#0/Director#0");
            DirectorObservation parent1 = FindDirector(scene, "Parent#1/MiddleP#0/Child#0/Director#0");
            Assert.That(parent0.Director.GlobalObjectId, Is.Not.EqualTo(parent1.Director.GlobalObjectId));
            Assert.That(parent0.Director.HierarchyPathHint, Is.EqualTo(parent1.Director.HierarchyPathHint));
        }

        [Test]
        public void AudioBinding_ToSecondComponent_KeepsComponentIndex()
        {
            ScanResult result = new SceneBindingScanner().ScanPrefabAsset(TestFixtureBuilder.ChildPath);

            TrackBindingObservation track = FindTrack(result, "Child#0/Director#0", TestFixtureBuilder.TrackAudio);

            Assert.That(track.Effective.Target.TypeName, Is.EqualTo("AudioSource"));
            Assert.That(track.Effective.Target.ComponentIndexHint, Is.EqualTo(1));
        }

        [Test]
        public void T11_Variant_ClearBinding_IsNoEntryAtVariant()
        {
            ScanResult result = new SceneBindingScanner().ScanPrefabAsset(TestFixtureBuilder.VariantPath);

            TrackBindingObservation signal = FindTrack(result, "ParentVariant#0/MiddleP#0/Child#1/Director#0", TestFixtureBuilder.TrackSignal);
            Assert.That(signal.Effective.ValueState, Is.EqualTo(ValueState.NoEntry));
            Assert.That(signal.Effective.StructuralOverride, Is.True);
            Assert.That(signal.OriginLevel, Is.EqualTo(0));
            Assert.That(signal.Origin.Owner.AssetKind, Is.EqualTo(AssetKind.PrefabVariant));

            // ClearGenericBinding による配列のずれで Override 印が付くが、値は変わっていない
            TrackBindingObservation shifted = FindTrack(result, "ParentVariant#0/MiddleP#0/Child#1/Director#0", TestFixtureBuilder.TrackGroupedActivation);
            Assert.That(shifted.Effective.ValueOverridden, Is.True);
            Assert.That(shifted.Effective.ChangedAtThisLevel, Is.False);
            Assert.That(shifted.Effective.StructuralOverride, Is.True);
            Assert.That(shifted.Effective.LocalState, Is.EqualTo(LocalState.Inherited));
            Assert.That(shifted.Origin.Owner.AssetPathHint, Is.EqualTo(TestFixtureBuilder.ChildPath));

            TrackBindingObservation explicitNull = FindTrack(result, "ParentVariant#0/MiddleP#0/Child#0/Director#0", TestFixtureBuilder.TrackAnimA);
            Assert.That(explicitNull.Effective.ValueState, Is.EqualTo(ValueState.Null));
            Assert.That(explicitNull.OriginLevel, Is.EqualTo(0));
        }

        [Test]
        public void T11_SceneSpecificOverride_OriginIsScene()
        {
            ScanResult result = ScanScene(TestFixtureBuilder.SceneAPath);

            TrackBindingObservation track = FindTrack(result, "Parent#1/MiddleP#0/Child#0/Director#0", TestFixtureBuilder.TrackActivation);

            Assert.That(track.OriginLevel, Is.EqualTo(0));
            Assert.That(track.Origin.Owner.AssetKind, Is.EqualTo(AssetKind.Scene));
            Assert.That(track.Effective.Target.HierarchyPathHint, Is.EqualTo("SceneOnlyTarget"));

            TrackBindingObservation sibling = FindTrack(result, "Parent#0/MiddleP#0/Child#0/Director#0", TestFixtureBuilder.TrackActivation);
            Assert.That(sibling.Origin.Owner.AssetPathHint, Is.EqualTo(TestFixtureBuilder.ChildPath));
        }

        [Test]
        public void T11_InactiveAndDisabledDirectors_AreScanned()
        {
            ScanResult result = ScanScene(TestFixtureBuilder.SceneAPath);

            DirectorObservation inactive = FindDirector(result, "InactiveDirector#5");
            DirectorObservation disabled = FindDirector(result, "DisabledDirector#6");
            DirectorObservation standalone = FindDirector(result, "SceneDirector#4");

            Assert.That(inactive.ActiveInHierarchy, Is.False);
            Assert.That(disabled.Enabled, Is.False);
            Assert.That(inactive.Tracks.Count, Is.EqualTo(BindableTrackNames.Length));
            Assert.That(standalone.InstanceChain.Count, Is.EqualTo(0));
            TrackBindingObservation unbound = standalone.Tracks.Single(t => t.Track.TrackPathHint == TestFixtureBuilder.TrackAudio);
            Assert.That(unbound.Effective.ValueState, Is.EqualTo(ValueState.NoEntry));
            Assert.That(unbound.Effective.LocalState, Is.EqualTo(LocalState.DefinedHere));
        }

        [Test]
        public void InstanceChain_IsOuterToInner()
        {
            ScanResult result = ScanScene(TestFixtureBuilder.SceneAPath);

            DirectorObservation director = FindDirector(result, "Parent#0/MiddleQ#1/Child#1/Director#0");

            List<string> names = director.InstanceChain.Select(n => n.Name).ToList();
            Assert.That(names, Is.EqualTo(new[] { "Parent", "MiddleQ", "Child" }));
            List<string> sources = director.InstanceChain.Select(n => n.SourceAssetPathHint).ToList();
            Assert.That(sources, Is.EqualTo(new[] { TestFixtureBuilder.ParentPath, TestFixtureBuilder.MiddlePath, TestFixtureBuilder.ChildPath }));
        }

        [Test]
        public void T25_TracksWithoutBindingType_AreExcluded()
        {
            ScanResult result = new SceneBindingScanner().ScanPrefabAsset(TestFixtureBuilder.ChildPath);

            DirectorObservation director = FindDirector(result, "Child#0/Director#0");
            List<string> names = director.Tracks.Select(t => t.Track.TrackPathHint.Split('/').Last()).ToList();

            Assert.That(names, Is.EquivalentTo(BindableTrackNames));
            Assert.That(director.Tracks.Single(t => t.Track.TrackPathHint.EndsWith(TestFixtureBuilder.TrackGroupedActivation)).Track.TrackPathHint,
                Is.EqualTo("Group/GroupedActivation"));
        }

        [Test]
        public void StaleKey_IsReportedAsDiagnosticOnly()
        {
            ScanResult result = new SceneBindingScanner().ScanPrefabAsset(TestFixtureBuilder.StalePath);

            DirectorObservation director = result.Directors.Single();

            Assert.That(director.Tracks.Count, Is.EqualTo(1));
            Assert.That(director.Tracks[0].Track.TrackPathHint, Is.EqualTo(TestFixtureBuilder.TrackKeep));
            Assert.That(director.Tracks[0].Effective.ValueState, Is.EqualTo(ValueState.Bound));
            Assert.That(director.Diagnostics.Any(d => d.Contains("stale")), Is.True);
        }

        [Test]
        public void Scan_IsReadOnly()
        {
            Dictionary<string, string> before = HashFolder(TestFixtureBuilder.Folder);
            UnityEngine.Object selectionBefore = Selection.activeObject;

            SceneBindingScanner scanner = new SceneBindingScanner();
            foreach (string path in new[] { TestFixtureBuilder.ChildPath, TestFixtureBuilder.MiddlePath, TestFixtureBuilder.ParentPath, TestFixtureBuilder.VariantPath, TestFixtureBuilder.StalePath })
            {
                scanner.ScanPrefabAsset(path);
                Assert.That(EditorUtility.IsDirty(AssetDatabase.LoadAssetAtPath<GameObject>(path)), Is.False, path);
            }

            foreach (string path in new[] { TestFixtureBuilder.SceneAPath, TestFixtureBuilder.SceneBPath })
            {
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                scanner.Scan(scene);
                Assert.That(scene.isDirty, Is.False, path);
            }

            AssetDatabase.SaveAssets();
            Assert.That(HashFolder(TestFixtureBuilder.Folder), Is.EqualTo(before));
            Assert.That(Selection.activeObject, Is.EqualTo(selectionBefore));
        }

        [Test]
        public void RepeatedScan_ProducesSameKeysAndStates()
        {
            ScanResult first = ScanScene(TestFixtureBuilder.SceneBPath);
            ScanResult second = ScanScene(TestFixtureBuilder.SceneBPath);

            Assert.That(Summarize(second), Is.EqualTo(Summarize(first)));
        }

        [Test]
        public void UnsavedScene_IsMarkedNotSaved()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PlayableDirector director = new GameObject("Unsaved").AddComponent<PlayableDirector>();
            director.playableAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Timeline.TimelineAsset>(TestFixtureBuilder.TimelinePath);

            ScanResult result = new SceneBindingScanner().Scan(scene);

            Assert.That(result.IsSaved, Is.False);
            Assert.That(result.Diagnostics.Count, Is.GreaterThan(0));
            Assert.That(result.Directors.Count, Is.EqualTo(1));
        }

        private static ScanResult ScanScene(string path)
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            return new SceneBindingScanner().Scan(scene);
        }

        private static DirectorObservation FindDirector(ScanResult result, string indexPath)
        {
            List<DirectorObservation> matches = result.Directors.Where(d => d.Director.IndexPathHint == indexPath).ToList();
            Assert.That(matches.Count, Is.EqualTo(1), $"Director '{indexPath}' in {result.Owner}. Found: {string.Join(", ", result.Directors.Select(d => d.Director.IndexPathHint))}");
            return matches[0];
        }

        private static TrackBindingObservation FindTrack(ScanResult result, string directorIndexPath, string trackName)
        {
            DirectorObservation director = FindDirector(result, directorIndexPath);
            return director.Tracks.Single(t => t.Track.TrackPathHint.Split('/').Last() == trackName);
        }

        private static List<string> Summarize(ScanResult result)
        {
            return result.Directors
                .SelectMany(d => d.Tracks)
                .Select(t => $"{t.BindingKey} {t.Effective.ValueState} {t.Effective.Target.GlobalObjectId} {t.OriginLevel}")
                .ToList();
        }

        private static Dictionary<string, string> HashFolder(string assetFolder)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            string fullFolder = Path.GetFullPath(assetFolder);
            using (SHA256 sha = SHA256.Create())
            {
                foreach (string file in Directory.GetFiles(fullFolder, "*", SearchOption.AllDirectories))
                {
                    result[file.Substring(fullFolder.Length)] = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file)));
                }
            }

            return result;
        }
    }
}
