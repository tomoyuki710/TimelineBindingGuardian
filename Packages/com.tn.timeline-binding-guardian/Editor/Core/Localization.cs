using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TimelineBindingGuardian
{
    public enum UiLanguage
    {
        English,
        Japanese,
    }

    public enum LanguagePreference
    {
        // OS の言語設定に合わせる
        Auto,
        English,
        Japanese,
    }

    /// <summary>
    /// 表示文言の翻訳。Unity API に依存しない。
    /// - 文言はキーで引き、英語と日本語の両方を翻訳表（LangTable の各ファイル）に持つ。
    /// - 専門用語（Bound / Null / No Entry / Override / Prefab / Scene / Director / Track / Timeline など）は日本語でも英語のまま。
    /// - 履歴に保存する注記は Note() でキーと引数の形にして保存し、表示時に RenderNote() で翻訳する（保存時の言語に固定しない）。
    /// </summary>
    public static class Lang
    {
        private const char NotePrefix = '@';
        private const char NoteSeparator = '\u001f';

        private static Dictionary<string, (string En, string Ja)> _table;

        public static UiLanguage Current { get; set; } = UiLanguage.English;

        // テストの実行中など、表示言語を一時的に上書きしている間は true。ウィンドウはこの間の表示更新を止め、終了後に更新し直す
        // （上書き中の言語で作られた文言が一瞬表示されないように）
        public static bool IsTemporarilyOverridden { get; set; }

        public static IReadOnlyDictionary<string, (string En, string Ja)> Table => _table ?? (_table = LangTable.Build());

        /// <summary>
        /// 現在の言語の文言。引数は string.Format の {0} {1} … に入る。キーが無ければキーをそのまま返す（表示が欠けないように）。
        /// </summary>
        public static string T(string key, params object[] args)
        {
            if (!Table.TryGetValue(key, out (string En, string Ja) entry))
            {
                return key;
            }

            string format = Current == UiLanguage.Japanese && !string.IsNullOrEmpty(entry.Ja) ? entry.Ja : entry.En;
            return args == null || args.Length == 0 ? format : string.Format(CultureInfo.InvariantCulture, format, args);
        }

        /// <summary>
        /// 履歴に保存する注記（キーと引数）。表示時に RenderNote で現在の言語にする。
        /// </summary>
        public static string Note(string key, params object[] args)
        {
            IEnumerable<string> parts = new[] { key }.Concat((args ?? Array.Empty<object>()).Select(a => Convert.ToString(a, CultureInfo.InvariantCulture)));
            return NotePrefix + string.Join(NoteSeparator.ToString(), parts);
        }

        /// <summary>
        /// 保存された注記を現在の言語で表示する。キー形式でなければ（以前の版で保存した英語の注記など）そのまま返す。
        /// </summary>
        public static string RenderNote(string stored)
        {
            if (string.IsNullOrEmpty(stored) || stored[0] != NotePrefix)
            {
                return stored ?? string.Empty;
            }

            string[] parts = stored.Substring(1).Split(NoteSeparator);
            return T(parts[0], parts.Skip(1).Cast<object>().ToArray());
        }

        /// <summary>
        /// 設定から表示言語を決める。Auto のときは OS の言語設定（日本語なら日本語、それ以外は英語）。
        /// </summary>
        public static UiLanguage Resolve(LanguagePreference preference, CultureInfo osCulture)
        {
            switch (preference)
            {
                case LanguagePreference.English:
                    return UiLanguage.English;
                case LanguagePreference.Japanese:
                    return UiLanguage.Japanese;
            }

            bool osIsJapanese = osCulture != null && osCulture.TwoLetterISOLanguageName == "ja";
            return osIsJapanese ? UiLanguage.Japanese : UiLanguage.English;
        }
    }

    /// <summary>
    /// 翻訳表。担当範囲ごとのファイル（Localization.*.cs）で Add… を実装し、ここで 1 つの表にまとめる。
    /// </summary>
    internal static partial class LangTable
    {
        public static Dictionary<string, (string En, string Ja)> Build()
        {
            Dictionary<string, (string En, string Ja)> table = new Dictionary<string, (string En, string Ja)>(StringComparer.Ordinal);
            AddCommon(table);
            AddWindow(table);
            AddViews(table);
            AddRestore(table);
            AddEngine(table);
            AddPhase5(table);
            return table;
        }

        static partial void AddWindow(Dictionary<string, (string En, string Ja)> table);

        static partial void AddViews(Dictionary<string, (string En, string Ja)> table);

        static partial void AddRestore(Dictionary<string, (string En, string Ja)> table);

        static partial void AddEngine(Dictionary<string, (string En, string Ja)> table);

        static partial void AddPhase5(Dictionary<string, (string En, string Ja)> table);

        private static void AddCommon(Dictionary<string, (string En, string Ja)> table)
        {
            table["common.language"] = ("Language", "言語");
            table["common.language.auto"] = ("Auto (follow the OS language)", "自動（OS の言語設定に合わせる）");
            table["common.language.english"] = ("English", "English");
            table["common.language.japanese"] = ("日本語", "日本語");
        }
    }
}
