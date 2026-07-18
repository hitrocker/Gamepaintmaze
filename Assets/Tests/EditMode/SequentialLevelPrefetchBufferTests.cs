using System.Collections.Generic;
using NUnit.Framework;
using PaintMaze.Core;
using PaintMaze.Domain;

namespace PaintMaze.Tests
{
    public class SequentialLevelPrefetchBufferTests
    {
        private sealed class FakePrefetchProvider : IPrefetchLevelProvider
        {
            public readonly List<(Difficulty difficulty, int index)> Requests = new();
            private readonly HashSet<(Difficulty difficulty, int index)> _ready = new();

            public void Prefetch(Difficulty difficulty, int index)
            {
                Requests.Add((difficulty, index));
            }

            public bool IsReady(Difficulty difficulty, int index) =>
                _ready.Contains((difficulty, index));

            public void MarkReady(Difficulty difficulty, int index)
            {
                _ready.Add((difficulty, index));
            }
        }

        [Test]
        public void Pump_RequestsTwoLevelsSequentially()
        {
            var provider = new FakePrefetchProvider();
            var buffer = new SequentialLevelPrefetchBuffer(provider);
            SequentialLevelPrefetchBuffer.Request request =
                buffer.Begin(Difficulty.ExtraHard, 2000);

            Assert.IsFalse(buffer.Pump(request));
            CollectionAssert.AreEqual(
                new[] { (Difficulty.ExtraHard, 2001) }, provider.Requests);

            Assert.IsFalse(buffer.Pump(request));
            Assert.AreEqual(1, provider.Requests.Count, "in-flight slot must not be requested twice");

            provider.MarkReady(Difficulty.ExtraHard, 2001);
            Assert.IsFalse(buffer.Pump(request));
            CollectionAssert.AreEqual(
                new[]
                {
                    (Difficulty.ExtraHard, 2001),
                    (Difficulty.ExtraHard, 2002)
                },
                provider.Requests);

            provider.MarkReady(Difficulty.ExtraHard, 2002);
            Assert.IsTrue(buffer.Pump(request));
            Assert.IsTrue(request.IsComplete);
        }

        [Test]
        public void Begin_SupersedesOlderRequestWithoutSchedulingItsSecondSlot()
        {
            var provider = new FakePrefetchProvider();
            var buffer = new SequentialLevelPrefetchBuffer(provider);
            SequentialLevelPrefetchBuffer.Request stale =
                buffer.Begin(Difficulty.ExtraHard, 2000);
            Assert.IsFalse(buffer.Pump(stale));

            SequentialLevelPrefetchBuffer.Request current =
                buffer.Begin(Difficulty.ExtraHard, 3000);
            provider.MarkReady(Difficulty.ExtraHard, 2001);

            Assert.IsTrue(buffer.Pump(stale));
            Assert.IsFalse(buffer.Pump(current));
            CollectionAssert.AreEqual(
                new[]
                {
                    (Difficulty.ExtraHard, 2001),
                    (Difficulty.ExtraHard, 3001)
                },
                provider.Requests);
        }

        [Test]
        public void PracticalMaximum_DoesNotRequestDuplicateIndices()
        {
            var provider = new FakePrefetchProvider();
            var buffer = new SequentialLevelPrefetchBuffer(provider);
            int current = DifficultyConfig.PracticalMaxLevel - 1;
            SequentialLevelPrefetchBuffer.Request request =
                buffer.Begin(Difficulty.ExtraHard, current);

            Assert.IsFalse(buffer.Pump(request));
            provider.MarkReady(Difficulty.ExtraHard, DifficultyConfig.PracticalMaxLevel);
            Assert.IsTrue(buffer.Pump(request));
            CollectionAssert.AreEqual(
                new[] { (Difficulty.ExtraHard, DifficultyConfig.PracticalMaxLevel) },
                provider.Requests);
        }
    }
}
