using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 復元の事前表示（RestorePreview）。実行時はこれを作り直して、表示時から状態が変わっていないことを確かめる。
    /// </summary>
    public sealed class RestorePreview
    {
        public ChangeEvent Event { get; set; }
        public RestoreInput Input { get; set; }
        public RestorePlan Plan { get; set; }
        public string OwnerPath { get; set; } = string.Empty;
        public bool IsScene { get; set; }
        // 履歴のイベントによらず、現在の Override を外すだけのモード（Scene Bindings の階層表から）
        public bool RemoveOverrideOnly { get; set; }
        // 表示時の状態（実行直前に一致を確認する）
        public string Fingerprint { get; set; } = string.Empty;
    }

    public sealed class RestoreResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<string> Details { get; } = new List<string>();
        public string OwnerPath { get; set; } = string.Empty;
        // Prefab の復元前バックアップ（Scene は Undo で戻すためバックアップしない）
        public string BackupPath { get; set; } = string.Empty;
        // 復元で書き込んだ直後のファイルのハッシュ（取り消し時に、その後変更されていないことを確かめる）
        public string WrittenHash { get; set; } = string.Empty;
        public bool CanRollback => Success && !string.IsNullOrEmpty(BackupPath);
    }

    /// <summary>
    /// 安全な復元（仕様 10 章）。ユーザーの明示操作でのみ実行する。
    /// 変更するのはイベントの Owner 階層だけで、Phase 0 で検証済みの最小操作（要素の value の Override 除去 / 値の設定）しか行わない。
    /// Prefab は保存前にバックアップを取り、保存後に読み直して対象以外が変わっていないことを確認する。失敗したらバックアップへ戻す。
    /// Scene は Undo に登録して dirty にするだけで、保存はユーザーに任せる。
    /// </summary>
    public static class BindingRestoreService
    {
        private static string _backupDirectory;
        private static GuardianHistory _history;

        /// <summary>
        /// 復元前のバックアップの保存先。既定は UserSettings/TimelineBindingGuardian/backups（自動削除しない）。テストでは差し替える。
        /// </summary>
        public static string BackupDirectory
        {
            get => _backupDirectory ?? Path.Combine(GuardianHistory.DefaultDirectory, "backups");
            set => _backupDirectory = value;
        }

        /// <summary>
        /// 復元結果を記録する履歴。既定はプロジェクトの履歴。テストでは差し替える。
        /// </summary>
        public static GuardianHistory History
        {
            get => _history ?? GuardianHistory.Shared;
            set => _history = value;
        }

        /// <summary>
        /// Scene Bindings の階層表の 1 行（ある階層の Director × Track）を、「Override を外す」の対象として表す。
        /// 履歴のイベントは使わないため、変更前の値は持たない。
        /// </summary>
        public static ChangeEvent CreateRemoveOverrideTarget(LevelBindingState level, TrackLocator track)
        {
            return new ChangeEvent("remove-override", string.Empty, string.Empty, ChangeType.LocalOverrideChanged, string.Empty,
                level.Owner, level.Director, track, LevelValue.None, SnapshotConverter.CreateValue(level), false, string.Empty);
        }

        public static RestorePreview CreatePreview(ChangeEvent changeEvent, bool removeOverrideOnly = false)
        {
            RestorePreview preview = new RestorePreview
            {
                Event = changeEvent,
                OwnerPath = AssetDatabase.GUIDToAssetPath(changeEvent.Owner.AssetGuid),
                IsScene = changeEvent.Owner.AssetKind == AssetKind.Scene,
                RemoveOverrideOnly = removeOverrideOnly,
            };
            RestoreInput input = new RestoreInput { Event = changeEvent };
            preview.Input = input;

            string unavailable = GatherInput(preview, input, out PlayableDirector director, out TrackAsset track);
            if (!string.IsNullOrEmpty(unavailable))
            {
                input.ConflictReason = unavailable;
                input.CurrentEntryExists = true;
                input.TargetResolvable = true;
            }

            preview.Plan = removeOverrideOnly ? RestorePlanner.CreateRemoveOverride(input) : RestorePlanner.Create(input);
            preview.Fingerprint = CreateFingerprint(preview, director, track);
            return preview;
        }

        public static RestoreResult Execute(RestorePreview preview, RestoreOption option)
        {
            RestoreResult result = new RestoreResult { OwnerPath = preview.OwnerPath };
            RestorePreview fresh = CreatePreview(preview.Event, preview.RemoveOverrideOnly);
            bool sameState = fresh.Fingerprint == preview.Fingerprint;
            bool stillPossible = fresh.Plan.CanRestore && fresh.Plan.Options.Any(o => o.Operation == option.Operation);
            if (!sameState || !stillPossible)
            {
                result.Message = Lang.T("restore.result.changedSincePreview");
                return result;
            }

            try
            {
                return fresh.IsScene ? ExecuteInScene(fresh, option, result) : ExecuteInPrefab(fresh, option, result);
            }
            catch (Exception exception)
            {
                result.Success = false;
                result.Message = Lang.T("restore.result.failed", exception.Message);
                TryRollbackAfterFailure(result);
                return result;
            }
        }

        /// <summary>
        /// Prefab の復元を取り消す（バックアップから戻す）。復元後にファイルが変更されていれば上書きしない。
        /// </summary>
        public static RestoreResult Rollback(RestoreResult restored)
        {
            RestoreResult result = new RestoreResult { OwnerPath = restored.OwnerPath };
            if (!restored.CanRollback)
            {
                result.Message = Lang.T("restore.result.cannotRollback");
                return result;
            }

            string current = ComputeHash(restored.OwnerPath);
            if (current != restored.WrittenHash)
            {
                result.Message = Lang.T("restore.result.rollbackChanged", restored.BackupPath);
                return result;
            }

            WriteBack(restored.BackupPath, restored.OwnerPath);
            RecordOwner(restored.OwnerPath, false, "Restore undone");
            result.Success = true;
            result.Message = Lang.T("restore.result.rollbackDone");
            return result;
        }

        /// <summary>
        /// 現在の状態を集めて RestoreInput を埋める。復元を判定できない状態なら理由を返す。
        /// </summary>
        private static string GatherInput(RestorePreview preview, RestoreInput input, out PlayableDirector director, out TrackAsset track)
        {
            director = null;
            track = null;
            ChangeEvent changeEvent = preview.Event;
            if (string.IsNullOrEmpty(preview.OwnerPath))
            {
                return Lang.T("restore.unavailable.ownerMissing");
            }

            if (preview.IsScene && !DependencyIndex.IsSceneLoaded(preview.OwnerPath))
            {
                return Lang.T("restore.unavailable.sceneNotLoaded", preview.OwnerPath);
            }

            director = ResolveObject(changeEvent.Director.GlobalObjectId) as PlayableDirector;
            if (director == null)
            {
                return Lang.T("restore.unavailable.directorMissing");
            }

            track = FindTrack(director, changeEvent.Track);
            if (track == null)
            {
                return Lang.T("restore.unavailable.trackMissing");
            }

            input.ReadOnlyReason = GetReadOnlyReason(preview.OwnerPath, preview.IsScene);
            input.ConflictReason = GetConflictReason(preview.OwnerPath, preview.IsScene);
            input.LevelIsInstance = PrefabUtility.IsPartOfPrefabInstance(director);

            DirectorBindingTable table = BindingReader.Read(director);
            input.CurrentEntryExists = table.TryGet(track, out BindingEntry entry);
            input.CurrentValue = input.CurrentEntryExists ? ToValue(entry) : LevelValue.None;
            input.CurrentValueOverridden = input.CurrentEntryExists && entry.ValueOverridden;

            PlayableDirector source = PrefabUtility.GetCorrespondingObjectFromSource(director);
            Object sourceValue = null;
            if (source != null && BindingReader.Read(source).TryGet(track, out BindingEntry sourceEntry))
            {
                sourceValue = sourceEntry.Value;
                input.InheritedValue = ToValue(sourceEntry);
            }

            Object beforeTarget = ResolveObject(changeEvent.Before.TargetGlobalObjectId);
            input.TargetResolvable = IsCompatibleTarget(director, track, beforeTarget);
            input.InheritedMatchesBefore = InheritedMatchesBefore(changeEvent.Before, beforeTarget, sourceValue);
            return string.Empty;
        }

        private static RestoreResult ExecuteInPrefab(RestorePreview preview, RestoreOption option, RestoreResult result)
        {
            string path = preview.OwnerPath;
            Dictionary<string, string> before = CaptureOwnerTable(path);
            result.BackupPath = CreateBackup(path);
            result.Details.Add(Lang.T("restore.detail.backup", result.BackupPath));

            using (PrefabUtility.EditPrefabContentsScope scope = new PrefabUtility.EditPrefabContentsScope(path))
            {
                PlayableDirector assetDirector = ResolveObject(preview.Event.Director.GlobalObjectId) as PlayableDirector;
                if (!PrefabStageLocator.TryMapToContents(assetDirector, scope.prefabContentsRoot.transform, out Object mapped))
                {
                    throw new InvalidOperationException(Lang.T("restore.error.directorNotInContents"));
                }

                PlayableDirector contentsDirector = (PlayableDirector)mapped;
                TrackAsset track = FindTrack(contentsDirector, preview.Event.Track);
                Object target = ResolveTargetInContents(preview.Event.Before, scope.prefabContentsRoot.transform);
                Apply(contentsDirector, track, option, target, InteractionMode.AutomatedAction);
            }

            result.WrittenHash = ComputeHash(path);
            string problem = VerifyOwnerTable(path, before, preview, option);
            if (!string.IsNullOrEmpty(problem))
            {
                throw new InvalidOperationException(Lang.T("restore.error.verifyAfterSaveFailed", problem));
            }

            result.Success = true;
            result.Message = preview.RemoveOverrideOnly
                ? Lang.T("restore.result.removedAndSaved", Path.GetFileName(path))
                : Lang.T("restore.result.restoredAndSaved", Path.GetFileName(path));
            result.Details.Add(Lang.T("restore.detail.verified"));
            RecordOwner(path, false, "Restore");
            return result;
        }

        private static RestoreResult ExecuteInScene(RestorePreview preview, RestoreOption option, RestoreResult result)
        {
            PlayableDirector director = ResolveObject(preview.Event.Director.GlobalObjectId) as PlayableDirector;
            TrackAsset track = FindTrack(director, preview.Event.Track);
            Scene scene = director.gameObject.scene;
            Dictionary<string, string> before = CaptureSceneTable(scene);

            Undo.RecordObject(director, "Restore Timeline Binding");
            Object target = ResolveObject(preview.Event.Before.TargetGlobalObjectId);
            Apply(director, track, option, target, InteractionMode.UserAction);
            // RevertPropertyOverride では Scene が dirty にならない（Phase 0）ため明示する。保存はユーザーに任せる
            EditorSceneManager.MarkSceneDirty(scene);

            string problem = CompareTables(before, CaptureSceneTable(scene), Key(director, track), ExpectedState(preview, option));
            if (!string.IsNullOrEmpty(problem))
            {
                Undo.PerformUndo();
                result.Message = Lang.T("restore.result.sceneVerifyFailed", problem);
                return result;
            }

            result.Success = true;
            result.Message = Lang.T("restore.result.restoredInScene", scene.name);
            return result;
        }

        private static void Apply(PlayableDirector director, TrackAsset track, RestoreOption option, Object target, InteractionMode mode)
        {
            if (option.Operation == RestoreOperation.RevertToInherited)
            {
                // Timeline のプレビュー中（Animation Mode）は RevertPropertyOverride が禁止されるため、
                // 該当する Modification 1 件だけを取り除く（結果は RevertPropertyOverride と同じ。事前確認済み）。
                // Scene では Undo できるよう、Prefab インスタンスの Modification 全体を Undo に登録してから行う
                bool isScene = mode == InteractionMode.UserAction;
                if (isScene)
                {
                    GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(director);
                    Undo.RegisterCompleteObjectUndo(PrefabUtility.GetPrefabInstanceHandle(root), "Restore Timeline Binding");
                }

                RemoveValueModification(director, track);
                return;
            }

            director.SetGenericBinding(track, option.ResultValue.ValueState == ValueState.Null ? null : target);
            if (PrefabUtility.IsPartOfPrefabInstance(director))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(director);
            }
        }

        /// <summary>
        /// 最も外側の Prefab インスタンスの Modification から、この Director（1 つ下の階層）の
        /// m_SceneBindings.Array.data[i].value だけを取り除く。仕様 10.1 が禁じる「無条件な置換」ではなく、対象 1 件だけを除く。
        /// 該当がちょうど 1 件でなければ何もせず例外にする（保存後の検証とバックアップへの書き戻しに任せる）。
        /// </summary>
        private static void RemoveValueModification(PlayableDirector director, TrackAsset track)
        {
            int index;
            using (SerializedObject serializedObject = new SerializedObject(director))
            {
                index = FindElementIndex(serializedObject, track);
            }

            GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(director);
            Object source = PrefabUtility.GetCorrespondingObjectFromSource(director);
            if (root == null || source == null)
            {
                throw new InvalidOperationException(Lang.T("restore.error.notInstance"));
            }

            string propertyPath = $"{BindingReader.SceneBindingsProperty}.Array.data[{index}].value";
            PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(root);
            int matches = modifications.Count(m => m.target == source && m.propertyPath == propertyPath);
            if (matches != 1)
            {
                throw new InvalidOperationException(Lang.T("restore.error.overrideCount", matches));
            }

            PropertyModification[] kept = modifications.Where(m => !(m.target == source && m.propertyPath == propertyPath)).ToArray();
            PrefabUtility.SetPropertyModifications(root, kept);
        }

        private static int FindElementIndex(SerializedObject serializedObject, TrackAsset track)
        {
            SerializedProperty array = serializedObject.FindProperty(BindingReader.SceneBindingsProperty);
            for (int i = 0; i < array.arraySize; i++)
            {
                if (array.GetArrayElementAtIndex(i).FindPropertyRelative("key").objectReferenceValue == track)
                {
                    return i;
                }
            }

            throw new InvalidOperationException(Lang.T("restore.error.entryNotFound"));
        }

        private static SerializedProperty FindValueProperty(SerializedObject serializedObject, TrackAsset track)
        {
            SerializedProperty array = serializedObject.FindProperty(BindingReader.SceneBindingsProperty);
            for (int i = 0; i < array.arraySize; i++)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(i);
                if (element.FindPropertyRelative("key").objectReferenceValue == track)
                {
                    return element.FindPropertyRelative("value");
                }
            }

            throw new InvalidOperationException(Lang.T("restore.error.entryNotFound"));
        }

        private static Object ResolveTargetInContents(LevelValue before, Transform contentsRoot)
        {
            if (before.ValueState != ValueState.Bound)
            {
                return null;
            }

            Object assetTarget = ResolveObject(before.TargetGlobalObjectId);
            if (!PrefabStageLocator.TryMapToContents(assetTarget, contentsRoot, out Object mapped))
            {
                throw new InvalidOperationException(Lang.T("restore.error.targetNotInContents"));
            }

            return mapped;
        }

        /// <summary>
        /// Owner Asset のすべての Director・Track の状態（要素の有無、値、Override 有無）。復元前後の比較に使う。
        /// </summary>
        private static Dictionary<string, string> CaptureOwnerTable(string path)
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return CaptureTable(root.GetComponentsInChildren<PlayableDirector>(true));
        }

        private static Dictionary<string, string> CaptureSceneTable(Scene scene)
        {
            return CaptureTable(scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayableDirector>(true)));
        }

        private static Dictionary<string, string> CaptureTable(IEnumerable<PlayableDirector> directors)
        {
            Dictionary<string, string> table = new Dictionary<string, string>();
            foreach (PlayableDirector director in directors)
            {
                TimelineAsset timeline = director.playableAsset as TimelineAsset;
                if (timeline == null)
                {
                    continue;
                }

                DirectorBindingTable bindings = BindingReader.Read(director);
                foreach (TrackAsset track in BindingReader.GetBindableTracks(timeline))
                {
                    table[Key(director, track)] = bindings.TryGet(track, out BindingEntry entry)
                        ? $"{PrefabContextResolver.GetGlobalObjectId(entry.Value)}|{entry.ValueOverridden}"
                        : "NoEntry";
                }
            }

            return table;
        }

        private static string VerifyOwnerTable(string path, Dictionary<string, string> before, RestorePreview preview, RestoreOption option)
        {
            PlayableDirector director = ResolveObject(preview.Event.Director.GlobalObjectId) as PlayableDirector;
            TrackAsset track = director != null ? FindTrack(director, preview.Event.Track) : null;
            if (track == null)
            {
                return Lang.T("restore.problem.notFoundAfterSave");
            }

            return CompareTables(before, CaptureOwnerTable(path), Key(director, track), ExpectedState(preview, option));
        }

        /// <summary>
        /// 対象以外がすべて同じで、対象が期待どおりかを確認する。問題があれば内容を返す。
        /// </summary>
        private static string CompareTables(Dictionary<string, string> before, Dictionary<string, string> after, string targetKey, Func<string, bool> isExpected)
        {
            List<string> problems = new List<string>();
            foreach (KeyValuePair<string, string> pair in before)
            {
                if (pair.Key == targetKey)
                {
                    continue;
                }

                if (!after.TryGetValue(pair.Key, out string value) || value != pair.Value)
                {
                    problems.Add(Lang.T("restore.problem.unrelatedChanged", pair.Key));
                }
            }

            if (!after.TryGetValue(targetKey, out string actual))
            {
                problems.Add(Lang.T("restore.problem.entryMissing"));
            }
            else if (!isExpected(actual))
            {
                problems.Add(Lang.T("restore.problem.unexpectedState", actual));
            }

            return string.Join("; ", problems);
        }

        /// <summary>
        /// 復元後の対象の状態（"値の ID|Override 有無"）が期待どおりかを判定する関数。
        /// 継承に戻した場合は Override が外れていること、値を設定した場合はその ID（NULL なら空）であること。
        /// </summary>
        private static Func<string, bool> ExpectedState(RestorePreview preview, RestoreOption option)
        {
            if (option.Operation == RestoreOperation.RevertToInherited)
            {
                return state => state.EndsWith("|False", StringComparison.Ordinal);
            }

            string id = option.ResultValue.ValueState == ValueState.Null ? string.Empty : option.ResultValue.TargetGlobalObjectId;
            return state => state.StartsWith(id + "|", StringComparison.Ordinal);
        }

        private static bool InheritedMatchesBefore(LevelValue before, Object beforeTarget, Object sourceValue)
        {
            if (before.ValueState != ValueState.Bound)
            {
                return sourceValue == null;
            }

            Object normalized = beforeTarget != null ? PrefabUtility.GetCorrespondingObjectFromSource(beforeTarget) : null;
            return normalized != null && normalized == sourceValue;
        }

        private static bool IsCompatibleTarget(PlayableDirector director, TrackAsset track, Object target)
        {
            if (target == null)
            {
                return false;
            }

            TimelineAsset timeline = director.playableAsset as TimelineAsset;
            PlayableBinding binding = timeline.outputs.FirstOrDefault(o => o.sourceObject == track);
            return binding.outputTargetType == null || binding.outputTargetType.IsInstanceOfType(target);
        }

        private static string GetReadOnlyReason(string path, bool isScene)
        {
            if (path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                return Lang.T("restore.readOnly.package");
            }

            if (!AssetDatabase.IsOpenForEdit(path))
            {
                return Lang.T("restore.readOnly.notEditable");
            }

            GameObject root = isScene ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            bool isModel = root != null && PrefabUtility.GetPrefabAssetType(root) == PrefabAssetType.Model;
            return isModel ? Lang.T("restore.readOnly.model") : string.Empty;
        }

        private static string GetConflictReason(string path, bool isScene)
        {
            if (isScene)
            {
                // Timeline のプレビュー中（Animation Mode）でも実行できる。
                // バインドはアニメーションで動かす値ではないため、プレビュー終了時に巻き戻されないことを確認済み
                return string.Empty;
            }

            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            bool isOpenAndDirty = stage != null && stage.assetPath == path && stage.scene.isDirty;
            return isOpenAndDirty ? Lang.T("restore.conflict.prefabModeDirty") : string.Empty;
        }

        private static string CreateFingerprint(RestorePreview preview, PlayableDirector director, TrackAsset track)
        {
            if (director == null || track == null)
            {
                return "unavailable";
            }

            string entry = BindingReader.Read(director).TryGet(track, out BindingEntry value)
                ? $"{PrefabContextResolver.GetGlobalObjectId(value.Value)}|{value.ValueOverridden}"
                : "NoEntry";
            string file = preview.IsScene ? string.Empty : ComputeHash(preview.OwnerPath);
            return $"{entry}|{file}";
        }

        private static TrackAsset FindTrack(PlayableDirector director, TrackLocator locator)
        {
            TimelineAsset timeline = director != null ? director.playableAsset as TimelineAsset : null;
            if (timeline == null)
            {
                return null;
            }

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(timeline)))
            {
                if (!(asset is TrackAsset track))
                {
                    continue;
                }

                bool hasId = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(track, out string _, out long localId);
                if (hasId && BindingIdentity.FormatFileId(localId) == locator.TrackFileId)
                {
                    return track;
                }
            }

            return null;
        }

        private static LevelValue ToValue(BindingEntry entry)
        {
            ValueState state = entry.Value != null ? ValueState.Bound : (entry.IsMissing ? ValueState.MissingReference : ValueState.Null);
            TargetLocator target = PrefabContextResolver.CreateTargetLocator(entry.Value);
            LocalState local = entry.ValueOverridden ? LocalState.ExplicitOverride : LocalState.Inherited;
            return new LevelValue(state, local, target.GlobalObjectId, target.HierarchyPathHint, target.TypeName);
        }

        private static string Key(PlayableDirector director, TrackAsset track)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(track, out string _, out long localId);
            return $"{PrefabContextResolver.GetGlobalObjectId(director)}|{localId}";
        }

        private static Object ResolveObject(string globalObjectId)
        {
            if (string.IsNullOrEmpty(globalObjectId) || !GlobalObjectId.TryParse(globalObjectId, out GlobalObjectId id))
            {
                return null;
            }

            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
        }

        private static string CreateBackup(string assetPath)
        {
            Directory.CreateDirectory(BackupDirectory);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            string backup = Path.Combine(BackupDirectory, $"{stamp}_{Path.GetFileName(assetPath)}");
            File.Copy(Path.GetFullPath(assetPath), backup, false);
            string meta = Path.GetFullPath(assetPath) + ".meta";
            if (File.Exists(meta))
            {
                File.Copy(meta, backup + ".meta", false);
            }

            return backup;
        }

        private static void TryRollbackAfterFailure(RestoreResult result)
        {
            if (string.IsNullOrEmpty(result.BackupPath))
            {
                return;
            }

            // 自分が書き込んだ直後のままのときだけ書き戻す（他の変更を上書きしない）
            bool untouched = string.IsNullOrEmpty(result.WrittenHash) || ComputeHash(result.OwnerPath) == result.WrittenHash;
            if (!untouched)
            {
                result.Details.Add(Lang.T("restore.detail.notPutBack", result.BackupPath));
                return;
            }

            WriteBack(result.BackupPath, result.OwnerPath);
            result.Details.Add(Lang.T("restore.detail.putBack"));
        }

        private static void WriteBack(string backupPath, string assetPath)
        {
            File.Copy(backupPath, Path.GetFullPath(assetPath), true);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        private static void RecordOwner(string path, bool isScene, string reason)
        {
            if (isScene)
            {
                return;
            }

            ScanResult scan = new SceneBindingScanner().ScanPrefabAsset(path);
            RecordSummary summary = History.Record(new[] { scan }, reason);
            if (!string.IsNullOrEmpty(summary.Error))
            {
                Debug.LogWarning(Lang.T("restore.log.historyNotSaved", summary.Error));
            }
        }

        private static string ComputeHash(string assetPath)
        {
            string fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath))
            {
                return string.Empty;
            }

            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(fullPath)));
            }
        }
    }
}
