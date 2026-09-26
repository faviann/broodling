namespace Broodling.Host;

/// <summary>
/// Test substitutes for the processing server's external peers and its scan clock. Production passes none: the
/// authenticated GitHub CLI and Git, the pinned gateway, the process environment's credentials and system time.
/// </summary>
internal sealed record ProcessingPeers(Func<ProgressionCredentials> Credentials, TimeProvider Clock,
    GitHubIssueSource IssueSource, GitHubRepositorySource RepositorySource, HttpMessageHandler? Gateway);

/// <summary>
/// What a server needs to promise unattended processing of the work it accepts: the secret-free DirectTarget
/// <see cref="InvocationConfiguration"/>, an existing durable root for service-owned repositories and current
/// credentials. Without <c>Broodling:Invocation</c> and <c>Broodling:RepositoryRoot</c> the server only reads.
/// </summary>
internal sealed record ProcessingSettings(InvocationConfiguration.Direct Target, string RepositoryRoot,
    Func<ProgressionCredentials> Credentials, ProcessingPeers? Peers)
{
    /// <summary>Null selects the read-only server. Anything else that cannot process refuses startup.</summary>
    internal static ProcessingSettings? Read(IConfiguration configuration, ProcessingPeers? peers)
    {
        var invocation = configuration["Broodling:Invocation"];
        var repositoryRoot = configuration["Broodling:RepositoryRoot"];
        if (invocation is null && repositoryRoot is null) return null;
        if (string.IsNullOrEmpty(invocation) || string.IsNullOrEmpty(repositoryRoot))
            throw new UnsupportedRuntime("Submission needs both Broodling:Invocation and Broodling:RepositoryRoot.");
        if (InvocationConfiguration.Read(invocation) is not InvocationConfiguration.Direct target)
            throw new UnsupportedRuntime("Submitted work continues only through a DirectTarget.");
        if (!Path.IsPathFullyQualified(repositoryRoot) || !Directory.Exists(repositoryRoot))
            throw new UnsupportedRuntime("Broodling:RepositoryRoot must name an existing absolute directory.");
        var credentials = peers?.Credentials ?? FromEnvironment;
        // The operations' own rules, so a server never accepts work its credentials cannot process. Each
        // operation reads the credentials again; nothing is retained.
        var current = credentials();
        _ = current.Gateway.ApiKey();
        _ = current.Dispatch.Environment();
        return new(target, repositoryRoot, credentials, peers);
    }

    /// <summary>The credentials the invocation commands use, from the process environment.</summary>
    private static ProgressionCredentials FromEnvironment()
    {
        var token = Environment.GetEnvironmentVariable("GH_TOKEN");
        var gateway = (Url: Environment.GetEnvironmentVariable("GATEWAY_BASE_URL"), Key: Environment.GetEnvironmentVariable("GATEWAY_API_KEY"));
        return new(new GitHubRepositoryCredentials(token ?? ""), new GatewayCredentials(gateway.Url, gateway.Key),
            new DispatchCredentials(token, gateway.Url, gateway.Key));
    }
}

/// <summary>
/// The server's unattended responsibility for accepted work: the process's one <see cref="IssueSubmissionPreparer"/>,
/// <see cref="SubmissionProgressor"/> and <see cref="CompletionObserver"/>, attached to the host's lifetime. Shutdown
/// ends the preparer's lifetime, then detaches both services; nothing is stopped, abandoned or cleaned up. If either
/// service fails unexpectedly, the server stops rather than keep accepting work nothing would process.
/// </summary>
internal sealed class Processing : BackgroundService
{
    private readonly CompletionObserver observer;
    private readonly IHostApplicationLifetime lifetime;
    private readonly ILogger<Processing> logger;

    internal Processing(BroodlingApplication application, string storePath, ProcessingSettings settings,
        IHostApplicationLifetime lifetime, ILogger<Processing> logger)
    {
        this.lifetime = lifetime;
        this.logger = logger;
        var root = settings.Target.DirectRootCertificate;
        var peers = settings.Peers;
        var preparer = new IssueSubmissionPreparer(application, storePath, settings.RepositoryRoot, lifetime.ApplicationStopping)
        {
            IssueSource = peers?.IssueSource, RepositorySource = peers?.RepositorySource, Gateway = peers?.Gateway
        };
        Progressor = new SubmissionProgressor(application, storePath, root, new InvocationTarget.Direct(settings.Target.DirectOrigin),
            preparer, settings.Credentials, Stopped) { Clock = peers?.Clock ?? TimeProvider.System };
        observer = new CompletionObserver(application, storePath, root, (attemptId, failure) =>
            logger.LogError(failure, "Completion observation of Attempt {AttemptId} failed unexpectedly; it is not observed again until restart.", attemptId))
        { Clock = peers?.Clock ?? TimeProvider.System };
    }

    internal SubmissionProgressor Progressor { get; }

    /// <summary>A service failed unexpectedly and stopped the server, which then exits unsuccessfully.</summary>
    internal bool Failed { get; private set; }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(
        Attach("Automatic progression", Progressor.RunAsync, stoppingToken),
        Attach("Completion observation", observer.RunAsync, stoppingToken));

    private async Task Attach(string service, Func<CancellationToken, Task> run, CancellationToken stoppingToken)
    {
        // Both services return only when cancelled; anything else is an unexpected failure of discovery.
        try { await run(stoppingToken); }
        catch (Exception failure)
        {
            Failed = true;
            logger.LogCritical(failure, "{Service} failed unexpectedly; stopping the server so that it accepts no work it would not process.", service);
            lifetime.StopApplication();
        }
    }

    /// <summary>Code and message are safe operator output; only an unexpected failure carries its exception.</summary>
    private void Stopped(SubmissionProgress progress, Exception? failure) => logger.Log(failure is null ? LogLevel.Warning : LogLevel.Error,
        failure, "Submission {SubmissionId} stopped for attention in {Stage} after {Failures} failure(s): {Code}: {Message} Resume it once the cause is resolved.",
        progress.SubmissionId, progress.Stage, progress.Failures, progress.Code, progress.Message);
}
