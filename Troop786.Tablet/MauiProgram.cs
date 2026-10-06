using Troop786.Core;
using Troop786.Tablet.Pages;
using Troop786.Tablet.Services;
using Troop786.Tablet.ViewModels;

namespace Troop786.Tablet;

public static class MauiProgram
{
    // Replace with the API Gateway base URL of the existing election API (include the trailing slash).
    private const string ApiBaseUrl = "https://REPLACE-ME.execute-api.us-east-1.amazonaws.com/";

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "outbox.db3");
        var outbox = new SqliteVoteOutbox(dbPath);

        // The device token comes from the enrollment flow (admin enters the enrollment key once).
        // Until that screen exists the token is empty and uploads simply stay queued.
        var http = new HttpClient { BaseAddress = new Uri(ApiBaseUrl), Timeout = TimeSpan.FromSeconds(20) };
        var api = new HttpKioskApi(http, () => null);

        builder.Services.AddSingleton(outbox);
        builder.Services.AddSingleton<IVoteOutbox>(outbox);
        builder.Services.AddSingleton<IKioskApi>(api);
        builder.Services.AddSingleton<OutboxSyncService>();
        builder.Services.AddSingleton<SyncCoordinator>();
        builder.Services.AddSingleton<ICodeLookup, UnsyncedCodeLookup>();
        builder.Services.AddSingleton<CodeEntryViewModel>();
        builder.Services.AddSingleton<CodeEntryPage>();

        return builder.Build();
    }
}
