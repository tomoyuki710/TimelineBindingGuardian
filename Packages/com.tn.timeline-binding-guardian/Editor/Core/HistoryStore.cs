using System;
using System.IO;
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
    /// 履歴（state.json）の読み書き。
    /// - 書き込みは一時ファイル → フラッシュ → 同一フォルダ内で置換。前世代を .bak に残す。
    /// - 読めない・壊れている・別プロジェクト・未対応バージョンのときは空の履歴で上書きせず、読み取り専用のエラー状態にする。
    /// - 呼び出し側は状態の複製に変更を加え、保存に成功したときだけ Commit すること（保存失敗でイベントが失われたり重複したりしない）。
    /// </summary>
    public sealed class HistoryStore
    {
        public const string FileName = "state.json";

        private readonly string _projectGuid;

        public HistoryStore(string directory, string projectGuid)
        {
            Directory = directory;
            _projectGuid = projectGuid ?? string.Empty;
            State = new HistoryState(_projectGuid);
        }

        public string Directory { get; }
        public string FilePath => Path.Combine(Directory, FileName);
        public string BackupPath => FilePath + ".bak";
        public HistoryStoreStatus Status { get; private set; } = HistoryStoreStatus.Ready;
        public string ErrorMessage { get; private set; } = string.Empty;
        public HistoryState State { get; private set; }
        public bool IsWritable => Status == HistoryStoreStatus.Ready;

        public void Load()
        {
            ErrorMessage = string.Empty;
            if (!File.Exists(FilePath))
            {
                State = new HistoryState(_projectGuid);
                Status = HistoryStoreStatus.Ready;
                return;
            }

            HistoryState loaded;
            try
            {
                loaded = JsonUtility.FromJson<HistoryState>(File.ReadAllText(FilePath, Encoding.UTF8));
            }
            catch (Exception exception)
            {
                SetError(HistoryStoreStatus.Corrupted, $"The history file could not be read: {exception.Message}");
                return;
            }

            if (loaded == null)
            {
                SetError(HistoryStoreStatus.Corrupted, "The history file is empty or invalid.");
                return;
            }

            if (loaded.SchemaVersion != HistoryState.CurrentSchemaVersion)
            {
                SetError(HistoryStoreStatus.UnsupportedSchema,
                    $"The history file uses schema version {loaded.SchemaVersion}, but this version supports {HistoryState.CurrentSchemaVersion}.");
                return;
            }

            bool hasProject = !string.IsNullOrEmpty(loaded.ProjectGuid) && !string.IsNullOrEmpty(_projectGuid);
            if (hasProject && loaded.ProjectGuid != _projectGuid)
            {
                SetError(HistoryStoreStatus.ProjectMismatch, "The history file belongs to a different project.");
                return;
            }

            State = loaded;
            Status = HistoryStoreStatus.Ready;
        }

        /// <summary>
        /// newState を保存し、成功したら現在の状態として採用する。失敗した場合は現在の状態を変えない。
        /// </summary>
        public bool TryCommit(HistoryState newState, out string error)
        {
            error = string.Empty;
            if (!IsWritable)
            {
                error = $"History is read-only: {ErrorMessage}";
                return false;
            }

            string tempPath = FilePath + ".tmp";
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                byte[] bytes = new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(newState));
                using (FileStream stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(FilePath))
                {
                    File.Replace(tempPath, FilePath, BackupPath);
                }
                else
                {
                    File.Move(tempPath, FilePath);
                }
            }
            catch (Exception exception)
            {
                error = $"The history file could not be saved: {exception.Message}";
                return false;
            }

            State = newState;
            return true;
        }

        public long GetFileSizeBytes()
        {
            return File.Exists(FilePath) ? new FileInfo(FilePath).Length : 0;
        }

        private void SetError(HistoryStoreStatus status, string message)
        {
            Status = status;
            ErrorMessage = message;
            State = new HistoryState(_projectGuid);
        }
    }
}
