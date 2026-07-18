namespace PaintMaze.Game
{
    /// <summary>
    /// Tracks one progress-hint session independently from movement animation timing.
    /// Guidance survives painted-only moves and ends at the first newly painted tile.
    /// </summary>
    public sealed class ProgressHintSession
    {
        public bool Active { get; private set; }
        public bool FoundNewTile { get; private set; }

        public void Begin()
        {
            Active = true;
            FoundNewTile = false;
        }

        public void ObserveTile(bool newlyPainted)
        {
            if (Active && newlyPainted)
                FoundNewTile = true;
        }

        public bool ShouldEndAtRest(bool levelComplete) =>
            Active && (FoundNewTile || levelComplete);

        public bool ShouldRefreshAtRest(bool levelComplete) =>
            Active && !FoundNewTile && !levelComplete;

        public void End()
        {
            Active = false;
            FoundNewTile = false;
        }
    }
}
