using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Broodling.Host;

public static class BroodlingHost
{
    /// <summary>
    /// The single application server. <c>Broodling:Store</c> must name existing compatible state:
    /// ordinary startup only opens it, and refuses missing or incompatible state before listening.
    /// </summary>
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var application = new BroodlingApplication();
        var storePath = builder.Configuration["Broodling:Store"] ?? "";
        using (application.OpenStore(storePath)) { }

        builder.Services.AddSingleton(application);
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
        HttpReads.Map(app, application, storePath);
        return app;
    }
}
