using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TimelineBindingGuardian
{
    public enum HistoryStoreStatus
    {
        // 読み込み済み（ファイルが無かった場合は空の履歴で開始）
        Ready,
        // 読み込めない・壊れている。上書きせず記録を停止する
        Corrupted,
        // 未対応の schemaVersion（新しい版で書かれた等）。上書きせず記録を停止する
        UnsupportedSchema,
        // 別プロジェクトの履歴ファイル。上書きせず記録を停止する
        ProjectMismatch,
    }

    /// <summary>
    /// 保存時に書き直す区画。差分エンジンの結果から作る。
    /// </summary>
    public sealed class HistoryChangeSet
    {
        public bool WriteAll { get; set; }
        public HashSet<string> ScopeGuids { get; } = new HashSet<string>();
        public HashSet<string> OwnerGuids { get; } = new HashSet<string>();
        public HashSet<string> EventIds { get; } = new HashSet<string>();
        public HashSet<string> ObservationIds { get; } = new HashSet<string>();

        public static HistoryChangeSet All => new HistoryChangeSet { WriteAll = true };

        public void Add(DiffResult result)
        {
            ScopeGuids.UnionWith(result.ChangedScopeGuids);
            OwnerGuids.UnionWith(result.ChangedOwnerGuids);
            EventIds.UnionWith(result.ChangedEventIds);
            ObservationIds.UnionWith(result.ChangedObservationIds);
        }
    }

    /// <summary>
    /// 履歴の読み書き（保存形式 v2）。
    ///   manifest.json                 平文の目次（世代番号、各区画の有効な世代、Scope 一覧）
    ///   contexts/&lt;SceneGUID&gt;.json.gz  その Scene の文脈
    ///   owners/&lt;OwnerGUID&gt;.json.gz    Owner（Prefab / Scene）ごとの階層の記録
    ///   events/&lt;yyyy-MM&gt;.json.gz      その月のイベントと観測記録
    /// - 書き直すのは変化した区画だけ。区画 → manifest の順に一時ファイル経由で置換し、manifest の置換をコミット点とする。
    /// - 各区画・manifest は前世代を .bak に残す。読み込み時に区画の世代が manifest と食い違えば .bak を使い、次の書き込み前に修復する。
    /// - 読めない・壊れている・別プロジェクト・未対応バージョンのときは上書きせず、読み取り専用のエラー状態にする。
    /// - ファイルの削除は行わない（置換と上書きのみ）。
    /// - 保存に失敗したら、呼び出し側は Load() で直前に保存された状態へ戻すこと。
    /// </summary>
    public sealed class HistoryStore
    {
        public const string ManifestFileName = "manifest.json";
        public const string LegacyFileName = "state.json";
        public const string MigratedLegacyFileName = "state.v1.json";

        private const string ContextsKind = "contexts";
        private const string OwnersKind = "owners";
        private const string EventsKind = "events";
        private const string PartitionExtension = ".json.gz";
        private const string BackupExtension = ".bak";
        private const int LegacySchemaVersion = 1;

        private readonly string _projectGuid;
        private readonly Dictionary<string, long> _partitionGenerations = new Dictionary<string, long>();
        private readonly HashSet<string> _needsRepair = new HashSet<string>();
        private long _generation;

        public HistoryStore(string directory, string projectGuid)
        {
            Directory = directory;
            _projectGuid = projectGuid ?? string.Empty;
            State = new HistoryState(_projectGuid);
        }

        public string Directory { get; }
        public string ManifestPath => Path.Combine(Directory, ManifestFileName);
        public HistoryStoreStatus Status { get; private set; } = HistoryStoreStatus.Ready;
        public string ErrorMessage { get; private set; } = string.Empty;
        public HistoryState State { get; private set; }
        public bool IsWritable => Status == HistoryStoreStatus.Ready;
        public long Generation => _generation;
        // 直近の保存で書き出したバイト数とファイル数（負荷確認用）
        public long LastCommitBytes { get; private set; }
        public int LastCommitFileCount { get; private set; }
        // 直近の読み込みで .bak から復旧した区画の数
        public int RecoveredPartitionCount => _needsRepair.Count;

        public void Load()
        {
            ErrorMessage = string.Empty;
            Status = HistoryStoreStatus.Ready;
            _partitionGenerations.Clear();
            _needsRepair.Clear();
            _generation = 0;
            State = new HistoryState(_projectGuid);

            if (File.Exists(ManifestPath))
            {
                LoadManifestAndPartitions();
                return;
            }

            string legacyPath = Path.Combine(Directory, LegacyFileName);
            if (File.Exists(legacyPath))
            {
                MigrateLegacy(legacyPath);
            }
        }

        /// <summary>
        /// state の変化した区画を保存する。成功したら state を現在の状態として採用する。
        /// 失敗した場合はファイル上の有効な状態は変わらない（呼び出し側は Load() で戻す）。
        /// </summary>
        public bool TryCommit(HistoryState state, HistoryChangeSet changes, out string error)
        {
            error = string.Empty;
            if (!IsWritable)
            {
                error = $"History is read-only: {ErrorMessage}";
                return false;
            }

            long next = _generation + 1;
            Dictionary<string, long> generations = new Dictionary<string, long>(_partitionGenerations);
            long bytes = 0;
            int files = 0;
            try
            {
                RepairPartitions();
                foreach (KeyValuePair<string, string> partition in WritePartitions(state, changes, next))
                {
                    generations[partition.Key] = next;
                    bytes += new FileInfo(partition.Value).Length;
                    files++;
                }

                bytes += WriteManifest(state, generations, next);
                files++;
            }
            catch (Exception exception)
            {
                error = $"The history could not be saved: {exception.Message}";
                return false;
            }

            _generation = next;
            _partitionGenerations.Clear();
            foreach (KeyValuePair<string, long> pair in generations)
            {
                _partitionGenerations[pair.Key] = pair.Value;
            }

            State = state;
            LastCommitBytes = bytes;
            LastCommitFileCount = files;
            return true;
        }

        /// <summary>
        /// 現在の履歴（manifest と区画ファイル、それぞれの .bak）のディスク上の合計サイズ。移行で残した v1 の旧ファイルは含めない。
        /// </summary>
        public long GetTotalSizeBytes()
        {
            if (!System.IO.Directory.Exists(Directory))
            {
                return 0;
            }

            long total = System.IO.Directory.GetFiles(Directory, ManifestFileName + "*").Sum(f => new FileInfo(f).Length);
            foreach (string kind in new[] { ContextsKind, OwnersKind, EventsKind })
            {
                string folder = Path.Combine(Directory, kind);
                if (System.IO.Directory.Exists(folder))
                {
                    total += System.IO.Directory.GetFiles(folder).Sum(f => new FileInfo(f).Length);
                }
            }

            return total;
        }

        private void LoadManifestAndPartitions()
        {
            Manifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath, Encoding.UTF8));
            }
            catch (Exception exception)
            {
                SetError(HistoryStoreStatus.Corrupted, $"The history manifest could not be read: {exception.Message}");
                return;
            }

            if (manifest == null || manifest.Partitions == null)
            {
                SetError(HistoryStoreStatus.Corrupted, "The history manifest is empty or invalid.");
                return;
            }

            if (!CheckHeader(manifest.SchemaVersion, manifest.ProjectGuid))
            {
                return;
            }

            HistoryState state = new HistoryState(_projectGuid);
            state.Scopes.AddRange(manifest.Scopes ?? new List<ScopeRecord>());
            foreach (PartitionEntry entry in manifest.Partitions.OrderBy(e => e.Kind, StringComparer.Ordinal).ThenBy(e => e.Id, StringComparer.Ordinal))
            {
                if (!TryLoadPartition(state, entry))
                {
                    return;
                }

                _partitionGenerations[GetPartitionKey(entry.Kind, entry.Id)] = entry.Generation;
            }

            state.InternStrings(new StringPool());
            State = state;
            _generation = manifest.Generation;
        }

        private bool TryLoadPartition(HistoryState state, PartitionEntry entry)
        {
            string path = GetPartitionPath(entry.Kind, entry.Id);
            string json = ReadPartitionJson(path, entry.Generation, out bool fromBackup);
            if (json == null)
            {
                SetError(HistoryStoreStatus.Corrupted, $"The history partition '{entry.Kind}/{entry.Id}' is missing or does not match the manifest (generation {entry.Generation}).");
                return false;
            }

            if (fromBackup)
            {
                // 書き込み途中で中断した区画。次の書き込み前に .bak から本体へ戻す
                _needsRepair.Add(GetPartitionKey(entry.Kind, entry.Id));
            }

            switch (entry.Kind)
            {
                case ContextsKind:
                    state.Contexts.AddRange(JsonUtility.FromJson<ContextsPartition>(json).Contexts);
                    return true;
                case OwnersKind:
                    state.OwnerLevels.AddRange(JsonUtility.FromJson<OwnersPartition>(json).Records);
                    return true;
                case EventsKind:
                    EventsPartition events = JsonUtility.FromJson<EventsPartition>(json);
                    state.Events.AddRange(events.Events);
                    state.Observations.AddRange(events.Observations);
                    return true;
                default:
                    SetError(HistoryStoreStatus.UnsupportedSchema, $"Unknown history partition kind '{entry.Kind}'.");
                    return false;
            }
        }

        /// <summary>
        /// 区画の本体、なければ .bak のうち、世代が expected と一致する方の JSON を返す。どちらも合わなければ null。
        /// </summary>
        private static string ReadPartitionJson(string path, long expected, out bool fromBackup)
        {
            fromBackup = false;
            string json = TryReadGzipJson(path);
            if (json != null && ReadGeneration(json) == expected)
            {
                return json;
            }

            string backup = TryReadGzipJson(path + BackupExtension);
            if (backup != null && ReadGeneration(backup) == expected)
            {
                fromBackup = true;
                return backup;
            }

            return null;
        }

        private void MigrateLegacy(string legacyPath)
        {
            HistoryState legacy;
            try
            {
                legacy = JsonUtility.FromJson<HistoryState>(File.ReadAllText(legacyPath, Encoding.UTF8));
            }
            catch (Exception exception)
            {
                SetError(HistoryStoreStatus.Corrupted, $"The legacy history file could not be read: {exception.Message}");
                return;
            }

            if (legacy == null)
            {
                SetError(HistoryStoreStatus.Corrupted, "The legacy history file is empty or invalid.");
                return;
            }

            if (legacy.SchemaVersion != LegacySchemaVersion)
            {
                SetError(HistoryStoreStatus.UnsupportedSchema, $"The legacy history file uses schema version {legacy.SchemaVersion}.");
                return;
            }

            if (!CheckProject(legacy.ProjectGuid))
            {
                return;
            }

            legacy.InternStrings(new StringPool());
            if (!TryCommit(legacy, HistoryChangeSet.All, out string error))
            {
                SetError(HistoryStoreStatus.Corrupted, $"The legacy history could not be migrated: {error}");
                return;
            }

            // 移行元は削除せず、名前を変えて残す
            File.Move(legacyPath, GetAvailablePath(Path.Combine(Directory, MigratedLegacyFileName)));
        }

        private IEnumerable<KeyValuePair<string, string>> WritePartitions(HistoryState state, HistoryChangeSet changes, long generation)
        {
            List<KeyValuePair<string, string>> written = new List<KeyValuePair<string, string>>();

            Dictionary<string, List<ContextSnapshot>> contextsByScope = GroupBy(state.Contexts, c => c.ScopeGuid, changes.WriteAll ? null : changes.ScopeGuids);
            foreach (string scope in changes.WriteAll ? contextsByScope.Keys.ToList() : changes.ScopeGuids.ToList())
            {
                contextsByScope.TryGetValue(scope, out List<ContextSnapshot> contexts);
                ContextsPartition partition = new ContextsPartition(generation, scope, contexts ?? new List<ContextSnapshot>());
                written.Add(WritePartition(ContextsKind, scope, JsonUtility.ToJson(partition)));
            }

            Dictionary<string, List<OwnerLevelRecord>> recordsByOwner = GroupBy(state.OwnerLevels, r => r.Level.Owner.AssetGuid, changes.WriteAll ? null : changes.OwnerGuids);
            foreach (string owner in changes.WriteAll ? recordsByOwner.Keys.ToList() : changes.OwnerGuids.ToList())
            {
                recordsByOwner.TryGetValue(owner, out List<OwnerLevelRecord> records);
                OwnersPartition partition = new OwnersPartition(generation, owner, records ?? new List<OwnerLevelRecord>());
                written.Add(WritePartition(OwnersKind, owner, JsonUtility.ToJson(partition)));
            }

            foreach (string month in GetChangedMonths(state, changes))
            {
                List<ChangeEvent> events = state.Events.Where(e => GetMonth(e.ObservedAtUtc) == month).ToList();
                List<ObservationRecord> observations = state.Observations.Where(o => GetMonth(o.ObservedAtUtc) == month).ToList();
                EventsPartition partition = new EventsPartition(generation, month, events, observations);
                written.Add(WritePartition(EventsKind, month, JsonUtility.ToJson(partition)));
            }

            return written;
        }

        private static HashSet<string> GetChangedMonths(HistoryState state, HistoryChangeSet changes)
        {
            HashSet<string> months = new HashSet<string>();
            foreach (ChangeEvent changeEvent in state.Events)
            {
                if (changes.WriteAll || changes.EventIds.Contains(changeEvent.EventId))
                {
                    months.Add(GetMonth(changeEvent.ObservedAtUtc));
                }
            }

            foreach (ObservationRecord observation in state.Observations)
            {
                if (changes.WriteAll || changes.ObservationIds.Contains(observation.ObservationId))
                {
                    months.Add(GetMonth(observation.ObservedAtUtc));
                }
            }

            return months;
        }

        private KeyValuePair<string, string> WritePartition(string kind, string id, string json)
        {
            string path = GetPartitionPath(kind, id);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Directory);
            string tempPath = path + ".tmp";
            using (FileStream file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (GZipStream gzip = new GZipStream(file, System.IO.Compression.CompressionLevel.Optimal, true))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                    gzip.Write(bytes, 0, bytes.Length);
                }

                file.Flush(true);
            }

            ReplaceFile(tempPath, path);
            return new KeyValuePair<string, string>(GetPartitionKey(kind, id), path);
        }

        private long WriteManifest(HistoryState state, Dictionary<string, long> generations, long generation)
        {
            List<PartitionEntry> entries = generations
                .Select(pair => SplitPartitionKey(pair.Key, pair.Value))
                .OrderBy(e => e.Kind, StringComparer.Ordinal)
                .ThenBy(e => e.Id, StringComparer.Ordinal)
                .ToList();
            Manifest manifest = new Manifest(HistoryState.CurrentSchemaVersion, _projectGuid, generation, state.Scopes, entries);
            System.IO.Directory.CreateDirectory(Directory);
            string tempPath = ManifestPath + ".tmp";
            byte[] bytes = new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(manifest, true));
            using (FileStream file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes, 0, bytes.Length);
                file.Flush(true);
            }

            ReplaceFile(tempPath, ManifestPath);
            return bytes.Length;
        }

        private void RepairPartitions()
        {
            foreach (string key in _needsRepair)
            {
                PartitionEntry entry = SplitPartitionKey(key, 0);
                string path = GetPartitionPath(entry.Kind, entry.Id);
                File.Copy(path + BackupExtension, path, true);
            }

            _needsRepair.Clear();
        }

        private static void ReplaceFile(string tempPath, string path)
        {
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, path + BackupExtension);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }

        private static string TryReadGzipJson(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                using (FileStream file = File.OpenRead(path))
                using (GZipStream gzip = new GZipStream(file, CompressionMode.Decompress))
                using (StreamReader reader = new StreamReader(gzip, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static long ReadGeneration(string json)
        {
            try
            {
                PartitionHeader header = JsonUtility.FromJson<PartitionHeader>(json);
                return header != null ? header.Generation : -1;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private bool CheckHeader(int schemaVersion, string projectGuid)
        {
            if (schemaVersion != HistoryState.CurrentSchemaVersion)
            {
                SetError(HistoryStoreStatus.UnsupportedSchema,
                    $"The history uses schema version {schemaVersion}, but this version supports {HistoryState.CurrentSchemaVersion}.");
                return false;
            }

            return CheckProject(projectGuid);
        }

        private bool CheckProject(string projectGuid)
        {
            bool hasProject = !string.IsNullOrEmpty(projectGuid) && !string.IsNullOrEmpty(_projectGuid);
            if (hasProject && projectGuid != _projectGuid)
            {
                SetError(HistoryStoreStatus.ProjectMismatch, "The history belongs to a different project.");
                return false;
            }

            return true;
        }

        private void SetError(HistoryStoreStatus status, string message)
        {
            Status = status;
            ErrorMessage = message;
            State = new HistoryState(_projectGuid);
        }

        private string GetPartitionPath(string kind, string id)
        {
            return Path.Combine(Directory, kind, ToSafeFileName(id) + PartitionExtension);
        }

        private static string GetPartitionKey(string kind, string id)
        {
            return kind + "/" + id;
        }

        private static PartitionEntry SplitPartitionKey(string key, long generation)
        {
            int slash = key.IndexOf('/');
            return new PartitionEntry(key.Substring(0, slash), key.Substring(slash + 1), generation);
        }

        private static string GetMonth(string utcText)
        {
            return utcText != null && utcText.Length >= 7 ? utcText.Substring(0, 7) : "unknown";
        }

        private static string ToSafeFileName(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return "_";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            return new string(id.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
        }

        private static string GetAvailablePath(string path)
        {
            if (!File.Exists(path))
            {
                return path;
            }

            string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
            return Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, $"{Path.GetFileNameWithoutExtension(path)}.{stamp}{Path.GetExtension(path)}");
        }

        private static Dictionary<string, List<T>> GroupBy<T>(IEnumerable<T> items, Func<T, string> keySelector, HashSet<string> onlyKeys)
        {
            Dictionary<string, List<T>> result = new Dictionary<string, List<T>>();
            foreach (T item in items)
            {
                string key = keySelector(item) ?? string.Empty;
                if (onlyKeys != null && !onlyKeys.Contains(key))
                {
                    continue;
                }

                if (!result.TryGetValue(key, out List<T> list))
                {
                    list = new List<T>();
                    result.Add(key, list);
                }

                list.Add(item);
            }

            return result;
        }

        [Serializable]
        private sealed class Manifest
        {
            [SerializeField] private int schemaVersion;
            [SerializeField] private string projectGuid;
            [SerializeField] private long generation;
            [SerializeField] private List<ScopeRecord> scopes;
            [SerializeField] private List<PartitionEntry> partitions;

            public Manifest(int schemaVersion, string projectGuid, long generation, List<ScopeRecord> scopes, List<PartitionEntry> partitions)
            {
                this.schemaVersion = schemaVersion;
                this.projectGuid = projectGuid;
                this.generation = generation;
                this.scopes = scopes;
                this.partitions = partitions;
            }

            public int SchemaVersion => schemaVersion;
            public string ProjectGuid => projectGuid;
            public long Generation => generation;
            public List<ScopeRecord> Scopes => scopes;
            public List<PartitionEntry> Partitions => partitions;
        }

        [Serializable]
        private sealed class PartitionEntry
        {
            [SerializeField] private string kind;
            [SerializeField] private string id;
            [SerializeField] private long generation;

            public PartitionEntry(string kind, string id, long generation)
            {
                this.kind = kind;
                this.id = id;
                this.generation = generation;
            }

            public string Kind => kind;
            public string Id => id;
            public long Generation => generation;
        }

        [Serializable]
        private sealed class PartitionHeader
        {
            [SerializeField] private long generation = -1;

            public long Generation => generation;
        }

        [Serializable]
        private sealed class ContextsPartition
        {
            [SerializeField] private long generation;
            [SerializeField] private string scopeGuid;
            [SerializeField] private List<ContextSnapshot> contexts;

            public ContextsPartition(long generation, string scopeGuid, List<ContextSnapshot> contexts)
            {
                this.generation = generation;
                this.scopeGuid = scopeGuid;
                this.contexts = contexts;
            }

            public List<ContextSnapshot> Contexts => contexts ?? new List<ContextSnapshot>();
        }

        [Serializable]
        private sealed class OwnersPartition
        {
            [SerializeField] private long generation;
            [SerializeField] private string ownerGuid;
            [SerializeField] private List<OwnerLevelRecord> records;

            public OwnersPartition(long generation, string ownerGuid, List<OwnerLevelRecord> records)
            {
                this.generation = generation;
                this.ownerGuid = ownerGuid;
                this.records = records;
            }

            public List<OwnerLevelRecord> Records => records ?? new List<OwnerLevelRecord>();
        }

        [Serializable]
        private sealed class EventsPartition
        {
            [SerializeField] private long generation;
            [SerializeField] private string month;
            [SerializeField] private List<ChangeEvent> events;
            [SerializeField] private List<ObservationRecord> observations;

            public EventsPartition(long generation, string month, List<ChangeEvent> events, List<ObservationRecord> observations)
            {
                this.generation = generation;
                this.month = month;
                this.events = events;
                this.observations = observations;
            }

            public List<ChangeEvent> Events => events ?? new List<ChangeEvent>();
            public List<ObservationRecord> Observations => observations ?? new List<ObservationRecord>();
        }
    }
}
