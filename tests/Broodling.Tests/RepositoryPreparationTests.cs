using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class RepositoryPreparationTests
{
    [Test]
    public async Task ServiceAcquisitionRefreshesAnExistingBareRepositoryButOldCaptureReadsStayPinned()
    {
        using var fixture = new RepositoryPreparationFixture();
        RequestBundle firstBundle;
        RequestBundle completed;
        RepositoryPreparation first;
        RepositoryPreparation second;
        using (var store = fixture.State.Initialize())
        {
            var firstSubmission = store.SubmitIssue("https://github.com/acme/widget/issues/12");
            firstBundle = store.BeginRequestBundleCapture(firstSubmission.SubmissionId,
                new RequestBundlePlan("inputs-v1"u8.ToArray(), "policy-v1"u8.ToArray(), "limits-v1"u8.ToArray()));
            first = await store.PrepareRequestBundleRepositoryAsync(firstBundle.BundleId, fixture.RepositoryRoot,
                new GitHubRepositoryCredentials("configured-token"), fixture.Source);
            await Assert.That(first.DefaultBranch).IsEqualTo("main");
            await Assert.That(first.StartingCommit).IsEqualTo(fixture.InitialCommit);
            await Assert.That(first.StartingRevision).IsEqualTo("refs/heads/main");
            await Assert.That(GitCustody.Text(first.Repository, "for-each-ref", "--format=%(refname)", "refs/heads").Trim())
                .IsEqualTo("");
            await Assert.That(RunGit(first.Repository, "rev-parse", "refs/broodling/upstream/main").Trim())
                .IsEqualTo(fixture.InitialCommit);

            var admitted = store.AdmitSources(ContractIngressTests.Reference, [ContractIngressTests.Primary()],
                ContractIngressTests.Propose, [], "caller");
            var active = store.AdmitAttempt(admitted.Revision.ContractRevisionId, first.Repository,
                Path.Combine(fixture.State.Root, "active-attempts"), first.StartingCommit);
            var materialized = store.ProvisionAttempt(active.AttemptId);
            await GitCustody.RetainAcceptedAsync(first.Repository,
                RunGit(first.Repository, "remote", "get-url", "origin").Trim(), first.StartingCommit,
                CancellationToken.None);
            var acceptedRef = "refs/broodling/accepted/" + first.StartingCommit;
            await Assert.That(RunGit(first.Repository, "rev-parse", acceptedRef).Trim())
                .IsEqualTo(first.StartingCommit);

            fixture.AdvanceMainAndAddDevelop();
            fixture.SetDefaultBranch("develop");
            var secondSubmission = store.SubmitIssue("https://github.com/acme/widget/issues/13");
            var secondBundle = store.BeginRequestBundleCapture(secondSubmission.SubmissionId,
                new RequestBundlePlan("inputs-v2"u8.ToArray(), "policy-v1"u8.ToArray(), "limits-v1"u8.ToArray()));
            second = await store.PrepareRequestBundleRepositoryAsync(secondBundle.BundleId, fixture.RepositoryRoot,
                new GitHubRepositoryCredentials("configured-token"), fixture.Source);

            // This registration deliberately happens after the second acquisition.
            // The helper must use the first retained preparation, not the refreshed branch.
            store.RegisterRequestBundleRepositoryFile(firstBundle.BundleId, "request", "request"u8.ToArray(), "request.txt");
            store.CaptureRequestBundleGitBlob(firstBundle.BundleId, "request");
            completed = store.CompleteRequestBundleCapture(firstBundle.BundleId);
            store.CompleteRequestBundleCapture(secondBundle.BundleId);

            await Assert.That(second.Repository).IsEqualTo(first.Repository);
            await Assert.That(second.DefaultBranch).IsEqualTo("develop");
            await Assert.That(second.StartingCommit).IsEqualTo(fixture.AdvancedCommit);
            await Assert.That(second.StartingCommit).IsNotEqualTo(first.StartingCommit);
            await Assert.That(store.GetRequestBundle(firstBundle.SubmissionId).Repository!.StartingCommit)
                .IsEqualTo(fixture.InitialCommit);
            await Assert.That(RunGit(first.Repository, "show-ref", "--verify", "--hash",
                "refs/heads/" + materialized.Allocation.Branch).Trim()).IsEqualTo(first.StartingCommit);
            await Assert.That(RunGit(materialized.Allocation.WorktreePath, "rev-parse", "HEAD").Trim())
                .IsEqualTo(first.StartingCommit);
            await Assert.That(RunGit(first.Repository, "rev-parse", acceptedRef).Trim())
                .IsEqualTo(first.StartingCommit);
        }

        var callsBeforeReplay = File.ReadAllText(fixture.GhCalls);
        using (var reopened = fixture.State.Open())
        {
            var replayed = await reopened.PrepareRequestBundleRepositoryAsync(firstBundle.BundleId,
                fixture.RepositoryRoot, new GitHubRepositoryCredentials("configured-token"),
                new GitHubRepositorySource(Path.Combine(fixture.State.Root, "unavailable-gh"), fixture.Git));
            await Assert.That(replayed).IsEqualTo(first);
            await Assert.That(File.ReadAllText(fixture.GhCalls)).IsEqualTo(callsBeforeReplay);

            var oldRead = reopened.ReadRequestBundleReference(completed.BundleId, "request");
            await Assert.That(oldRead.Content.SequenceEqual("initial request\n"u8.ToArray())).IsTrue();
        }

        await Assert.That(File.ReadAllText(fixture.GitCalls))
            .Contains("+refs/heads/*:refs/broodling/upstream/*");
        var expectedAuth = "Authorization: Basic "
            + Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:configured-token"));
        await Assert.That(File.ReadAllText(fixture.GitCalls)).Contains(expectedAuth);
        await Assert.That(File.ReadAllText(fixture.GhCalls)).Contains("GH_TOKEN=configured-token");
    }

    [Test]
    public async Task ExistingContradictoryRepositoryOriginRefusesBeforeCredentialedFetch()
    {
        using var fixture = new RepositoryPreparationFixture();
        using var store = fixture.State.Initialize();
        var first = store.SubmitIssue("https://github.com/acme/widget/issues/12");
        var bundle = store.BeginRequestBundleCapture(first.SubmissionId,
            new RequestBundlePlan("inputs"u8.ToArray(), "policy"u8.ToArray(), "limits"u8.ToArray()));
        var prepared = await store.PrepareRequestBundleRepositoryAsync(bundle.BundleId, fixture.RepositoryRoot,
            new GitHubRepositoryCredentials("configured-token"), fixture.Source);
        var serviceRepository = prepared.Repository;
        RunGit(serviceRepository, "remote", "set-url", "origin", "https://github.com/other/repository.git");
        var before = RunGit(serviceRepository, "show-ref");
        var callsBefore = File.ReadAllText(fixture.GitCalls);
        var second = store.SubmitIssue("https://github.com/acme/widget/issues/13");
        var secondBundle = store.BeginRequestBundleCapture(second.SubmissionId,
            new RequestBundlePlan("inputs-2"u8.ToArray(), "policy"u8.ToArray(), "limits"u8.ToArray()));

        await Assert.That(async () => await store.PrepareRequestBundleRepositoryAsync(secondBundle.BundleId,
            fixture.RepositoryRoot, new GitHubRepositoryCredentials("configured-token"), fixture.Source))
            .Throws<GitHubRepositoryError>();
        await Assert.That(RunGit(serviceRepository, "show-ref")).IsEqualTo(before);
        var gitCallsAfter = File.ReadAllText(fixture.GitCalls);
        await Assert.That(gitCallsAfter[ callsBefore.Length..].Contains("Authorization: Basic ")).IsFalse();
    }

    [Test]
    public async Task ConcurrentUnpinnedPreparationsCannotCrossTheFirstRepositoryIdentityPin()
    {
        using var fixture = new RepositoryPreparationFixture();
        fixture.PrimeServiceRepository();
        string submissionId;
        string bundleId;
        using (var store = fixture.State.Initialize())
        {
            var submission = store.SubmitIssue("https://github.com/acme/widget/issues/12");
            submissionId = submission.SubmissionId;
            bundleId = store.BeginRequestBundleCapture(submissionId,
                new RequestBundlePlan("inputs"u8.ToArray(), "policy"u8.ToArray(), "limits"u8.ToArray())).BundleId;
        }
        using var firstStore = fixture.State.Open();
        using var secondStore = fixture.State.Open();
        var sourceA = new GitHubRepositorySource(fixture.CreateForge("R_A", "A", "B"), fixture.Git);
        var sourceB = new GitHubRepositorySource(fixture.CreateForge("R_B", "B", "A"), fixture.Git);

        async Task<(RepositoryPreparation? Preparation, Exception? Error)> TryPrepare(
            BroodlingStore store, GitHubRepositorySource source)
        {
            try
            {
                return (await store.PrepareRequestBundleRepositoryAsync(bundleId, fixture.RepositoryRoot,
                    new GitHubRepositoryCredentials("configured-token"), source), null);
            }
            catch (Exception error)
            {
                return (null, error);
            }
        }

        var outcomes = await Task.WhenAll(
            TryPrepare(firstStore, sourceA), TryPrepare(secondStore, sourceB));
        await Assert.That(outcomes.Count(outcome => outcome.Preparation is not null)).IsEqualTo(1);
        var loser = outcomes.Single(outcome => outcome.Error is not null).Error!;
        await Assert.That(loser).IsTypeOf<WorkUnitIdentityConflict>();
        await Assert.That(firstStore.GetWorkUnit(firstStore.GetIssueSubmission(submissionId).WorkUnitId).RepositoryIdentity)
            .IsIn("R_A", "R_B");
    }

    [Test]
    [Arguments("worktree")]
    [Arguments("http")]
    public async Task PreparedAttemptUsesRetainedStartingStateAndRefusesContradictoryTargetBranch(string kind)
    {
        using var fixture = new RepositoryPreparationFixture();
        using var store = fixture.State.Initialize();
        var submission = store.SubmitIssue("https://github.com/acme/widget/issues/12");
        var bundle = store.BeginRequestBundleCapture(submission.SubmissionId,
            new RequestBundlePlan("inputs"u8.ToArray(), "policy"u8.ToArray(), "limits"u8.ToArray()));
        var prepared = await store.PrepareRequestBundleRepositoryAsync(bundle.BundleId, fixture.RepositoryRoot,
            new GitHubRepositoryCredentials("configured-token"), fixture.Source);
        store.CompleteRequestBundleCapture(bundle.BundleId);
        fixture.AdvanceMainAndAddDevelop();
        fixture.SetDefaultBranch("develop");
        var refreshSubmission = store.SubmitIssue("https://github.com/acme/widget/issues/13");
        var refreshBundle = store.BeginRequestBundleCapture(refreshSubmission.SubmissionId,
            new RequestBundlePlan("refresh-inputs"u8.ToArray(), "policy"u8.ToArray(), "limits"u8.ToArray()));
        await store.PrepareRequestBundleRepositoryAsync(refreshBundle.BundleId, fixture.RepositoryRoot,
            new GitHubRepositoryCredentials("configured-token"), fixture.Source);
        await Assert.That(RunGit(prepared.Repository, "rev-parse", "refs/broodling/upstream/main").Trim())
            .IsEqualTo(fixture.AdvancedCommit);
        var admitted = store.AdmitSources(ContractIngressTests.Reference, [ContractIngressTests.Primary()],
            ContractIngressTests.Propose, [new("pr", "Open PR", "pull_request", "main")], "caller");
        store.AssociateIssueSubmission(submission.SubmissionId, admitted.Revision.ContractRevisionId);
        var attempt = kind == "http" ? store.AdmitHttpAttempt(submission.SubmissionId)
            : store.AdmitAttempt(submission.SubmissionId, Path.Combine(fixture.State.Root, "attempts"));
        await Assert.That(attempt.ResourceKind).IsEqualTo(kind);
        await Assert.That(RunGit(prepared.Repository, "rev-parse", "refs/broodling/starting/" + prepared.StartingCommit).Trim())
            .IsEqualTo(prepared.StartingCommit);
        await Assert.That(attempt.B1.Repository).IsEqualTo(prepared.Repository);
        await Assert.That(attempt.B1.CommitOid).IsEqualTo(prepared.StartingCommit);
        await Assert.That(attempt.B1.RequestedRevision).IsEqualTo(prepared.StartingRevision);

        using var mismatch = new RepositoryPreparationFixture(defaultBranch: "develop");
        using var mismatchStore = mismatch.State.Initialize();
        var mismatchSubmission = mismatchStore.SubmitIssue("https://github.com/acme/widget/issues/12");
        var mismatchBundle = mismatchStore.BeginRequestBundleCapture(mismatchSubmission.SubmissionId,
            new RequestBundlePlan("inputs"u8.ToArray(), "policy"u8.ToArray(), "limits"u8.ToArray()));
        await mismatchStore.PrepareRequestBundleRepositoryAsync(mismatchBundle.BundleId, mismatch.RepositoryRoot,
            new GitHubRepositoryCredentials("configured-token"), mismatch.Source);
        mismatchStore.CompleteRequestBundleCapture(mismatchBundle.BundleId);
        var mismatchAdmission = mismatchStore.AdmitSources(ContractIngressTests.Reference,
            [ContractIngressTests.Primary()], ContractIngressTests.Propose,
            [new("pr", "Open PR", "pull_request", "main")], "caller");
        mismatchStore.AssociateIssueSubmission(mismatchSubmission.SubmissionId, mismatchAdmission.Revision.ContractRevisionId);
        await Assert.That(() => kind == "http" ? mismatchStore.AdmitHttpAttempt(mismatchSubmission.SubmissionId)
            : mismatchStore.AdmitAttempt(mismatchSubmission.SubmissionId, Path.Combine(mismatch.State.Root, "attempts")))
            .Throws<AttemptAdmissionError>();
        await Assert.That(mismatchStore.Status(mismatchAdmission.Revision.ContractRevisionId).Attempts.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RepositoryMetadataRequiresExactGitHubCloneAndPinnedIdentity()
    {
        using var fixture = new RepositoryPreparationFixture();
        var reference = WorkReference.Parse("acme/widget", 12, repositoryIdentity: "R_expected");
        fixture.SetMetadata(cloneUrl: "https://github.com:444/acme/widget.git", repositoryIdentity: "R_expected");
        await Assert.That(async () => await fixture.Source.AcquireAsync(reference,
            new GitHubRepositoryCredentials("configured-token"), fixture.RepositoryRoot))
            .Throws<GitHubRepositoryError>();
        fixture.SetMetadata(cloneUrl: "https://github.com/acme/widget.git", repositoryIdentity: "R_other");
        await Assert.That(async () => await fixture.Source.AcquireAsync(reference,
            new GitHubRepositoryCredentials("configured-token"), fixture.RepositoryRoot))
            .Throws<GitHubRepositoryError>();
        await Assert.That(File.Exists(fixture.GitCalls)).IsFalse();
    }

    [Test]
    public async Task RepositoryIdentityChangingAcrossGitAcquisitionIsNotRetained()
    {
        using var fixture = new RepositoryPreparationFixture();
        fixture.SetMetadata(repositoryIdentity: "R_before");
        fixture.QueueMetadataChangeAfterGit("R_after");
        using var store = fixture.State.Initialize();
        var submission = store.SubmitIssue("https://github.com/acme/widget/issues/12");
        var bundle = store.BeginRequestBundleCapture(submission.SubmissionId,
            new RequestBundlePlan("inputs"u8.ToArray(), "policy"u8.ToArray(), "limits"u8.ToArray()));

        await Assert.That(async () => await store.PrepareRequestBundleRepositoryAsync(bundle.BundleId,
            fixture.RepositoryRoot, new GitHubRepositoryCredentials("configured-token"), fixture.Source))
            .Throws<GitHubRepositoryError>();

        await Assert.That(store.GetRequestBundle(submission.SubmissionId).Repository).IsNull();
        await Assert.That(store.GetWorkUnit(submission.WorkUnitId).RepositoryIdentity).IsNull();
        await Assert.That(Directory.Exists(Path.Combine(fixture.ServiceRepository, "refs", "broodling", "starting")))
            .IsFalse();
    }

    [Test]
    public async Task AbruptInitialRepositoryEstablishmentCanResumeOnRetry()
    {
        using var fixture = new RepositoryPreparationFixture();
        string bundleId;
        using (var store = fixture.State.Initialize())
        {
            var submission = store.SubmitIssue("https://github.com/acme/widget/issues/12");
            bundleId = store.BeginRequestBundleCapture(submission.SubmissionId,
                new RequestBundlePlan("inputs"u8.ToArray(), "policy"u8.ToArray(), "limits"u8.ToArray())).BundleId;
        }

        fixture.EnableInterruptedInitialEstablishment();
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var argument in new[] { Path.Combine(AppContext.BaseDirectory, "Broodling.ProcessWitness.dll"),
            "repository-preparation-crash", fixture.State.Path, bundleId, fixture.RepositoryRoot,
            fixture.Gh, fixture.Git })
            start.ArgumentList.Add(argument);

        using var witness = Process.Start(start)!;
        var error = witness.StandardError.ReadToEndAsync();
        try
        {
            var started = Stopwatch.StartNew();
            while (!File.Exists(fixture.OriginSetupStarted) && !witness.HasExited
                && started.Elapsed < TimeSpan.FromSeconds(20))
                await Task.Delay(20);
            if (!File.Exists(fixture.OriginSetupStarted))
                throw new Exception("Process witness did not reach remote-add gate; exited="
                    + witness.HasExited + ", error=" + (witness.HasExited ? await error : "still running"));

            witness.Kill(entireProcessTree: true);
            await witness.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            if (!witness.HasExited)
            {
                witness.Kill(entireProcessTree: true);
                await witness.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            try { await error; } catch (Exception) { }
        }

        await Assert.That(Directory.Exists(fixture.ServiceRepository)).IsFalse();
        var staging = Directory.GetDirectories(Path.GetDirectoryName(fixture.ServiceRepository)!,
            ".widget.git.initializing-*");
        await Assert.That(staging.Length).IsEqualTo(1);
        fixture.RestoreGit();
        using var reopened = fixture.State.Open();
        var prepared = await reopened.PrepareRequestBundleRepositoryAsync(bundleId, fixture.RepositoryRoot,
            new GitHubRepositoryCredentials("configured-token"), fixture.Source);
        await Assert.That(prepared.StartingCommit).IsEqualTo(fixture.InitialCommit);
        await Assert.That(Directory.Exists(fixture.ServiceRepository)).IsTrue();
    }

    [Test]
    public async Task AcquisitionCancellationDoesNotReturnWhileAnActiveGitChildCanMutate()
    {
        using var fixture = new RepositoryPreparationFixture();
        fixture.PrimeServiceRepository();
        fixture.EnableCancellableFetch();
        using var cancellation = new CancellationTokenSource();
        var acquisition = fixture.Source.AcquireAsync(WorkReference.Parse("acme/widget", 12),
            new GitHubRepositoryCredentials("configured-token"), fixture.RepositoryRoot, cancellation.Token);
        var started = Stopwatch.StartNew();
        while (!File.Exists(fixture.FetchStarted) && started.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(20);
        await Assert.That(File.Exists(fixture.FetchStarted)).IsTrue();
        var childPid = int.Parse(File.ReadAllText(fixture.FetchChildPid));

        cancellation.Cancel();
        Exception? error = null;
        try { await acquisition.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (Exception caught) { error = caught; }
        await Assert.That(error).IsTypeOf<OperationCanceledException>();

        await Assert.That(ProcessCanExecute(childPid)).IsFalse();
        var markerAtReturn = File.Exists(fixture.FetchChildFinished);
        await Task.Delay(1200);
        await Assert.That(File.Exists(fixture.FetchChildFinished)).IsEqualTo(markerAtReturn);
    }

    private static bool ProcessCanExecute(int pid)
    {
        var stat = "/proc/" + pid + "/stat";
        if (!File.Exists(stat)) return false;
        string contents;
        try { contents = File.ReadAllText(stat); }
        catch (FileNotFoundException) { return false; }
        var close = contents.LastIndexOf(')');
        return close >= 0 && contents.Length > close + 2 && contents[close + 2] != 'Z';
    }

    private static string RunGit(string repository, params string[] arguments) => AttemptFixture.RunGit(repository, arguments);

    private sealed class RepositoryPreparationFixture : IDisposable
    {
        internal StoreFixture State { get; } = new();
        internal string Seed { get; }
        internal string Remote { get; }
        internal string RepositoryRoot { get; }
        internal string GhCalls { get; }
        internal string GitCalls { get; }
        internal string OriginSetupStarted { get; }
        internal string FetchStarted { get; }
        internal string FetchChildPid { get; }
        internal string FetchChildFinished { get; }
        internal string Gh => gh;
        internal string Git => git;
        internal string ServiceRepository => Path.Combine(RepositoryRoot, "acme", "widget.git");
        internal GitHubRepositorySource Source { get; }
        internal string InitialCommit { get; }
        internal string AdvancedCommit { get; private set; } = "";
        private readonly string gh;
        private readonly string git;
        private readonly string metadata;
        private readonly string metadataFlip;
        private readonly string metadataAfterFlip;
        private string defaultBranch;

        internal RepositoryPreparationFixture(string defaultBranch = "main")
        {
            Seed = Path.Combine(State.Root, "seed");
            Remote = Path.Combine(State.Root, "remote.git");
            RepositoryRoot = Path.Combine(State.Root, "service-repositories");
            GhCalls = Path.Combine(State.Root, "gh-calls");
            GitCalls = Path.Combine(State.Root, "git-calls");
            OriginSetupStarted = Path.Combine(State.Root, "origin-setup-started");
            FetchStarted = Path.Combine(State.Root, "fetch-started");
            FetchChildPid = Path.Combine(State.Root, "fetch-child-pid");
            FetchChildFinished = Path.Combine(State.Root, "fetch-child-finished");
            gh = Path.Combine(State.Root, "gh");
            git = Path.Combine(State.Root, "git");
            metadata = Path.Combine(State.Root, "metadata.json");
            metadataFlip = Path.Combine(State.Root, "metadata-flip");
            metadataAfterFlip = Path.Combine(State.Root, "metadata-after-flip.json");
            this.defaultBranch = defaultBranch;
            Directory.CreateDirectory(Seed);
            RunGitIn(Seed, "init", "--initial-branch=main");
            RunGitIn(Seed, "config", "user.email", "test@example.invalid");
            RunGitIn(Seed, "config", "user.name", "Broodling test");
            File.WriteAllText(Path.Combine(Seed, "request.txt"), "initial request\n");
            RunGitIn(Seed, "add", ".");
            RunGitIn(Seed, "commit", "-m", "initial");
            InitialCommit = RunGitIn(Seed, "rev-parse", "HEAD").Trim();
            RunGitIn(Seed, "clone", "--bare", Seed, Remote);
            RunGitIn(Seed, "remote", "add", "origin", Remote);
            RunGitIn(Seed, "push", "-u", "origin", "main");
            RunGitIn(Seed, "branch", "develop");
            RunGitIn(Seed, "push", "origin", "develop");
            SetMetadata();

            ExecutableFile.Write(gh, "#!/bin/sh\nset -eu\nprintf 'GH_TOKEN=%s\\n' \"$GH_TOKEN\" >> '" + GhCalls + "'\ncat '" + metadata + "'\n");
            WriteStandardGit();
            if (OperatingSystem.IsLinux())
            {
                var executableMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
                File.SetUnixFileMode(gh, executableMode);
            }
            Source = new(gh, git);
        }

        internal void AdvanceMainAndAddDevelop()
        {
            File.WriteAllText(Path.Combine(Seed, "request.txt"), "advanced request\n");
            RunGitIn(Seed, "add", ".");
            RunGitIn(Seed, "commit", "-m", "advanced");
            AdvancedCommit = RunGitIn(Seed, "rev-parse", "HEAD").Trim();
            RunGitIn(Seed, "push", "origin", "main");
            RunGitIn(Seed, "branch", "-f", "develop", "HEAD");
            RunGitIn(Seed, "push", "--force", "origin", "develop");
        }

        internal void SetDefaultBranch(string branch)
        {
            defaultBranch = branch;
            SetMetadata();
        }

        internal void PrimeServiceRepository()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ServiceRepository)!);
            RunGitIn(State.Root, "clone", "--bare", "--no-tags", Remote, ServiceRepository);
            RunGitIn(State.Root, "-C", ServiceRepository, "remote", "set-url", "origin",
                "https://github.com/acme/widget.git");
        }

        internal void QueueMetadataChangeAfterGit(string repositoryIdentity)
        {
            WriteMetadata(metadataAfterFlip, "https://github.com/acme/widget.git", repositoryIdentity);
            File.WriteAllText(metadataFlip, "pending\n");
        }

        internal void EnableInterruptedInitialEstablishment()
        {
            ExecutableFile.Write(git, "#!/bin/sh\nset -eu\n"
                + "printf 'ARGS=%s\\n' \"$*\" >> '" + GitCalls + "'\n"
                + "printf 'AUTH=%s\\n' \"${GIT_CONFIG_VALUE_0-}\" >> '" + GitCalls + "'\n"
                + "if [ \"$1\" = -C ] && [ \"$3\" = remote ] && [ \"$4\" = add ]; then\n"
                + "  printf started > '" + OriginSetupStarted + "'\n"
                + "  sleep 30\n"
                + "  exit 1\n"
                + "fi\nexec /usr/bin/git \"$@\"\n");
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(git, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        internal void RestoreGit() => WriteStandardGit();

        private void WriteStandardGit()
        {
            ExecutableFile.Write(git, "#!/bin/sh\nset -eu\nprintf 'ARGS=%s\\n' \"$*\" >> '" + GitCalls + "'\nprintf 'AUTH=%s\\n' \"${GIT_CONFIG_VALUE_0-}\" >> '" + GitCalls + "'\n"
                + "if [ \"$1\" = clone ]; then\n"
                + "  if [ -f '" + metadataFlip + "' ]; then cp '" + metadataAfterFlip + "' '" + metadata + "'; rm '" + metadataFlip + "'; fi\n"
                + "  /usr/bin/git -c 'url." + Remote + ".insteadOf=https://github.com/acme/widget.git' \"$@\"\n"
                + "  exit 0\nfi\n"
                + "if [ \"$1\" = -C ] && [ \"$3\" = fetch ]; then\n"
                + "  if [ -f '" + metadataFlip + "' ]; then cp '" + metadataAfterFlip + "' '" + metadata + "'; rm '" + metadataFlip + "'; fi\n"
                + "  /usr/bin/git -c 'url." + Remote + ".insteadOf=https://github.com/acme/widget.git' \"$@\"\n"
                + "  exit 0\nfi\nexec /usr/bin/git \"$@\"\n");
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(git, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        internal void EnableCancellableFetch()
        {
            ExecutableFile.Write(git, "#!/bin/sh\nset -eu\n"
                + "printf 'ARGS=%s\\n' \"$*\" >> '" + GitCalls + "'\n"
                + "printf 'AUTH=%s\\n' \"${GIT_CONFIG_VALUE_0-}\" >> '" + GitCalls + "'\n"
                + "if [ \"$1\" = -C ] && [ \"$3\" = fetch ]; then\n"
                + "  printf '%s\\n' \"$$\" > '" + FetchStarted + "'\n"
                + "  ( sleep 1; printf done > '" + FetchChildFinished + "' ) &\n"
                + "  printf '%s\\n' \"$!\" > '" + FetchChildPid + "'\n"
                + "  sleep 30\n"
                + "  exit 1\n"
                + "fi\nexec /usr/bin/git \"$@\"\n");
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(git, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        internal string CreateForge(string repositoryIdentity, string? barrierLabel = null,
            string? otherBarrierLabel = null)
        {
            var forge = Path.Combine(State.Root, "gh-" + repositoryIdentity);
            var forgeMetadata = Path.Combine(State.Root, "metadata-" + repositoryIdentity + ".json");
            WriteMetadata(forgeMetadata, "https://github.com/acme/widget.git", repositoryIdentity);
            var barrier = barrierLabel is null || otherBarrierLabel is null ? "" :
                "mkdir -p '" + Path.Combine(State.Root, "forge-barrier") + "'\n"
                + ": > '" + Path.Combine(State.Root, "forge-barrier", barrierLabel) + "'\n"
                + "while [ ! -f '" + Path.Combine(State.Root, "forge-barrier", otherBarrierLabel) + "' ]; do sleep 0.01; done\n";
            ExecutableFile.Write(forge, "#!/bin/sh\nset -eu\nprintf 'GH_TOKEN=%s\\n' \"$GH_TOKEN\" >> '" + GhCalls + "'\n"
                + barrier + "cat '" + forgeMetadata + "'\n");
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(forge, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return forge;
        }

        internal void SetMetadata(string? cloneUrl = null, string? repositoryIdentity = null)
        {
            WriteMetadata(metadata, cloneUrl ?? "https://github.com/acme/widget.git",
                repositoryIdentity ?? "R_widget");
        }

        private void WriteMetadata(string path, string cloneUrl, string repositoryIdentity)
        {
            var value = new
            {
                full_name = "acme/widget",
                html_url = "https://github.com/acme/widget",
                node_id = repositoryIdentity,
                default_branch = defaultBranch,
                clone_url = cloneUrl
            };
            File.WriteAllText(path, JsonSerializer.Serialize(value));
        }

        private string RunGitIn(string repository, params string[] arguments) => RunGit(repository, arguments);
        public void Dispose() => State.Dispose();
    }
}
