using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// Prefab Mode を開いている間は自動検査を延期し、閉じたら処理する（自動保存のたびに待たされないように）。
    /// </summary>
    public sealed class PrefabModeDeferralTests
    {
        private string _middlePath;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            TestFixtureBuilder.BuildMutable();
            _middlePath = TestFixtureBuilder.PathIn(TestFixtureBuilder.MutableFolderName, "Middle.prefab");
        }

        [TearDown]
        public void TearDown()
        {
            StageUtility.GoToMainStage();
        }

        [Test]
        public void AutoScan_IsDeferredWhilePrefabModeIsOpen_AndResumesAfterClosing()
        {
            // テスト中は監視が一時停止されているため、ここで判定する間だけ外す（予約は入れないので検査は走らない）
            bool suspended = BindingChangeMonitor.IsSuspended;
            BindingChangeMonitor.IsSuspended = false;
            try
            {
                AssertDeferredOnlyInPrefabMode();
            }
            finally
            {
                BindingChangeMonitor.IsSuspended = suspended;
            }
        }

        private void AssertDeferredOnlyInPrefabMode()
        {
            Assert.That(CanProcess(out string reasonBefore), Is.True, reasonBefore);

            PrefabStage stage = PrefabStageUtility.OpenPrefab(_middlePath);
            Assert.That(stage, Is.Not.Null);

            Assert.That(CanProcess(out string reasonDuring), Is.False);
            Assert.That(reasonDuring, Is.EqualTo(Lang.T("engine.defer.prefabMode")));

            StageUtility.GoToMainStage();

            Assert.That(CanProcess(out string reasonAfter), Is.True, reasonAfter);
        }

        private static bool CanProcess(out string reason)
        {
            MethodInfo method = typeof(ScanScheduler).GetMethod("CanProcess", BindingFlags.NonPublic | BindingFlags.Static);
            object[] arguments = { null };
            bool result = (bool)method.Invoke(null, arguments);
            reason = (string)arguments[0];
            return result;
        }
    }
}
