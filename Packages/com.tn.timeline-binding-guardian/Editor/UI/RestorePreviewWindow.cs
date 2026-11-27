using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 復元の事前表示と実行（仕様 10 章）。復元はこの画面での明示操作でのみ行う。
    /// 変更する場所・現在の値・戻す先・操作内容・警告・影響・復元できない理由を示し、確認のうえ実行する。
    /// </summary>
    public sealed class RestorePreviewWindow : EditorWindow
    {
        private static readonly Color AttentionColor = new Color(0.95f, 0.65f, 0.2f);
        private static readonly Color SuccessColor = new Color(0.5f, 0.8f, 0.5f);
        private static readonly Color MutedColor = new Color(0.6f, 0.6f, 0.6f);

        private ChangeEvent _event;
        private bool _removeOverrideOnly;
        private RestorePreview _preview;
        private RestoreOption _selected;
        private RestoreResult _lastResult;
        private ScrollView _content;

        /// <summary>
        /// 履歴のイベントの「変更前の状態」へ戻す。
        /// </summary>
        public static void Open(ChangeEvent changeEvent)
        {
            Show(changeEvent, false);
        }

        /// <summary>
        /// Scene Bindings の階層表の 1 行で、その階層の Override を外す（履歴のイベントによらない）。
        /// </summary>
        public static void OpenRemoveOverride(LevelBindingState level, TrackLocator track)
        {
            Show(BindingRestoreService.CreateRemoveOverrideTarget(level, track), true);
        }

        private static void Show(ChangeEvent changeEvent, bool removeOverrideOnly)
        {
            RestorePreviewWindow window = GetWindow<RestorePreviewWindow>(true, Lang.T("restore.window.title"));
            // 既に開いているウィンドウは GetWindow でタイトルが更新されないため、現在の言語で付け直す
            window.titleContent = new GUIContent(Lang.T("restore.window.title"));
            window.minSize = new Vector2(560, 420);
            window._event = changeEvent;
            window._removeOverrideOnly = removeOverrideOnly;
            window._lastResult = null;
            window.RefreshPreview();
        }

        private void OnEnable()
        {
            GuardianSettings.Changed += OnSettingsChanged;
        }

        private void OnDisable()
        {
            GuardianSettings.Changed -= OnSettingsChanged;
        }

        private void CreateGUI()
        {
            _content = new ScrollView();
            _content.style.paddingLeft = 10;
            _content.style.paddingRight = 10;
            _content.style.paddingTop = 8;
            rootVisualElement.Add(_content);
            Render();
        }

        private void RefreshPreview()
        {
            if (_event == null)
            {
                return;
            }

            _preview = BindingRestoreService.CreatePreview(_event, _removeOverrideOnly);
            _selected = RestorePlanner.GetRecommended(_preview.Plan);
            Render();
        }

        /// <summary>
        /// 確認ダイアログを出して復元する（confirm=false は自動確認用）。
        /// </summary>
        internal void RunRestore(bool confirm)
        {
            if (_preview == null || _selected == null || !_preview.Plan.CanRestore)
            {
                return;
            }

            if (confirm && !EditorUtility.DisplayDialog(Lang.T("restore.confirm.title"), CreateConfirmMessage(), Lang.T("restore.confirm.ok"), Lang.T("restore.confirm.cancel")))
            {
                return;
            }

            _lastResult = BindingRestoreService.Execute(_preview, _selected);
            RefreshPreview();
        }

        internal void RunRollback()
        {
            if (_lastResult == null || !_lastResult.CanRollback)
            {
                return;
            }

            _lastResult = BindingRestoreService.Rollback(_lastResult);
            RefreshPreview();
        }

        /// <summary>
        /// 設定（表示言語など）が変わったら作り直す。作り直しで Planner の文言も新しい言語になる。
        /// </summary>
        private void OnSettingsChanged()
        {
            titleContent = new GUIContent(Lang.T("restore.window.title"));
            if (_event == null)
            {
                Render();
                return;
            }

            RefreshPreview();
        }

        private void OnRestoreClicked()
        {
            RunRestore(true);
        }

        private void OnRollbackClicked()
        {
            RunRollback();
        }

        private void OnRefreshClicked()
        {
            RefreshPreview();
        }

        private void OnOpenOwnerClicked()
        {
            Object asset = AssetDatabase.LoadAssetAtPath<Object>(_preview.OwnerPath);
            if (asset != null)
            {
                AssetDatabase.OpenAsset(asset);
            }
        }

        private void OnOptionSelected(RestoreOption option)
        {
            _selected = option;
            Render();
        }

        private string CreateConfirmMessage()
        {
            string where = Path.GetFileName(_preview.OwnerPath);
            string track = _event.Track.TrackPathHint;
            string director = _event.Director.HierarchyPathHint;
            string action = _selected.Operation == RestoreOperation.RevertToInherited
                ? Lang.T("restore.confirm.revert", track, director, where)
                : Lang.T("restore.confirm.set", track, director, where, HistoryView.FormatValue(_selected.ResultValue));
            string save = _preview.IsScene
                ? Lang.T("restore.confirm.scene")
                : Lang.T("restore.confirm.prefab", BindingRestoreService.BackupDirectory);
            return $"{action}\n\n{save}";
        }

        private void Render()
        {
            if (_content == null)
            {
                return;
            }

            _content.Clear();
            if (_preview == null)
            {
                _content.Add(CreateMuted(Lang.T("restore.window.empty")));
                return;
            }

            AddSummary();
            AddOptions();
            AddMessages();
            AddImpact();
            AddButtons();
            AddResult();
        }

        private void AddSummary()
        {
            ChangeEvent changeEvent = _preview.Event;
            if (_removeOverrideOnly)
            {
                _content.Add(CreateHeader(Lang.T("restore.summary.removeHeader")));
            }
            else
            {
                _content.Add(CreateHeader(Lang.T("restore.summary.restoreHeader")));
                _content.Add(new Label(Lang.T("restore.summary.change", changeEvent.ChangeType, HistoryView.FormatLocalTime(changeEvent.ObservedAtUtc))));
            }

            _content.Add(new Label(Lang.T("restore.summary.where", _preview.OwnerPath, changeEvent.Owner.AssetKind)));
            _content.Add(new Label(Lang.T("restore.summary.director", changeEvent.Director.HierarchyPathHint)));
            _content.Add(new Label(Lang.T("restore.summary.track", changeEvent.Track.TrackPathHint, changeEvent.Track.TimelinePathHint)));
            string currentKey = _preview.Input.CurrentValueOverridden ? "restore.summary.currentOverridden" : "restore.summary.current";
            _content.Add(new Label(Lang.T(currentKey, HistoryView.FormatValue(_preview.Input.CurrentValue))));
            if (!_removeOverrideOnly)
            {
                _content.Add(new Label(Lang.T("restore.summary.before", HistoryView.FormatValue(changeEvent.Before), changeEvent.Before.LocalState)));
            }

            _content.Add(CreateMuted(Lang.T("restore.summary.onlyThisLevel")));
        }

        private void AddOptions()
        {
            if (!_preview.Plan.CanRestore)
            {
                return;
            }

            _content.Add(CreateHeader(Lang.T("restore.options.header")));
            foreach (RestoreOption option in _preview.Plan.Options)
            {
                RestoreOption captured = option;
                string result = option.Operation == RestoreOperation.RevertToInherited
                    ? Lang.T("restore.options.inheritedResult", HistoryView.FormatValue(option.ResultValue))
                    : HistoryView.FormatValue(option.ResultValue);
                string recommended = option.IsRecommended ? Lang.T("restore.options.recommended") : string.Empty;
                Toggle toggle = new Toggle { value = option == _selected, text = recommended + Lang.T("restore.options.text", option.Description, result) };
                toggle.RegisterValueChangedCallback(changeEvent => OnOptionSelected(captured));
                // 長い説明を折り返して全文を見せる
                foreach (Label label in toggle.Query<Label>().ToList())
                {
                    label.style.whiteSpace = WhiteSpace.Normal;
                    label.style.flexShrink = 1;
                }

                _content.Add(toggle);
            }
        }

        private void AddMessages()
        {
            foreach (string warning in _preview.Plan.Warnings)
            {
                _content.Add(CreateColored(Lang.T("restore.messages.warning", warning), AttentionColor));
            }

            if (_preview.Plan.CanRestore)
            {
                return;
            }

            if (_preview.Plan.IsAlreadyInEarlierState)
            {
                _content.Add(CreateHeader(Lang.T(_removeOverrideOnly ? "restore.messages.nothingToRemove" : "restore.messages.nothingToRestore")));
                _content.Add(CreateMuted(_preview.Plan.Refusals.FirstOrDefault() ?? string.Empty));
                return;
            }

            _content.Add(CreateHeader(Lang.T("restore.messages.cannotRestore")));
            foreach (string refusal in _preview.Plan.Refusals)
            {
                _content.Add(CreateColored($"• {refusal}", AttentionColor));
            }

            _content.Add(CreateMuted(Lang.T("restore.messages.fixManually")));
        }

        private void AddImpact()
        {
            _content.Add(CreateHeader(Lang.T("restore.impact.header")));
            if (_removeOverrideOnly)
            {
                _content.Add(new Label(Lang.T("restore.impact.removeOverride")));
            }
            else
            {
                int count = _preview.Event.AffectedContexts.Count;
                _content.Add(new Label(count > 0
                    ? Lang.T("restore.impact.affected", count)
                    : Lang.T("restore.impact.noneAffected")));
            }

            _content.Add(CreateMuted(Lang.T("restore.impact.unscanned")));
            _content.Add(CreateMuted(_preview.IsScene
                ? Lang.T("restore.impact.scene")
                : Lang.T("restore.impact.prefab")));
        }

        private void AddButtons()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 8;
            Button restore = new Button(OnRestoreClicked) { text = Lang.T(_removeOverrideOnly ? "restore.button.removeOverride" : "restore.button.restore") };
            restore.SetEnabled(_preview.Plan.CanRestore && _selected != null);
            row.Add(restore);
            row.Add(new Button(OnOpenOwnerClicked) { text = Lang.T(_preview.IsScene ? "restore.button.openScene" : "restore.button.openPrefab") });
            row.Add(new Button(OnRefreshClicked) { text = Lang.T("restore.button.refresh") });
            _content.Add(row);
        }

        private void AddResult()
        {
            if (_lastResult == null)
            {
                return;
            }

            _content.Add(CreateHeader(Lang.T("restore.result.header")));
            _content.Add(CreateColored(_lastResult.Message, _lastResult.Success ? SuccessColor : AttentionColor));
            foreach (string detail in _lastResult.Details)
            {
                _content.Add(CreateMuted(detail));
            }

            if (_lastResult.CanRollback)
            {
                Button undo = new Button(OnRollbackClicked) { text = Lang.T("restore.button.undoRestore") };
                undo.style.alignSelf = Align.FlexStart;
                _content.Add(undo);
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

        private static Label CreateMuted(string text)
        {
            return CreateColored(text, MutedColor);
        }

        private static Label CreateColored(string text, Color color)
        {
            Label label = new Label(text);
            label.style.color = color;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginBottom = 2;
            return label;
        }
    }
}
