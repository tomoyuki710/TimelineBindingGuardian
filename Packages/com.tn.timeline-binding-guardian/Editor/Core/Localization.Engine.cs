using System.Collections.Generic;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 翻訳表（検査・差分・履歴の保存・自動監視・Console 通知・設定）。
    /// 英語は以前のコードの文言をそのまま使う（テストが英語の部分文字列で検証しているため）。
    /// </summary>
    internal static partial class LangTable
    {
        static partial void AddEngine(Dictionary<string, (string En, string Ja)> table)
        {
            AddEngineScan(table);
            AddEngineLevel(table);
            AddEngineStore(table);
            AddEngineHistory(table);
            AddEngineMonitor(table);
            AddEngineNotify(table);
            AddEngineNotes(table);
        }

        // スキャンの診断（SceneBindingScanner の diagnostics）
        private static void AddEngineScan(Dictionary<string, (string En, string Ja)> table)
        {
            table["engine.scan.unsavedScene"] = (
                "Scene is not saved. Stable IDs are unavailable, so this scene is excluded from persistent history.",
                "Scene が保存されていません。安定した ID が得られないため、この Scene は履歴に記録しません。");
            table["engine.scan.dirtyScene"] = (
                "Scene has unsaved changes. History records saved states only, so this scan is not recorded.",
                "Scene に未保存の変更があります。履歴は保存済みの状態だけを記録するため、この検査は記録しません。");
            table["engine.scan.prefabLoadFailed"] = (
                "Prefab asset could not be loaded: {0}",
                "Prefab Asset を読み込めませんでした: {0}");
            table["engine.scan.noPlayableAsset"] = (
                "No playable asset is assigned.",
                "Playable Asset が割り当てられていません。");
            table["engine.scan.notTimelineAsset"] = (
                "Playable asset is not a TimelineAsset.",
                "Playable Asset が TimelineAsset ではありません。");
            table["engine.scan.sceneBindingsNotFound"] = (
                "L{0} {1}: m_SceneBindings was not found.",
                "L{0} {1}: m_SceneBindings が見つかりませんでした。");
            table["engine.scan.staleKeys"] = (
                "L{0} {1}: {2} stale binding key(s) (deleted tracks).",
                "L{0} {1}: 古いバインドのキーが {2} 件あります（削除された Track）。");
            table["engine.scan.nullKeys"] = (
                "L{0} {1}: {2} binding entry(ies) with a null key.",
                "L{0} {1}: キーが Null のバインドが {2} 件あります。");
        }

        // 各階層の状態の注記（LevelBindingState の note）
        private static void AddEngineLevel(Dictionary<string, (string En, string Ja)> table)
        {
            table["engine.level.differentPlayableAsset"] = (
                "A different playable asset is assigned at this level.",
                "この階層では別の Playable Asset が割り当てられています。");
            table["engine.level.sourceUnresolved"] = (
                "Source object could not be resolved.",
                "元のオブジェクトを特定できませんでした。");
            table["engine.level.playableAssetDiffers"] = (
                "The playable asset differs from the lower level.",
                "Playable Asset が下位の階層と異なります。");
            table["engine.level.duplicateEntries"] = (
                "Duplicate binding entries for the same track.",
                "同じ Track のバインドが重複しています。");
            table["engine.level.arrayShift"] = (
                "Overridden by an array shift, but the value is the same as the lower level.",
                "配列のずれで Override になっていますが、値は下位の階層と同じです。");
            table["engine.level.entryRemovedHere"] = (
                "The binding entry was removed at this level (array size override).",
                "この階層でバインドの要素が削除されました（配列サイズの Override）。");
            table["engine.level.entryMissingNoEvidence"] = (
                "The binding entry is missing at this level, but no override evidence was found.",
                "この階層にバインドの要素がありませんが、Override の根拠は見つかりませんでした。");
            table["engine.level.valueDiffersNoEvidence"] = (
                "The value differs from the lower level, but no override evidence was found.",
                "値が下位の階層と異なりますが、Override の根拠は見つかりませんでした。");
        }

        // 履歴の読み書きのエラー（HistoryStore）
        private static void AddEngineStore(Dictionary<string, (string En, string Ja)> table)
        {
            table["engine.store.readOnly"] = (
                "History is read-only: {0}",
                "履歴は読み取り専用です: {0}");
            table["engine.store.saveFailed"] = (
                "The history could not be saved: {0}",
                "履歴を保存できませんでした: {0}");
            table["engine.store.partitionNotLoaded"] = (
                "History partition '{0}' must be loaded before it is rewritten.",
                "履歴の区画 '{0}' は、書き直す前に読み込む必要があります。");
            table["engine.store.manifestReadFailed"] = (
                "The history manifest could not be read: {0}",
                "履歴の manifest を読み込めませんでした: {0}");
            table["engine.store.manifestInvalid"] = (
                "The history manifest is empty or invalid.",
                "履歴の manifest が空か、不正な形式です。");
            table["engine.store.partitionMismatch"] = (
                "The history partition '{0}' is missing or does not match the manifest (generation {1}).",
                "履歴の区画 '{0}' が見つからないか、manifest と一致しません（世代 {1}）。");
            table["engine.store.unknownPartitionKind"] = (
                "Unknown history partition kind '{0}'.",
                "不明な履歴の区画の種類です: '{0}'");
            table["engine.store.legacyReadFailed"] = (
                "The legacy history file could not be read: {0}",
                "旧形式の履歴ファイルを読み込めませんでした: {0}");
            table["engine.store.legacyInvalid"] = (
                "The legacy history file is empty or invalid.",
                "旧形式の履歴ファイルが空か、不正な形式です。");
            table["engine.store.legacySchema"] = (
                "The legacy history file uses schema version {0}.",
                "旧形式の履歴ファイルの schema version は {0} です（未対応）。");
            table["engine.store.legacyMigrateFailed"] = (
                "The legacy history could not be migrated: {0}",
                "旧形式の履歴を移行できませんでした: {0}");
            table["engine.store.schemaMismatch"] = (
                "The history uses schema version {0}, but this version supports {1}.",
                "履歴の schema version は {0} ですが、この版が対応しているのは {1} です。");
            table["engine.store.projectMismatch"] = (
                "The history belongs to a different project.",
                "この履歴は別のプロジェクトのものです。");
        }

        // 記録のエラーと、記録しなかった理由（GuardianHistory）
        private static void AddEngineHistory(Dictionary<string, (string En, string Ja)> table)
        {
            table["engine.history.playMode"] = (
                "History is not recorded in Play Mode.",
                "Play Mode 中は履歴を記録しません。");
            table["engine.history.updateFailed"] = (
                "History could not be updated: {0}",
                "履歴を更新できませんでした: {0}");
            table["engine.skip.neverSaved"] = (
                "the scene has never been saved (no stable ID).",
                "Scene が一度も保存されていません（安定した ID がありません）。");
            table["engine.skip.unsavedChanges"] = (
                "the scene has unsaved changes (only saved states are recorded).",
                "Scene に未保存の変更があります（保存済みの状態だけを記録します）。");
            table["engine.skip.excludedFolder"] = (
                "it is in an excluded folder (see Settings).",
                "除外フォルダ内にあります（設定を参照してください）。");
        }

        // 自動監視の延期理由・検査のきっかけ・Console の Warning（ScanScheduler / BindingChangeMonitor / GuardianSettings）
        private static void AddEngineMonitor(Dictionary<string, (string En, string Ja)> table)
        {
            table["engine.defer.compiling"] = (
                "Waiting for compilation or asset import to finish.",
                "コンパイルまたは Asset のインポートの完了を待っています。");
            table["engine.defer.playMode"] = (
                "Deferred during Play Mode.",
                "Play Mode 中のため延期しています。");
            table["engine.defer.suspended"] = (
                "Monitoring is suspended.",
                "監視を一時停止しています。");

            table["engine.reason.dependsOn"] = (
                "{0} (depends on {1})",
                "{0}（{1} に依存）");
            table["engine.reason.editorStartup"] = ("Editor startup", "Editor の起動");
            table["engine.reason.sceneSaved"] = ("Scene saved", "Scene の保存");
            table["engine.reason.sceneOpened"] = ("Scene opened", "Scene を開いた");
            table["engine.reason.prefabSaved"] = ("Prefab saved", "Prefab の保存");
            table["engine.reason.assetImported"] = ("Asset imported", "Asset のインポート");
            table["engine.reason.assetMoved"] = ("Asset moved", "Asset の移動");

            table["engine.warning.autoScanFailed"] = (
                "[Timeline Binding Guardian] Automatic scan of '{0}' failed and was skipped: {1}",
                "[Timeline Binding Guardian] '{0}' の自動検査に失敗したため、スキップしました: {1}");
            table["engine.warning.historyNotSaved"] = (
                "[Timeline Binding Guardian] History was not saved: {0}",
                "[Timeline Binding Guardian] 履歴を保存しませんでした: {0}");
            table["engine.warning.settingsReadFailed"] = (
                "[Timeline Binding Guardian] Settings could not be read. Defaults are used. {0}",
                "[Timeline Binding Guardian] 設定を読み込めなかったため、既定値を使います。{0}");
            table["engine.warning.settingsSaveFailed"] = (
                "[Timeline Binding Guardian] Settings could not be saved. {0}",
                "[Timeline Binding Guardian] 設定を保存できませんでした。{0}");
        }

        // 新しい通知の Console Warning（NotificationService）
        private static void AddEngineNotify(Dictionary<string, (string En, string Ja)> table)
        {
            table["engine.notify.header"] = (
                "[Timeline Binding Guardian] {0} newly unbound Timeline binding(s) detected ({1}). ",
                "[Timeline Binding Guardian] 新たに未バインドになった Timeline のバインドを {0} 件検出しました（{1}）。");
            table["engine.notify.notAnError"] = (
                "This is not necessarily an error; review them in Window > Timeline Binding Guardian.",
                "エラーとは限りません。Window > Timeline Binding Guardian で確認してください。");
            table["engine.notify.andMore"] = (
                "  ... and {0} more.",
                "  …ほか {0} 件。");
            table["engine.notify.affects"] = (
                "affects {0} observed context(s) so far",
                "これまでに観測した {0} 件の Context に影響");
            table["engine.notify.noEffectYet"] = (
                "no effect observed yet; scenes using it are checked when scanned",
                "まだ影響は観測されていません。使っている Scene は検査時に確認されます");
        }

        // 履歴に保存する注記（BindingDiffEngine で Lang.Note として保存し、表示時に Lang.RenderNote で翻訳する）
        private static void AddEngineNotes(Dictionary<string, (string En, string Ja)> table)
        {
            table["engine.note.initial"] = (
                "Initial observation: {0} binding(s).",
                "初回の観測: {0} 件のバインド。");
            table["engine.note.timelineChanged"] = (
                "Timeline: {0} -> {1}",
                "Timeline: {0} -> {1}");
            table["engine.note.unresolved"] = (
                "The binding could not be classified. The last certain value is kept.",
                "バインドの状態を判定できませんでした。最後に確実だった値を保持しています。");
            table["engine.note.unknownPeriod"] = (
                "The state was unknown since {0}; changes during that period may not have been observed.",
                "{0} から状態が不明でした。その間の変化は観測できていない可能性があります。");
            table["engine.note.originUndetermined"] = (
                "The level that caused this change could not be determined.",
                "この変化の原因となった階層を特定できませんでした。");
        }
    }
}
