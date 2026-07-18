using PaintMaze.Domain;

namespace PaintMaze.Core
{
    /// <summary>
    /// Optional in-memory retention policy for providers that persist generated levels.
    /// Disk-cached levels are unaffected and can be hydrated again when requested.
    /// </summary>
    public interface ILevelMemoryWindow
    {
        void RetainMemoryWindow(
            Difficulty difficulty,
            int currentIndex,
            int levelsBehind,
            int levelsAhead);
    }
}
