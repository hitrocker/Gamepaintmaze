using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PaintMaze.Core;
using PaintMaze.Domain;
using UnityEngine;

namespace PaintMaze.Services
{
    [Serializable]
    public sealed class ActiveLevelCell
    {
        public int row;
        public int col;

        public ActiveLevelCell(int row, int col)
        {
            this.row = row;
            this.col = col;
        }

        public Position ToPosition() => new(row, col);
    }

    [Serializable]
    public sealed class ActiveLevelSnapshot
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public string owner;
        public int difficulty;
        public int visibleIndex;
        public int catalogOrderVersion;
        public string levelFingerprint;
        public int ballRow;
        public int ballCol;
        public int moveCount;
        public List<ActiveLevelCell> painted = new();

        public static ActiveLevelSnapshot From(GameState state, string progressOwner)
        {
            if (state == null || state.Level == null || state.MoveCount <= 0 ||
                state.IsComplete)
                return null;

            var ordered = new List<Position>(state.Painted);
            ordered.Sort((a, b) =>
            {
                int row = a.Row.CompareTo(b.Row);
                return row != 0 ? row : a.Col.CompareTo(b.Col);
            });

            var snapshot = new ActiveLevelSnapshot
            {
                owner = NormalizeOwner(progressOwner),
                difficulty = (int)state.Level.Difficulty,
                visibleIndex = state.Level.Index,
                catalogOrderVersion = LevelCatalogOrder.OrderVersion,
                levelFingerprint =
                    LevelFingerprint.HashFor(state.Level).ToString("X16"),
                ballRow = state.BallPos.Row,
                ballCol = state.BallPos.Col,
                moveCount = state.MoveCount
            };
            foreach (Position position in ordered)
                snapshot.painted.Add(
                    new ActiveLevelCell(position.Row, position.Col));
            return snapshot;
        }

        public bool TryCreateState(Level level, out GameState state)
        {
            state = null;
            if (level == null || painted == null || moveCount <= 0) return false;
            var positions = new List<Position>(painted.Count);
            foreach (ActiveLevelCell cell in painted)
            {
                if (cell == null) return false;
                positions.Add(cell.ToPosition());
            }
            return GameState.TryRestore(
                level,
                new Position(ballRow, ballCol),
                positions,
                moveCount,
                out state);
        }

        internal static string NormalizeOwner(string value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    /// <summary>
    /// Versioned local persistence for committed, unfinished gameplay sessions.
    /// Snapshots are scoped by progress owner and difficulty and never cloud-synced.
    /// </summary>
    public sealed class ActiveLevelSessionStore
    {
        private readonly string _root;

        public ActiveLevelSessionStore(string rootDirectory = null)
        {
            _root = rootDirectory ?? Path.Combine(
                Application.persistentDataPath, "active-level", "v1");
        }

        public string Root => _root;

        public void Save(ActiveLevelSnapshot snapshot)
        {
            if (snapshot == null || snapshot.version != ActiveLevelSnapshot.CurrentVersion ||
                snapshot.painted == null || snapshot.moveCount <= 0)
                return;

            string path = SnapshotPath(
                snapshot.owner, (Difficulty)snapshot.difficulty);
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string tempPath = path + ".tmp";
            string payload = JsonUtility.ToJson(snapshot);
            File.WriteAllText(tempPath, payload, Encoding.UTF8);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tempPath, path);
        }

        public bool TryLoad(
            string progressOwner,
            Level gameplayLevel,
            out ActiveLevelSnapshot snapshot,
            out GameState restoredState)
        {
            snapshot = null;
            restoredState = null;
            if (gameplayLevel == null ||
                !DifficultyCatalog.IsPlayable(gameplayLevel.Difficulty))
                return false;

            string normalizedOwner =
                ActiveLevelSnapshot.NormalizeOwner(progressOwner);
            string path = SnapshotPath(
                normalizedOwner, gameplayLevel.Difficulty);
            if (!File.Exists(path)) return false;

            try
            {
                ActiveLevelSnapshot loaded =
                    JsonUtility.FromJson<ActiveLevelSnapshot>(
                        File.ReadAllText(path, Encoding.UTF8));
                if (!Matches(loaded, normalizedOwner, gameplayLevel) ||
                    !loaded.TryCreateState(gameplayLevel, out GameState state))
                {
                    DeleteBestEffort(path);
                    return false;
                }

                snapshot = loaded;
                restoredState = state;
                return true;
            }
            catch (Exception)
            {
                DeleteBestEffort(path);
                return false;
            }
        }

        public void Clear(string progressOwner, Difficulty difficulty)
        {
            DeleteBestEffort(SnapshotPath(progressOwner, difficulty));
        }

        public void ClearOwner(string progressOwner)
        {
            string directory = OwnerDirectory(progressOwner);
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
            catch (Exception)
            {
                // Best-effort cleanup. Owner validation prevents cross-account restore.
            }
        }

        public string PathFor(string progressOwner, Difficulty difficulty) =>
            SnapshotPath(progressOwner, difficulty);

        private static bool Matches(
            ActiveLevelSnapshot snapshot,
            string normalizedOwner,
            Level gameplayLevel)
        {
            if (snapshot == null ||
                snapshot.version != ActiveLevelSnapshot.CurrentVersion ||
                ActiveLevelSnapshot.NormalizeOwner(snapshot.owner) != normalizedOwner ||
                snapshot.difficulty != (int)gameplayLevel.Difficulty ||
                snapshot.visibleIndex != gameplayLevel.Index ||
                snapshot.moveCount <= 0)
                return false;

            int bakedCount =
                DifficultyCatalog.BakedCountFor(gameplayLevel.Difficulty);
            if (gameplayLevel.Index <= bakedCount &&
                snapshot.catalogOrderVersion != LevelCatalogOrder.OrderVersion)
                return false;

            string expected =
                LevelFingerprint.HashFor(gameplayLevel).ToString("X16");
            return string.Equals(
                snapshot.levelFingerprint, expected,
                StringComparison.OrdinalIgnoreCase);
        }

        private string SnapshotPath(
            string progressOwner, Difficulty difficulty) =>
            Path.Combine(
                OwnerDirectory(progressOwner),
                ((int)difficulty).ToString() + ".json");

        private string OwnerDirectory(string progressOwner) =>
            Path.Combine(
                _root,
                OwnerHash(ActiveLevelSnapshot.NormalizeOwner(progressOwner)));

        private static string OwnerHash(string owner)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            string value = string.IsNullOrEmpty(owner) ? "guest" : owner;
            unchecked
            {
                foreach (char c in value)
                {
                    hash ^= (byte)c;
                    hash *= prime;
                    hash ^= (byte)(c >> 8);
                    hash *= prime;
                }
            }
            return hash.ToString("X16");
        }

        private static void DeleteBestEffort(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                string tempPath = path + ".tmp";
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch (Exception)
            {
                // Invalid snapshots must never block gameplay.
            }
        }
    }
}
