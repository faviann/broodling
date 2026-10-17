using System.Text;
using System.Text.Json;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class RequestCaptureTests
{
    private const string PrimaryPath = "/repos/acme/widget/issues/12";
    private static readonly GitHubRepositoryCredentials Credentials = new("configured-token");

    [Test]
    public async Task TheWholeIssueBodyIsTheRequestAndNothingElseIsCaptured()
    {
        using var fixture = new RepositoryPreparationTests.RepositoryPreparationFixture();
        const string body = """
            # Widget

            <!-- broodling-request:v1 -->
            Add CSV export. https://github.com/acme/widget/issues/14 is only history.

            ### Available references
            - schema: repo:docs/schema.md
            - design: https://github.com/acme/widget/issues/7

            """;
        fixture.SetIssue(12, body);
        fixture.SetIssue(7, "Design.");
        fixture.CommitFile("docs/schema.md", "Schema\n");
        using var store = fixture.State.Initialize();
        var submission = store.SubmitIssue("https://github.com/acme/widget/issues/12");

        var bundle = await Capture(store, fixture, submission.SubmissionId);

        await Assert.That(bundle.State).IsEqualTo("complete");
        await Assert.That(bundle.Findings.Count).IsEqualTo(0);
        await Assert.That(string.Join(" ", bundle.References.Select(reference => reference.ReferenceId))).IsEqualTo("primary request");
        await Assert.That(ApiReads(fixture)).IsEqualTo(PrimaryPath);
        await Assert.That(Text(store, bundle, "request")).IsEqualTo(body);
        await Assert.That(Encoding.UTF8.GetString(bundle.AcquisitionPolicy)).IsEqualTo("""{"request":"primary_issue_body"}""");

        fixture.SetIssue(12, "Edited after capture.");
        var readsBefore = fixture.ReadGhPaths().Length;
        var replay = await Capture(store, fixture, submission.SubmissionId);
        await Assert.That(replay.ManifestSha256).IsEqualTo(bundle.ManifestSha256);
        await Assert.That(fixture.ReadGhPaths().Length).IsEqualTo(readsBefore);
        await Assert.That(Text(store, replay, "request")).IsEqualTo(body);
    }

    [Test]
    public async Task ResumeTakesTheRequestFromTheCapturedIssueWithoutReadingItAgain()
    {
        using var fixture = new RepositoryPreparationTests.RepositoryPreparationFixture();
        fixture.SetIssue(12, "Captured request.");
        using var store = fixture.State.Initialize();
        var submission = store.SubmitIssue("https://github.com/acme/widget/issues/12");
        var reference = WorkReference.Parse("acme/widget", 12);
        var issue = await new GitHubIssueSource(fixture.Gh).AcquireAsync(reference, Credentials, CancellationToken.None);
        // A crash after the first capture leaves the bundle open with only the issue retained.
        var open = store.BeginRequestBundleCapture(submission.SubmissionId, new RequestBundlePlan(
            JsonSerializer.SerializeToUtf8Bytes(new { issue = reference.IssueLocator }), """{"request":"primary_issue_body"}"""u8.ToArray()));
        store.RegisterRequestBundleReference(open.BundleId, "primary",
            JsonSerializer.SerializeToUtf8Bytes(new { role = "primary", url = reference.IssueLocator }));
        store.CaptureRequestBundleSource(open.BundleId, "primary", issue.Source);
        fixture.SetIssue(12, "Edited after capture.");
        var reads = fixture.ReadGhPaths().Length;

        var bundle = await Capture(store, fixture, submission.SubmissionId);

        await Assert.That(bundle.State).IsEqualTo("complete");
        await Assert.That(Text(store, bundle, "request")).IsEqualTo("Captured request.");
        await Assert.That(fixture.ReadGhPaths().Skip(reads).Contains(PrimaryPath)).IsFalse();
    }

    [Test]
    public async Task BundleFrozenUnderAnotherPlanIsNotAdopted()
    {
        using var fixture = new RepositoryPreparationTests.RepositoryPreparationFixture();
        fixture.SetIssue(12, "Add CSV export.");
        using var store = fixture.State.Initialize();
        var submission = store.SubmitIssue("https://github.com/acme/widget/issues/12");
        store.BeginRequestBundleCapture(submission.SubmissionId, new RequestBundlePlan(
            """{"issue":"https://github.com/acme/widget/issues/12"}"""u8.ToArray(),
            """{"convention":"broodling-request:v1"}"""u8.ToArray()));

        await Assert.That(async () => await Capture(store, fixture, submission.SubmissionId))
            .Throws<RequestBundleConflict>();
        var retained = store.GetRequestBundle(submission.SubmissionId);
        await Assert.That(retained.State).IsEqualTo("capturing");
        await Assert.That(retained.References.Count).IsEqualTo(0);
        await Assert.That(fixture.ReadGhPaths().Length).IsEqualTo(0);
    }

    [Test]
    [Arguments("empty")]
    [Arguments("whitespace")]
    [Arguments("null")]
    public async Task AnEmptyBodyIsARetainedRefusal(string variant)
    {
        using var fixture = new RepositoryPreparationTests.RepositoryPreparationFixture();
        if (variant == "null")
            fixture.SetResponse(PrimaryPath, new
            {
                number = 12, title = "Issue 12", body = (string?)null,
                html_url = "https://github.com/acme/widget/issues/12",
                repository_url = "https://api.github.com/repos/acme/widget", node_id = "I_12"
            });
        else
            fixture.SetIssue(12, variant == "empty" ? "" : " \n\t \r\n");
        using var store = fixture.State.Initialize();
        var submission = store.SubmitIssue("https://github.com/acme/widget/issues/12");

        var bundle = await Capture(store, fixture, submission.SubmissionId);

        await AssertRefused(store, bundle, "request_empty");
        await Assert.That(string.Join(" ", bundle.References.Select(reference => reference.ReferenceId))).IsEqualTo("primary");
        await Assert.That(bundle.Repository).IsNull();

        // A refused bundle blocks Contract association.
        var admitted = store.AdmitSources(ContractIngressTests.Reference, [ContractIngressTests.Primary()],
            ContractIngressTests.Propose, ContractIngressTests.PullRequest, "caller");
        await Assert.That(() => store.AssociateIssueSubmission(bundle.SubmissionId, admitted.Revision.ContractRevisionId))
            .Throws<IssueSubmissionConflict>();
    }

    [Test]
    [Arguments("missing")]
    [Arguments("gone")]
    public async Task AnUnavailableIssueIsARetainedRefusal(string variant)
    {
        using var fixture = new RepositoryPreparationTests.RepositoryPreparationFixture();
        // The fixture's repository is visible, so its 404 is genuine absence.
        if (variant == "gone")
            fixture.Gone(PrimaryPath);
        using var store = fixture.State.Initialize();
        var submission = store.SubmitIssue("https://github.com/acme/widget/issues/12");

        var bundle = await Capture(store, fixture, submission.SubmissionId);

        await AssertRefused(store, bundle, "issue_unavailable");
        await Assert.That(bundle.References.Single().IsCaptured).IsFalse();
    }

    private static async Task AssertRefused(BroodlingStore store, RequestBundle bundle, string code)
    {
        await Assert.That(bundle.State).IsEqualTo("refused");
        await Assert.That(bundle.Findings.Select(finding => finding.Code)).IsEquivalentTo([code]);
        var retained = store.GetRequestBundle(bundle.SubmissionId);
        await Assert.That(retained.State).IsEqualTo("refused");
        await Assert.That(retained.Findings).IsEquivalentTo(bundle.Findings);
        await Assert.That(store.GetIssueSubmission(bundle.SubmissionId).State).IsEqualTo("rejected");
    }

    private static Task<RequestBundle> Capture(BroodlingStore store,
        RepositoryPreparationTests.RepositoryPreparationFixture fixture, string submissionId) =>
        store.CaptureRequestBundleAsync(submissionId, fixture.RepositoryRoot, Credentials,
            new GitHubIssueSource(fixture.Gh), fixture.Source);

    private static string ApiReads(RepositoryPreparationTests.RepositoryPreparationFixture fixture) =>
        string.Join(" ", fixture.ReadGhPaths().Where(path => path != "/repos/acme/widget"));

    private static string Text(BroodlingStore store, RequestBundle bundle, string referenceId) =>
        Encoding.UTF8.GetString(store.GetEntitledSource(
            bundle.References.Single(reference => reference.ReferenceId == referenceId).SourceId!).Content);
}
