using System;
using System.Collections.Generic;
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
    /// 負荷対策:
    /// - 依存一覧は DependencyIndex のキャッシュを共有する。Prefab・Scene・Timeline の間だけをたどり、それらが直接参照するもの
    ///   （モデル・Track のスクリプトなど）は依存ハッシュで含める。テクスチャの中身など、検査結果に関係しない深い変化は含めない。
    /// - ファイル内容のハッシュは、ファイルサイズと最終更新時刻が同じなら使い回す（変わっていないファイルは読み直さない）。
    /// </summary>
    public static class ScopeFingerprint
    {
        // 検査結果に影響する変更、または指紋の作り方を変えたら上げる（2: ファイルごとのハッシュを使い回す方式、3: 依存は Prefab・Scene・Timeline の間だけたどる）
        public const string LogicVersion = "3";

        private static readonly string[] ContentHashedExtensions = { ".unity", ".prefab", ".playable" };
        private static readonly Dictionary<string, FileHash> _fileHashes = new Dictionary<string, FileHash>(StringComparer.Ordinal);

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
                    foreach (string path in DependencyIndex.GetSortedDependencies(assetPath))
                    {
                        Append(md5, path + "\n");
                        if (IsContentHashed(path))
                        {
                            Append(md5, GetContentHash(path));
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

        /// <summary>
        /// ファイル内容のハッシュ。サイズと最終更新時刻が前回と同じなら、読み直さずに前回の値を返す。
        /// </summary>
        private static string GetContentHash(string path)
        {
            FileInfo file = new FileInfo(path);
            long length = file.Length;
            DateTime lastWriteUtc = file.LastWriteTimeUtc;
            bool isCached = _fileHashes.TryGetValue(path, out FileHash cached) && cached.Length == length;
            if (isCached && cached.LastWriteUtc == lastWriteUtc)
            {
                return cached.Hash;
            }

            using (MD5 md5 = MD5.Create())
            {
                string hash = BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(path))).Replace("-", string.Empty);
                _fileHashes[path] = new FileHash(length, lastWriteUtc, hash);
                return hash;
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

        private readonly struct FileHash
        {
            public FileHash(long length, DateTime lastWriteUtc, string hash)
            {
                Length = length;
                LastWriteUtc = lastWriteUtc;
                Hash = hash;
            }

            public long Length { get; }

            public DateTime LastWriteUtc { get; }

            public string Hash { get; }
        }
    }
}
