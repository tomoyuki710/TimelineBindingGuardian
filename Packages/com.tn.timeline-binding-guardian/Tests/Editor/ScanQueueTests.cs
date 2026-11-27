using NUnit.Framework;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 自動検査の待ち行列のテスト（デバウンス、重複のまとめ、処理不可時の延期）。
    /// </summary>
    public sealed class ScanQueueTests
    {
        [Test]
        public void Debounce_WaitsUntilNoNewRequestsForInterval()
        {
            ScanQueue queue = new ScanQueue(1.0);
            queue.Enqueue(Request("A.prefab"), 10.0);

            Assert.That(queue.TryDequeue(10.5, true, out ScanRequest _), Is.False);
            queue.Enqueue(Request("B.prefab"), 10.8);
            Assert.That(queue.TryDequeue(11.5, true, out ScanRequest _), Is.False);
            Assert.That(queue.TryDequeue(11.8, true, out ScanRequest first), Is.True);
            Assert.That(first.Path, Is.EqualTo("A.prefab"));
        }

        [Test]
        public void SamePath_IsMerged()
        {
            ScanQueue queue = new ScanQueue(1.0);
            queue.Enqueue(Request("A.prefab"), 0);
            queue.Enqueue(Request("A.prefab"), 0.1);
            queue.Enqueue(Request("B.unity"), 0.2);

            Assert.That(queue.Count, Is.EqualTo(2));
        }

        [Test]
        public void DerivedRequest_IsUpgradedByDirectRequest()
        {
            ScanQueue queue = new ScanQueue(0);
            queue.Enqueue(new ScanRequest("A.prefab", ScanTargetKind.Prefab, "dependency", true), 0);
            queue.Enqueue(new ScanRequest("A.prefab", ScanTargetKind.Prefab, "saved", false), 0);

            queue.TryDequeue(1, true, out ScanRequest request);

            Assert.That(request.IsDerived, Is.False);
            Assert.That(request.Reason, Is.EqualTo("saved"));
        }

        [Test]
        public void CannotProcess_DefersWithoutLosingRequests()
        {
            ScanQueue queue = new ScanQueue(1.0);
            queue.Enqueue(Request("A.prefab"), 0);

            Assert.That(queue.TryDequeue(5, false, out ScanRequest _), Is.False);
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(queue.TryDequeue(6, true, out ScanRequest request), Is.True);
            Assert.That(request.Path, Is.EqualTo("A.prefab"));
            Assert.That(queue.Count, Is.EqualTo(0));
        }

        [Test]
        public void SecondsUntilReady_CountsDownFromLastEnqueue()
        {
            ScanQueue queue = new ScanQueue(1.0);
            queue.Enqueue(Request("A.prefab"), 3.0);

            Assert.That(queue.GetSecondsUntilReady(3.25), Is.EqualTo(0.75).Within(1e-6));
            Assert.That(queue.GetSecondsUntilReady(10), Is.EqualTo(0));
        }

        private static ScanRequest Request(string path)
        {
            return new ScanRequest(path, ScanTargetKind.Prefab, "test", false);
        }
    }
}
