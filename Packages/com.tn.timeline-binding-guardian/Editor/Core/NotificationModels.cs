using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TimelineBindingGuardian
{
    public enum NotificationStatus
    {
        Unread,
        Dismissed,
    }

    /// <summary>
    /// 通知の状態（履歴・現在の状態とは別に保存する。仕様 8 章）。Dismiss はこの eventId だけに作用する。
    /// 「復旧済み（Resolved）」は保存せず、現在の値から都度判定する。
    /// </summary>
    [Serializable]
    public sealed class NotificationRecord
    {
        [SerializeField] private string eventId;
        [SerializeField] private NotificationStatus status;
        [SerializeField] private string createdAtUtc;
        [SerializeField] private string dismissedAtUtc;

        public NotificationRecord(string eventId, string createdAtUtc)
        {
            this.eventId = eventId;
            this.createdAtUtc = createdAtUtc ?? string.Empty;
            status = NotificationStatus.Unread;
            dismissedAtUtc = string.Empty;
        }

        public string EventId => eventId;
        public NotificationStatus Status => status;
        public string CreatedAtUtc => createdAtUtc;
        public string DismissedAtUtc => dismissedAtUtc;
        public bool IsUnread => status == NotificationStatus.Unread;

        public void Dismiss(string atUtc)
        {
            status = NotificationStatus.Dismissed;
            dismissedAtUtc = atUtc ?? string.Empty;
        }
    }

    /// <summary>
    /// 通知の判定（仕様 7.3）。Unity API に依存しない。
    /// - 通知するのは BindingLost（その階層の直前の確実な値が Bound で、存続している Track が Null / No Entry / Missing になった）だけ。
    /// - 初回からの NULL、NULL の継続、NULL→Bound、Bound A→B、Unknown、削除系は通知しない（履歴のみ）。
    /// - 意図的な NULL でも通知する（正常・異常の判定や承認は求めない）。
    /// - 同じイベントは二度通知しない。Bound に戻った後に再び NULL になれば、差分エンジンが新しい eventId を作るので新しく通知される。
    /// </summary>
    public static class NotificationRules
    {
        public static bool IsNotifiable(ChangeEvent changeEvent)
        {
            return changeEvent.ChangeType == ChangeType.BindingLost;
        }

        /// <summary>
        /// 新しいイベントのうち通知対象のものに未読の通知を作る。作った通知を返す。
        /// </summary>
        public static List<NotificationRecord> CreateForNewEvents(HistoryState state, IEnumerable<ChangeEvent> newEvents, string atUtc)
        {
            HashSet<string> existing = new HashSet<string>(state.Notifications.Select(n => n.EventId));
            List<NotificationRecord> created = new List<NotificationRecord>();
            foreach (ChangeEvent changeEvent in newEvents)
            {
                if (!IsNotifiable(changeEvent) || !existing.Add(changeEvent.EventId))
                {
                    continue;
                }

                NotificationRecord record = new NotificationRecord(changeEvent.EventId, atUtc);
                state.Notifications.Add(record);
                created.Add(record);
            }

            return created;
        }

        /// <summary>
        /// 指定した通知を消去する。履歴・現在の状態・今後のイベントには影響しない。消去した件数を返す。
        /// </summary>
        public static int Dismiss(HistoryState state, IEnumerable<string> eventIds, string atUtc)
        {
            HashSet<string> targets = new HashSet<string>(eventIds);
            int count = 0;
            foreach (NotificationRecord record in state.Notifications)
            {
                if (record.IsUnread && targets.Contains(record.EventId))
                {
                    record.Dismiss(atUtc);
                    count++;
                }
            }

            return count;
        }

        public static List<NotificationRecord> GetUnread(HistoryState state)
        {
            return state.Notifications.Where(n => n.IsUnread).ToList();
        }

        /// <summary>
        /// 通知元の変化が現在は解消しているか（その階層の現在の記録値が Bound）。
        /// currentValue は「Owner 階層キー → 現在の記録値」（読み込まれていなければ null）。判定できなければ false。
        /// </summary>
        public static bool IsResolved(ChangeEvent changeEvent, Func<string, LevelValue> currentValue)
        {
            LevelValue value = currentValue(changeEvent.OwnerLevelKey);
            return value != null && value.ValueState == ValueState.Bound;
        }

        /// <summary>
        /// 現在の記録値を引く関数を作る。Owner 階層の記録、なければ文脈（変更元未確定のイベントは文脈キーを持つ）から引く。
        /// </summary>
        public static Func<string, LevelValue> CreateCurrentValueLookup(HistoryState state)
        {
            Dictionary<string, LevelValue> values = new Dictionary<string, LevelValue>();
            foreach (ContextSnapshot context in state.Contexts)
            {
                values[context.BindingKey] = context.Effective;
            }

            foreach (OwnerLevelRecord record in state.OwnerLevels)
            {
                values[record.OwnerLevelKey] = record.Level.Value;
            }

            return key => values.TryGetValue(key, out LevelValue value) ? value : null;
        }
    }
}
