using System;
using System.Collections.Generic;
using UnityEngine;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 仕様 7.1 の変更種別。Recovered（復旧済み）は Phase 3 の通知状態で扱う。
    /// </summary>
    public enum ChangeType
    {
        Initial,
        TargetChanged,
        BindingLost,
        BindingAssigned,
        LocalOverrideChanged,
        EntryAdded,
        EntryRemoved,
        TrackAdded,
        TrackRemoved,
        DirectorAdded,
        DirectorRemoved,
        TimelineChanged,
        Unresolved,
    }

    /// <summary>
    /// ある階層の「その階層自身が定めているもの」。
    /// 継承しているだけの階層は下位の変化で値が変わっても変化として扱わない（変更元の誤認を防ぐ）。
    /// </summary>
    public enum ContributionKind
    {
        Inherited,
        Defined,
        Unknown,
    }

    /// <summary>
    /// 比較に使う値。表示ヒント（パス・名前）は比較しない。
    /// </summary>
    [Serializable]
    public sealed class LevelValue
    {
        [SerializeField] private ValueState valueState;
        [SerializeField] private LocalState localState;
        [SerializeField] private string targetGlobalObjectId;
        [SerializeField] private string targetHint;
        [SerializeField] private string targetType;

        public LevelValue(ValueState valueState, LocalState localState, string targetGlobalObjectId, string targetHint, string targetType)
        {
            this.valueState = valueState;
            this.localState = localState;
            this.targetGlobalObjectId = targetGlobalObjectId ?? string.Empty;
            this.targetHint = targetHint ?? string.Empty;
            this.targetType = targetType ?? string.Empty;
        }

        // JsonUtility は null のクラスを既定値で保存してしまうため、「値なし」は null ではなくこれで表す
        public static LevelValue None => new LevelValue(ValueState.NotApplicable, LocalState.NotApplicable, string.Empty, string.Empty, string.Empty);

        public ValueState ValueState => valueState;
        public LocalState LocalState => localState;
        public string TargetGlobalObjectId => targetGlobalObjectId;
        public string TargetHint => targetHint;
        public string TargetType => targetType;

        public bool IsLostState => valueState == ValueState.Null || valueState == ValueState.NoEntry || valueState == ValueState.MissingReference;

        /// <summary>
        /// 値（状態と対象 ID）が同じか。LocalState・表示ヒントは含めない。
        /// </summary>
        public bool HasSameValue(LevelValue other)
        {
            return other != null && valueState == other.valueState && targetGlobalObjectId == other.targetGlobalObjectId;
        }

        public override string ToString()
        {
            return valueState == ValueState.Bound ? $"{targetHint} [{targetType}]" : valueState.ToString();
        }

        internal void Intern(StringPool pool)
        {
            targetGlobalObjectId = pool.Intern(targetGlobalObjectId);
            targetHint = pool.Intern(targetHint);
            targetType = pool.Intern(targetType);
        }
    }

    [Serializable]
    public sealed class LevelSnapshot
    {
        [SerializeField] private string ownerLevelKey;
        [SerializeField] private AssetLocator owner;
        [SerializeField] private DirectorLocator director;
        [SerializeField] private LevelValue value;
        [SerializeField] private ContributionKind contribution;

        public LevelSnapshot(string ownerLevelKey, AssetLocator owner, DirectorLocator director, LevelValue value, ContributionKind contribution)
        {
            this.ownerLevelKey = ownerLevelKey;
            this.owner = owner;
            this.director = director;
            this.value = value;
            this.contribution = contribution;
        }

        // Owner GUID | 階層 Director の GlobalObjectId | Timeline GUID | Track fileId
        public string OwnerLevelKey => ownerLevelKey;
        public AssetLocator Owner => owner;
        public DirectorLocator Director => director;
        // この階層での実効値（継承分を含む）
        public LevelValue Value => value;
        public ContributionKind Contribution => contribution;

        /// <summary>
        /// この階層自身の定義が同じか（Inherited 同士は値に関係なく同じ）。
        /// </summary>
        public bool HasSameContribution(LevelSnapshot other)
        {
            if (other == null || contribution != other.contribution)
            {
                return false;
            }

            return contribution != ContributionKind.Defined || value.HasSameValue(other.value);
        }

        internal void Intern(StringPool pool)
        {
            ownerLevelKey = pool.Intern(ownerLevelKey);
            owner.Intern(pool);
            director.Intern(pool);
            value.Intern(pool);
        }
    }

    /// <summary>
    /// 文脈（Scene 内の Director × Track）ごとの、最後に確実に観測できた状態。
    /// </summary>
    [Serializable]
    public sealed class ContextSnapshot
    {
        [SerializeField] private string bindingKey;
        [SerializeField] private string scopeGuid;
        [SerializeField] private string scopePathHint;
        [SerializeField] private DirectorLocator director;
        [SerializeField] private TrackLocator track;
        [SerializeField] private LevelValue effective;
        [SerializeField] private int originLevel;
        [SerializeField] private bool isUncertain;
        [SerializeField] private List<LevelSnapshot> levels;
        [SerializeField] private string observationId;
        [SerializeField] private string observedAtUtc;
        [SerializeField] private string unknownSinceUtc;

        public ContextSnapshot(
            string bindingKey,
            string scopeGuid,
            string scopePathHint,
            DirectorLocator director,
            TrackLocator track,
            LevelValue effective,
            int originLevel,
            bool isUncertain,
            List<LevelSnapshot> levels)
        {
            this.bindingKey = bindingKey;
            this.scopeGuid = scopeGuid;
            this.scopePathHint = scopePathHint ?? string.Empty;
            this.director = director;
            this.track = track;
            this.effective = effective;
            this.originLevel = originLevel;
            this.isUncertain = isUncertain;
            this.levels = levels ?? new List<LevelSnapshot>();
            observationId = string.Empty;
            observedAtUtc = string.Empty;
            unknownSinceUtc = string.Empty;
        }

        public string BindingKey => bindingKey;
        public string ScopeGuid => scopeGuid;
        public string ScopePathHint => scopePathHint;
        public DirectorLocator Director => director;
        public TrackLocator Track => track;
        public LevelValue Effective => effective;
        public int OriginLevel => originLevel;
        // 今回の観測に Unknown / 変更元未確定が含まれる（確実な値として扱わない）
        public bool IsUncertain => isUncertain;
        public IReadOnlyList<LevelSnapshot> Levels => levels;
        public string ObservationId => observationId;
        public string ObservedAtUtc => observedAtUtc;
        public string UnknownSinceUtc => unknownSinceUtc;

        public string DirectorGlobalObjectId => director.GlobalObjectId;
        public string TimelineGuid => track.TimelineGuid;

        public void SetObservation(string id, string atUtc)
        {
            observationId = id;
            observedAtUtc = atUtc;
        }

        public void SetUnknownSince(string atUtc)
        {
            unknownSinceUtc = atUtc ?? string.Empty;
        }

        /// <summary>
        /// 保存用の複製。各階層の Owner / Director の情報は OwnerLevelRecord に同じものがあるため省き、容量を抑える。
        /// 差分に必要なキー・値・定義の種類は残す。
        /// </summary>
        public ContextSnapshot CreateStorageCopy()
        {
            List<LevelSnapshot> compactLevels = new List<LevelSnapshot>();
            foreach (LevelSnapshot level in levels)
            {
                compactLevels.Add(new LevelSnapshot(level.OwnerLevelKey, CompactOwner(level.Owner), EmptyDirector, level.Value, level.Contribution));
            }

            ContextSnapshot copy = new ContextSnapshot(bindingKey, scopeGuid, scopePathHint, director, track, effective, originLevel, isUncertain, compactLevels);
            copy.SetObservation(observationId, observedAtUtc);
            copy.SetUnknownSince(unknownSinceUtc);
            return copy;
        }

        private static DirectorLocator EmptyDirector => new DirectorLocator(string.Empty, string.Empty, string.Empty, -1);

        private static AssetLocator CompactOwner(AssetLocator owner)
        {
            return new AssetLocator(owner.AssetGuid, string.Empty, owner.AssetKind);
        }

        internal void Intern(StringPool pool)
        {
            bindingKey = pool.Intern(bindingKey);
            scopeGuid = pool.Intern(scopeGuid);
            scopePathHint = pool.Intern(scopePathHint);
            observationId = pool.Intern(observationId);
            observedAtUtc = pool.Intern(observedAtUtc);
            director.Intern(pool);
            track.Intern(pool);
            effective.Intern(pool);
            foreach (LevelSnapshot level in levels)
            {
                level.Intern(pool);
            }
        }

        public LevelSnapshot FindLevel(string key)
        {
            foreach (LevelSnapshot level in levels)
            {
                if (level.OwnerLevelKey == key)
                {
                    return level;
                }
            }

            return null;
        }
    }

    [Serializable]
    public sealed class OwnerLevelRecord
    {
        [SerializeField] private string ownerLevelKey;
        [SerializeField] private LevelSnapshot level;
        [SerializeField] private string observedAtUtc;

        public OwnerLevelRecord(string ownerLevelKey, LevelSnapshot level, string observedAtUtc)
        {
            this.ownerLevelKey = ownerLevelKey;
            this.level = level;
            this.observedAtUtc = observedAtUtc;
        }

        public string OwnerLevelKey => ownerLevelKey;
        public LevelSnapshot Level => level;
        public string ObservedAtUtc => observedAtUtc;

        internal void Intern(StringPool pool)
        {
            ownerLevelKey = pool.Intern(ownerLevelKey);
            observedAtUtc = pool.Intern(observedAtUtc);
            level.Intern(pool);
        }
    }

    [Serializable]
    public sealed class AffectedContext
    {
        [SerializeField] private string contextBindingKey;
        [SerializeField] private string scopePathHint;
        [SerializeField] private string directorPathHint;
        [SerializeField] private string directorIndexPathHint;
        [SerializeField] private LevelValue effectiveBefore;
        [SerializeField] private LevelValue effectiveAfter;
        [SerializeField] private string note;

        public AffectedContext(string contextBindingKey, string scopePathHint, DirectorLocator director, LevelValue effectiveBefore, LevelValue effectiveAfter, string note)
        {
            this.contextBindingKey = contextBindingKey;
            this.scopePathHint = scopePathHint ?? string.Empty;
            directorPathHint = director.HierarchyPathHint;
            directorIndexPathHint = director.IndexPathHint;
            this.effectiveBefore = effectiveBefore;
            this.effectiveAfter = effectiveAfter;
            this.note = note ?? string.Empty;
        }

        public string ContextBindingKey => contextBindingKey;
        public string ScopePathHint => scopePathHint;
        public string DirectorPathHint => directorPathHint;
        // 同名の兄弟（同じ Prefab の複数インスタンス）を区別するための表示用パス（名前#兄弟インデックス）
        public string DirectorIndexPathHint => directorIndexPathHint;
        public LevelValue EffectiveBefore => effectiveBefore;
        public LevelValue EffectiveAfter => effectiveAfter;
        public string Note => note;

        internal void Intern(StringPool pool)
        {
            contextBindingKey = pool.Intern(contextBindingKey);
            scopePathHint = pool.Intern(scopePathHint);
            directorPathHint = pool.Intern(directorPathHint);
            directorIndexPathHint = pool.Intern(directorIndexPathHint);
            effectiveBefore.Intern(pool);
            effectiveAfter.Intern(pool);
        }
    }

    [Serializable]
    public sealed class ChangeEvent
    {
        [SerializeField] private string eventId;
        [SerializeField] private string observationId;
        [SerializeField] private string observedAtUtc;
        [SerializeField] private ChangeType changeType;
        [SerializeField] private string ownerLevelKey;
        [SerializeField] private AssetLocator owner;
        [SerializeField] private DirectorLocator director;
        [SerializeField] private TrackLocator track;
        [SerializeField] private LevelValue before;
        [SerializeField] private LevelValue after;
        [SerializeField] private bool affectsEffective;
        [SerializeField] private bool originUndetermined;
        [SerializeField] private List<AffectedContext> affectedContexts;
        [SerializeField] private string note;

        public ChangeEvent(
            string eventId,
            string observationId,
            string observedAtUtc,
            ChangeType changeType,
            string ownerLevelKey,
            AssetLocator owner,
            DirectorLocator director,
            TrackLocator track,
            LevelValue before,
            LevelValue after,
            bool originUndetermined,
            string note)
        {
            this.eventId = eventId;
            this.observationId = observationId;
            this.observedAtUtc = observedAtUtc;
            this.changeType = changeType;
            this.ownerLevelKey = ownerLevelKey ?? string.Empty;
            this.owner = owner;
            this.director = director;
            this.track = track;
            this.before = before;
            this.after = after;
            this.originUndetermined = originUndetermined;
            this.note = note ?? string.Empty;
            affectedContexts = new List<AffectedContext>();
        }

        // 再起動後も一意（GUID）
        public string EventId => eventId;
        public string ObservationId => observationId;
        // 観測時刻。実際の変更時刻・変更者とは限らない
        public string ObservedAtUtc => observedAtUtc;
        public ChangeType ChangeType => changeType;
        // 変更元の Owner 階層。変更元未確定・Scope 単位のイベントでは文脈キーやスコープを示す
        public string OwnerLevelKey => ownerLevelKey;
        public AssetLocator Owner => owner;
        public DirectorLocator Director => director;
        public TrackLocator Track => track;
        public LevelValue Before => before;
        public LevelValue After => after;
        // 観測した文脈の実効値に影響したか（上位で上書き済みなら false）
        public bool AffectsEffective => affectsEffective;
        public bool OriginUndetermined => originUndetermined;
        public IReadOnlyList<AffectedContext> AffectedContexts => affectedContexts;
        public string Note => note;

        public void AddAffectedContext(AffectedContext context)
        {
            affectedContexts.RemoveAll(c => c.ContextBindingKey == context.ContextBindingKey);
            affectedContexts.Add(context);
            affectsEffective = true;
        }

        internal void Intern(StringPool pool)
        {
            observationId = pool.Intern(observationId);
            observedAtUtc = pool.Intern(observedAtUtc);
            ownerLevelKey = pool.Intern(ownerLevelKey);
            owner.Intern(pool);
            director.Intern(pool);
            track.Intern(pool);
            before.Intern(pool);
            after.Intern(pool);
            foreach (AffectedContext context in affectedContexts)
            {
                context.Intern(pool);
            }
        }
    }

    [Serializable]
    public sealed class ScopeRecord
    {
        [SerializeField] private string scopeGuid;
        [SerializeField] private string scopePathHint;
        [SerializeField] private string lastObservedAtUtc;
        [SerializeField] private string lastObservationId;

        public ScopeRecord(string scopeGuid, string scopePathHint, string lastObservedAtUtc, string lastObservationId)
        {
            this.scopeGuid = scopeGuid;
            this.scopePathHint = scopePathHint ?? string.Empty;
            this.lastObservedAtUtc = lastObservedAtUtc;
            this.lastObservationId = lastObservationId;
        }

        public string ScopeGuid => scopeGuid;
        public string ScopePathHint => scopePathHint;
        public string LastObservedAtUtc => lastObservedAtUtc;
        public string LastObservationId => lastObservationId;
    }

    [Serializable]
    public sealed class ObservationRecord
    {
        [SerializeField] private string observationId;
        [SerializeField] private string observedAtUtc;
        [SerializeField] private string reason;
        [SerializeField] private string scopeGuid;
        [SerializeField] private string scopePathHint;
        [SerializeField] private int contextCount;
        [SerializeField] private int newEventCount;

        public ObservationRecord(string observationId, string observedAtUtc, string reason, string scopeGuid, string scopePathHint, int contextCount, int newEventCount)
        {
            this.observationId = observationId;
            this.observedAtUtc = observedAtUtc;
            this.reason = reason ?? string.Empty;
            this.scopeGuid = scopeGuid;
            this.scopePathHint = scopePathHint ?? string.Empty;
            this.contextCount = contextCount;
            this.newEventCount = newEventCount;
        }

        public string ObservationId => observationId;
        public string ObservedAtUtc => observedAtUtc;
        public string Reason => reason;
        public string ScopeGuid => scopeGuid;
        public string ScopePathHint => scopePathHint;
        public int ContextCount => contextCount;
        public int NewEventCount => newEventCount;
    }

    /// <summary>
    /// 履歴の全体（メモリ上）。最新の確実な観測値（contexts / ownerLevels）、変更履歴（events）、観測記録を持つ。
    /// 保存時は HistoryStore が Scene・Owner・月ごとの区画に分けて書き出す。
    /// 通知状態（Unread / Dismissed）は Phase 3 で追加する。
    /// </summary>
    [Serializable]
    public sealed class HistoryState
    {
        // 1: 単一の state.json、2: manifest と区画ファイル（gzip）
        public const int CurrentSchemaVersion = 2;

        [SerializeField] private int schemaVersion = CurrentSchemaVersion;
        [SerializeField] private string projectGuid = string.Empty;
        [SerializeField] private List<ContextSnapshot> contexts = new List<ContextSnapshot>();
        [SerializeField] private List<OwnerLevelRecord> ownerLevels = new List<OwnerLevelRecord>();
        [SerializeField] private List<ScopeRecord> scopes = new List<ScopeRecord>();
        [SerializeField] private List<ObservationRecord> observations = new List<ObservationRecord>();
        [SerializeField] private List<ChangeEvent> events = new List<ChangeEvent>();

        public HistoryState(string projectGuid)
        {
            this.projectGuid = projectGuid ?? string.Empty;
        }

        public int SchemaVersion => schemaVersion;
        public string ProjectGuid => projectGuid;
        public List<ContextSnapshot> Contexts => contexts;
        public List<OwnerLevelRecord> OwnerLevels => ownerLevels;
        public List<ScopeRecord> Scopes => scopes;
        public List<ObservationRecord> Observations => observations;
        public List<ChangeEvent> Events => events;

        /// <summary>
        /// 同じ内容の文字列を 1 インスタンスにまとめる（読み込み後に呼ぶ）。
        /// </summary>
        public void InternStrings(StringPool pool)
        {
            foreach (ContextSnapshot context in contexts)
            {
                context.Intern(pool);
            }

            foreach (OwnerLevelRecord record in ownerLevels)
            {
                record.Intern(pool);
            }

            foreach (ChangeEvent changeEvent in events)
            {
                changeEvent.Intern(pool);
            }
        }

        public HistoryState Clone()
        {
            return JsonUtility.FromJson<HistoryState>(JsonUtility.ToJson(this));
        }
    }

    /// <summary>
    /// 1 Scope（Scene）の完全スキャン 1 回分の入力。
    /// </summary>
    public sealed class ScopeObservation
    {
        public ScopeObservation(string observationId, string observedAtUtc, string reason, string scopeGuid, string scopePathHint, List<ContextSnapshot> contexts)
        {
            ObservationId = observationId;
            ObservedAtUtc = observedAtUtc;
            Reason = reason ?? string.Empty;
            ScopeGuid = scopeGuid;
            ScopePathHint = scopePathHint ?? string.Empty;
            Contexts = contexts ?? new List<ContextSnapshot>();
        }

        public string ObservationId { get; }
        public string ObservedAtUtc { get; }
        public string Reason { get; }
        public string ScopeGuid { get; }
        public string ScopePathHint { get; }
        public List<ContextSnapshot> Contexts { get; }
    }
}
