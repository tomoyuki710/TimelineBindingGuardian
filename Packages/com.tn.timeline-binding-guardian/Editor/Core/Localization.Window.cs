using System.Collections.Generic;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// メインウィンドウ（GuardianWindow）の翻訳表。
    /// </summary>
    internal static partial class LangTable
    {
        static partial void AddWindow(Dictionary<string, (string En, string Ja)> table)
        {
            // ツールバー
            table["window.scanNow"] = ("Scan Now", "今すぐ検査");
            table["window.toolbarNote"] = ("Observations are recorded to the local history. Saved states only.", "観測結果はローカルの履歴に記録します。保存済みの状態のみが対象です。");

            // タブ
            table["window.tab.notifications"] = ("Notifications", "通知");
            table["window.tab.sceneBindings"] = ("Scene Bindings", "Scene のバインド");
            table["window.tab.history"] = ("History", "履歴");
            table["window.tab.settings"] = ("Settings", "設定");

            // ヘッダー（通知と自動監視の状況）
            table["window.header.summary"] = (
                "Unread notifications: {0} (still unbound: {1})   |   Automatic monitoring: {2}, {3}, last automatic scan {4}",
                "未読の通知: {0} 件（まだ外れたまま: {1} 件）   |   自動監視: {2}、{3}、最終自動検査 {4}");
            table["window.header.monitoringOn"] = ("on", "オン");
            table["window.header.monitoringOff"] = ("OFF", "オフ");
            table["window.header.pending"] = ("{0} pending", "待機中 {0} 件");
            table["window.header.pendingWithReason"] = ("{0} pending ({1})", "待機中 {0} 件（{1}）");
            table["window.header.nonePending"] = ("none pending", "待機なし");
            table["window.header.readOnly"] = ("   |   History is read-only: {0}", "   |   履歴は読み取り専用です: {0}");

            // ステータス
            table["window.status.initial"] = (
                "Scenes and prefabs are checked automatically when saved, imported, or opened. Press \"{0}\" to inspect the open scene.",
                "Scene と Prefab は保存・インポート・オープン時に自動で検査されます。開いている Scene を検査するには「{0}」を押してください。");
            table["window.status.playMode"] = (
                "Scanning is disabled in Play Mode. Only stable Edit Mode states are inspected.",
                "Play Mode 中は検査できません。安定した Edit Mode の状態のみを検査します。");
            table["window.status.summary"] = (
                "Last scan: {0}  |  {1} scene(s), {2} director(s), {3} binding(s), {4} currently Null / No Entry{5}{6}",
                "最終検査: {0}  |  Scene {1} 件、Director {2} 件、バインド {3} 件、現在 Null / No Entry のもの {4} 件{5}{6}");
            table["window.status.unsaved"] = ("  |  {0} unsaved scene(s): not eligible for history", "  |  未保存の Scene {0} 件: 履歴の対象外です");

            // 履歴への記録結果
            table["window.record.notSaved"] = ("History not saved: {0}", "履歴を保存できませんでした: {0}");
            table["window.record.saved"] = (
                "History: {0} new event(s), {1} newly lost (saved {2} KB in {3} file(s))",
                "履歴: 新しいイベント {0} 件、新たに外れたもの {1} 件（{3} ファイル、{2} KB を保存）");
            table["window.record.notRecorded"] = ("Not recorded: {0}", "記録しませんでした: {0}");

            // ツリー
            table["window.filter.lostOnly"] = ("Null / No Entry only", "Null / No Entry のみ");
            table["window.filter.undeterminedOnly"] = ("Origin undetermined only", "由来が未確定のもののみ");
            table["window.untitledScene"] = ("Untitled (unsaved)", "Untitled（未保存）");
            table["window.trackLabel"] = ("{0}  —  {1}  (origin: {2})", "{0}  —  {1}  （由来: {2}）");
            table["window.origin.undeterminedShort"] = ("undetermined", "未確定");
            table["window.directorState.inactive"] = ("inactive", "非アクティブ");
            table["window.directorState.disabled"] = ("disabled", "無効");

            // 詳細: 共通
            table["window.details.placeholder"] = (
                "Select a track to see its binding state at each prefab level.",
                "Track を選ぶと、Prefab の各階層でのバインド状態を表示します。");
            table["window.details.nullNote"] = (
                "Note: a Null binding is not necessarily an error. Parent prefabs may intentionally clear bindings.",
                "注意: Null のバインドが必ずしもエラーとは限りません。親の Prefab で意図的にバインドを外している場合があります。");

            // 詳細: Scene
            table["window.scene.scannedAt"] = ("Scanned at (UTC): {0}", "検査日時（UTC）: {0}");
            table["window.scene.directors"] = (
                "Directors: {0} (including inactive GameObjects and disabled components)",
                "Director: {0} 件（非アクティブな GameObject と無効なコンポーネントを含みます）");

            // 詳細: Director
            table["window.director.owner"] = ("Owner: {0}", "所属: {0}");
            table["window.director.timeline"] = ("Timeline: {0}", "Timeline: {0}");
            table["window.director.activeEnabled"] = ("Active in hierarchy: {0}   Component enabled: {1}", "Hierarchy 上でアクティブ: {0}   コンポーネント有効: {1}");
            table["window.director.bindings"] = (
                "Bindings: {0}  (tracks without a binding type, such as Control and Group, are excluded)",
                "バインド: {0} 件（Control や Group など、バインドの型を持たない Track は除きます）");
            table["window.pingDirector"] = ("Ping Director", "Director を Ping");

            // 詳細: Track
            table["window.track.director"] = ("Director: {0}", "Director: {0}");
            table["window.track.timeline"] = ("Timeline: {0}", "Timeline: {0}");
            table["window.track.effective"] = ("Effective: {0}", "実効値: {0}");
            table["window.pingTarget"] = ("Ping Target", "バインド先を Ping");
            table["window.openOriginPrefab"] = ("Open Origin Prefab", "由来の Prefab を開く");
            table["window.levels.header"] = ("Levels (definition → … → current context)", "階層（定義 → … → 現在の Context）");
            table["window.levels.asset"] = ("Asset", "Asset");
            table["window.levels.context"] = ("Context", "Context");
            table["window.levels.localState"] = ("Local State", "この階層の状態");
            table["window.levels.value"] = ("Value", "値");
            table["window.levels.override"] = ("Override", "Override");
            table["window.levels.role"] = ("Role", "役割");
            table["window.open"] = ("Open", "開く");
            table["window.removeOverride"] = ("Remove Override…", "Override を解除…");
            table["window.removeOverride.tooltip"] = (
                "Remove the override at this level so the binding is inherited again (preview first).",
                "この階層の Override を解除し、バインドを継承する状態に戻します（先にプレビューを表示します）。");

            // 詳細: Override と役割
            table["window.override.arrayShift"] = ("Yes (array shift)", "あり（配列のずれ）");
            table["window.override.entryRemoved"] = ("Yes (entry removed)", "あり（要素の削除）");
            table["window.override.yes"] = ("Yes", "あり");
            table["window.role.origin"] = ("Origin of effective value", "実効値の由来");
            table["window.role.shadowed"] = ("Changed, but overridden above", "変更あり（上の階層で Override 済み）");
            table["window.role.changed"] = ("Changed", "変更あり");

            // 詳細: 由来
            table["window.origin.undetermined"] = ("Origin: undetermined (a level could not be classified).", "由来: 未確定（分類できない階層があります）。");
            table["window.origin.unsavedScene"] = ("unsaved scene", "未保存の Scene");
            table["window.origin.defined"] = ("Origin: {0} (defined at this level)", "由来: {0}（この階層で定義）");
            table["window.origin.overridden"] = ("Origin: {0} (overridden at this level)", "由来: {0}（この階層で Override）");

            // 詳細: 履歴
            table["window.history.header"] = ("History", "履歴");
            table["window.history.none"] = (
                "No changes recorded for this binding since the first observation. Bindings that were already broken before the first scan cannot be detected.",
                "最初の観測以降、このバインドの変更は記録されていません。最初の検査より前に外れていたバインドは検出できません。");
            table["window.history.previousEffective"] = ("Previous effective value: {0} → {1}", "以前の実効値: {0} → {1}");
            table["window.history.previousAt"] = ("Previous value at {0}: {1} → {2}", "{0} での以前の値: {1} → {2}");
            table["window.history.lastChange"] = ("Last change: {0} at {1} (observed {2})", "最後の変更: {1} で {0}（観測 {2}）");
            table["window.history.count"] = ("{0} recorded change(s) related to this binding.", "このバインドに関する変更の記録は {0} 件です。");
            table["window.history.contextLabel"] = ("{0} on {1}", "{1} の {0}");
            table["window.showHistory"] = ("Show History", "履歴を表示");
            table["window.history.timeNote"] = (
                "Times shown are observation times, not when the change was actually made.",
                "表示している時刻は観測した時刻で、実際に変更された時刻ではありません。");

            // Ping
            table["window.ping.noTarget"] = ("No target to ping: the effective value is {0}.", "Ping する対象がありません: 実効値が {0} です。");
            table["window.ping.noTargetInPrefab"] = (
                "No target to ping in the open prefab: the value at {0} is {1}.",
                "開いている Prefab に Ping する対象がありません: {0} での値が {1} です。");
            table["window.ping.notInPrefab.director"] = (
                "The director could not be located in the open prefab (it may have unsaved structural changes). Showing it in the scene instead.",
                "開いている Prefab 内で Director を特定できませんでした（構造の変更が未保存の可能性があります）。代わりに Scene 側で表示します。");
            table["window.ping.notInPrefab.target"] = (
                "The target could not be located in the open prefab (it may have unsaved structural changes). Showing it in the scene instead.",
                "開いている Prefab 内でバインド先を特定できませんでした（構造の変更が未保存の可能性があります）。代わりに Scene 側で表示します。");
            table["window.ping.objectNotFound"] = (
                "The object could not be found. The scene may have been closed or changed. Press \"{0}\" again.",
                "オブジェクトが見つかりません。Scene が閉じられたか、変更された可能性があります。もう一度「{0}」を押してください。");

            // ダイアログ
            table["window.dialog.returnToScene.message"] = (
                "Prefab Mode is open, so the object cannot be shown in the Hierarchy.\nReturn to the scene and ping it?",
                "Prefab Mode を開いているため、Hierarchy にオブジェクトを表示できません。\nScene に戻って Ping しますか？");
            table["window.dialog.returnToScene.ok"] = ("Return to Scene", "Scene に戻る");
            table["window.dialog.cancel"] = ("Cancel", "キャンセル");
        }
    }
}
