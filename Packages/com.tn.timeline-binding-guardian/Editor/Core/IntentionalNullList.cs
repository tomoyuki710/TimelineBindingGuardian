using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 意図的な NULL として登録された 1 件（NULL にした階層 = Owner 階層キー）。表示用のパスと理由（任意）を持つ。
    /// </summary>
    [Serializable]
    public sealed class IntentionalNullEntry
    {
        [SerializeField] private string ownerLevelKey;
        [SerializeField] private string ownerPath;
        [SerializeField] private string directorPath;
        [SerializeField] private string timelinePath;
        [SerializeField] private string trackPath;
        [SerializeField] private string reason;
        [SerializeField] private string addedAtUtc;

        public IntentionalNullEntry(string ownerLevelKey, string ownerPath, string directorPath, string timelinePath, string trackPath,
            string reason, string addedAtUtc)
        {
            this.ownerLevelKey = ownerLevelKey ?? string.Empty;
            this.ownerPath = ownerPath ?? string.Empty;
            this.directorPath = directorPath ?? string.Empty;
            this.timelinePath = timelinePath ?? string.Empty;
            this.trackPath = trackPath ?? string.Empty;
            this.reason = reason ?? string.Empty;
            this.addedAtUtc = addedAtUtc ?? string.Empty;
        }

        public string OwnerLevelKey => ownerLevelKey ?? string.Empty;
        public string OwnerPath => ownerPath ?? string.Empty;
        public string DirectorPath => directorPath ?? string.Empty;
        public string TimelinePath => timelinePath ?? string.Empty;
        public string TrackPath => trackPath ?? string.Empty;
        public string Reason => reason ?? string.Empty;
        public string AddedAtUtc => addedAtUtc ?? string.Empty;
    }

    /// <summary>
    /// 意図的な NULL の共有リスト（リポジトリに入れてチームで共有する。ユーザーの判断で仕様 8 章の方針から追加した機能）。
    /// ここに載っている階層の BindingLost は、誰の手元でも通知しない（履歴には残る）。
    /// 書き出しは Owner 階層キーの順に並べ、追加の順序によらず同じ内容になる（Git の差分・マージを読みやすくするため）。
    /// </summary>
    [Serializable]
    public sealed class IntentionalNullList
    {
        public const string FormatName = "TimelineBindingGuardian.IntentionalNullBindings";
        public const int CurrentFormatVersion = 1;

        [SerializeField] private string format = FormatName;
        [SerializeField] private int formatVersion = CurrentFormatVersion;
        [SerializeField] private List<IntentionalNullEntry> entries = new List<IntentionalNullEntry>();

        public IReadOnlyList<IntentionalNullEntry> Entries => entries ?? (entries = new List<IntentionalNullEntry>());

        public bool Contains(string ownerLevelKey)
        {
            return !string.IsNullOrEmpty(ownerLevelKey) && Entries.Any(e => e.OwnerLevelKey == ownerLevelKey);
        }

        /// <summary>
        /// 追加する（同じキーがあれば置き換える）。
        /// </summary>
        public void Add(IntentionalNullEntry entry)
        {
            Remove(entry.OwnerLevelKey);
            entries.Add(entry);
            Sort();
        }

        public bool Remove(string ownerLevelKey)
        {
            return (entries ?? (entries = new List<IntentionalNullEntry>())).RemoveAll(e => e.OwnerLevelKey == ownerLevelKey) > 0;
        }

        public string ToJson()
        {
            Sort();
            return JsonUtility.ToJson(this, true) + "\n";
        }

        /// <summary>
        /// 共有ファイルの内容を読む。形式が違う・壊れている（マージの衝突記号など）場合は false と理由を返す。
        /// </summary>
        public static bool TryParse(string json, out IntentionalNullList list, out string error)
        {
            list = null;
            error = string.Empty;
            try
            {
                list = JsonUtility.FromJson<IntentionalNullList>(json);
            }
            catch (Exception exception)
            {
                error = Lang.T("intentional.file.invalidJson", exception.Message);
                return false;
            }

            if (list == null || list.format != FormatName)
            {
                error = Lang.T("intentional.file.notThisFormat");
                list = null;
                return false;
            }

            if (list.formatVersion > CurrentFormatVersion)
            {
                error = Lang.T("intentional.file.newerFormat", list.formatVersion, CurrentFormatVersion);
                list = null;
                return false;
            }

            list.entries = (list.entries ?? new List<IntentionalNullEntry>()).Where(e => e != null && e.OwnerLevelKey.Length > 0).ToList();
            return true;
        }

        private void Sort()
        {
            entries = Entries.OrderBy(e => e.OwnerLevelKey, StringComparer.Ordinal).ToList();
        }
    }
}
