using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// Hierarchy の行の ID（6000.4 以降は EntityId、それより前は Instance ID）の扱い。
    /// Scene の見出し行の ID は、Unity 自身が作るのと同じ方法で作り、SceneKey が一致することを確かめる
    /// （6000.4 以降の Hierarchy は scene.handle.ToEntityId() を渡す: UnityCsReference の GameObjectTreeViewDataSource）。
    /// </summary>
    public sealed class HierarchyItemIdTests
    {
        private const BindingFlags StaticMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private Type _type;
        private Type _itemIdType;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            TestFixtureBuilder.EnsureBuilt();
        }

        [SetUp]
        public void SetUp()
        {
            _type = typeof(GuardianSettings).Assembly.GetType("TimelineBindingGuardian.HierarchyItemId", true);
            _itemIdType = _type.GetMethod("For", StaticMembers, null, new[] { typeof(GameObject) }, null).ReturnType;
        }

        [Test]
        public void GameObjectId_RoundTripsToTheSameObject()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject gameObject = new GameObject("HierarchyItemIdTarget");

            object id = Invoke("For", new[] { typeof(GameObject) }, gameObject);
            Object resolved = (Object)Invoke("ToObject", new[] { _itemIdType }, id);

            Assert.That(resolved, Is.SameAs(gameObject));
        }

        [Test]
        public void SceneHeaderRowId_MatchesTheSceneKey()
        {
            Scene scene = EditorSceneManager.OpenScene(TestFixtureBuilder.SceneAPath, OpenSceneMode.Single);
            object rowId = CreateSceneHeaderRowId(scene);

            ulong keyFromRow = (ulong)Invoke("SceneKey", new[] { _itemIdType }, rowId);
            ulong keyFromScene = (ulong)Invoke("SceneKey", new[] { typeof(Scene) }, scene);

            Assert.That(keyFromRow, Is.EqualTo(keyFromScene));
        }

        [Test]
        public void SceneKeys_DifferBetweenLoadedScenes()
        {
            Scene sceneA = EditorSceneManager.OpenScene(TestFixtureBuilder.SceneAPath, OpenSceneMode.Single);
            Scene sceneB = EditorSceneManager.OpenScene(TestFixtureBuilder.SceneBPath, OpenSceneMode.Additive);

            ulong first = (ulong)Invoke("SceneKey", new[] { typeof(Scene) }, sceneA);
            ulong second = (ulong)Invoke("SceneKey", new[] { typeof(Scene) }, sceneB);

            Assert.That(first, Is.Not.EqualTo(second));
        }

        /// <summary>
        /// Hierarchy が Scene の見出し行に渡すのと同じ ID を作る。
        /// </summary>
        private static object CreateSceneHeaderRowId(Scene scene)
        {
#if UNITY_6000_4_OR_NEWER
            MethodInfo toEntityId = scene.handle.GetType().GetMethod("ToEntityId", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(toEntityId, Is.Not.Null, "SceneHandle.ToEntityId was not found");
            return toEntityId.Invoke(scene.handle, null);
#else
            int handle = scene.handle;
            return handle;
#endif
        }

        private object Invoke(string name, Type[] parameterTypes, object argument)
        {
            MethodInfo method = _type.GetMethod(name, StaticMembers, null, parameterTypes, null);
            Assert.That(method, Is.Not.Null, name);
            return method.Invoke(null, new[] { argument });
        }
    }
}
