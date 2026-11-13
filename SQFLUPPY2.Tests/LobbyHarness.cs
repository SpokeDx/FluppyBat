using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SQFLUPPY2.Models;
using SQFLUPPY2.Services;

namespace SQFLUPPY2.Tests;

/// <summary>
/// Изолированный реестр лобби с временным файлом хранения.
/// </summary>
public sealed class LobbyHarness : IDisposable
{
    private readonly string _directory;

    public LobbyHarness()
    {
        _directory = Path.Combine(Path.GetTempPath(), "fluppy-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lobbies:StoragePath"] = "lobbies.json"
            })
            .Build();

        Registry = new LobbyRegistry(
            configuration,
            new TestHostEnvironment(_directory),
            NullLogger<LobbyRegistry>.Instance);
    }

    public const string HostConnection = "conn-host";
    public const string GuestConnection = "conn-guest";

    public LobbyRegistry Registry { get; }

    public string StorageDirectory => _directory;

    public Lobby CreateLobby(string name = "Пещера", string? password = null, string nickname = "Хозяин") =>
        Registry.Create(name, password, nickname, HostConnection).Unwrap();

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { /* временная папка */ }
    }

    private sealed class TestHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "SQFLUPPY2.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
