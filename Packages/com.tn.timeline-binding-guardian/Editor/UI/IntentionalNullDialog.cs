using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 意図的な NULL として共有リストに登録する確認ウィンドウ（理由は任意）。
    /// </summary>
    internal sealed class IntentionalNullDialog : EditorWindow
    {
        private static readonly Color MutedColor = new Color(0.6f, 0.6f, 0.6f);
        private static readonly Color ErrorColor = new Color(0.95f, 0.45f, 0.35f);

        // Domain Reload 後も内容を保つため保存する
        [SerializeField] private IntentionalNullEntry draft;
        private TextField _reason;
        private Label _error;

        public static void Open(IntentionalNullEntry draft)
        {
            IntentionalNullDialog window = GetWindow<IntentionalNullDialog>(true, Lang.T("intentional.dialog.title"));
            window.titleContent = new GUIContent(Lang.T("intentional.dialog.title"));
            window.minSize = new Vector2(520, 300);
            window.draft = draft;
            window.Build();
            window.ShowUtility();
        }

        private void CreateGUI()
        {
            Build();
        }

        private void Build()
        {
            rootVisualElement.Clear();
            VisualElement root = rootVisualElement;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.paddingTop = 8;
            if (draft == null)
            {
                return;
            }

            root.Add(CreateLabel(Lang.T("intentional.dialog.header"), FontStyle.Bold));
            root.Add(CreateLabel(Lang.T("intentional.dialog.target", draft.OwnerPath, draft.DirectorPath, draft.TrackPath, draft.TimelinePath), FontStyle.Normal));
            root.Add(CreateMuted(Lang.T("intentional.dialog.effect")));
            root.Add(CreateMuted(Lang.T("intentional.dialog.share", IntentionalNullRegistry.DefaultFilePath)));

            _reason = new TextField(Lang.T("intentional.dialog.reason")) { multiline = true };
            _reason.style.minHeight = 48;
            _reason.style.marginTop = 6;
            root.Add(_reason);

            _error = CreateLabel(string.Empty, FontStyle.Normal);
            _error.style.color = ErrorColor;
            root.Add(_error);

            VisualElement buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.justifyContent = Justify.FlexEnd;
            buttons.style.marginTop = 6;
            buttons.Add(new Button(OnRegisterClicked) { text = Lang.T("intentional.dialog.ok") });
            buttons.Add(new Button(OnCancelClicked) { text = Lang.T("window.dialog.cancel") });
            root.Add(buttons);
        }

        private void OnRegisterClicked()
        {
            Register();
        }

        private void OnCancelClicked()
        {
            Close();
        }

        private void Register()
        {
            string now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            IntentionalNullEntry entry = new IntentionalNullEntry(draft.OwnerLevelKey, draft.OwnerPath, draft.DirectorPath, draft.TimelinePath,
                draft.TrackPath, (_reason.value ?? string.Empty).Trim(), now);
            if (!IntentionalNullRegistry.TryAdd(entry, out string error))
            {
                _error.text = Lang.T("intentional.error", error);
                return;
            }

            Close();
        }

        private static Label CreateLabel(string text, FontStyle style)
        {
            Label label = new Label(text);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.unityFontStyleAndWeight = style;
            label.style.marginBottom = 4;
            return label;
        }

        private static Label CreateMuted(string text)
        {
            Label label = CreateLabel(text, FontStyle.Normal);
            label.style.color = MutedColor;
            return label;
        }
    }
}
