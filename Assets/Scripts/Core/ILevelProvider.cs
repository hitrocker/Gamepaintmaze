using PaintMaze.Domain;

namespace PaintMaze.Core
{
    /// <summary>Supplies a level for a given mode and 1-based index.</summary>
    public interface ILevelProvider
    {
        /// <summary>Levels bundled locally before deterministic runtime generation begins.</summary>
        int BakedCountFor(Difficulty difficulty);

        /// <summary>Returns a level for any supported positive index.</summary>
        Level GetLevel(Difficulty difficulty, int index);
    }
}
