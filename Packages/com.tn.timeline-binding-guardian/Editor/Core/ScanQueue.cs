using System;
using System.Collections.Generic;

namespace TimelineBindingGuardian
{
    public enum ScanTargetKind
    {
        Scene,
        Prefab,
        Timeline,
    }

    public readonly struct ScanRequest
    {
        public ScanRequest(string path, ScanTargetKind kind, string reason, bool isDerived)
        {
            Path = path;
            Kind = kind;
            Reason = reason ?? string.Empty;
            IsDerived = isDerived;
        }

        public string Path { get; }
        public ScanTargetKind Kind { get; }
        public string Reason { get; }
        // 他の変更から派生した再検査（さらに依存先へは広げない）
        public bool IsDerived { get; }
    }

    /// <summary>
    /// 自動検査の待ち行列。Unity API に依存しない（時刻と処理可否は呼び出し側が渡す）。
    /// - 同じパスは 1 件にまとめる（派生でない要求が来たら派生フラグを外す）。
    /// - 最後に追加されてから debounceSeconds 経つまでは取り出さない（保存・インポートが続く間は待つ）。
    /// - 処理できない状態（コンパイル中・Play Mode 中など）では取り出さない。
    /// </summary>
    public sealed class ScanQueue
    {
        private readonly List<ScanRequest> _items = new List<ScanRequest>();
        private readonly double _debounceSeconds;
        private double _lastEnqueueTime = double.NegativeInfinity;

        public ScanQueue(double debounceSeconds)
        {
            _debounceSeconds = debounceSeconds;
        }

        public int Count => _items.Count;

        /// <summary>
        /// 待っている要求（先頭から順）。Domain Reload で失わないよう退避するときに使う。
        /// </summary>
        public IReadOnlyList<ScanRequest> Items => _items;

        public void Enqueue(ScanRequest request, double now)
        {
            _lastEnqueueTime = now;
            int index = _items.FindIndex(i => string.Equals(i.Path, request.Path, StringComparison.Ordinal));
            if (index < 0)
            {
                _items.Add(request);
                return;
            }

            bool upgrade = _items[index].IsDerived && !request.IsDerived;
            if (upgrade)
            {
                _items[index] = request;
            }
        }

        public bool TryDequeue(double now, bool canProcess, out ScanRequest request)
        {
            request = default;
            bool isReady = _items.Count > 0 && now - _lastEnqueueTime >= _debounceSeconds;
            if (!isReady || !canProcess)
            {
                return false;
            }

            request = _items[0];
            _items.RemoveAt(0);
            return true;
        }

        public double GetSecondsUntilReady(double now)
        {
            return Math.Max(0, _debounceSeconds - (now - _lastEnqueueTime));
        }

        public void Clear()
        {
            _items.Clear();
        }
    }
}
