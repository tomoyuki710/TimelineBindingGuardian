using NUnit.Framework;
using UnityEditor;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// テスト中は自動監視を止め（テスト用の Scene・Prefab を開いたり生成したりしても、実プロジェクトの履歴に記録されないように）、表示言語を英語に固定する。
    /// テスト終了直後に届くインポート通知も記録しないよう、再開は次の Editor 更新まで遅らせ、待機中の予約を捨てる。
    /// </summary>
    [SetUpFixture]
    public sealed class TestSetUpFixture
    {
        [OneTimeSetUp]
        public void SuspendMonitoring()
        {
            // 既存テストは英語の文言で検証するため、テスト中は英語に固定する（終了時に設定の言語へ戻す）
            Lang.Current = UiLanguage.English;
            BindingChangeMonitor.IsSuspended = true;
            ScanScheduler.ClearPending();
        }

        [OneTimeTearDown]
        public void ResumeMonitoring()
        {
            EditorApplication.delayCall += ResumeAfterTests;
        }

        private static void ResumeAfterTests()
        {
            GuardianSettings.Instance.ApplyLanguage();
            ScanScheduler.ClearPending();
            BindingChangeMonitor.IsSuspended = false;
        }
    }
}
