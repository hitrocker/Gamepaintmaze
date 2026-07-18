namespace PaintMaze.Domain
{
    /// <summary>
    /// Difficulty identifiers. UltraHard is retained at value 4 for legacy saves,
    /// resources, and deterministic seeds; visible play uses DifficultyCatalog.Playable.
    /// </summary>
    public enum Difficulty
    {
        Easy,
        Medium,
        Hard,
        ExtraHard,
        UltraHard
    }
}
