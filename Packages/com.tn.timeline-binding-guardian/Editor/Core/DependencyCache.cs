using System;
using System.Collections.Generic;
using System.Linq;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// パスごとの依存一覧（推移的、自分自身を含む）のキャッシュ。Unity に依存しない。
    /// アセット X が変わったときは、X 自身と、X を依存に含む一覧だけを捨てる。
    /// - X の参照先が変わると、X を含む一覧は古くなる。
    /// - X を新たに参照し始めたものは、そのファイル自身が変わってインポートされるので、そのときに捨てられる。
    /// </summary>
    public sealed class DependencyCache
    {
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Func<string, string[]> _provider;

        /// <param name="provider">パスの依存一覧（推移的）を取得する関数。</param>
        public DependencyCache(Func<string, string[]> provider)
        {
            _provider = provider;
        }

        public int Count => _entries.Count;

        public bool Contains(string path)
        {
            return _entries.ContainsKey(path);
        }

        /// <summary>
        /// 依存一覧を序数順に並べたもの（指紋の計算で使う）。
        /// </summary>
        public IReadOnlyList<string> GetSorted(string path)
        {
            return GetEntry(path).Sorted;
        }

        public bool DependsOn(string path, string dependency)
        {
            return GetEntry(path).Set.Contains(dependency);
        }

        /// <summary>
        /// 変わったアセット（インポート・移動・削除）ごとに、その自身と、それを依存に含む一覧を捨てる。
        /// </summary>
        public void Invalidate(IEnumerable<string> changedPaths)
        {
            foreach (string changed in changedPaths)
            {
                if (string.IsNullOrEmpty(changed) || _entries.Count == 0)
                {
                    continue;
                }

                _entries.Remove(changed);
                List<string> stale = _entries.Where(pair => pair.Value.Set.Contains(changed)).Select(pair => pair.Key).ToList();
                foreach (string key in stale)
                {
                    _entries.Remove(key);
                }
            }
        }

        public void Clear()
        {
            _entries.Clear();
        }

        private Entry GetEntry(string path)
        {
            if (!_entries.TryGetValue(path, out Entry entry))
            {
                entry = new Entry(_provider(path) ?? Array.Empty<string>());
                _entries.Add(path, entry);
            }

            return entry;
        }

        private sealed class Entry
        {
            public Entry(string[] dependencies)
            {
                Sorted = dependencies.OrderBy(p => p, StringComparer.Ordinal).ToArray();
                Set = new HashSet<string>(dependencies, StringComparer.Ordinal);
            }

            public string[] Sorted { get; }

            public HashSet<string> Set { get; }
        }
    }
}
