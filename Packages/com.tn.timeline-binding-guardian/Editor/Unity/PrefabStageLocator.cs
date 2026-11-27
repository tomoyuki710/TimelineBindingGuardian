using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// Prefab Asset 内の GameObject / Component を、Prefab Mode で開いている同じ Prefab の対応オブジェクトへ読み替える。
    /// Prefab Mode の内容は Asset とは別のオブジェクトで、公開 API に直接の対応付けが無いため、
    /// Asset ルートからの兄弟インデックスのパスと同型コンポーネントの順番で探し、Source の一致で検証する。
    /// 表示（Ping）専用。復元などの自動処理には使わない。
    /// </summary>
    public static class PrefabStageLocator
    {
        /// <summary>
        /// 現在の Prefab Stage が assetObject の属する Prefab Asset を開いていれば、対応するオブジェクトを返す。
        /// assetObject は GameObject または Component。
        /// </summary>
        public static bool TryFindInOpenPrefabStage(Object assetObject, out Object stageObject)
        {
            stageObject = null;
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            Transform assetTransform = GetTransform(assetObject);
            if (stage == null || assetTransform == null)
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(assetObject);
            if (string.IsNullOrEmpty(assetPath) || assetPath != stage.assetPath)
            {
                return false;
            }

            Transform stageTransform = FindByIndexPath(stage.prefabContentsRoot.transform, GetIndexPath(assetTransform));
            if (stageTransform == null)
            {
                return false;
            }

            Object candidate = assetObject is Component component
                ? FindSameComponent(component, stageTransform)
                : stageTransform.gameObject;
            if (candidate == null || !IsSameObject(assetObject, candidate))
            {
                return false;
            }

            stageObject = candidate;
            return true;
        }

        private static Component FindSameComponent(Component assetComponent, Transform stageTransform)
        {
            Type type = assetComponent.GetType();
            int componentIndex = Array.IndexOf(assetComponent.GetComponents(type), assetComponent);
            Component[] candidates = stageTransform.GetComponents(type);
            bool inRange = componentIndex >= 0 && componentIndex < candidates.Length;
            return inRange ? candidates[componentIndex] : null;
        }

        /// <summary>
        /// Nested Prefab の一部なら1段下の Source が一致すること、Prefab 自身で定義されたものなら名前が一致することで検証する。
        /// </summary>
        private static bool IsSameObject(Object assetObject, Object stageObject)
        {
            Object assetSource = PrefabUtility.GetCorrespondingObjectFromSource(assetObject);
            Object stageSource = PrefabUtility.GetCorrespondingObjectFromSource(stageObject);
            if (assetSource != null || stageSource != null)
            {
                return assetSource == stageSource;
            }

            return GetTransform(assetObject).name == GetTransform(stageObject).name;
        }

        private static Transform GetTransform(Object target)
        {
            if (target is GameObject gameObject)
            {
                return gameObject.transform;
            }

            return target is Component component ? component.transform : null;
        }

        // ルート自身を除いた、各階層の兄弟インデックス（外側→内側）
        private static List<int> GetIndexPath(Transform transform)
        {
            List<int> path = new List<int>();
            Transform current = transform;
            while (current.parent != null)
            {
                path.Insert(0, current.GetSiblingIndex());
                current = current.parent;
            }

            return path;
        }

        private static Transform FindByIndexPath(Transform root, List<int> path)
        {
            Transform current = root;
            foreach (int index in path)
            {
                if (index >= current.childCount)
                {
                    return null;
                }

                current = current.GetChild(index);
            }

            return current;
        }
    }
}
