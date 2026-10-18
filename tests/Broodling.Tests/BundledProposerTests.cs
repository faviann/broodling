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
    private static readonly GatewayCredentials Credentials = new(DirectTargetBinding.GatewayBaseUrl, ApiKey);

    [Test]
    public async Task ProposerSendsOnlyTheRequestAndAuthorityInOneCallAndIsAdmittedBundleBound()
    {
        using var fixture = Fixture(out var store, out var submissionId, out var bundle);
        using var session = store;
        var gateway = new ControlledGateway(
            _ => ControlledGateway.Final("""{"criteria": [{"criterionId": "csv", "statement": "Export CSV."}]}"""));

        var status = await store.AdmitRequestBundleAsync(submissionId, Credentials, gateway, default);

        var sent = gateway.Requests.Single();
        await Assert.That(gateway.Uris[0]).IsEqualTo(new Uri("https://gateway.ai.faviann.com/v1/chat/completions"));
        await Assert.That(gateway.Authorizations[0]).IsEqualTo("Bearer " + ApiKey);
        await Assert.That((string)sent["model"]!).IsEqualTo("claude-opus-5-5");
        await Assert.That((int)sent["max_tokens"]!).IsEqualTo(12000);
        await Assert.That((string)sent["reasoning_effort"]!).IsEqualTo("medium");
        await Assert.That(sent.ContainsKey("tools")).IsFalse();
        var context = JsonNode.Parse((string)sent["messages"]![1]!["content"]!)!.AsObject();
        await Assert.That(string.Join(",", context.Select(field => field.Key))).IsEqualTo("executableRequest,authority");
        await Assert.That((string)context["executableRequest"]!).IsEqualTo(RequestAdmissionTests.Request());

        var request = bundle.References.Single(reference => reference.ReferenceId == "request");
        using var reopened = fixture.State.Open();
        var admitted = reopened.Status(status.Revision.ContractRevisionId);
        await Assert.That(admitted.Decision!.Admitted).IsTrue();
        await Assert.That(admitted.Revision.Contract.Criteria.Single().Statement).IsEqualTo("Export CSV.");
        await Assert.That(admitted.Revision.ConstructedBy).IsEqualTo("model_extraction");
        await Assert.That(admitted.Revision.Contract.RequestBundle)
            .IsEqualTo(new ContractRequestBundle(bundle.BundleId, bundle.ManifestSha256!));
        await Assert.That(admitted.Revision.Contract.SourceAttribution)
            .IsEquivalentTo([new SourceAttribution(request.SourceId!, request.ContentSha256!)]);
        await Assert.That(reopened.GetIssueSubmission(submissionId).State).IsEqualTo("admitted");
    }

    [Test]
    [Arguments("malformed", "The model's proposal is not well-formed JSON without repeated properties.")]
    [Arguments("duplicate", "The model's proposal is not well-formed JSON without repeated properties.")]
    [Arguments("authority", "The proposal changed the caller's exact effect authority.")]
    public async Task MalformedOrAuthorityChangingProposalIsRetainedAsTheSubmissionsRefusal(string variant, string detail)
    {
        using var fixture = Fixture(out var store, out var submissionId, out _);
        using var session = store;
        var gateway = new ControlledGateway(_ => ControlledGateway.Final(variant switch
        {
            "malformed" => "Here is the Contract: criteria csv.",
            "duplicate" => """{"criteria": [], "criteria": [{"criterionId": "csv", "statement": "Export CSV."}]}""",
            _ => """
                {"criteria": [{"criterionId": "csv", "statement": "Export CSV."}],
                 "requiredEffects": [{"effectId": "pull_request", "statement": "Open a PR to release.",
                                      "kind": "pull_request", "targetBranch": "release"}]}
                """
        }));

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
    [Arguments("version-path", false)]
    public async Task GatewayFailureRetainsNothingAndALaterCallProposesAgain(string failure, bool retryable)
    {
        using var fixture = Fixture(out var store, out var submissionId, out _);
        using var session = store;
        var failing = new ControlledGateway(_ => failure == "transport"
            ? throw new HttpRequestException("connection refused")
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("overloaded") });

        var error = await Assert.That(async () => await store.AdmitRequestBundleAsync(submissionId,
                failure switch
                {
                    "missing-key" => new GatewayCredentials(DirectTargetBinding.GatewayBaseUrl, null),
                    "version-path" => new GatewayCredentials("https://gateway.ai.faviann.com/v1", ApiKey),
                    _ => Credentials
                },
                failing, default))
            .Throws<ContractProposerError>();
        await Assert.That(error!.Retryable).IsEqualTo(retryable);
        await Assert.That(failing.Requests.Count).IsEqualTo(retryable ? 1 : 0);
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
    public async Task AToolCallReplyIsUnusableAndRetainsNothing()
    {
        using var fixture = Fixture(out var store, out var submissionId, out _);
        using var session = store;
        var gateway = new ControlledGateway(_ => ControlledGateway.Reply(new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = """{"criteria": [{"criterionId": "csv", "statement": "Export CSV."}]}""",
            ["tool_calls"] = new JsonArray(new JsonObject
            {
                ["id"] = "call_1", ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = "read_reference", ["arguments"] = "{}" }
            })
        }));

        var error = await Assert.That(async () => await store.AdmitRequestBundleAsync(submissionId, Credentials, gateway, default))
            .Throws<ContractProposerError>();

        await Assert.That(error!.Message).IsEqualTo("The model gateway returned an unusable response.");
        await Assert.That(error.Retryable).IsTrue();
        var pending = store.GetIssueSubmission(submissionId);
        await Assert.That(pending.State).IsEqualTo("capturing");
        await Assert.That(pending.ProposalRefusal).IsNull();
        await Assert.That(pending.ContractRevisionId).IsNull();
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
        fixture.SetIssue(12, RequestAdmissionTests.Request());
        store = fixture.State.Initialize();
        submissionId = store.SubmitIssue("https://github.com/acme/widget/issues/12").SubmissionId;
        bundle = RequestAdmissionTests.Capture(store, fixture, submissionId).GetAwaiter().GetResult();
        return fixture;
    }

    /// <summary>A scripted OpenAI-compatible Chat Completions peer; it makes no network call.</summary>
    internal sealed class ControlledGateway(params Func<JsonObject, HttpResponseMessage>[] turns) : HttpMessageHandler
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

        internal static HttpResponseMessage Reply(JsonObject message) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(new JsonObject
            {
                ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["message"] = message })
            }.ToJsonString(), Encoding.UTF8, "application/json")
        };
    }
}
