using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// PrefabUtility.GetPropertyModifications から、特定の階層・Director・Track に関する Override 証拠を取り出す。
    /// GetPropertyModifications は最も外側の PrefabInstance の全 Modification を返すため、
    /// target が1つ下の階層の Director と一致するものだけに絞る。
    /// 1回のスキャン内で使い回し、外側ルート単位でキャッシュする。
    /// </summary>
    public sealed class PrefabOverrideInspector
    {
        private const string ArrayDataPrefix = BindingReader.SceneBindingsProperty + ".Array.data[";
        private const string ArraySizePath = BindingReader.SceneBindingsProperty + ".Array.size";

        // キーは最も外側の Prefab インスタンスのルート（オブジェクトそのもの。Instance ID は Unity 6000.4 以降で廃止のため使わない）
        private readonly Dictionary<GameObject, PropertyModification[]> _cache = new Dictionary<GameObject, PropertyModification[]>();

        /// <summary>
        /// director の階層で、sourceDirector（1つ下の階層）に対して記録された m_SceneBindings の Override のうち、
        /// 指定インデックスの要素と配列サイズに関するものを返す。インデックスは復元キーにせず、証拠としてのみ使う。
        /// </summary>
        public List<OverrideEvidence> GetEvidence(PlayableDirector director, PlayableDirector sourceDirector, int elementIndex)
        {
            List<OverrideEvidence> result = new List<OverrideEvidence>();
            bool hasSource = sourceDirector != null;
            if (!hasSource || !PrefabUtility.IsPartOfPrefabInstance(director))
            {
                return result;
            }

            string elementPrefix = elementIndex >= 0 ? $"{ArrayDataPrefix}{elementIndex}]" : null;
            foreach (PropertyModification modification in GetModifications(director))
            {
                if (modification.target != sourceDirector)
                {
                    continue;
                }

                bool isSize = modification.propertyPath == ArraySizePath;
                bool isElement = elementPrefix != null && modification.propertyPath.StartsWith(elementPrefix);
                if (isSize || isElement)
                {
                    result.Add(new OverrideEvidence(
                        PrefabContextResolver.GetGlobalObjectId(modification.target),
                        modification.propertyPath,
                        modification.value,
                        PrefabContextResolver.GetGlobalObjectId(modification.objectReference)));
                }
            }

            return result;
        }

        private PropertyModification[] GetModifications(PlayableDirector director)
        {
            GameObject outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(director);
            if (outermost == null)
            {
                return System.Array.Empty<PropertyModification>();
            }

            if (!_cache.TryGetValue(outermost, out PropertyModification[] modifications))
            {
                modifications = PrefabUtility.GetPropertyModifications(outermost) ?? System.Array.Empty<PropertyModification>();
                _cache.Add(outermost, modifications);
            }

            return modifications;
        }
    }
}
