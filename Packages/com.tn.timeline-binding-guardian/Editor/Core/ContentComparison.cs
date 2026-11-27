using System;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 保存内容の比較で使う小さな共通処理。null の文字列と空文字は同じとみなす（保存・読み込みで区別されないため）。
    /// </summary>
    internal static class ContentComparison
    {
        public static bool Same(string a, string b)
        {
            return string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal);
        }
    }
}
