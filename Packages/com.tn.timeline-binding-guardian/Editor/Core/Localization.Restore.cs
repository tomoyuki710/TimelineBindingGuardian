using System.Collections.Generic;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 復元（RestorePlanner / BindingRestoreService / RestorePreviewWindow）の翻訳表。
    /// 英語はテストが部分文字列で検証しているため、変更前のコードの文言をそのまま使う。
    /// </summary>
    internal static partial class LangTable
    {
        static partial void AddRestore(Dictionary<string, (string En, string Ja)> table)
        {
            AddRestorePlanner(table);
            AddRestoreService(table);
            AddRestoreWindow(table);
        }

        private static void AddRestorePlanner(Dictionary<string, (string En, string Ja)> table)
        {
            // 復元できない理由
            table["restore.refusal.alreadyEarlierState"] = ("The binding is already in the state before this change.",
                "バインドは既にこの変更の前の状態です。");
            table["restore.refusal.undetermined"] = ("The level that caused this change is undetermined, so the place to restore cannot be decided automatically.",
                "この変更を起こした階層が特定できないため、復元する場所を自動で決められません。");
            table["restore.refusal.unsupportedType"] = ("{0} changes cannot be restored by this tool.",
                "{0} の変更はこのツールでは復元できません。");
            table["restore.refusal.unsupportedBeforeState"] = ("The state before this change was {0}. Restoring it would require changing the binding array structure, which is not supported.",
                "この変更の前の状態は {0} でした。復元するにはバインドの配列の構造を変える必要があり、対応していません。");
            table["restore.refusal.entryMissingRestore"] = ("The binding entry does not exist at this level now. Restoring it would require changing the binding array structure, which is not supported.",
                "この階層には現在バインドの要素がありません。復元するにはバインドの配列の構造を変える必要があり、対応していません。");
            table["restore.refusal.entryMissingRemove"] = ("The binding entry does not exist at this level now. Removing it would require changing the binding array structure, which is not supported.",
                "この階層には現在バインドの要素がありません。Override を外すにはバインドの配列の構造を変える必要があり、対応していません。");
            table["restore.refusal.targetUnresolvable"] = ("The earlier target ({0}) can no longer be found or has a different type.",
                "変更前の参照先（{0}）が見つからないか、型が異なります。");
            table["restore.refusal.notInstance"] = ("This level defines the binding itself (it is not a prefab instance), so there is no override to remove.",
                "この階層はバインドを自身で定義している（Prefab インスタンスではない）ため、外す Override がありません。");
            table["restore.refusal.notOverridden"] = ("The binding is not overridden at this level.",
                "この階層ではバインドは Override されていません。");

            // 警告
            table["restore.warning.inheritedChanged"] = ("The inherited value has changed since then. Reverting gives {0}, not the earlier {1}.",
                "継承元の値がその後変わっています。継承に戻すと、変更前の {1} ではなく {0} になります。");

            // 操作の説明
            table["restore.option.revertToBefore"] = ("Remove the override at this level so the binding is inherited again (as it was before this change).",
                "この階層の Override を外し、再び継承するようにします（この変更の前と同じ状態）。");
            table["restore.option.setNone"] = ("Set the binding to None explicitly at this level (as it was before this change).",
                "この階層でバインドを明示的に None に設定します（この変更の前と同じ状態）。");
            table["restore.option.setTarget"] = ("Set the binding to {0} explicitly at this level.",
                "この階層でバインドを明示的に {0} に設定します。");
            table["restore.option.revertToSource"] = ("Remove the override at this level so the binding is inherited from the source prefab.",
                "この階層の Override を外し、継承元の Prefab の値を継承するようにします。");
        }

        private static void AddRestoreService(Dictionary<string, (string En, string Ja)> table)
        {
            // 実行・取り消しの結果
            table["restore.result.changedSincePreview"] = ("The binding has changed since the preview was created. Open the preview again.",
                "プレビューを作成した後にバインドが変更されました。プレビューを開き直してください。");
            table["restore.result.failed"] = ("Restore failed: {0}",
                "復元に失敗しました: {0}");
            table["restore.result.cannotRollback"] = ("This restore cannot be undone from a backup.",
                "この復元はバックアップから取り消せません。");
            table["restore.result.rollbackChanged"] = ("The prefab has changed since the restore, so it was not overwritten. The backup is at: {0}",
                "復元の後に Prefab が変更されたため、上書きしませんでした。バックアップの場所: {0}");
            table["restore.result.rollbackDone"] = ("The restore was undone (the prefab was put back from the backup).",
                "復元を取り消しました（バックアップから Prefab を戻しました）。");
            table["restore.result.removedAndSaved"] = ("Removed the override and saved {0}.",
                "Override を外して {0} を保存しました。");
            table["restore.result.restoredAndSaved"] = ("Restored and saved {0}.",
                "復元して {0} を保存しました。");
            table["restore.result.sceneVerifyFailed"] = ("Restore was undone because verification failed: {0}",
                "検証に失敗したため、復元を Undo で取り消しました: {0}");
            table["restore.result.restoredInScene"] = ("Restored in {0}. The scene is marked as modified; save it to keep the change (Undo is available).",
                "{0} で復元しました。Scene は変更ありの状態で、まだ保存されていません。変更を残すには保存してください（Undo できます）。");

            // 結果の詳細
            table["restore.detail.backup"] = ("Backup: {0}",
                "バックアップ: {0}");
            table["restore.detail.verified"] = ("Verified: only the selected binding changed in this prefab.",
                "検証済み: この Prefab で変更されたのは選択したバインドだけです。");
            table["restore.detail.notPutBack"] = ("The prefab was not restored from the backup because it changed meanwhile. Backup: {0}",
                "その間に Prefab が変更されたため、バックアップから戻しませんでした。バックアップ: {0}");
            table["restore.detail.putBack"] = ("The prefab was put back from the backup.",
                "バックアップから Prefab を戻しました。");

            // 復元を判定できない理由
            table["restore.unavailable.ownerMissing"] = ("The asset or scene where the change happened no longer exists.",
                "変更が起きた Asset または Scene がもう存在しません。");
            table["restore.unavailable.sceneNotLoaded"] = ("Open the scene first to restore a scene override: {0}",
                "Scene の Override を復元するには、先に Scene を開いてください: {0}");
            table["restore.unavailable.directorMissing"] = ("The director at that level could not be found.",
                "その階層の Director が見つかりません。");
            table["restore.unavailable.trackMissing"] = ("The track could not be found in the director's Timeline.",
                "Director の Timeline に Track が見つかりません。");

            // 書き込めない理由・競合の理由
            table["restore.readOnly.package"] = ("The asset is inside a package and cannot be edited here.",
                "この Asset は Package の中にあるため、ここでは編集できません。");
            table["restore.readOnly.notEditable"] = ("The asset is not editable (for example, locked by version control).",
                "この Asset は編集できません（バージョン管理でロックされているなど）。");
            table["restore.readOnly.model"] = ("Model prefabs cannot be edited. Override the binding in a prefab that contains it.",
                "Model Prefab は編集できません。これを含む Prefab でバインドを Override してください。");
            table["restore.conflict.prefabModeDirty"] = ("The prefab is open in Prefab Mode with unsaved changes. Save or close it first.",
                "この Prefab は未保存の変更がある状態で Prefab Mode で開かれています。先に保存するか閉じてください。");

            // 例外（結果のメッセージに出る）
            table["restore.error.directorNotInContents"] = ("The director could not be located in the prefab contents.",
                "Prefab の内容の中に Director が見つかりません。");
            table["restore.error.verifyAfterSaveFailed"] = ("Verification after saving failed: {0}",
                "保存後の検証に失敗しました: {0}");
            table["restore.error.notInstance"] = ("The director is not part of a prefab instance, so there is no override to remove.",
                "Director が Prefab インスタンスの一部ではないため、外す Override がありません。");
            table["restore.error.overrideCount"] = ("Expected exactly one override for the binding, found {0}.",
                "バインドの Override はちょうど 1 件のはずですが、{0} 件見つかりました。");
            table["restore.error.entryNotFound"] = ("The binding entry was not found.",
                "バインドの要素が見つかりません。");
            table["restore.error.targetNotInContents"] = ("The earlier target could not be located in the prefab contents.",
                "Prefab の内容の中に変更前の参照先が見つかりません。");

            // 検証で見つかった問題（"; " でつなげてメッセージに入る）
            table["restore.problem.notFoundAfterSave"] = ("the director or track could not be found after saving.",
                "保存後に Director または Track が見つかりません。");
            table["restore.problem.unrelatedChanged"] = ("an unrelated binding changed ({0})",
                "対象外のバインドが変更されました（{0}）");
            table["restore.problem.entryMissing"] = ("the restored binding entry is missing",
                "復元したバインドの要素がありません");
            table["restore.problem.unexpectedState"] = ("the restored binding is not in the expected state ('{0}')",
                "復元したバインドが期待した状態ではありません（'{0}'）");

            // Console への警告
            table["restore.log.historyNotSaved"] = ("[Timeline Binding Guardian] The restore was done, but history was not saved: {0}",
                "[Timeline Binding Guardian] 復元は完了しましたが、履歴を保存できませんでした: {0}");
        }

        private static void AddRestoreWindow(Dictionary<string, (string En, string Ja)> table)
        {
            table["restore.window.title"] = ("Restore Timeline Binding",
                "Timeline バインドの復元");
            table["restore.window.empty"] = ("Open this window from a notification or a history event.",
                "通知または履歴のイベントからこのウィンドウを開いてください。");

            // 確認ダイアログ
            table["restore.confirm.title"] = ("Restore Timeline Binding",
                "Timeline バインドの復元");
            table["restore.confirm.ok"] = ("Restore",
                "復元");
            table["restore.confirm.cancel"] = ("Cancel",
                "キャンセル");
            table["restore.confirm.revert"] = ("This will remove the override for {0} on {1} in {2}.",
                "{2} の {1} にある {0} の Override を外します。");
            table["restore.confirm.set"] = ("This will set the binding to {3} for {0} on {1} in {2}.",
                "{2} の {1} にある {0} のバインドを {3} に設定します。");
            table["restore.confirm.scene"] = ("The scene will be marked as modified (not saved). You can undo with Ctrl+Z.",
                "Scene は変更ありの状態になります（保存はしません）。Ctrl+Z で元に戻せます。");
            table["restore.confirm.prefab"] = ("The prefab will be saved. A backup is created first in:\n{0}",
                "Prefab を保存します。先に次の場所へバックアップを作成します:\n{0}");

            // 概要
            table["restore.summary.removeHeader"] = ("Remove the override at this level",
                "この階層の Override を外す");
            table["restore.summary.restoreHeader"] = ("Restore the binding to its state before this change",
                "バインドをこの変更の前の状態に戻す");
            table["restore.summary.change"] = ("Change: {0} (observed {1})",
                "変更: {0}（{1} に検出）");
            table["restore.summary.where"] = ("Where it will be changed: {0} ({1})",
                "変更する場所: {0}（{1}）");
            table["restore.summary.director"] = ("Director: {0}",
                "Director: {0}");
            table["restore.summary.track"] = ("Track: {0}   Timeline: {1}",
                "Track: {0}   Timeline: {1}");
            table["restore.summary.current"] = ("Current value at this level: {0}",
                "この階層の現在の値: {0}");
            table["restore.summary.currentOverridden"] = ("Current value at this level: {0} (overridden here)",
                "この階層の現在の値: {0}（この階層で Override）");
            table["restore.summary.before"] = ("Value before the change: {0} ({1})",
                "変更前の値: {0}（{1}）");
            table["restore.summary.onlyThisLevel"] = ("Only this level is changed. Upper levels (parent prefabs and scenes) are not edited; they follow it unless they override the binding themselves.",
                "変更するのはこの階層だけです。上位の階層（親の Prefab や Scene）は編集しません。上位の階層は、自身でバインドを Override していなければこの階層の値に従います。");

            // 操作の選択
            table["restore.options.header"] = ("Operation",
                "操作");
            table["restore.options.recommended"] = ("[Recommended] ",
                "[推奨] ");
            table["restore.options.text"] = ("{0}  → result: {1}",
                "{0}  → 結果: {1}");
            table["restore.options.inheritedResult"] = ("inherited from the source prefab: {0}",
                "継承元の Prefab から継承: {0}");

            // 警告・復元できない理由
            table["restore.messages.warning"] = ("Warning: {0}",
                "警告: {0}");
            table["restore.messages.nothingToRemove"] = ("Nothing to remove",
                "外す Override はありません");
            table["restore.messages.nothingToRestore"] = ("Nothing to restore",
                "戻す必要はありません");
            table["restore.messages.cannotRestore"] = ("This change cannot be restored automatically",
                "この変更は自動では復元できません");
            table["restore.messages.fixManually"] = ("Open the asset and fix the binding manually if needed.",
                "必要に応じて Asset を開き、バインドを手動で直してください。");

            // 影響
            table["restore.impact.header"] = ("Known impact",
                "わかっている影響");
            table["restore.impact.removeOverride"] = ("Parent prefabs and scenes that use this prefab will follow the inherited value, unless they override this binding themselves.",
                "この Prefab を使う親の Prefab や Scene は、自身でこのバインドを Override していなければ、継承した値に従います。");
            table["restore.impact.affected"] = ("{0} observed context(s) were affected by this change; they will follow the restored value unless they override it.",
                "この変更の影響を受けた Contextが {0} 件確認されています。これらは自身で Override していなければ、復元後の値に従います。");
            table["restore.impact.noneAffected"] = ("No observed context was affected by this change.",
                "この変更の影響を受けた Contextは確認されていません。");
            table["restore.impact.unscanned"] = ("Scenes and prefabs that have not been scanned may also inherit this binding. They are checked automatically when saved or opened.",
                "まだ検査していない Scene や Prefab も、このバインドを継承している可能性があります。それらは保存したときや開いたときに自動で検査されます。");
            table["restore.impact.scene"] = ("The scene is marked as modified and is not saved. Save it to keep the change; Ctrl+Z undoes it.",
                "Scene は変更ありの状態になるだけで、保存はされません。変更を残すには保存してください。Ctrl+Z で元に戻せます。");
            table["restore.impact.prefab"] = ("The prefab is saved immediately. Prefab saves cannot be undone with Ctrl+Z, so a backup is created and 'Undo this restore' is offered.",
                "Prefab はすぐに保存されます。Prefab の保存は Ctrl+Z では戻せないため、バックアップを作成し、「この復元を取り消す」ボタンを表示します。");

            // ボタン
            table["restore.button.removeOverride"] = ("Remove Override…",
                "Override を外す…");
            table["restore.button.restore"] = ("Restore…",
                "復元…");
            table["restore.button.openScene"] = ("Open Scene",
                "Scene を開く");
            table["restore.button.openPrefab"] = ("Open Prefab",
                "Prefab を開く");
            table["restore.button.refresh"] = ("Refresh",
                "更新");

            // 結果
            table["restore.result.header"] = ("Result",
                "結果");
            table["restore.button.undoRestore"] = ("Undo this restore",
                "この復元を取り消す");
        }
    }
}
