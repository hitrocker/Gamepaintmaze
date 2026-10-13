using PaintMaze.Domain;

namespace PaintMaze.Services
{
    /// <summary>
    /// Static cosmetic-coin payout table. Campaign grants are first-clear only;
    /// daily podium grants settle from the previous UTC day's final rank.
    /// </summary>
    public static class CoinEconomy
    {
        public const int DailyBase = 10;
        public const int PodiumFirst = 50;
        public const int PodiumSecond = 40;
        public const int PodiumThird = 30;
        public const int HintCost = 50;

        public static int Campaign(Difficulty difficulty)
        {
            return DifficultyCatalog.NormalizePlayable(difficulty) switch
            {
                Difficulty.Easy => 5,
                Difficulty.Medium => 10,
                Difficulty.Hard => 15,
                Difficulty.ExtraHard => 20,
                _ => 5
            };
        }

        public static int Podium(int rank) =>
            rank switch
            {
                1 => PodiumFirst,
                2 => PodiumSecond,
                3 => PodiumThird,
                _ => 0
            };
    }
}
