using System.Globalization;
using UnityEngine;

namespace PaintMaze.Services
{
    /// <summary>
    /// Debug-only helpers for on-device / emulator testing. Release players
    /// never call these; AppRoot gates them with Debug.isDebugBuild.
    /// </summary>
    public static class DeviceTestSupport
    {
        public const int DebugCoinFloor = 50_000;

        /// <summary>
        /// Floors a debug balance at 50k so shop skins can be bought. A
        /// positive <paramref name="rawProperty"/> overrides the floor.
        /// A higher existing balance is kept.
        /// </summary>
        public static int ResolveDebugCoinGrant(string rawProperty, int currentCoins)
        {
            int current = Mathf.Max(0, currentCoins);
            if (int.TryParse(
                    rawProperty?.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int parsed) &&
                parsed > 0)
            {
                int wanted = Mathf.Min(parsed, SaveService.MaxCoins);
                return Mathf.Max(current, wanted);
            }

            return Mathf.Max(current, DebugCoinFloor);
        }
    }
}
