using System.Collections.Generic;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 同じ内容の文字列を 1 つのインスタンスにまとめる（GUID・パス・ID は大量に重複するため、読み込み後のメモリを減らす）。
    /// string.Intern と違いプロセス全体に残らず、プールを捨てれば解放される。
    /// </summary>
    public sealed class StringPool
    {
        private readonly Dictionary<string, string> _pool = new Dictionary<string, string>();

        public int Count => _pool.Count;

        public string Intern(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (_pool.TryGetValue(value, out string pooled))
            {
                return pooled;
            }

            _pool.Add(value, value);
            return value;
        }
    }
}
