namespace SQFLUPPY2.Models
{
    public sealed record LobbyPlayerView(
        string Nickname,
        bool IsHost,
        bool IsConnected,
        bool IsAlive,
        int Score,
        int TotalScore,
        float X,
        float Y);

    public sealed record LobbyView(
        string Code,
        string Name,
        bool HasPassword,
        string HostNickname,
        string Phase,
        int RoundNumber,
        LobbySettings Settings,
        IReadOnlyList<LobbyPlayerView> Players,
        int MaxPlayers);

    /// <summary>
    /// Обновление позиций игроков в общем мире (нормализованные координаты 0..1).
    /// </summary>
    public sealed record PlayerPositionView(
        string Nickname,
        float X,
        float Y,
        bool IsAlive,
        int Score);

    /// <summary>
    /// Ответ хаба на любое действие клиента.
    /// </summary>
    public sealed record LobbyAck(bool Ok, string? Error, LobbySnapshot? Lobby)
    {
        public static LobbyAck Success(LobbySnapshot lobby) => new(true, null, lobby);
        public static LobbyAck Failure(string error) => new(false, error, null);
    }

    /// <summary>
    /// Полное состояние лобби, рассылаемое подключённым клиентам.
    /// </summary>
    public sealed record LobbySnapshot(
        string Code,
        string Name,
        bool HasPassword,
        string HostNickname,
        string Phase,
        int RoundNumber,
        int Seed,
        LobbySettings Settings,
        IReadOnlyList<LobbyPlayerView> Players,
        int MaxPlayers);

    public sealed record RoundResultView(
        string Nickname,
        int Score,
        int TotalScore,
        bool IsHost);

    /// <summary>
    /// Результат операции в реестре лобби. Ошибки передаются клиенту кодом.
    /// </summary>
    public sealed class LobbyResult
    {
        private LobbyResult(Lobby? lobby, string? error)
        {
            Lobby = lobby;
            Error = error;
        }

        public Lobby? Lobby { get; }
        public string? Error { get; }

        public bool Success => Error is null;

        public static LobbyResult Ok(Lobby lobby) => new(lobby, null);
        public static LobbyResult Fail(string error) => new(null, error);

        public Lobby Unwrap() => Lobby ?? throw new InvalidOperationException("Lobby is missing.");
    }

    public static class LobbyErrors
    {
        public const string LobbyNotFound = "lobby_not_found";
        public const string LobbyFull = "lobby_full";
        public const string NameTaken = "name_taken";
        public const string NicknameTaken = "nickname_taken";
        public const string WrongPassword = "wrong_password";
        public const string PasswordTooShort = "password_too_short";
        public const string InvalidName = "invalid_name";
        public const string InvalidNickname = "invalid_nickname";
        public const string NotInLobby = "not_in_lobby";
        public const string NotHost = "not_host";
        public const string AlreadyPlaying = "already_playing";
        public const string NotPlaying = "not_playing";
        public const string PlayerNotFound = "player_not_found";
    }
}
