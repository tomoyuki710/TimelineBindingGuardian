using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// プロジェクトごとの設定（UserSettings/TimelineBindingGuardian/settings.json。Git 管理外）。設定を触らなくても使えるよう、既定はすべて ON。
    /// </summary>
    [Serializable]
    public sealed class GuardianSettings
    {
        public const string FileName = "settings.json";

        private static GuardianSettings _instance;

        [SerializeField] private bool monitoringEnabled = true;
        [SerializeField] private bool hierarchyIconsEnabled = true;
        [SerializeField] private bool consoleWarningsEnabled = true;
        // 監視・記録から除外するフォルダ（Asset パス）。記録済みの履歴は消さない
        [SerializeField] private List<string> excludedFolders = new List<string>();

        public static event Action Changed;

        public static GuardianSettings Instance => _instance ?? (_instance = Load());

        public static string FilePath => Path.Combine(GuardianHistory.DefaultDirectory, FileName);

        // 保存・インポート・起動時の自動検査。OFF の間の変化は、次に検査したときに比較される
        public bool MonitoringEnabled
        {
            get => monitoringEnabled;
            set => SetValue(ref monitoringEnabled, value);
        }

        public bool HierarchyIconsEnabled
        {
            get => hierarchyIconsEnabled;
            set => SetValue(ref hierarchyIconsEnabled, value);
        }

        // 新しい未バインドを検出したときに Console へ集約した Warning を 1 回出す
        public bool ConsoleWarningsEnabled
        {
            get => consoleWarningsEnabled;
            set => SetValue(ref consoleWarningsEnabled, value);
        }

        public IReadOnlyList<string> ExcludedFolders => excludedFolders ?? (excludedFolders = new List<string>());

        public bool IsExcluded(string assetPath)
        {
            return PathExclusion.IsExcluded(assetPath, ExcludedFolders);
        }

        public void AddExcludedFolder(string folder)
        {
            string normalized = PathExclusion.NormalizeFolder(folder);
            bool exists = ExcludedFolders.Any(f => string.Equals(f, normalized, StringComparison.OrdinalIgnoreCase));
            if (normalized.Length == 0 || exists)
            {
                return;
            }

            excludedFolders.Add(normalized);
            Save();
            Changed?.Invoke();
        }

        public void RemoveExcludedFolder(string folder)
        {
            if (excludedFolders != null && excludedFolders.Remove(folder))
            {
                Save();
                Changed?.Invoke();
            }
        }

        private static GuardianSettings Load()
        {
            if (!File.Exists(FilePath))
            {
                return new GuardianSettings();
            }

            try
            {
                return JsonUtility.FromJson<GuardianSettings>(File.ReadAllText(FilePath, Encoding.UTF8)) ?? new GuardianSettings();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Timeline Binding Guardian] Settings could not be read. Defaults are used. {exception.Message}");
                return new GuardianSettings();
            }
        }

        private void SetValue(ref bool field, bool value)
        {
            if (field == value)
            {
                return;
            }

            field = value;
            Save();
            Changed?.Invoke();
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(GuardianHistory.DefaultDirectory);
                File.WriteAllText(FilePath, JsonUtility.ToJson(this, true), new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Timeline Binding Guardian] Settings could not be saved. {exception.Message}");
            }
        }
    }
}
