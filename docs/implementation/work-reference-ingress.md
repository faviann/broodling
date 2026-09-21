# Work-reference to Contract ingress

`ContractIngress` implements [#75](https://github.com/faviann/broodling/issues/75)
up to a stored admission decision. It creates no Attempt, workspace or execution.
The [current architecture](../governing/current.md) and
[#74 first-use decision](https://github.com/faviann/broodling/issues/74) govern this
path. The latter permits limited first-use implementation while retaining the
scoped P5 **FAIL**; it does not establish reliable semantic acceptance.

## Three responsibilities

1. `acquire_github_issue` reads one explicit `WorkReference` using the installed,
   authenticated `gh api` CLI. It validates the issue locator, repository locator,
   number and stable issue identity, rejects pull requests, and preserves the
   exact REST response bytes. `ContractIngress` resolves the Work Unit and records
   the primary issue snapshot under existing Broodling entitlement policy.
2. A caller-supplied callback receives `ContractProposalInput`: immutable Work
   Unit/source records, source pins, the caller's exact required effects, and the
   declared producer. It returns the existing typed `Contract`. This may be caller
   code, a structured Contract supplied by a caller, or a model adapter. There is
   no bundled model, provider selection or general Markdown parser.
3. Ingress checks the proposal's structure, producer, Work Unit, complete source
   attribution and exact effects, then uses `record_contract_revision` and `admit`.
   Existing Closability policy determines supported capabilities. Unsupported
   effects, missing PR targets and unsatisfied prerequisites stay in rejected
   Contracts. Ingress never repairs a proposal by deleting a requirement.

## Work-reference identity

`WorkReference.parse` canonicalizes the supported HTTPS, SSH, scp-like,
schemeless `host/owner/repository`, and bare `owner/repository` forms. Repository
host, owner and name are case-insensitive; an optional `.git` suffix or trailing
slash does not change identity. The issue may be an integer, decimal string,
`#n`, or canonical issue URL. These spellings resolve to the same Work Unit.
Other spellings that the parser happens to tolerate are not a compatibility
promise.

The required `required_effects` argument is trusted caller authority, including
when it is explicitly `()`. Ingress requires the proposal to match it exactly;
the immutable Contract then retains that authority in its `required_effects`
field. A model cannot add, remove or retarget an effect, even to another otherwise
supported PR branch. No synthetic source or separate grant record is created.
Repository authority comes from the Work Unit; B1 is still established by later
Attempt admission.

Every input snapshot must be attributed at its exact digest. The existing
Broodling execution policy makes the complete frozen sources govern scope and
acceptance alongside the Contract; that policy is not caller-authored source
material. Extracted criteria cannot waive source requirements. A different,
previously entitled snapshot cannot be substituted. Source acquisition and
semantic proposal remain separate; the model output has no source-entitlement
channel.

## Use

For an issue already reviewed to require only a candidate change, local checks
and the explicitly authorized PR, a caller can use its complete prose as one
criterion. A more detailed proposer can lift the existing Completion/Acceptance
sections verbatim and represent other obligations and prerequisites using the
existing Contract fields. It must inspect the complete request, including scope
restrictions; unsupported requirements must remain visible to admission.

Initialize a new store explicitly with `BroodlingStore.initialize(path)` before
using this sample. `BroodlingStore.open(path)` only opens an existing current
store; use `BroodlingStore.upgrade(path)` for a supported historical schema.

```python
import json

from broodling import (
    BroodlingStore,
    Contract,
    ContractIngress,
    Criterion,
    RequiredEffect,
    WorkReference,
)


def propose_reviewed_local_change(inputs):
    primary = next(source for source in inputs.sources if source.kind == "primary_issue")
    issue = json.loads(primary.content)
    return Contract(
        work_unit_id=inputs.work_unit.work_unit_id,
        source_attribution=inputs.source_attribution,
        criteria=(Criterion("request", issue["body"]),),
        required_effects=inputs.required_effects,
        constructed_by=inputs.constructed_by,
    )


with BroodlingStore.open("/srv/broodling/state/broodling.sqlite3") as store:
    result = ContractIngress(store).from_github(
        WorkReference.parse("acme/widget", 123),
        propose_reviewed_local_change,
        constructed_by="caller",
        required_effects=(
            RequiredEffect("pr", "Deliver a PR targeting main.", "pull_request", "main"),
        ),
    )
    print(result.revision.contract_revision_id, result.decision.outcome)
    for finding in result.decision.findings:
        print(finding.code, finding.preserved_obligation)
```

`from_sources(reference, sources, propose, required_effects=...)` accepts exact
`SourceSubmission` bytes directly, with no JSON/Markdown dependency in Contract
construction. The current Work Unit profile still requires one primary issue.
All caller-supplied sources, including the primary issue in `from_sources`, need
`origin="caller"` and an explicit `SourceEntitlement("caller", basis)`. Supplied
bytes cannot claim Broodling-policy acquisition or entitlement. Only the primary
issue actually acquired and validated by `from_github` receives implicit policy
entitlement. Additional sources in that method also need explicit caller grants.
Links, issue comments and repository guidance are never fetched or entitled
implicitly. This allows callers to freeze a relevant decision or prerequisite
record without granting authority to everything the issue links to. Caller grants
authorize supplied bytes; they do not verify those bytes against an upstream site.

An external model adapter must decode its output to the existing dataclasses;
ingress validates their runtime types as well as nonempty, unique identifiers and
nonempty statements. It cannot claim a different `constructed_by` value than the
caller selected (the default is `model_extraction`). A caller supplying a typed
Contract can return it from the callback after binding these source pins.

## Reproduction and limits

The result exposes the Work Unit, all entitled snapshots, immutable revision and
stored decision. Repeating identical source bytes and proposal meaning resolves
the same source/revision/decision identities, including after reopening the store.
Source attribution is unordered: after validating the complete pin set, ingress
sorts pins by source ID and digest before recording a new Contract. Reordering
input sources or proposal pins therefore does not create a different revision.
Historical Contract v1 serialization and stored revision bytes remain unchanged;
ingress normalization does not rewrite earlier revisions.
Changed issue bytes or proposal meaning create new records; they cannot amend an
earlier admitted revision or its Attempt. Replaying a recorded decision needs no
GitHub fetch or extraction. Re-running a model is not promised to reproduce its
earlier proposal. Even a metadata-only change to the captured REST response is a
new source snapshot.

Acquisition, entitlement and malformed/authority-changing proposal failures
raise before Contract admission. Already captured sources remain durable facts.
A valid structured proposal lacking supported capabilities receives the existing
durable rejected decision. An interruption after revision storage but before its
decision leaves a non-admitted revision that `store.admit(revision_id)` can decide.

Deterministic admission validates the declared structure and capabilities; it
cannot establish that natural-language extraction is complete or correct. The
caller/proposer must expose unsupported obligations and unresolved prerequisites,
and conflicting source requirements require handback. Keeping complete sources
prevents a summary from replacing authority, but does not prove model obedience.
Under #74, any subsequently delivered PR still needs independent human/operator
review of its exact revision against the frozen request and Contract, with
appropriate tests/CI, before a separate merge decision. `SUCCEEDED` is not semantic
certification. No-effect admission retains the existing unsupported stable-result
limitation. Current invocation and deployment behavior are documented by their
respective application and operations seams.

The offline ingress tests use retained current Broodling issues
[#75 and #82](../../tests/fixtures/ingress/README.md), with explicit fixture
proposals, to demonstrate reproducible admitted/rejected results and the authority
boundaries. They make no provider-quality or live-delivery claim.
