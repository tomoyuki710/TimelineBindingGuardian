using System.Collections.Generic;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// Phase 5（ビルド前検査・Build Scene の一括検査・チーム共有）の文言。
    /// </summary>
    internal static partial class LangTable
    {
        static partial void AddPhase5(Dictionary<string, (string En, string Ja)> table)
        {
            AddPreBuild(table);
            AddBuildScan(table);
            AddIntentionalNull(table);
            AddResponsiveness(table);
            table["common.listEmpty"] = ("Nothing to show.", "表示する項目はありません。");
            table["views.common.position"] = ("Position when recorded: {0}", "位置（記録時点）: {0}");
            table["views.common.position.tooltip"] = (
                "Names with the sibling index (#n) at the time this was recorded. It is only for display: objects are identified by their GlobalObjectId, so deleting or reordering siblings later does not cause a mix-up, but later records may show a different number for the same object.",
                "記録した時点の、名前と兄弟の中での番号（#n）です。表示のためだけのもので、オブジェクトの識別には GlobalObjectId を使っています。そのため、後で兄弟が削除・並べ替えされても取り違えることはありませんが、同じオブジェクトでも後の記録では番号が変わることがあります。");
        }

        private static void AddResponsiveness(Dictionary<string, (string En, string Ja)> table)
        {
            table["engine.defer.prefabMode"] = ("checked when Prefab Mode is closed", "Prefab Mode を閉じると検査します");
            table["engine.defer.waitingForSave"] = ("checked when the scene is saved", "Scene を保存すると検査します");
            table["views.settings.timingLog"] = ("Log processing time to the Console (for troubleshooting)", "処理時間を Console に出す（調査用）");
            table["views.settings.timingLog.tooltip"] = (
                "For each automatic check, log the target and the time it took (fingerprint, scan, record, and finding dependents) to the Console. Use it to find out what is slow.",
                "自動検査 1 件ごとに、対象と処理時間の内訳（指紋の計算・検査・記録・依存先の予約）を Console に出します。何に時間がかかっているかを調べるときに使います。");
        }

        private static void AddIntentionalNull(Dictionary<string, (string En, string Ja)> table)
        {
            table["intentional.file.invalidJson"] = ("The shared file is not valid JSON (for example, it contains merge conflict markers): {0}",
                "共有ファイルが正しい JSON ではありません（マージの衝突記号が残っている等）: {0}");
            table["intentional.file.notThisFormat"] = ("The shared file is not an intentional-null list.", "共有ファイルが意図的な NULL の一覧の形式ではありません。");
            table["intentional.file.newerFormat"] = ("The shared file uses a newer format (version {0}; this version supports {1}). Update the package.",
                "共有ファイルの形式が新しすぎます（版 {0}。この版が扱えるのは {1} まで）。パッケージを更新してください。");
            table["intentional.file.readFailed"] = ("The shared file could not be read: {0}", "共有ファイルを読めませんでした: {0}");
            table["intentional.file.saveFailed"] = ("The shared file could not be saved: {0}", "共有ファイルを保存できませんでした: {0}");
            table["intentional.error"] = ("Intentional null list: {0}", "意図的な NULL の一覧: {0}");
            table["intentional.button.register"] = ("Mark as Intentional Null…", "意図的な NULL として登録…");
            table["intentional.button.register.tooltip"] = (
                "Add this level to the shared list of intentional nulls. Nobody on the team is notified when it becomes null (after you commit the file).",
                "この階層を、意図的な NULL の共有リストに追加します。ファイルをコミットすると、チームの誰の手元でも NULL になったことを通知しなくなります。");
            table["intentional.button.unregister"] = ("Remove", "登録を外す");
            table["intentional.mark"] = ("[Intentional null (shared)]", "［意図的な NULL（共有）］");
            table["intentional.mark.tooltip"] = (
                "This level is in the shared list of intentional nulls, so it is not notified. It is still recorded in the history.",
                "この階層は意図的な NULL の共有リストに載っているため、通知しません。履歴には記録します。");
            table["intentional.dialog.title"] = ("Mark as Intentional Null", "意図的な NULL として登録");
            table["intentional.dialog.header"] = ("Mark this binding as an intentional null", "このバインドを意図的な NULL として登録します");
            table["intentional.dialog.target"] = ("Level: {0}\nDirector (position when registered): {1}\nTrack: {2}  (Timeline: {3})",
                "階層: {0}\nDirector（登録時点の位置）: {1}\nTrack: {2}（Timeline: {3}）");
            table["intentional.dialog.effect"] = (
                "While this level is in the list, it is not notified when it becomes null, even if it is bound again and then set to null later. The same track set to null in another prefab or scene is still notified.",
                "登録している間は、この階層が NULL になっても通知しません（一度 Bound に戻って再び NULL になった場合も）。別の Prefab や Scene で同じ Track が NULL になった場合は通知します。");
            table["intentional.dialog.share"] = (
                "It is saved to the shared file below. Commit it so that the whole team stops being notified:\n{0}",
                "次の共有ファイルに保存されます。コミットすると、チーム全員の手元で通知されなくなります:\n{0}");
            table["intentional.dialog.reason"] = ("Reason (optional)", "理由（任意）");
            table["intentional.dialog.ok"] = ("Register", "登録する");
            table["intentional.settings.header"] = ("Intentional nulls (shared with the team)", "意図的な NULL（チームで共有）");
            table["intentional.settings.note"] = (
                "Levels in this list are not notified on anyone's machine when they become null. The list is saved in the file below; commit it to share it with the team:\n{0}",
                "この一覧の階層は、NULL になっても誰の手元でも通知しません。一覧は次のファイルに保存されます。コミットしてチームで共有してください:\n{0}");
            table["intentional.settings.fileError"] = (
                "The shared intentional-null file cannot be used, so notifications are not suppressed: {0}",
                "意図的な NULL の共有ファイルを使えないため、通知を止めていません: {0}");
            table["intentional.state.null"] = ("currently Null", "現在は Null");
            table["intentional.state.bound"] = ("currently Bound (may no longer be needed)", "現在は Bound（不要かもしれません）");
            table["intentional.state.unobserved"] = ("not observed on this machine yet", "この PC ではまだ観測していません");
        }

        private static void AddBuildScan(Dictionary<string, (string En, string Ja)> table)
        {
            table["engine.reason.buildScan"] = ("Build scene scan", "Build Scene の一括検査");
            table["engine.buildScan.blocked.playMode"] = ("Play Mode is active.", "Play Mode 中です。");
            table["engine.buildScan.blocked.compiling"] = ("Unity is compiling or importing assets.", "Unity がコンパイル中、または Asset をインポート中です。");
            table["engine.buildScan.blocked.prefabMode"] = ("A prefab is open in Prefab Mode. Close it first.", "Prefab Mode で Prefab を開いています。先に閉じてください。");
            table["engine.buildScan.blocked.unsaved"] = (
                "Open scenes have unsaved changes: {0}. Save them first.",
                "開いている Scene に未保存の変更があります: {0}。先に保存してください。");
            table["window.buildScan.button"] = ("Scan Build Scenes…", "Build Scene を検査…");
            table["window.buildScan.tooltip"] = (
                "Scan every enabled scene in Build Settings. Closed scenes are opened temporarily, one at a time, and closed again.",
                "Build Settings で有効なすべての Scene を検査します。閉じている Scene は 1 つずつ一時的に開き、検査後に閉じます。");
            table["window.buildScan.blocked"] = ("Cannot scan build scenes: {0}", "Build Scene を検査できません: {0}");
            table["window.buildScan.noScenes"] = ("There are no enabled scenes in Build Settings.", "Build Settings に有効な Scene がありません。");
            table["window.buildScan.confirm"] = (
                "Scan {0} build scene(s)?\n\n{1} closed scene(s) will be opened temporarily, one at a time, and closed again. Your open scenes, active scene, and selection are restored afterwards. Opening a scene may run scripts in your project (for example, [ExecuteAlways]).",
                "{0} 件の Build Scene を検査しますか？\n\n閉じている {1} 件の Scene を 1 つずつ一時的に開き、検査後に閉じます。終了後、開いている Scene・アクティブな Scene・選択は元に戻します。Scene を開くと、プロジェクトのスクリプト（[ExecuteAlways] など）が動く場合があります。");
            table["window.buildScan.confirm.ok"] = ("Scan", "検査する");
            table["window.buildScan.progress"] = ("Scanning build scenes ({0}/{1}): {2}", "Build Scene を検査中（{0}/{1}）: {2}");
            table["window.buildScan.done"] = (
                "Build scene scan finished: {0} scene(s) recorded ({1} opened temporarily). {2} new event(s), {3} newly unbound.",
                "Build Scene の検査が終わりました: {0} 件の Scene を記録（うち {1} 件を一時的に開きました）。新しいイベント {2} 件、新たに未バインド {3} 件。");
            table["window.buildScan.cancelled"] = (
                "Build scene scan cancelled: {0} scene(s) recorded before cancelling ({1} opened temporarily). {2} new event(s), {3} newly unbound.",
                "Build Scene の検査をキャンセルしました: キャンセルまでに {0} 件の Scene を記録（うち {1} 件を一時的に開きました）。新しいイベント {2} 件、新たに未バインド {3} 件。");
        }

        private static void AddPreBuild(Dictionary<string, (string En, string Ja)> table)
        {
            table["engine.reason.preBuild"] = ("Pre-build check", "ビルド前検査");
            table["engine.preBuild.sceneClosed"] = (
                "{0}: the scene is not open, so only the prefabs it uses were checked (the scene itself is checked when opened).",
                "{0}: Scene が開かれていないため、使用している Prefab だけを検査しました（Scene 自体は開いたときに検査します）。");
            table["engine.warning.preBuildFailed"] = (
                "[Timeline Binding Guardian] The pre-build check failed and was skipped. The build continues. ({0})",
                "[Timeline Binding Guardian] ビルド前検査に失敗したため省略しました。ビルドは続行します。（{0}）");
            table["views.settings.preBuildCheck"] = ("Pre-build check", "ビルド前検査");
            table["views.settings.preBuildCheck.tooltip"] = (
                "When a build starts, check the open build scenes and the prefabs that the build scenes use. The build is never stopped.",
                "ビルドの開始時に、開いている Build Scene と、Build Scene が使用している Prefab を検査します。ビルドは止めません。");
        }
    }
}
