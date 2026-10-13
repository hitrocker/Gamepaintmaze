using System;
using UnityEngine;

namespace PaintMaze.Services
{
    /// <summary>Build-specific feature gate for the interactive macOS screenshot player.</summary>
    public static class ScreenshotPlayerMode
    {
        public static bool IsActive => IsScreenshotProduct(Application.productName);

        public static bool IsScreenshotProduct(string productName) =>
            !string.IsNullOrEmpty(productName) &&
            productName.IndexOf("Screenshot", StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool HasCommandLineArgument(string[] arguments, string name)
        {
            if (arguments == null || string.IsNullOrEmpty(name)) return false;
            return Array.IndexOf(arguments, name) >= 0;
        }
    }
}
