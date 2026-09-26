using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Broodling.Host;

public static class BroodlingHost
{
    /// <summary>
    /// The single application server. <c>Broodling:Store</c> must name existing compatible state:
    /// ordinary startup only opens it, and refuses missing or incompatible state before listening.
    /// With <c>Broodling:Invocation</c> and <c>Broodling:RepositoryRoot</c> it also accepts, resumes and
    /// stops work and runs automatic progression and completion observation for its lifetime; without
    /// them it only reads. A configuration that cannot process refuses startup.
    /// </summary>
    public static WebApplication Build(string[] args) => Build(args, null);

    internal static WebApplication Build(string[] args, ProcessingPeers? peers)
    {
        var builder = WebApplication.CreateBuilder(args);
        var application = new BroodlingApplication();
        var storePath = builder.Configuration["Broodling:Store"] ?? "";
        using (application.OpenStore(storePath)) { }
        var settings = ProcessingSettings.Read(builder.Configuration, peers);

        builder.Services.AddSingleton(application);
        if (settings is not null)
        {
            builder.Services.AddSingleton(services => new Processing(application, storePath, settings,
                services.GetRequiredService<IHostApplicationLifetime>(), services.GetRequiredService<ILogger<Processing>>()));
            builder.Services.AddHostedService(services => services.GetRequiredService<Processing>());
        }
        builder.Services
            .AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter())
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter());

        var app = builder.Build();
        HttpApi.Map(app, application, storePath, settings?.Target.DirectRootCertificate,
            settings is null ? null : app.Services.GetRequiredService<Processing>());
        return app;
    }
}
