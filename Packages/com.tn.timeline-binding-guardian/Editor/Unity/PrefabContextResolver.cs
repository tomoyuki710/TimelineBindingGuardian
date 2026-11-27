using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// Director の Prefab 階層（Source チェーン）と、各階層の Owner・文脈を解決する。
    /// </summary>
    public static class PrefabContextResolver
    {
        private const int MaxChainDepth = 64;

        /// <summary>
        /// [0] = 指定 Director、以降は GetCorrespondingObjectFromSource で1段ずつ辿った下位階層。
        /// OriginalSource だけで中間階層を飛ばさない。
        /// </summary>
        public static List<PlayableDirector> GetSourceChain(PlayableDirector director)
        {
            List<PlayableDirector> chain = new List<PlayableDirector>();
            PlayableDirector current = director;
            while (current != null && chain.Count < MaxChainDepth)
            {
                chain.Add(current);
                current = PrefabUtility.GetCorrespondingObjectFromSource(current);
            }

            return chain;
        }

        /// <summary>
        /// 上位階層の参照先を、1つ下の階層の対応オブジェクトへ正規化する。対応が無ければ null。
        /// </summary>
        public static Object NormalizeToSource(Object target)
        {
            return target != null ? PrefabUtility.GetCorrespondingObjectFromSource(target) : null;
        }

        public static ContextKind GetContextKind(PlayableDirector director)
        {
            bool isAsset = PrefabUtility.IsPartOfPrefabAsset(director);
            bool isInstance = PrefabUtility.IsPartOfPrefabInstance(director);
            if (isAsset)
            {
                return isInstance ? ContextKind.PrefabInstance : ContextKind.PrefabDefinition;
            }

            return isInstance ? ContextKind.SceneInstance : ContextKind.SceneObject;
        }

        public static AssetLocator GetOwner(Object target)
        {
            string assetPath = AssetDatabase.GetAssetPath(target);
            if (!string.IsNullOrEmpty(assetPath))
            {
                return CreateAssetLocator(assetPath);
            }

            GameObject gameObject = GetGameObject(target);
            if (gameObject == null)
            {
                return new AssetLocator(string.Empty, string.Empty, AssetKind.Unknown);
            }

            return CreateSceneLocator(gameObject.scene);
        }

        public static AssetLocator CreateSceneLocator(Scene scene)
        {
            string path = scene.path;
            string guid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
            return new AssetLocator(guid, path, AssetKind.Scene);
        }

        public static AssetLocator CreateAssetLocator(string assetPath)
        {
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            return new AssetLocator(guid, assetPath, GetAssetKind(assetPath));
        }

        /// <summary>
        /// Scene 内の Director が属する Prefab インスタンスのルートを外側→内側の順で返す。
        /// </summary>
        public static List<InstanceNode> GetInstanceChain(PlayableDirector director)
        {
            List<InstanceNode> chain = new List<InstanceNode>();
            Stack<Transform> ancestors = new Stack<Transform>();
            Transform current = director.transform;
            while (current != null)
            {
                ancestors.Push(current);
                current = current.parent;
            }

            foreach (Transform transform in ancestors)
            {
                GameObject gameObject = transform.gameObject;
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(gameObject))
                {
                    continue;
                }

                // 1段上の Source ではなく、このインスタンス自身の Prefab Asset（ネスト時も Middle.prefab 等）を表示に使う
                string sourcePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
                chain.Add(new InstanceNode(GetGlobalObjectId(gameObject), gameObject.name, sourcePath));
            }

            return chain;
        }

        public static DirectorLocator CreateDirectorLocator(PlayableDirector director)
        {
            int componentIndex = Array.IndexOf(director.GetComponents<PlayableDirector>(), director);
            return new DirectorLocator(
                GetGlobalObjectId(director),
                GetHierarchyPath(director.transform, false),
                GetHierarchyPath(director.transform, true),
                componentIndex);
        }

        public static TargetLocator CreateTargetLocator(Object target)
        {
            if (target == null)
            {
                return TargetLocator.None;
            }

            Transform transform = GetTransform(target);
            int componentIndex = -1;
            if (target is Component component)
            {
                componentIndex = Array.IndexOf(component.GetComponents(component.GetType()), component);
            }

            string hierarchyPath = transform != null ? GetHierarchyPath(transform, false) : target.name;
            string indexPath = transform != null ? GetHierarchyPath(transform, true) : target.name;
            return new TargetLocator(
                GetGlobalObjectId(target),
                target.GetType().Name,
                hierarchyPath,
                indexPath,
                componentIndex,
                GetOwner(target).AssetPathHint);
        }

        public static string GetGlobalObjectId(Object target)
        {
            return target != null ? GlobalObjectId.GetGlobalObjectIdSlow(target).ToString() : string.Empty;
        }

        /// <summary>
        /// 表示用の Hierarchy パス。withIndex=true なら「名前#兄弟インデックス」（Scene ルートの同名オブジェクトも区別するためルートにも付ける）。
        /// </summary>
        public static string GetHierarchyPath(Transform transform, bool withIndex)
        {
            Stack<string> parts = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                parts.Push(withIndex ? $"{current.name}#{current.GetSiblingIndex()}" : current.name);
                current = current.parent;
            }

            StringBuilder builder = new StringBuilder();
            foreach (string part in parts)
            {
                if (builder.Length > 0)
                {
                    builder.Append('/');
                }

                builder.Append(part);
            }

            return builder.ToString();
        }

        public static bool IsPreviewScene(Object target)
        {
            GameObject gameObject = GetGameObject(target);
            return gameObject != null && EditorSceneManager.IsPreviewScene(gameObject.scene);
        }

        private static AssetKind GetAssetKind(string assetPath)
        {
            if (assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                return AssetKind.Scene;
            }

            if (assetPath.EndsWith(".playable", StringComparison.OrdinalIgnoreCase))
            {
                return AssetKind.Timeline;
            }

            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (root == null)
            {
                return AssetKind.Unknown;
            }

            switch (PrefabUtility.GetPrefabAssetType(root))
            {
                case PrefabAssetType.Regular:
                    return AssetKind.Prefab;
                case PrefabAssetType.Variant:
                    return AssetKind.PrefabVariant;
                case PrefabAssetType.Model:
                    return AssetKind.ModelPrefab;
                default:
                    return AssetKind.Unknown;
            }
        }

        private static GameObject GetGameObject(Object target)
        {
            if (target is GameObject gameObject)
            {
                return gameObject;
            }

            return target is Component component ? component.gameObject : null;
        }

        private static Transform GetTransform(Object target)
        {
            GameObject gameObject = GetGameObject(target);
            return gameObject != null ? gameObject.transform : null;
        }
    }
}
