using Microsoft.AspNetCore.SignalR;
using SQFLUPPY2.Models;
using SQFLUPPY2.Services;

namespace SQFLUPPY2.Hubs
{
    /// <summary>
    /// Единственная точка сетевого взаимодействия: создание/вход в лобби и синхронизация раунда.
    /// </summary>
    public sealed class GameHub(LobbyRegistry registry, ILogger<GameHub> logger) : Hub
    {
        private const string LobbyUpdatedEvent = "LobbyUpdated";
        private const string GameStartedEvent = "GameStarted";
        private const string RoundEndedEvent = "RoundEnded";
        private const string PlayerMovedEvent = "PlayerMoved";
        private const string PlayerDiedEvent = "PlayerDied";

        public Task<LobbyAck> CreateLobby(string? name, string? password, string? nickname) =>
            Execute(nickname, () => registry.Create(name, password, nickname, Context.ConnectionId));

        public Task<LobbyAck> JoinLobby(string? code, string? nickname, string? password) =>
            Execute(nickname, () => registry.JoinByCode(code, nickname, password, Context.ConnectionId));

        public Task<LobbyAck> JoinLobbyByName(string? name, string? nickname, string? password) =>
            Execute(nickname, () => registry.JoinByName(name, nickname, password, Context.ConnectionId));

        public async Task<LobbyAck> LeaveLobby()
        {
            LobbyResult result = registry.Leave(Context.ConnectionId, out _);
            if (!result.Success)
                return LobbyAck.Failure(result.Error!);

            Lobby lobby = result.Unwrap();
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupOf(lobby));
            await BroadcastLobby(lobby);
            return LobbyAck.Success(registry.TakeSnapshot(lobby));
        }

        public async Task<LobbyAck> StartGame()
        {
            LobbyResult result = registry.StartGame(Context.ConnectionId, out _);
            if (!result.Success)
                return LobbyAck.Failure(result.Error!);

            Lobby lobby = result.Unwrap();
            LobbySnapshot snapshot = registry.TakeSnapshot(lobby);

            await Clients.Group(GroupOf(lobby)).SendAsync(GameStartedEvent, snapshot);
            return LobbyAck.Success(snapshot);
        }

        public async Task<LobbyAck> UpdateSettings(string? difficulty, string? biomeMode, int? biomeIndex)
        {
            LobbyResult result = registry.UpdateSettings(Context.ConnectionId, difficulty, biomeMode, biomeIndex);
            return await Finish(result);
        }

        public async Task<LobbyAck> TransferHost(string? newHostNickname)
        {
            LobbyResult result = registry.TransferHost(Context.ConnectionId, newHostNickname ?? "");
            return await Finish(result);
        }

        public async Task<LobbyAck> PlayerDied(int score)
        {
            LobbyResult result = registry.SubmitDeath(Context.ConnectionId, score, out string? nickname);
            if (!result.Success)
                return LobbyAck.Failure(result.Error!);

            Lobby lobby = result.Unwrap();
            LobbySnapshot snapshot = registry.TakeSnapshot(lobby);

            if (nickname is not null)
            {
                await Clients.Group(GroupOf(lobby)).SendAsync(PlayerDiedEvent, nickname, score);
            }

            if (lobby.Phase == LobbyPhase.Waiting && lobby.RoundNumber > 0)
            {
                IReadOnlyList<RoundResultView> results = registry.BuildResults(lobby);
                await Clients.Group(GroupOf(lobby)).SendAsync(RoundEndedEvent, results);
            }

            await BroadcastLobby(lobby);
            return LobbyAck.Success(snapshot);
        }

        public async Task PlayerState(float x, float y, int score)
        {
            LobbyResult result = registry.UpdatePlayerState(Context.ConnectionId, x, y, score);
            if (!result.Success) return;

            Lobby lobby = result.Unwrap();
            LobbyPlayer? player = lobby.FindPlayerByConnection(Context.ConnectionId);
            if (player is null) return;

            await Clients.OthersInGroup(GroupOf(lobby))
                .SendAsync(PlayerMovedEvent, player.Nickname, player.X, player.Y, player.Score);
        }

        public async Task SyncLobby()
        {
            LobbyResult result = registry.Sync(Context.ConnectionId);
            if (!result.Success) return;

            await BroadcastLobby(result.Unwrap());
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            LobbyResult result = registry.Leave(Context.ConnectionId, out string? nickname);
            if (result.Success)
            {
                Lobby lobby = result.Unwrap();
                await BroadcastLobby(lobby);

                if (nickname is not null)
                {
                    await Clients.Group(GroupOf(lobby)).SendAsync(PlayerMovedEvent, nickname, 0f, 0f, 0);
                }
            }
            else if (result.Error != LobbyErrors.NotInLobby)
            {
                logger.LogDebug(exception, "Отключение {ConnectionId} без лобби: {Error}", Context.ConnectionId, result.Error);
            }

            await base.OnDisconnectedAsync(exception);
        }

        private async Task<LobbyAck> Execute(string? nickname, Func<LobbyResult> action)
        {
            LobbyResult result = action();
            if (!result.Success)
                return LobbyAck.Failure(result.Error!);

            Lobby lobby = result.Unwrap();
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupOf(lobby));
            await BroadcastLobby(lobby);

            logger.LogInformation("Игрок {Nickname} в лобби {Code}", nickname, lobby.Code);
            return LobbyAck.Success(registry.TakeSnapshot(lobby));
        }

        private async Task<LobbyAck> Finish(LobbyResult result)
        {
            if (!result.Success)
                return LobbyAck.Failure(result.Error!);

            Lobby lobby = result.Unwrap();
            await BroadcastLobby(lobby);
            return LobbyAck.Success(registry.TakeSnapshot(lobby));
        }

        private Task BroadcastLobby(Lobby lobby) =>
            Clients.Group(GroupOf(lobby)).SendAsync(LobbyUpdatedEvent, registry.TakeSnapshot(lobby));

        private static string GroupOf(Lobby lobby) => $"lobby:{lobby.Code}";
    }
}