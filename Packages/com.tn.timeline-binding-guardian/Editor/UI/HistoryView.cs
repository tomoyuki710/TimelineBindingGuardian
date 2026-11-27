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
    /// History タブ。変更イベントを期間・種別・文字列・特定 Track で絞り込んで表示する（読み取り専用）。
    /// </summary>
    internal sealed class HistoryView
    {
        private const string AllTypes = "All types";
        private const string BindingChangesOnly = "Binding changes only";

        private static readonly Color AttentionColor = new Color(0.95f, 0.65f, 0.2f);
        private static readonly Color MutedColor = new Color(0.6f, 0.6f, 0.6f);

        private static readonly string[] PeriodChoices = { "Last 24 hours", "Last 7 days", "Since date", "All time" };

        private readonly List<ChangeEvent> _visibleEvents = new List<ChangeEvent>();
        private string _period = "All time";
        private string _typeFilter = BindingChangesOnly;
        private string _searchText = string.Empty;
        private string _sinceText = string.Empty;
        private string _contextKey = string.Empty;
        private HashSet<string> _contextOwnerLevelKeys = new HashSet<string>();

        private Label _storeLabel;
        private TextField _sinceField;
        private VisualElement _contextFilterRow;
        private Label _contextFilterLabel;
        private ListView _listView;
        private ScrollView _details;
        private Label _countLabel;

        public HistoryView()
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1;
            Root.Add(CreateFilterBar());
            Root.Add(CreateContextFilterRow());

            _storeLabel = CreateMutedLabel(string.Empty);
            Root.Add(_storeLabel);
            Root.Add(CreateMutedLabel("Times are observation times (when a scan noticed the change), not when the change was made or who made it."));

            TwoPaneSplitView split = new TwoPaneSplitView(0, 260, TwoPaneSplitViewOrientation.Vertical);
            split.style.flexGrow = 1;
            _listView = new ListView
            {
                makeItem = MakeItem,
                bindItem = BindItem,
                fixedItemHeight = 18,
                selectionType = SelectionType.Single,
                itemsSource = _visibleEvents,
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
            HistoryStore store = history.Store;
            string size = (store.GetTotalSizeBytes() / 1024.0).ToString("0.0", CultureInfo.InvariantCulture);
            _storeLabel.text = store.IsWritable
                ? $"History folder: {store.Directory} ({size} KB on disk, {store.State.Events.Count} event(s), local only)"
                : $"History is read-only ({store.Status}): {store.ErrorMessage}  Folder: {store.Directory}";
            _storeLabel.style.color = store.IsWritable ? new StyleColor(MutedColor) : new StyleColor(AttentionColor);

            _visibleEvents.Clear();
            _visibleEvents.AddRange(store.State.Events.Where(Matches).OrderByDescending(e => e.ObservedAtUtc, StringComparer.Ordinal));
            _countLabel.text = $"{_visibleEvents.Count} event(s)";
            _listView.ClearSelection();
            _listView.RefreshItems();
            ShowDetails(null);
        }

        /// <summary>
        /// 特定の文脈（Scene 内の Director × Track）に関係するイベントだけを表示する。
        /// </summary>
        public void ShowContext(string bindingKey, IEnumerable<string> ownerLevelKeys, string label)
        {
            _contextKey = bindingKey;
            _contextOwnerLevelKeys = new HashSet<string>(ownerLevelKeys);
            _contextFilterLabel.text = $"Showing history for: {label}";
            _contextFilterRow.style.display = DisplayStyle.Flex;
            Refresh();
        }

        /// <summary>
        /// 文脈に関係するイベント（影響文脈に含まれる、またはその文脈のいずれかの階層で起きた変化）を新しい順に返す。
        /// </summary>
        public static List<ChangeEvent> FindEventsForContext(HistoryState state, string bindingKey, ICollection<string> ownerLevelKeys)
        {
            return state.Events
                .Where(e => IsRelatedToContext(e, bindingKey, ownerLevelKeys))
                .OrderByDescending(e => e.ObservedAtUtc, StringComparer.Ordinal)
                .ToList();
        }

        public static string FormatLocalTime(string utcText)
        {
            bool parsed = DateTime.TryParse(utcText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime utc);
            return parsed ? utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : utcText;
        }

        private VisualElement CreateFilterBar()
        {
            Toolbar bar = new Toolbar();
            PopupField<string> period = new PopupField<string>(PeriodChoices.ToList(), _period);
            period.style.width = 120;
            period.RegisterValueChangedCallback(OnPeriodChanged);
            bar.Add(period);

            _sinceField = new TextField { value = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
            _sinceField.tooltip = "Local date/time, e.g. 2026-11-27 or 2026-11-27 10:00";
            _sinceField.style.width = 130;
            _sinceField.style.display = DisplayStyle.None;
            _sinceField.RegisterValueChangedCallback(OnSinceChanged);
            _sinceText = _sinceField.value;
            bar.Add(_sinceField);

            List<string> types = new List<string> { BindingChangesOnly, AllTypes };
            types.AddRange(Enum.GetNames(typeof(ChangeType)));
            PopupField<string> type = new PopupField<string>(types, _typeFilter);
            type.style.width = 170;
            type.RegisterValueChangedCallback(OnTypeChanged);
            bar.Add(type);

            ToolbarSearchField search = new ToolbarSearchField();
            search.tooltip = "Search scene, prefab, director, or track";
            search.RegisterValueChangedCallback(OnSearchChanged);
            bar.Add(search);

            ToolbarSpacer spacer = new ToolbarSpacer();
            spacer.style.flexGrow = 1;
            bar.Add(spacer);
            _countLabel = new Label();
            _countLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            bar.Add(_countLabel);
            return bar;
        }

        private VisualElement CreateContextFilterRow()
        {
            _contextFilterRow = new VisualElement();
            _contextFilterRow.style.flexDirection = FlexDirection.Row;
            _contextFilterRow.style.display = DisplayStyle.None;
            _contextFilterRow.style.paddingLeft = 6;
            _contextFilterLabel = new Label();
            _contextFilterLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            _contextFilterRow.Add(_contextFilterLabel);
            _contextFilterRow.Add(new Button(OnClearContextClicked) { text = "Clear" });
            return _contextFilterRow;
        }

        private void OnPeriodChanged(ChangeEvent<string> changeEvent)
        {
            _period = changeEvent.newValue;
            _sinceField.style.display = _period == "Since date" ? DisplayStyle.Flex : DisplayStyle.None;
            Refresh();
        }

        private void OnSinceChanged(ChangeEvent<string> changeEvent)
        {
            _sinceText = changeEvent.newValue ?? string.Empty;
            Refresh();
        }

        private void OnTypeChanged(ChangeEvent<string> changeEvent)
        {
            _typeFilter = changeEvent.newValue;
            Refresh();
        }

        private void OnSearchChanged(ChangeEvent<string> changeEvent)
        {
            _searchText = changeEvent.newValue ?? string.Empty;
            Refresh();
        }

        private void OnClearContextClicked()
        {
            _contextKey = string.Empty;
            _contextOwnerLevelKeys.Clear();
            _contextFilterRow.style.display = DisplayStyle.None;
            Refresh();
        }

        private void OnSelectionChanged(IEnumerable<object> selection)
        {
            ShowDetails(selection.OfType<ChangeEvent>().FirstOrDefault());
        }

        private bool Matches(ChangeEvent changeEvent)
        {
            if (!MatchesPeriod(changeEvent) || !MatchesType(changeEvent))
            {
                return false;
            }

            bool hasContext = !string.IsNullOrEmpty(_contextKey);
            if (hasContext && !IsRelatedToContext(changeEvent, _contextKey, _contextOwnerLevelKeys))
            {
                return false;
            }

            return string.IsNullOrEmpty(_searchText) || MatchesSearch(changeEvent);
        }

        private bool MatchesPeriod(ChangeEvent changeEvent)
        {
            if (_period == "All time")
            {
                return true;
            }

            if (!DateTime.TryParse(changeEvent.ObservedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime observed))
            {
                return true;
            }

            DateTime threshold;
            switch (_period)
            {
                case "Last 24 hours":
                    threshold = DateTime.UtcNow.AddHours(-24);
                    break;
                case "Last 7 days":
                    threshold = DateTime.UtcNow.AddDays(-7);
                    break;
                default:
                    bool parsed = DateTime.TryParse(_sinceText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime since);
                    if (!parsed)
                    {
                        return true;
                    }

                    threshold = since.ToUniversalTime();
                    break;
            }

            return observed.ToUniversalTime() >= threshold;
        }

        private bool MatchesType(ChangeEvent changeEvent)
        {
            if (_typeFilter == AllTypes)
            {
                return true;
            }

            if (_typeFilter == BindingChangesOnly)
            {
                return changeEvent.ChangeType != ChangeType.Initial;
            }

            return changeEvent.ChangeType.ToString() == _typeFilter;
        }

        private bool MatchesSearch(ChangeEvent changeEvent)
        {
            IEnumerable<string> texts = new[]
            {
                changeEvent.Owner.AssetPathHint,
                changeEvent.Director.HierarchyPathHint,
                changeEvent.Track.TrackPathHint,
                changeEvent.Track.TimelinePathHint,
            }.Concat(changeEvent.AffectedContexts.SelectMany(c => new[] { c.ScopePathHint, c.DirectorPathHint }));
            return texts.Any(t => !string.IsNullOrEmpty(t) && t.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsRelatedToContext(ChangeEvent changeEvent, string bindingKey, ICollection<string> ownerLevelKeys)
        {
            bool isOwnerLevel = ownerLevelKeys.Contains(changeEvent.OwnerLevelKey) || changeEvent.OwnerLevelKey == bindingKey;
            return isOwnerLevel || changeEvent.AffectedContexts.Any(c => c.ContextBindingKey == bindingKey);
        }

        private static VisualElement MakeItem()
        {
            Label label = new Label();
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            return label;
        }

        private void BindItem(VisualElement element, int index)
        {
            ChangeEvent changeEvent = _visibleEvents[index];
            Label label = (Label)element;
            label.text = FormatSummary(changeEvent);
            label.tooltip = label.text;
            bool isLost = changeEvent.ChangeType == ChangeType.BindingLost;
            label.style.color = isLost ? new StyleColor(AttentionColor) : new StyleColor(StyleKeyword.Null);
        }

        private static string FormatSummary(ChangeEvent changeEvent)
        {
            string time = FormatLocalTime(changeEvent.ObservedAtUtc);
            if (changeEvent.ChangeType == ChangeType.Initial)
            {
                return $"{time}   Initial   {changeEvent.Owner}   {changeEvent.Note}";
            }

            string owner = Path.GetFileName(changeEvent.Owner.AssetPathHint);
            string where = $"{changeEvent.Director.HierarchyPathHint} › {changeEvent.Track.TrackPathHint}";
            string values = $"{FormatValue(changeEvent.Before)} → {FormatValue(changeEvent.After)}";
            string impact = FormatImpact(changeEvent);
            return $"{time}   {changeEvent.ChangeType}   {owner}   {where}   {values}   {impact}";
        }

        private static string FormatImpact(ChangeEvent changeEvent)
        {
            if (changeEvent.OriginUndetermined)
            {
                return "[origin undetermined]";
            }

            int count = changeEvent.AffectedContexts.Count;
            return count > 0 ? $"[affects {count} context(s)]" : "[no effect on observed contexts]";
        }

        public static string FormatValue(LevelValue value)
        {
            switch (value.ValueState)
            {
                case ValueState.Bound:
                    return $"{value.TargetHint} [{value.TargetType}]";
                case ValueState.NoEntry:
                    return "No Entry";
                case ValueState.MissingReference:
                    return "Missing Reference";
                case ValueState.NotApplicable:
                    return "-";
                default:
                    return value.ValueState.ToString();
            }
        }

        private void ShowDetails(ChangeEvent changeEvent)
        {
            _details.Clear();
            if (changeEvent == null)
            {
                _details.Add(CreateMutedLabel("Select an event to see details. Null is not necessarily an error; a parent prefab may clear a binding on purpose."));
                return;
            }

            _details.Add(CreateHeader($"{changeEvent.ChangeType}   (observed {FormatLocalTime(changeEvent.ObservedAtUtc)})"));
            _details.Add(new Label($"Changed at: {changeEvent.Owner} ({changeEvent.Owner.AssetKind})"));
            if (!string.IsNullOrEmpty(changeEvent.Director.HierarchyPathHint))
            {
                _details.Add(new Label($"Director: {changeEvent.Director.HierarchyPathHint}"));
                _details.Add(new Label($"Track: {changeEvent.Track.TrackPathHint}   Timeline: {changeEvent.Track.TimelinePathHint}"));
                _details.Add(new Label($"Value at that level: {FormatValue(changeEvent.Before)} → {FormatValue(changeEvent.After)}"));
            }

            if (changeEvent.OriginUndetermined)
            {
                _details.Add(CreateWarningLabel("Origin undetermined: the level that caused this change could not be identified."));
            }

            if (!string.IsNullOrEmpty(changeEvent.Note))
            {
                _details.Add(CreateMutedLabel(changeEvent.Note));
            }

            AddOpenButton(changeEvent.Owner);
            AddAffectedContexts(changeEvent);
        }

        private void AddOpenButton(AssetLocator owner)
        {
            bool isPrefab = owner.AssetKind != AssetKind.Scene && !string.IsNullOrEmpty(owner.AssetPathHint);
            if (!isPrefab)
            {
                return;
            }

            string path = owner.AssetPathHint;
            Button open = new Button(() => OpenAsset(path)) { text = "Open Prefab" };
            open.style.alignSelf = Align.FlexStart;
            _details.Add(open);
        }

        private void AddAffectedContexts(ChangeEvent changeEvent)
        {
            if (changeEvent.ChangeType == ChangeType.Initial)
            {
                return;
            }

            _details.Add(CreateHeader($"Effective value in observed contexts ({changeEvent.AffectedContexts.Count})"));
            if (changeEvent.AffectedContexts.Count == 0)
            {
                _details.Add(CreateMutedLabel("No observed scene context changed its effective value (for example, an upper level overrides this binding)."));
                return;
            }

            foreach (AffectedContext context in changeEvent.AffectedContexts)
            {
                string note = string.IsNullOrEmpty(context.Note) ? string.Empty : $"   ({context.Note})";
                string directorPath = string.IsNullOrEmpty(context.DirectorIndexPathHint) ? context.DirectorPathHint : context.DirectorIndexPathHint;
                Label row = new Label($"{context.ScopePathHint}   {directorPath}   {FormatValue(context.EffectiveBefore)} → {FormatValue(context.EffectiveAfter)}{note}");
                row.tooltip = "Path format: Name#SiblingIndex (distinguishes instances with the same name).";
                _details.Add(row);
            }
        }

        private static void OpenAsset(string assetPath)
        {
            Object asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (asset != null)
            {
                AssetDatabase.OpenAsset(asset);
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
