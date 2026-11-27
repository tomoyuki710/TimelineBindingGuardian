using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 新しい通知を Console に知らせる（記録 1 回につき集約した Warning を 1 回だけ）。
    /// 再スキャンでは新しい通知が作られないので再発行されない。Console の Clear やポップアップは行わない。
    /// </summary>
    public static class NotificationService
    {
        private const int MaxListed = 5;

        public static void ReportNewNotifications(RecordSummary summary, string trigger)
        {
            if (summary.NewNotifications.Count == 0 || !GuardianSettings.Instance.ConsoleWarningsEnabled)
            {
                return;
            }

            Dictionary<string, ChangeEvent> events = summary.NewEvents.ToDictionary(e => e.EventId);
            StringBuilder message = new StringBuilder();
            message.Append($"[Timeline Binding Guardian] {summary.NewNotifications.Count} newly unbound Timeline binding(s) detected ({trigger}). ");
            message.AppendLine("This is not necessarily an error; review them in Window > Timeline > Timeline Binding Guardian.");
            foreach (NotificationRecord notification in summary.NewNotifications.Take(MaxListed))
            {
                if (events.TryGetValue(notification.EventId, out ChangeEvent changeEvent))
                {
                    message.AppendLine($"  - {Describe(changeEvent)}");
                }
            }

            if (summary.NewNotifications.Count > MaxListed)
            {
                message.AppendLine($"  ... and {summary.NewNotifications.Count - MaxListed} more.");
            }

            Debug.LogWarning(message.ToString());
        }

        public static string Describe(ChangeEvent changeEvent)
        {
            string owner = Path.GetFileName(changeEvent.Owner.AssetPathHint);
            // 記録した時点で観測済みの文脈に影響が無くても、依存する Scene の検査が後に続くことがある
            string impact = changeEvent.AffectsEffective
                ? $"affects {changeEvent.AffectedContexts.Count} observed context(s) so far"
                : "no effect observed yet; scenes using it are checked when scanned";
            return $"{owner} › {changeEvent.Director.HierarchyPathHint} › {changeEvent.Track.TrackPathHint}: " +
                $"{HistoryView.FormatValue(changeEvent.Before)} → {HistoryView.FormatValue(changeEvent.After)} ({impact})";
        }
    }
}
