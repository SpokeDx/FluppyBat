using System.Security.Cryptography;
using System.Text.Json;
using SQFLUPPY2.Models;

namespace SQFLUPPY2.Services
{
    /// <summary>
    /// Потокобезопасный реестр лобби с сохранением на диск.
    /// </summary>
    public sealed class LobbyRegistry
    {
        public static readonly TimeSpan ReservationWindow = TimeSpan.FromSeconds(60);

        private readonly object _sync = new();
        private readonly Dictionary<string, Lobby> _byCode = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _codeByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _lobbyByConnection = new(StringComparer.Ordinal);
        private readonly string _storagePath;
        private readonly ILogger<LobbyRegistry> _logger;

        public LobbyRegistry(IConfiguration configuration, IHostEnvironment environment, ILogger<LobbyRegistry> logger)
        {
            _logger = logger;
            string configured = configuration["Lobbies:StoragePath"] ?? "App_Data/lobbies.json";
            _storagePath = Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(environment.ContentRootPath, configured);

            LoadFromDisk();
        }

        public LobbyResult Create(string? name, string? password, string? nickname, string connectionId)
        {
            string lobbyName = (name ?? "").Trim();
            string player = (nickname ?? "").Trim();

            if (lobbyName.Length < 3 || lobbyName.Length > LobbyLimits.MaxNameLength)
                return LobbyResult.Fail(LobbyErrors.InvalidName);

            if (!IsNicknameValid(player))
                return LobbyResult.Fail(LobbyErrors.InvalidNickname);

            string? passwordError = ValidatePassword(password, optional: true);
            if (passwordError is not null)
                return LobbyResult.Fail(passwordError);

            lock (_sync)
            {
                PurgeExpired();

                string nameKey = NormalizeName(lobbyName);
                if (_codeByName.ContainsKey(nameKey))
                    return LobbyResult.Fail(LobbyErrors.NameTaken);

                var lobby = new Lobby
                {
                    Code = GenerateCode(),
                    Name = lobbyName,
                    NameKey = nameKey,
                    HostNickname = player,
                    Seed = RandomNumberGenerator.GetInt32(int.MaxValue),
                    RoundNumber = 0
                };

                if (!string.IsNullOrEmpty(password))
                {
                    var (hash, salt) = LobbyPasswordHasher.Create(password);
                    lobby.PasswordHash = hash;
                    lobby.PasswordSalt = salt;
                }

                lobby.Players.Add(new LobbyPlayer
                {
                    Nickname = player,
                    ConnectionId = connectionId,
                    IsAlive = false
                });

                _byCode[lobby.Code] = lobby;
                _codeByName[nameKey] = lobby.Code;
                _lobbyByConnection[connectionId] = lobby.Code;

                Persist();
                return LobbyResult.Ok(lobby);
            }
        }

        public LobbyResult JoinByCode(string? code, string? nickname, string? password, string connectionId)
        {
            string normalizedCode = (code ?? "").Trim().ToUpperInvariant();

            lock (_sync)
            {
                PurgeExpired();

                if (!_byCode.TryGetValue(normalizedCode, out Lobby? lobby))
                    return LobbyResult.Fail(LobbyErrors.LobbyNotFound);

                return JoinLocked(lobby, nickname, password, connectionId);
            }
        }

        public LobbyResult JoinByName(string? name, string? nickname, string? password, string connectionId)
        {
            string nameKey = NormalizeName((name ?? "").Trim());

            lock (_sync)
            {
                PurgeExpired();

                if (nameKey.Length == 0 || !_codeByName.TryGetValue(nameKey, out string? code))
                    return LobbyResult.Fail(LobbyErrors.LobbyNotFound);

                return JoinLocked(_byCode[code], nickname, password, connectionId);
            }
        }

        public LobbyResult JoinById(string? lobbyId, string? nickname, string? password, string connectionId) =>
            JoinByCode(lobbyId, nickname, password, connectionId);

        private LobbyResult JoinLocked(Lobby lobby, string? nickname, string? password, string connectionId)
        {
            string player = (nickname ?? "").Trim();

            if (!IsNicknameValid(player))
                return LobbyResult.Fail(LobbyErrors.InvalidNickname);

            if (lobby.HasPassword && !LobbyPasswordHasher.Verify(password ?? "", lobby.PasswordHash, lobby.PasswordSalt))
                return LobbyResult.Fail(LobbyErrors.WrongPassword);

            // Возврат на зарезервированное место не занимает новый слот.
            LobbyPlayer? existing = lobby.FindPlayer(player);
            if (existing is { IsConnected: false })
            {
                existing.ConnectionId = connectionId;
                existing.ReservedUntil = null;
                _lobbyByConnection[connectionId] = lobby.Code;
                lobby.UpdatedAt = DateTimeOffset.UtcNow;
                Persist();
                return LobbyResult.Ok(lobby);
            }

            if (existing is not null)
                return LobbyResult.Fail(LobbyErrors.NicknameTaken);

            if (lobby.IsFull)
                return LobbyResult.Fail(LobbyErrors.LobbyFull);

            lobby.Players.Add(new LobbyPlayer
            {
                Nickname = player,
                ConnectionId = connectionId,
                IsAlive = false
            });

            _lobbyByConnection[connectionId] = lobby.Code;
            lobby.UpdatedAt = DateTimeOffset.UtcNow;

            Persist();
            return LobbyResult.Ok(lobby);
        }

        /// <summary>
        /// Освобождает соединение игрока. Место удерживается <see cref="ReservationWindow"/> для переподключения.
        /// </summary>
        public LobbyResult Leave(string connectionId, out string? nickname)
        {
            nickname = null;

            lock (_sync)
            {
                if (!_lobbyByConnection.TryGetValue(connectionId, out string? code))
                    return LobbyResult.Fail(LobbyErrors.NotInLobby);

                _lobbyByConnection.Remove(connectionId);

                if (!_byCode.TryGetValue(code, out Lobby? lobby))
                    return LobbyResult.Fail(LobbyErrors.LobbyNotFound);

                LobbyPlayer? player = lobby.Players.FirstOrDefault(p => p.ConnectionId == connectionId);
                if (player is null)
                    return LobbyResult.Fail(LobbyErrors.PlayerNotFound);

                nickname = player.Nickname;
                player.ConnectionId = null;
                player.IsAlive = false;
                player.ReservedUntil = DateTimeOffset.UtcNow + ReservationWindow;
                lobby.UpdatedAt = DateTimeOffset.UtcNow;

                TransferHostIfNeeded(lobby);
                PurgeExpired();

                Persist();
                return LobbyResult.Ok(lobby);
            }
        }

        public LobbyResult StartGame(string connectionId, out string? nickname)
        {
            nickname = null;

            lock (_sync)
            {
                PurgeExpired();

                Lobby? lobby = FindByConnection(connectionId);
                if (lobby is null) return LobbyResult.Fail(LobbyErrors.NotInLobby);

                LobbyPlayer? player = lobby.FindPlayerByConnection(connectionId);
                if (player is null) return LobbyResult.Fail(LobbyErrors.PlayerNotFound);

                nickname = player.Nickname;

                if (!string.Equals(player.Nickname, lobby.HostNickname, StringComparison.OrdinalIgnoreCase))
                    return LobbyResult.Fail(LobbyErrors.NotHost);

                if (lobby.Phase == LobbyPhase.Playing)
                    return LobbyResult.Fail(LobbyErrors.AlreadyPlaying);

                lobby.Phase = LobbyPhase.Playing;
                lobby.RoundNumber++;
                lobby.Seed = RandomNumberGenerator.GetInt32(int.MaxValue);

                foreach (LobbyPlayer p in lobby.Players)
                {
                    p.IsAlive = p.IsConnected;
                    p.Score = 0;
                }

                lobby.UpdatedAt = DateTimeOffset.UtcNow;
                Persist();
                return LobbyResult.Ok(lobby);
            }
        }

        public LobbyResult SubmitDeath(string connectionId, int score, out string? nickname)
        {
            nickname = null;

            lock (_sync)
            {
                PurgeExpired();

                Lobby? lobby = FindByConnection(connectionId);
                if (lobby is null) return LobbyResult.Fail(LobbyErrors.NotInLobby);
                if (lobby.Phase != LobbyPhase.Playing) return LobbyResult.Fail(LobbyErrors.NotPlaying);

                LobbyPlayer? player = lobby.FindPlayerByConnection(connectionId);
                if (player is null) return LobbyResult.Fail(LobbyErrors.PlayerNotFound);

                nickname = player.Nickname;
                if (!player.IsAlive) return LobbyResult.Ok(lobby);

                player.IsAlive = false;
                player.Score = Math.Max(0, score);
                player.TotalScore += player.Score;
                lobby.UpdatedAt = DateTimeOffset.UtcNow;

                if (lobby.Players.Where(p => p.IsConnected).All(p => !p.IsAlive))
                {
                    lobby.Phase = LobbyPhase.Waiting;
                }

                Persist();
                return LobbyResult.Ok(lobby);
            }
        }

        public LobbyResult EndRound(string connectionId)
        {
            lock (_sync)
            {
                Lobby? lobby = FindByConnection(connectionId);
                if (lobby is null) return LobbyResult.Fail(LobbyErrors.NotInLobby);

                if (lobby.Phase == LobbyPhase.Playing)
                {
                    lobby.Phase = LobbyPhase.Waiting;
                    foreach (LobbyPlayer p in lobby.Players)
                    {
                        p.IsAlive = false;
                    }

                    lobby.UpdatedAt = DateTimeOffset.UtcNow;
                    Persist();
                }

                return LobbyResult.Ok(lobby);
            }
        }

        public LobbyResult TransferHost(string connectionId, string newHostNickname)
        {
            lock (_sync)
            {
                Lobby? lobby = FindByConnection(connectionId);
                if (lobby is null) return LobbyResult.Fail(LobbyErrors.NotInLobby);

                LobbyPlayer? player = lobby.FindPlayerByConnection(connectionId);
                if (player is null) return LobbyResult.Fail(LobbyErrors.PlayerNotFound);

                if (!string.Equals(player.Nickname, lobby.HostNickname, StringComparison.OrdinalIgnoreCase))
                    return LobbyResult.Fail(LobbyErrors.NotHost);

                LobbyPlayer? target = lobby.FindPlayer(newHostNickname.Trim());
                if (target is null) return LobbyResult.Fail(LobbyErrors.PlayerNotFound);

                lobby.HostNickname = target.Nickname;
                lobby.UpdatedAt = DateTimeOffset.UtcNow;
                Persist();
                return LobbyResult.Ok(lobby);
            }
        }

        public LobbyResult UpdateSettings(string connectionId, string? difficulty, string? biomeMode, int? biomeIndex)
        {
            lock (_sync)
            {
                Lobby? lobby = FindByConnection(connectionId);
                if (lobby is null) return LobbyResult.Fail(LobbyErrors.NotInLobby);

                LobbyPlayer? player = lobby.FindPlayerByConnection(connectionId);
                if (player is null) return LobbyResult.Fail(LobbyErrors.PlayerNotFound);

                if (!string.Equals(player.Nickname, lobby.HostNickname, StringComparison.OrdinalIgnoreCase))
                    return LobbyResult.Fail(LobbyErrors.NotHost);

                if (lobby.Phase == LobbyPhase.Playing)
                    return LobbyResult.Fail(LobbyErrors.AlreadyPlaying);

                lobby.Settings.Apply(difficulty, biomeMode, biomeIndex);
                lobby.UpdatedAt = DateTimeOffset.UtcNow;
                Persist();
                return LobbyResult.Ok(lobby);
            }
        }

        public LobbyResult UpdatePlayerState(string connectionId, float x, float y, int score)
        {
            lock (_sync)
            {
                Lobby? lobby = FindByConnection(connectionId);
                if (lobby is null) return LobbyResult.Fail(LobbyErrors.NotInLobby);

                LobbyPlayer? player = lobby.FindPlayerByConnection(connectionId);
                if (player is null) return LobbyResult.Fail(LobbyErrors.PlayerNotFound);

                if (lobby.Phase != LobbyPhase.Playing || !player.IsAlive)
                    return LobbyResult.Ok(lobby);

                player.X = Math.Clamp(x, 0f, 1f);
                player.Y = Math.Clamp(y, 0f, 1f);
                player.Score = Math.Max(0, score);

                return LobbyResult.Ok(lobby);
            }
        }

        public LobbyResult Sync(string connectionId)
        {
            lock (_sync)
            {
                PurgeExpired();

                Lobby? lobby = FindByConnection(connectionId);
                return lobby is null ? LobbyResult.Fail(LobbyErrors.NotInLobby) : LobbyResult.Ok(lobby);
            }
        }

        public Lobby? FindByConnection(string connectionId)        {
            lock (_sync)
            {
                return _lobbyByConnection.TryGetValue(connectionId, out string? code)
                    && _byCode.TryGetValue(code, out Lobby? lobby)
                        ? lobby
                        : null;
            }
        }

        public Lobby? FindByCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;

            lock (_sync)
            {
                PurgeExpired();
                return _byCode.TryGetValue(code.Trim(), out Lobby? lobby) ? lobby : null;
            }
        }

        public Lobby? FindByName(string? name)
        {
            string nameKey = NormalizeName((name ?? "").Trim());
            if (nameKey.Length == 0) return null;

            lock (_sync)
            {
                PurgeExpired();
                return _codeByName.TryGetValue(nameKey, out string? code) && _byCode.TryGetValue(code, out Lobby? lobby)
                    ? lobby
                    : null;
            }
        }

        public LobbySnapshot TakeSnapshot(Lobby lobby)
        {
            lock (_sync)
            {
                List<LobbyPlayerView> players = lobby.Players
                    .Select(p => new LobbyPlayerView(
                        p.Nickname,
                        string.Equals(p.Nickname, lobby.HostNickname, StringComparison.OrdinalIgnoreCase),
                        p.IsConnected,
                        p.IsAlive,
                        p.Score,
                        p.TotalScore,
                        p.X,
                        p.Y))
                    .ToList();

                return new LobbySnapshot(
                    lobby.Code,
                    lobby.Name,
                    lobby.HasPassword,
                    lobby.HostNickname,
                    lobby.Phase.ToString(),
                    lobby.RoundNumber,
                    lobby.Seed,
                    lobby.Settings.Clone(),
                    players,
                    LobbyLimits.MaxPlayers);
            }
        }

        public IReadOnlyList<RoundResultView> BuildResults(Lobby lobby) =>
            lobby.Players
                .OrderByDescending(p => p.IsAlive)
                .ThenByDescending(p => p.Score)
                .Select(p => new RoundResultView(
                    p.Nickname,
                    p.Score,
                    p.TotalScore,
                    string.Equals(p.Nickname, lobby.HostNickname, StringComparison.OrdinalIgnoreCase)))
                .ToList();

        private void TransferHostIfNeeded(Lobby lobby)
        {
            LobbyPlayer? host = lobby.FindPlayer(lobby.HostNickname);
            if (host is { IsConnected: true }) return;

            LobbyPlayer? next = lobby.Players.FirstOrDefault(p => p.IsConnected);
            if (next is not null)
            {
                lobby.HostNickname = next.Nickname;
            }
        }

        private void PurgeExpired()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            List<string> emptyCodes = [];

            foreach (Lobby lobby in _byCode.Values)
            {
                lobby.Players.RemoveAll(p =>
                    !p.IsConnected && (p.ReservedUntil is null || p.ReservedUntil <= now));

                if (lobby.ConnectedCount == 0 && lobby.Players.Count == 0)
                {
                    emptyCodes.Add(lobby.Code);
                    continue;
                }

                TransferHostIfNeeded(lobby);
            }

            foreach (string code in emptyCodes)
            {
                if (_byCode.Remove(code, out Lobby? removed))
                {
                    _codeByName.Remove(removed.NameKey);
                }
            }
        }

        private static bool IsNicknameValid(string nickname) =>
            nickname.Length is > 0 and <= LobbyLimits.MaxNicknameLength;

        private static string? ValidatePassword(string? password, bool optional)
        {
            if (string.IsNullOrEmpty(password))
                return optional ? null : LobbyErrors.PasswordTooShort;

            if (password.Length < LobbyLimits.MinPasswordLength)
                return LobbyErrors.PasswordTooShort;

            if (password.Length > LobbyLimits.MaxPasswordLength)
                return LobbyErrors.PasswordTooShort;

            return null;
        }

        private static string NormalizeName(string name) => name.ToLowerInvariant();

        private string GenerateCode()
        {
            const string alphabet = LobbyLimits.RoomCodeAlphabet;
            Span<char> code = stackalloc char[LobbyLimits.RoomCodeLength];

            for (int attempt = 0; attempt < 1000; attempt++)
            {
                for (int i = 0; i < code.Length; i++)
                {
                    code[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
                }

                string candidate = new(code);
                if (!_byCode.ContainsKey(candidate)) return candidate;
            }

            throw new InvalidOperationException("Не удалось сгенерировать уникальный код лобби.");
        }

        private void LoadFromDisk()
        {
            try
            {
                if (!File.Exists(_storagePath)) return;

                string json = File.ReadAllText(_storagePath);
                List<Lobby>? lobbies = JsonSerializer.Deserialize<List<Lobby>>(json);
                if (lobbies is null) return;

                foreach (Lobby lobby in lobbies)
                {
                    // После рестарта живых соединений нет: места держим окном переподключения.
                    foreach (LobbyPlayer player in lobby.Players)
                    {
                        player.ConnectionId = null;
                        player.ReservedUntil = DateTimeOffset.UtcNow + ReservationWindow;
                    }

                    lobby.Phase = LobbyPhase.Waiting;
                    _byCode[lobby.Code] = lobby;
                    _codeByName[lobby.NameKey] = lobby.Code;
                }

                PurgeExpired();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось загрузить сохранённые лобби из {Path}", _storagePath);
            }
        }

        private void Persist()
        {
            try
            {
                string? directory = Path.GetDirectoryName(_storagePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonSerializer.Serialize(_byCode.Values.ToList(), new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(_storagePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось сохранить лобби в {Path}", _storagePath);
            }
        }
    }
}
