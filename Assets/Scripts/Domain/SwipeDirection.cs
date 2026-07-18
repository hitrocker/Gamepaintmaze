namespace PaintMaze.Domain
{
    /// <summary>The four swipe inputs that roll the ball.</summary>
    public enum SwipeDirection
    {
        Up,
        Down,
        Left,
        Right
    }

    public static class SwipeDirectionExtensions
    {
        /// <summary>Row delta for a direction (Up decreases row).</summary>
        public static int DRow(this SwipeDirection d) => d switch
        {
            SwipeDirection.Up => -1,
            SwipeDirection.Down => 1,
            _ => 0
        };

        /// <summary>Column delta for a direction (Left decreases col).</summary>
        public static int DCol(this SwipeDirection d) => d switch
        {
            SwipeDirection.Left => -1,
            SwipeDirection.Right => 1,
            _ => 0
        };

        public static readonly SwipeDirection[] All =
        {
            SwipeDirection.Up,
            SwipeDirection.Down,
            SwipeDirection.Left,
            SwipeDirection.Right
        };
    }
}
