using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

/// <summary>
/// The bundled proposer over real capture, Git and SQLite, with a controlled in-process model
/// gateway. Bundle-bound authority checks themselves are <see cref="RequestAdmissionTests"/>'.
/// </summary>
public sealed class BundledProposerTests
{
    private const string ApiKey = "gateway-key-7f3a9c";
    private static readonly GatewayCredentials Credentials = new(NativeProfile.GatewayBaseUrl, ApiKey);
    private const string DesignBody = "Supporting design: rows are comma separated.";
    private const string SchemaFile = "Schema: id, name, total\n";

    [Test]
    public async Task ProposerStartsFromTheRequestAndManifestReadsAReferenceOnDemandAndIsAdmittedBundleBound()
    {
        using var fixture = Fixture(out var store, out var submissionId, out var bundle);
        using var session = store;
        var schema = bundle.References.Single(reference => reference.CaptureKind == "git_blob");
        var gateway = new ControlledGateway(
            _ => ControlledGateway.ToolCall("call-1", new JsonObject { ["referenceId"] = schema.ReferenceId }),
            _ => ControlledGateway.Final("""{"criteria": [{"criterionId": "csv", "statement": "Export CSV with the schema columns."}]}"""));

        var status = await store.AdmitRequestBundleAsync(submissionId, Credentials, gateway, default);

        // The initial context is the request, fixed authority and member identities, never member content.
        var first = gateway.Requests[0];
        await Assert.That(gateway.Uris[0]).IsEqualTo(new Uri(NativeProfile.GatewayBaseUrl + "/chat/completions"));
        await Assert.That(gateway.Authorizations[0]).IsEqualTo("Bearer " + ApiKey);
        await Assert.That((string)first["model"]!).IsEqualTo("gpt-5.6-sol");
        await Assert.That((string)first["tools"]![0]!["function"]!["name"]!).IsEqualTo("read_reference");
        var context = JsonNode.Parse((string)first["messages"]![1]!["content"]!)!;
        var request = bundle.References.Single(reference => reference.ReferenceId == "request");
        await Assert.That((string)context["executableRequest"]!)
            .IsEqualTo(Encoding.UTF8.GetString(store.ReadRequestBundleReference(bundle.BundleId, "request").Content));
        await Assert.That(context["manifest"]!["references"]!.AsArray().Select(item => (string)item!["referenceId"]!))
            .IsEquivalentTo(bundle.References.Select(reference => reference.ReferenceId));
        await Assert.That(first.ToJsonString()).DoesNotContain(DesignBody);
        await Assert.That(first.ToJsonString()).DoesNotContain("id, name, total");

        // The on-demand read returns the frozen member through the bundle-scoped read.
        var read = JsonNode.Parse((string)gateway.Requests[1]["messages"]!.AsArray().Last()!["content"]!)!;
        await Assert.That((string)read["content"]!).IsEqualTo(SchemaFile);
        await Assert.That((string)read["contentSha256"]!).IsEqualTo(schema.ContentSha256);

        using var reopened = fixture.State.Open();
        var admitted = reopened.Status(status.Revision.ContractRevisionId);
        await Assert.That(admitted.Decision!.Admitted).IsTrue();
        await Assert.That(admitted.Revision.Contract.Criteria.Single().Statement).IsEqualTo("Export CSV with the schema columns.");
        await Assert.That(admitted.Revision.ConstructedBy).IsEqualTo("model_extraction");
        await Assert.That(admitted.Revision.Contract.RequestBundle)
            .IsEqualTo(new ContractRequestBundle(bundle.BundleId, bundle.ManifestSha256!));
        await Assert.That(admitted.Revision.Contract.SourceAttribution)
            .IsEquivalentTo([new SourceAttribution(request.SourceId!, request.ContentSha256!)]);
        await Assert.That(reopened.GetIssueSubmission(submissionId).State).IsEqualTo("admitted");
    }

    [Test]
    [Arguments("malformed", "The model's proposal is not a JSON object.")]
    [Arguments("authority", "The proposal changed the caller's exact effect authority.")]
    public async Task MalformedOrAuthorityChangingProposalIsRetainedAsTheSubmissionsRefusal(string variant, string detail)
    {
        using var fixture = Fixture(out var store, out var submissionId, out _);
        using var session = store;
        var gateway = new ControlledGateway(_ => ControlledGateway.Final(variant == "malformed"
            ? "Here is the Contract: criteria csv."
            : """
              {"criteria": [{"criterionId": "csv", "statement": "Export CSV."}],
               "requiredEffects": [{"effectId": "pull_request", "statement": "Open a PR to release.",
                                    "kind": "pull_request", "targetBranch": "release"}]}
              """));

        await Assert.That(async () => await store.AdmitRequestBundleAsync(submissionId, Credentials, gateway, default))
            .Throws<ContractProposalRefused>();

        using var reopened = fixture.State.Open();
        var submission = reopened.GetIssueSubmission(submissionId);
        await Assert.That(submission.State).IsEqualTo("rejected");
        await Assert.That(submission.ContractRevisionId).IsNull();
        await Assert.That(submission.ProposalRefusal!.Findings)
            .IsEquivalentTo([new ContractProposalFinding("invalid_contract_proposal", detail)]);
        await Assert.That(reopened.History(ContractIngressTests.Reference)).IsEmpty();
        // The refusal is final: a later call reports it without proposing again.
        await Assert.That(async () => await reopened.AdmitRequestBundleAsync(submissionId, Credentials, gateway, default))
            .Throws<ContractProposalRefused>();
        await Assert.That(gateway.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task UnsupportedRequirementsTheModelPreservesAreRefusedByAdmission()
    {
        using var fixture = Fixture(out var store, out var submissionId, out _);
        using var session = store;
        var gateway = new ControlledGateway(_ => ControlledGateway.Final("""
            {"criteria": [{"criterionId": "csv", "statement": "Export CSV."}],
             "obligations": [{"obligationId": "merge", "statement": "Merge the PR once approved.", "kind": "merge"}],
             "prerequisites": [{"prerequisiteId": "approval", "statement": "Wait for maintainer approval.",
                                "satisfiedWithinProfile": false}]}
            """));

        var status = await store.AdmitRequestBundleAsync(submissionId, Credentials, gateway, default);

        await Assert.That(status.Decision!.Findings.Select(finding => finding.Code))
            .IsEquivalentTo(["unsupported_external_obligation", "unsatisfied_prerequisite"]);
        await Assert.That(status.Revision.Contract.Obligations.Single().Kind).IsEqualTo("merge");
        await Assert.That(store.GetIssueSubmission(submissionId).State).IsEqualTo("rejected");
    }

    [Test]
    [Arguments("http-503", true)]
    [Arguments("transport", true)]
    [Arguments("missing-key", false)]
    public async Task GatewayFailureRetainsNothingAndALaterCallProposesAgain(string failure, bool retryable)
    {
        using var fixture = Fixture(out var store, out var submissionId, out _);
        using var session = store;
        var failing = new ControlledGateway(_ => failure == "transport"
            ? throw new HttpRequestException("connection refused")
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("overloaded") });

        var error = await Assert.That(async () => await store.AdmitRequestBundleAsync(submissionId,
                failure == "missing-key" ? new GatewayCredentials(NativeProfile.GatewayBaseUrl, null) : Credentials,
                failing, default))
            .Throws<ContractProposerError>();
        await Assert.That(error!.Retryable).IsEqualTo(retryable);
        await Assert.That(failing.Requests.Count).IsEqualTo(failure == "missing-key" ? 0 : 1);
        var pending = store.GetIssueSubmission(submissionId);
        await Assert.That(pending.State).IsEqualTo("capturing");
        await Assert.That(pending.ProposalRefusal).IsNull();
        await Assert.That(pending.ContractRevisionId).IsNull();

        using var reopened = fixture.State.Open();
        var status = await reopened.AdmitRequestBundleAsync(submissionId, Credentials,
            new ControlledGateway(_ => ControlledGateway.Final("""{"criteria": [{"criterionId": "csv", "statement": "Export CSV."}]}""")),
            default);
        await Assert.That(status.Decision!.Admitted).IsTrue();
    }

    [Test]
    public async Task GatewayKeyIsAbsentFromRetainedState()
    {
        using var fixture = new RepositoryPreparationTests.RepositoryPreparationFixture();
        fixture.SetIssue(12, RequestAdmissionTests.Request());
        fixture.SetIssue(13, RequestAdmissionTests.Request());
        using (var store = fixture.State.Initialize())
        {
            var admitted = store.SubmitIssue("https://github.com/acme/widget/issues/12").SubmissionId;
            await RequestAdmissionTests.Capture(store, fixture, admitted);
            await store.AdmitRequestBundleAsync(admitted, Credentials, new ControlledGateway(_ =>
                ControlledGateway.Final("""{"criteria": [{"criterionId": "csv", "statement": "Export CSV."}]}""")), default);
            var refused = store.SubmitIssue("https://github.com/acme/widget/issues/13").SubmissionId;
            await RequestAdmissionTests.Capture(store, fixture, refused);
            await Assert.That(async () => await store.AdmitRequestBundleAsync(refused, Credentials,
                new ControlledGateway(_ => ControlledGateway.Final("[]")), default)).Throws<ContractProposalRefused>();

            // Inspect while the session is open so an uncheckpointed WAL is included.
            var state = Path.GetDirectoryName(fixture.State.Path)!;
            var key = Encoding.UTF8.GetBytes(ApiKey);
            foreach (var file in Directory.GetFiles(state))
            {
                byte[] bytes;
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    bytes = copy.ToArray();
                }
                await Assert.That(bytes.AsSpan().IndexOf(key)).IsEqualTo(-1);
            }
        }
    }

    private static RepositoryPreparationTests.RepositoryPreparationFixture Fixture(out BroodlingStore store,
        out string submissionId, out RequestBundle bundle)
    {
        var fixture = new RepositoryPreparationTests.RepositoryPreparationFixture();
        fixture.SetIssue(12, RequestAdmissionTests.Request("- schema: repo:docs/schema.md",
            "- design: https://github.com/acme/widget/issues/7"));
        fixture.SetIssue(7, DesignBody);
        fixture.CommitFile("docs/schema.md", SchemaFile);
        store = fixture.State.Initialize();
        submissionId = store.SubmitIssue("https://github.com/acme/widget/issues/12").SubmissionId;
        bundle = RequestAdmissionTests.Capture(store, fixture, submissionId).GetAwaiter().GetResult();
        return fixture;
    }

    /// <summary>A scripted OpenAI-compatible Chat Completions peer; it makes no network call.</summary>
    private sealed class ControlledGateway(params Func<JsonObject, HttpResponseMessage>[] turns) : HttpMessageHandler
    {
        internal List<JsonObject> Requests { get; } = [];
        internal List<Uri?> Uris { get; } = [];
        internal List<string?> Authorizations { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject());
            Uris.Add(request.RequestUri);
            Authorizations.Add(request.Headers.Authorization?.ToString());
            return turns[Requests.Count - 1](Requests[^1]);
        }

        internal static HttpResponseMessage Final(string content) =>
            Reply(new JsonObject { ["role"] = "assistant", ["content"] = content });

        internal static HttpResponseMessage ToolCall(string id, JsonObject arguments) => Reply(new JsonObject
        {
            ["role"] = "assistant", ["content"] = null,
            ["tool_calls"] = new JsonArray(new JsonObject
            {
                ["id"] = id, ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = "read_reference", ["arguments"] = arguments.ToJsonString() }
            })
        });

        private static HttpResponseMessage Reply(JsonObject message) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(new JsonObject
            {
                ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["message"] = message })
            }.ToJsonString(), Encoding.UTF8, "application/json")
        };
    }
}
