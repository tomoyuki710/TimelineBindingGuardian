using System;
using System.Collections.Generic;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 監視から除外するフォルダの判定。Unity API に依存しない。
    /// フォルダ自身とその下位が対象で、"Assets/Foo" は "Assets/FooBar" に一致しない。大文字小文字は区別しない。
    /// </summary>
    public static class PathExclusion
    {
        /// <summary>
        /// 表記を揃える（"\" → "/"、前後の空白と末尾の "/" を除く）。
        /// </summary>
        public static string NormalizeFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return string.Empty;
            }

            return folder.Trim().Replace('\\', '/').TrimEnd('/');
        }

        public static bool IsExcluded(string assetPath, IEnumerable<string> folders)
        {
            if (string.IsNullOrEmpty(assetPath) || folders == null)
            {
                return false;
            }

            string path = assetPath.Replace('\\', '/');
            foreach (string raw in folders)
            {
                string folder = NormalizeFolder(raw);
                if (folder.Length == 0)
                {
                    continue;
                }

                bool isSame = string.Equals(path, folder, StringComparison.OrdinalIgnoreCase);
                bool isInside = path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);
                if (isSame || isInside)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
