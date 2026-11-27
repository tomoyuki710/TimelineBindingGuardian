using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// Scope（Scene / Prefab）のファイル内容の指紋（Phase 5）。前回記録したときと同じなら、自動検査を省ける。
    /// - Scope 自身と、依存する Prefab・Scene・Timeline（.prefab / .unity / .playable）はファイルの内容をハッシュする。
    /// - それ以外の依存（Track の型のスクリプトなど）は Unity の依存ハッシュを使う（軽い）。
    /// - 検査ロジックの版と Unity のバージョンも含める（検査の中身が変わったら全件を検査し直すため）。
    /// </summary>
    public static class ScopeFingerprint
    {
        // 検査結果に影響する変更をしたら上げる
        public const string LogicVersion = "1";

        private static readonly string[] ContentHashedExtensions = { ".unity", ".prefab", ".playable" };

        /// <summary>
        /// 指紋を計算する。ファイルが読めなければ空文字（比較では常に「変化あり」になる）。
        /// </summary>
        public static string Compute(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath))
            {
                return string.Empty;
            }

            try
            {
                using (MD5 md5 = MD5.Create())
                {
                    Append(md5, $"tbg-fingerprint:{LogicVersion}:{Application.unityVersion}\n");
                    string[] dependencies = AssetDatabase.GetDependencies(assetPath, true);
                    foreach (string path in dependencies.OrderBy(p => p, StringComparer.Ordinal))
                    {
                        Append(md5, path + "\n");
                        if (IsContentHashed(path))
                        {
                            byte[] content = File.ReadAllBytes(path);
                            md5.TransformBlock(content, 0, content.Length, null, 0);
                        }
                        else
                        {
                            Append(md5, AssetDatabase.GetAssetDependencyHash(path).ToString());
                        }
                    }

                    md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    return BitConverter.ToString(md5.Hash).Replace("-", string.Empty);
                }
            }
            catch (IOException)
            {
                return string.Empty;
            }
            catch (UnauthorizedAccessException)
            {
                return string.Empty;
            }
        }

        private static bool IsContentHashed(string path)
        {
            return ContentHashedExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase)) && File.Exists(path);
        }

        private static void Append(HashAlgorithm hash, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            hash.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
    }
}
