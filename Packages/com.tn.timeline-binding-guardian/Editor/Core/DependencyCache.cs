using System;
using System.Collections.Generic;
using System.Linq;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 依存関係のキャッシュ。Unity に依存しない。
    /// - パスごとに「1 段だけの依存」を提供関数から取得して持つ（再帰的な取得は、テクスチャ等まで何千件もたどって重いため使わない）。
    /// - 推移的な依存一覧は、1 段の依存をたどって作る。たどるのは Prefab・Scene・Timeline（.prefab / .unity / .playable）だけで、
    ///   それ以外（スクリプト・モデル・マテリアルなど）はたどらずに「葉」として一覧に入れる。一覧には自分自身も含む。
    /// - アセットが変わったら、そのアセットの 1 段の依存だけを捨てる（推移的な一覧のメモは作り直しが軽いので全部捨てる）。
    /// </summary>
    public sealed class DependencyCache
    {
        private static readonly string[] TraversedExtensions = { ".prefab", ".unity", ".playable" };

        private readonly Dictionary<string, string[]> _direct = new Dictionary<string, string[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, Closure> _closures = new Dictionary<string, Closure>(StringComparer.Ordinal);
        private readonly Func<string, string[]> _provider;

        /// <param name="provider">パスの 1 段だけの依存を取得する関数（自分自身は含まなくてよい）。</param>
        public DependencyCache(Func<string, string[]> provider)
        {
            _provider = provider;
        }

        /// <summary>
        /// 1 段の依存をキャッシュしているパスの数。
        /// </summary>
        public int Count => _direct.Count;

        /// <summary>
        /// 1 段の依存をキャッシュしているか。
        /// </summary>
        public bool Contains(string path)
        {
            return _direct.ContainsKey(path);
        }

        /// <summary>
        /// 推移的な依存一覧（自分自身を含む）を序数順に並べたもの（指紋の計算で使う）。
        /// </summary>
        public IReadOnlyList<string> GetSorted(string path)
        {
            return GetClosure(path).Sorted;
        }

        public bool DependsOn(string path, string dependency)
        {
            return GetClosure(path).Set.Contains(dependency);
        }

        /// <summary>
        /// 変わったアセット（インポート・移動・削除）の 1 段の依存を捨てる。
        /// </summary>
        public void Invalidate(IEnumerable<string> changedPaths)
        {
            bool removed = false;
            foreach (string changed in changedPaths)
            {
                if (!string.IsNullOrEmpty(changed))
                {
                    removed |= _direct.Remove(changed);
                }
            }

            if (removed)
            {
                _closures.Clear();
            }
        }

        public void Clear()
        {
            _direct.Clear();
            _closures.Clear();
        }

        private Closure GetClosure(string path)
        {
            if (_closures.TryGetValue(path, out Closure closure))
            {
                return closure;
            }

            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal) { path };
            Queue<string> pending = new Queue<string>();
            pending.Enqueue(path);
            while (pending.Count > 0)
            {
                foreach (string dependency in GetDirect(pending.Dequeue()))
                {
                    bool isNew = visited.Add(dependency);
                    if (isNew && IsTraversed(dependency))
                    {
                        pending.Enqueue(dependency);
                    }
                }
            }

            closure = new Closure(visited);
            _closures.Add(path, closure);
            return closure;
        }

        private string[] GetDirect(string path)
        {
            if (!_direct.TryGetValue(path, out string[] dependencies))
            {
                dependencies = _provider(path) ?? Array.Empty<string>();
                _direct.Add(path, dependencies);
            }

            return dependencies;
        }

        private static bool IsTraversed(string path)
        {
            return TraversedExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        }

        private sealed class Closure
        {
            public Closure(HashSet<string> paths)
            {
                Set = paths;
                Sorted = paths.OrderBy(p => p, StringComparer.Ordinal).ToArray();
            }

            public HashSet<string> Set { get; }

            public string[] Sorted { get; }
        }
    }
}
