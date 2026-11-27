using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// Scene / Prefab Asset を起点に、各 Director・Track の階層ごとのバインド状態を読み取り専用で取得する。
    /// Scene・Prefab・Timeline の変更、Selection・アクティブ Scene の変更、Inspector 生成、Evaluate は行わない。
    /// </summary>
    public sealed class SceneBindingScanner
    {
        private readonly Dictionary<int, DirectorBindingTable> _tableCache = new Dictionary<int, DirectorBindingTable>();
        private readonly PrefabOverrideInspector _overrideInspector = new PrefabOverrideInspector();

        public List<ScanResult> ScanLoadedScenes()
        {
            List<ScanResult> results = new List<ScanResult>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                {
                    results.Add(Scan(scene));
                }
            }

            return results;
        }

        public ScanResult Scan(Scene scene)
        {
            List<string> diagnostics = new List<string>();
            AssetLocator owner = PrefabContextResolver.CreateSceneLocator(scene);
            if (!owner.IsSaved)
            {
                diagnostics.Add(Lang.T("engine.scan.unsavedScene"));
            }

            List<DirectorObservation> directors = new List<DirectorObservation>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (PlayableDirector director in root.GetComponentsInChildren<PlayableDirector>(true))
                {
                    directors.Add(Observe(director, true));
                }
            }

            if (scene.isDirty)
            {
                diagnostics.Add(Lang.T("engine.scan.dirtyScene"));
            }

            return new ScanResult(owner, GetUtcNow(), directors, diagnostics, scene.isDirty);
        }

        /// <summary>
        /// Prefab Asset を直接ロードして（LoadPrefabContents は使わない）、その Asset を文脈とした状態を取得する。
        /// </summary>
        public ScanResult ScanPrefabAsset(string assetPath)
        {
            List<string> diagnostics = new List<string>();
            List<DirectorObservation> directors = new List<DirectorObservation>();
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (root == null)
            {
                diagnostics.Add(Lang.T("engine.scan.prefabLoadFailed", assetPath));
            }
            else
            {
                foreach (PlayableDirector director in root.GetComponentsInChildren<PlayableDirector>(true))
                {
                    directors.Add(Observe(director, false));
                }
            }

            return new ScanResult(PrefabContextResolver.CreateAssetLocator(assetPath), GetUtcNow(), directors, diagnostics);
        }

        private DirectorObservation Observe(PlayableDirector director, bool isSceneContext)
        {
            List<string> diagnostics = new List<string>();
            DirectorLocator locator = PrefabContextResolver.CreateDirectorLocator(director);
            AssetLocator owner = PrefabContextResolver.GetOwner(director);
            List<InstanceNode> instanceChain = isSceneContext ? PrefabContextResolver.GetInstanceChain(director) : new List<InstanceNode>();
            List<TrackBindingObservation> tracks = new List<TrackBindingObservation>();

            TimelineAsset timeline = director.playableAsset as TimelineAsset;
            if (timeline == null)
            {
                string reason = director.playableAsset == null ? Lang.T("engine.scan.noPlayableAsset") : Lang.T("engine.scan.notTimelineAsset");
                diagnostics.Add(reason);
                return new DirectorObservation(locator, owner, null, director.gameObject.activeInHierarchy, director.enabled, instanceChain, tracks, diagnostics);
            }

            AssetLocator timelineLocator = PrefabContextResolver.CreateAssetLocator(AssetDatabase.GetAssetPath(timeline));
            List<PlayableDirector> chain = PrefabContextResolver.GetSourceChain(director);
            List<DirectorBindingTable> tables = new List<DirectorBindingTable>();
            for (int i = 0; i < chain.Count; i++)
            {
                DirectorBindingTable table = GetTable(chain[i]);
                tables.Add(table);
                AddTableDiagnostics(diagnostics, i, chain[i], table);
            }

            foreach (TrackAsset track in BindingReader.GetBindableTracks(timeline))
            {
                tracks.Add(ObserveTrack(timeline, timelineLocator, track, chain, tables));
            }

            return new DirectorObservation(locator, owner, timelineLocator, director.gameObject.activeInHierarchy, director.enabled, instanceChain, tracks, diagnostics);
        }

        private TrackBindingObservation ObserveTrack(
            TimelineAsset timeline,
            AssetLocator timelineLocator,
            TrackAsset track,
            List<PlayableDirector> chain,
            List<DirectorBindingTable> tables)
        {
            List<LevelBindingState> levels = new List<LevelBindingState>();
            for (int i = 0; i < chain.Count; i++)
            {
                PlayableDirector source = i + 1 < chain.Count ? chain[i + 1] : null;
                DirectorBindingTable sourceTable = i + 1 < chain.Count ? tables[i + 1] : null;
                levels.Add(ObserveLevel(i, timeline, track, chain[i], tables[i], source, sourceTable));
            }

            TrackLocator trackLocator = CreateTrackLocator(timeline, timelineLocator, track);
            string key = BindingIdentity.CreateBindingKey(levels[0].Director.GlobalObjectId, trackLocator.TimelineGuid, trackLocator.TrackFileId);
            TrackBindingObservation observation = new TrackBindingObservation(key, trackLocator, levels);
            BindingOriginAnalyzer.Analyze(observation);
            return observation;
        }

        private LevelBindingState ObserveLevel(
            int level,
            TimelineAsset timeline,
            TrackAsset track,
            PlayableDirector director,
            DirectorBindingTable table,
            PlayableDirector source,
            DirectorBindingTable sourceTable)
        {
            AssetLocator owner = PrefabContextResolver.GetOwner(director);
            ContextKind contextKind = PrefabContextResolver.GetContextKind(director);
            DirectorLocator directorLocator = PrefabContextResolver.CreateDirectorLocator(director);

            if (director.playableAsset != timeline)
            {
                return new LevelBindingState(level, owner, contextKind, directorLocator, ValueState.NotApplicable, LocalState.NotApplicable,
                    TargetLocator.None, false, false, false, null, Lang.T("engine.level.differentPlayableAsset"));
            }

            ValueState valueState = GetValueState(table, track, out BindingEntry entry);
            bool hasEntry = valueState != ValueState.NoEntry && valueState != ValueState.Unknown;
            bool valueOverridden = hasEntry && entry.ValueOverridden;
            List<OverrideEvidence> evidence = _overrideInspector.GetEvidence(director, source, hasEntry ? entry.Index : -1);

            if (source == null)
            {
                LocalState baseState = contextKind == ContextKind.SceneInstance || contextKind == ContextKind.PrefabInstance
                    ? LocalState.Unknown
                    : LocalState.DefinedHere;
                string baseNote = baseState == LocalState.Unknown ? Lang.T("engine.level.sourceUnresolved") : string.Empty;
                return new LevelBindingState(level, owner, contextKind, directorLocator, valueState, baseState,
                    CreateTarget(valueState, entry), valueOverridden, false, false, evidence, baseNote);
            }

            if (source.playableAsset != timeline)
            {
                return new LevelBindingState(level, owner, contextKind, directorLocator, valueState, LocalState.Unknown,
                    CreateTarget(valueState, entry), valueOverridden, false, true, evidence, Lang.T("engine.level.playableAssetDiffers"));
            }

            ValueState sourceState = GetValueState(sourceTable, track, out BindingEntry sourceEntry);
            bool changed = !IsSameBinding(valueState, entry, sourceState, sourceEntry);
            // 配列のずれで Override 印が付いたが値は同じ、または要素の削除（配列サイズの Override）
            bool shifted = valueOverridden && !changed;
            bool isNoEntryChange = changed && valueState == ValueState.NoEntry;
            bool removedHere = isNoEntryChange && table.SizeOverridden;
            bool structural = shifted || removedHere;
            LocalState localState = GetLocalState(valueState, valueOverridden, changed, table.SizeOverridden, out string note);
            return new LevelBindingState(level, owner, contextKind, directorLocator, valueState, localState,
                CreateTarget(valueState, entry), valueOverridden, structural, changed, evidence, note);
        }

        /// <summary>
        /// Phase 0 の判定規則（Docs~/Phase0_Investigation_JP.md 5章）。
        /// </summary>
        private static LocalState GetLocalState(ValueState valueState, bool valueOverridden, bool changed, bool sizeOverridden, out string note)
        {
            note = string.Empty;
            if (valueState == ValueState.Unknown)
            {
                note = Lang.T("engine.level.duplicateEntries");
                return LocalState.Unknown;
            }

            if (!changed)
            {
                if (valueOverridden)
                {
                    note = Lang.T("engine.level.arrayShift");
                }

                return LocalState.Inherited;
            }

            if (valueState == ValueState.NoEntry)
            {
                if (sizeOverridden)
                {
                    note = Lang.T("engine.level.entryRemovedHere");
                    return LocalState.ExplicitOverride;
                }

                note = Lang.T("engine.level.entryMissingNoEvidence");
                return LocalState.Unknown;
            }

            if (valueOverridden)
            {
                return LocalState.ExplicitOverride;
            }

            note = Lang.T("engine.level.valueDiffersNoEvidence");
            return LocalState.Unknown;
        }

        private static bool IsSameBinding(ValueState state, BindingEntry entry, ValueState sourceState, BindingEntry sourceEntry)
        {
            if (state != sourceState)
            {
                return false;
            }

            if (state != ValueState.Bound)
            {
                return true;
            }

            Object normalized = PrefabContextResolver.NormalizeToSource(entry.Value);
            return normalized != null && normalized == sourceEntry.Value;
        }

        private static ValueState GetValueState(DirectorBindingTable table, TrackAsset track, out BindingEntry entry)
        {
            entry = default;
            if (!table.ArrayFound || table.IsDuplicate(track))
            {
                return ValueState.Unknown;
            }

            if (!table.TryGet(track, out entry))
            {
                return ValueState.NoEntry;
            }

            if (entry.Value != null)
            {
                return ValueState.Bound;
            }

            return entry.IsMissing ? ValueState.MissingReference : ValueState.Null;
        }

        private static TargetLocator CreateTarget(ValueState valueState, BindingEntry entry)
        {
            return valueState == ValueState.Bound ? PrefabContextResolver.CreateTargetLocator(entry.Value) : TargetLocator.None;
        }

        private static TrackLocator CreateTrackLocator(TimelineAsset timeline, AssetLocator timelineLocator, TrackAsset track)
        {
            string fileId = string.Empty;
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(track, out string _, out long localId))
            {
                fileId = BindingIdentity.FormatFileId(localId);
            }

            return new TrackLocator(
                timelineLocator.AssetGuid,
                timelineLocator.AssetPathHint,
                fileId,
                track.GetType().Name,
                GetTrackPath(track),
                BindingReader.GetOutputTargetTypeName(timeline, track));
        }

        private static string GetTrackPath(TrackAsset track)
        {
            string path = track.name;
            TrackAsset parent = track.parent as TrackAsset;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent as TrackAsset;
            }

            return path;
        }

        private DirectorBindingTable GetTable(PlayableDirector director)
        {
            int key = director.GetInstanceID();
            if (!_tableCache.TryGetValue(key, out DirectorBindingTable table))
            {
                table = BindingReader.Read(director);
                // Prefab Asset のオブジェクトは同じスキャン内で共有されるためキャッシュする（Scene 側も同一 ID なら同一オブジェクト）
                _tableCache.Add(key, table);
            }

            return table;
        }

        private static void AddTableDiagnostics(List<string> diagnostics, int level, PlayableDirector director, DirectorBindingTable table)
        {
            string owner = PrefabContextResolver.GetOwner(director).ToString();
            if (!table.ArrayFound)
            {
                diagnostics.Add(Lang.T("engine.scan.sceneBindingsNotFound", level, owner));
            }

            if (table.StaleKeyCount > 0)
            {
                diagnostics.Add(Lang.T("engine.scan.staleKeys", level, owner, table.StaleKeyCount));
            }

            if (table.NullKeyCount > 0)
            {
                diagnostics.Add(Lang.T("engine.scan.nullKeys", level, owner, table.NullKeyCount));
            }
        }

        private static string GetUtcNow()
        {
            return DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        }
    }
}
