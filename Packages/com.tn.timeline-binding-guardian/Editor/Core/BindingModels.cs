using System;
using System.Collections.Generic;
using UnityEngine;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// ある階層（Context）で取得されるバインド値の状態。
    /// </summary>
    public enum ValueState
    {
        Bound,
        Null,
        MissingReference,
        NoEntry,
        Unknown,
        NotApplicable,
    }

    /// <summary>
    /// その階層固有のバインド定義・Override の状態。
    /// </summary>
    public enum LocalState
    {
        DefinedHere,
        Inherited,
        ExplicitOverride,
        Unknown,
        NotApplicable,
    }

    public enum AssetKind
    {
        Unknown,
        Scene,
        Prefab,
        PrefabVariant,
        ModelPrefab,
        Timeline,
    }

    /// <summary>
    /// Director がその階層でどのような文脈に置かれているか。
    /// </summary>
    public enum ContextKind
    {
        // Scene に直接置かれた（Prefab インスタンスではない）
        SceneObject,
        // Scene 内の Prefab インスタンスの一部
        SceneInstance,
        // Prefab Asset に直接定義されている（定義元）
        PrefabDefinition,
        // Prefab Asset 内の Nested Prefab インスタンス、または Variant の一部
        PrefabInstance,
    }

    public enum OriginKind
    {
        // 変更元の階層を特定できた
        Determined,
        // 途中に Unknown があり変更元を特定できない
        Undetermined,
    }

    /// <summary>
    /// 実効値に対する各階層の役割。
    /// </summary>
    public enum LevelRole
    {
        None,
        // 実効値を決めている階層
        Origin,
        // この階層でも変化しているが、上位の変更で上書きされ実効値に影響しない
        Shadowed,
    }

    [Serializable]
    public sealed class AssetLocator
    {
        [SerializeField] private string assetGuid;
        [SerializeField] private string assetPathHint;
        [SerializeField] private AssetKind assetKind;

        public AssetLocator(string assetGuid, string assetPathHint, AssetKind assetKind)
        {
            this.assetGuid = assetGuid ?? string.Empty;
            this.assetPathHint = assetPathHint ?? string.Empty;
            this.assetKind = assetKind;
        }

        public string AssetGuid => assetGuid;
        public string AssetPathHint => assetPathHint;
        public AssetKind AssetKind => assetKind;
        public bool IsSaved => !string.IsNullOrEmpty(assetGuid);

        public override string ToString()
        {
            return string.IsNullOrEmpty(assetPathHint) ? "<unsaved>" : assetPathHint;
        }
    }

    [Serializable]
    public sealed class DirectorLocator
    {
        [SerializeField] private string globalObjectId;
        [SerializeField] private string hierarchyPathHint;
        [SerializeField] private string indexPathHint;
        [SerializeField] private int componentIndexHint;

        public DirectorLocator(string globalObjectId, string hierarchyPathHint, string indexPathHint, int componentIndexHint)
        {
            this.globalObjectId = globalObjectId ?? string.Empty;
            this.hierarchyPathHint = hierarchyPathHint ?? string.Empty;
            this.indexPathHint = indexPathHint ?? string.Empty;
            this.componentIndexHint = componentIndexHint;
        }

        public string GlobalObjectId => globalObjectId;
        // 表示用（名前のみ）。永続キーには使わない
        public string HierarchyPathHint => hierarchyPathHint;
        // 表示・デバッグ用（名前#兄弟インデックス）。永続キーには使わない
        public string IndexPathHint => indexPathHint;
        public int ComponentIndexHint => componentIndexHint;
    }

    [Serializable]
    public sealed class TrackLocator
    {
        [SerializeField] private string timelineGuid;
        [SerializeField] private string timelinePathHint;
        [SerializeField] private string trackFileId;
        [SerializeField] private string trackType;
        [SerializeField] private string trackPathHint;
        [SerializeField] private string outputTargetType;

        public TrackLocator(string timelineGuid, string timelinePathHint, string trackFileId, string trackType, string trackPathHint, string outputTargetType)
        {
            this.timelineGuid = timelineGuid ?? string.Empty;
            this.timelinePathHint = timelinePathHint ?? string.Empty;
            this.trackFileId = trackFileId ?? string.Empty;
            this.trackType = trackType ?? string.Empty;
            this.trackPathHint = trackPathHint ?? string.Empty;
            this.outputTargetType = outputTargetType ?? string.Empty;
        }

        public string TimelineGuid => timelineGuid;
        public string TimelinePathHint => timelinePathHint;
        // 符号付き 64bit の文字列表現
        public string TrackFileId => trackFileId;
        public string TrackType => trackType;
        public string TrackPathHint => trackPathHint;
        public string OutputTargetType => outputTargetType;
    }

    [Serializable]
    public sealed class TargetLocator
    {
        [SerializeField] private string globalObjectId;
        [SerializeField] private string typeName;
        [SerializeField] private string hierarchyPathHint;
        [SerializeField] private string indexPathHint;
        [SerializeField] private int componentIndexHint;
        [SerializeField] private string ownerPathHint;

        public TargetLocator(string globalObjectId, string typeName, string hierarchyPathHint, string indexPathHint, int componentIndexHint, string ownerPathHint)
        {
            this.globalObjectId = globalObjectId ?? string.Empty;
            this.typeName = typeName ?? string.Empty;
            this.hierarchyPathHint = hierarchyPathHint ?? string.Empty;
            this.indexPathHint = indexPathHint ?? string.Empty;
            this.componentIndexHint = componentIndexHint;
            this.ownerPathHint = ownerPathHint ?? string.Empty;
        }

        public static TargetLocator None => new TargetLocator(string.Empty, string.Empty, string.Empty, string.Empty, -1, string.Empty);

        public string GlobalObjectId => globalObjectId;
        public string TypeName => typeName;
        public string HierarchyPathHint => hierarchyPathHint;
        public string IndexPathHint => indexPathHint;
        public int ComponentIndexHint => componentIndexHint;
        public string OwnerPathHint => ownerPathHint;
        public bool IsNone => string.IsNullOrEmpty(globalObjectId);
    }

    /// <summary>
    /// PropertyModification のうち、該当バインドに関する証拠。
    /// </summary>
    [Serializable]
    public sealed class OverrideEvidence
    {
        [SerializeField] private string targetGlobalObjectId;
        [SerializeField] private string propertyPath;
        [SerializeField] private string value;
        [SerializeField] private string objectReferenceGlobalObjectId;

        public OverrideEvidence(string targetGlobalObjectId, string propertyPath, string value, string objectReferenceGlobalObjectId)
        {
            this.targetGlobalObjectId = targetGlobalObjectId ?? string.Empty;
            this.propertyPath = propertyPath ?? string.Empty;
            this.value = value ?? string.Empty;
            this.objectReferenceGlobalObjectId = objectReferenceGlobalObjectId ?? string.Empty;
        }

        public string TargetGlobalObjectId => targetGlobalObjectId;
        public string PropertyPath => propertyPath;
        public string Value => value;
        public string ObjectReferenceGlobalObjectId => objectReferenceGlobalObjectId;
    }

    /// <summary>
    /// 1 Track の、1 階層分の状態。Level 0 が観測している文脈（Scene など）、数字が大きいほど下位（定義元側）。
    /// </summary>
    [Serializable]
    public sealed class LevelBindingState
    {
        [SerializeField] private int level;
        [SerializeField] private AssetLocator owner;
        [SerializeField] private ContextKind contextKind;
        [SerializeField] private DirectorLocator director;
        [SerializeField] private ValueState valueState;
        [SerializeField] private LocalState localState;
        [SerializeField] private TargetLocator target;
        [SerializeField] private bool valueOverridden;
        [SerializeField] private bool structuralOverride;
        [SerializeField] private bool changedAtThisLevel;
        [SerializeField] private LevelRole role;
        [SerializeField] private List<OverrideEvidence> evidence;
        [SerializeField] private string note;

        public LevelBindingState(
            int level,
            AssetLocator owner,
            ContextKind contextKind,
            DirectorLocator director,
            ValueState valueState,
            LocalState localState,
            TargetLocator target,
            bool valueOverridden,
            bool structuralOverride,
            bool changedAtThisLevel,
            List<OverrideEvidence> evidence,
            string note)
        {
            this.level = level;
            this.owner = owner;
            this.contextKind = contextKind;
            this.director = director;
            this.valueState = valueState;
            this.localState = localState;
            this.target = target ?? TargetLocator.None;
            this.valueOverridden = valueOverridden;
            this.structuralOverride = structuralOverride;
            this.changedAtThisLevel = changedAtThisLevel;
            this.evidence = evidence ?? new List<OverrideEvidence>();
            this.note = note ?? string.Empty;
            role = LevelRole.None;
        }

        public int Level => level;
        public AssetLocator Owner => owner;
        public ContextKind ContextKind => contextKind;
        public DirectorLocator Director => director;
        public ValueState ValueState => valueState;
        public LocalState LocalState => localState;
        public TargetLocator Target => target;
        // この階層で value が Override されている（SerializedProperty.prefabOverride）
        public bool ValueOverridden => valueOverridden;
        // 配列サイズ変更や要素のずれによる構造的な Override がある
        public bool StructuralOverride => structuralOverride;
        // 1つ下の階層と比べて状態・対象が変化している
        public bool ChangedAtThisLevel => changedAtThisLevel;
        public IReadOnlyList<OverrideEvidence> Evidence => evidence;
        public string Note => note;

        public LevelRole Role
        {
            get => role;
            set => role = value;
        }
    }

    [Serializable]
    public sealed class TrackBindingObservation
    {
        [SerializeField] private string bindingKey;
        [SerializeField] private TrackLocator track;
        [SerializeField] private List<LevelBindingState> levels;
        [SerializeField] private OriginKind originKind;
        [SerializeField] private int originLevel;

        public TrackBindingObservation(string bindingKey, TrackLocator track, List<LevelBindingState> levels)
        {
            this.bindingKey = bindingKey;
            this.track = track;
            this.levels = levels ?? new List<LevelBindingState>();
            originKind = OriginKind.Undetermined;
            originLevel = -1;
        }

        public string BindingKey => bindingKey;
        public TrackLocator Track => track;
        public IReadOnlyList<LevelBindingState> Levels => levels;
        public OriginKind OriginKind => originKind;
        // 変更元の Level。未確定なら -1
        public int OriginLevel => originLevel;

        public LevelBindingState Effective => levels.Count > 0 ? levels[0] : null;

        public LevelBindingState Origin => originLevel >= 0 && originLevel < levels.Count ? levels[originLevel] : null;

        public void SetOrigin(OriginKind kind, int level)
        {
            originKind = kind;
            originLevel = level;
        }
    }

    /// <summary>
    /// Scene 内で Director が属する Prefab インスタンスのルート（外側→内側の順）。
    /// </summary>
    [Serializable]
    public sealed class InstanceNode
    {
        [SerializeField] private string globalObjectId;
        [SerializeField] private string name;
        [SerializeField] private string sourceAssetPathHint;

        public InstanceNode(string globalObjectId, string name, string sourceAssetPathHint)
        {
            this.globalObjectId = globalObjectId ?? string.Empty;
            this.name = name ?? string.Empty;
            this.sourceAssetPathHint = sourceAssetPathHint ?? string.Empty;
        }

        public string GlobalObjectId => globalObjectId;
        public string Name => name;
        public string SourceAssetPathHint => sourceAssetPathHint;
    }

    [Serializable]
    public sealed class DirectorObservation
    {
        [SerializeField] private DirectorLocator director;
        [SerializeField] private AssetLocator owner;
        [SerializeField] private AssetLocator timeline;
        [SerializeField] private bool activeInHierarchy;
        [SerializeField] private bool enabled;
        [SerializeField] private List<InstanceNode> instanceChain;
        [SerializeField] private List<TrackBindingObservation> tracks;
        [SerializeField] private List<string> diagnostics;

        public DirectorObservation(
            DirectorLocator director,
            AssetLocator owner,
            AssetLocator timeline,
            bool activeInHierarchy,
            bool enabled,
            List<InstanceNode> instanceChain,
            List<TrackBindingObservation> tracks,
            List<string> diagnostics)
        {
            this.director = director;
            this.owner = owner;
            this.timeline = timeline;
            this.activeInHierarchy = activeInHierarchy;
            this.enabled = enabled;
            this.instanceChain = instanceChain ?? new List<InstanceNode>();
            this.tracks = tracks ?? new List<TrackBindingObservation>();
            this.diagnostics = diagnostics ?? new List<string>();
        }

        public DirectorLocator Director => director;
        public AssetLocator Owner => owner;
        // TimelineAsset でない場合は null
        public AssetLocator Timeline => timeline;
        public bool ActiveInHierarchy => activeInHierarchy;
        public bool Enabled => enabled;
        public IReadOnlyList<InstanceNode> InstanceChain => instanceChain;
        public IReadOnlyList<TrackBindingObservation> Tracks => tracks;
        public IReadOnlyList<string> Diagnostics => diagnostics;
    }

    /// <summary>
    /// 1 つの Scene または Prefab Asset を起点にしたスキャン結果。
    /// </summary>
    [Serializable]
    public sealed class ScanResult
    {
        [SerializeField] private AssetLocator owner;
        [SerializeField] private string scannedAtUtc;
        [SerializeField] private List<DirectorObservation> directors;
        [SerializeField] private List<string> diagnostics;

        public ScanResult(AssetLocator owner, string scannedAtUtc, List<DirectorObservation> directors, List<string> diagnostics)
        {
            this.owner = owner;
            this.scannedAtUtc = scannedAtUtc ?? string.Empty;
            this.directors = directors ?? new List<DirectorObservation>();
            this.diagnostics = diagnostics ?? new List<string>();
        }

        public AssetLocator Owner => owner;
        // 安定 ID を持たない未保存 Scene は永続履歴の対象外（Phase 2 以降）
        public bool IsSaved => owner != null && owner.IsSaved;
        public string ScannedAtUtc => scannedAtUtc;
        public IReadOnlyList<DirectorObservation> Directors => directors;
        public IReadOnlyList<string> Diagnostics => diagnostics;
    }
}
