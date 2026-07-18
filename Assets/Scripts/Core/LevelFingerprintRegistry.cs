using System.Collections.Generic;

namespace PaintMaze.Core
{
    /// <summary>
    /// Tracks accepted canonical fingerprints for global bake deduplication.
    /// </summary>
    public sealed class LevelFingerprintRegistry
    {
        private readonly Dictionary<ulong, List<byte[]>> _entries = new();

        public int Count { get; private set; }

        public bool IsDuplicate(LevelFingerprint fingerprint)
        {
            if (!_entries.TryGetValue(fingerprint.Hash, out List<byte[]> bucket))
                return false;

            foreach (byte[] existing in bucket)
            {
                if (LevelFingerprint.CanonicalBytesEqual(existing, fingerprint.CanonicalBytes))
                    return true;
            }

            return false;
        }

        public void Register(LevelFingerprint fingerprint)
        {
            if (!_entries.TryGetValue(fingerprint.Hash, out List<byte[]> bucket))
            {
                bucket = new List<byte[]>();
                _entries[fingerprint.Hash] = bucket;
            }

            bucket.Add(fingerprint.CanonicalBytes);
            Count++;
        }

        public bool TryRegister(LevelFingerprint fingerprint)
        {
            if (IsDuplicate(fingerprint))
                return false;

            Register(fingerprint);
            return true;
        }
    }
}
