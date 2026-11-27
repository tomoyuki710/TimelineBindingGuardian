using System.Collections.Generic;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// Phase 1 のスキャン結果（ScanResult）を、履歴用のスナップショット（ContextSnapshot）へ変換する。
    /// </summary>
    public static class SnapshotConverter
    {
        public static List<ContextSnapshot> Convert(ScanResult scan)
        {
            List<ContextSnapshot> contexts = new List<ContextSnapshot>();
            foreach (DirectorObservation director in scan.Directors)
            {
                foreach (TrackBindingObservation track in director.Tracks)
                {
                    contexts.Add(Convert(scan, director, track));
                }
            }

            return contexts;
        }

        public static ContextSnapshot Convert(ScanResult scan, DirectorObservation director, TrackBindingObservation track)
        {
            List<LevelSnapshot> levels = new List<LevelSnapshot>();
            foreach (LevelBindingState level in track.Levels)
            {
                string key = BindingIdentity.CreateOwnerLevelKey(level.Owner.AssetGuid, level.Director.GlobalObjectId,
                    track.Track.TimelineGuid, track.Track.TrackFileId);
                levels.Add(new LevelSnapshot(key, level.Owner, level.Director, CreateValue(level), GetContribution(level)));
            }

            LevelValue effective = levels.Count > 0 ? levels[0].Value : LevelValue.None;
            bool isUncertain = track.OriginKind == OriginKind.Undetermined || effective.ValueState == ValueState.Unknown;
            return new ContextSnapshot(track.BindingKey, scan.Owner.AssetGuid, scan.Owner.AssetPathHint, director.Director, track.Track,
                effective, track.OriginLevel, isUncertain, levels);
        }

        public static LevelValue CreateValue(LevelBindingState level)
        {
            return new LevelValue(level.ValueState, level.LocalState, level.Target.GlobalObjectId, level.Target.HierarchyPathHint, level.Target.TypeName);
        }

        /// <summary>
        /// その階層自身が値を定めているか。Override された値は下位の変化に追従しないため Defined とする。
        /// </summary>
        public static ContributionKind GetContribution(LevelBindingState level)
        {
            bool isUnknownValue = level.ValueState == ValueState.Unknown || level.ValueState == ValueState.NotApplicable;
            if (isUnknownValue || level.LocalState == LocalState.Unknown)
            {
                return ContributionKind.Unknown;
            }

            bool isDefinedHere = level.LocalState == LocalState.DefinedHere || level.LocalState == LocalState.ExplicitOverride;
            if (isDefinedHere || level.ValueOverridden)
            {
                return ContributionKind.Defined;
            }

            bool isRemovedHere = level.StructuralOverride && level.ValueState == ValueState.NoEntry;
            return isRemovedHere ? ContributionKind.Defined : ContributionKind.Inherited;
        }
    }
}
