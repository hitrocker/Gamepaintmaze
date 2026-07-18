using PaintMaze.Domain;

namespace PaintMaze.Core
{
    /// <summary>Optional background warmup for upcoming level indices.</summary>
    public interface IPrefetchLevelProvider
    {
        /// <summary>
        /// Begins loading or generating the requested level without blocking the caller.
        /// Safe to call for any positive index; no-ops when the level is already ready.
        /// </summary>
        void Prefetch(Difficulty difficulty, int index);

        /// <summary>True when GetLevel can return without loading or generation work.</summary>
        bool IsReady(Difficulty difficulty, int index);
    }
}
