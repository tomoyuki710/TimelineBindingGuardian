using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_6000_4_OR_NEWER
using ItemId = UnityEngine.EntityId;
#else
using ItemId = System.Int32;
#endif

namespace TimelineBindingGuardian
{
    /// <summary>
    /// Hierarchy の行の ID を扱う。Unity 6000.4 以降は EntityId、それより前は Instance ID（int）。
    /// Instance ID 系の API は 6000.4 で廃止予定、6000.5 以降で削除されたため、バージョンで使い分ける。
    /// Scene の見出し行には、Scene の handle が表す ID が渡される
    /// （6000.3 以前は handle の値そのもの、6000.4 以降は handle が内部に持つ EntityId）。
    /// </summary>
    internal static class HierarchyItemId
    {
        public static ItemId For(GameObject gameObject)
        {
#if UNITY_6000_4_OR_NEWER
            return gameObject.GetEntityId();
#else
            return gameObject.GetInstanceID();
#endif
        }

        public static Object ToObject(ItemId id)
        {
#if UNITY_6000_4_OR_NEWER
            return EditorUtility.EntityIdToObject(id);
#else
#pragma warning disable 618
            return EditorUtility.InstanceIDToObject(id);
#pragma warning restore 618
#endif
        }

        /// <summary>
        /// Scene を見分けるキー。Scene の見出し行の ID から作る SceneKey(ItemId) と一致する。
        /// </summary>
        public static ulong SceneKey(Scene scene)
        {
#if UNITY_6000_4_OR_NEWER
            return scene.handle.GetRawData();
#else
            // 6000.0〜6000.2 は int、6000.3 は int へ変換できる SceneHandle
            int handle = scene.handle;
            return unchecked((ulong)(long)handle);
#endif
        }

        public static ulong SceneKey(ItemId id)
        {
#if UNITY_6000_4_OR_NEWER
            return EntityId.ToULong(id);
#else
            return unchecked((ulong)(long)id);
#endif
        }
    }
}
