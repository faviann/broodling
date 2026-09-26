using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Broodling.Host;
using Microsoft.AspNetCore.Builder;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class HttpReadTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task ServerOpensOnlyExistingStateAndReleasesItOnShutdown()
    {
        using var fixture = new StoreFixture();
        var missing = await Assert.That(async () => await Start(fixture.Path)).Throws<StoreStateException>();
        await Assert.That(missing!.Code).IsEqualTo("store_missing");
        await Assert.That(Directory.Exists(System.IO.Path.GetDirectoryName(fixture.Path))).IsFalse();

        using (fixture.Initialize()) { }
        var (app, client) = await Start(fixture.Path);
        using (client)
        {
            await Assert.That((await client.GetAsync("/health")).StatusCode).IsEqualTo(HttpStatusCode.OK);
            File.Move(fixture.Path, fixture.Path + ".moved");
            var unavailable = await client.GetAsync("/health");
            await Assert.That(unavailable.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
            await Assert.That((await Body(unavailable))["error"]!.GetValue<string>()).IsEqualTo("store_missing");
            File.Move(fixture.Path + ".moved", fixture.Path);
            await Assert.That((await client.GetAsync("/submissions/unknown")).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
        await app.StopAsync();
        await app.DisposeAsync();

        // SQLite removes the WAL sidecars only when the last connection closes.
        await Assert.That(File.Exists(fixture.Path + "-wal") || File.Exists(fixture.Path + "-shm")).IsFalse();
    }

    [Test]
    public async Task RetainedWorkAndFrozenReferencesAreServedWithoutExternalServices()
    {
        using var fixture = new CompletionFixture();
        await fixture.Dispatch();
        await fixture.Wait();
        var store = fixture.Store;
        var submission = store.SubmitIssue("https://github.com/acme/widget/issues/12");
        var bundle = store.BeginRequestBundleCapture(submission.SubmissionId,
            new RequestBundlePlan("inputs"u8.ToArray(), "policy"u8.ToArray(), "limits"u8.ToArray()));
        store.RegisterRequestBundleReference(bundle.BundleId,
            RequestBundleReferenceInput.Source("issue/12", "primary"u8.ToArray()));
        store.CaptureRequestBundleSource(bundle.BundleId, "issue/12", new SourceSubmission("caller_statement",
            "caller://issue/12", [0, 255, 1, 128], origin: "caller", entitlement: new("caller", "captured request")));
        store.RegisterRequestBundleReference(bundle.BundleId, RequestBundleReferenceInput.GitBlob("repo:original.txt",
            "repository file"u8.ToArray(), fixture.Git.Repository, "HEAD", "original.txt"));
        store.CaptureRequestBundleGitBlob(bundle.BundleId, "repo:original.txt");
        store.CompleteRequestBundleCapture(bundle.BundleId);
        // A separate Work Unit's bundle-bound Contract and Attempt, admitted from its submission.
        using var github = new RepositoryPreparationTests.RepositoryPreparationFixture();
        github.SetIssue(13, "## Request\n<!-- broodling-request:v1 -->\nAdd CSV export.\n");
        var bound = store.SubmitIssue("https://github.com/acme/widget/issues/13");
        await store.CaptureRequestBundleAsync(bound.SubmissionId, github.RepositoryRoot,
            new GitHubRepositoryCredentials("configured-token"), new GitHubIssueSource(github.Gh), github.Source);
        var boundRevision = store.AdmitRequestBundle(bound.SubmissionId, ContractIngressTests.Propose, "caller")
            .Revision.ContractRevisionId;
        var boundAttempt = store.AdmitHttpAttempt(bound.SubmissionId);
        var capturing = store.SubmitIssue("https://github.com/acme/widget/issues/14");
        var incomplete = store.BeginRequestBundleCapture(capturing.SubmissionId,
            new RequestBundlePlan("inputs"u8.ToArray(), "policy"u8.ToArray(), "limits"u8.ToArray()));
        store.RegisterRequestBundleReference(incomplete.BundleId, RequestBundleReferenceInput.Source("pending", "pending"u8.ToArray()));
        var contacted = false;
        fixture.Target.Reply = (_, _) => { contacted = true; return null; };
        var before = Dump(fixture.Git.State);

        var (app, client) = await Start(fixture.Git.State.Path);
        await using var _ = app;
        using var __ = client;
        const string issue = "https%3A%2F%2Fgithub.com%2Facme%2Fwidget%2Fissues%2F12";
        const string boundIssue = "https%3A%2F%2Fgithub.com%2Facme%2Fwidget%2Fissues%2F13";
        var reads = new (string Path, object Expected)[]
        {
            ($"/issues?url={issue}", new
            {
                submissions = store.IssueHistory(ContractIngressTests.Reference),
                revisions = store.History(ContractIngressTests.Reference)
            }),
            // A reader has no progression, and neither Attempt has a native run to observe.
            ($"/submissions/{submission.SubmissionId}", new
            {
                submission = store.GetIssueSubmission(submission.SubmissionId),
                admission = (object?)null, progression = (object?)null, observation = (object?)null
            }),
            ($"/submissions/{bound.SubmissionId}", new
            {
                submission = store.GetIssueSubmission(bound.SubmissionId),
                admission = store.Status(boundRevision).Decision, progression = (object?)null, observation = (object?)null
            }),
            ($"/issues?url={boundIssue}", new
            {
                submissions = store.IssueHistory("https://github.com/acme/widget/issues/13"),
                revisions = store.History(WorkReference.Parse("acme/widget", 13))
            }),
            ($"/submissions/{submission.SubmissionId}/bundle", store.GetRequestBundle(submission.SubmissionId)),
            ($"/bundles/{bundle.BundleId}/reference?id=issue%2F12", store.ReadRequestBundleReference(bundle.BundleId, "issue/12")),
            ($"/bundles/{bundle.BundleId}/reference?id=repo%3Aoriginal.txt",
                store.ReadRequestBundleReference(bundle.BundleId, "repo:original.txt")),
            ($"/revisions/{fixture.Attempt.ContractRevisionId}", store.Status(fixture.Attempt.ContractRevisionId)),
            ($"/attempts/{fixture.Attempt.AttemptId}", new
            {
                attempt = store.GetAttempt(fixture.Attempt.AttemptId),
                submission = store.FindSubmission(fixture.Attempt.AttemptId),
                completion = store.FindCompletion(fixture.Attempt.AttemptId),
                observation = (object?)null // A retained completion is not observed again.
            })
        };
        foreach (var (path, expected) in reads)
        {
            var response = await client.GetAsync(path);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(JsonNode.DeepEquals(await Body(response), JsonSerializer.SerializeToNode(expected, Json))).IsTrue();
        }
        var boundRead = (await Body(await client.GetAsync($"/submissions/{bound.SubmissionId}")))["submission"]!;
        await Assert.That(boundRead["contractRevisionId"]!.GetValue<string>()).IsEqualTo(boundRevision);
        await Assert.That(boundRead["attemptIds"]!.AsArray().Select(id => id!.GetValue<string>()))
            .IsEquivalentTo([boundAttempt.AttemptId]);
        var attempt = await Body(await client.GetAsync($"/attempts/{fixture.Attempt.AttemptId}"));
        await Assert.That(attempt["completion"]!["acceptedRevision"]!.GetValue<string>()).IsEqualTo(fixture.Accepted);
        var source = await Body(await client.GetAsync($"/bundles/{bundle.BundleId}/reference?id=issue%2F12"));
        await Assert.That(Convert.FromBase64String(source["content"]!.GetValue<string>())).IsEquivalentTo(new byte[] { 0, 255, 1, 128 });

        var unknownIssue = await Body(await client.GetAsync("/issues?url=https%3A%2F%2Fgithub.com%2Facme%2Fwidget%2Fissues%2F99"));
        await Assert.That(unknownIssue["submissions"]!.AsArray().Count + unknownIssue["revisions"]!.AsArray().Count).IsEqualTo(0);
        foreach (var (path, status) in new[]
        {
            ($"/bundles/{bundle.BundleId}/reference?id=pending", HttpStatusCode.NotFound),
            ("/issues?url=https%3A%2F%2Fgithub.com%2Facme%2Fwidget%2Fpull%2F12", HttpStatusCode.BadRequest),
            ($"/bundles/{incomplete.BundleId}/reference?id=pending", HttpStatusCode.Conflict)
        })
            await Assert.That((await client.GetAsync(path)).StatusCode).IsEqualTo(status);

        await Assert.That(contacted).IsFalse();
        await Assert.That(Dump(fixture.Git.State)).IsEqualTo(before);
    }

    [Test]
    public async Task RetainedReadsAnswerWhileAnotherSessionHoldsTheWriter()
    {
        using var fixture = new StoreFixture();
        string submissionId;
        using (var store = fixture.Initialize())
            submissionId = store.SubmitIssue("https://github.com/acme/widget/issues/12").SubmissionId;
        var (app, client) = await Start(fixture.Path);
        await using var _ = app;
        using var __ = client;

        using var writer = fixture.Connect();
        using var transaction = writer.BeginTransaction(deferred: false);
        await Assert.That((await client.GetAsync("/health")).StatusCode).IsEqualTo(HttpStatusCode.OK);
        var submission = await Body(await client.GetAsync($"/submissions/{submissionId}"));
        await Assert.That(submission["submission"]!["submissionId"]!.GetValue<string>()).IsEqualTo(submissionId);
        transaction.Rollback();
    }

    internal static async Task<(WebApplication App, HttpClient Client)> Start(string store)
    {
        var app = BroodlingHost.Build(["--urls=http://127.0.0.1:0", "--Broodling:Store=" + store]);
        await app.StartAsync();
        return (app, new HttpClient { BaseAddress = new Uri(app.Urls.Single()) });
    }

    private static async Task<JsonNode> Body(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    /// <summary>Every retained row, so a read that wrote anything cannot pass.</summary>
    private static string Dump(StoreFixture state)
    {
        using var connection = state.Connect();
        var tables = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
            using var row = command.ExecuteReader();
            while (row.Read()) tables.Add(row.GetString(0));
        }
        var dump = new System.Text.StringBuilder();
        foreach (var table in tables)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM \"{table}\" ORDER BY rowid";
            using var row = command.ExecuteReader();
            while (row.Read())
            {
                dump.Append(table);
                for (var index = 0; index < row.FieldCount; index++)
                    dump.Append('|').Append(row.GetValue(index) is byte[] bytes ? Convert.ToBase64String(bytes) : row.GetValue(index));
                dump.Append('\n');
            }
        }
        return dump.ToString();
    }
}
