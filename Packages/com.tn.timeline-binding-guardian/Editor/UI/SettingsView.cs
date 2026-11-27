using System.Globalization;
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

            HistoryStore store = GuardianHistory.Shared.Store;
            string size = (store.GetTotalSizeBytes() / 1024.0).ToString("0.0", CultureInfo.InvariantCulture);
            string status = store.IsWritable ? "OK" : $"read-only ({store.Status}): {store.ErrorMessage}";
            _storeLabel.text = $"Folder: {store.Directory}\nSize on disk: {size} KB   Scopes: {store.State.Scopes.Count}   Events: {store.State.Events.Count}   Status: {status}";
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
