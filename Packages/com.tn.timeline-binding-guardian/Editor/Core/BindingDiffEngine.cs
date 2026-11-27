using System;
using System.Collections.Generic;
using System.Linq;

namespace TimelineBindingGuardian
{
    public sealed class DiffResult
    {
        public List<ChangeEvent> NewEvents { get; } = new List<ChangeEvent>();
        // 既存イベントに影響文脈を追加したもの（別 Scene で記録済みの同じ変更など）
        public List<ChangeEvent> UpdatedEvents { get; } = new List<ChangeEvent>();
    }

    /// <summary>
    /// 前回の確実な観測と今回の観測を比較し、変更イベントを作って状態を更新する。Unity API に依存しない。
    ///
    /// - 変更は「Owner 階層」（Child.prefab の Director、Middle.prefab 内の Child インスタンスの Director…）単位で 1 件にまとめ、
    ///   実効値が変わった各文脈（Scene 内の Director × Track）を影響文脈として付ける（仕様 7.2）。
    /// - 継承しているだけの階層は、下位の変化で値が変わっても変化として扱わない。
    /// - 今回 Unknown の文脈は旧値を上書きしない（仕様 7.1）。
    /// - Track / Director の削除・追加、Timeline 差し替えは BindingLost と分けて記録する。
    /// - 表示ヒント（名前・パス）だけの変化はイベントにしない。
    /// state を直接更新するため、呼び出し側は保存に失敗したときに備えて複製を渡すこと。
    /// </summary>
    public static class BindingDiffEngine
    {
        public static DiffResult Apply(HistoryState state, ScopeObservation observation)
        {
            DiffResult result = new DiffResult();
            bool isInitialScope = !state.Scopes.Any(s => s.ScopeGuid == observation.ScopeGuid);
            Dictionary<string, ChangeEvent> ownerEvents = ApplyOwnerLevels(state, observation, result);

            if (isInitialScope)
            {
                ApplyInitialScope(state, observation, result);
            }
            else
            {
                ApplyScopeDiff(state, observation, ownerEvents, result);
            }

            UpdateScopeRecord(state, observation);
            state.Observations.Add(new ObservationRecord(
                observation.ObservationId,
                observation.ObservedAtUtc,
                observation.Reason,
                observation.ScopeGuid,
                observation.ScopePathHint,
                observation.Contexts.Count,
                result.NewEvents.Count));
            return result;
        }

        public static ChangeType Classify(LevelValue before, LevelValue after)
        {
            bool beforeBound = before.ValueState == ValueState.Bound;
            bool afterBound = after.ValueState == ValueState.Bound;
            if (beforeBound && after.IsLostState)
            {
                return ChangeType.BindingLost;
            }

            if (before.IsLostState && afterBound)
            {
                return ChangeType.BindingAssigned;
            }

            if (beforeBound && afterBound)
            {
                return before.TargetGlobalObjectId != after.TargetGlobalObjectId ? ChangeType.TargetChanged : ChangeType.LocalOverrideChanged;
            }

            bool isRemoval = before.ValueState == ValueState.Null && after.ValueState == ValueState.NoEntry;
            if (isRemoval)
            {
                return ChangeType.EntryRemoved;
            }

            bool isAddition = before.ValueState == ValueState.NoEntry && after.ValueState == ValueState.Null;
            return isAddition ? ChangeType.EntryAdded : ChangeType.LocalOverrideChanged;
        }

        /// <summary>
        /// 今回観測した各 Owner 階層の「その階層自身の定義」を前回と比べ、変化していればイベントを作る。
        /// Owner 階層の記録は Scope をまたいで共有されるため、別 Scene で観測済みの変化は二重に記録しない。
        /// </summary>
        private static Dictionary<string, ChangeEvent> ApplyOwnerLevels(HistoryState state, ScopeObservation observation, DiffResult result)
        {
            Dictionary<string, ChangeEvent> ownerEvents = new Dictionary<string, ChangeEvent>();
            HashSet<string> processed = new HashSet<string>();
            Dictionary<string, int> recordIndex = BuildOwnerLevelIndex(state);

            foreach (ContextSnapshot context in observation.Contexts.Where(c => !c.IsUncertain))
            {
                foreach (LevelSnapshot level in context.Levels)
                {
                    bool isUnknown = level.Contribution == ContributionKind.Unknown;
                    if (isUnknown || !processed.Add(level.OwnerLevelKey))
                    {
                        continue;
                    }

                    OwnerLevelRecord current = new OwnerLevelRecord(level.OwnerLevelKey, level, observation.ObservedAtUtc);
                    if (!recordIndex.TryGetValue(level.OwnerLevelKey, out int index))
                    {
                        recordIndex[level.OwnerLevelKey] = state.OwnerLevels.Count;
                        state.OwnerLevels.Add(current);
                        continue;
                    }

                    LevelSnapshot previous = state.OwnerLevels[index].Level;
                    state.OwnerLevels[index] = current;
                    if (previous.HasSameContribution(level))
                    {
                        continue;
                    }

                    ChangeEvent changeEvent = CreateEvent(observation, Classify(previous.Value, level.Value), level.OwnerLevelKey,
                        level.Owner, level.Director, context.Track, previous.Value, level.Value, false, string.Empty);
                    AddNewEvent(state, result, changeEvent);
                    ownerEvents[level.OwnerLevelKey] = changeEvent;
                }
            }

            return ownerEvents;
        }

        private static void ApplyInitialScope(HistoryState state, ScopeObservation observation, DiffResult result)
        {
            foreach (ContextSnapshot context in observation.Contexts.Where(c => !c.IsUncertain))
            {
                context.SetObservation(observation.ObservationId, observation.ObservedAtUtc);
                state.Contexts.Add(context.CreateStorageCopy());
            }

            // 初回は基点として保存するだけ。初回から NULL のバインドは通知対象にしない（仕様 7.3）
            ChangeEvent initial = CreateEvent(observation, ChangeType.Initial, observation.ScopeGuid, CreateScopeLocator(observation),
                EmptyDirector(), EmptyTrack(), LevelValue.None, LevelValue.None, false,
                $"Initial observation: {observation.Contexts.Count} binding(s).");
            AddNewEvent(state, result, initial);
        }

        private static void ApplyScopeDiff(HistoryState state, ScopeObservation observation, Dictionary<string, ChangeEvent> ownerEvents, DiffResult result)
        {
            List<ContextSnapshot> previousContexts = state.Contexts.Where(c => c.ScopeGuid == observation.ScopeGuid).ToList();
            Dictionary<string, ContextSnapshot> previousByKey = previousContexts.ToDictionary(c => c.BindingKey);
            Dictionary<string, ContextSnapshot> currentByKey = new Dictionary<string, ContextSnapshot>();
            foreach (ContextSnapshot context in observation.Contexts)
            {
                currentByKey[context.BindingKey] = context;
            }

            Dictionary<string, HashSet<string>> previousTimelines = GroupTimelinesByDirector(previousContexts);
            Dictionary<string, HashSet<string>> currentTimelines = GroupTimelinesByDirector(observation.Contexts);
            HashSet<string> timelineChanged = DetectTimelineChanges(state, observation, previousTimelines, currentTimelines, previousContexts, result);

            ApplyRemovals(state, observation, previousContexts, currentByKey, currentTimelines, timelineChanged, result);
            ApplyAdditionsAndChanges(state, observation, previousByKey, previousTimelines, timelineChanged, ownerEvents, result);
        }

        private static HashSet<string> DetectTimelineChanges(
            HistoryState state,
            ScopeObservation observation,
            Dictionary<string, HashSet<string>> previousTimelines,
            Dictionary<string, HashSet<string>> currentTimelines,
            List<ContextSnapshot> previousContexts,
            DiffResult result)
        {
            HashSet<string> changed = new HashSet<string>();
            foreach (KeyValuePair<string, HashSet<string>> pair in currentTimelines)
            {
                if (!previousTimelines.TryGetValue(pair.Key, out HashSet<string> before) || before.SetEquals(pair.Value))
                {
                    continue;
                }

                changed.Add(pair.Key);
                ContextSnapshot previous = previousContexts.First(c => c.DirectorGlobalObjectId == pair.Key);
                ContextSnapshot current = observation.Contexts.First(c => c.DirectorGlobalObjectId == pair.Key);
                string note = $"Timeline: {previous.Track.TimelinePathHint} -> {current.Track.TimelinePathHint}";
                AddNewEvent(state, result, CreateEvent(observation, ChangeType.TimelineChanged, pair.Key, CreateScopeLocator(observation),
                    current.Director, current.Track, LevelValue.None, LevelValue.None, false, note));
            }

            return changed;
        }

        private static void ApplyRemovals(
            HistoryState state,
            ScopeObservation observation,
            List<ContextSnapshot> previousContexts,
            Dictionary<string, ContextSnapshot> currentByKey,
            Dictionary<string, HashSet<string>> currentTimelines,
            HashSet<string> timelineChanged,
            DiffResult result)
        {
            HashSet<string> removedDirectors = new HashSet<string>();
            foreach (ContextSnapshot previous in previousContexts)
            {
                if (currentByKey.ContainsKey(previous.BindingKey))
                {
                    continue;
                }

                state.Contexts.Remove(previous);
                string director = previous.DirectorGlobalObjectId;
                if (timelineChanged.Contains(director))
                {
                    continue;
                }

                if (currentTimelines.ContainsKey(director))
                {
                    // Director は残っている → Track が Timeline から削除された（BindingLost にしない）
                    AddNewEvent(state, result, CreateEvent(observation, ChangeType.TrackRemoved, previous.BindingKey, CreateScopeLocator(observation),
                        previous.Director, previous.Track, previous.Effective, LevelValue.None, false, string.Empty));
                }
                else if (removedDirectors.Add(director))
                {
                    AddNewEvent(state, result, CreateEvent(observation, ChangeType.DirectorRemoved, director, CreateScopeLocator(observation),
                        previous.Director, previous.Track, LevelValue.None, LevelValue.None, false, string.Empty));
                }
            }
        }

        private static void ApplyAdditionsAndChanges(
            HistoryState state,
            ScopeObservation observation,
            Dictionary<string, ContextSnapshot> previousByKey,
            Dictionary<string, HashSet<string>> previousTimelines,
            HashSet<string> timelineChanged,
            Dictionary<string, ChangeEvent> ownerEvents,
            DiffResult result)
        {
            HashSet<string> addedDirectors = new HashSet<string>();
            foreach (ContextSnapshot current in observation.Contexts)
            {
                if (previousByKey.TryGetValue(current.BindingKey, out ContextSnapshot previous))
                {
                    ApplyContextChange(state, observation, previous, current, ownerEvents, result);
                    continue;
                }

                string director = current.DirectorGlobalObjectId;
                if (!current.IsUncertain)
                {
                    current.SetObservation(observation.ObservationId, observation.ObservedAtUtc);
                    state.Contexts.Add(current.CreateStorageCopy());
                }

                if (timelineChanged.Contains(director))
                {
                    continue;
                }

                if (previousTimelines.ContainsKey(director))
                {
                    AddNewEvent(state, result, CreateEvent(observation, ChangeType.TrackAdded, current.BindingKey, CreateScopeLocator(observation),
                        current.Director, current.Track, LevelValue.None, current.Effective, false, string.Empty));
                }
                else if (addedDirectors.Add(director))
                {
                    AddNewEvent(state, result, CreateEvent(observation, ChangeType.DirectorAdded, director, CreateScopeLocator(observation),
                        current.Director, current.Track, LevelValue.None, LevelValue.None, false, string.Empty));
                }
            }
        }

        private static void ApplyContextChange(
            HistoryState state,
            ScopeObservation observation,
            ContextSnapshot previous,
            ContextSnapshot current,
            Dictionary<string, ChangeEvent> ownerEvents,
            DiffResult result)
        {
            if (current.IsUncertain)
            {
                // 旧値を保持し、不明になった最初の 1 回だけ記録する
                if (string.IsNullOrEmpty(previous.UnknownSinceUtc))
                {
                    previous.SetUnknownSince(observation.ObservedAtUtc);
                    ChangeEvent unresolved = CreateEvent(observation, ChangeType.Unresolved, current.BindingKey, CreateScopeLocator(observation),
                        current.Director, current.Track, previous.Effective, LevelValue.None, true, "The binding could not be classified. The last certain value is kept.");
                    unresolved.AddAffectedContext(CreateAffected(observation, previous, current, string.Empty));
                    AddNewEvent(state, result, unresolved);
                }

                return;
            }

            string note = string.IsNullOrEmpty(previous.UnknownSinceUtc)
                ? string.Empty
                : $"The state was unknown since {previous.UnknownSinceUtc}; changes during that period may not have been observed.";
            current.SetObservation(observation.ObservationId, observation.ObservedAtUtc);
            state.Contexts[state.Contexts.IndexOf(previous)] = current.CreateStorageCopy();

            if (previous.Effective.HasSameValue(current.Effective))
            {
                return;
            }

            AffectedContext affected = CreateAffected(observation, previous, current, note);
            LevelSnapshot source = FindSingleSourceLevel(previous, current, out LevelSnapshot sourceBefore);
            if (source == null)
            {
                ChangeEvent undetermined = CreateEvent(observation, Classify(previous.Effective, current.Effective), current.BindingKey,
                    CreateScopeLocator(observation), current.Director, current.Track, previous.Effective, current.Effective, true,
                    "The level that caused this change could not be determined.");
                undetermined.AddAffectedContext(affected);
                AddNewEvent(state, result, undetermined);
                return;
            }

            ChangeEvent ownerEvent = FindOrCreateOwnerEvent(state, observation, ownerEvents, source, sourceBefore, current, result);
            ownerEvent.AddAffectedContext(affected);
            bool isNew = result.NewEvents.Contains(ownerEvent);
            if (!isNew && !result.UpdatedEvents.Contains(ownerEvent))
            {
                result.UpdatedEvents.Add(ownerEvent);
            }
        }

        /// <summary>
        /// 実効値の変化を説明できる、定義が変わった階層を 1 つだけ返す。
        /// 1) 今回の変更元（Origin）以上で定義が変わった階層を探す（今の実効値を決めている側。より下位の変化は上書き済み）。
        /// 2) 無ければ前回の Origin 以上で探す（上位の Override が外された場合など）。
        /// 該当が複数なら null（変更元未確定）。
        /// </summary>
        private static LevelSnapshot FindSingleSourceLevel(ContextSnapshot previous, ContextSnapshot current, out LevelSnapshot sourceBefore)
        {
            List<int> changed = new List<int>();
            for (int i = 0; i < current.Levels.Count; i++)
            {
                LevelSnapshot before = previous.FindLevel(current.Levels[i].OwnerLevelKey);
                if (before == null || !before.HasSameContribution(current.Levels[i]))
                {
                    changed.Add(i);
                }
            }

            int currentOrigin = current.OriginLevel >= 0 ? current.OriginLevel : current.Levels.Count - 1;
            List<int> candidates = changed.Where(i => i <= currentOrigin).ToList();
            if (candidates.Count == 0)
            {
                int previousOrigin = MapPreviousOrigin(previous, current);
                candidates = changed.Where(i => i <= previousOrigin).ToList();
            }

            sourceBefore = null;
            if (candidates.Count != 1)
            {
                return null;
            }

            LevelSnapshot source = current.Levels[candidates[0]];
            sourceBefore = previous.FindLevel(source.OwnerLevelKey);
            return source;
        }

        private static int MapPreviousOrigin(ContextSnapshot previous, ContextSnapshot current)
        {
            bool hasOrigin = previous.OriginLevel >= 0 && previous.OriginLevel < previous.Levels.Count;
            if (!hasOrigin)
            {
                return current.Levels.Count - 1;
            }

            string key = previous.Levels[previous.OriginLevel].OwnerLevelKey;
            for (int i = 0; i < current.Levels.Count; i++)
            {
                if (current.Levels[i].OwnerLevelKey == key)
                {
                    return i;
                }
            }

            return current.Levels.Count - 1;
        }

        private static ChangeEvent FindOrCreateOwnerEvent(
            HistoryState state,
            ScopeObservation observation,
            Dictionary<string, ChangeEvent> ownerEvents,
            LevelSnapshot source,
            LevelSnapshot sourceBefore,
            ContextSnapshot current,
            DiffResult result)
        {
            if (ownerEvents.TryGetValue(source.OwnerLevelKey, out ChangeEvent created))
            {
                return created;
            }

            // 別 Scope の観測で記録済みの同じ変化（同じ Owner 階層・同じ変化後の値）があれば、それに影響文脈を追加する
            ChangeEvent existing = state.Events.LastOrDefault(e => e.OwnerLevelKey == source.OwnerLevelKey && e.After.HasSameValue(source.Value));
            if (existing != null)
            {
                return existing;
            }

            LevelValue before = sourceBefore != null ? sourceBefore.Value : LevelValue.None;
            ChangeEvent changeEvent = CreateEvent(observation, Classify(before, source.Value), source.OwnerLevelKey, source.Owner,
                source.Director, current.Track, before, source.Value, false, string.Empty);
            AddNewEvent(state, result, changeEvent);
            ownerEvents[source.OwnerLevelKey] = changeEvent;
            return changeEvent;
        }

        private static AffectedContext CreateAffected(ScopeObservation observation, ContextSnapshot previous, ContextSnapshot current, string note)
        {
            LevelValue after = current.IsUncertain ? LevelValue.None : current.Effective;
            return new AffectedContext(current.BindingKey, observation.ScopePathHint, current.Director, previous.Effective, after, note);
        }

        private static Dictionary<string, HashSet<string>> GroupTimelinesByDirector(IEnumerable<ContextSnapshot> contexts)
        {
            Dictionary<string, HashSet<string>> result = new Dictionary<string, HashSet<string>>();
            foreach (ContextSnapshot context in contexts)
            {
                if (!result.TryGetValue(context.DirectorGlobalObjectId, out HashSet<string> timelines))
                {
                    timelines = new HashSet<string>();
                    result.Add(context.DirectorGlobalObjectId, timelines);
                }

                timelines.Add(context.TimelineGuid);
            }

            return result;
        }

        private static Dictionary<string, int> BuildOwnerLevelIndex(HistoryState state)
        {
            Dictionary<string, int> index = new Dictionary<string, int>();
            for (int i = 0; i < state.OwnerLevels.Count; i++)
            {
                index[state.OwnerLevels[i].OwnerLevelKey] = i;
            }

            return index;
        }

        private static void UpdateScopeRecord(HistoryState state, ScopeObservation observation)
        {
            state.Scopes.RemoveAll(s => s.ScopeGuid == observation.ScopeGuid);
            state.Scopes.Add(new ScopeRecord(observation.ScopeGuid, observation.ScopePathHint, observation.ObservedAtUtc, observation.ObservationId));
        }

        private static void AddNewEvent(HistoryState state, DiffResult result, ChangeEvent changeEvent)
        {
            state.Events.Add(changeEvent);
            result.NewEvents.Add(changeEvent);
        }

        private static ChangeEvent CreateEvent(
            ScopeObservation observation,
            ChangeType type,
            string ownerLevelKey,
            AssetLocator owner,
            DirectorLocator director,
            TrackLocator track,
            LevelValue before,
            LevelValue after,
            bool originUndetermined,
            string note)
        {
            return new ChangeEvent(Guid.NewGuid().ToString("N"), observation.ObservationId, observation.ObservedAtUtc, type,
                ownerLevelKey, owner, director, track, before, after, originUndetermined, note);
        }

        private static AssetLocator CreateScopeLocator(ScopeObservation observation)
        {
            return new AssetLocator(observation.ScopeGuid, observation.ScopePathHint, AssetKind.Scene);
        }

        private static DirectorLocator EmptyDirector()
        {
            return new DirectorLocator(string.Empty, string.Empty, string.Empty, -1);
        }

        private static TrackLocator EmptyTrack()
        {
            return new TrackLocator(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        }
    }
}
