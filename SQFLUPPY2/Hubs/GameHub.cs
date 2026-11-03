using Microsoft.AspNetCore.SignalR;
using SQFLUPPY2.Models;
using System.Collections.Concurrent;

namespace SQFLUPPY2.Hubs
{
    public class GameHub : Hub
    {
        private static ConcurrentDictionary<string, List<string>> Rooms = new();

        public async Task JoinRoom(string roomId, string username)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);

            if (!Rooms.ContainsKey(roomId))
            {
                Rooms[roomId] = new List<string>();
            }

            Rooms[roomId].Add(username);

            await Clients.Group(roomId).SendAsync("UserJoined", username, Rooms[roomId]);
        }

        public async Task SendAction(string roomId, string action)
        {
            if (action == "JUMP")
            {
                await Clients.OthersInGroup(roomId).SendAsync("PlayerJumped", Context.ConnectionId);
            }
        }

        public async Task SyncPosition(string roomId, float y, int score)
        {
            await Clients.OthersInGroup(roomId).SendAsync("UpdateOpponent", Context.ConnectionId, y, score);
        }
    }
}