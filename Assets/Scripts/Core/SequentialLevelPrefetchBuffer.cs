using System;
using PaintMaze.Domain;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace PaintMaze.Core
{
    /// <summary>
    /// Maintains a deterministic, sequential look-ahead window. The next slot must be
    /// ready before the following slot is requested, preventing two expensive mobile
    /// generation jobs from being launched by one window.
    /// </summary>
    public sealed class SequentialLevelPrefetchBuffer
    {
        public const int DefaultDepth = 2;

        private readonly IPrefetchLevelProvider _provider;
        private readonly int _depth;
        private int _generation;

        public sealed class Request
        {
            internal int Generation;
            internal int Offset = 1;
            internal bool Started;
            internal long StartedAt;

            public Difficulty Difficulty { get; internal set; }
            public int CurrentIndex { get; internal set; }
            public bool IsComplete { get; internal set; }
            public int TargetIndex { get; internal set; }
        }

        public SequentialLevelPrefetchBuffer(
            IPrefetchLevelProvider provider,
            int depth = DefaultDepth)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _depth = Math.Max(1, depth);
        }

        public Request Begin(Difficulty difficulty, int currentIndex)
        {
            int clamped = Math.Max(
                1, Math.Min(currentIndex, DifficultyConfig.PracticalMaxLevel));
            return new Request
            {
                Generation = ++_generation,
                Difficulty = difficulty,
                CurrentIndex = clamped
            };
        }

        public void Cancel()
        {
            _generation++;
        }

        /// <summary>
        /// Advances one non-blocking step. Returns true when the request completed or was
        /// superseded. Call once per frame while it returns false.
        /// </summary>
        public bool Pump(Request request)
        {
            if (request == null || request.Generation != _generation)
                return true;
            if (request.IsComplete)
                return true;

            while (request.Offset <= _depth)
            {
                long candidate = (long)request.CurrentIndex + request.Offset;
                int target = (int)Math.Min(
                    DifficultyConfig.PracticalMaxLevel, candidate);
                if (target <= request.CurrentIndex ||
                    (!request.Started &&
                     request.TargetIndex > 0 &&
                     target <= request.TargetIndex))
                {
                    request.Offset++;
                    continue;
                }

                if (!request.Started)
                {
                    request.TargetIndex = target;
                    request.StartedAt = Stopwatch.GetTimestamp();
                    request.Started = true;
                    Debug.Log(
                        $"[Perf] aheadPrefetchStart difficulty={request.Difficulty} " +
                        $"current={request.CurrentIndex} target={target} depth={request.Offset}");
                    _provider.Prefetch(request.Difficulty, target);
                }

                if (!_provider.IsReady(request.Difficulty, target))
                    return false;

                double elapsedMs =
                    (Stopwatch.GetTimestamp() - request.StartedAt) * 1000.0 /
                    Stopwatch.Frequency;
                Debug.Log(
                    $"[Perf] aheadPrefetchReady difficulty={request.Difficulty} " +
                    $"current={request.CurrentIndex} target={target} depth={request.Offset} " +
                    $"elapsedMs={elapsedMs:F1}");
                request.Offset++;
                request.Started = false;
                request.StartedAt = 0;
            }

            request.IsComplete = true;
            return true;
        }
    }
}
