using System.Collections.Generic;
using NUnit.Framework;
using PaintMaze.Core;
using PaintMaze.Domain;
using UnityEngine;

namespace PaintMaze.Tests
{
    public class BakedLevelCatalogTests
    {
        private readonly Solver _solver = new Solver();
        private readonly LevelQualityScorer _scorer = new LevelQualityScorer();

        [Test]
        public void AllBakedSections_HaveExactCountsAndQuality()
        {
            var provider = new SectionedLevelProvider();
            var fingerprints = new HashSet<ulong>();
            int total = 0;

            foreach (Difficulty difficulty in EditModeTestSupport.PlayableDifficulties)
            {
                for (int index = 1; index <= provider.BakedCountFor(difficulty); index++)
                {
                    Level level = provider.GetLevel(difficulty, index);
                    Assert.IsNotNull(level, $"{difficulty} #{index}");
                    Assert.AreEqual(difficulty, level.Difficulty);
                    Assert.AreEqual(index, level.Index);

                    LevelQualityMetrics metrics = _scorer.Score(level, difficulty);
                    Assert.IsTrue(_solver.IsAlwaysSolvable(level), $"{difficulty} #{index}");
                    Assert.IsTrue(LevelBakeValidator.MeetsRequirements(level, _solver, metrics, out string reason),
                        $"{difficulty} #{index}: {reason}");

                    ulong hash = LevelFingerprint.From(level).Hash;
                    Assert.IsTrue(fingerprints.Add(hash), $"{difficulty} #{index} duplicate fingerprint {hash:X16}");
                    total++;
                }
            }

            Assert.AreEqual(6500, total);
        }

        [Test]
        public void ResourcesRetain_ExpectedSectionsPerSourceMode()
        {
            foreach (Difficulty difficulty in EditModeTestSupport.AllDifficulties)
            {
                int sourceCount = difficulty == Difficulty.UltraHard ? 4500 : 500;
                int sections = 0;
                for (int start = 1;
                     start <= sourceCount;
                     start += SectionedLevelProvider.SectionSize)
                {
                    int end = SectionedLevelProvider.SectionEndFor(start);
                    string slug = difficulty switch
                    {
                        Difficulty.Easy => "easy",
                        Difficulty.Medium => "medium",
                        Difficulty.Hard => "hard",
                        Difficulty.ExtraHard => "extrahard",
                        Difficulty.UltraHard => "ultrahard",
                        _ => "easy"
                    };
                    string path = $"Levels/Baked/{slug}_{start:D4}_{end:D4}";
                    var asset = Resources.Load<TextAsset>(path);
                    Assert.IsNotNull(asset, path);
                    var parsed = LevelParser.Parse(asset.text, difficulty);
                    Assert.AreEqual(SectionedLevelProvider.SectionSize, parsed.Count, path);
                    sections++;
                }

                Assert.AreEqual(
                    sourceCount / SectionedLevelProvider.SectionSize,
                    sections,
                    difficulty.ToString());
            }
        }
    }
}
