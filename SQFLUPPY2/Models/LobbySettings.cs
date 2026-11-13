namespace SQFLUPPY2.Models
{
    public static class LobbyDifficulty
    {
        public const string Easy = "easy";
        public const string Normal = "normal";
        public const string Hard = "hard";

        public static bool IsValid(string? value) => value is Easy or Normal or Hard;
    }

    public static class LobbyBiomeMode
    {
        public const string Auto = "auto";
        public const string Fixed = "fixed";

        public static bool IsValid(string? value) => value is Auto or Fixed;
    }

    /// <summary>
    /// Настройки лобби, которые хост может менять до старта раунда.
    /// </summary>
    public sealed class LobbySettings
    {
        public const int BiomeCount = 10;

        public string Difficulty { get; set; } = LobbyDifficulty.Normal;
        public string BiomeMode { get; set; } = LobbyBiomeMode.Auto;
        public int BiomeIndex { get; set; }

        public bool IsValid() =>
            LobbyDifficulty.IsValid(Difficulty)
            && LobbyBiomeMode.IsValid(BiomeMode)
            && BiomeIndex >= 0
            && BiomeIndex < BiomeCount;

        public LobbySettings Clone() => new()
        {
            Difficulty = Difficulty,
            BiomeMode = BiomeMode,
            BiomeIndex = BiomeIndex
        };

        /// <summary>
        /// Применяет присланные клиентом настройки, подставляя значения по умолчанию для некорректных полей.
        /// </summary>
        public void Apply(string? difficulty, string? biomeMode, int? biomeIndex)
        {
            Difficulty = LobbyDifficulty.IsValid(difficulty) ? difficulty! : LobbyDifficulty.Normal;
            BiomeMode = LobbyBiomeMode.IsValid(biomeMode) ? biomeMode! : LobbyBiomeMode.Auto;
            BiomeIndex = biomeIndex is >= 0 and < BiomeCount ? biomeIndex.Value : 0;
        }
    }
}
