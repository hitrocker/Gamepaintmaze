namespace PaintMaze.Domain
{
    /// <summary>
    /// A board cell. The ball rolls across <see cref="Floor"/> tiles (painting
    /// them) and stops against <see cref="Wall"/>, <see cref="Void"/>, or the board edge.
    /// Floors and Walls render on the same unified board plane with different
    /// material states; Void is unrendered exterior space.
    /// The spawn cell is a normal Floor tile; the spawn position is stored
    /// separately on <see cref="Level"/>.
    /// </summary>
    public enum Tile
    {
        Floor,
        Wall,
        Void
    }
}
