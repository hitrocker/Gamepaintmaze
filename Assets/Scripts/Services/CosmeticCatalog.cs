using System;
using System.Collections.Generic;
using System.Linq;

namespace PaintMaze.Services
{
    public enum SkinCategory
    {
        Sports = 0,
        SolarSystem = 1
    }

    public sealed class BallSkinDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public int Price { get; }
        public SkinCategory Category { get; }

        public BallSkinDefinition(string id, string displayName, int price, SkinCategory category)
        {
            Id = id;
            DisplayName = displayName;
            Price = price;
            Category = category;
        }
    }

    /// <summary>
    /// Authoritative catalog for cosmetic ball skins. Save data stores only IDs;
    /// definitions and prices always come from this table.
    /// </summary>
    public static class CosmeticCatalog
    {
        public const int SportsBallPrice = 500;

        private static readonly BallSkinDefinition[] Definitions =
        {
            new(SaveService.DefaultSkinId, "Classic", 0, SkinCategory.Sports),
            new("cricket", "Cricket", SportsBallPrice, SkinCategory.Sports),
            new("soccer", "Soccer", SportsBallPrice, SkinCategory.Sports),
            new("basketball", "Basketball", SportsBallPrice, SkinCategory.Sports),
            new("baseball", "Baseball", SportsBallPrice, SkinCategory.Sports),
            new("tennis", "Tennis", SportsBallPrice, SkinCategory.Sports),
            new("volleyball", "Volleyball", SportsBallPrice, SkinCategory.Sports),
            new("golf", "Golf", SportsBallPrice, SkinCategory.Sports),
            new("bowling", "Bowling", SportsBallPrice, SkinCategory.Sports),
            new("eightball", "8-Ball", SportsBallPrice, SkinCategory.Sports),
            new("beachball", "Beach Ball", SportsBallPrice, SkinCategory.Sports),
            new("wiffle", "Wiffle", SportsBallPrice, SkinCategory.Sports),
            new("spikeball", "Spikeball", SportsBallPrice, SkinCategory.Sports),
            new("dodgeball", "Dodgeball", SportsBallPrice, SkinCategory.Sports),
            new("takraw", "Takraw", SportsBallPrice, SkinCategory.Sports),
            new("sun", "Sun", SportsBallPrice, SkinCategory.SolarSystem),
            new("mercury", "Mercury", SportsBallPrice, SkinCategory.SolarSystem),
            new("venus", "Venus", SportsBallPrice, SkinCategory.SolarSystem),
            new("earth", "Earth", SportsBallPrice, SkinCategory.SolarSystem),
            new("moon", "Moon", SportsBallPrice, SkinCategory.SolarSystem),
            new("mars", "Mars", SportsBallPrice, SkinCategory.SolarSystem),
            new("jupiter", "Jupiter", SportsBallPrice, SkinCategory.SolarSystem),
            new("saturn", "Saturn", SportsBallPrice, SkinCategory.SolarSystem),
            new("uranus", "Uranus", SportsBallPrice, SkinCategory.SolarSystem),
            new("neptune", "Neptune", SportsBallPrice, SkinCategory.SolarSystem),
            new("pluto", "Pluto", SportsBallPrice, SkinCategory.SolarSystem)
        };

        public static readonly IReadOnlyList<SkinCategory> Categories = new[]
        {
            SkinCategory.Sports,
            SkinCategory.SolarSystem
        };

        public static string CategoryLabel(SkinCategory category) =>
            category switch
            {
                SkinCategory.SolarSystem => "SOLAR SYSTEM",
                _ => "SPORTS"
            };

        private static readonly Dictionary<string, BallSkinDefinition> ById =
            Definitions.ToDictionary(
                definition => definition.Id,
                definition => definition,
                StringComparer.Ordinal);

        public static IReadOnlyList<BallSkinDefinition> All => Definitions;

        public static bool TryGet(string skinId, out BallSkinDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(skinId))
            {
                definition = null;
                return false;
            }

            return ById.TryGetValue(skinId.Trim(), out definition);
        }

        public static BallSkinDefinition GetOrClassic(string skinId) =>
            TryGet(skinId, out BallSkinDefinition definition)
                ? definition
                : ById[SaveService.DefaultSkinId];
    }
}
