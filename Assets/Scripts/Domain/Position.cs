using System;

namespace PaintMaze.Domain
{
    /// <summary>
    /// Immutable grid coordinate (row, col). Value-equality so it can be used as
    /// a dictionary/hash-set key for painted tiles and solver states.
    /// </summary>
    public readonly struct Position : IEquatable<Position>
    {
        public readonly int Row;
        public readonly int Col;

        public Position(int row, int col)
        {
            Row = row;
            Col = col;
        }

        public Position Offset(int dRow, int dCol) => new Position(Row + dRow, Col + dCol);

        public bool Equals(Position other) => Row == other.Row && Col == other.Col;

        public override bool Equals(object obj) => obj is Position other && Equals(other);

        public override int GetHashCode() => unchecked((Row * 397) ^ Col);

        public static bool operator ==(Position a, Position b) => a.Equals(b);

        public static bool operator !=(Position a, Position b) => !a.Equals(b);

        public override string ToString() => $"({Row},{Col})";
    }
}
