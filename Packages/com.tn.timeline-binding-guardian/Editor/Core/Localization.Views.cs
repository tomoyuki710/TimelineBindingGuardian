using System.Collections.Generic;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 翻訳表: Notifications / History / Settings タブと Hierarchy アイコンの文言。
    /// </summary>
    internal static partial class LangTable
    {
        static partial void AddViews(Dictionary<string, (string En, string Ja)> table)
        {
            AddViewsCommon(table);
            AddViewsNotifications(table);
            AddViewsHistory(table);
            AddViewsSettings(table);
            AddViewsHierarchy(table);
        }

        // 複数のビューで共通の文言
        private static void AddViewsCommon(Dictionary<string, (string En, string Ja)> table)
        {
            table["views.common.clear"] = ("Clear", "解除");
            table["views.common.cancel"] = ("Cancel", "キャンセル");
            table["views.common.ok"] = ("OK", "OK");
            table["views.common.openPrefab"] = ("Open Prefab", "Prefab を開く");
            table["views.common.changedAt"] = ("Changed at: {0} ({1})", "変更箇所: {0}（{1}）");
            table["views.common.director"] = ("Director: {0}", "Director: {0}");
            table["views.common.trackTimeline"] = ("Track: {0}   Timeline: {1}", "Track: {0}   Timeline: {1}");
            table["views.common.valueAtLevel"] = ("Value at that level: {0} → {1}", "その階層での値: {0} → {1}");
            table["views.common.originUndetermined"] = ("Origin undetermined: the level that caused this change could not be identified.", "変更元不明: この変化を起こした階層を特定できませんでした。");
            table["views.common.effectiveHeader"] = ("Effective value in observed contexts ({0})", "観測済みの Context での実効値（{0}）");
            table["views.common.impact.originUndetermined"] = ("origin undetermined", "変更元不明");
            table["views.common.impact.affects"] = ("affects {0} context(s)", "{0} 件の Context に影響");
            table["views.common.impact.noEffect"] = ("no effect on observed contexts", "観測済みの Context に影響なし");
        }

        private static void AddViewsNotifications(Dictionary<string, (string En, string Ja)> table)
        {
            table["views.notifications.showDismissed"] = ("Show dismissed", "消去済みも表示");
            table["views.notifications.search.tooltip"] = ("Search prefab, scene, director, or track", "Prefab・Scene・Director・Track を検索します");
            table["views.notifications.dismissShown"] = ("Dismiss Shown", "表示中を消去");
            table["views.notifications.dismissShown.tooltip"] = ("Dismiss the unread notifications currently listed.", "一覧に表示中の未読の通知を消去します。");
            table["views.notifications.dismissAll"] = ("Dismiss All…", "すべて消去…");
            table["views.notifications.dismissAll.tooltip"] = ("Dismiss every unread notification (asks for confirmation).", "未読の通知をすべて消去します（確認があります）。");
            table["views.notifications.dismissAll.confirm"] = (
                "Dismiss all {0} unread notification(s)?\nHistory and current bindings are not changed, and future changes are still notified.",
                "未読の通知 {0} 件をすべて消去しますか？\n履歴と現在のバインドは変わりません。今後の変化も引き続き通知されます。");
            table["views.notifications.dismissAll.ok"] = ("Dismiss All", "すべて消去");
            table["views.notifications.count"] = ("{0} shown, {1} unread", "表示 {0} 件、未読 {1} 件");
            table["views.notifications.filter"] = ("Showing notifications for: {0}", "{0} の通知を表示しています");
            table["views.notifications.intro"] = (
                "A notification means a binding became unbound. It is not necessarily an error (a parent prefab may clear it on purpose). Dismissing does not mark the binding as correct.",
                "通知は、バインドが未バインドになったことを示します。必ずしもエラーではありません（親の Prefab が意図的に外している場合があります）。消去しても、そのバインドが正しいと認めたことにはなりません。");
            table["views.notifications.dismissFailed"] = ("Notifications could not be dismissed: {0}", "通知を消去できませんでした: {0}");
            table["views.notifications.flag.dismissed"] = ("dismissed", "消去済み");
            table["views.notifications.flag.resolved"] = ("resolved now", "現在は解消済み");
            table["views.notifications.flag.unscanned"] = ("{0} scene(s) not scanned yet", "未検査の Scene {0} 件");
            table["views.notifications.selectPrompt"] = ("Select a notification to see details.", "通知を選択すると詳細を表示します。");
            table["views.notifications.details.header"] = ("Newly unbound binding (observed {0})", "新たに未バインドになったバインド（観測: {0}）");
            table["views.notifications.state.resolved"] = (
                "Current state: bound again (resolved). The notification stays until dismissed.",
                "現在の状態: 再びバインドされています（解消済み）。通知は消去するまで残ります。");
            table["views.notifications.state.unbound"] = ("Current state: still unbound.", "現在の状態: 未バインドのままです。");
            table["views.notifications.dismissedAt"] = ("Dismissed at {0}.", "{0} に消去しました。");
            table["views.notifications.dismiss"] = ("Dismiss", "消去");
            table["views.notifications.pingTimeline"] = ("Ping Timeline Asset (Project)", "Timeline アセットを Ping（Project）");
            table["views.notifications.restorePreview"] = ("Restore Preview…", "復元のプレビュー…");
            table["views.notifications.noAffected"] = (
                "No observed context changed its effective value (for example, an upper level overrides this binding, or no scene using it has been scanned yet).",
                "実効値が変わった観測済みの Context はありません（上位の階層がこのバインドを Override している、または使用している Scene がまだ検査されていない、など）。");
            table["views.notifications.unscannedHeader"] = (
                "Scenes that may be affected but have not been scanned since this change ({0})",
                "影響する可能性があり、この変化以降まだ検査されていない Scene（{0}）");
            table["views.notifications.unscannedNote"] = ("These scenes are checked automatically when they are opened.", "これらの Scene は開いたときに自動で検査されます。");
        }

        private static void AddViewsHistory(Dictionary<string, (string En, string Ja)> table)
        {
            table["views.history.period.last24Hours"] = ("Last 24 hours", "過去 24 時間");
            table["views.history.period.last7Days"] = ("Last 7 days", "過去 7 日間");
            table["views.history.period.sinceDate"] = ("Since date", "指定日時以降");
            table["views.history.period.allTime"] = ("All time", "全期間");
            table["views.history.period.sinceLastBuild"] = ("Since last build ({0})", "前回ビルド以降（{0}）");
            table["views.history.period.sinceLastBuild.none"] = ("Since last build (no record)", "前回ビルド以降（記録なし）");
            table["views.history.type.bindingChangesOnly"] = ("Binding changes only", "バインドの変化のみ");
            table["views.history.type.all"] = ("All types", "すべての種別");
            table["views.history.since.tooltip"] = ("Local date/time, e.g. 2026-11-27 or 2026-11-27 10:00", "ローカルの日付・時刻（例: 2026-11-27、2026-11-27 10:00）");
            table["views.history.search.tooltip"] = ("Search scene, prefab, director, or track", "Scene・Prefab・Director・Track を検索します");
            table["views.history.timesNote"] = (
                "Times are observation times (when a scan noticed the change), not when the change was made or who made it.",
                "時刻は観測時刻（検査で変化に気づいた時刻）です。変更が行われた時刻や、変更した人を示すものではありません。");
            table["views.history.store.writable"] = (
                "History folder: {0} ({1} KB on disk, {2} event(s), local only)",
                "履歴フォルダー: {0}（ディスク上 {1} KB、イベント {2} 件、ローカルのみ）");
            table["views.history.store.readOnly"] = ("History is read-only ({0}): {1}  Folder: {2}", "履歴は読み取り専用です（{0}）: {1}  フォルダー: {2}");
            table["views.history.count"] = ("{0} event(s)", "{0} 件");
            table["views.history.filter"] = ("Showing history for: {0}", "{0} の履歴を表示しています");
            table["views.history.selectPrompt"] = (
                "Select an event to see details. Null is not necessarily an error; a parent prefab may clear a binding on purpose.",
                "イベントを選択すると詳細を表示します。Null は必ずしもエラーではありません（親の Prefab が意図的にバインドを外している場合があります）。");
            table["views.history.details.header"] = ("{0}   (observed {1})", "{0}   （観測: {1}）");
            table["views.history.restore"] = ("Restore to before this change…", "この変化の前に復元…");
            table["views.history.noAffected"] = (
                "No observed scene context changed its effective value (for example, an upper level overrides this binding).",
                "実効値が変わった観測済みの Scene の Context はありません（上位の階層がこのバインドを Override している、など）。");
            table["views.history.pathTooltip"] = (
                "Path format: Name#SiblingIndex (distinguishes instances with the same name).",
                "パスの形式: Name#SiblingIndex（同じ名前のインスタンスを区別します）。");
        }

        private static void AddViewsSettings(Dictionary<string, (string En, string Ja)> table)
        {
            table["views.settings.monitoring"] = ("Automatic monitoring", "自動監視");
            table["views.settings.monitoring.tooltip"] = (
                "Scan automatically when scenes and prefabs are saved, imported, or opened, and when the editor starts.",
                "Scene や Prefab の保存・インポート・オープン時と、Editor の起動時に自動で検査します。");
            table["views.settings.monitoring.note"] = (
                "While off, changes are not observed. They are compared at the next scan (the observation time will be the time of that scan).",
                "オフの間は変化を観測しません。次回の検査で比較されます（観測時刻はその検査の時刻になります）。");
            table["views.settings.hierarchyIcons"] = ("Hierarchy icons", "Hierarchy アイコン");
            table["views.settings.hierarchyIcons.tooltip"] = (
                "Show the number of unread notifications next to directors and their parents in the Hierarchy.",
                "Hierarchy で、Director とその親の横に未読の通知数を表示します。");
            table["views.settings.consoleWarnings"] = ("Console warnings", "Console への警告");
            table["views.settings.consoleWarnings.tooltip"] = (
                "Write one summarized warning to the Console when new unbound bindings are detected.",
                "新たな未バインドを検出したとき、Console に要約した警告を 1 件出力します。");
            table["views.settings.excluded.header"] = ("Excluded folders", "除外するフォルダー");
            table["views.settings.excluded.note"] = (
                "Scenes, prefabs, and timelines in these folders (and their subfolders) are not monitored or recorded, even by {0}. Existing history is kept.",
                "これらのフォルダー（サブフォルダーを含む）にある Scene・Prefab・Timeline は、「{0}」でも監視・記録しません。既存の履歴は残ります。");
            table["views.settings.excluded.none"] = ("(none)", "（なし）");
            table["views.settings.excluded.add"] = ("Add Folder…", "フォルダーを追加…");
            table["views.settings.excluded.remove"] = ("Remove", "削除");
            table["views.settings.excluded.panelTitle"] = ("Exclude folder from Timeline Binding Guardian", "Timeline Binding Guardian の対象から除外するフォルダー");
            table["views.settings.excluded.outsideAssets"] = ("Select a folder inside this project's Assets folder.", "このプロジェクトの Assets フォルダー内のフォルダーを選択してください。");
            table["views.settings.history.header"] = ("History", "履歴");
            table["views.settings.history.store"] = (
                "Folder: {0}\nSize on disk: {1} KB   Scopes: {2}   Events: {3}   Status: {4}",
                "フォルダー: {0}\nディスク上のサイズ: {1} KB   Scope: {2}   イベント: {3}   状態: {4}");
            table["views.settings.history.statusOk"] = ("OK", "OK");
            table["views.settings.history.statusReadOnly"] = ("read-only ({0}): {1}", "読み取り専用（{0}）: {1}");
            table["views.settings.history.privacy"] = (
                "History and notification states are stored only on this computer (UserSettings is normally not under version control). Nothing is sent over the network.",
                "履歴と通知の状態はこのコンピューターにのみ保存されます（UserSettings は通常バージョン管理の対象外です）。ネットワークには何も送信しません。");
            table["views.settings.history.limitation"] = (
                "History starts at the first observation. Bindings that were already broken before that cannot be detected, and changes that happen and revert between scans may not be observed.",
                "履歴は最初の観測から始まります。それ以前から壊れていたバインドは検出できません。また、検査と検査の間に起きて元に戻った変化は観測されないことがあります。");
        }

        private static void AddViewsHierarchy(Dictionary<string, (string En, string Ja)> table)
        {
            table["views.hierarchy.tooltip"] = (
                "{0} unread Timeline binding notification(s). Click to review, right-click to dismiss.",
                "未読の Timeline バインド通知が {0} 件あります。クリックで確認、右クリックで消去できます。");
            table["views.hierarchy.menu.header"] = ("{0} unread notification(s) for {1} and its children", "{1} とその子の未読の通知: {0} 件");
            table["views.hierarchy.menu.dismiss"] = ("Dismiss {0} notification(s) (this change only)", "{0} 件の通知を消去（この変化のみ）");
            table["views.hierarchy.menu.show"] = ("Show notifications", "通知を表示");
            table["views.hierarchy.selectedObject"] = ("selected object", "選択したオブジェクト");
        }
    }
}
