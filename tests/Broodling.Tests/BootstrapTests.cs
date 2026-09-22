using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class BootstrapTests
{
    [Test]
    public async Task HostComposesBroodlingApplication()
    {
        await using var host = Broodling.Host.BroodlingHost.Build([]);

        var application = host.Services.GetService<BroodlingApplication>();
        var meterProvider = host.Services.GetService<MeterProvider>();
        var tracerProvider = host.Services.GetService<TracerProvider>();

        await Assert.That(application).IsNotNull();
        await Assert.That(meterProvider).IsNotNull();
        await Assert.That(tracerProvider).IsNotNull();
    }
}
