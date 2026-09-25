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
    public async Task CapturesTheMarkedRequestAndItsDeterministicBoundedClosureOnce()
    {
        using var fixture = new RepositoryPreparationTests.RepositoryPreparationFixture();
        const string request = """
            ## Executable Request
            <!-- broodling-request:v1 -->

            Add CSV export. https://github.com/acme/widget/issues/14 is only history.

            ```sh
            # a shell comment, not a heading
            ```

            <!--
            ## Hidden note, not a heading
            -->

            ### Details
            Conform to `schema`.

            ### Available references
            - schema: repo:docs/schema.md
            - design: https://github.com/Acme/Widget/issues/7
            - decision: https://github.com/acme/widget/issues/8#issuecomment-456

            """;
        fixture.SetIssue(12, "# Widget\n\nBackground: https://github.com/acme/widget/issues/13\n"
            + "Use `<!-- broodling-request:v1 -->` or `<!-- broodling-request:v9 -->` inline.\n\n"
            + request + "## Discussion\nNot part of the request.\n");
        fixture.SetIssue(7, "Decision https://github.com/acme/widget/issues/8#issuecomment-456, primary "
            + "https://github.com/acme/widget/issues/12, self https://github.com/acme/widget/issues/7, "
            + "PR https://github.com/acme/widget/pull/3, external https://example.com/acme/widget/issues/1, "
            + "embedded https://example.com/r?u=https://github.com/acme/widget/issues/15, "
            + "file https://github.com/acme/widget/issues/6.md, dot https://github.com/acme/../issues/5, "
            + "and https://github.com/ACME/widget/issues/9.");
        fixture.SetComment(8, 456, "Back to https://github.com/acme/widget/issues/7 and "
            + "https://github.com/acme/widget/issues/10#issue-1 .");
        fixture.SetIssue(9, "Loop to https://github.com/acme/widget/issues/8#issuecomment-456.");
        fixture.CommitFile("docs/schema.md", "Schema; see https://github.com/acme/widget/issues/11\n");
        using var store = fixture.State.Initialize();
        var submission = store.SubmitIssue("https://github.com/acme/widget/issues/12");

        var bundle = await Capture(store, fixture, submission.SubmissionId);

        await Assert.That(bundle.State).IsEqualTo("complete");
        await Assert.That(bundle.Findings.Count).IsEqualTo(0);
        await Assert.That(string.Join(" ", bundle.References.Select(reference => reference.ReferenceId))).IsEqualTo(
            "primary request repo:docs/schema.md github:acme/widget/issues/7 "
            + "github:acme/widget/issues/8#issuecomment-456 github:acme/widget/issues/9");
        await Assert.That(ApiReads(fixture)).IsEqualTo(PrimaryPath + " /repos/acme/widget/issues/7 "
            + "/repos/acme/widget/issues/comments/456 /repos/acme/widget/issues/9");
        await Assert.That(Text(store, bundle, "request")).IsEqualTo(request);
        await Assert.That(Text(store, bundle, "repo:docs/schema.md"))
            .IsEqualTo("Schema; see https://github.com/acme/widget/issues/11\n");
        var linked = JsonDocument.Parse(bundle.References.Last().Selector).RootElement;
        await Assert.That(linked.GetProperty("from").GetString()).IsEqualTo("github:acme/widget/issues/7");
        var manifest = JsonDocument.Parse(bundle.ManifestJson!).RootElement;
        var limits = JsonDocument.Parse(Convert.FromBase64String(manifest.GetProperty("acquisitionLimits").GetString()!)).RootElement;
        await Assert.That(limits.GetProperty("maxReferences").GetInt32()).IsEqualTo(50);
        await Assert.That(limits.GetProperty("maxItemBytes").GetInt64()).IsEqualTo(1024 * 1024);
        await Assert.That(limits.GetProperty("maxTotalBytes").GetInt64()).IsEqualTo(8 * 1024 * 1024);

        var designBytes = store.ReadRequestBundleReference(bundle.BundleId, "github:acme/widget/issues/7").Content;
        fixture.SetIssue(7, "Edited after capture.");
        fixture.CommitFile("docs/schema.md", "Edited schema\n");
        var readsBefore = fixture.ReadGhPaths().Length;
        var replay = await Capture(store, fixture, submission.SubmissionId);
        await Assert.That(replay.ManifestSha256).IsEqualTo(bundle.ManifestSha256);
        await Assert.That(fixture.ReadGhPaths().Length).IsEqualTo(readsBefore);
        await Assert.That(store.ReadRequestBundleReference(bundle.BundleId, "github:acme/widget/issues/7").Content
            .SequenceEqual(designBytes)).IsTrue();
    }

    [Test]
    public async Task NotFoundInAnInvisibleRepositoryIsRetryableAndResumeKeepsFirstCaptures()
    {
        using var fixture = new RepositoryPreparationTests.RepositoryPreparationFixture();
        fixture.SetIssue(12, Request("- design: https://github.com/acme/widget/issues/7",
            "- notes: https://github.com/acme/private/issues/3"));
        fixture.SetIssue(7, "Original design.");
        string submissionId;
        using (var store = fixture.State.Initialize())
        {
            submissionId = store.SubmitIssue("https://github.com/acme/widget/issues/12").SubmissionId;
            GitHubSourceError? error = null;
            try { await Capture(store, fixture, submissionId); }
            catch (GitHubSourceError caught) { error = caught; }
            await Assert.That(error).IsNotNull();
            await Assert.That(error!.Retryable).IsTrue();
            var open = store.GetRequestBundle(submissionId);
            await Assert.That(open.State).IsEqualTo("capturing");
            await Assert.That(open.Findings.Count).IsEqualTo(0);
            await Assert.That(store.GetIssueSubmission(submissionId).State).IsEqualTo("capturing");
        }

        var primaryBytes = File.ReadAllBytes(fixture.ResponseFile(PrimaryPath));
        var designBytes = File.ReadAllBytes(fixture.ResponseFile("/repos/acme/widget/issues/7"));
        fixture.SetIssue(12, Request("- other: https://github.com/acme/widget/issues/9"));
        fixture.SetIssue(7, "Edited design.");
        // The credentials can now see the private repository and its issue.
        fixture.SetResponse("/repos/acme/private", new { full_name = "acme/private" });
        fixture.SetIssue(3, "Notes.", repository: "private");
        using var reopened = fixture.State.Open();
        var bundle = await Capture(reopened, fixture, submissionId);

        await Assert.That(bundle.State).IsEqualTo("complete");
        await Assert.That(reopened.ReadRequestBundleReference(bundle.BundleId, "primary").Content
            .SequenceEqual(primaryBytes)).IsTrue();
        await Assert.That(reopened.ReadRequestBundleReference(bundle.BundleId, "github:acme/widget/issues/7").Content
            .SequenceEqual(designBytes)).IsTrue();
        await Assert.That(ApiReads(fixture)).IsEqualTo(PrimaryPath + " /repos/acme/widget/issues/7 "
            + "/repos/acme/private/issues/3 /repos/acme/private /repos/acme/private/issues/3");
    }

    [Test]
    [Arguments("missing", "request_section_missing")]
    [Arguments("multiple", "request_section_multiple")]
    [Arguments("not-beneath-heading", "request_section_ambiguous")]
    [Arguments("unsupported-version", "request_section_unsupported")]
    [Arguments("unlabeled-declaration", "invalid_reference_declaration")]
    [Arguments("duplicate-label", "invalid_reference_declaration")]
    [Arguments("declaration-before-comment", "invalid_reference_declaration")]
    public async Task RequestGrammarViolationsAreRetainedRefusalsBeforeAnyReferenceAcquisition(string variant, string code)
    {
        using var fixture = new RepositoryPreparationTests.RepositoryPreparationFixture();
        fixture.SetIssue(12, variant switch
        {
            "missing" => "Please add CSV export.",
            "multiple" => "## One\n<!-- broodling-request:v1 -->\nA\n## Two\n<!-- broodling-request:v1 -->\nB\n",
            "not-beneath-heading" => "## Request\nIntro\n<!-- broodling-request:v1 -->\nA\n",
            "unsupported-version" => "## Request\n<!-- broodling-request:v2 -->\nA\n",
            "unlabeled-declaration" => Request("- https://github.com/acme/widget/issues/7"),
            "declaration-before-comment" => Request("- design: repo:docs/design.md <!--", "  rationale", "-->"),
            _ => Request("- spec: repo:docs/a.md", "- Spec: repo:docs/b.md")
        });
        using var store = fixture.State.Initialize();
        var submission = store.SubmitIssue("https://github.com/acme/widget/issues/12");

        var bundle = await Capture(store, fixture, submission.SubmissionId);

        await AssertRefused(store, bundle, code);
        await Assert.That(string.Join(" ", fixture.ReadGhPaths())).IsEqualTo(PrimaryPath);
        if (variant != "missing") return;

        // A refused bundle blocks Contract association and keeps its captures readable.
        var admitted = store.AdmitSources(ContractIngressTests.Reference, [ContractIngressTests.Primary()],
            ContractIngressTests.Propose, [], "caller");
        await Assert.That(() => store.AssociateIssueSubmission(bundle.SubmissionId, admitted.Revision.ContractRevisionId))
            .Throws<IssueSubmissionConflict>();
        await Assert.That(store.ReadRequestBundleReference(bundle.BundleId, "primary").Content
            .SequenceEqual(File.ReadAllBytes(fixture.ResponseFile(PrimaryPath)))).IsTrue();
    }

    [Test]
    [Arguments("primary-unavailable")]
    [Arguments("reference-unavailable")]
    [Arguments("reference-gone")]
    [Arguments("unresolved-path")]
    [Arguments("reference-count")]
    [Arguments("item-size")]
    [Arguments("total-size")]
    public async Task UnavailableMaterialAndExceededBoundsAreRetainedRefusals(string variant)
    {
        using var fixture = new RepositoryPreparationTests.RepositoryPreparationFixture();
        fixture.CommitFile("docs/a.md", "A\n");
        fixture.CommitFile("docs/large.md", new string('x', variant == "total-size" ? 2800 : 5000));
        if (variant != "primary-unavailable")
            fixture.SetIssue(12, variant switch
            {
                "reference-unavailable" or "reference-gone" => Request("- design: https://github.com/acme/widget/issues/7"),
                "unresolved-path" => Request("- spec: repo:docs/missing.md"),
                "reference-count" => Request("- a: repo:docs/a.md", "- large: repo:docs/large.md"),
                _ => Request("- large: repo:docs/large.md")
            });
        // The fixture's repository is visible, so its 404s are genuine absence.
        if (variant == "reference-gone")
            fixture.Gone("/repos/acme/widget/issues/7");
        var limits = variant switch
        {
            "reference-count" => new RequestBundleLimits(1, 1024 * 1024, 8 * 1024 * 1024),
            "item-size" => new RequestBundleLimits(50, 4096, 8 * 1024 * 1024),
            "total-size" => new RequestBundleLimits(50, 4096, 3000),
            _ => null
        };
        using var store = fixture.State.Initialize();
        var submission = store.SubmitIssue("https://github.com/acme/widget/issues/12");

        var bundle = await Capture(store, fixture, submission.SubmissionId, limits);

        await AssertRefused(store, bundle, variant is "primary-unavailable" or "reference-unavailable"
            or "reference-gone" or "unresolved-path" ? "reference_unavailable" : "reference_limit_exceeded");
        await Assert.That(ApiReads(fixture)).IsEqualTo(variant is "reference-unavailable" or "reference-gone"
            ? PrimaryPath + " /repos/acme/widget/issues/7" : PrimaryPath);
        if (variant is "item-size" or "total-size")
            await Assert.That(bundle.References.Single(reference => reference.ReferenceId == "repo:docs/large.md").IsCaptured)
                .IsFalse();
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

    private static string Request(params string[] declarations) =>
        "## Request\n<!-- broodling-request:v1 -->\nAdd CSV export.\n\n### Available references\n"
        + string.Join("\n", declarations) + "\n";

    private static Task<RequestBundle> Capture(BroodlingStore store,
        RepositoryPreparationTests.RepositoryPreparationFixture fixture, string submissionId,
        RequestBundleLimits? limits = null) =>
        store.CaptureRequestBundleAsync(submissionId, fixture.RepositoryRoot, Credentials,
            new GitHubIssueSource(fixture.Gh), fixture.Source, limits);

    private static string ApiReads(RepositoryPreparationTests.RepositoryPreparationFixture fixture) =>
        string.Join(" ", fixture.ReadGhPaths().Where(path => path != "/repos/acme/widget"));

    private static string Text(BroodlingStore store, RequestBundle bundle, string referenceId) =>
        Encoding.UTF8.GetString(store.ReadRequestBundleReference(bundle.BundleId, referenceId).Content);
}
