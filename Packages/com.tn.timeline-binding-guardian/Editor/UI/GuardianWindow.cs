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

        private Label _statusLabel;
        private TreeView _treeView;
        private ScrollView _details;

        [MenuItem("Window/Timeline/Timeline Binding Guardian")]
        private static void Open()
        {
            GuardianWindow window = GetWindow<GuardianWindow>();
            window.titleContent = new GUIContent("Binding Guardian");
            window.minSize = new Vector2(720, 360);
        }

        private void CreateGUI()
        {
            rootVisualElement.Add(CreateToolbar());

            _statusLabel = new Label("Press \"Scan Now\" to inspect Timeline bindings in the scene.");
            _statusLabel.style.paddingLeft = 6;
            _statusLabel.style.paddingTop = 2;
            _statusLabel.style.paddingBottom = 2;
            rootVisualElement.Add(_statusLabel);

            TwoPaneSplitView split = new TwoPaneSplitView(0, 340, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1;
            split.Add(CreateTreePane());
            _details = new ScrollView();
            _details.style.paddingLeft = 8;
            _details.style.paddingRight = 8;
            split.Add(_details);
            rootVisualElement.Add(split);

            ShowDetailsPlaceholder();
        }

        private VisualElement CreateToolbar()
        {
            Toolbar toolbar = new Toolbar();
            ToolbarButton scanButton = new ToolbarButton(OnScanClicked) { text = "Scan Now" };
            toolbar.Add(scanButton);

            EnumField scopeField = new EnumField(_scope);
            scopeField.style.width = 130;
            scopeField.RegisterValueChangedCallback(OnScopeChanged);
            toolbar.Add(scopeField);

            ToolbarSpacer spacer = new ToolbarSpacer();
            spacer.style.flexGrow = 1;
            toolbar.Add(spacer);

            Label phaseNote = new Label("Read-only view. Notifications and history are not available yet.");
            phaseNote.style.color = MutedColor;
            phaseNote.style.unityTextAlign = TextAnchor.MiddleRight;
            toolbar.Add(phaseNote);
            return toolbar;
        }

        private VisualElement CreateTreePane()
        {
            VisualElement pane = new VisualElement();
            pane.style.minWidth = 220;

            ToolbarSearchField search = new ToolbarSearchField();
            search.style.width = StyleKeyword.Auto;
            search.RegisterValueChangedCallback(OnSearchChanged);
            pane.Add(search);

            VisualElement filters = new VisualElement();
            filters.style.flexDirection = FlexDirection.Row;
            filters.style.paddingLeft = 4;
            Toggle lostToggle = new Toggle("Null / No Entry only");
            lostToggle.RegisterValueChangedCallback(OnLostFilterChanged);
            Toggle undeterminedToggle = new Toggle("Origin undetermined only");
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
                _statusLabel.text = "Scanning is disabled in Play Mode. Only stable Edit Mode states are inspected.";
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
            UpdateStatus();
            RebuildTree();
            ShowDetailsPlaceholder();
        }

        private void UpdateStatus()
        {
            int directorCount = _results.Sum(r => r.Directors.Count);
            int trackCount = _results.Sum(r => r.Directors.Sum(d => d.Tracks.Count));
            int lostCount = _results.Sum(r => r.Directors.Sum(d => d.Tracks.Count(IsLost)));
            int unsavedCount = _results.Count(r => !r.IsSaved);
            string unsaved = unsavedCount > 0 ? $"  |  {unsavedCount} unsaved scene(s): not eligible for history" : string.Empty;
            string time = _lastScanLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            _statusLabel.text = $"Last scan: {time}  |  {_results.Count} scene(s), {directorCount} director(s), {trackCount} binding(s), {lostCount} currently Null / No Entry{unsaved}";
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

            string sceneLabel = result.Owner.IsSaved ? result.Owner.AssetPathHint : "Untitled (unsaved)";
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
            _details.Add(CreateMutedLabel("Select a track to see its binding state at each prefab level."));
            _details.Add(CreateMutedLabel("Note: a Null binding is not necessarily an error. Parent prefabs may intentionally clear bindings."));
        }

        private void ShowDetails(NodeData node)
        {
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
            _details.Add(CreateHeader(result.Owner.IsSaved ? result.Owner.AssetPathHint : "Untitled (unsaved)"));
            _details.Add(new Label($"Scanned at (UTC): {result.ScannedAtUtc}"));
            _details.Add(new Label($"Directors: {result.Directors.Count} (including inactive GameObjects and disabled components)"));
            foreach (string diagnostic in result.Diagnostics)
            {
                _details.Add(CreateWarningLabel(diagnostic));
            }
        }

        private void ShowDirectorDetails(DirectorObservation director)
        {
            _details.Add(CreateHeader(director.Director.HierarchyPathHint));
            _details.Add(new Label($"Owner: {director.Owner}"));
            _details.Add(new Label($"Timeline: {(director.Timeline != null ? director.Timeline.AssetPathHint : "-")}"));
            _details.Add(new Label($"Active in hierarchy: {director.ActiveInHierarchy}   Component enabled: {director.Enabled}"));
            _details.Add(new Label($"Bindings: {director.Tracks.Count}  (tracks without a binding type, such as Control and Group, are excluded)"));
            _details.Add(new Button(() => OnPingDirectorClicked(director)) { text = "Ping Director" });
            foreach (string diagnostic in director.Diagnostics)
            {
                _details.Add(CreateWarningLabel(diagnostic));
            }
        }

        private void ShowTrackDetails(DirectorObservation director, TrackBindingObservation track)
        {
            _details.Add(CreateHeader($"{track.Track.TrackPathHint}  ({track.Track.TrackType} → {track.Track.OutputTargetType})"));
            _details.Add(new Label($"Director: {director.Director.HierarchyPathHint}"));
            _details.Add(new Label($"Timeline: {track.Track.TimelinePathHint}"));
            _details.Add(new Label($"Effective: {FormatState(track.Effective)}"));
            _details.Add(new Label(FormatOriginSummary(track)));

            VisualElement buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.Add(new Button(() => OnPingDirectorClicked(director)) { text = "Ping Director" });
            // Prefab Mode では開いている階層の値を Ping するため、どこかの階層に対象があれば押せるようにする
            Button pingTarget = new Button(() => OnPingTargetClicked(track)) { text = "Ping Target" };
            pingTarget.SetEnabled(track.Levels.Any(l => !l.Target.IsNone));
            buttons.Add(pingTarget);
            if (track.Origin != null && track.Origin.Owner.AssetKind != AssetKind.Scene)
            {
                LevelBindingState origin = track.Origin;
                buttons.Add(new Button(() => OpenAsset(origin.Owner.AssetPathHint)) { text = "Open Origin Prefab" });
            }

            _details.Add(buttons);

            _details.Add(CreateHeader("Levels (definition → … → current context)"));
            _details.Add(CreateLevelRow(true, null));
            for (int i = track.Levels.Count - 1; i >= 0; i--)
            {
                _details.Add(CreateLevelRow(false, track.Levels[i]));
            }

            _details.Add(CreateMutedLabel("Times shown are observation times, not when the change was actually made."));
        }

        private VisualElement CreateLevelRow(bool isHeader, LevelBindingState level)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;

            string[] cells = isHeader
                ? new[] { "Asset", "Context", "Local State", "Value", "Override", "Role" }
                : new[]
                {
                    System.IO.Path.GetFileName(level.Owner.AssetPathHint),
                    level.ContextKind.ToString(),
                    level.LocalState.ToString(),
                    FormatState(level),
                    FormatOverride(level),
                    FormatRole(level),
                };
            float[] widths = { 150, 120, 110, 260, 120, 210 };
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
                Button open = new Button(() => OpenAsset(path)) { text = "Open" };
                row.Add(open);
            }

            row.tooltip = string.IsNullOrEmpty(level.Note) ? level.Owner.AssetPathHint : $"{level.Owner.AssetPathHint}\n{level.Note}";
            return row;
        }

        private static string FormatTrackLabel(TrackBindingObservation track)
        {
            string name = GetLeafName(track.Track.TrackPathHint);
            string origin = track.Origin != null ? System.IO.Path.GetFileName(track.Origin.Owner.AssetPathHint) : "undetermined";
            return $"{name}  —  {FormatValueState(track.Effective.ValueState)}  (origin: {origin})";
        }

        private static string FormatDirectorState(DirectorObservation director)
        {
            List<string> flags = new List<string>();
            if (!director.ActiveInHierarchy)
            {
                flags.Add("inactive");
            }

            if (!director.Enabled)
            {
                flags.Add("disabled");
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
                return level.ValueOverridden ? "Yes (array shift)" : "Yes (entry removed)";
            }

            return level.ValueOverridden ? "Yes" : "-";
        }

        private static string FormatRole(LevelBindingState level)
        {
            switch (level.Role)
            {
                case LevelRole.Origin:
                    return "Origin of effective value";
                case LevelRole.Shadowed:
                    return "Changed, but overridden above";
                default:
                    return level.ChangedAtThisLevel ? "Changed" : string.Empty;
            }
        }

        private static string FormatOriginSummary(TrackBindingObservation track)
        {
            if (track.OriginKind == OriginKind.Undetermined)
            {
                return "Origin: undetermined (a level could not be classified).";
            }

            LevelBindingState origin = track.Origin;
            string where = origin.Owner.IsSaved ? origin.Owner.AssetPathHint : "unsaved scene";
            string how = origin.LocalState == LocalState.DefinedHere ? "defined" : "overridden";
            return $"Origin: {where} ({how} at this level)";
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
                    _statusLabel.text = $"No target to ping: the effective value is {FormatValueState(track.Effective.ValueState)}.";
                    return;
                }

                PingByGlobalObjectId(track.Effective.Target.GlobalObjectId);
                return;
            }

            if (level.Target.IsNone)
            {
                string prefabName = System.IO.Path.GetFileName(level.Owner.AssetPathHint);
                _statusLabel.text = $"No target to ping in the open prefab: the value at {prefabName} is {FormatValueState(level.ValueState)}.";
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
                _statusLabel.text = $"The {label} could not be located in the open prefab (it may have unsaved structural changes). Showing it in the scene instead.";
                return false;
            }

            EditorGUIUtility.PingObject(stageObject);
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
                _statusLabel.text = "The object could not be found. The scene may have been closed or changed. Press \"Scan Now\" again.";
                return;
            }

            bool isSceneObject = string.IsNullOrEmpty(AssetDatabase.GetAssetPath(target));
            bool isInPrefabMode = PrefabStageUtility.GetCurrentPrefabStage() != null;
            if (!isSceneObject || !isInPrefabMode)
            {
                EditorGUIUtility.PingObject(target);
                return;
            }

            // Prefab Mode 中は Hierarchy に Scene が表示されないため、ユーザーの確認を得てから Scene に戻る
            bool confirmed = EditorUtility.DisplayDialog(
                "Timeline Binding Guardian",
                "Prefab Mode is open, so the object cannot be shown in the Hierarchy.\nReturn to the scene and ping it?",
                "Return to Scene",
                "Cancel");
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
                EditorGUIUtility.PingObject(target);
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
