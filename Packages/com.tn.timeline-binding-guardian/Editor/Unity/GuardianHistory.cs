using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
        // 保存で書き出したバイト数・ファイル数（変化した区画のみ書き直すため、規模に比例しない）
        public long WrittenBytes { get; set; }
        public int WrittenFileCount { get; set; }
    }

    /// <summary>
    /// スキャン結果 → スナップショット変換 → 差分 → 保存をまとめる窓口。
    /// 保存済みの Scene だけを記録する（未保存・未保存の変更あり・Play Mode 中は記録しない）。
    /// 保存に失敗した場合は状態を変えない（次の記録で同じ差分を取り直す）。
    /// </summary>
    public sealed class GuardianHistory
    {
        private static GuardianHistory _shared;

        public GuardianHistory(HistoryStore store)
        {
            Store = store;
        }

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
                    _shared = new GuardianHistory(store);
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
                summary.Error = "History is not recorded in Play Mode.";
                return summary;
            }

            if (!Store.IsWritable)
            {
                summary.Error = $"History is read-only: {Store.ErrorMessage}";
                return summary;
            }

            // 履歴全体を複製すると規模に比例して重くなるため、状態を直接更新し、保存に失敗したらファイルから読み直して戻す
            HistoryState state = Store.State;
            HistoryChangeSet changes = new HistoryChangeSet();
            try
            {
                foreach (ScanResult result in results)
                {
                    if (!TryGetSkipReason(result, out string skipReason))
                    {
                        RecordScope(state, result, reason, summary, changes);
                        continue;
                    }

                    summary.SkippedReasons.Add($"{result.Owner}: {skipReason}");
                }
            }
            catch (Exception exception)
            {
                // 途中まで更新された状態を捨て、直前に保存された状態へ戻す
                Store.Load();
                summary.Error = $"History could not be updated: {exception.Message}";
                summary.NewEvents.Clear();
                summary.NewEventCount = 0;
                summary.UpdatedEventCount = 0;
                return summary;
            }

            if (summary.RecordedScopeCount == 0)
            {
                return summary;
            }

            if (!Store.TryCommit(state, changes, out string error))
            {
                Store.Load();
                summary.Error = error;
                summary.NewEvents.Clear();
                summary.NewEventCount = 0;
                summary.UpdatedEventCount = 0;
                return summary;
            }

            summary.Saved = true;
            summary.WrittenBytes = Store.LastCommitBytes;
            summary.WrittenFileCount = Store.LastCommitFileCount;
            return summary;
        }

        private static void RecordScope(HistoryState state, ScanResult result, string reason, RecordSummary summary, HistoryChangeSet changes)
        {
            string observationId = Guid.NewGuid().ToString("N");
            string observedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            ScopeObservation observation = new ScopeObservation(observationId, observedAtUtc, reason,
                result.Owner.AssetGuid, result.Owner.AssetPathHint, SnapshotConverter.Convert(result));
            DiffResult diff = BindingDiffEngine.Apply(state, observation);
            changes.Add(diff);
            summary.RecordedScopeCount++;
            summary.NewEventCount += diff.NewEvents.Count;
            summary.UpdatedEventCount += diff.UpdatedEvents.Count;
            summary.NewEvents.AddRange(diff.NewEvents);
        }

        private static bool TryGetSkipReason(ScanResult result, out string reason)
        {
            reason = string.Empty;
            if (!result.IsSaved)
            {
                reason = "the scene has never been saved (no stable ID).";
            }
            else if (result.HasUnsavedChanges)
            {
                reason = "the scene has unsaved changes (only saved states are recorded).";
            }
            else if (result.Owner.AssetKind != AssetKind.Scene)
            {
                reason = "only scenes are recorded.";
            }

            return reason.Length > 0;
        }
    }
}
