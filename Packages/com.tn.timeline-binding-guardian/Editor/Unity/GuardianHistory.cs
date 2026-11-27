using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 1 回の記録の結果。
    /// </summary>
    public sealed class RecordSummary
    {
        public bool Saved { get; set; }
        public string Error { get; set; } = string.Empty;
        public int RecordedScopeCount { get; set; }
        public int NewEventCount { get; set; }
        public int UpdatedEventCount { get; set; }
        public List<string> SkippedReasons { get; } = new List<string>();
        public List<ChangeEvent> NewEvents { get; } = new List<ChangeEvent>();
        public List<NotificationRecord> NewNotifications { get; } = new List<NotificationRecord>();
        // 記録した観測の ID（ビルド前検査の記録に使う）
        public List<string> ObservationIds { get; } = new List<string>();
        // 保存で書き出したバイト数・ファイル数（変化した区画のみ書き直すため、規模に比例しない）
        public long WrittenBytes { get; set; }
        public int WrittenFileCount { get; set; }

        public void ClearResults()
        {
            NewEvents.Clear();
            NewNotifications.Clear();
            ObservationIds.Clear();
            NewEventCount = 0;
            UpdatedEventCount = 0;
        }
    }

    /// <summary>
    /// スキャン結果 → スナップショット変換 → 差分 → 通知 → 保存をまとめる窓口。
    /// 保存済みの Scene と Prefab Asset を記録する（未保存・未保存の変更あり・Play Mode 中は記録しない）。
    /// 保存に失敗した場合はファイルから読み直し、直前に保存された状態に戻す（次の記録で同じ差分を取り直す）。
    /// </summary>
    public sealed class GuardianHistory
    {
        private static GuardianHistory _shared;
        private readonly Func<string, bool> _isExcluded;
        private readonly Func<string, bool> _isIntentional;

        /// <param name="isExcluded">記録から除外するパスの判定。null なら除外しない（テスト用の履歴など）。</param>
        /// <param name="isIntentional">意図的な NULL の共有リストに載っている Owner 階層キーか（通知しない）。null なら判定しない。</param>
        public GuardianHistory(HistoryStore store, Func<string, bool> isExcluded = null, Func<string, bool> isIntentional = null)
        {
            Store = store;
            _isExcluded = isExcluded ?? (path => false);
            _isIntentional = isIntentional ?? (key => false);
        }

        /// <summary>
        /// 履歴・通知が変わったとき（記録・Dismiss の保存後）に呼ばれる。UI や Hierarchy の表示更新に使う。
        /// </summary>
        public static event Action Changed;

        public HistoryStore Store { get; }

        /// <summary>
        /// プロジェクトの UserSettings/TimelineBindingGuardian/ を使う共有インスタンス。Domain Reload 後はファイルから読み直す。
        /// </summary>
        public static GuardianHistory Shared
        {
            get
            {
                if (_shared == null)
                {
                    HistoryStore store = new HistoryStore(DefaultDirectory, PlayerSettings.productGUID.ToString());
                    store.Load();
                    // プロジェクトの履歴は設定の除外フォルダに従う
                    _shared = new GuardianHistory(store, path => GuardianSettings.Instance.IsExcluded(path), IntentionalNullRegistry.Contains);
                }

                return _shared;
            }
        }

        public static string DefaultDirectory => Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty, "UserSettings", "TimelineBindingGuardian");

        public RecordSummary Record(IReadOnlyList<ScanResult> results, string reason)
        {
            RecordSummary summary = new RecordSummary();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                summary.Error = Lang.T("engine.history.playMode");
                return summary;
            }

            if (!Store.IsWritable)
            {
                summary.Error = Lang.T("engine.store.readOnly", Store.ErrorMessage);
                return summary;
            }

            // 履歴全体を複製すると規模に比例して重くなるため、状態を直接更新し、保存に失敗したらファイルから読み直して戻す
            HistoryChangeSet changes = new HistoryChangeSet();
            try
            {
                foreach (ScanResult result in results)
                {
                    if (TryGetSkipReason(result, out string skipReason))
                    {
                        summary.SkippedReasons.Add($"{result.Owner}: {skipReason}");
                        continue;
                    }

                    RecordScope(result, reason, summary, changes);
                }
            }
            catch (Exception exception)
            {
                // 途中まで更新された状態を捨て、直前に保存された状態へ戻す
                Store.Load();
                summary.Error = Lang.T("engine.history.updateFailed", exception.Message);
                summary.ClearResults();
                return summary;
            }

            if (summary.RecordedScopeCount == 0)
            {
                return summary;
            }

            if (!Store.TryCommit(Store.State, changes, out string error))
            {
                Store.Load();
                summary.Error = error;
                summary.ClearResults();
                return summary;
            }

            summary.Saved = true;
            summary.WrittenBytes = Store.LastCommitBytes;
            summary.WrittenFileCount = Store.LastCommitFileCount;
            Changed?.Invoke();
            return summary;
        }

        /// <summary>
        /// 前回記録したときからファイル内容（依存を含む）が変わっていないか（Phase 5）。変わっていなければ自動検査を省ける。
        /// 一度も記録していない、指紋が無い（以前の版で記録した）場合は false。
        /// </summary>
        public bool IsUnchangedSinceLastRecord(string assetPath, string fingerprint)
        {
            if (string.IsNullOrEmpty(fingerprint))
            {
                return false;
            }

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            ScopeRecord scope = Store.State.Scopes.FirstOrDefault(s => s.ScopeGuid == guid);
            return scope != null && scope.Fingerprint == fingerprint;
        }

        /// <summary>
        /// ビルド前検査の記録を保存する（manifest だけを書き直す）。
        /// </summary>
        public bool RecordBuild(BuildCheckRecord record, out string error)
        {
            error = string.Empty;
            if (!Store.IsWritable)
            {
                error = Lang.T("engine.store.readOnly", Store.ErrorMessage);
                return false;
            }

            Store.State.AddBuild(record);
            if (!Store.TryCommit(Store.State, new HistoryChangeSet(), out error))
            {
                Store.Load();
                return false;
            }

            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// 通知を消去する。履歴・現在の状態・今後のイベントには影響しない。
        /// </summary>
        public bool Dismiss(IEnumerable<string> eventIds, out string error)
        {
            error = string.Empty;
            if (!Store.IsWritable)
            {
                error = Lang.T("engine.store.readOnly", Store.ErrorMessage);
                return false;
            }

            string now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            if (NotificationRules.Dismiss(Store.State, eventIds, now) == 0)
            {
                return true;
            }

            HistoryChangeSet changes = new HistoryChangeSet { NotificationsChanged = true };
            if (!Store.TryCommit(Store.State, changes, out error))
            {
                Store.Load();
                return false;
            }

            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// 未消去の通知と、その元のイベント（新しい順）。復旧済みの判定に必要な記録も読み込む。
        /// </summary>
        public List<(NotificationRecord Notification, ChangeEvent Event)> GetNotifications(bool includeDismissed)
        {
            Dictionary<string, ChangeEvent> events = Store.State.Events.ToDictionary(e => e.EventId);
            List<(NotificationRecord, ChangeEvent)> result = new List<(NotificationRecord, ChangeEvent)>();
            foreach (NotificationRecord record in Store.State.Notifications)
            {
                bool visible = includeDismissed || record.IsUnread;
                ChangeEvent changeEvent = null;
                bool found = visible && events.TryGetValue(record.EventId, out changeEvent);
                // 共有リストで意図的とされた階層の通知は、登録前に出ていたものも表示しない
                if (found && !_isIntentional(changeEvent.OwnerLevelKey))
                {
                    result.Add((record, changeEvent));
                }
            }

            EnsureLoadedForResolution(result.Select(r => r.Item2));
            return result.OrderByDescending(r => r.Item2.ObservedAtUtc, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// 意図的な NULL の共有リストに載っている階層か（表示用）。
        /// </summary>
        public bool IsIntentional(string ownerLevelKey)
        {
            return _isIntentional(ownerLevelKey);
        }

        public Func<string, LevelValue> CreateCurrentValueLookup()
        {
            return NotificationRules.CreateCurrentValueLookup(Store.State);
        }

        internal static void RaiseChanged()
        {
            Changed?.Invoke();
        }

        private void EnsureLoadedForResolution(IEnumerable<ChangeEvent> events)
        {
            // 復旧済みの判定には、その Owner の現在の記録（変更元未確定のイベントは文脈）が必要
            foreach (ChangeEvent changeEvent in events)
            {
                if (changeEvent.OriginUndetermined)
                {
                    Store.EnsureScopeLoaded(changeEvent.Owner.AssetGuid);
                }
                else
                {
                    Store.EnsureOwnersLoaded(new[] { changeEvent.Owner.AssetGuid });
                }
            }
        }

        private void RecordScope(ScanResult result, string reason, RecordSummary summary, HistoryChangeSet changes)
        {
            List<ContextSnapshot> contexts = SnapshotConverter.Convert(result);
            IEnumerable<string> owners = contexts.SelectMany(c => c.Levels).Select(l => l.Owner.AssetGuid);
            bool loaded = Store.EnsureScopeLoaded(result.Owner.AssetGuid) && Store.EnsureOwnersLoaded(owners);
            if (!loaded)
            {
                throw new InvalidOperationException(Lang.T("engine.store.readOnly", Store.ErrorMessage));
            }

            string observationId = Guid.NewGuid().ToString("N");
            string observedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            ScopeObservation observation = new ScopeObservation(observationId, observedAtUtc, reason,
                result.Owner.AssetGuid, result.Owner.AssetPathHint, contexts, result.Owner.AssetKind, result.SourceFingerprint);
            DiffResult diff = BindingDiffEngine.Apply(Store.State, observation);
            changes.Add(diff);

            List<NotificationRecord> created = NotificationRules.CreateForNewEvents(Store.State, diff.NewEvents, observedAtUtc, _isIntentional);
            changes.NotificationsChanged |= created.Count > 0;

            summary.RecordedScopeCount++;
            summary.NewEventCount += diff.NewEvents.Count;
            summary.UpdatedEventCount += diff.UpdatedEvents.Count;
            summary.NewEvents.AddRange(diff.NewEvents);
            summary.NewNotifications.AddRange(created);
            summary.ObservationIds.Add(observationId);
        }

        private bool TryGetSkipReason(ScanResult result, out string reason)
        {
            reason = string.Empty;
            if (!result.IsSaved)
            {
                reason = Lang.T("engine.skip.neverSaved");
            }
            else if (result.HasUnsavedChanges)
            {
                reason = Lang.T("engine.skip.unsavedChanges");
            }
            else if (_isExcluded(result.Owner.AssetPathHint))
            {
                reason = Lang.T("engine.skip.excludedFolder");
            }

            return reason.Length > 0;
        }
    }
}
