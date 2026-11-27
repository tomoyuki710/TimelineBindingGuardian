using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// Settings タブ。設定を触らなくても使える（既定はすべて ON）。
    /// </summary>
    internal sealed class SettingsView
    {
        private static readonly Color MutedColor = new Color(0.6f, 0.6f, 0.6f);

        private readonly Toggle _monitoring;
        private readonly Toggle _hierarchyIcons;
        private readonly Toggle _consoleWarnings;
        private readonly Label _storeLabel;
        private readonly VisualElement _excludedList;

        public SettingsView()
        {
            Root = new ScrollView();
            Root.style.flexGrow = 1;
            Root.style.paddingLeft = 8;
            Root.style.paddingTop = 6;

            _monitoring = new Toggle("Automatic monitoring");
            _monitoring.tooltip = "Scan automatically when scenes and prefabs are saved, imported, or opened, and when the editor starts.";
            _monitoring.RegisterValueChangedCallback(OnMonitoringChanged);
            Root.Add(_monitoring);
            Root.Add(CreateMutedLabel("While off, changes are not observed. They are compared at the next scan (the observation time will be the time of that scan)."));

            _hierarchyIcons = new Toggle("Hierarchy icons");
            _hierarchyIcons.tooltip = "Show the number of unread notifications next to directors and their parents in the Hierarchy.";
            _hierarchyIcons.RegisterValueChangedCallback(OnHierarchyIconsChanged);
            Root.Add(_hierarchyIcons);

            _consoleWarnings = new Toggle("Console warnings");
            _consoleWarnings.tooltip = "Write one summarized warning to the Console when new unbound bindings are detected.";
            _consoleWarnings.RegisterValueChangedCallback(OnConsoleWarningsChanged);
            Root.Add(_consoleWarnings);

            Root.Add(CreateHeader("Excluded folders"));
            Root.Add(CreateMutedLabel("Scenes, prefabs, and timelines in these folders (and their subfolders) are not monitored or recorded, even by Scan Now. Existing history is kept."));
            _excludedList = new VisualElement();
            Root.Add(_excludedList);
            Button add = new Button(OnAddFolderClicked) { text = "Add Folder…" };
            add.style.alignSelf = Align.FlexStart;
            Root.Add(add);

            Root.Add(CreateHeader("History"));
            _storeLabel = new Label();
            _storeLabel.style.whiteSpace = WhiteSpace.Normal;
            Root.Add(_storeLabel);
            Root.Add(CreateMutedLabel("History and notification states are stored only on this computer (UserSettings is normally not under version control). Nothing is sent over the network."));
            Root.Add(CreateMutedLabel("History starts at the first observation. Bindings that were already broken before that cannot be detected, and changes that happen and revert between scans may not be observed."));
        }

        public ScrollView Root { get; }

        public void Refresh()
        {
            GuardianSettings settings = GuardianSettings.Instance;
            _monitoring.SetValueWithoutNotify(settings.MonitoringEnabled);
            _hierarchyIcons.SetValueWithoutNotify(settings.HierarchyIconsEnabled);
            _consoleWarnings.SetValueWithoutNotify(settings.ConsoleWarningsEnabled);

            RefreshExcludedFolders(settings);

            HistoryStore store = GuardianHistory.Shared.Store;
            string size = (store.GetTotalSizeBytes() / 1024.0).ToString("0.0", CultureInfo.InvariantCulture);
            string status = store.IsWritable ? "OK" : $"read-only ({store.Status}): {store.ErrorMessage}";
            _storeLabel.text = $"Folder: {store.Directory}\nSize on disk: {size} KB   Scopes: {store.State.Scopes.Count}   Events: {store.State.Events.Count}   Status: {status}";
        }

        private void RefreshExcludedFolders(GuardianSettings settings)
        {
            _excludedList.Clear();
            if (settings.ExcludedFolders.Count == 0)
            {
                _excludedList.Add(CreateMutedLabel("(none)"));
                return;
            }

            foreach (string folder in settings.ExcludedFolders)
            {
                VisualElement row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                Label label = new Label(folder);
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                label.style.minWidth = 300;
                row.Add(label);
                string target = folder;
                row.Add(new Button(() => OnRemoveFolderClicked(target)) { text = "Remove" });
                _excludedList.Add(row);
            }
        }

        private void OnAddFolderClicked()
        {
            string selected = EditorUtility.OpenFolderPanel("Exclude folder from Timeline Binding Guardian", Application.dataPath, string.Empty);
            if (string.IsNullOrEmpty(selected))
            {
                return;
            }

            string assetPath = ToAssetPath(selected);
            if (assetPath == null)
            {
                EditorUtility.DisplayDialog("Timeline Binding Guardian", "Select a folder inside this project's Assets folder.", "OK");
                return;
            }

            GuardianSettings.Instance.AddExcludedFolder(assetPath);
        }

        private void OnRemoveFolderClicked(string folder)
        {
            GuardianSettings.Instance.RemoveExcludedFolder(folder);
        }

        /// <summary>
        /// 絶対パスをプロジェクトの Asset パス（Assets/...）に変換する。Assets の外なら null。
        /// </summary>
        private static string ToAssetPath(string absolutePath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/') ?? string.Empty;
            string path = absolutePath.Replace('\\', '/');
            bool insideProject = path.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase);
            if (!insideProject)
            {
                return null;
            }

            string relative = path.Substring(projectRoot.Length + 1);
            bool isAssets = relative.Equals("Assets", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
            return isAssets ? relative : null;
        }

        private void OnMonitoringChanged(ChangeEvent<bool> changeEvent)
        {
            GuardianSettings.Instance.MonitoringEnabled = changeEvent.newValue;
        }

        private void OnHierarchyIconsChanged(ChangeEvent<bool> changeEvent)
        {
            GuardianSettings.Instance.HierarchyIconsEnabled = changeEvent.newValue;
        }

        private void OnConsoleWarningsChanged(ChangeEvent<bool> changeEvent)
        {
            GuardianSettings.Instance.ConsoleWarningsEnabled = changeEvent.newValue;
        }

        private static Label CreateHeader(string text)
        {
            Label label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 10;
            label.style.marginBottom = 4;
            return label;
        }

        private static Label CreateMutedLabel(string text)
        {
            Label label = new Label(text);
            label.style.color = MutedColor;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginBottom = 4;
            return label;
        }
    }
}
