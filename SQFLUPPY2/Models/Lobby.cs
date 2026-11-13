using System.Text.Json.Serialization;

namespace SQFLUPPY2.Models
{
    public enum LobbyPhase
    {
        Waiting,
        Playing
    }

    public sealed class LobbyPlayer
    {
        public string Nickname { get; set; } = "";
        public string? ConnectionId { get; set; }
        public bool IsAlive { get; set; }
        public int Score { get; set; }
        public int TotalScore { get; set; }
        public float X { get; set; }
        public float Y { get; set; }

        /// <summary>
        /// До какого момента место в лобби удерживается за игроком после обрыва связи.
        /// </summary>
        public DateTimeOffset? ReservedUntil { get; set; }

        [JsonIgnore]
        public bool IsConnected => ConnectionId is not null;

        [JsonIgnore]
        public bool IsSpectating => !IsAlive;
    }

    public sealed class Lobby
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string NameKey { get; set; } = "";
        public string? PasswordHash { get; set; }
        public string? PasswordSalt { get; set; }
        public string HostNickname { get; set; } = "";
        public List<LobbyPlayer> Players { get; set; } = [];
        public LobbySettings Settings { get; set; } = new();
        public LobbyPhase Phase { get; set; } = LobbyPhase.Waiting;
        public int RoundNumber { get; set; }
        public int Seed { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        [JsonIgnore]
        public bool HasPassword => PasswordHash is not null;

        [JsonIgnore]
        public bool IsFull => Players.Count >= LobbyLimits.MaxPlayers;

        [JsonIgnore]
        public int ConnectedCount => Players.Count(p => p.IsConnected);

        public LobbyPlayer? FindPlayer(string nickname) =>
            Players.FirstOrDefault(p => string.Equals(p.Nickname, nickname, StringComparison.OrdinalIgnoreCase));

        public LobbyPlayer? FindPlayerByConnection(string connectionId) =>
            Players.FirstOrDefault(p => p.ConnectionId == connectionId);

        [JsonIgnore]
        public bool HasOnlinePlayers => ConnectedCount > 0;
    }

    public static class LobbyLimits
    {
        public const int MaxPlayers = 16;
        public const int RoomCodeLength = 6;
        public const int MaxNameLength = 32;
        public const int MaxNicknameLength = 16;
        public const int MinPasswordLength = 4;
        public const int MaxPasswordLength = 64;
        public const string RoomCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    }
}
