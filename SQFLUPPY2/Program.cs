using SQFLUPPY2.Hubs;
using SQFLUPPY2.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddSingleton<LobbyRegistry>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();

app.MapHub<GameHub>("/gameHub");

app.Run();