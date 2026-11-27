using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// Timeline Binding Guardian のメインウィンドウ（Phase 1: Scene Bindings の手動スキャン表示のみ）。
    /// スキャンはユーザーが Scan Now を押したときだけ実行し、GUI 描画中には行わない。
    /// </summary>
    public sealed class GuardianWindow : EditorWindow
    {
        private enum ScanScope
        {
            ActiveScene,
            LoadedScenes,
        }

        private enum NodeKind
        {
            Scene,
            Instance,
            Director,
            Track,
        }

        private sealed class NodeData
        {
            public NodeKind Kind;
            public string Label;
            public ScanResult Scan;
            public DirectorObservation Director;
            public TrackBindingObservation Track;
            public bool NeedsAttention;
        }

        private static readonly Color AttentionColor = new Color(0.95f, 0.65f, 0.2f);
        private static readonly Color OriginRowColor = new Color(0.3f, 0.5f, 0.8f, 0.25f);
        private static readonly Color MutedColor = new Color(0.6f, 0.6f, 0.6f);

        private readonly List<ScanResult> _results = new List<ScanResult>();
        private ScanScope _scope = ScanScope.ActiveScene;
        private string _searchText = string.Empty;
        private bool _lostOnly;
        private bool _undeterminedOnly;
        private DateTime _lastScanLocal;
        private int _nextId;

        private enum Tab
        {
            Notifications,
            SceneBindings,
            History,
            Settings,
        }

        private Label _headerLabel;
        private Label _statusLabel;
        private TreeView _treeView;
        private ScrollView _details;
        private VisualElement _bindingsTab;
        private HistoryView _historyView;
        private NotificationsView _notificationsView;
        private SettingsView _settingsView;
        private readonly Dictionary<Tab, ToolbarToggle> _tabToggles = new Dictionary<Tab, ToolbarToggle>();
        private Tab _currentTab = Tab.Notifications;
        // 表示言語を切り替えたときに作り直せるよう、文言ではなく記録結果そのものを持つ
        private RecordSummary _lastRecordSummary;
        private bool _subscribed;
        // 前回 UI を作ったときの表示言語（変わっていたら UI を作り直す）
        private UiLanguage _builtLanguage;
        private bool _refreshPending;
        // 詳細に表示中のノード（登録・解除の後に表示し直すため）
        private NodeData _selectedNode;
        private bool _rebuildScheduled;

        [MenuItem("Window/Timeline Binding Guardian")]
        private static void Open()
        {
            GetOrCreateWindow();
        }

        /// <summary>
        /// 指定したイベントの通知だけを表示した状態でウィンドウを開く（Hierarchy アイコンから）。
        /// </summary>
        public static void ShowNotificationsFor(IEnumerable<string> eventIds, string label)
        {
            GuardianWindow window = GetOrCreateWindow();
            window.ShowTab(Tab.Notifications);
            window._notificationsView.ShowEvents(eventIds, label);
        }

        private static GuardianWindow GetOrCreateWindow()
        {
            GuardianWindow window = GetWindow<GuardianWindow>();
            window.titleContent = new GUIContent("Binding Guardian");
            window.minSize = new Vector2(720, 360);
            return window;
        }

        private void CreateGUI()
        {
            BuildUI();
            Subscribe();
            // 待機中の自動検査の件数などを表示し続ける（件数を数えるだけで検査はしない）
            rootVisualElement.schedule.Execute(UpdateHeader).Every(1000);
        }

        /// <summary>
        /// UI を組み立てる。言語の切り替え時にも呼ぶため、表示中のタブとスキャン結果はそのまま引き継ぐ。
        /// </summary>
        private void BuildUI()
        {
            _builtLanguage = Lang.Current;
            _tabToggles.Clear();
            rootVisualElement.Add(CreateToolbar());

            _headerLabel = new Label();
            _headerLabel.style.paddingLeft = 6;
            _headerLabel.style.paddingTop = 2;
            _headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            rootVisualElement.Add(_headerLabel);

            _statusLabel = new Label(Lang.T("window.status.initial", Lang.T("window.scanNow")));
            _statusLabel.style.paddingLeft = 6;
            _statusLabel.style.paddingTop = 2;
            _statusLabel.style.paddingBottom = 2;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            rootVisualElement.Add(_statusLabel);
            rootVisualElement.Add(CreateTabBar());

            _notificationsView = new NotificationsView();
            rootVisualElement.Add(_notificationsView.Root);

            _bindingsTab = new TwoPaneSplitView(0, 340, TwoPaneSplitViewOrientation.Horizontal);
            _bindingsTab.style.flexGrow = 1;
            _bindingsTab.Add(CreateTreePane());
            _details = new ScrollView();
            _details.style.paddingLeft = 8;
            _details.style.paddingRight = 8;
            _bindingsTab.Add(_details);
            rootVisualElement.Add(_bindingsTab);

            _historyView = new HistoryView();
            rootVisualElement.Add(_historyView.Root);

            _settingsView = new SettingsView();
            rootVisualElement.Add(_settingsView.Root);

            // 作り直したときは、前回のスキャン結果を表示し直す
            if (_results.Count > 0)
            {
                UpdateStatus();
                RebuildTree();
            }

            ShowDetailsPlaceholder();
            ShowTab(_currentTab);
        }

        /// <summary>
        /// 表示言語が前回 UI を作ったときから変わっていれば、UI を作り直す。
        /// </summary>
        private void RebuildIfLanguageChanged()
        {
            _rebuildScheduled = false;
            if (Lang.Current == _builtLanguage)
            {
                return;
            }

            rootVisualElement.Clear();
            BuildUI();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (_subscribed)
            {
                return;
            }

            GuardianHistory.Changed += OnHistoryChanged;
            GuardianSettings.Changed += OnSettingsChanged;
            ScanScheduler.Processed += OnAutoScanProcessed;
            IntentionalNullRegistry.Changed += OnIntentionalListChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            GuardianHistory.Changed -= OnHistoryChanged;
            GuardianSettings.Changed -= OnSettingsChanged;
            ScanScheduler.Processed -= OnAutoScanProcessed;
            IntentionalNullRegistry.Changed -= OnIntentionalListChanged;
            _subscribed = false;
        }

        private void OnHistoryChanged()
        {
            // 表示言語の一時的な上書き中（テストの実行中）は更新せず、上書きが終わってから更新する
            if (Lang.IsTemporarilyOverridden)
            {
                _refreshPending = true;
                return;
            }

            RefreshCurrentTab();
            UpdateHeader();
        }

        /// <summary>
        /// 意図的な NULL の共有リストが変わったら、Scene Bindings の詳細（登録・解除のボタン）を表示し直す。
        /// </summary>
        private void OnIntentionalListChanged()
        {
            if (_currentTab == Tab.SceneBindings && _selectedNode != null)
            {
                ShowDetails(_selectedNode);
            }
        }

        private void OnSettingsChanged()
        {
            if (Lang.Current != _builtLanguage)
            {
                ScheduleRebuild();
                return;
            }

            RefreshCurrentTab();
            UpdateHeader();
        }

        /// <summary>
        /// 設定タブの操作（言語の選択）のコールバック中に UI を消さないよう、作り直しは次の更新で行う。
        /// </summary>
        private void ScheduleRebuild()
        {
            if (_rebuildScheduled)
            {
                return;
            }

            _rebuildScheduled = true;
            rootVisualElement.schedule.Execute(RebuildIfLanguageChanged);
        }

        /// <summary>
        /// 自動検査された Scene が Scene Bindings に表示中なら、その結果に置き換える。
        /// </summary>
        private void OnAutoScanProcessed(ScanResult result, RecordSummary summary)
        {
            int index = _results.FindIndex(r => r.Owner.AssetGuid == result.Owner.AssetGuid);
            if (index < 0)
            {
                return;
            }

            _results[index] = result;
            RebuildTree();
            ShowDetailsPlaceholder();
        }

        private void UpdateHeader()
        {
            if (_headerLabel == null)
            {
                return;
            }

            // 表示言語の一時的な上書き中（テストの実行中）は、上書き中の言語の文言を出さないよう何もしない
            if (Lang.IsTemporarilyOverridden)
            {
                return;
            }

            // 設定の変更以外で表示言語が変わった場合（テストの実行中は英語に固定され、終了後に戻る等）も、
            // 1 秒ごとのこの更新で気付いて作り直す（古い言語の文言が残らないように）
            if (Lang.Current != _builtLanguage)
            {
                ScheduleRebuild();
                return;
            }

            // 上書き中に届いた履歴の変化を、上書きが終わってから表示に反映する
            if (_refreshPending)
            {
                _refreshPending = false;
                RefreshCurrentTab();
            }

            GuardianHistory history = GuardianHistory.Shared;
            List<(NotificationRecord Notification, ChangeEvent Event)> unread = history.GetNotifications(false);
            Func<string, LevelValue> current = history.CreateCurrentValueLookup();
            int stillUnbound = unread.Count(n => !NotificationRules.IsResolved(n.Event, current));
            string monitoring = GuardianSettings.Instance.MonitoringEnabled ? Lang.T("window.header.monitoringOn") : Lang.T("window.header.monitoringOff");
            string pending = FormatPending();
            string last = ScanScheduler.LastProcessedLocal == default ? "-" : ScanScheduler.LastProcessedLocal.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            _headerLabel.text = Lang.T("window.header.summary", unread.Count, stillUnbound, monitoring, pending, last);
            _headerLabel.style.color = unread.Count > 0 ? new StyleColor(AttentionColor) : new StyleColor(StyleKeyword.Null);
            if (!history.Store.IsWritable)
            {
                _headerLabel.text += Lang.T("window.header.readOnly", history.Store.ErrorMessage);
            }

            string intentionalError = IntentionalNullRegistry.Error;
            if (!string.IsNullOrEmpty(intentionalError))
            {
                _headerLabel.text += "  |  " + Lang.T("intentional.settings.fileError", intentionalError);
            }
        }

        private static string FormatPending()
        {
            if (ScanScheduler.PendingCount <= 0)
            {
                return Lang.T("window.header.nonePending");
            }

            return string.IsNullOrEmpty(ScanScheduler.DeferReason)
                ? Lang.T("window.header.pending", ScanScheduler.PendingCount)
                : Lang.T("window.header.pendingWithReason", ScanScheduler.PendingCount, ScanScheduler.DeferReason);
        }

        private VisualElement CreateTabBar()
        {
            Toolbar tabs = new Toolbar();
            AddTabToggle(tabs, Tab.Notifications, Lang.T("window.tab.notifications"));
            AddTabToggle(tabs, Tab.SceneBindings, Lang.T("window.tab.sceneBindings"));
            AddTabToggle(tabs, Tab.History, Lang.T("window.tab.history"));
            AddTabToggle(tabs, Tab.Settings, Lang.T("window.tab.settings"));
            return tabs;
        }

        private void AddTabToggle(Toolbar tabs, Tab tab, string text)
        {
            ToolbarToggle toggle = new ToolbarToggle { text = text };
            toggle.RegisterValueChangedCallback(changeEvent => OnTabToggled(tab, changeEvent));
            _tabToggles[tab] = toggle;
            tabs.Add(toggle);
        }

        private void OnTabToggled(Tab tab, ChangeEvent<bool> changeEvent)
        {
            if (changeEvent.newValue)
            {
                ShowTab(tab);
            }
            else
            {
                // 選択中のタブを押しても選択を外さない
                _tabToggles[tab].SetValueWithoutNotify(true);
            }
        }

        private void ShowTab(Tab tab)
        {
            _currentTab = tab;
            foreach (KeyValuePair<Tab, ToolbarToggle> pair in _tabToggles)
            {
                pair.Value.SetValueWithoutNotify(pair.Key == tab);
            }

            _notificationsView.Root.style.display = tab == Tab.Notifications ? DisplayStyle.Flex : DisplayStyle.None;
            _bindingsTab.style.display = tab == Tab.SceneBindings ? DisplayStyle.Flex : DisplayStyle.None;
            _historyView.Root.style.display = tab == Tab.History ? DisplayStyle.Flex : DisplayStyle.None;
            _settingsView.Root.style.display = tab == Tab.Settings ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshCurrentTab();
            UpdateHeader();
        }

        private void RefreshCurrentTab()
        {
            switch (_currentTab)
            {
                case Tab.Notifications:
                    _notificationsView.Refresh();
                    break;
                case Tab.History:
                    _historyView.Refresh();
                    break;
                case Tab.Settings:
                    _settingsView.Refresh();
                    break;
            }
        }

        private VisualElement CreateToolbar()
        {
            Toolbar toolbar = new Toolbar();
            ToolbarButton scanButton = new ToolbarButton(OnScanClicked) { text = Lang.T("window.scanNow") };
            toolbar.Add(scanButton);

            ToolbarButton buildScanButton = new ToolbarButton(OnBuildScenesScanClicked) { text = Lang.T("window.buildScan.button") };
            buildScanButton.tooltip = Lang.T("window.buildScan.tooltip");
            toolbar.Add(buildScanButton);

            EnumField scopeField = new EnumField(_scope);
            scopeField.style.width = 130;
            scopeField.RegisterValueChangedCallback(OnScopeChanged);
            toolbar.Add(scopeField);

            ToolbarSpacer spacer = new ToolbarSpacer();
            spacer.style.flexGrow = 1;
            toolbar.Add(spacer);

            Label phaseNote = new Label(Lang.T("window.toolbarNote"));
            phaseNote.style.color = MutedColor;
            phaseNote.style.unityTextAlign = TextAnchor.MiddleRight;
            toolbar.Add(phaseNote);
            return toolbar;
        }

        private VisualElement CreateTreePane()
        {
            VisualElement pane = new VisualElement();
            pane.style.minWidth = 220;

            // UI を作り直したときも検索とフィルタの状態を引き継ぐ
            ToolbarSearchField search = new ToolbarSearchField();
            search.style.width = StyleKeyword.Auto;
            search.SetValueWithoutNotify(_searchText);
            search.RegisterValueChangedCallback(OnSearchChanged);
            pane.Add(search);

            VisualElement filters = new VisualElement();
            filters.style.flexDirection = FlexDirection.Row;
            filters.style.paddingLeft = 4;
            Toggle lostToggle = new Toggle(Lang.T("window.filter.lostOnly"));
            lostToggle.SetValueWithoutNotify(_lostOnly);
            lostToggle.RegisterValueChangedCallback(OnLostFilterChanged);
            Toggle undeterminedToggle = new Toggle(Lang.T("window.filter.undeterminedOnly"));
            undeterminedToggle.SetValueWithoutNotify(_undeterminedOnly);
            undeterminedToggle.RegisterValueChangedCallback(OnUndeterminedFilterChanged);
            filters.Add(lostToggle);
            filters.Add(undeterminedToggle);
            pane.Add(filters);

            _treeView = new TreeView
            {
                makeItem = MakeTreeItem,
                bindItem = BindTreeItem,
                selectionType = SelectionType.Single,
                fixedItemHeight = 18,
            };
            _treeView.style.flexGrow = 1;
            _treeView.selectionChanged += OnTreeSelectionChanged;
            pane.Add(_treeView);
            return pane;
        }

        private void OnScanClicked()
        {
            Scan();
        }

        private void OnBuildScenesScanClicked()
        {
            ScanBuildScenes();
        }

        private void OnScopeChanged(ChangeEvent<Enum> changeEvent)
        {
            _scope = (ScanScope)changeEvent.newValue;
        }

        private void OnSearchChanged(ChangeEvent<string> changeEvent)
        {
            _searchText = changeEvent.newValue ?? string.Empty;
            RebuildTree();
        }

        private void OnLostFilterChanged(ChangeEvent<bool> changeEvent)
        {
            _lostOnly = changeEvent.newValue;
            RebuildTree();
        }

        private void OnUndeterminedFilterChanged(ChangeEvent<bool> changeEvent)
        {
            _undeterminedOnly = changeEvent.newValue;
            RebuildTree();
        }

        private void OnTreeSelectionChanged(IEnumerable<object> selection)
        {
            NodeData node = selection.OfType<NodeData>().FirstOrDefault();
            ShowDetails(node);
        }

        private void Scan()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                _statusLabel.text = Lang.T("window.status.playMode");
                return;
            }

            SceneBindingScanner scanner = new SceneBindingScanner();
            _results.Clear();
            if (_scope == ScanScope.ActiveScene)
            {
                _results.Add(scanner.Scan(SceneManager.GetActiveScene()));
            }
            else
            {
                _results.AddRange(scanner.ScanLoadedScenes());
            }

            _lastScanLocal = DateTime.Now;
            RecordHistory();
            UpdateStatus();
            RebuildTree();
            ShowDetailsPlaceholder();
            RefreshCurrentTab();
        }

        /// <summary>
        /// スキャン結果を履歴に記録する（保存済みの Scene のみ）。
        /// </summary>
        /// <summary>
        /// Build Settings の Scene を、閉じているものは一時的に開いて検査する（確認ダイアログ・進捗・キャンセルあり）。
        /// </summary>
        private void ScanBuildScenes()
        {
            string blocking = BuildSceneScan.GetBlockingReason();
            if (blocking.Length > 0)
            {
                _statusLabel.text = Lang.T("window.buildScan.blocked", blocking);
                return;
            }

            List<string> scenes = PreBuildCheck.GetBuildScenePaths();
            if (scenes.Count == 0)
            {
                _statusLabel.text = Lang.T("window.buildScan.noScenes");
                return;
            }

            int closedCount = scenes.Count(p => !SceneManager.GetSceneByPath(p).isLoaded);
            bool confirmed = EditorUtility.DisplayDialog("Timeline Binding Guardian",
                Lang.T("window.buildScan.confirm", scenes.Count, closedCount),
                Lang.T("window.buildScan.confirm.ok"), Lang.T("window.dialog.cancel"));
            if (!confirmed)
            {
                return;
            }

            BuildSceneScanResult result;
            try
            {
                result = BuildSceneScan.Run(scenes, GuardianHistory.Shared, GuardianSettings.Instance.IsExcluded, ShowBuildScanProgress);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            NotificationService.ReportNewNotifications(result.Combined, Lang.T("engine.reason.buildScan"));
            _statusLabel.text = FormatBuildScanResult(result);
            RefreshCurrentTab();
        }

        private static bool ShowBuildScanProgress(int index, int total, string scenePath)
        {
            string message = Lang.T("window.buildScan.progress", index + 1, total, scenePath);
            return EditorUtility.DisplayCancelableProgressBar("Timeline Binding Guardian", message, total == 0 ? 1f : (float)index / total);
        }

        private static string FormatBuildScanResult(BuildSceneScanResult result)
        {
            if (result.BlockingReason.Length > 0)
            {
                return Lang.T("window.buildScan.blocked", result.BlockingReason);
            }

            int lost = result.Combined.NewEvents.Count(e => e.ChangeType == ChangeType.BindingLost);
            string key = result.Cancelled ? "window.buildScan.cancelled" : "window.buildScan.done";
            List<string> lines = new List<string> { Lang.T(key, result.ScannedSceneCount, result.OpenedSceneCount, result.Combined.NewEventCount, lost) };
            lines.AddRange(result.Errors.Select(e => Lang.T("window.record.notSaved", e)));
            lines.AddRange(result.SkippedReasons.Select(r => Lang.T("window.record.notRecorded", r)));
            return string.Join("\n", lines);
        }

        private void RecordHistory()
        {
            RecordSummary summary = GuardianHistory.Shared.Record(_results, "Manual scan");
            NotificationService.ReportNewNotifications(summary, "Manual scan");
            _lastRecordSummary = summary;
        }

        /// <summary>
        /// 直近の記録結果をステータス表示用の文言にする（表示言語の切り替えに追従するため、表示のたびに作る）。
        /// </summary>
        private static string FormatRecordMessage(RecordSummary summary)
        {
            if (summary == null)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>();
            if (!string.IsNullOrEmpty(summary.Error))
            {
                parts.Add(Lang.T("window.record.notSaved", summary.Error));
            }
            else if (summary.Saved)
            {
                int lost = summary.NewEvents.Count(e => e.ChangeType == ChangeType.BindingLost);
                string written = (summary.WrittenBytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture);
                parts.Add(Lang.T("window.record.saved", summary.NewEventCount, lost, written, summary.WrittenFileCount));
            }

            parts.AddRange(summary.SkippedReasons.Select(r => Lang.T("window.record.notRecorded", r)));
            return string.Join("  |  ", parts);
        }

        private void UpdateStatus()
        {
            int directorCount = _results.Sum(r => r.Directors.Count);
            int trackCount = _results.Sum(r => r.Directors.Sum(d => d.Tracks.Count));
            int lostCount = _results.Sum(r => r.Directors.Sum(d => d.Tracks.Count(IsLost)));
            int unsavedCount = _results.Count(r => !r.IsSaved);
            string unsaved = unsavedCount > 0 ? Lang.T("window.status.unsaved", unsavedCount) : string.Empty;
            string time = _lastScanLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            string recordMessage = FormatRecordMessage(_lastRecordSummary);
            string record = string.IsNullOrEmpty(recordMessage) ? string.Empty : $"\n{recordMessage}";
            _statusLabel.text = Lang.T("window.status.summary", time, _results.Count, directorCount, trackCount, lostCount, unsaved, record);
        }

        private void RebuildTree()
        {
            if (_treeView == null)
            {
                return;
            }

            _nextId = 0;
            List<TreeViewItemData<NodeData>> roots = new List<TreeViewItemData<NodeData>>();
            foreach (ScanResult result in _results)
            {
                roots.Add(CreateSceneItem(result));
            }

            _treeView.SetRootItems(roots);
            _treeView.Rebuild();
            _treeView.ExpandAll();
        }

        private TreeViewItemData<NodeData> CreateSceneItem(ScanResult result)
        {
            // Prefab インスタンスの外側→内側の列でツリーを組む（同名インスタンスは GlobalObjectId で区別）
            InstanceBranch root = new InstanceBranch();
            foreach (DirectorObservation director in result.Directors)
            {
                List<TrackBindingObservation> tracks = director.Tracks.Where(t => MatchesFilter(director, t)).ToList();
                // バインド対象の無い Director（Timeline 未設定など）はフィルタ未使用時のみ表示する
                bool showEmptyDirector = !HasActiveFilter() && director.Tracks.Count == 0;
                bool directorMatches = tracks.Count > 0 || showEmptyDirector;
                if (!directorMatches)
                {
                    continue;
                }

                InstanceBranch branch = root;
                foreach (InstanceNode instance in director.InstanceChain)
                {
                    branch = branch.GetChild(instance);
                }

                branch.Directors.Add((director, tracks));
            }

            string sceneLabel = result.Owner.IsSaved ? result.Owner.AssetPathHint : Lang.T("window.untitledScene");
            NodeData sceneNode = new NodeData { Kind = NodeKind.Scene, Label = sceneLabel, Scan = result, NeedsAttention = !result.IsSaved };
            return new TreeViewItemData<NodeData>(_nextId++, sceneNode, CreateBranchItems(root, result));
        }

        private List<TreeViewItemData<NodeData>> CreateBranchItems(InstanceBranch branch, ScanResult result)
        {
            List<TreeViewItemData<NodeData>> items = new List<TreeViewItemData<NodeData>>();
            foreach (InstanceBranch child in branch.Children)
            {
                string label = string.IsNullOrEmpty(child.Node.SourceAssetPathHint)
                    ? child.Node.Name
                    : $"{child.Node.Name}  ({System.IO.Path.GetFileName(child.Node.SourceAssetPathHint)})";
                NodeData node = new NodeData { Kind = NodeKind.Instance, Label = label, Scan = result };
                items.Add(new TreeViewItemData<NodeData>(_nextId++, node, CreateBranchItems(child, result)));
            }

            foreach ((DirectorObservation director, List<TrackBindingObservation> tracks) in branch.Directors)
            {
                List<TreeViewItemData<NodeData>> trackItems = new List<TreeViewItemData<NodeData>>();
                foreach (TrackBindingObservation track in tracks)
                {
                    NodeData trackNode = new NodeData
                    {
                        Kind = NodeKind.Track,
                        Label = FormatTrackLabel(track),
                        Scan = result,
                        Director = director,
                        Track = track,
                        NeedsAttention = IsLost(track) || track.OriginKind == OriginKind.Undetermined,
                    };
                    trackItems.Add(new TreeViewItemData<NodeData>(_nextId++, trackNode));
                }

                string state = FormatDirectorState(director);
                NodeData directorNode = new NodeData
                {
                    Kind = NodeKind.Director,
                    Label = $"{GetLeafName(director.Director.HierarchyPathHint)}  [Director]{state}",
                    Scan = result,
                    Director = director,
                    NeedsAttention = director.Diagnostics.Count > 0,
                };
                items.Add(new TreeViewItemData<NodeData>(_nextId++, directorNode, trackItems));
            }

            return items;
        }

        private bool MatchesFilter(DirectorObservation director, TrackBindingObservation track)
        {
            bool lostMatches = !_lostOnly || IsLost(track);
            bool undeterminedMatches = !_undeterminedOnly || track.OriginKind == OriginKind.Undetermined;
            if (!lostMatches || !undeterminedMatches)
            {
                return false;
            }

            if (string.IsNullOrEmpty(_searchText))
            {
                return true;
            }

            bool trackMatches = Contains(track.Track.TrackPathHint);
            bool directorMatches = Contains(director.Director.HierarchyPathHint);
            return trackMatches || directorMatches;
        }

        private bool HasActiveFilter()
        {
            bool hasStateFilter = _lostOnly || _undeterminedOnly;
            return hasStateFilter || !string.IsNullOrEmpty(_searchText);
        }

        private bool Contains(string text)
        {
            return text.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static VisualElement MakeTreeItem()
        {
            Label label = new Label();
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            return label;
        }

        private void BindTreeItem(VisualElement element, int index)
        {
            NodeData node = _treeView.GetItemDataForIndex<NodeData>(index);
            Label label = (Label)element;
            label.text = node.Label;
            label.tooltip = node.Label;
            label.style.color = node.NeedsAttention ? new StyleColor(AttentionColor) : new StyleColor(StyleKeyword.Null);
            label.style.unityFontStyleAndWeight = node.Kind == NodeKind.Scene ? FontStyle.Bold : FontStyle.Normal;
        }

        private void ShowDetailsPlaceholder()
        {
            _details.Clear();
            _details.Add(CreateMutedLabel(Lang.T("window.details.placeholder")));
            _details.Add(CreateMutedLabel(Lang.T("window.details.nullNote")));
        }

        private void ShowDetails(NodeData node)
        {
            _selectedNode = node;
            if (node == null)
            {
                ShowDetailsPlaceholder();
                return;
            }

            _details.Clear();
            switch (node.Kind)
            {
                case NodeKind.Track:
                    ShowTrackDetails(node.Director, node.Track);
                    break;
                case NodeKind.Director:
                    ShowDirectorDetails(node.Director);
                    break;
                case NodeKind.Scene:
                    ShowSceneDetails(node.Scan);
                    break;
                default:
                    _details.Add(CreateMutedLabel(node.Label));
                    break;
            }
        }

        private void ShowSceneDetails(ScanResult result)
        {
            _details.Add(CreateHeader(result.Owner.IsSaved ? result.Owner.AssetPathHint : Lang.T("window.untitledScene")));
            _details.Add(new Label(Lang.T("window.scene.scannedAt", result.ScannedAtUtc)));
            _details.Add(new Label(Lang.T("window.scene.directors", result.Directors.Count)));
            foreach (string diagnostic in result.Diagnostics)
            {
                _details.Add(CreateWarningLabel(diagnostic));
            }
        }

        private void ShowDirectorDetails(DirectorObservation director)
        {
            _details.Add(CreateHeader(director.Director.HierarchyPathHint));
            _details.Add(new Label(Lang.T("window.director.owner", director.Owner)));
            _details.Add(new Label(Lang.T("window.director.timeline", director.Timeline != null ? director.Timeline.AssetPathHint : "-")));
            _details.Add(new Label(Lang.T("window.director.activeEnabled", director.ActiveInHierarchy, director.Enabled)));
            _details.Add(new Label(Lang.T("window.director.bindings", director.Tracks.Count)));
            _details.Add(new Button(() => OnPingDirectorClicked(director)) { text = Lang.T("window.pingDirector") });
            foreach (string diagnostic in director.Diagnostics)
            {
                _details.Add(CreateWarningLabel(diagnostic));
            }
        }

        private void ShowTrackDetails(DirectorObservation director, TrackBindingObservation track)
        {
            _details.Add(CreateHeader($"{track.Track.TrackPathHint}  ({track.Track.TrackType} → {track.Track.OutputTargetType})"));
            _details.Add(new Label(Lang.T("window.track.director", director.Director.HierarchyPathHint)));
            _details.Add(new Label(Lang.T("window.track.timeline", track.Track.TimelinePathHint)));
            _details.Add(new Label(Lang.T("window.track.effective", FormatState(track.Effective))));
            _details.Add(new Label(FormatOriginSummary(track)));

            VisualElement buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.Add(new Button(() => OnPingDirectorClicked(director)) { text = Lang.T("window.pingDirector") });
            // Prefab Mode では開いている階層の値を Ping するため、どこかの階層に対象があれば押せるようにする
            Button pingTarget = new Button(() => OnPingTargetClicked(track)) { text = Lang.T("window.pingTarget") };
            pingTarget.SetEnabled(track.Levels.Any(l => !l.Target.IsNone));
            buttons.Add(pingTarget);
            if (track.Origin != null && track.Origin.Owner.AssetKind != AssetKind.Scene)
            {
                LevelBindingState origin = track.Origin;
                buttons.Add(new Button(() => OpenAsset(origin.Owner.AssetPathHint)) { text = Lang.T("window.openOriginPrefab") });
            }

            _details.Add(buttons);

            _details.Add(CreateHeader(Lang.T("window.levels.header")));
            _details.Add(CreateLevelRow(true, null, null));
            for (int i = track.Levels.Count - 1; i >= 0; i--)
            {
                _details.Add(CreateLevelRow(false, track.Levels[i], track.Track));
            }

            AddTrackHistory(director, track);
        }

        private void AddTrackHistory(DirectorObservation director, TrackBindingObservation track)
        {
            List<string> ownerLevelKeys = track.Levels
                .Select(l => BindingIdentity.CreateOwnerLevelKey(l.Owner.AssetGuid, l.Director.GlobalObjectId, track.Track.TimelineGuid, track.Track.TrackFileId))
                .ToList();
            List<ChangeEvent> events = HistoryView.FindEventsForContext(GuardianHistory.Shared.Store.State, track.BindingKey, ownerLevelKeys)
                .Where(e => e.ChangeType != ChangeType.Initial)
                .ToList();

            _details.Add(CreateHeader(Lang.T("window.history.header")));
            if (events.Count == 0)
            {
                _details.Add(CreateMutedLabel(Lang.T("window.history.none")));
            }
            else
            {
                ChangeEvent latest = events[0];
                AffectedContext context = latest.AffectedContexts.FirstOrDefault(c => c.ContextBindingKey == track.BindingKey);
                string ownerName = System.IO.Path.GetFileName(latest.Owner.AssetPathHint);
                string previous = context != null
                    ? Lang.T("window.history.previousEffective", HistoryView.FormatValue(context.EffectiveBefore), HistoryView.FormatValue(context.EffectiveAfter))
                    : Lang.T("window.history.previousAt", ownerName, HistoryView.FormatValue(latest.Before), HistoryView.FormatValue(latest.After));
                _details.Add(new Label(Lang.T("window.history.lastChange", latest.ChangeType, ownerName, HistoryView.FormatLocalTime(latest.ObservedAtUtc))));
                _details.Add(new Label(previous));
                _details.Add(new Label(Lang.T("window.history.count", events.Count)));
            }

            string label = Lang.T("window.history.contextLabel", GetLeafName(track.Track.TrackPathHint), director.Director.HierarchyPathHint);
            Button showHistory = new Button(() => OnShowHistoryClicked(track.BindingKey, ownerLevelKeys, label)) { text = Lang.T("window.showHistory") };
            showHistory.style.alignSelf = Align.FlexStart;
            _details.Add(showHistory);
            _details.Add(CreateMutedLabel(Lang.T("window.history.timeNote")));
        }

        private void OnShowHistoryClicked(string bindingKey, List<string> ownerLevelKeys, string label)
        {
            ShowTab(Tab.History);
            _historyView.ShowContext(bindingKey, ownerLevelKeys, label);
        }

        private VisualElement CreateLevelRow(bool isHeader, LevelBindingState level, TrackLocator track)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;

            string[] cells = isHeader
                ? new[]
                {
                    Lang.T("window.levels.asset"),
                    Lang.T("window.levels.context"),
                    Lang.T("window.levels.localState"),
                    Lang.T("window.levels.value"),
                    Lang.T("window.levels.override"),
                    Lang.T("window.levels.role"),
                }
                : new[]
                {
                    System.IO.Path.GetFileName(level.Owner.AssetPathHint),
                    level.ContextKind.ToString(),
                    level.LocalState.ToString(),
                    FormatState(level),
                    FormatOverride(level),
                    FormatRole(level),
                };
            float[] widths = { 140, 110, 105, 220, 110, 170 };
            for (int i = 0; i < cells.Length; i++)
            {
                Label cell = new Label(cells[i]);
                cell.style.width = widths[i];
                cell.style.overflow = Overflow.Hidden;
                cell.style.textOverflow = TextOverflow.Ellipsis;
                cell.style.paddingRight = 6;
                cell.tooltip = cells[i];
                cell.style.unityFontStyleAndWeight = isHeader ? FontStyle.Bold : FontStyle.Normal;
                row.Add(cell);
            }

            if (isHeader)
            {
                return row;
            }

            if (level.Role == LevelRole.Origin)
            {
                row.style.backgroundColor = OriginRowColor;
            }

            if (level.Owner.AssetKind != AssetKind.Scene)
            {
                string path = level.Owner.AssetPathHint;
                Button open = new Button(() => OpenAsset(path)) { text = Lang.T("window.open") };
                row.Add(open);
            }

            // この階層で値を Override している行（NULL の上書きを含む）は、その Override を外して継承に戻せる
            bool isOverriddenHere = level.ValueOverridden && level.LocalState == LocalState.ExplicitOverride;
            if (isOverriddenHere)
            {
                Button remove = new Button(() => OnRemoveOverrideClicked(level, track)) { text = Lang.T("window.removeOverride") };
                remove.tooltip = Lang.T("window.removeOverride.tooltip");
                row.Add(remove);
            }

            // この階層で NULL にしている行は、意図的な NULL として共有リストに登録・解除できる
            bool isNullSetHere = level.LocalState == LocalState.DefinedHere || level.LocalState == LocalState.ExplicitOverride;
            if (level.ValueState == ValueState.Null && isNullSetHere)
            {
                AddIntentionalNullControls(row, level, track);
            }

            row.tooltip = string.IsNullOrEmpty(level.Note) ? level.Owner.AssetPathHint : $"{level.Owner.AssetPathHint}\n{level.Note}";
            return row;
        }

        private void AddIntentionalNullControls(VisualElement row, LevelBindingState level, TrackLocator track)
        {
            string key = BindingIdentity.CreateOwnerLevelKey(level.Owner.AssetGuid, level.Director.GlobalObjectId, track.TimelineGuid, track.TrackFileId);
            if (!IntentionalNullRegistry.Contains(key))
            {
                Button register = new Button(() => OnRegisterIntentionalClicked(level, track, key)) { text = Lang.T("intentional.button.register") };
                register.tooltip = Lang.T("intentional.button.register.tooltip");
                row.Add(register);
                return;
            }

            Label mark = new Label(Lang.T("intentional.mark"));
            mark.style.unityTextAlign = TextAnchor.MiddleLeft;
            mark.style.color = MutedColor;
            mark.tooltip = Lang.T("intentional.mark.tooltip");
            row.Add(mark);
            Button unregister = new Button(() => OnUnregisterIntentionalClicked(key)) { text = Lang.T("intentional.button.unregister") };
            row.Add(unregister);
        }

        private static void OnRegisterIntentionalClicked(LevelBindingState level, TrackLocator track, string key)
        {
            IntentionalNullDialog.Open(new IntentionalNullEntry(key, level.Owner.AssetPathHint, level.Director.IndexPathHint,
                track.TimelinePathHint, track.TrackPathHint, string.Empty, string.Empty));
        }

        private void OnUnregisterIntentionalClicked(string key)
        {
            if (!IntentionalNullRegistry.TryRemove(key, out string error))
            {
                _statusLabel.text = Lang.T("intentional.error", error);
                return;
            }

            ShowDetails(_selectedNode);
        }

        private static void OnRemoveOverrideClicked(LevelBindingState level, TrackLocator track)
        {
            RestorePreviewWindow.OpenRemoveOverride(level, track);
        }

        private static string FormatTrackLabel(TrackBindingObservation track)
        {
            string name = GetLeafName(track.Track.TrackPathHint);
            string origin = track.Origin != null ? System.IO.Path.GetFileName(track.Origin.Owner.AssetPathHint) : Lang.T("window.origin.undeterminedShort");
            return Lang.T("window.trackLabel", name, FormatValueState(track.Effective.ValueState), origin);
        }

        private static string FormatDirectorState(DirectorObservation director)
        {
            List<string> flags = new List<string>();
            if (!director.ActiveInHierarchy)
            {
                flags.Add(Lang.T("window.directorState.inactive"));
            }

            if (!director.Enabled)
            {
                flags.Add(Lang.T("window.directorState.disabled"));
            }

            return flags.Count > 0 ? $"  ({string.Join(", ", flags)})" : string.Empty;
        }

        private static string FormatState(LevelBindingState level)
        {
            if (level.ValueState != ValueState.Bound)
            {
                return FormatValueState(level.ValueState);
            }

            string component = level.Target.ComponentIndexHint > 0 ? $" #{level.Target.ComponentIndexHint}" : string.Empty;
            return $"{level.Target.HierarchyPathHint} [{level.Target.TypeName}{component}]";
        }

        private static string FormatValueState(ValueState state)
        {
            switch (state)
            {
                case ValueState.NoEntry:
                    return "No Entry";
                case ValueState.MissingReference:
                    return "Missing Reference";
                case ValueState.NotApplicable:
                    return "Not Applicable";
                default:
                    return state.ToString();
            }
        }

        private static string FormatOverride(LevelBindingState level)
        {
            if (level.StructuralOverride)
            {
                return level.ValueOverridden ? Lang.T("window.override.arrayShift") : Lang.T("window.override.entryRemoved");
            }

            return level.ValueOverridden ? Lang.T("window.override.yes") : "-";
        }

        private static string FormatRole(LevelBindingState level)
        {
            switch (level.Role)
            {
                case LevelRole.Origin:
                    return Lang.T("window.role.origin");
                case LevelRole.Shadowed:
                    return Lang.T("window.role.shadowed");
                default:
                    return level.ChangedAtThisLevel ? Lang.T("window.role.changed") : string.Empty;
            }
        }

        private static string FormatOriginSummary(TrackBindingObservation track)
        {
            if (track.OriginKind == OriginKind.Undetermined)
            {
                return Lang.T("window.origin.undetermined");
            }

            LevelBindingState origin = track.Origin;
            string where = origin.Owner.IsSaved ? origin.Owner.AssetPathHint : Lang.T("window.origin.unsavedScene");
            // 語順が言語で変わるため、「定義」と「Override」で文全体を分ける
            string key = origin.LocalState == LocalState.DefinedHere ? "window.origin.defined" : "window.origin.overridden";
            return Lang.T(key, where);
        }

        private void OnPingDirectorClicked(DirectorObservation director)
        {
            PingDirector(director);
        }

        /// <summary>
        /// Prefab Mode で、この Director の階層に含まれる Prefab を開いていれば、Prefab Mode 内の対応 Director を Ping する。
        /// それ以外は Scene 側の Director を Ping する（Prefab Mode 中なら確認のうえ Scene に戻る）。
        /// </summary>
        private void PingDirector(DirectorObservation director)
        {
            // 各階層の Director の Asset 側 ID は、どの Track の階層情報にも同じものが入っている
            TrackBindingObservation anyTrack = director.Tracks.FirstOrDefault();
            LevelBindingState level = anyTrack != null ? FindOpenPrefabLevel(anyTrack) : null;
            if (level != null && TryPingInOpenPrefabStage(level.Director.GlobalObjectId, "director"))
            {
                return;
            }

            PingByGlobalObjectId(director.Director.GlobalObjectId);
        }

        private void OnPingTargetClicked(TrackBindingObservation track)
        {
            PingTarget(track);
        }

        /// <summary>
        /// Prefab Mode で、この Track の階層に含まれる Prefab を開いていれば、その階層でのバインド先を Prefab Mode 内で Ping する。
        /// その階層の値が Null / No Entry なら理由を表示する（Scene 側の値とは異なる場合があるため Scene には切り替えない）。
        /// それ以外は Scene 側の実効値のバインド先を Ping する。
        /// </summary>
        private void PingTarget(TrackBindingObservation track)
        {
            LevelBindingState level = FindOpenPrefabLevel(track);
            if (level == null)
            {
                if (track.Effective.Target.IsNone)
                {
                    _statusLabel.text = Lang.T("window.ping.noTarget", FormatValueState(track.Effective.ValueState));
                    return;
                }

                PingByGlobalObjectId(track.Effective.Target.GlobalObjectId);
                return;
            }

            if (level.Target.IsNone)
            {
                string prefabName = System.IO.Path.GetFileName(level.Owner.AssetPathHint);
                _statusLabel.text = Lang.T("window.ping.noTargetInPrefab", prefabName, FormatValueState(level.ValueState));
                return;
            }

            if (!TryPingInOpenPrefabStage(level.Target.GlobalObjectId, "target"))
            {
                PingByGlobalObjectId(track.Effective.Target.GlobalObjectId);
            }
        }

        /// <summary>
        /// Prefab Mode で開いている Prefab に対応する階層を返す。開いていない、または階層に含まれなければ null。
        /// </summary>
        private static LevelBindingState FindOpenPrefabLevel(TrackBindingObservation track)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
            {
                return null;
            }

            return track.Levels.FirstOrDefault(l => l.Owner.AssetPathHint == stage.assetPath);
        }

        private bool TryPingInOpenPrefabStage(string assetGlobalObjectId, string label)
        {
            Object assetObject = ResolveObject(assetGlobalObjectId);
            if (!PrefabStageLocator.TryFindInOpenPrefabStage(assetObject, out Object stageObject))
            {
                // label は "director" か "target"（言語ごとに名詞の訳が変わるため文全体をキーで分ける）
                _statusLabel.text = Lang.T($"window.ping.notInPrefab.{label}");
                return false;
            }

            PingUtility.Ping(stageObject);
            return true;
        }

        private static bool IsLost(TrackBindingObservation track)
        {
            ValueState state = track.Effective.ValueState;
            bool isNullLike = state == ValueState.Null || state == ValueState.MissingReference;
            return isNullLike || state == ValueState.NoEntry;
        }

        private static string GetLeafName(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash >= 0 ? path.Substring(slash + 1) : path;
        }

        private void PingByGlobalObjectId(string globalObjectId)
        {
            Object target = ResolveObject(globalObjectId);
            if (target == null)
            {
                _statusLabel.text = Lang.T("window.ping.objectNotFound", Lang.T("window.scanNow"));
                return;
            }

            bool isSceneObject = string.IsNullOrEmpty(AssetDatabase.GetAssetPath(target));
            bool isInPrefabMode = PrefabStageUtility.GetCurrentPrefabStage() != null;
            if (!isSceneObject || !isInPrefabMode)
            {
                PingUtility.Ping(target);
                return;
            }

            // Prefab Mode 中は Hierarchy に Scene が表示されないため、ユーザーの確認を得てから Scene に戻る
            bool confirmed = EditorUtility.DisplayDialog(
                "Timeline Binding Guardian",
                Lang.T("window.dialog.returnToScene.message"),
                Lang.T("window.dialog.returnToScene.ok"),
                Lang.T("window.dialog.cancel"));
            if (!confirmed)
            {
                return;
            }

            StageUtility.GoToMainStage();
            // Stage の切り替え後に Hierarchy が更新されてから Ping する
            EditorApplication.delayCall += () => PingAfterStageChange(globalObjectId);
        }

        private void PingAfterStageChange(string globalObjectId)
        {
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                // 保存確認ダイアログでキャンセルされた等で Prefab Mode のまま
                return;
            }

            Object target = ResolveObject(globalObjectId);
            if (target != null)
            {
                PingUtility.Ping(target);
            }
        }

        private static Object ResolveObject(string globalObjectId)
        {
            if (!GlobalObjectId.TryParse(globalObjectId, out GlobalObjectId id))
            {
                return null;
            }

            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
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
            label.style.marginTop = 4;
            return label;
        }

        private static Label CreateWarningLabel(string text)
        {
            Label label = new Label(text);
            label.style.color = AttentionColor;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        /// <summary>
        /// Prefab インスタンスの入れ子をまとめるための中間構造。
        /// </summary>
        private sealed class InstanceBranch
        {
            private readonly Dictionary<string, InstanceBranch> _childrenById = new Dictionary<string, InstanceBranch>();

            public InstanceNode Node { get; private set; }
            public List<InstanceBranch> Children { get; } = new List<InstanceBranch>();
            public List<(DirectorObservation, List<TrackBindingObservation>)> Directors { get; } = new List<(DirectorObservation, List<TrackBindingObservation>)>();

            public InstanceBranch GetChild(InstanceNode node)
            {
                if (_childrenById.TryGetValue(node.GlobalObjectId, out InstanceBranch child))
                {
                    return child;
                }

                child = new InstanceBranch { Node = node };
                _childrenById.Add(node.GlobalObjectId, child);
                Children.Add(child);
                return child;
            }
        }
    }
}
