using LearnLuxembourgish.Shared.Http;
using LearnLuxembourgish.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using MudBlazor.Services;
using System.Net.Http;
using System.Reflection;

namespace LearnLuxembourgish.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Load appsettings.json from embedded resource
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("appsettings.json");
        if (stream is not null)
            builder.Configuration.AddJsonStream(stream);

        // In Release builds, load appsettings.release.json if it was embedded.
        // This file is gitignored and injected by CI (or created locally) to
        // supply the production API URL without committing it to source control.
        using var releaseStream = assembly.GetManifestResourceStream("appsettings.release.json");
        if (releaseStream is not null)
            builder.Configuration.AddJsonStream(releaseStream);

        builder.Services.AddMauiBlazorWebView();

        // MudBlazor UI services
        builder.Services.AddMudServices();

        // Auth / token storage
        builder.Services.AddScoped<TokenProvider>();

        // Per-session user settings (grammar provider preference, etc.)
        builder.Services.AddScoped<UserSettingsService>();

        // Resolve API base URL – prefer platform-specific key, fall back to generic, then default
        var apiBaseUrl = GetApiBaseUrl(builder.Configuration);

        builder.Services.AddHttpClient("api", client =>
        {
            client.BaseAddress = new Uri(apiBaseUrl);
        });

        // ApiClient is scoped so it shares the same DI scope as TokenProvider
        builder.Services.AddScoped<ApiClient>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("api");
            var tokenProvider = sp.GetRequiredService<TokenProvider>();
            var logger = sp.GetRequiredService<ILogger<ApiClient>>();
            return new ApiClient(httpClient, tokenProvider, logger);
        });

        // sproochmaschinn.lu TTS client (called from the app, not the API server)
        builder.Services.AddHttpClient<ISproochmaschinnService, SproochmaschinnService>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static string GetApiBaseUrl(Microsoft.Extensions.Configuration.IConfiguration config)
    {
#if ANDROID
        return config["ApiBaseUrl.Android"]
            ?? config["ApiBaseUrl"]
            ?? "http://10.0.2.2:5000";
#else
        return config["ApiBaseUrl"] ?? "http://localhost:5000";
#endif
    }
}
