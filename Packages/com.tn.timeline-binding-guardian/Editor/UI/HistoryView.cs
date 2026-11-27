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
        // 期間・種別フィルタの内部値。表示文字列は表示時に Lang.T で作るので、翻訳されても判定は変わらない
        private const string PeriodLast24Hours = "last24Hours";
        private const string PeriodLast7Days = "last7Days";
        private const string PeriodSinceDate = "sinceDate";
        private const string PeriodAllTime = "allTime";
        // ビルド前検査の記録があるときだけ選択肢に出す
        private const string PeriodSinceLastBuild = "sinceLastBuild";
        private const string AllTypes = "*allTypes";
        private const string BindingChangesOnly = "*bindingChangesOnly";

        private static readonly Color AttentionColor = new Color(0.95f, 0.65f, 0.2f);
        private static readonly Color MutedColor = new Color(0.6f, 0.6f, 0.6f);

        private static readonly string[] PeriodChoices = { PeriodLast24Hours, PeriodLast7Days, PeriodSinceDate, PeriodAllTime };

        private readonly List<ChangeEvent> _visibleEvents = new List<ChangeEvent>();
        private string _period = PeriodAllTime;
        private string _typeFilter = BindingChangesOnly;
        private string _searchText = string.Empty;
        private string _sinceText = string.Empty;
        private string _contextKey = string.Empty;
        private HashSet<string> _contextOwnerLevelKeys = new HashSet<string>();

        private Label _storeLabel;
        private PopupField<string> _periodField;
        private BuildCheckRecord _latestBuild;
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
            Root.Add(CreateMutedLabel(Lang.T("views.history.timesNote")));

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
            CollectionViewUtility.UseLocalizedEmptyText(_listView);
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
            _latestBuild = store.State.LatestBuild;
            UpdatePeriodChoices();
            string size = (store.GetTotalSizeBytes() / 1024.0).ToString("0.0", CultureInfo.InvariantCulture);
            _storeLabel.text = store.IsWritable
                ? Lang.T("views.history.store.writable", store.Directory, size, store.State.Events.Count)
                : Lang.T("views.history.store.readOnly", store.Status, store.ErrorMessage, store.Directory);
            _storeLabel.style.color = store.IsWritable ? new StyleColor(MutedColor) : new StyleColor(AttentionColor);

            _visibleEvents.Clear();
            _visibleEvents.AddRange(store.State.Events.Where(Matches).OrderByDescending(e => e.ObservedAtUtc, StringComparer.Ordinal));
            _countLabel.text = Lang.T("views.history.count", _visibleEvents.Count);
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
            _contextFilterLabel.text = Lang.T("views.history.filter", label);
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

        /// <summary>
        /// Director の位置（名前＃兄弟の番号）を「記録時点」と分かる形で添える。番号は表示用で、識別には使っていない
        /// （兄弟が削除されると以後の番号はずれるため、一覧には出さずツールチップと詳細にだけ出す）。
        /// </summary>
        public static string AppendPosition(string text, ChangeEvent changeEvent)
        {
            string position = changeEvent.Director.IndexPathHint;
            return string.IsNullOrEmpty(position) ? text : text + "\n" + Lang.T("views.common.position", position);
        }

        public static void AddPositionLabel(VisualElement container, ChangeEvent changeEvent)
        {
            string position = changeEvent.Director.IndexPathHint;
            if (string.IsNullOrEmpty(position))
            {
                return;
            }

            Label label = new Label(Lang.T("views.common.position", position));
            label.style.color = MutedColor;
            label.tooltip = Lang.T("views.common.position.tooltip");
            container.Add(label);
        }

        public static string FormatLocalTime(string utcText)
        {
            bool parsed = DateTime.TryParse(utcText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime utc);
            return parsed ? utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : utcText;
        }

        private VisualElement CreateFilterBar()
        {
            Toolbar bar = new Toolbar();
            _periodField = new PopupField<string>(PeriodChoices.ToList(), _period, FormatPeriodChoice, FormatPeriodChoice);
            _periodField.style.width = 150;
            _periodField.RegisterValueChangedCallback(OnPeriodChanged);
            bar.Add(_periodField);

            _sinceField = new TextField { value = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
            _sinceField.tooltip = Lang.T("views.history.since.tooltip");
            _sinceField.style.width = 130;
            _sinceField.style.display = DisplayStyle.None;
            _sinceField.RegisterValueChangedCallback(OnSinceChanged);
            _sinceText = _sinceField.value;
            bar.Add(_sinceField);

            List<string> types = new List<string> { BindingChangesOnly, AllTypes };
            types.AddRange(Enum.GetNames(typeof(ChangeType)));
            PopupField<string> type = new PopupField<string>(types, _typeFilter, FormatTypeChoice, FormatTypeChoice);
            type.style.width = 170;
            type.RegisterValueChangedCallback(OnTypeChanged);
            bar.Add(type);

            ToolbarSearchField search = new ToolbarSearchField();
            search.tooltip = Lang.T("views.history.search.tooltip");
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

        /// <summary>
        /// 期間フィルタの内部値を表示文字列にする。
        /// </summary>
        private string FormatPeriodChoice(string period)
        {
            switch (period)
            {
                case PeriodSinceLastBuild:
                    return _latestBuild == null
                        ? Lang.T("views.history.period.sinceLastBuild.none")
                        : Lang.T("views.history.period.sinceLastBuild", FormatLocalTime(_latestBuild.CheckedAtUtc));
                case PeriodLast24Hours:
                    return Lang.T("views.history.period.last24Hours");
                case PeriodLast7Days:
                    return Lang.T("views.history.period.last7Days");
                case PeriodSinceDate:
                    return Lang.T("views.history.period.sinceDate");
                case PeriodAllTime:
                    return Lang.T("views.history.period.allTime");
                default:
                    return period;
            }
        }

        /// <summary>
        /// 種別フィルタの内部値を表示文字列にする。ChangeType の値は enum 名のまま表示する。
        /// </summary>
        private static string FormatTypeChoice(string typeFilter)
        {
            switch (typeFilter)
            {
                case AllTypes:
                    return Lang.T("views.history.type.all");
                case BindingChangesOnly:
                    return Lang.T("views.history.type.bindingChangesOnly");
                default:
                    return typeFilter;
            }
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
            _contextFilterRow.Add(new Button(OnClearContextClicked) { text = Lang.T("views.common.clear") });
            return _contextFilterRow;
        }

        /// <summary>
        /// 「前回ビルド以降」は、ビルド前検査の記録があるときだけ選択肢に出す。記録が無くなったら全期間に戻す。
        /// </summary>
        private void UpdatePeriodChoices()
        {
            List<string> choices = PeriodChoices.ToList();
            if (_latestBuild != null)
            {
                choices.Insert(choices.Count - 1, PeriodSinceLastBuild);
            }

            if (_period == PeriodSinceLastBuild && _latestBuild == null)
            {
                _period = PeriodAllTime;
            }

            _periodField.choices = choices;
            _periodField.SetValueWithoutNotify(_period);
        }

        private void OnPeriodChanged(ChangeEvent<string> changeEvent)
        {
            _period = changeEvent.newValue;
            _sinceField.style.display = _period == PeriodSinceDate ? DisplayStyle.Flex : DisplayStyle.None;
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
            if (_period == PeriodAllTime)
            {
                return true;
            }

            if (!DateTime.TryParse(changeEvent.ObservedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime observed))
            {
                return true;
            }

            if (_period == PeriodSinceLastBuild)
            {
                return _latestBuild == null || _latestBuild.IsObservedAfter(changeEvent.ObservedAtUtc);
            }

            DateTime threshold;
            switch (_period)
            {
                case PeriodLast24Hours:
                    threshold = DateTime.UtcNow.AddHours(-24);
                    break;
                case PeriodLast7Days:
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
            bool isLost = changeEvent.ChangeType == ChangeType.BindingLost;
            // 意図的な NULL の共有リストに載っている階層の変化は、印を付けて目立たせない（履歴としては残す）
            bool isIntentional = isLost && GuardianHistory.Shared.IsIntentional(changeEvent.OwnerLevelKey);
            label.text = isIntentional ? $"{FormatSummary(changeEvent)}   {Lang.T("intentional.mark")}" : FormatSummary(changeEvent);
            label.tooltip = AppendPosition(label.text, changeEvent);
            label.style.color = isLost && !isIntentional ? new StyleColor(AttentionColor) : new StyleColor(StyleKeyword.Null);
        }

        private static string FormatSummary(ChangeEvent changeEvent)
        {
            string time = FormatLocalTime(changeEvent.ObservedAtUtc);
            if (changeEvent.ChangeType == ChangeType.Initial)
            {
                return $"{time}   Initial   {changeEvent.Owner}   {Lang.RenderNote(changeEvent.Note)}";
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
                return $"[{Lang.T("views.common.impact.originUndetermined")}]";
            }

            int count = changeEvent.AffectedContexts.Count;
            return count > 0 ? $"[{Lang.T("views.common.impact.affects", count)}]" : $"[{Lang.T("views.common.impact.noEffect")}]";
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
                _details.Add(CreateMutedLabel(Lang.T("views.history.selectPrompt")));
                return;
            }

            _details.Add(CreateHeader(Lang.T("views.history.details.header", changeEvent.ChangeType, FormatLocalTime(changeEvent.ObservedAtUtc))));
            _details.Add(new Label(Lang.T("views.common.changedAt", changeEvent.Owner, changeEvent.Owner.AssetKind)));
            if (GuardianHistory.Shared.IsIntentional(changeEvent.OwnerLevelKey))
            {
                _details.Add(CreateMutedLabel(Lang.T("intentional.mark.tooltip")));
            }

            if (!string.IsNullOrEmpty(changeEvent.Director.HierarchyPathHint))
            {
                _details.Add(new Label(Lang.T("views.common.director", changeEvent.Director.HierarchyPathHint)));
                AddPositionLabel(_details, changeEvent);
                _details.Add(new Label(Lang.T("views.common.trackTimeline", changeEvent.Track.TrackPathHint, changeEvent.Track.TimelinePathHint)));
                _details.Add(new Label(Lang.T("views.common.valueAtLevel", FormatValue(changeEvent.Before), FormatValue(changeEvent.After))));
            }

            if (changeEvent.OriginUndetermined)
            {
                _details.Add(CreateWarningLabel(Lang.T("views.common.originUndetermined")));
            }

            if (!string.IsNullOrEmpty(changeEvent.Note))
            {
                _details.Add(CreateMutedLabel(Lang.RenderNote(changeEvent.Note)));
            }

            AddOpenButton(changeEvent.Owner);
            AddRestoreButton(changeEvent);
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
            Button open = new Button(() => OpenAsset(path)) { text = Lang.T("views.common.openPrefab") };
            open.style.alignSelf = Align.FlexStart;
            _details.Add(open);
        }

        /// <summary>
        /// 過去の状態（このイベントの変更前）を復元候補にする。可否と理由は Restore Preview で表示する。
        /// </summary>
        private void AddRestoreButton(ChangeEvent changeEvent)
        {
            bool isBindingChange = changeEvent.ChangeType != ChangeType.Initial && !string.IsNullOrEmpty(changeEvent.Director.GlobalObjectId);
            if (!isBindingChange)
            {
                return;
            }

            Button restore = new Button(() => RestorePreviewWindow.Open(changeEvent)) { text = Lang.T("views.history.restore") };
            restore.style.alignSelf = Align.FlexStart;
            _details.Add(restore);
        }

        private void AddAffectedContexts(ChangeEvent changeEvent)
        {
            if (changeEvent.ChangeType == ChangeType.Initial)
            {
                return;
            }

            _details.Add(CreateHeader(Lang.T("views.common.effectiveHeader", changeEvent.AffectedContexts.Count)));
            if (changeEvent.AffectedContexts.Count == 0)
            {
                _details.Add(CreateMutedLabel(Lang.T("views.history.noAffected")));
                return;
            }

            foreach (AffectedContext context in changeEvent.AffectedContexts)
            {
                string note = string.IsNullOrEmpty(context.Note) ? string.Empty : $"   ({Lang.RenderNote(context.Note)})";
                string directorPath = string.IsNullOrEmpty(context.DirectorIndexPathHint) ? context.DirectorPathHint : context.DirectorIndexPathHint;
                Label row = new Label($"{context.ScopePathHint}   {directorPath}   {FormatValue(context.EffectiveBefore)} → {FormatValue(context.EffectiveAfter)}{note}");
                row.tooltip = Lang.T("views.history.pathTooltip");
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
