using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 意図的な NULL の共有ファイル（ProjectSettings/Packages/com.tn.timeline-binding-guardian/IntentionalNullBindings.json）。
    /// - リポジトリに入れてチームで共有する前提。Pull などで外から書き換わったら、更新日時を見て自動で読み直す。
    /// - 壊れている（マージの衝突記号など）ときは、通知を止めない（安全側）・上書き保存もしない。理由は Error で示す。
    /// </summary>
    [InitializeOnLoad]
    public static class IntentionalNullRegistry
    {
        public const string FileName = "IntentionalNullBindings.json";
        private const double PollIntervalSeconds = 2.0;
        // 一覧の表示などで何度も参照されるため、ファイルの日時の確認はこの間隔に 1 回まで
        private const double CheckIntervalSeconds = 0.5;

        private static readonly IntentionalNullList Empty = new IntentionalNullList();
        private static IntentionalNullList _list;
        private static string _loadedPath;
        private static DateTime _loadedWriteTimeUtc;
        private static bool _loadedExists;
        private static string _error = string.Empty;
        private static double _nextPollTime;
        private static bool _raiseScheduled;
        private static double _lastCheckTime = double.MinValue;

        static IntentionalNullRegistry()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        /// <summary>
        /// 共有リストが変わったとき（自分の登録・解除、外からの書き換え）。
        /// </summary>
        public static event Action Changed;

        /// <summary>
        /// テストで共有ファイルの場所を差し替える（null なら既定の場所）。
        /// </summary>
        public static string FilePathOverride { get; set; }

        public static string DefaultFilePath => Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty,
            "ProjectSettings", "Packages", "com.tn.timeline-binding-guardian", FileName);

        public static string FilePath => FilePathOverride ?? DefaultFilePath;

        /// <summary>
        /// 共有ファイルを読めないときの理由（読めていれば空文字）。
        /// </summary>
        public static string Error
        {
            get
            {
                Refresh();
                return _error;
            }
        }

        public static IReadOnlyList<IntentionalNullEntry> Entries
        {
            get
            {
                Refresh();
                return (_list ?? Empty).Entries;
            }
        }

        /// <summary>
        /// 共有リストに載っている階層か。ファイルが壊れているときは false（通知を止めない）。
        /// </summary>
        public static bool Contains(string ownerLevelKey)
        {
            Refresh();
            return _list != null && _list.Contains(ownerLevelKey);
        }

        public static bool TryAdd(IntentionalNullEntry entry, out string error)
        {
            return TryUpdate(list => list.Add(entry), out error);
        }

        public static bool TryRemove(string ownerLevelKey, out string error)
        {
            return TryUpdate(list => list.Remove(ownerLevelKey), out error);
        }

        private static bool TryUpdate(Action<IntentionalNullList> update, out string error)
        {
            // 保存の直前は必ず最新を読む（他の人の登録を Pull した直後でも消さないため）
            Refresh(true);
            error = _error;
            if (_list == null)
            {
                // 壊れたファイルを上書きしない（チームの登録を失わないため）
                return false;
            }

            update(_list);
            try
            {
                string path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
                string tempPath = path + ".tmp";
                File.WriteAllText(tempPath, _list.ToJson(), new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, null);
                }
                else
                {
                    File.Move(tempPath, path);
                }

                RememberFileState(path);
            }
            catch (Exception exception)
            {
                error = Lang.T("intentional.file.saveFailed", exception.Message);
                // 保存できなかった変更は捨て、ファイルから読み直す
                _loadedPath = null;
                return false;
            }

            ScheduleRaiseChanged();
            return true;
        }

        /// <summary>
        /// すぐにファイルを確認し、外から書き換わっていれば読み直す（テストや、確実に最新にしたいとき用）。
        /// </summary>
        public static void CheckForChanges()
        {
            Refresh(true);
        }

        /// <summary>
        /// 場所・存在・更新日時が前回と違えば読み直す。読み直したら true。
        /// </summary>
        private static bool Refresh(bool force = false)
        {
            string path = FilePath;
            double now = EditorApplication.timeSinceStartup;
            bool checkedRecently = path == _loadedPath && now - _lastCheckTime < CheckIntervalSeconds;
            if (checkedRecently && !force)
            {
                return false;
            }

            _lastCheckTime = now;
            bool exists = File.Exists(path);
            DateTime writeTime = exists ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            bool unchanged = path == _loadedPath && exists == _loadedExists && writeTime == _loadedWriteTimeUtc;
            if (unchanged)
            {
                return false;
            }

            _loadedPath = path;
            _loadedExists = exists;
            _loadedWriteTimeUtc = writeTime;
            _error = string.Empty;
            if (!exists)
            {
                _list = new IntentionalNullList();
                return true;
            }

            try
            {
                if (!IntentionalNullList.TryParse(File.ReadAllText(path, Encoding.UTF8), out _list, out _error))
                {
                    _list = null;
                }
            }
            catch (Exception exception)
            {
                _list = null;
                _error = Lang.T("intentional.file.readFailed", exception.Message);
            }

            return true;
        }

        private static void RememberFileState(string path)
        {
            _loadedPath = path;
            _loadedExists = File.Exists(path);
            _loadedWriteTimeUtc = _loadedExists ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        }

        /// <summary>
        /// Pull などで外から書き換わったことに気付けるよう、ときどき更新日時を確認する（読み直したら表示を更新する）。
        /// </summary>
        private static void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup < _nextPollTime)
            {
                return;
            }

            _nextPollTime = EditorApplication.timeSinceStartup + PollIntervalSeconds;
            bool wasLoaded = _loadedPath != null;
            if (Refresh(true) && wasLoaded)
            {
                ScheduleRaiseChanged();
            }
        }

        private static void ScheduleRaiseChanged()
        {
            if (_raiseScheduled)
            {
                return;
            }

            _raiseScheduled = true;
            EditorApplication.delayCall += RaiseChanged;
        }

        private static void RaiseChanged()
        {
            _raiseScheduled = false;
            Changed?.Invoke();
            // 通知の表示（ウィンドウ・Hierarchy）は履歴の変化と同じ経路で更新する
            GuardianHistory.RaiseChanged();
        }
    }
}
