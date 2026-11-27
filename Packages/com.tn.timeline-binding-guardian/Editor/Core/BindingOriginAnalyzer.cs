using System.Collections.Generic;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 階層ごとの状態から、実効値の変更元（Origin）と上書き済み（Shadowed）の階層を決める。
    /// Unity API に依存しない純粋ロジック。
    ///
    /// 規則:
    /// - Level 0（観測している文脈）から下位へ順に見て、最初に変化している階層が変更元。
    /// - どの階層も変化していなければ、最下位（定義元）が変更元。
    /// - 変更元が見つかる前に Unknown の階層があれば「変更元未確定」。
    /// - 変更元より下位で変化している階層は「上書き済み（実効値に影響なし）」。
    /// </summary>
    public static class BindingOriginAnalyzer
    {
        public static void Analyze(TrackBindingObservation observation)
        {
            IReadOnlyList<LevelBindingState> levels = observation.Levels;
            foreach (LevelBindingState level in levels)
            {
                level.Role = LevelRole.None;
            }

            if (levels.Count == 0)
            {
                observation.SetOrigin(OriginKind.Undetermined, -1);
                return;
            }

            int origin = FindOrigin(levels);
            if (origin < 0)
            {
                observation.SetOrigin(OriginKind.Undetermined, -1);
                return;
            }

            observation.SetOrigin(OriginKind.Determined, origin);
            levels[origin].Role = LevelRole.Origin;
            for (int i = origin + 1; i < levels.Count; i++)
            {
                if (levels[i].ChangedAtThisLevel)
                {
                    levels[i].Role = LevelRole.Shadowed;
                }
            }
        }

        private static int FindOrigin(IReadOnlyList<LevelBindingState> levels)
        {
            int lastIndex = levels.Count - 1;
            for (int i = 0; i < levels.Count; i++)
            {
                LevelBindingState level = levels[i];
                if (IsUnknown(level))
                {
                    return -1;
                }

                bool isBase = i == lastIndex;
                if (level.ChangedAtThisLevel || isBase)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsUnknown(LevelBindingState level)
        {
            return level.ValueState == ValueState.Unknown || level.LocalState == LocalState.Unknown;
        }
    }
}
