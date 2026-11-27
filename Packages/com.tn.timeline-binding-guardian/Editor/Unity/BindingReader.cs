using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// m_SceneBindings の1要素。
    /// </summary>
    public readonly struct BindingEntry
    {
        public BindingEntry(int index, Object value, bool isMissing, bool valueOverridden)
        {
            Index = index;
            Value = value;
            IsMissing = isMissing;
            ValueOverridden = valueOverridden;
        }

        public int Index { get; }
        public Object Value { get; }
        // 保存された非 NULL 参照が解決できない証拠がある（stored instanceID≠0 かつ値 NULL）
        public bool IsMissing { get; }
        public bool ValueOverridden { get; }
    }

    /// <summary>
    /// 1 Director の m_SceneBindings を Track（key）で引ける形にしたもの。
    /// </summary>
    public sealed class DirectorBindingTable
    {
        private readonly Dictionary<Object, BindingEntry> _entries = new Dictionary<Object, BindingEntry>();
        private readonly HashSet<Object> _duplicateKeys = new HashSet<Object>();

        public bool ArrayFound { get; set; }
        public bool SizeOverridden { get; set; }
        // key が NULL だが stored instanceID≠0（削除された Track の残骸）
        public int StaleKeyCount { get; set; }
        // key が NULL で証拠もない要素
        public int NullKeyCount { get; set; }

        public void Add(Object key, BindingEntry entry)
        {
            if (_entries.ContainsKey(key))
            {
                _duplicateKeys.Add(key);
                return;
            }

            _entries.Add(key, entry);
        }

        public bool TryGet(Object key, out BindingEntry entry)
        {
            return _entries.TryGetValue(key, out entry);
        }

        public bool IsDuplicate(Object key)
        {
            return _duplicateKeys.Contains(key);
        }
    }

    /// <summary>
    /// PlayableDirector のバインド定義を読み取り専用で取得する。
    /// SerializedObject の変更適用・SetDirty は行わない。
    /// </summary>
    public static class BindingReader
    {
        public const string SceneBindingsProperty = "m_SceneBindings";
        private const string KeyProperty = "key";
        private const string ValueProperty = "value";

        /// <summary>
        /// バインド対象型を持つ出力の Track だけを返す（Control/Group など対象型の無い出力は除外）。
        /// </summary>
        public static List<TrackAsset> GetBindableTracks(TimelineAsset timeline)
        {
            List<TrackAsset> result = new List<TrackAsset>();
            foreach (PlayableBinding binding in timeline.outputs)
            {
                TrackAsset track = binding.sourceObject as TrackAsset;
                bool hasTargetType = binding.outputTargetType != null;
                if (track != null && hasTargetType)
                {
                    result.Add(track);
                }
            }

            return result;
        }

        public static string GetOutputTargetTypeName(TimelineAsset timeline, TrackAsset track)
        {
            foreach (PlayableBinding binding in timeline.outputs)
            {
                if (binding.sourceObject == track && binding.outputTargetType != null)
                {
                    return binding.outputTargetType.Name;
                }
            }

            return string.Empty;
        }

        public static DirectorBindingTable Read(PlayableDirector director)
        {
            DirectorBindingTable table = new DirectorBindingTable();
            using (SerializedObject serializedObject = new SerializedObject(director))
            {
                SerializedProperty array = serializedObject.FindProperty(SceneBindingsProperty);
                if (array == null || !array.isArray)
                {
                    return table;
                }

                table.ArrayFound = true;
                SerializedProperty size = serializedObject.FindProperty(SceneBindingsProperty + ".Array.size");
                table.SizeOverridden = size != null && size.prefabOverride;

                for (int i = 0; i < array.arraySize; i++)
                {
                    SerializedProperty element = array.GetArrayElementAtIndex(i);
                    SerializedProperty key = element.FindPropertyRelative(KeyProperty);
                    SerializedProperty value = element.FindPropertyRelative(ValueProperty);
                    if (key == null || value == null)
                    {
                        continue;
                    }

                    if (key.objectReferenceValue == null)
                    {
                        CountNullKey(table, key);
                        continue;
                    }

                    table.Add(key.objectReferenceValue, CreateEntry(i, value));
                }
            }

            return table;
        }

        private static BindingEntry CreateEntry(int index, SerializedProperty value)
        {
            Object target = value.objectReferenceValue;
            bool isMissing = target == null && GetStoredInstanceId(value) != 0;
            return new BindingEntry(index, target, isMissing, value.prefabOverride);
        }

        private static void CountNullKey(DirectorBindingTable table, SerializedProperty key)
        {
            if (GetStoredInstanceId(key) != 0)
            {
                table.StaleKeyCount++;
            }
            else
            {
                table.NullKeyCount++;
            }
        }

        private static int GetStoredInstanceId(SerializedProperty property)
        {
#pragma warning disable 618
            return property.objectReferenceInstanceIDValue;
#pragma warning restore 618
        }
    }
}
