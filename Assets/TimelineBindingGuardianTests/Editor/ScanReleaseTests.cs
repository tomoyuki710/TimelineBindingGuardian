using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 作業中は自動検査をせず、区切り（Scene の保存など）で Release() されたときだけ処理する。
    /// 要求は存在しないパスで入れ、待ち合わせ（1 秒）が過ぎる前に確かめるので、実際の検査・記録は走らない。
    /// </summary>
    public sealed class ScanReleaseTests
    {
        private const BindingFlags StaticMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const string FakePrefab = "Assets/TBGScanReleaseTests/Fake.prefab";
        private const string FakeScene = "Assets/TBGScanReleaseTests/Fake.unity";

        private bool _suspended;

        [SetUp]
        public void SetUp()
        {
            // テスト中は監視が一時停止されているため、予約を入れられるよう一時的に外す
            _suspended = BindingChangeMonitor.IsSuspended;
            BindingChangeMonitor.IsSuspended = false;
            ScanScheduler.ClearPending();
            SetReleased(false);
        }

        [TearDown]
        public void TearDown()
        {
            ScanScheduler.ClearPending();
            SetReleased(false);
            BindingChangeMonitor.IsSuspended = _suspended;
        }

        [Test]
        public void Pending_IsNotProcessedUntilReleased()
        {
            Assume.That(GuardianSettings.Instance.MonitoringEnabled, Is.True, "automatic monitoring is off in this project");
            ScanScheduler.Enqueue(FakePrefab, ScanTargetKind.Prefab, "test");

            Update();

            Assert.That(ScanScheduler.PendingCount, Is.EqualTo(1));
            Assert.That(ScanScheduler.DeferReason, Is.EqualTo(Lang.T("engine.defer.waitingForSave")));

            ScanScheduler.Release();
            Update();

            Assert.That(ScanScheduler.DeferReason, Is.Empty, "released: only the 1-second debounce is left");
            Assert.That(ScanScheduler.PendingCount, Is.EqualTo(1));
        }

        [Test]
        public void Release_EndsWhenTheQueueIsEmpty()
        {
            ScanScheduler.Release();

            Update();

            Assert.That(IsReleased(), Is.False);
        }

        [Test]
        public void PendingRequests_SurviveSaveAndRestore()
        {
            Assume.That(GuardianSettings.Instance.MonitoringEnabled, Is.True, "automatic monitoring is off in this project");
            ScanScheduler.Enqueue(FakePrefab, ScanTargetKind.Prefab, "prefab reason");
            ScanScheduler.Enqueue(FakeScene, ScanTargetKind.Scene, "scene reason", true);

            Invoke("SavePending");
            ScanScheduler.ClearPending();
            Invoke("RestorePending");

            ScanRequest[] restored = GetQueue().Items.ToArray();
            Assert.That(restored.Select(r => r.Path), Is.EqualTo(new[] { FakePrefab, FakeScene }));
            Assert.That(restored.Select(r => r.Kind), Is.EqualTo(new[] { ScanTargetKind.Prefab, ScanTargetKind.Scene }));
            Assert.That(restored.Select(r => r.Reason), Is.EqualTo(new[] { "prefab reason", "scene reason" }));
            Assert.That(restored.Select(r => r.IsDerived), Is.EqualTo(new[] { false, true }));
        }

        private static void Update()
        {
            Invoke("OnEditorUpdate");
        }

        private static void Invoke(string name)
        {
            MethodInfo method = typeof(ScanScheduler).GetMethod(name, StaticMembers);
            Assert.That(method, Is.Not.Null, name);
            method.Invoke(null, null);
        }

        private static ScanQueue GetQueue()
        {
            return (ScanQueue)typeof(ScanScheduler).GetField("_queue", StaticMembers).GetValue(null);
        }

        private static bool IsReleased()
        {
            return (bool)typeof(ScanScheduler).GetField("_released", StaticMembers).GetValue(null);
        }

        private static void SetReleased(bool value)
        {
            typeof(ScanScheduler).GetField("_released", StaticMembers).SetValue(null, value);
        }
    }
}
