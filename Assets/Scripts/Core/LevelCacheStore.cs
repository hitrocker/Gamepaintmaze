using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PaintMaze.Domain;
using UnityEngine;

namespace PaintMaze.Core
{
    /// <summary>
    /// Versioned per-level on-disk cache for runtime-generated levels.
    /// Unity path capture happens in the constructor; cache IO is pure System.IO.
    /// </summary>
    public sealed class LevelCacheStore
    {
        private readonly string _root;
        private readonly Solver _solver;

        public LevelCacheStore(string rootDirectory = null, Solver solver = null)
        {
            string basePath = rootDirectory ?? Application.persistentDataPath;
            // Keep lower modes in their unchanged v3 namespace. Extra Hard files share
            // this location but carry a v6 header and are rejected individually if stale.
            _root = Path.Combine(basePath, "generated-levels", $"v{LevelGenerator.LegacyGeneratorVersion}");
            _solver = solver ?? new Solver();
        }

        public string Root => _root;

        public bool TryLoadLevel(Difficulty difficulty, int index,
            out Level level, List<string> errors = null)
        {
            level = null;
            string path = LevelPath(difficulty, index);
            if (!File.Exists(path)) return false;

            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                if (!HasCurrentGeneratorVersion(text, difficulty))
                {
                    DeleteCorrupt(path);
                    return false;
                }

                var parsed = LevelParser.Parse(text, difficulty, errors);
                if (parsed.Count != 1)
                {
                    DeleteCorrupt(path);
                    return false;
                }

                Level parsedLevel = parsed[0];
                var loaded = new Level(
                    parsedLevel.Grid, parsedLevel.Spawn, difficulty, index);
                if (!LevelSafetyValidator.IsSafe(loaded, _solver, out string failureReason))
                {
                    errors?.Add(
                        $"{difficulty} #{index}: cache safety validation failed ({failureReason})");
                    DeleteCorrupt(path);
                    return false;
                }

                level = loaded;
                return true;
            }
            catch (Exception)
            {
                DeleteCorrupt(path);
                return false;
            }
        }

        public void SaveLevel(Difficulty difficulty, int index, Level level)
        {
            if (level == null) return;

            string path = LevelPath(difficulty, index);
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string tempPath = path + ".tmp";
            string payload = LevelPackSerializer.ToPack(new[] { level });
            File.WriteAllText(tempPath, payload, Encoding.UTF8);

            if (File.Exists(path))
                File.Delete(path);
            File.Move(tempPath, path);
        }

        private string LevelPath(Difficulty difficulty, int index)
        {
            string fileName = $"{ModeSlug(difficulty)}_{index:D10}.txt";
            return Path.Combine(_root, ModeSlug(difficulty), fileName);
        }

        private static void DeleteCorrupt(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception)
            {
                // Best-effort cleanup; caller regenerates on miss.
            }
        }

        private static bool HasCurrentGeneratorVersion(string text, Difficulty difficulty)
        {
            const string prefix = "# generator-version=";
            using var reader = new StringReader(text);
            for (int i = 0; i < 5; i++)
            {
                string line = reader.ReadLine();
                if (line == null) break;
                if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
                return line == prefix + LevelGenerator.VersionFor(difficulty);
            }
            return false;
        }

        private static string ModeSlug(Difficulty difficulty) => difficulty switch
        {
            Difficulty.Easy => "easy",
            Difficulty.Medium => "medium",
            Difficulty.Hard => "hard",
            Difficulty.ExtraHard => "extrahard",
            Difficulty.UltraHard => "ultrahard",
            _ => "easy"
        };
    }
}
