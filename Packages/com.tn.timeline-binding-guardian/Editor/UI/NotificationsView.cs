using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// Notifications タブ。新たに未バインドになった変化（BindingLost）の通知を一覧し、個別・一括で消去する。
    /// 消去はバインドの正常性を承認するものではなく、履歴・現在の状態・今後のイベントに影響しない。
    /// </summary>
    internal sealed class NotificationsView
    {
        private static readonly Color AttentionColor = new Color(0.95f, 0.65f, 0.2f);
        private static readonly Color ResolvedColor = new Color(0.5f, 0.8f, 0.5f);
        private static readonly Color MutedColor = new Color(0.6f, 0.6f, 0.6f);

        private readonly List<(NotificationRecord Notification, ChangeEvent Event)> _visible = new List<(NotificationRecord, ChangeEvent)>();
        private Func<string, LevelValue> _currentValue = key => null;
        private bool _showDismissed;
        private string _searchText = string.Empty;
        private HashSet<string> _eventFilter;

        private readonly Label _statusLabel;
        private readonly VisualElement _filterRow;
        private readonly Label _filterLabel;
        private readonly ListView _listView;
        private readonly ScrollView _details;
        private readonly Label _countLabel;

        public NotificationsView()
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1;

            Toolbar bar = new Toolbar();
            ToolbarToggle dismissedToggle = new ToolbarToggle { text = "Show dismissed" };
            dismissedToggle.RegisterValueChangedCallback(OnShowDismissedChanged);
            bar.Add(dismissedToggle);
            ToolbarSearchField search = new ToolbarSearchField();
            search.tooltip = "Search prefab, scene, director, or track";
            search.RegisterValueChangedCallback(OnSearchChanged);
            bar.Add(search);
            bar.Add(new ToolbarButton(OnDismissShownClicked) { text = "Dismiss Shown", tooltip = "Dismiss the unread notifications currently listed." });
            bar.Add(new ToolbarButton(OnDismissAllClicked) { text = "Dismiss All…", tooltip = "Dismiss every unread notification (asks for confirmation)." });
            ToolbarSpacer spacer = new ToolbarSpacer();
            spacer.style.flexGrow = 1;
            bar.Add(spacer);
            _countLabel = new Label();
            _countLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            bar.Add(_countLabel);
            Root.Add(bar);

            _filterRow = new VisualElement();
            _filterRow.style.flexDirection = FlexDirection.Row;
            _filterRow.style.display = DisplayStyle.None;
            _filterRow.style.paddingLeft = 6;
            _filterLabel = new Label();
            _filterLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            _filterRow.Add(_filterLabel);
            _filterRow.Add(new Button(OnClearFilterClicked) { text = "Clear" });
            Root.Add(_filterRow);

            _statusLabel = CreateMutedLabel("A notification means a binding became unbound. It is not necessarily an error (a parent prefab may clear it on purpose). Dismissing does not mark the binding as correct.");
            Root.Add(_statusLabel);

            TwoPaneSplitView split = new TwoPaneSplitView(0, 240, TwoPaneSplitViewOrientation.Vertical);
            split.style.flexGrow = 1;
            _listView = new ListView
            {
                makeItem = MakeItem,
                bindItem = BindItem,
                fixedItemHeight = 18,
                selectionType = SelectionType.Single,
                itemsSource = _visible,
            };
            _listView.selectionChanged += OnSelectionChanged;
            split.Add(_listView);
            _details = new ScrollView();
            _details.style.paddingLeft = 8;
            split.Add(_details);
            Root.Add(split);
        }

        public VisualElement Root { get; }

        public void Refresh()
        {
            GuardianHistory history = GuardianHistory.Shared;
            List<(NotificationRecord Notification, ChangeEvent Event)> all = history.GetNotifications(_showDismissed);
            _currentValue = history.CreateCurrentValueLookup();
            _visible.Clear();
            _visible.AddRange(all.Where(Matches));
            int unread = all.Count(n => n.Notification.IsUnread);
            _countLabel.text = $"{_visible.Count} shown, {unread} unread";
            _listView.ClearSelection();
            _listView.RefreshItems();
            ShowDetails(null);
        }

        /// <summary>
        /// 特定のイベント（Hierarchy アイコンから）だけを表示する。
        /// </summary>
        public void ShowEvents(IEnumerable<string> eventIds, string label)
        {
            _eventFilter = new HashSet<string>(eventIds);
            _filterLabel.text = $"Showing notifications for: {label}";
            _filterRow.style.display = DisplayStyle.Flex;
            Refresh();
        }

        private bool Matches((NotificationRecord Notification, ChangeEvent Event) item)
        {
            if (_eventFilter != null && !_eventFilter.Contains(item.Event.EventId))
            {
                return false;
            }

            if (string.IsNullOrEmpty(_searchText))
            {
                return true;
            }

            IEnumerable<string> texts = new[] { item.Event.Owner.AssetPathHint, item.Event.Director.HierarchyPathHint, item.Event.Track.TrackPathHint }
                .Concat(item.Event.AffectedContexts.Select(c => c.ScopePathHint));
            return texts.Any(t => !string.IsNullOrEmpty(t) && t.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void OnShowDismissedChanged(ChangeEvent<bool> changeEvent)
        {
            _showDismissed = changeEvent.newValue;
            Refresh();
        }

        private void OnSearchChanged(ChangeEvent<string> changeEvent)
        {
            _searchText = changeEvent.newValue ?? string.Empty;
            Refresh();
        }

        private void OnClearFilterClicked()
        {
            _eventFilter = null;
            _filterRow.style.display = DisplayStyle.None;
            Refresh();
        }

        private void OnDismissShownClicked()
        {
            Dismiss(_visible.Where(v => v.Notification.IsUnread).Select(v => v.Event.EventId).ToList());
        }

        private void OnDismissAllClicked()
        {
            List<string> unread = GuardianHistory.Shared.GetNotifications(false).Select(n => n.Event.EventId).ToList();
            if (unread.Count == 0)
            {
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog("Timeline Binding Guardian",
                $"Dismiss all {unread.Count} unread notification(s)?\nHistory and current bindings are not changed, and future changes are still notified.",
                "Dismiss All", "Cancel");
            if (confirmed)
            {
                Dismiss(unread);
            }
        }

        private void OnDismissOneClicked(string eventId)
        {
            Dismiss(new List<string> { eventId });
        }

        private void Dismiss(List<string> eventIds)
        {
            if (eventIds.Count == 0)
            {
                return;
            }

            if (!GuardianHistory.Shared.Dismiss(eventIds, out string error))
            {
                _statusLabel.text = $"Notifications could not be dismissed: {error}";
                _statusLabel.style.color = AttentionColor;
            }
        }

        private void OnSelectionChanged(IEnumerable<object> selection)
        {
            object selected = selection.FirstOrDefault();
            if (selected is ValueTuple<NotificationRecord, ChangeEvent> item)
            {
                ShowDetails(item);
                return;
            }

            ShowDetails(null);
        }

        private static VisualElement MakeItem()
        {
            Label label = new Label();
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            return label;
        }

        private void BindItem(VisualElement element, int index)
        {
            (NotificationRecord notification, ChangeEvent changeEvent) = _visible[index];
            bool resolved = NotificationRules.IsResolved(changeEvent, _currentValue);
            Label label = (Label)element;
            label.text = FormatSummary(notification, changeEvent, resolved);
            label.tooltip = label.text;
            label.style.color = GetColor(notification, resolved);
        }

        private string FormatSummary(NotificationRecord notification, ChangeEvent changeEvent, bool resolved)
        {
            List<string> flags = new List<string>();
            if (!notification.IsUnread)
            {
                flags.Add("dismissed");
            }

            if (resolved)
            {
                flags.Add("resolved now");
            }

            if (changeEvent.OriginUndetermined)
            {
                flags.Add("origin undetermined");
            }

            flags.Add(changeEvent.AffectsEffective ? $"affects {changeEvent.AffectedContexts.Count} context(s)" : "no effect on observed contexts");
            int unscanned = GetUnscannedScenes(changeEvent).Count;
            if (unscanned > 0)
            {
                flags.Add($"{unscanned} scene(s) not scanned yet");
            }

            string time = HistoryView.FormatLocalTime(changeEvent.ObservedAtUtc);
            string owner = Path.GetFileName(changeEvent.Owner.AssetPathHint);
            return $"{time}   {owner} › {changeEvent.Director.HierarchyPathHint} › {changeEvent.Track.TrackPathHint}   " +
                $"{HistoryView.FormatValue(changeEvent.Before)} → {HistoryView.FormatValue(changeEvent.After)}   [{string.Join(", ", flags)}]";
        }

        private static StyleColor GetColor(NotificationRecord notification, bool resolved)
        {
            if (!notification.IsUnread)
            {
                return new StyleColor(MutedColor);
            }

            return resolved ? new StyleColor(ResolvedColor) : new StyleColor(AttentionColor);
        }

        private void ShowDetails((NotificationRecord Notification, ChangeEvent Event)? item)
        {
            _details.Clear();
            if (item == null)
            {
                _details.Add(CreateMutedLabel("Select a notification to see details."));
                return;
            }

            NotificationRecord notification = item.Value.Notification;
            ChangeEvent changeEvent = item.Value.Event;
            bool resolved = NotificationRules.IsResolved(changeEvent, _currentValue);

            _details.Add(CreateHeader($"Newly unbound binding (observed {HistoryView.FormatLocalTime(changeEvent.ObservedAtUtc)})"));
            _details.Add(new Label($"Changed at: {changeEvent.Owner} ({changeEvent.Owner.AssetKind})"));
            _details.Add(new Label($"Director: {changeEvent.Director.HierarchyPathHint}"));
            _details.Add(new Label($"Track: {changeEvent.Track.TrackPathHint}   Timeline: {changeEvent.Track.TimelinePathHint}"));
            _details.Add(new Label($"Value at that level: {HistoryView.FormatValue(changeEvent.Before)} → {HistoryView.FormatValue(changeEvent.After)}"));
            _details.Add(new Label(resolved ? "Current state: bound again (resolved). The notification stays until dismissed." : "Current state: still unbound."));
            if (!notification.IsUnread)
            {
                _details.Add(CreateMutedLabel($"Dismissed at {HistoryView.FormatLocalTime(notification.DismissedAtUtc)}."));
            }

            if (changeEvent.OriginUndetermined)
            {
                _details.Add(CreateWarningLabel("Origin undetermined: the level that caused this change could not be identified."));
            }

            AddButtons(notification, changeEvent);
            AddAffected(changeEvent);
            AddUnscanned(changeEvent);
        }

        private void AddButtons(NotificationRecord notification, ChangeEvent changeEvent)
        {
            VisualElement buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            if (notification.IsUnread)
            {
                string eventId = changeEvent.EventId;
                buttons.Add(new Button(() => OnDismissOneClicked(eventId)) { text = "Dismiss" });
            }

            bool isPrefab = changeEvent.Owner.AssetKind != AssetKind.Scene;
            if (isPrefab)
            {
                string path = changeEvent.Owner.AssetPathHint;
                buttons.Add(new Button(() => OpenAsset(path)) { text = "Open Prefab" });
            }

            string timelinePath = changeEvent.Track.TimelinePathHint;
            buttons.Add(new Button(() => PingAsset(timelinePath)) { text = "Ping Timeline" });
            _details.Add(buttons);
        }

        private void AddAffected(ChangeEvent changeEvent)
        {
            _details.Add(CreateHeader($"Effective value in observed contexts ({changeEvent.AffectedContexts.Count})"));
            if (changeEvent.AffectedContexts.Count == 0)
            {
                _details.Add(CreateMutedLabel("No observed context changed its effective value (for example, an upper level overrides this binding, or no scene using it has been scanned yet)."));
                return;
            }

            foreach (AffectedContext context in changeEvent.AffectedContexts)
            {
                string directorPath = string.IsNullOrEmpty(context.DirectorIndexPathHint) ? context.DirectorPathHint : context.DirectorIndexPathHint;
                _details.Add(new Label($"{context.ScopePathHint}   {directorPath}   {HistoryView.FormatValue(context.EffectiveBefore)} → {HistoryView.FormatValue(context.EffectiveAfter)}"));
            }
        }

        private void AddUnscanned(ChangeEvent changeEvent)
        {
            List<string> unscanned = GetUnscannedScenes(changeEvent);
            if (unscanned.Count == 0)
            {
                return;
            }

            _details.Add(CreateHeader($"Scenes that may be affected but have not been scanned since this change ({unscanned.Count})"));
            foreach (string scene in unscanned)
            {
                _details.Add(new Label(scene));
            }

            _details.Add(CreateMutedLabel("These scenes are checked automatically when they are opened."));
        }

        /// <summary>
        /// 変更元の Prefab に依存する、履歴にある Scene のうち、この変更の観測以降に検査されていないもの。
        /// </summary>
        private static List<string> GetUnscannedScenes(ChangeEvent changeEvent)
        {
            if (changeEvent.Owner.AssetKind == AssetKind.Scene)
            {
                return new List<string>();
            }

            HistoryState state = GuardianHistory.Shared.Store.State;
            string ownerPath = AssetDatabase.GUIDToAssetPath(changeEvent.Owner.AssetGuid);
            HashSet<string> scanned = new HashSet<string>(state.Scopes
                .Where(s => string.CompareOrdinal(s.LastObservedAtUtc, changeEvent.ObservedAtUtc) >= 0)
                .Select(s => AssetDatabase.GUIDToAssetPath(s.ScopeGuid)));
            return DependencyIndex.GetDependentScopePaths(ownerPath, state.Scopes)
                .Where(p => p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) && !scanned.Contains(p))
                .ToList();
        }

        private static void OpenAsset(string assetPath)
        {
            Object asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (asset != null)
            {
                AssetDatabase.OpenAsset(asset);
            }
        }

        private static void PingAsset(string assetPath)
        {
            Object asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (asset != null)
            {
                EditorGUIUtility.PingObject(asset);
            }
        }

        private static Label CreateHeader(string text)
        {
            Label label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 8;
            label.style.marginBottom = 4;
            return label;
        }

        private static Label CreateMutedLabel(string text)
        {
            Label label = new Label(text);
            label.style.color = MutedColor;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.paddingLeft = 6;
            return label;
        }

        private static Label CreateWarningLabel(string text)
        {
            Label label = new Label(text);
            label.style.color = AttentionColor;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }
    }
}
