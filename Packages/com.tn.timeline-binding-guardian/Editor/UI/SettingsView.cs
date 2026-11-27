using System;
using System.Collections.Generic;
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

        private readonly PopupField<LanguagePreference> _language;
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

            List<LanguagePreference> languages = new List<LanguagePreference> { LanguagePreference.Auto, LanguagePreference.English, LanguagePreference.Japanese };
            _language = new PopupField<LanguagePreference>(Lang.T("common.language"), languages, GuardianSettings.Instance.Language, FormatLanguageChoice, FormatLanguageChoice);
            _language.RegisterValueChangedCallback(OnLanguageChanged);
            Root.Add(_language);

            _monitoring = new Toggle(Lang.T("views.settings.monitoring"));
            _monitoring.tooltip = Lang.T("views.settings.monitoring.tooltip");
            _monitoring.RegisterValueChangedCallback(OnMonitoringChanged);
            Root.Add(_monitoring);
            Root.Add(CreateMutedLabel(Lang.T("views.settings.monitoring.note")));

            _hierarchyIcons = new Toggle(Lang.T("views.settings.hierarchyIcons"));
            _hierarchyIcons.tooltip = Lang.T("views.settings.hierarchyIcons.tooltip");
            _hierarchyIcons.RegisterValueChangedCallback(OnHierarchyIconsChanged);
            Root.Add(_hierarchyIcons);

            _consoleWarnings = new Toggle(Lang.T("views.settings.consoleWarnings"));
            _consoleWarnings.tooltip = Lang.T("views.settings.consoleWarnings.tooltip");
            _consoleWarnings.RegisterValueChangedCallback(OnConsoleWarningsChanged);
            Root.Add(_consoleWarnings);

            Root.Add(CreateHeader(Lang.T("views.settings.excluded.header")));
            Root.Add(CreateMutedLabel(Lang.T("views.settings.excluded.note", Lang.T("window.scanNow"))));
            _excludedList = new VisualElement();
            Root.Add(_excludedList);
            Button add = new Button(OnAddFolderClicked) { text = Lang.T("views.settings.excluded.add") };
            add.style.alignSelf = Align.FlexStart;
            Root.Add(add);

            Root.Add(CreateHeader(Lang.T("views.settings.history.header")));
            _storeLabel = new Label();
            _storeLabel.style.whiteSpace = WhiteSpace.Normal;
            Root.Add(_storeLabel);
            Root.Add(CreateMutedLabel(Lang.T("views.settings.history.privacy")));
            Root.Add(CreateMutedLabel(Lang.T("views.settings.history.limitation")));
        }

        public ScrollView Root { get; }

        public void Refresh()
        {
            GuardianSettings settings = GuardianSettings.Instance;
            _language.SetValueWithoutNotify(settings.Language);
            _monitoring.SetValueWithoutNotify(settings.MonitoringEnabled);
            _hierarchyIcons.SetValueWithoutNotify(settings.HierarchyIconsEnabled);
            _consoleWarnings.SetValueWithoutNotify(settings.ConsoleWarningsEnabled);

            RefreshExcludedFolders(settings);

            HistoryStore store = GuardianHistory.Shared.Store;
            string size = (store.GetTotalSizeBytes() / 1024.0).ToString("0.0", CultureInfo.InvariantCulture);
            string status = store.IsWritable ? Lang.T("views.settings.history.statusOk") : Lang.T("views.settings.history.statusReadOnly", store.Status, store.ErrorMessage);
            _storeLabel.text = Lang.T("views.settings.history.store", store.Directory, size, store.State.Scopes.Count, store.State.Events.Count, status);
        }

        private void RefreshExcludedFolders(GuardianSettings settings)
        {
            _excludedList.Clear();
            if (settings.ExcludedFolders.Count == 0)
            {
                _excludedList.Add(CreateMutedLabel(Lang.T("views.settings.excluded.none")));
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
                row.Add(new Button(() => OnRemoveFolderClicked(target)) { text = Lang.T("views.settings.excluded.remove") });
                _excludedList.Add(row);
            }
        }

        private void OnAddFolderClicked()
        {
            string selected = EditorUtility.OpenFolderPanel(Lang.T("views.settings.excluded.panelTitle"), Application.dataPath, string.Empty);
            if (string.IsNullOrEmpty(selected))
            {
                return;
            }

            string assetPath = ToAssetPath(selected);
            if (assetPath == null)
            {
                EditorUtility.DisplayDialog("Timeline Binding Guardian", Lang.T("views.settings.excluded.outsideAssets"), Lang.T("views.common.ok"));
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

        /// <summary>
        /// 言語の選択肢の表示文字列。
        /// </summary>
        private static string FormatLanguageChoice(LanguagePreference preference)
        {
            switch (preference)
            {
                case LanguagePreference.English:
                    return Lang.T("common.language.english");
                case LanguagePreference.Japanese:
                    return Lang.T("common.language.japanese");
                default:
                    return Lang.T("common.language.auto");
            }
        }

        private void OnLanguageChanged(ChangeEvent<LanguagePreference> changeEvent)
        {
            GuardianSettings.Instance.Language = changeEvent.newValue;
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
