using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SQFLUPPY2.Models;
using SQFLUPPY2.Services;

namespace SQFLUPPY2.Tests;

public class LobbyRegistryTests
{
    [Fact]
    public void Create_AssignsHostAndShortCode()
    {
        using var harness = new LobbyHarness();

        Lobby lobby = harness.CreateLobby(name: "Пещера друзей", password: "1234", nickname: "Хозяин");

        Assert.Equal("Хозяин", lobby.HostNickname);
        Assert.Equal(LobbyLimits.RoomCodeLength, lobby.Code.Length);
        Assert.True(lobby.HasPassword);
        Assert.Equal(LobbyPhase.Waiting, lobby.Phase);
        Assert.Single(lobby.Players);
        Assert.NotEqual(0, lobby.Seed);
    }

    [Fact]
    public void Create_RejectsDuplicateNameIgnoringCase()
    {
        using var harness = new LobbyHarness();
        harness.CreateLobby(name: "Пещера");

        LobbyResult result = harness.Registry.Create("ПЕЩЕРА", null, "Другой", LobbyHarness.GuestConnection);

        Assert.Equal(LobbyErrors.NameTaken, result.Error);
    }

    [Fact]
    public void Create_RejectsShortPassword()
    {
        using var harness = new LobbyHarness();

        LobbyResult result = harness.Registry.Create("Пещера", "abc", "Хозяин", LobbyHarness.HostConnection);

        Assert.Equal(LobbyErrors.PasswordTooShort, result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("   ")]
    public void Create_RejectsInvalidName(string name)
    {
        using var harness = new LobbyHarness();

        LobbyResult result = harness.Registry.Create(name, null, "Хозяин", LobbyHarness.HostConnection);

        Assert.Equal(LobbyErrors.InvalidName, result.Error);
    }

    [Fact]
    public void JoinByCode_RequiresCorrectPassword()
    {
        using var harness = new LobbyHarness();
        Lobby lobby = harness.CreateLobby(password: "1234");

        LobbyResult wrong = harness.Registry.JoinByCode(lobby.Code, "Гость", "4321", LobbyHarness.GuestConnection);
        LobbyResult missing = harness.Registry.JoinByCode(lobby.Code, "Гость", null, LobbyHarness.GuestConnection);
        LobbyResult ok = harness.Registry.JoinByCode(lobby.Code.ToLowerInvariant(), "Гость", "1234", LobbyHarness.GuestConnection);

        Assert.Equal(LobbyErrors.WrongPassword, wrong.Error);
        Assert.Equal(LobbyErrors.WrongPassword, missing.Error);
        Assert.True(ok.Success);
        Assert.Equal(2, lobby.Players.Count);
    }

    [Fact]
    public void JoinByName_FindsLobbyIgnoringCase()
    {
        using var harness = new LobbyHarness();
        Lobby lobby = harness.CreateLobby(name: "Пещера друзей");

        LobbyResult result = harness.Registry.JoinByName("  пещера ДРУЗЕЙ ", "Гость", null, LobbyHarness.GuestConnection);

        Assert.True(result.Success);
        Assert.Same(lobby, result.Lobby);
    }

    [Fact]
    public void Join_RejectsNicknameOfOnlinePlayer()
    {
        using var harness = new LobbyHarness();
        Lobby lobby = harness.CreateLobby(nickname: "Хозяин");
        harness.Registry.JoinByCode(lobby.Code, "Гость", null, LobbyHarness.GuestConnection);

        LobbyResult result = harness.Registry.JoinByCode(lobby.Code, "гость", null, "conn-3");

        Assert.Equal(LobbyErrors.NicknameTaken, result.Error);
    }

    [Fact]
    public void Join_RejectsSeventeenthPlayer()
    {
        using var harness = new LobbyHarness();
        Lobby lobby = harness.CreateLobby(nickname: "Игрок0");

        for (int i = 1; i < LobbyLimits.MaxPlayers; i++)
        {
            LobbyResult joined = harness.Registry.JoinByCode(lobby.Code, $"Игрок{i}", null, $"conn-{i}");
            Assert.True(joined.Success);
        }

        LobbyResult overflow = harness.Registry.JoinByCode(lobby.Code, "Игрок16", null, "conn-16");

        Assert.Equal(LobbyErrors.LobbyFull, overflow.Error);
        Assert.Equal(LobbyLimits.MaxPlayers, lobby.Players.Count);
    }

    [Fact]
    public void Leave_KeepsReservedSeatAndCanReturn()
    {
        using var harness = new LobbyHarness();
        Lobby lobby = harness.CreateLobby();
        harness.Registry.JoinByCode(lobby.Code, "Гость", null, LobbyHarness.GuestConnection);

        LobbyResult left = harness.Registry.Leave(LobbyHarness.GuestConnection, out string? nickname);

        Assert.True(left.Success);
        Assert.Equal("Гость", nickname);

        LobbyPlayer reserved = Assert.Single(lobby.Players, p => p.Nickname == "Гость");
        Assert.False(reserved.IsConnected);
        Assert.NotNull(reserved.ReservedUntil);

        LobbyResult back = harness.Registry.JoinByCode(lobby.Code, "Гость", null, "conn-9");

        Assert.True(back.Success);
        Assert.Equal(2, lobby.Players.Count);
        Assert.True(lobby.FindPlayer("Гость")!.IsConnected);
    }

    [Fact]
    public void Leave_TransfersHostToNextConnectedPlayer()
    {
        using var harness = new LobbyHarness();
        Lobby lobby = harness.CreateLobby(nickname: "Хозяин");
        harness.Registry.JoinByCode(lobby.Code, "Гость", null, LobbyHarness.GuestConnection);

        harness.Registry.Leave(LobbyHarness.HostConnection, out _);

        Assert.Equal("Гость", lobby.HostNickname);
    }

    [Fact]
    public void StartGame_OnlyHostCanStartAndSeedChanges()
    {
        using var harness = new LobbyHarness();
        Lobby lobby = harness.CreateLobby();
        harness.Registry.JoinByCode(lobby.Code, "Гость", null, LobbyHarness.GuestConnection);
        int seedBefore = lobby.Seed;

        LobbyResult notHost = harness.Registry.StartGame(LobbyHarness.GuestConnection, out _);
        LobbyResult host = harness.Registry.StartGame(LobbyHarness.HostConnection, out _);

        Assert.Equal(LobbyErrors.NotHost, notHost.Error);
        Assert.True(host.Success);
        Assert.Equal(LobbyPhase.Playing, lobby.Phase);
        Assert.Equal(1, lobby.RoundNumber);
        Assert.NotEqual(seedBefore, lobby.Seed);
        Assert.All(lobby.Players, p => Assert.True(p.IsAlive));
    }

    [Fact]
    public void SubmitDeath_EndsRoundWhenAllConnectedPlayersAreDead()
    {
        using var harness = new LobbyHarness();
        Lobby lobby = harness.CreateLobby();
        harness.Registry.JoinByCode(lobby.Code, "Гость", null, LobbyHarness.GuestConnection);
        harness.Registry.StartGame(LobbyHarness.HostConnection, out _);

        harness.Registry.SubmitDeath(LobbyHarness.HostConnection, 30, out _);
        LobbyResult stillPlaying = harness.Registry.SubmitDeath(LobbyHarness.HostConnection, 30, out _);

        Assert.True(stillPlaying.Success);
        Assert.Equal(LobbyPhase.Playing, lobby.Phase);

        harness.Registry.SubmitDeath(LobbyHarness.GuestConnection, 12, out _);

        Assert.Equal(LobbyPhase.Waiting, lobby.Phase);

        IReadOnlyList<RoundResultView> results = harness.Registry.BuildResults(lobby);

        Assert.Equal(new[] { "Хозяин", "Гость" }, results.Select(r => r.Nickname));
        Assert.Equal(new[] { 30, 12 }, results.Select(r => r.Score));
        Assert.Equal(new[] { 30, 12 }, results.Select(r => r.TotalScore));
    }

    [Fact]
    public void UpdateSettings_RejectedWhilePlaying()
    {
        using var harness = new LobbyHarness();
        Lobby lobby = harness.CreateLobby();

        Assert.True(harness.Registry.UpdateSettings(LobbyHarness.HostConnection, LobbyDifficulty.Hard, LobbyBiomeMode.Fixed, 4).Success);
        Assert.Equal(LobbyDifficulty.Hard, lobby.Settings.Difficulty);
        Assert.Equal(4, lobby.Settings.BiomeIndex);

        harness.Registry.StartGame(LobbyHarness.HostConnection, out _);
        LobbyResult result = harness.Registry.UpdateSettings(LobbyHarness.HostConnection, LobbyDifficulty.Easy, LobbyBiomeMode.Auto, 0);

        Assert.Equal(LobbyPhase.Playing, lobby.Phase);
        Assert.Equal(LobbyErrors.AlreadyPlaying, result.Error);
        Assert.Equal(LobbyDifficulty.Hard, lobby.Settings.Difficulty);
    }

    [Fact]
    public void UpdatePlayerState_ClampsAndIgnoresDeadPlayers()
    {
        using var harness = new LobbyHarness();
        Lobby lobby = harness.CreateLobby();
        harness.Registry.StartGame(LobbyHarness.HostConnection, out _);

        harness.Registry.UpdatePlayerState(LobbyHarness.HostConnection, 1.5f, -0.25f, 7);

        LobbyPlayer player = lobby.Players[0];
        Assert.Equal(1f, player.X);
        Assert.Equal(0f, player.Y);
        Assert.Equal(7, player.Score);

        harness.Registry.SubmitDeath(LobbyHarness.HostConnection, 7, out _);
        harness.Registry.UpdatePlayerState(LobbyHarness.HostConnection, 0.5f, 0.5f, 99);

        Assert.Equal(1f, player.X);
        Assert.Equal(7, player.Score);
    }

    [Fact]
    public void State_SurvivesRegistryRestart()
    {
        using var harness = new LobbyHarness();
        Lobby lobby = harness.CreateLobby(name: "Переживёт", password: "1234", nickname: "Хозяин");
        harness.Registry.JoinByCode(lobby.Code, "Гость", "1234", LobbyHarness.GuestConnection);

        var reloaded = new LobbyRegistry(
            new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Lobbies:StoragePath"] = "lobbies.json" }).Build(),
            new LobbyHarnessEnvironment(harness.StorageDirectory),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SQFLUPPY2.Services.LobbyRegistry>.Instance);

        LobbyResult join = reloaded.JoinByCode(lobby.Code, "Третий", "1234", "conn-3");

        Assert.True(join.Success);
        Assert.NotNull(lobby.PasswordHash);
        Assert.Equal("Хозяин", join.Lobby!.HostNickname);
    }

    private sealed class LobbyHarnessEnvironment(string contentRoot) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "SQFLUPPY2.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
