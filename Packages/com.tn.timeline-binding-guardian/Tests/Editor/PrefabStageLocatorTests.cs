using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// Prefab Asset 内のオブジェクトを、Prefab Mode で開いた同じ Prefab の対応オブジェクトへ読み替えるテスト。
    /// </summary>
    public sealed class PrefabStageLocatorTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            TestFixtureBuilder.EnsureBuilt();
        }

        [TearDown]
        public void TearDown()
        {
            StageUtility.GoToMainStage();
        }

        [Test]
        public void NestedInstance_SameNameSiblings_MapsToCorrectDirector()
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(TestFixtureBuilder.MiddlePath);
            PlayableDirector second = asset.transform.GetChild(1).GetComponentInChildren<PlayableDirector>(true);
            PrefabStage stage = PrefabStageUtility.OpenPrefab(TestFixtureBuilder.MiddlePath);

            bool found = PrefabStageLocator.TryFindInOpenPrefabStage(second, out Object stageObject);

            Assert.That(found, Is.True);
            PlayableDirector stageDirector = stageObject as PlayableDirector;
            Assert.That(stageDirector, Is.Not.Null);
            Assert.That(stageDirector.gameObject.scene, Is.EqualTo(stage.scene));
            Assert.That(stageDirector.transform.parent.GetSiblingIndex(), Is.EqualTo(1));
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(stageDirector),
                Is.EqualTo(PrefabUtility.GetCorrespondingObjectFromSource(second)));
        }

        [Test]
        public void DefinitionLevel_MapsToDirectorInStage()
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(TestFixtureBuilder.ChildPath);
            PlayableDirector director = asset.GetComponentInChildren<PlayableDirector>(true);
            PrefabStage stage = PrefabStageUtility.OpenPrefab(TestFixtureBuilder.ChildPath);

            bool found = PrefabStageLocator.TryFindInOpenPrefabStage(director, out Object stageObject);

            Assert.That(found, Is.True);
            PlayableDirector stageDirector = stageObject as PlayableDirector;
            Assert.That(stageDirector, Is.Not.Null);
            Assert.That(stageDirector.gameObject.scene, Is.EqualTo(stage.scene));
            Assert.That(stageDirector.gameObject.name, Is.EqualTo("Director"));
        }

        [Test]
        public void GameObjectTarget_MapsToGameObjectInStage()
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(TestFixtureBuilder.MiddlePath);
            GameObject actTarget = asset.transform.GetChild(0).Find("ActTarget").gameObject;
            PrefabStage stage = PrefabStageUtility.OpenPrefab(TestFixtureBuilder.MiddlePath);

            bool found = PrefabStageLocator.TryFindInOpenPrefabStage(actTarget, out Object stageObject);

            Assert.That(found, Is.True);
            GameObject stageTarget = stageObject as GameObject;
            Assert.That(stageTarget, Is.Not.Null);
            Assert.That(stageTarget.scene, Is.EqualTo(stage.scene));
            Assert.That(stageTarget.transform.parent.GetSiblingIndex(), Is.EqualTo(0));
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(stageTarget),
                Is.EqualTo(PrefabUtility.GetCorrespondingObjectFromSource(actTarget)));
        }

        [Test]
        public void SecondComponentOfSameType_MapsToSameIndex()
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(TestFixtureBuilder.ChildPath);
            AudioSource second = asset.transform.Find("Speaker").GetComponents<AudioSource>()[1];
            PrefabStageUtility.OpenPrefab(TestFixtureBuilder.ChildPath);

            bool found = PrefabStageLocator.TryFindInOpenPrefabStage(second, out Object stageObject);

            Assert.That(found, Is.True);
            AudioSource stageAudio = stageObject as AudioSource;
            Assert.That(stageAudio, Is.Not.Null);
            Assert.That(stageAudio.GetComponents<AudioSource>()[1], Is.EqualTo(stageAudio));
        }

        [Test]
        public void DirectorFromAnotherPrefab_IsNotMapped()
        {
            GameObject parent = AssetDatabase.LoadAssetAtPath<GameObject>(TestFixtureBuilder.ParentPath);
            PlayableDirector director = parent.GetComponentInChildren<PlayableDirector>(true);
            PrefabStageUtility.OpenPrefab(TestFixtureBuilder.MiddlePath);

            bool found = PrefabStageLocator.TryFindInOpenPrefabStage(director, out Object stageObject);

            Assert.That(found, Is.False);
            Assert.That(stageObject, Is.Null);
        }

        [Test]
        public void NoPrefabStage_IsNotMapped()
        {
            StageUtility.GoToMainStage();
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(TestFixtureBuilder.MiddlePath);
            PlayableDirector director = asset.GetComponentInChildren<PlayableDirector>(true);

            Assert.That(PrefabStageLocator.TryFindInOpenPrefabStage(director, out Object _), Is.False);
        }
    }
}
