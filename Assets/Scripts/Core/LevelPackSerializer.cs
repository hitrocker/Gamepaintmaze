using System.Text;
using PaintMaze.Domain;

namespace PaintMaze.Core
{
    /// <summary>Canonical text serialization shared by baked packs and runtime cache.</summary>
    public static class LevelPackSerializer
    {
        public static string ToBlock(Level level)
        {
            var sb = new StringBuilder();
            sb.Append(DifficultyConfig.For(level.Difficulty).DisplayName)
                .Append(' ').Append(level.Index).Append('\n');
            for (int r = 0; r < level.Rows; r++)
            {
                for (int c = 0; c < level.Cols; c++)
                {
                    if (level.Spawn.Row == r && level.Spawn.Col == c)
                    {
                        sb.Append('S');
                        continue;
                    }

                    sb.Append(level.Grid[r, c] switch
                    {
                        Tile.Floor => '.',
                        Tile.Wall => '#',
                        Tile.Void => '_',
                        _ => '_'
                    });
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        public static string ToPack(System.Collections.Generic.IEnumerable<Level> levels)
        {
            var snapshot = new System.Collections.Generic.List<Level>(levels);
            int version = snapshot.Count > 0
                ? LevelGenerator.VersionFor(snapshot[0].Difficulty)
                : LevelGenerator.GeneratorVersion;
            var sb = new StringBuilder();
            sb.AppendLine("# PaintMaze deterministic level section")
                .Append("# generator-version=").Append(version).AppendLine()
                .AppendLine("# S=spawn .=floor #=wall _=void")
                .AppendLine();
            foreach (Level level in snapshot)
                sb.Append(ToBlock(level)).AppendLine();
            return sb.ToString();
        }
    }
}
